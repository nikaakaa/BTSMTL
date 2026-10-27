import { LogicViewport, fixed as fp } from './logic-viewport.js';
import { ObservationQuery } from './observation-query.js';

const $ = id => document.getElementById(id);
const state = { sources: null, runs: [], selected: null, summary: null, frames: new Map(), count: 0, sealed: false, initialFrameIndex: 0, inputCatalog: null, inputScene: null, sourceMap: null };
let pollTimer;
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
  $('replay-version').textContent = replay?.source.revision || '尚无原 SDK 录像';
  const local = $('run-source').value === 'local';
  const catalogReady = scene && state.inputCatalog?.revision === scene.revision && state.inputScene === scene.path;
  $('replay-source').hidden = local; $('local-source').hidden = !local;
  $('load-inputs').disabled = !scene;
  $('add-input').disabled = !catalogReady || !state.inputCatalog.players.some(player => player.values.length || player.requests.length);
  $('start').textContent = local ? '按输入新建真实运行' : '新建真实重放';
  $('start').disabled = !scene || (local ? !catalogReady : !replay);
}

async function refresh() {
  const [sources, runs] = await Promise.all([request('/api/preview/sources'), request('/api/preview/runs')]);
  state.sources = sources; state.runs = runs;
  options($('scene'), sources.scenes, entry => entry.path, entry => entry.path, $('scene').value);
  options($('replay'), sources.replays, entry => entry.displayName, entry => entry.id, $('replay').value);
  sourceVersion();
  $('source-state').textContent = `${sources.scenes.length} 个离线包 · ${sources.replays.length} 份原录像。${!sources.scenes.length ? '请先从已保存 Unity 场景正式导出。' : ''}${!sources.replays.length ? '真实重放需要原 SDK 录像文件。' : ''}`;
  $('runs').innerHTML = runs.map(run => `<button data-run="${escapeHtml(run.jobId)}" class="${run.jobId === state.selected ? 'selected' : ''}">${escapeHtml(stages[run.stage])}<small>${escapeHtml(run.acceptedAt)}</small><code>${escapeHtml(run.jobId)}</code></button>`).join('') || '<p class="muted">尚未提交运行。</p>';
  options($('comparison'), runs.filter(run => ['completed', 'stopped'].includes(run.stage) && run.jobId !== state.selected),
    entry => `${entry.acceptedAt} · ${entry.jobId.slice(0, 8)}`, entry => entry.jobId, $('comparison').value);
  $('compare').disabled = !state.sealed || !$('comparison').value;
  $('connection').textContent = '项目宿主已连接';
}

async function start() {
  let pending = sessionStorage.getItem('btsmtl.preview.pending-request');
  if (!pending) {
    const source = $('run-source').value === 'local' ? localSource() :
      state.sources.replays.find(item => item.id === $('replay').value).source;
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
  state.inputCatalog = catalog; state.inputScene = selected.path;
  $('input-events').replaceChildren();
  $('input-state').textContent = `${catalog.players.length} 个本地角色 · ${catalog.tickRate} Tick/秒。日程零点为玩家加入后的下一个输入帧。`;
  sourceVersion(); report('正式输入已读取，可添加请求或输入值。');
}

function addInput() {
  const row = document.createElement('div'); row.className = 'input-event';
  row.innerHTML = '<label>时刻（秒）<input class="input-seconds" type="number" required min="0" step="any" value="0"></label><label>本地角色<select class="input-player"></select></label><label>正式输入<select class="input-binding"></select></label><div class="input-value"></div><button class="remove-input" type="button">移除此时刻</button>';
  const playerSelect = row.querySelector('.input-player');
  const bindingSelect = row.querySelector('.input-binding');
  options(playerSelect, state.inputCatalog.players.filter(player => player.values.length || player.requests.length),
    player => player.actorId, player => player.actorId);
  let bindings;
  function renderValue() {
    const binding = bindings[Number(bindingSelect.value)];
    const value = row.querySelector('.input-value'); value.replaceChildren();
    if (binding.eventKind === 'request') return;
    switch (binding.kind) {
      case 'Boolean': value.innerHTML = '<label>布尔值<select class="input-boolean"><option value="true">true</option><option value="false">false</option></select></label>'; break;
      case 'Scalar': value.innerHTML = '<label>数值<input class="input-scalar" type="number" required step="any" value="0"></label>'; break;
      case 'Yaw': value.innerHTML = '<label>朝向值（Yaw）<input class="input-scalar" type="number" required step="any" value="0"></label>'; break;
      case 'Vector2': value.innerHTML = '<label>X<input class="input-x" type="number" required step="any" value="0"></label><label>Y<input class="input-y" type="number" required step="any" value="0"></label>'; break;
      case 'Vector3': value.innerHTML = '<label>X<input class="input-x" type="number" required step="any" value="0"></label><label>Y<input class="input-y" type="number" required step="any" value="0"></label><label>Z<input class="input-z" type="number" required step="any" value="0"></label>'; break;
      case 'Identity': {
        const label = document.createElement('label'); label.textContent = '目标身份（留空清除）';
        const input = document.createElement('input'); input.className = 'input-identity'; input.type = 'text';
        const choices = document.createElement('datalist'); choices.id = `targets-${crypto.randomUUID()}`;
        input.setAttribute('list', choices.id);
        choices.replaceChildren(...state.inputCatalog.actorTargets.map(actorId => { const option = document.createElement('option'); option.value = actorId; return option; }));
        label.append(input); value.append(label, choices); break;
      }
      default: throw new Error(`输入 ${binding.inputId} 的 ${binding.kind} 类型没有网页编辑映射。`);
    }
  }
  function renderBindings() {
    const player = state.inputCatalog.players.find(player => player.actorId === playerSelect.value);
    bindings = [...player.requests.map(input => ({ ...input, eventKind: 'request' })),
      ...player.values.map(input => ({ ...input, eventKind: 'value' }))];
    options(bindingSelect, bindings, input => `${input.eventKind === 'request' ? '请求' : input.kind} · ${input.inputId}`,
      (_, index) => String(index));
    renderValue();
  }
  playerSelect.onchange = () => guarded(async () => renderBindings());
  bindingSelect.onchange = () => guarded(async () => renderValue());
  row.querySelector('.remove-input').onclick = () => row.remove();
  row.readInput = () => {
    const binding = bindings[Number(bindingSelect.value)];
    const input = { kind: binding.eventKind, inputId: binding.inputId, seconds: Number(row.querySelector('.input-seconds').value) };
    if (input.kind === 'value') {
      switch (binding.kind) {
        case 'Boolean': input.value = { kind: 'boolean', value: row.querySelector('.input-boolean').value === 'true' }; break;
        case 'Scalar': input.value = { kind: 'scalar', value: Number(row.querySelector('.input-scalar').value) }; break;
        case 'Yaw': input.value = { kind: 'yaw', value: Number(row.querySelector('.input-scalar').value) }; break;
        case 'Vector2': input.value = { kind: 'vector2', x: Number(row.querySelector('.input-x').value), y: Number(row.querySelector('.input-y').value) }; break;
        case 'Vector3': input.value = { kind: 'vector3', x: Number(row.querySelector('.input-x').value), y: Number(row.querySelector('.input-y').value), z: Number(row.querySelector('.input-z').value) }; break;
        case 'Identity': input.value = { kind: 'identity', value: row.querySelector('.input-identity').value }; break;
        default: throw new Error(`输入 ${binding.inputId} 的 ${binding.kind} 类型没有正式映射。`);
      }
    }
    return { actorId: playerSelect.value, input };
  };
  renderBindings(); $('input-events').append(row);
}

function localSource() {
  for (const input of $('local-source').querySelectorAll('input'))
    if (!input.reportValidity()) throw new Error('请补齐输入时刻、数值及结束 SDK 帧。');
  const players = new Map();
  for (const row of $('input-events').children) {
    const { actorId, input } = row.readInput();
    if (!players.has(actorId)) players.set(actorId, []);
    players.get(actorId).push(input);
  }
  return { kind: 'local', lastSdkFrame: Number($('last-sdk-frame').value),
    players: [...players].map(([actorId, inputs]) => ({ actorId, inputs: inputs.sort((left, right) => left.seconds - right.seconds) })) };
}

async function selectRun(id, frameIndex = 0) {
  if (!Number.isSafeInteger(frameIndex) || frameIndex < 0) throw new Error('帧序号必须是非负整数。');
  clearTimeout(pollTimer);
  observationQuery.reset();
  state.selected = id; state.summary = null; state.frames.clear(); state.count = 0; state.sealed = false;
  state.initialFrameIndex = frameIndex;
  delete document.documentElement.dataset.previewImageReady;
  history.replaceState(null, '', `?run=${encodeURIComponent(id)}&frameIndex=${frameIndex}`);
  $('playback-controls').hidden = true;
  clearActors(); $('run-details').replaceChildren(); $('frame-facts').replaceChildren();
  $('actors').replaceChildren(); $('action-changes').replaceChildren(); $('frame-json').textContent = '';
  $('timeline-playbacks').replaceChildren();
  state.sourceMap = null;
  ['operation-states', 'blackboard-values', 'combat-facts', 'output-events'].forEach(id => $(id).replaceChildren());
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
  $('run-state').textContent = receipt.error || (receipt.stage === 'completed' ? '独立运行已完成。下方读取本次封存的确认帧。' : receipt.stage === 'stopped' ? '实际会话已停止，保留停止前已形成的确认帧。' : receipt.stage === 'cancelled' ? '进程已确认取消。此运行没有完整封存记录。' : '正在准备或执行实际独立会话。');
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
  if (first) observationQuery.bind(state.selected, summary);
  state.sealed = sealed;
  if (sealed && state.initialFrameIndex > 0 && state.initialFrameIndex >= state.count) throw new Error('请求的帧序号超出本次已封存范围。');
  const facts = { '运行身份': summary.runId, '场景修订': summary.sceneRevision,
    '原录像修订': summary.replaySourceHash, '来源场景': summary.scene.path,
    '场景定义': summary.definition.path, '内容身份': summary.sessionIdentity.gameplayContent,
    'TickRate': summary.sessionIdentity.tickRate, 'SDK 帧范围': `${summary.initialSdkFrame} — ${summary.lastSdkFrame}`,
    '已记录确认帧': summary.frameCount, '记录状态': sealed ? '已封存' : '实际会话观察中' };
  $('run-details').innerHTML = Object.entries(facts).map(([name, value]) => `<dt>${escapeHtml(name)}</dt><dd>${escapeHtml(value)}</dd>`).join('');
  $('channels').textContent = `本次实际观察：${summary.channels.join('、')}。${summary.channels.includes('collision-geometry') ? '视口显示 SDK 世界、角色接收体、扫掠采样两端及实际命中点。' : '该记录只提供角色根坐标轴；参考网格不代表碰撞地面。'}`;
  $('frame').max = Math.max(0, state.count - 1);
  if (first) $('frame').value = state.initialFrameIndex;
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
  let frame = state.frames.get(index);
  let observation = null;
  if (state.summary.channels.some(channel => ['collision-geometry', 'operation-states', 'blackboard-values'].includes(channel))) {
    if (state.summary.channels.includes('collision-geometry') && !viewport.geometry)
      viewport.setGeometry(await request(`/api/preview/runs/${id}/geometry`));
    if (state.sourceMap === null) {
      const sources = []; let offset = 0;
      do {
        const page = await request(`/api/preview/runs/${id}/source-map?offset=${offset}&limit=512`);
        sources.push(...page.items); offset = page.nextOffset;
      } while (offset !== null);
      if (id !== state.selected) return;
      state.sourceMap = sources;
    }
    const page = await request(`/api/preview/runs/${id}/observations?firstFrame=${frame.frame}&lastFrame=${frame.frame}&offset=0&limit=1`);
    if (id !== state.selected || index !== Number($('frame').value)) return;
    observation = page.items[0]; frame = observation.frame;
  }
  $('previous-frame').disabled = index === 0; $('next-frame').disabled = index === state.count - 1;
  $('frame-label').textContent = `SDK ${frame.frame}`;
  $('frame-facts').innerHTML = `<dt>确认 SDK 帧</dt><dd>${escapeHtml(frame.frame)}</dd><dt>玩法 Tick</dt><dd>${escapeHtml(frame.gameplayTick)}</dd><dt>完整 checksum</dt><dd><code>${escapeHtml(frame.checksum)}</code></dd>`;
  $('actors').innerHTML = frame.actors.map(actor => `<div class="actor-fact"><strong>Roster ${actor.rosterIndex}</strong> · Entity ${actor.entityIndex}:${actor.entityVersion}<code>Position FP Raw · ${actor.positionRaw.map(escapeHtml).join(' / ')}</code><code>Rotation FP Raw · ${actor.rotationRaw.map(escapeHtml).join(' / ')}</code></div>`).join('');
  if (state.summary.channels.includes('action-changes')) {
    $('action-changes').innerHTML = frame.actionChanges.length ? frame.actionChanges.map(action => {
      const hasAction = action.instanceId !== '0';
      const title = hasAction ? `动作实例 ${action.instanceId} · ${action.phase}` : `激活请求 ${action.requestId} · ${action.admission}`;
      const facts = { '角色': `Roster ${action.rosterIndex}`, '能力资产': action.abilityAssetId,
        '输入序号': action.inputSequence, '执行代数': action.executionGeneration,
        '上下文 / 操作': `${action.contextId} / ${action.entryOperationIndex}`,
        '业务结果': action.committed ? '动作已提交' : action.reason,
        '控制意图': action.controlIntent, '执行失败': action.stepKind };
      return `<div class="actor-fact"><strong>${escapeHtml(title)}</strong><dl>${Object.entries(facts)
        .filter(([, value]) => value !== null && value !== '')
        .map(([name, value]) => `<dt>${escapeHtml(name)}</dt><dd>${escapeHtml(value)}</dd>`).join('')}</dl></div>`;
    }).join('') : '<p class="muted">此确认帧没有动作变化。完整请求与失败字段保留在同源 JSON 中。</p>';
  } else $('action-changes').innerHTML = '<p class="muted">此运行未采集动作变化通道。</p>';
  if (state.summary.channels.includes('timeline-playbacks')) {
    $('timeline-playbacks').innerHTML = frame.timelinePlaybacks.length ? frame.timelinePlaybacks.map(playback => {
      const facts = { '角色 / 播放槽': `${playback.rosterIndex} / ${playback.playbackSlot}`,
        '实际内容': playback.contentIdentity, '内容修订': playback.contentRevision,
        '播放 / 父播放': `${playback.playbackIdentity} / ${playback.callerPlaybackIdentity}`,
        '所属动作 / 输入': `${playback.ownerInstanceId} / ${playback.inputSequence}`,
        '调用来源': playback.sourceIdentity, '来源代数': playback.sourceGeneration,
        '逻辑游标': `${fp(playback.cursorTimeRaw).toFixed(4)} 秒 · raw ${playback.cursorTimeRaw}`,
        '倍率 / 循环': `${fp(playback.rateRaw).toFixed(4)} / ${playback.cycle}`,
        '状态': `${playback.status}${playback.paused ? ' · 暂停' : ''}` };
      return `<div class="actor-fact"><strong>逻辑 Timeline · ${escapeHtml(playback.playbackIdentity)}</strong><dl>${Object.entries(facts)
        .map(([name, value]) => `<dt>${escapeHtml(name)}</dt><dd>${escapeHtml(value)}</dd>`).join('')}</dl></div>`;
    }).join('') : '<p class="muted">此确认帧没有活动的逻辑 Timeline。</p>';
  } else $('timeline-playbacks').innerHTML = '<p class="muted">此运行未采集 Timeline 播放通道。</p>';
  renderCommittedFacts(frame, observation);
  $('frame-json').textContent = JSON.stringify(observation || frame, null, 2);
  viewport.draw(frame, observation?.queries || []);
  const rendered = document.documentElement.dataset;
  rendered.previewRun = state.summary.runId;
  rendered.previewFrame = String(frame.frame);
  rendered.previewFrameIndex = String(index);
  rendered.previewChecksum = frame.checksum;
  rendered.previewScene = state.summary.sceneRevision;
  rendered.previewRendered = viewport.geometry ? 'sdk-world-receivers-sweeps-hits' : 'root-axes';
  rendered.previewImageReady = 'true';
  history.replaceState(null, '', `?run=${encodeURIComponent(id)}&frameIndex=${index}`);
}

function renderCommittedFacts(frame, observation) {
  const detail = (title, fields) => `<div class="actor-fact"><strong>${escapeHtml(title)}</strong><dl>${Object.entries(fields)
    .filter(([, value]) => value !== null && value !== undefined && value !== '')
    .map(([name, value]) => `<dt>${escapeHtml(name)}</dt><dd>${escapeHtml(value)}</dd>`).join('')}</dl></div>`;
  $('operation-states').innerHTML = observation?.operations.length ? observation.operations.map(operation => {
    const sources = state.sourceMap.filter(map => map.rosterIndex === operation.rosterIndex &&
      map.programIndex === operation.programIndex && map.source.targetKind === 'Operation' && map.source.targetIndex === operation.operationIndex);
    return detail(`${operation.status} · Roster ${operation.rosterIndex}`, {
      '活动状态': operation.activeState, '待进入状态': operation.pendingState, '退出状态': operation.exitingState,
      '实际转移': operation.transition, '动作 / 播放': `${operation.actionInstanceId} / ${operation.playbackIdentity}`,
      '执行 / 调用代次': `${operation.executionGeneration} / ${operation.generation}`,
      '作者来源': sources.map(map => `${map.source.graphId} / ${map.source.nodeId || map.source.edgeId}`).join('；')
    });
  }).join('') : `<p class="muted">${state.summary.channels.includes('operation-states') ? '本帧没有活动操作记录。' : '该运行未采集操作状态。'}</p>`;
  $('blackboard-values').innerHTML = observation?.variables.length ? observation.variables.map(variable => detail(variable.key, {
    '有效值': variable.available ? JSON.stringify(variable.value) : variable.reason,
    '类型': variable.valueType, '作用域 / 生命周期': `${variable.scope} / ${variable.lifetime}`,
    '动作 / 执行代次': `${variable.actionInstanceId} / ${variable.executionGeneration}`,
    '调用代次': variable.invocationGeneration, '声明来源': `${variable.graphId} / ${variable.declarationId}`,
    '调用路径': variable.invocationPath
  })).join('') : `<p class="muted">${state.summary.channels.includes('blackboard-values') ? '本帧没有对应调用的黑板观察。' : '该运行未采集黑板有效值。'}</p>`;
  const hits = frame.hits || [], failures = frame.queryFailures || [], queries = observation?.queries || [];
  $('combat-facts').innerHTML = hits.map(hit => detail(`实际命中 ${hit.resultId}`, {
    '攻击方 → 目标': `${hit.sourceIndex}:${hit.sourceVersion} → ${hit.targetIndex}:${hit.targetVersion}`,
    '动作 / 执行代次': `${hit.attack.actionInstanceId} / ${hit.attack.executionGeneration}`,
    '命中点 FP Raw': hit.pointRaw.join(' / '), '法线 FP Raw': hit.normalRaw.join(' / '),
    '接触时刻 FP Raw': hit.contactTimeRaw, '处理结果': hit.disposition
  })).concat(failures.map(failure => detail('实际查询失败', { '失败种类': failure.kind, '扫掠状态': failure.sweepStatus,
    '请求': failure.requestId, '动作实例': failure.attack.actionInstanceId })),
  queries.map(query => detail(`扫掠查询 · ${query.status}`, { '攻击事件': query.attackEventId,
    '正式后端已执行': query.backendInvoked, '采样分段 / 目标': `${query.segments.length} / ${query.targets.length}`,
    '动作 / 播放': `${query.attack.actionInstanceId} / ${query.attack.playbackIdentity}` }))).join('') ||
    `<p class="muted">${state.summary.channels.includes('combat-results') ? '本帧没有命中或失败事实。' : '该运行未采集命中通道。'}</p>`;
  $('output-events').innerHTML = observation?.outputEvents.length ? observation.outputEvents.map(event => detail(
    `${event.kind} · ${event.lifecycle}`, { '角色 / 动作': `${event.rosterIndex} / ${event.actionInstanceId}`,
      '逻辑 Tick': event.logicTick, '播放身份': event.playbackIdentity, '来源': event.sourceIdentity,
      '实际请求数': event.requests.length, '请求来源': event.requests.map(request => request.sourceIdentity).join('；') }
  )).join('') : `<p class="muted">${state.summary.channels.includes('output-events') ? '本帧没有表现请求事件。' : '该运行未采集表现请求。'}</p>`;
}

const viewport = new LogicViewport($('viewport'));
const observationQuery = new ObservationQuery($('observation-query'), request);
window.addEventListener('pagehide', () => observationQuery.close());
function clearActors() { viewport.reset(); }
function fitView() { viewport.fit(); }

$('scene').onchange = () => {
  state.inputCatalog = null; state.inputScene = null; $('input-events').replaceChildren();
  $('add-input').disabled = true; $('input-state').textContent = '场景已切换，请读取该版本的正式输入。'; sourceVersion();
};
$('replay').onchange = sourceVersion; $('run-source').onchange = sourceVersion;
$('load-inputs').onclick = () => guarded(loadInputs); $('add-input').onclick = () => guarded(async () => addInput());
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
guarded(async () => { await refresh(); const pending = sessionStorage.getItem('btsmtl.preview.pending-request'); if (pending) { report('上次提交结果尚未确认，正在以原请求身份重查。'); await start(); } else { const query = new URLSearchParams(location.search); const run = query.get('run'); if (run) await selectRun(run, Number(query.get('frameIndex') ?? '0')); else report('已读取实际来源。选择正式离线包与原 SDK 录像后可提交重放。'); } });
