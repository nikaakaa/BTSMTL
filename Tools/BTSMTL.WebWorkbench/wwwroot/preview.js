import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

const $ = id => document.getElementById(id);
const state = { sources: null, runs: [], selected: null, summary: null, frames: new Map(), count: 0, sealed: false };
let pollTimer;
const stages = { accepted: '已接收', running: '独立会话运行中', completed: '完整重放已封存', stopped: '会话已停止并封存', failed: '执行失败', cancelled: '已取消' };
const playbackNames = { paused: '已暂停', playing: '正在播放', stepping: '等待下一个确认帧', stopped: '已停止', completed: '已完成' };
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

function options(element, entries, label, value, selected) {
  element.replaceChildren(...entries.map(entry => {
    const option = document.createElement('option');
    option.value = value(entry); option.textContent = label(entry);
    option.selected = option.value === selected;
    return option;
  }));
}

function sourceVersion() {
  const scene = state.sources.scenes.find(item => item.path === $('scene').value);
  const replay = state.sources.replays.find(item => item.path === $('replay').value);
  $('scene-version').textContent = scene?.revision || '尚未导出正式离线包';
  $('replay-version').textContent = replay?.revision || '尚无原 SDK 录像';
  $('start').disabled = !scene || !replay;
}

async function refresh() {
  const [sources, runs] = await Promise.all([request('/api/preview/sources'), request('/api/preview/runs')]);
  state.sources = sources; state.runs = runs;
  options($('scene'), sources.scenes, entry => entry.path, entry => entry.path, $('scene').value);
  options($('replay'), sources.replays, entry => entry.path, entry => entry.path, $('replay').value);
  sourceVersion();
  $('source-state').textContent = `${sources.scenes.length} 个离线包 · ${sources.replays.length} 份原录像。${!sources.scenes.length ? '请先从已保存 Unity 场景正式导出。' : ''}${!sources.replays.length ? '真实重放需要原 SDK 录像文件。' : ''}`;
  $('runs').innerHTML = runs.map(run => `<button data-run="${escapeHtml(run.jobId)}" class="${run.jobId === state.selected ? 'selected' : ''}">${escapeHtml(stages[run.stage])}<small>${escapeHtml(run.acceptedAt)}</small><code>${escapeHtml(run.jobId)}</code></button>`).join('') || '<p class="muted">尚未提交运行。</p>';
  options($('comparison'), runs.filter(run => run.stage === 'completed' && run.jobId !== state.selected),
    entry => `${entry.acceptedAt} · ${entry.jobId.slice(0, 8)}`, entry => entry.jobId, $('comparison').value);
  $('compare').disabled = !state.sealed || !$('comparison').value;
  $('connection').textContent = '项目宿主已连接';
}

async function start() {
  let pending = sessionStorage.getItem('btsmtl.preview.pending-request');
  if (!pending) {
    pending = JSON.stringify({ requestId: crypto.randomUUID(), scene: $('scene').value,
      sceneRevision: state.sources.scenes.find(item => item.path === $('scene').value).revision,
      replay: $('replay').value, replayRevision: state.sources.replays.find(item => item.path === $('replay').value).revision,
      interactive: $('interactive').checked });
    sessionStorage.setItem('btsmtl.preview.pending-request', pending);
  }
  const receipt = await request('/api/preview/runs', JSON.parse(pending));
  sessionStorage.removeItem('btsmtl.preview.pending-request');
  await refresh();
  await selectRun(receipt.jobId);
}

async function selectRun(id) {
  clearTimeout(pollTimer);
  state.selected = id; state.summary = null; state.frames.clear(); state.count = 0; state.sealed = false;
  history.replaceState(null, '', `?run=${encodeURIComponent(id)}`);
  $('playback-controls').hidden = true;
  clearActors(); $('run-details').replaceChildren(); $('frame-facts').replaceChildren();
  $('actors').replaceChildren(); $('frame-json').textContent = '';
  $('frame').disabled = true; $('previous-frame').disabled = true; $('next-frame').disabled = true;
  $('fit-view').disabled = true; $('compare').disabled = true; $('frame-label').textContent = '无确认帧';
  $('comparison-result').textContent = '';
  $('run-identity').textContent = id;
  await updateRun();
  await refresh();
}

async function updateRun() {
  clearTimeout(pollTimer);
  const id = state.selected;
  const receipt = await request(`/api/preview/runs/${id}`);
  if (id !== state.selected) return;
  $('run-title').textContent = stages[receipt.stage];
  $('cancel').hidden = !['accepted', 'running'].includes(receipt.stage);
  $('run-state').textContent = receipt.error || (receipt.stage === 'completed' ? '原 SDK 重放已完成。下方读取本次封存的确认帧。' : receipt.stage === 'stopped' ? '实际会话已停止，保留停止前已形成的确认帧。' : receipt.stage === 'cancelled' ? '进程已确认取消。此运行没有完整封存记录。' : '正在准备或执行实际独立会话。');
  $('run-state').classList.toggle('error', receipt.stage === 'failed');
  if (['accepted', 'running'].includes(receipt.stage)) {
    const playback = await request(`/api/preview/runs/${id}/playback`);
    if (id !== state.selected) return;
    if (playback.ready) {
      const snapshot = playback.snapshot;
      $('playback-controls').hidden = false;
      $('playback-state').textContent = playbackNames[snapshot.state];
      document.querySelectorAll('[data-control]').forEach(button => {
        button.disabled = !!sessionStorage.getItem('btsmtl.preview.pending-control') ||
          (button.dataset.control === 'Play' && snapshot.state === 'playing') ||
          (button.dataset.control === 'Pause' && snapshot.state === 'paused') ||
          (button.dataset.control === 'Step' && snapshot.state !== 'paused');
      });
      const observation = snapshot.observation;
      const summary = { ...observation, frameCount: observation.frames.length };
      state.frames.clear(); observation.frames.forEach((frame, index) => state.frames.set(index, frame));
      await displayObservation(summary, false);
      await sendPendingControl();
      if (!sessionStorage.getItem('btsmtl.preview.pending-control'))
        report(`实际会话${playbackNames[snapshot.state]} · 已记录 ${summary.frameCount} 个确认帧。`);
    }
    pollTimer = setTimeout(() => guarded(updateRun), 500);
    return;
  }
  $('playback-controls').hidden = true;
  await sendPendingControl();
  if (!['completed', 'stopped'].includes(receipt.stage) || state.sealed) return;
  const summary = await request(`/api/preview/runs/${id}/summary`);
  if (id !== state.selected) return;
  await displayObservation(summary, true);
  report(`${stages[receipt.stage]} · 保留 ${summary.frameCount} 个确认帧。`);
  await refresh();
}

async function displayObservation(summary, sealed) {
  const firstFrame = state.count === 0 && summary.frameCount > 0;
  const first = state.summary === null;
  state.summary = summary; state.count = summary.frameCount;
  state.sealed = sealed;
  const facts = { '运行身份': summary.runId, '场景修订': summary.sceneRevision,
    '原录像修订': summary.replaySourceHash, '来源场景': summary.scene.path,
    '场景定义': summary.definition.path, '内容身份': summary.sessionIdentity.gameplayContent,
    'TickRate': summary.sessionIdentity.tickRate, 'SDK 帧范围': `${summary.initialSdkFrame} — ${summary.lastSdkFrame}`,
    '已记录确认帧': summary.frameCount, '记录状态': sealed ? '已封存' : '实际会话观察中' };
  $('run-details').innerHTML = Object.entries(facts).map(([name, value]) => `<dt>${escapeHtml(name)}</dt><dd>${escapeHtml(value)}</dd>`).join('');
  $('channels').textContent = `本次实际观察：${summary.channels.join('、')}。视口只显示根标记；尚未提供动画、碰撞及命中画面。`;
  $('frame').max = Math.max(0, state.count - 1);
  if (first) $('frame').value = 0;
  if (!sealed && $('follow-live').checked) $('frame').value = Math.max(0, state.count - 1);
  $('frame').disabled = state.count === 0;
  $('fit-view').disabled = state.count === 0;
  if (state.count) { await showFrame(Number($('frame').value)); if (firstFrame) fitView(); }
}

async function sendPendingControl() {
  const pending = sessionStorage.getItem('btsmtl.preview.pending-control');
  if (!pending) return;
  const { runId, ...command } = JSON.parse(pending);
  if (runId !== state.selected) return;
  const result = await request(`/api/preview/runs/${runId}/controls`, command);
  if (result.stage === 'submitted') { report('命令已提交，等待真实会话确认。'); return; }
  sessionStorage.removeItem('btsmtl.preview.pending-control');
  report(result.stage === 'applied' ? `命令已执行，实际确认帧 ${result.verifiedFrame ?? '尚未形成'}。` : result.error,
    result.stage !== 'applied');
}

async function showFrame(index) {
  const id = state.selected;
  const page = Math.floor(index / 128) * 128;
  if (!state.frames.has(index)) {
    const result = await request(`/api/preview/runs/${id}/frames?offset=${page}&limit=128`);
    if (id !== state.selected) return;
    result.items.forEach((frame, offset) => state.frames.set(page + offset, frame));
  }
  if (index !== Number($('frame').value)) return;
  const frame = state.frames.get(index);
  $('previous-frame').disabled = index === 0; $('next-frame').disabled = index === state.count - 1;
  $('frame-label').textContent = `SDK ${frame.frame}`;
  $('frame-facts').innerHTML = `<dt>确认 SDK 帧</dt><dd>${escapeHtml(frame.frame)}</dd><dt>玩法 Tick</dt><dd>${escapeHtml(frame.gameplayTick)}</dd><dt>完整 checksum</dt><dd><code>${escapeHtml(frame.checksum)}</code></dd>`;
  $('actors').innerHTML = frame.actors.map(actor => `<div class="actor-fact"><strong>Roster ${actor.rosterIndex}</strong> · Entity ${actor.entityIndex}:${actor.entityVersion}<code>Position FP Raw · ${actor.positionRaw.map(escapeHtml).join(' / ')}</code><code>Rotation FP Raw · ${actor.rotationRaw.map(escapeHtml).join(' / ')}</code></div>`).join('');
  $('frame-json').textContent = JSON.stringify(frame, null, 2);
  drawActors(frame.actors);
}

const viewport = $('viewport');
const scene = new THREE.Scene(); scene.background = new THREE.Color('#10171d');
const camera = new THREE.PerspectiveCamera(42, 1, 0.01, 2000); camera.position.set(4, 3, 5);
const renderer = new THREE.WebGLRenderer({ antialias: true }); renderer.setPixelRatio(window.devicePixelRatio);
viewport.appendChild(renderer.domElement);
const controls = new OrbitControls(camera, renderer.domElement); controls.target.set(0, 0.6, 0);
scene.add(new THREE.GridHelper(40, 40, '#39505b', '#24323a'));
scene.add(new THREE.HemisphereLight('#deefff', '#26353e', 2));
const actorRoots = new Map();
function clearActors() {
  actorRoots.forEach(root => { scene.remove(root); root.traverse(item => { item.geometry?.dispose(); item.material?.dispose(); }); });
  actorRoots.clear();
}
function fp(value) { const raw = BigInt(value); return Number(raw / 65536n) + Number(raw % 65536n) / 65536; }
function drawActors(actors) {
  clearActors();
  actors.forEach(actor => {
    const root = new THREE.Group();
    const marker = new THREE.Mesh(new THREE.ConeGeometry(0.18, 0.5, 12), new THREE.MeshStandardMaterial({ color: new THREE.Color().setHSL((actor.rosterIndex * 0.23) % 1, 0.5, 0.65) }));
    marker.rotation.x = Math.PI / 2; marker.position.y = 0.3; root.add(marker);
    root.add(new THREE.AxesHelper(0.7));
    const [x, y, z] = actor.positionRaw.map(fp), [qx, qy, qz, qw] = actor.rotationRaw.map(fp);
    root.position.set(x, y, -z); root.quaternion.set(-qx, -qy, qz, qw);
    scene.add(root); actorRoots.set(actor.rosterIndex, root);
  });
}
function fitView() {
  if (!actorRoots.size) return;
  const bounds = new THREE.Box3(); actorRoots.forEach(root => bounds.expandByObject(root));
  const center = bounds.getCenter(new THREE.Vector3());
  const size = Math.max(2, bounds.getSize(new THREE.Vector3()).length());
  controls.target.copy(center); camera.position.copy(center).add(new THREE.Vector3(size, size * 0.7, size)); controls.update();
}
new ResizeObserver(() => { renderer.setSize(viewport.clientWidth, viewport.clientHeight); camera.aspect = viewport.clientWidth / viewport.clientHeight; camera.updateProjectionMatrix(); }).observe(viewport);
renderer.setAnimationLoop(() => { controls.update(); renderer.render(scene, camera); });

$('scene').onchange = sourceVersion; $('replay').onchange = sourceVersion;
$('start').onclick = () => guarded(start); $('refresh').onclick = () => guarded(refresh);
$('runs').onclick = event => { const button = event.target.closest('[data-run]'); if (button) guarded(() => selectRun(button.dataset.run)); };
$('cancel').onclick = () => guarded(async () => { const result = await request(`/api/preview/runs/${state.selected}/cancel`, {}); report(result.cancellationRequested ? '取消请求已送达，等待进程确认。' : '本宿主没有仍在执行的对应进程，请查询当前回执。'); await updateRun(); });
$('playback-controls').onclick = event => { const button = event.target.closest('[data-control]'); if (!button) return; guarded(async () => {
  sessionStorage.setItem('btsmtl.preview.pending-control', JSON.stringify({ runId: state.selected, commandId: crypto.randomUUID(), command: button.dataset.control }));
  document.querySelectorAll('[data-control]').forEach(control => { control.disabled = true; });
  await sendPendingControl(); await updateRun();
}); };
$('frame').oninput = () => { $('follow-live').checked = false; guarded(() => showFrame(Number($('frame').value))); };
function moveFrame(delta) { $('follow-live').checked = false; $('frame').value = Math.max(0, Math.min(state.count - 1, Number($('frame').value) + delta)); return showFrame(Number($('frame').value)); }
$('previous-frame').onclick = () => guarded(() => moveFrame(-1)); $('next-frame').onclick = () => guarded(() => moveFrame(1));
$('fit-view').onclick = fitView;
$('compare').onclick = () => guarded(async () => { const result = await request(`/api/preview/runs/${state.selected}/compare/${$('comparison').value}`); $('comparison-result').textContent = JSON.stringify(result, null, 2); report(!result.comparable ? '运行条件不同，未比较 checksum。' : result.equal ? `已比较 ${result.comparedFrames} 帧，本次已记录范围内一致。` : `首个差异在 SDK 帧 ${result.firstDifference.frame}。`); });
guarded(async () => { await refresh(); const pending = sessionStorage.getItem('btsmtl.preview.pending-request'); if (pending) { report('上次提交结果尚未确认，正在以原请求身份重查。'); await start(); } else { const run = new URLSearchParams(location.search).get('run'); if (run) await selectRun(run); else report('已读取实际来源。选择正式离线包与原 SDK 录像后可提交重放。'); } });
