import { LogicViewport } from './logic-viewport.js';
import { ObservationQuery } from './observation-query.js';
import { fieldsHtml, sectionsHtml } from './preview-records.js';
import { PreviewObservation } from './preview-observation.js';

const $ = id => document.getElementById(id);
const state = { sources: null, runs: [], selected: null, summary: null, frames: new Map(), count: 0, sealed: false, frameIndex: 0, host: null, playback: null, historyPlayback: null, currentFrame: null, mode: 'real' };
let pollTimer;
let historyTimer;
const stages = { accepted: '准备中', running: '运行中', completed: '已封存', stopped: '已停止', failed: '执行失败', cancelled: '已取消' };
const playbackNames = { paused: '已暂停', playing: '正在播放', stepping: '等待下一个确认帧', stopped: '已停止', completed: '已完成' };
const runLabel = run => `${new Date(run.acceptedAt).toLocaleString()} · ${stages[run.stage]}`;
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
  $('start').textContent = local ? '输入方案不可用' : '重新播放';
  $('start').disabled = !scene || local || !replay || state.mode === 'off';
}

async function refresh() {
  const [sources, runs] = await Promise.all([request('/api/preview/sources'), request('/api/preview/runs')]);
  state.sources = sources; state.runs = runs;
  options($('scene'), sources.scenes, entry => entry.path.split('/')[0], entry => entry.path, $('scene').value);
  options($('replay'), sources.replays, entry => entry.displayName, entry => entry.id, $('replay').value);
  sourceVersion();
  $('source-state').textContent = `${sources.scenes.length} 个正式内容包 · ${sources.replays.length} 份原录像。${!sources.scenes.length ? '宿主尚未提供正式内容。' : ''}${!sources.replays.length ? '真实重放需要宿主的原录像。' : ''}`;
  options($('run-select'), [null, ...runs], run => run ? runLabel(run) : '选择运行', run => run?.jobId ?? '', state.selected ?? '');
  $('run-select').disabled = runs.length === 0;
  options($('comparison'), runs.filter(run => ['completed', 'stopped'].includes(run.stage) && run.jobId !== state.selected),
    entry => `${entry.acceptedAt} · ${entry.jobId.slice(0, 8)}`, entry => entry.jobId, $('comparison').value);
  $('compare').disabled = !state.sealed || !$('comparison').value;
  $('connection').textContent = '已连接';
  document.querySelectorAll('[data-preview-mode]').forEach(button => {
    const mode = state.host.modes.find(mode => mode.id === button.dataset.previewMode);
    button.disabled = !mode.available; button.title = mode.reason || mode.displayName;
  });
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
  $('preview-settings').close();
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
  state.frameIndex = frameIndex;
  delete document.documentElement.dataset.previewImageReady;
  history.replaceState(null, '', `?run=${encodeURIComponent(id)}&frameIndex=${frameIndex}`);
  clearActors(); $('run-details').replaceChildren(); $('frame-facts').replaceChildren();
  $('actors').replaceChildren(); $('frame-sections').replaceChildren(); $('frame-json').textContent = '';
  $('compare').disabled = true; $('frame-label').textContent = '无确认帧';
  $('comparison-result').textContent = '';
  $('run-state').hidden = true; $('run-error-detail').textContent = '';
  $('run-title').textContent = '读取中';
  updateTransport();
  $('run-identity').textContent = id;
  $('run-select').value = id;
  await updateRun();
  await refresh();
}

async function updateRun() {
  clearTimeout(pollTimer);
  const id = state.selected;
  const receipt = await request(`/api/preview/runs/${id}`);
  if (id !== state.selected) return;
  $('run-title').textContent = stages[receipt.stage];
  $('run-select').selectedOptions[0].textContent = runLabel(receipt);
  $('cancel').hidden = !['accepted', 'running'].includes(receipt.stage);
  $('run-state').textContent = receipt.error ? receipt.error.split(/\r?\n/, 1)[0].replace(/^[\w.]+Exception: /, '') : '';
  $('run-error-detail').textContent = receipt.error || '';
  $('run-state').hidden = receipt.stage !== 'failed';
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
      if (id !== state.selected) return;
      await sendPendingControl();
      if (id !== state.selected) return;
      if (!sessionStorage.getItem('btsmtl.preview.pending-control'))
        report(playbackNames[snapshot.state]);
    }
    pollTimer = setTimeout(() => guarded(updateRun), 500);
    return;
  }
  state.playback = null; updateTransport();
  await sendPendingControl();
  if (receipt.stage === 'failed' || receipt.stage === 'cancelled') {
    report(stages[receipt.stage], receipt.stage === 'failed');
    await refresh();
    return;
  }
  if (!['completed', 'stopped'].includes(receipt.stage) || state.sealed) return;
  const summary = await request(`/api/preview/runs/${id}/summary`);
  if (id !== state.selected) return;
  await displayObservation(summary, true);
  if (id !== state.selected) return;
  report(stages[receipt.stage]);
  await refresh();
}

async function displayObservation(summary, sealed) {
  const runId = state.selected;
  const firstFrame = state.count === 0 && summary.frameCount > 0;
  const first = state.summary === null;
  state.summary = summary; state.count = summary.frameCount;
  if (first) {
    const source = state.sources.scenes.find(source => source.revision === summary.contentRevision);
    $('scene').value = source ? source.path : '';
    sourceVersion();
  }
  if (first) observationQuery.bind(state.selected, summary);
  state.sealed = sealed;
  if (executionObservation.runId !== state.selected) await executionObservation.load(state.selected);
  if (runId !== state.selected) return;
  await executionObservation.sync(summary);
  if (runId !== state.selected) return;
  if (sealed && state.frameIndex > 0 && state.frameIndex >= state.count) throw new Error('请求的帧序号超出本次已封存范围。');
  $('run-details').innerHTML = fieldsHtml([...summary.fields,
    { displayName: `${summary.frameDisplayName}范围`, value: `${summary.firstFrame} — ${summary.lastFrame}` },
    { displayName: '已记录确认帧', value: summary.frameCount },
    { displayName: '记录状态', value: sealed ? '已封存' : '实际会话观察中' }]);
  $('channels').textContent = `本次实际观察：${summary.channels.join('、')}。${summary.geometryAvailable ? '视口显示世界、角色接收体、扫掠采样两端及实际命中点。' : '该记录只提供角色根坐标轴；参考网格不代表碰撞地面。'}`;
  if (!sealed && $('follow-live').checked) state.frameIndex = Math.max(0, state.count - 1);
  updateTransport();
  if (state.count) { await showFrame(state.frameIndex); if (firstFrame) fitView(); }
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
  if (index !== state.frameIndex) return;
  const frame = state.frames.get(index);
  if (state.summary.geometryAvailable && !viewport.geometry) {
    const geometry = await request(`/api/preview/runs/${id}/geometry`);
    if (id !== state.selected || index !== state.frameIndex) return;
    viewport.setGeometry(geometry);
  }
  $('frame-label').textContent = `${frame.hostFrame} f · ${frame.seconds.toFixed(3)} s`;
  report(state.sealed ? state.historyPlayback ? '正在回看' : '记录回看' : playbackNames[state.playback]);
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
  pauseHistory(); $('follow-live').checked = false; state.frameIndex = index;
  guarded(() => showFrame(index));
}, updateCapabilities);

function updateCapabilities(availability) {
  const channels = new Map(availability.map(channel => [channel.channel, channel]));
  const gaps = [{ name: '输入轨', reason: '宿主尚未提供正式输入方案及按键轨。' }];
  if (state.host) for (const mode of state.host.modes) {
    if (!mode.available) gaps.push({ name: `${mode.displayName}预览`, reason: mode.reason });
  }
  const names = { 'graph-definitions': '节点图', 'node-occurrences': '节点路线', 'edge-occurrences': '连线路线',
    'port-values': '端口值', 'author-layout': '作者布局', 'author-display-names': '作者名称', 'author-value-connections': '数值连线',
    'execution-source-bindings': '节点来源', 'port-source-bindings': '端口来源', 'port-parent-invocation-generation': '父调用代次',
    'compiled-control-flow': '控制连线' };
  for (const channel of channels.values()) {
    if (!channel.available || !channel.complete)
      gaps.push({ name: `${names[channel.channel] ?? channel.channel}${channel.available ? '不完整' : ''}`, reason: channel.reason });
  }
  $('capabilities').hidden = !state.host || !gaps.length;
  $('capability-state').textContent = `预览能力（缺项 ${gaps.length}）`;
  $('capability-state').title = gaps.map(gap => `${gap.name}：${gap.reason}`).join('\n');
  $('capability-details').innerHTML = fieldsHtml(gaps.map(gap => ({ displayName: gap.name, value: gap.reason })));
}
window.addEventListener('pagehide', () => { observationQuery.close(); pauseHistory(); clearTimeout(pollTimer); });
function clearActors() { viewport.reset(); }
function fitView() { viewport.fit(); }
function renderFrameRecords() { $('frame-sections').innerHTML = sectionsHtml(state.currentFrame.sections); }

function updateTransport() {
  const off = state.mode === 'off';
  const pending = !!sessionStorage.getItem('btsmtl.preview.pending-control');
  const playing = state.sealed ? !!state.historyPlayback : state.playback === 'playing';
  $('preview-toggle').disabled = off || pending || (state.sealed ? !state.count : !state.playback);
  $('preview-toggle').textContent = playing ? 'Ⅱ' : '▶';
  $('preview-toggle').setAttribute('aria-label', playing ? '暂停' : '播放');
  $('preview-toggle').title = playing ? '暂停' : '播放';
  document.querySelectorAll('[data-preview-mode]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.previewMode === state.mode)));
  $('previous-frame').disabled = off || !state.count || state.frameIndex === 0;
  $('next-frame').disabled = off || !state.count || state.frameIndex === state.count - 1;
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

function control(command) {
  sessionStorage.setItem('btsmtl.preview.pending-control', JSON.stringify({ runId: state.selected, commandId: crypto.randomUUID(), command }));
  updateTransport();
}

async function playHistory() {
  if (state.frameIndex === state.count - 1) { state.frameIndex = 0; await showFrame(0); }
  const playback = { runId: state.selected, origin: state.frames.get(state.frameIndex).seconds, started: performance.now() };
  state.historyPlayback = playback; updateTransport();
  const advance = async () => {
    const target = playback.origin + (performance.now() - playback.started) / 1000;
    let index = state.frameIndex;
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
    if (index !== state.frameIndex) { state.frameIndex = index; await showFrame(index); }
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
$('run-select').onchange = () => { if ($('run-select').value) guarded(() => selectRun($('run-select').value)); };
$('cancel').onclick = () => guarded(async () => { const result = await request(`/api/preview/runs/${state.selected}/cancel`, {}); report(result.cancellationRequested ? '取消请求已送达，等待进程确认。' : '本宿主没有仍在执行的对应进程，请查询当前回执。'); });
$('playback-controls').onclick = event => { const button = event.target.closest('[data-control]'); if (button) guarded(() => control(button.dataset.control)); };
$('preview-toggle').onclick = () => guarded(() => state.sealed ? state.historyPlayback ? pauseHistory() : playHistory() : control(state.playback === 'playing' ? 'Pause' : 'Play'));
document.querySelectorAll('[data-preview-mode]').forEach(button => { button.onclick = () => guarded(async () => {
  state.mode = button.dataset.previewMode;
  pauseHistory();
  if (state.mode === 'off') {
    if (state.playback === 'playing') control('Pause');
    report('预览已关闭');
  }
  else if (state.count) await showFrame(state.frameIndex);
  else report(state.selected ? $('run-title').textContent : '选择运行', !$('run-state').hidden);
  updateTransport(); sourceVersion();
}); });
function moveFrame(delta) { pauseHistory(); $('follow-live').checked = false; state.frameIndex = Math.max(0, Math.min(state.count - 1, state.frameIndex + delta)); return showFrame(state.frameIndex); }
$('previous-frame').onclick = () => guarded(() => moveFrame(-1)); $('next-frame').onclick = () => guarded(() => moveFrame(1));
$('fit-view').onclick = fitView;
$('frame-record-details').ontoggle = () => { if ($('frame-record-details').open && state.currentFrame) renderFrameRecords(); };
$('settings-open').onclick = () => $('preview-settings').showModal();
$('settings-close').onclick = () => $('preview-settings').close();
$('compare').onclick = () => guarded(async () => { const result = await request(`/api/preview/runs/${state.selected}/compare/${$('comparison').value}`); $('comparison-result').textContent = result.rawJson; report(!result.comparable ? '运行条件不同，未比较一致性值。' : result.equal ? `已比较 ${result.comparedFrames} 帧，本次已记录范围内一致。` : `首个差异在${state.summary.frameDisplayName} ${result.firstDifference.hostFrame}。`); });
guarded(async () => {
  const host = await request('/api/preview/host');
  if (!host.available) { $('connection').textContent = '未接入运行宿主'; report('当前工作台未接入正式运行宿主，预览不可用。'); return; }
  state.host = host.description; observationQuery.configure(state.host);
  updateCapabilities([]);
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
