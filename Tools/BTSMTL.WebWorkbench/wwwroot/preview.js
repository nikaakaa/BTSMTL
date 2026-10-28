import { LogicViewport } from './logic-viewport.js';
import { ObservationQuery } from './observation-query.js';
import { fieldsHtml, sectionsHtml } from './preview-records.js';
import { PreviewObservation } from './preview-observation.js';

const $ = id => document.getElementById(id);
const state = { sources: null, runs: [], selected: null, summary: null, frames: new Map(), count: 0, sealed: false, initialFrameIndex: 0, host: null, playback: null, historyPlayback: null, currentFrame: null };
let pollTimer;
let historyTimer;
const stages = { accepted: '已接收', running: '独立会话运行中', completed: '完整运行已封存', stopped: '会话已停止并封存', failed: '执行失败', cancelled: '已取消' };
const playbackNames = { paused: '已暂停', playing: '正在播放', stepping: '等待下一个确认帧', stopped: '已停止', completed: '已完成' };
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

async function request(path, body, signal) {
  const response = await fetch(path, { signal, ...(body === undefined ? {} : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  }) });
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
  element.replaceChildren(...entries.map((entry, index) => {
    const option = document.createElement('option');
    option.value = value(entry, index); option.textContent = label(entry);
    option.selected = option.value === selected;
    return option;
  }));
}

function sourceVersion() {
  const scene = state.sources.scenes.find(item => item.path === $('scene').value);
  const replay = state.sources.replays.find(item => item.id === $('replay').value);
  $('scene-version').textContent = scene?.revision || '尚未导出正式离线包';
  $('replay-version').textContent = replay?.source.revision || '尚无原录像';
  const local = $('run-source').value === 'local';
  $('replay-source').hidden = local; $('local-source').hidden = !local;
  $('load-inputs').disabled = !scene;
  $('start').textContent = local ? '按输入新建真实运行' : '新建真实重放';
  $('start').disabled = !scene || local || !replay || $('preview-mode').value === 'off';
}

async function refresh() {
  const [sources, runs] = await Promise.all([request('/api/preview/sources'), request('/api/preview/runs')]);
  state.sources = sources; state.runs = runs;
  options($('scene'), sources.scenes, entry => entry.path, entry => entry.path, $('scene').value);
  options($('replay'), sources.replays, entry => entry.displayName, entry => entry.id, $('replay').value);
  sourceVersion();
  $('source-state').textContent = `${sources.scenes.length} 个正式内容包 · ${sources.replays.length} 份原录像。${!sources.scenes.length ? '宿主尚未提供正式内容。' : ''}${!sources.replays.length ? '真实重放需要宿主的原录像。' : ''}`;
  $('runs').innerHTML = runs.map(run => `<button data-run="${escapeHtml(run.jobId)}" class="${run.jobId === state.selected ? 'selected' : ''}">${escapeHtml(stages[run.stage])}<small>${escapeHtml(run.acceptedAt)}</small><code>${escapeHtml(run.jobId)}</code></button>`).join('') || '<p class="muted">尚未提交运行。</p>';
  options($('comparison'), runs.filter(run => ['completed', 'stopped'].includes(run.stage) && run.jobId !== state.selected),
    entry => `${entry.acceptedAt} · ${entry.jobId.slice(0, 8)}`, entry => entry.jobId, $('comparison').value);
  $('compare').disabled = !state.sealed || !$('comparison').value;
  $('connection').textContent = '项目宿主已连接';
}

async function start() {
  let pending = sessionStorage.getItem('btsmtl.preview.pending-request');
  if (!pending) {
    const source = state.sources.replays.find(item => item.id === $('replay').value).source;
    pending = JSON.stringify({ requestId: crypto.randomUUID(), scene: $('scene').value,
      sceneRevision: state.sources.scenes.find(item => item.path === $('scene').value).revision,
      source,
      interactive: $('interactive').checked });
    sessionStorage.setItem('btsmtl.preview.pending-request', pending);
  }
  const receipt = await request('/api/preview/runs', JSON.parse(pending));
  sessionStorage.removeItem('btsmtl.preview.pending-request');
  await refresh();
  await selectRun(receipt.jobId);
}

async function loadInputs() {
  const selected = state.sources.scenes.find(item => item.path === $('scene').value);
  report('正在准备所选离线包，读取正式输入声明…');
  const catalog = await request(`/api/preview/inputs?scene=${encodeURIComponent(selected.path)}&revision=${encodeURIComponent(selected.revision)}`);
  if ($('scene').value !== selected.path) return;
  $('input-events').innerHTML = catalog.players.map(player => `<article class="input-event"><strong>${escapeHtml(player.actorId)}</strong><ul class="input-declarations">${[...player.values, ...player.requests].map(input => `<li>${escapeHtml(input.inputId)}</li>`).join('')}</ul></article>`).join('');
  $('input-state').textContent = `${catalog.players.length} 个本地角色 · ${catalog.tickRate} Tick/秒。宿主尚未提供正式输入方案目录及按键轨，本页只能读取输入声明。请选用正式录像运行。`;
  sourceVersion(); report('已读取正式输入声明；输入方案运行等待宿主提供正式方案来源。');
}

async function selectRun(id, frameIndex = 0) {
  if (!Number.isSafeInteger(frameIndex) || frameIndex < 0) throw new Error('帧序号必须是非负整数。');
  clearTimeout(pollTimer);
  pauseHistory(); state.playback = null;
  observationQuery.reset();
  executionObservation.reset();
  state.selected = id; state.summary = null; state.frames.clear(); state.count = 0; state.sealed = false;
  state.currentFrame = null;
  state.initialFrameIndex = frameIndex;
  delete document.documentElement.dataset.previewImageReady;
  history.replaceState(null, '', `?run=${encodeURIComponent(id)}&frameIndex=${frameIndex}`);
  clearActors(); $('run-details').replaceChildren(); $('frame-facts').replaceChildren();
  $('actors').replaceChildren(); $('frame-sections').replaceChildren(); $('frame-json').textContent = '';
  $('compare').disabled = true; $('frame-label').textContent = '无确认帧';
  $('comparison-result').textContent = '';
  updateTransport();
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
  $('run-state').textContent = receipt.error || (receipt.stage === 'completed' ? '独立运行已完成。下方读取本次封存的确认帧。' : receipt.stage === 'stopped' ? '实际会话已停止，保留停止前已形成的确认帧。' : receipt.stage === 'cancelled' ? '进程已确认取消。此运行没有完整封存记录。' : '正在准备或执行实际独立会话。');
  $('run-state').classList.toggle('error', receipt.stage === 'failed');
  if (['accepted', 'running'].includes(receipt.stage)) {
    const playback = await request(`/api/preview/runs/${id}/playback`);
    if (id !== state.selected) return;
    if (playback.ready) {
      const snapshot = playback.snapshot;
      state.playback = snapshot.state;
      $('playback-state').textContent = playbackNames[snapshot.state];
      const observation = snapshot.observation;
      const summary = observation.summary;
      state.frames.clear(); observation.frames.forEach((frame, index) => state.frames.set(index, frame));
      await displayObservation(summary, false);
      await sendPendingControl();
      if (!sessionStorage.getItem('btsmtl.preview.pending-control'))
        report(`实际会话${playbackNames[snapshot.state]} · 已记录 ${summary.frameCount} 个确认帧。`);
    }
    pollTimer = setTimeout(() => guarded(updateRun), 500);
    return;
  }
  state.playback = null; updateTransport();
  await sendPendingControl();
  if (!['completed', 'stopped'].includes(receipt.stage) || state.sealed) return;
  const summary = await request(`/api/preview/runs/${id}/summary`);
  if (id !== state.selected) return;
  await displayObservation(summary, true);
  report(`${stages[receipt.stage]} · 保留 ${summary.frameCount} 个确认帧。`);
  await refresh();
}

async function displayObservation(summary, sealed) {
  const runId = state.selected;
  const firstFrame = state.count === 0 && summary.frameCount > 0;
  const first = state.summary === null;
  state.summary = summary; state.count = summary.frameCount;
  if (first) observationQuery.bind(state.selected, summary);
  state.sealed = sealed;
  if (sealed && executionObservation.runId !== state.selected) await executionObservation.load(state.selected);
  if (runId !== state.selected) return;
  if (sealed && state.initialFrameIndex > 0 && state.initialFrameIndex >= state.count) throw new Error('请求的帧序号超出本次已封存范围。');
  $('run-details').innerHTML = fieldsHtml([...summary.fields,
    { displayName: `${summary.frameDisplayName}范围`, value: `${summary.firstFrame} — ${summary.lastFrame}` },
    { displayName: '已记录确认帧', value: summary.frameCount },
    { displayName: '记录状态', value: sealed ? '已封存' : '实际会话观察中' }]);
  $('channels').textContent = `本次实际观察：${summary.channels.join('、')}。${summary.geometryAvailable ? '视口显示世界、角色接收体、扫掠采样两端及实际命中点。' : '该记录只提供角色根坐标轴；参考网格不代表碰撞地面。'}`;
  $('frame').max = Math.max(0, state.count - 1);
  if (first) $('frame').value = state.initialFrameIndex;
  if (!sealed && $('follow-live').checked) $('frame').value = Math.max(0, state.count - 1);
  updateTransport();
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
  report(result.stage === 'applied' ? `命令已执行，实际确认帧 ${result.hostFrame ?? '尚未形成'}。` : result.error,
    result.stage !== 'applied');
}

async function showFrame(index) {
  const id = state.selected;
  delete document.documentElement.dataset.previewImageReady;
  const page = Math.floor(index / 128) * 128;
  if (!state.frames.has(index)) {
    const result = await request(`/api/preview/runs/${id}/frames?offset=${page}&limit=128`);
    if (id !== state.selected) return;
    result.items.forEach((frame, offset) => state.frames.set(page + offset, frame));
  }
  if (index !== Number($('frame').value)) return;
  const frame = state.frames.get(index);
  if (state.summary.geometryAvailable && !viewport.geometry) {
    const geometry = await request(`/api/preview/runs/${id}/geometry`);
    if (id !== state.selected || index !== Number($('frame').value)) return;
    viewport.setGeometry(geometry);
  }
  $('frame-label').textContent = `${state.summary.frameDisplayName} ${frame.hostFrame}`;
  $('cursor-state').textContent = `${state.sealed ? '回看封存记录' : '读取已提交记录'} · ${frame.seconds.toFixed(3)} 秒 · 移动游标未重新执行会话。`;
  $('frame-facts').innerHTML = fieldsHtml([{ displayName: '运行相对秒数', value: frame.seconds },
    ...frame.fields, ...(frame.consistency ? [{ displayName: frame.consistency.displayName, value: frame.consistency.value }] : [])]);
  $('actors').innerHTML = frame.actors.map(actor => `<article class="actor-fact"><strong>${escapeHtml(actor.displayName)}</strong><dl>${fieldsHtml(actor.fields)}</dl><details><summary>宿主角色记录</summary><pre>${escapeHtml(actor.rawJson)}</pre></details></article>`).join('');
  state.currentFrame = frame;
  if ($('frame-record-details').open) renderFrameRecords();
  $('frame-json').textContent = frame.rawJson;
  executionObservation.showFrame(frame, index);
  viewport.draw(frame);
  const rendered = document.documentElement.dataset;
  rendered.previewRun = state.summary.runId;
  rendered.previewFrame = frame.hostFrame;
  rendered.previewFrameIndex = String(index);
  rendered.previewConsistency = frame.consistency?.value ?? '';
  rendered.previewContent = state.summary.contentRevision;
  rendered.previewRendered = viewport.geometry ? 'world-receivers-sweeps-contacts' : 'root-axes';
  rendered.previewImageReady = 'true';
  updateTransport();
  history.replaceState(null, '', `?run=${encodeURIComponent(id)}&frameIndex=${index}`);
}

const viewport = new LogicViewport($('viewport'));
const observationQuery = new ObservationQuery($('observation-query'), request);
const executionObservation = new PreviewObservation($('execution-observation'), request, index => {
  pauseHistory(); $('follow-live').checked = false; $('frame').value = index;
  guarded(() => showFrame(index));
});
window.addEventListener('pagehide', () => { observationQuery.close(); pauseHistory(); clearTimeout(pollTimer); });
function clearActors() { viewport.reset(); }
function fitView() { viewport.fit(); }
function renderFrameRecords() { $('frame-sections').innerHTML = sectionsHtml(state.currentFrame.sections); }

function updateTransport() {
  const off = $('preview-mode').value === 'off';
  const pending = !!sessionStorage.getItem('btsmtl.preview.pending-control');
  $('preview-play').disabled = off || pending || (state.sealed ? !state.count || !!state.historyPlayback : !state.playback || state.playback === 'playing');
  $('preview-pause').disabled = off || pending || (state.sealed ? !state.historyPlayback : state.playback !== 'playing');
  $('preview-play').textContent = state.sealed ? '播放记录' : '播放';
  $('preview-pause').textContent = state.sealed ? '暂停记录' : '暂停';
  $('frame').disabled = off || !state.count;
  $('previous-frame').disabled = off || !state.count || Number($('frame').value) === 0;
  $('next-frame').disabled = off || !state.count || Number($('frame').value) === state.count - 1;
  $('fit-view').disabled = off || !state.count;
  $('preview-content').hidden = off;
  $('playback-controls').hidden = off || !state.playback;
  document.querySelectorAll('[data-control]').forEach(button => {
    button.disabled = off || pending || !state.playback || (button.dataset.control === 'Step' && state.playback !== 'paused');
  });
}

function pauseHistory() {
  clearTimeout(historyTimer); state.historyPlayback = null;
  updateTransport();
}

async function control(command) {
  sessionStorage.setItem('btsmtl.preview.pending-control', JSON.stringify({ runId: state.selected, commandId: crypto.randomUUID(), command }));
  updateTransport();
  await sendPendingControl(); await updateRun();
}

async function playHistory() {
  if (Number($('frame').value) === state.count - 1) { $('frame').value = 0; await showFrame(0); }
  const playback = { runId: state.selected, origin: state.frames.get(Number($('frame').value)).seconds, started: performance.now() };
  state.historyPlayback = playback; updateTransport();
  const advance = async () => {
    const target = playback.origin + (performance.now() - playback.started) / 1000;
    let index = Number($('frame').value);
    while (index < state.count - 1) {
      if (!state.frames.has(index + 1)) {
        const page = await request(`/api/preview/runs/${playback.runId}/frames?offset=${index + 1}&limit=128`);
        if (state.historyPlayback !== playback) return;
        page.items.forEach((frame, offset) => state.frames.set(index + 1 + offset, frame));
      }
      if (state.frames.get(index + 1).seconds > target) break;
      index++;
    }
    if (state.historyPlayback !== playback) return;
    if (index !== Number($('frame').value)) { $('frame').value = index; await showFrame(index); }
    if (state.historyPlayback !== playback) return;
    if (index === state.count - 1) { pauseHistory(); return; }
    historyTimer = setTimeout(() => guarded(advance), 16);
  };
  await advance();
}

$('scene').onchange = () => {
  $('input-events').replaceChildren();
  $('input-state').textContent = '场景已切换，请读取该版本的正式输入。'; sourceVersion();
};
$('replay').onchange = sourceVersion; $('run-source').onchange = sourceVersion;
$('load-inputs').onclick = () => guarded(loadInputs);
$('start').onclick = () => guarded(start); $('refresh').onclick = () => guarded(refresh);
$('runs').onclick = event => { const button = event.target.closest('[data-run]'); if (button) guarded(() => selectRun(button.dataset.run)); };
$('cancel').onclick = () => guarded(async () => { const result = await request(`/api/preview/runs/${state.selected}/cancel`, {}); report(result.cancellationRequested ? '取消请求已送达，等待进程确认。' : '本宿主没有仍在执行的对应进程，请查询当前回执。'); await updateRun(); });
$('playback-controls').onclick = event => { const button = event.target.closest('[data-control]'); if (button) guarded(() => control(button.dataset.control)); };
$('preview-play').onclick = () => guarded(() => state.sealed ? playHistory() : control('Play'));
$('preview-pause').onclick = () => guarded(() => state.sealed ? pauseHistory() : control('Pause'));
$('preview-mode').onchange = () => guarded(async () => {
  pauseHistory();
  if ($('preview-mode').value === 'off') {
    if (state.playback === 'playing') await control('Pause');
    $('cursor-state').textContent = '预览已关闭，保留所选运行与游标。';
  }
  updateTransport(); sourceVersion();
});
$('frame').oninput = () => { pauseHistory(); $('follow-live').checked = false; guarded(() => showFrame(Number($('frame').value))); };
function moveFrame(delta) { pauseHistory(); $('follow-live').checked = false; $('frame').value = Math.max(0, Math.min(state.count - 1, Number($('frame').value) + delta)); return showFrame(Number($('frame').value)); }
$('previous-frame').onclick = () => guarded(() => moveFrame(-1)); $('next-frame').onclick = () => guarded(() => moveFrame(1));
$('fit-view').onclick = fitView;
$('frame-record-details').ontoggle = () => { if ($('frame-record-details').open && state.currentFrame) renderFrameRecords(); };
$('compare').onclick = () => guarded(async () => { const result = await request(`/api/preview/runs/${state.selected}/compare/${$('comparison').value}`); $('comparison-result').textContent = result.rawJson; report(!result.comparable ? '运行条件不同，未比较一致性值。' : result.equal ? `已比较 ${result.comparedFrames} 帧，本次已记录范围内一致。` : `首个差异在${state.summary.frameDisplayName} ${result.firstDifference.hostFrame}。`); });
guarded(async () => {
  const host = await request('/api/preview/host');
  if (!host.available) { $('connection').textContent = '未接入运行宿主'; report('当前工作台未接入正式运行宿主，预览不可用。'); return; }
  state.host = host.description; observationQuery.configure(state.host);
  $('runtime-sources').inert = false;
  await refresh();
  const pending = sessionStorage.getItem('btsmtl.preview.pending-request');
  if (pending) { report('上次提交结果尚未确认，正在以原请求身份重查。'); await start(); }
  else {
    const query = new URLSearchParams(location.search), run = query.get('run');
    if (run) await selectRun(run, Number(query.get('frameIndex') ?? '0'));
    else report('已读取实际来源。选择正式内容与录像后可提交重放。');
  }
});
