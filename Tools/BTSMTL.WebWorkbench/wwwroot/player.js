const $ = id => document.getElementById(id);
const state = { builds: [], lives: [], runs: [], scenes: [], selected: null, socket: null, live: null,
  alignment: null, frame: -1, video: null, dotnet: new Map(), dotnetRun: null, keys: new Set(),
  mouse: { dx: 0, dy: 0, buttons: 0, scroll: 0 }, captured: false, pendingCamera: null, acks: [] };
const playerStates = { idle: '空闲，等待载入', loading: '正在载入离线包', paused: '已暂停', playing: '正在播放', stepping: '单步中', seeking: '正在跳帧', failed: '载入或运行失败' };
const alignmentNames = { matched: '一致', mismatch: '不一致', pending: '等待对照', 'no-dotnet-record': '.NET 记录未封存', 'live-input': '实时试玩（无对照）', 'not-loaded': '未载入' };
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

async function request(path, body) {
  const response = await fetch(path, body === undefined ? undefined : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${response.status} · ${text}`);
  return JSON.parse(text);
}

function report(text, error = false) {
  $('status').textContent = text;
  $('status').classList.toggle('error', error);
}

async function guarded(action) {
  try { await action(); }
  catch (error) { report(error.message, true); }
}

function options(element, entries, label, value) {
  const selected = element.value;
  element.replaceChildren(...entries.map(entry => {
    const option = document.createElement('option');
    option.value = value(entry); option.textContent = label(entry);
    option.selected = option.value === selected;
    return option;
  }));
}

async function refresh() {
  const [builds, lives, runs, sources] = await Promise.all([request('/api/preview/players'),
    request('/api/preview/live'), request('/api/preview/runs'), request('/api/preview/sources')]);
  state.builds = builds.items; state.lives = lives; state.scenes = sources.scenes;
  state.runs = runs.filter(run => ['completed', 'stopped'].includes(run.stage));
  options($('build'), state.builds, build => `${build.builtAt} · ${build.scene?.path ?? ''}`, build => build.buildId);
  const build = state.builds.find(item => item.buildId === $('build').value);
  $('build-info').textContent = build ? `${build.buildId} · ${build.buildMode}/${build.scriptingBackend} · 资源 ${build.resourceCount}` : '尚无构建，请先在 Editor 构建预览播放器。';
  $('start-live').disabled = !build;
  options($('run'), state.runs, run => `${run.acceptedAt} · ${run.jobId.slice(0, 8)}`, run => run.jobId);
  options($('scene'), state.scenes, scene => scene.path, scene => scene.path);
  $('scene-version').textContent = state.scenes.find(scene => scene.path === $('scene').value)?.revision ?? '';
  $('lives').innerHTML = lives.map(live => `<button data-live="${escapeHtml(live.liveId)}" class="${live.liveId === state.selected ? 'selected' : ''}">${escapeHtml(live.state)}<small>${escapeHtml(live.startedAt)}</small><code>${escapeHtml(live.liveId)}</code></button>`).join('') || '<p class="muted">尚未启动播放器。</p>';
  $('connection').textContent = '项目宿主已连接';
  updateControls();
}

async function startLive() {
  let pending = sessionStorage.getItem('btsmtl.player.pending-start');
  if (!pending) {
    const stage = $('stage').getBoundingClientRect();
    const width = Math.max(640, Math.round(stage.width / 16) * 16);
    pending = JSON.stringify({ requestId: crypto.randomUUID(), playerBuildId: $('build').value,
      width, height: Math.round(width * 9 / 16) });
    sessionStorage.setItem('btsmtl.player.pending-start', pending);
  }
  const live = await request('/api/preview/live', JSON.parse(pending));
  sessionStorage.removeItem('btsmtl.player.pending-start');
  await refresh();
  select(live.liveId);
  report('播放器进程已启动，等待它连回主机。');
}

function select(id) {
  state.socket?.close();
  state.selected = id; state.live = null; state.alignment = null; state.frame = -1; state.acks = [];
  $('acks').textContent = ''; $('live-identity').textContent = id;
  history.replaceState(null, '', `?live=${encodeURIComponent(id)}`);
  const socket = new WebSocket(`${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/api/preview/live/${id}/view`);
  socket.binaryType = 'arraybuffer';
  socket.onmessage = event => guarded(() => typeof event.data === 'string' ? onText(JSON.parse(event.data)) : onVideo(event.data));
  socket.onclose = () => { if (state.socket === socket) { report('与播放器画面的连接已断开。', true); state.socket = null; updateControls(); } };
  state.socket = socket;
  refresh();
}

function send(type, fields = {}, needsId = true) {
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) throw new Error('尚未连接播放器画面。');
  const message = { type, ...fields };
  if (needsId) message.commandId = crypto.randomUUID();
  state.socket.send(JSON.stringify(message));
  return message.commandId;
}

async function onText(message) {
  switch (message.type) {
    case 'status': state.live = message.live; state.alignment = message.live.alignment; renderLive(); break;
    case 'frames': state.alignment = message.alignment; if (message.items.length) state.frame = message.items.at(-1).frame; renderAlignment(); break;
    case 'ack': case 'pick': onAck(message); break;
    case 'error': report(message.error, true); break;
  }
}

function onAck(message) {
  state.acks.unshift(message); state.acks.length = Math.min(state.acks.length, 30);
  $('acks').textContent = state.acks.map(ack => JSON.stringify(ack, null, 1)).join('\n');
  if (message.type === 'pick') {
    $('pick-result').innerHTML = message.actorId
      ? `<div class="actor-fact"><strong>${escapeHtml(message.actorId)}</strong> · 确认帧 ${message.frame}<code>根位置 ${message.position.map(value => value.toFixed(3)).join(' / ')}</code>${message.hitCollider ? `<code>射线命中 ${escapeHtml(message.hitCollider)}</code>` : ''}<button id="focus-picked">环绕此角色</button></div>`
      : `<p class="muted">点击处附近没有角色${message.hitCollider ? `；射线命中 ${escapeHtml(message.hitCollider)}` : ''}。</p>`;
    $('focus-picked')?.addEventListener('click', () => guarded(async () => {
      $('orbit').checked = true; send('camera', { mode: 'orbit', focusActorId: message.actorId }, false);
    }));
    return;
  }
  report(message.ok ? `命令已执行 · 确认帧 ${message.frame}` : `命令被拒绝：${message.error}`, !message.ok);
  if (message.ok && message.path) {
    const name = message.commandId.replaceAll('-', '') + '.png';
    report(`截图已保存：${message.path}`);
    window.open(`/api/preview/live/${state.selected}/captures/${name}`, '_blank');
  }
}

function renderLive() {
  const live = state.live, player = live.player;
  $('live-title').textContent = player ? playerStates[player.state] ?? player.state : `进程 ${live.state}`;
  $('stop-live').hidden = live.state === 'exited';
  $('live-state').textContent = player?.error || (live.state === 'exited' ? `播放器已退出（${live.exitCode}）。` :
    !player ? '等待播放器连回主机…' : player.state === 'idle' ? '已连接。选择运行或离线包载入。' : '');
  $('live-state').classList.toggle('error', !!player?.error);
  if (player) {
    state.frame = player.frame;
    const facts = { '构建': live.buildId, '场景修订': player.sceneRevision, '输入方式': player.inputMode === 'device' ? '实时试玩' : player.inputMode === 'schedule' ? '输入日程' : '',
      '确认帧': player.frame, '结束帧': player.lastSdkFrame || '不限', '日程零点': player.scheduleStartFrame,
      '表现就绪': player.presentationPrepared ? '是' : '否', '相机': player.cameraMode === 'orbit' ? `环绕 ${player.focusActorId}` : '游戏相机',
      '角色': (player.actors || []).join('、'), '缺少资源': player.missingResource, '失败类型': player.failureKind };
    $('live-facts').innerHTML = Object.entries(facts).filter(([, value]) => value !== '' && value !== undefined && value !== -1)
      .map(([name, value]) => `<dt>${escapeHtml(name)}</dt><dd>${escapeHtml(value)}</dd>`).join('');
    if (player.lastSdkFrame > 0) $('seek').max = player.lastSdkFrame;
    else $('seek').max = Math.max(Number($('seek').max), player.frame);
    if (document.activeElement !== $('seek')) $('seek').value = Math.max(0, player.frame);
    $('orbit').checked = player.cameraMode === 'orbit';
  }
  renderAlignment(); updateControls();
}

function renderAlignment() {
  const alignment = state.alignment;
  $('frame-label').textContent = state.frame >= 0 ? `SDK ${state.frame}` : '无确认帧';
  if (!alignment) { $('alignment').replaceChildren(); return; }
  const facts = { '结论': alignmentNames[alignment.state] ?? alignment.state, '已对照帧': alignment.comparedFrames,
    '首个不一致': alignment.firstMismatchFrame, '播放器最新帧': alignment.lastPlayerFrame, '需要的更新': alignment.changeKind, '说明': alignment.reason };
  $('alignment').innerHTML = Object.entries(facts).filter(([, value]) => value !== null && value !== undefined)
    .map(([name, value]) => `<dt>${escapeHtml(name)}</dt><dd class="${name === '结论' ? `badge-${alignment.state}` : ''}">${escapeHtml(value)}</dd>`).join('');
}

function updateControls() {
  const connected = !!state.socket && state.socket.readyState === WebSocket.OPEN && state.live?.player;
  const player = state.live?.player;
  const ready = connected && ['paused', 'playing', 'stepping', 'seeking'].includes(player.state);
  document.querySelectorAll('[data-command]').forEach(button => {
    button.disabled = !ready || (button.dataset.command === 'play' && player.state === 'playing') ||
      (button.dataset.command === 'pause' && player.state === 'paused') || (button.dataset.command === 'step' && player.state !== 'paused');
  });
  $('seek').disabled = !ready; $('orbit').disabled = !ready; $('reset-camera').disabled = !ready || !$('orbit').checked;
  $('capture').disabled = !ready;
  $('overlay-dotnet').disabled = !ready || !state.live?.runId;
  $('capture-input').disabled = !ready || player.inputMode !== 'device';
  if ($('capture-input').disabled) $('capture-input').checked = false;
  $('capture-hint').hidden = !$('capture-input').checked;
  $('load-run').disabled = !connected || !$('run').value || player?.state === 'loading';
  $('load-scene').disabled = !connected || !$('scene').value || player?.state === 'loading';
}

// 画面帧：84 字节帧头（'Q3PV'、确认帧、宽、高、标志、viewProjection 列主序）后接 JPEG。
async function onVideo(buffer) {
  const header = new DataView(buffer, 0, 84);
  const frame = header.getInt32(4, true), width = header.getInt32(8, true), height = header.getInt32(12, true);
  const flipY = (header.getInt32(16, true) & 1) !== 0;
  const matrix = Array.from({ length: 16 }, (_, index) => header.getFloat32(20 + index * 4, true));
  const image = await createImageBitmap(new Blob([new Uint8Array(buffer, 84)], { type: 'image/jpeg' }));
  const canvas = $('video'), overlay = $('overlay');
  if (canvas.width !== width || canvas.height !== height) { canvas.width = overlay.width = width; canvas.height = overlay.height = height; }
  const context = canvas.getContext('2d');
  context.save();
  if (flipY) { context.translate(0, height); context.scale(1, -1); }
  context.drawImage(image, 0, 0, width, height);
  context.restore(); image.close();
  state.video = { frame, width, height, matrix };
  await drawOverlay();
}

function fp(value) { const raw = BigInt(value); return Number(raw / 65536n) + Number(raw % 65536n) / 65536; }

async function drawOverlay() {
  const overlay = $('overlay'), context = overlay.getContext('2d');
  context.clearRect(0, 0, overlay.width, overlay.height);
  const video = state.video;
  if (!$('overlay-dotnet').checked || !video || !state.live?.runId) return;
  await loadDotnetFrames(state.live.runId);
  const record = state.dotnet.get(video.frame);
  if (!record) return;
  const m = video.matrix;
  context.font = '12px Consolas, monospace'; context.lineWidth = 2;
  for (const actor of record.actors) {
    const [x, y, z] = actor.positionRaw.map(fp);
    const cx = m[0] * x + m[4] * y + m[8] * z + m[12], cy = m[1] * x + m[5] * y + m[9] * z + m[13];
    const cw = m[3] * x + m[7] * y + m[11] * z + m[15];
    if (cw <= 0) continue;
    const sx = (cx / cw + 1) / 2 * video.width, sy = (1 - cy / cw) / 2 * video.height;
    context.strokeStyle = context.fillStyle = '#89dec9';
    context.beginPath(); context.arc(sx, sy, 8, 0, Math.PI * 2); context.stroke();
    context.fillText(`.NET Roster ${actor.rosterIndex}`, sx + 11, sy - 6);
  }
}

async function loadDotnetFrames(runId) {
  if (state.dotnetRun === runId) return;
  state.dotnetRun = runId; state.dotnet.clear();
  for (let offset = 0; offset !== null;) {
    const page = await request(`/api/preview/runs/${runId}/frames?offset=${offset}&limit=512`);
    page.items.forEach(frame => state.dotnet.set(frame.frame, frame));
    offset = page.nextOffset;
  }
}

// 画布坐标：object-fit: contain 留边时换算到画面 0–1。
function imagePoint(event) {
  const rect = $('video').getBoundingClientRect(), width = $('video').width || 16, height = $('video').height || 9;
  const scale = Math.min(rect.width / width, rect.height / height);
  const left = rect.left + (rect.width - width * scale) / 2, top = rect.top + (rect.height - height * scale) / 2;
  return { x: (event.clientX - left) / (width * scale), y: (event.clientY - top) / (height * scale) };
}

// 浏览器 KeyboardEvent.code 转 Input System Key 名。
function keyName(code) {
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (/^Digit[0-9]$/.test(code) || /^F([1-9]|1[0-2])$/.test(code)) return code;
  return ({ Space: 'Space', Enter: 'Enter', Escape: 'Escape', Tab: 'Tab', Backspace: 'Backspace', ShiftLeft: 'LeftShift', ShiftRight: 'RightShift',
    ControlLeft: 'LeftCtrl', ControlRight: 'RightCtrl', AltLeft: 'LeftAlt', AltRight: 'RightAlt', ArrowUp: 'UpArrow', ArrowDown: 'DownArrow',
    ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow', CapsLock: 'CapsLock', Backquote: 'Backquote', Minus: 'Minus', Equal: 'Equals',
    BracketLeft: 'LeftBracket', BracketRight: 'RightBracket', Semicolon: 'Semicolon', Quote: 'Quote', Comma: 'Comma', Period: 'Period',
    Slash: 'Slash', Backslash: 'Backslash' })[code];
}

function inputMessage() {
  const pad = [...(navigator.getGamepads?.() ?? [])].find(item => item?.connected);
  const message = { keys: [...state.keys].map(keyName).filter(Boolean), mouseDx: state.mouse.dx, mouseDy: state.mouse.dy,
    mouseButtons: state.mouse.buttons, scroll: state.mouse.scroll, x: 0.5, y: 0.5,
    lx: pad?.axes[0] ?? 0, ly: pad?.axes[1] ?? 0, rx: pad?.axes[2] ?? 0, ry: pad?.axes[3] ?? 0,
    lt: pad?.buttons[6]?.value ?? 0, rt: pad?.buttons[7]?.value ?? 0,
    padButtons: (pad?.buttons ?? []).slice(0, 16).reduce((bits, button, index) => button.pressed ? bits | (1 << index) : bits, 0) };
  state.mouse.dx = state.mouse.dy = state.mouse.scroll = 0;
  return message;
}

function tick() {
  requestAnimationFrame(tick);
  if (!state.socket || state.socket.readyState !== WebSocket.OPEN) return;
  try {
    if (state.captured || ($('capture-input').checked && navigator.getGamepads?.().some(item => item?.connected)))
      send('input', inputMessage(), false);
    if (state.pendingCamera) { send('camera', state.pendingCamera, false); state.pendingCamera = null; }
  } catch (error) { report(error.message, true); }
}

function releaseInput() {
  state.keys.clear(); state.mouse = { dx: 0, dy: 0, buttons: 0, scroll: 0 };
  if (state.socket?.readyState === WebSocket.OPEN && state.live?.player?.inputMode === 'device') send('input', inputMessage(), false);
}

function queueCamera(delta) {
  const pending = state.pendingCamera ?? { mode: 'orbit', yaw: 0, pitch: 0, zoom: 0, panX: 0, panY: 0 };
  for (const [name, value] of Object.entries(delta)) pending[name] = (pending[name] ?? 0) + value;
  state.pendingCamera = pending;
}

const stage = $('stage');
let drag = null;
stage.addEventListener('contextmenu', event => event.preventDefault());
stage.addEventListener('mousedown', event => {
  if (state.captured) { state.mouse.buttons |= 1 << [0, 2, 1][event.button]; return; }
  if ($('capture-input').checked) { stage.requestPointerLock(); return; }
  drag = { x: event.clientX, y: event.clientY, button: event.button, moved: false };
});
window.addEventListener('mousemove', event => {
  if (state.captured) { state.mouse.dx += event.movementX; state.mouse.dy += event.movementY; return; }
  if (!drag) return;
  const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
  if (Math.abs(dx) + Math.abs(dy) > 3) drag.moved = true;
  drag.x = event.clientX; drag.y = event.clientY;
  if (!drag.moved || !$('orbit').checked) return;
  if (drag.button === 0 && !event.shiftKey) queueCamera({ yaw: dx * 0.3, pitch: dy * 0.3 });
  else queueCamera({ panX: -dx * 0.002, panY: dy * 0.002 });
});
window.addEventListener('mouseup', event => {
  if (state.captured) { state.mouse.buttons &= ~(1 << [0, 2, 1][event.button]); return; }
  if (drag && !drag.moved && drag.button === 0 && state.live?.player)
    guarded(async () => { const point = imagePoint(event); if (point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1) send('pick', point); });
  drag = null;
});
stage.addEventListener('wheel', event => {
  event.preventDefault();
  if (state.captured) state.mouse.scroll += Math.sign(event.deltaY) * 120;
  else if ($('orbit').checked) queueCamera({ zoom: Math.sign(event.deltaY) * 0.1 });
}, { passive: false });
document.addEventListener('pointerlockchange', () => {
  state.captured = document.pointerLockElement === stage;
  stage.classList.toggle('captured', state.captured);
  if (!state.captured) releaseInput();
});
window.addEventListener('keydown', event => { if (!state.captured) return; event.preventDefault(); state.keys.add(event.code); });
window.addEventListener('keyup', event => { if (!state.captured) return; event.preventDefault(); state.keys.delete(event.code); });
window.addEventListener('blur', () => { if (state.captured) document.exitPointerLock(); });

$('build').onchange = () => guarded(refresh);
$('scene').onchange = () => { $('scene-version').textContent = state.scenes.find(scene => scene.path === $('scene').value)?.revision ?? ''; updateControls(); };
$('run').onchange = updateControls;
$('start-live').onclick = () => guarded(startLive);
$('lives').onclick = event => { const button = event.target.closest('[data-live]'); if (button) select(button.dataset.live); };
$('stop-live').onclick = () => guarded(async () => { await request(`/api/preview/live/${state.selected}/stop`, {}); await refresh(); });
$('load-run').onclick = () => guarded(async () => { send('load', { runId: $('run').value }); state.dotnetRun = null; report('正在按运行载入同一离线包与输入日程…'); });
$('load-scene').onclick = () => guarded(async () => {
  const scene = state.scenes.find(item => item.path === $('scene').value);
  send('load', { scene: scene.path, sceneRevision: scene.revision, inputMode: 'device' }); report('正在载入离线包，完成后可接管输入试玩…');
});
$('transport').onclick = event => { const button = event.target.closest('[data-command]'); if (button) guarded(async () => send(button.dataset.command, button.dataset.command === 'step' ? { count: 1 } : {})); };
$('seek').onchange = () => guarded(async () => { send('seek', { frame: Number($('seek').value) }); report(`跳到 SDK ${$('seek').value}…`); });
$('orbit').onchange = () => guarded(async () => { send('camera', { mode: $('orbit').checked ? 'orbit' : 'game' }, false); updateControls(); });
$('reset-camera').onclick = () => guarded(async () => send('camera', { mode: 'orbit', reset: true }, false));
$('overlay-dotnet').onchange = () => guarded(drawOverlay);
$('capture-input').onchange = () => { if (!$('capture-input').checked) { document.exitPointerLock(); releaseInput(); } updateControls(); };
$('capture').onclick = () => guarded(async () => send('capture'));

requestAnimationFrame(tick);
setInterval(() => guarded(refresh), 3000);
guarded(async () => {
  await refresh();
  if (sessionStorage.getItem('btsmtl.player.pending-start')) { report('上次启动结果尚未确认，正在以原请求身份重查。'); await startLive(); return; }
  const live = new URLSearchParams(location.search).get('live');
  if (live && state.lives.some(item => item.liveId === live)) select(live);
  else report('选择播放器构建并启动，或选择已启动的播放器。');
});
