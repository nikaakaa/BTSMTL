// 作者页运行预览：读取宿主封存运行的轻量路线，在当前 Timeline 画游标、在图上标出本帧执行对象；
// 不推导快速档、不缓存画面，节点值按需读取单帧记录。
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const preview = { host: null, sources: null, runs: [], runId: localStorage.getItem('btsmtl.run-preview.run') || '',
  route: null, frame: 0, playing: false, timer: 0, plans: new Map(), inputs: new Map(), frameRecords: new Map(),
  context: null, status: '', loading: null };

async function request(path, body) {
  const response = await fetch(path, body === undefined ? {} : { method: 'POST',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const text = await response.text();
  if (!response.ok) throw new Error(`${response.status} · ${text.slice(0, 300)}`);
  return JSON.parse(text);
}

function report(text) { preview.status = text; const status = document.querySelector('[data-run-preview-status]'); if (status) status.textContent = text; }

async function ensureHost() {
  if (preview.host) return;
  const host = await request('/api/preview/host');
  if (!host.available) { preview.host = host; return; }
  const [sources, runs] = await Promise.all([request('/api/preview/sources'), request('/api/preview/runs')]);
  preview.host = host; preview.sources = sources;
  preview.runs = runs.filter(run => run.stage === 'completed' || run.stage === 'stopped');
}

// 输入方案按 Owner 归属技能根：编排根为 ability:<根名>，与 Unity 方案资产的归属一致。
async function inputPlan(root) {
  const owner = `ability:${root.displayName}`;
  if (preview.plans.has(owner)) return preview.plans.get(owner);
  const [workspace, roots] = await Promise.all([request('/api/workspace'), request('/api/workspace/roots?offset=0&limit=200')]);
  let plan = null;
  for (const candidate of roots.items.filter(item => item.kind === 'TimelinePreviewInputSchemes')) {
    const snapshot = await request(`/api/workspace/root?path=${encodeURIComponent(candidate.path)}`);
    const document = JSON.parse(snapshot.root);
    if (document.fields.Owner?.value !== owner) continue;
    const scheme = Number(document.fields.Selected?.value ?? 0);
    const member = document.fields.Schemes.items[scheme]?.members;
    const timelineId = member?.InputTimeline?.target?.documentId;
    const file = snapshot.files.find(entry => entry.content.includes(`"id": "${timelineId}"`) && entry.path.startsWith('timelines/'));
    plan = { path: candidate.path, revision: snapshot.revision, scheme, schemeName: member?.Name?.value || `方案 ${scheme}`,
      directory: `${workspace.path}/${candidate.path.slice(0, candidate.path.lastIndexOf('/'))}`,
      timeScale: Number(workspace.timelineTimeScale), timeline: file ? JSON.parse(file.content) : null };
    break;
  }
  preview.plans.set(owner, plan);
  return plan;
}

async function loadRoute(runId) {
  if (!runId) { preview.route = null; return; }
  report('正在读取运行路线…');
  preview.route = await request(`/api/preview/runs/${runId}/route`);
  preview.runId = runId; preview.frame = 0; preview.frameRecords.clear();
  localStorage.setItem('btsmtl.run-preview.run', runId);
  report(`运行 ${runId.slice(0, 8)} · ${preview.route.frames.length} 个确认帧。`);
}

async function startRun(scene) {
  const context = preview.context, plan = await inputPlan(context.root);
  if (!plan) throw new Error(`工作区没有 Owner 为 ability:${context.root.displayName} 的输入方案。`);
  const source = preview.sources.scenes.find(item => item.path === scene);
  report('正在准备场景输入声明…');
  const key = `${source.path}@${source.revision}`;
  if (!preview.inputs.has(key)) preview.inputs.set(key, await request(
    `/api/preview/inputs?scene=${encodeURIComponent(source.path)}&revision=${encodeURIComponent(source.revision)}`));
  const actor = preview.inputs.get(key).players.find(player => player.values.length || player.requests.length);
  const limit = preview.host.description.inputLimit;
  const receipt = await request('/api/preview/runs', { requestId: crypto.randomUUID(), scene: source.path,
    sceneRevision: source.revision, interactive: false, source: { kind: 'local', [limit.name]: Number(limit.initialValue),
      players: [{ actorId: actor.actorId, inputPlanRoot: plan.directory, inputPlanRevision: plan.revision, schemeIndex: plan.scheme }] } });
  for (;;) {
    report(`运行 ${receipt.jobId.slice(0, 8)} 执行中…`);
    await new Promise(resolve => setTimeout(resolve, 1000));
    const run = await request(`/api/preview/runs/${receipt.jobId}`);
    if (run.stage === 'failed') throw new Error(`运行失败：${(run.error || '').split('\n')[0]}`);
    if (run.stage === 'completed' || run.stage === 'stopped') {
      preview.runs.unshift(run);
      localStorage.setItem('btsmtl.run-preview.scene', scene);
      await loadRoute(receipt.jobId);
      return;
    }
  }
}

function currentFrame() { return preview.route?.frames[preview.frame]; }

function setFrame(index) {
  if (!preview.route) return;
  preview.frame = Math.max(0, Math.min(preview.route.frames.length - 1, index));
  decorate();
}

function togglePlay() {
  preview.playing = !preview.playing;
  clearInterval(preview.timer);
  if (preview.playing) {
    let last = performance.now();
    preview.timer = setInterval(() => {
      const frames = preview.route?.frames, now = performance.now();
      if (!frames) return;
      const target = frames[preview.frame].seconds + (now - last) / 1000;
      while (preview.frame < frames.length - 1 && frames[preview.frame + 1].seconds <= target) preview.frame++;
      last = now;
      if (preview.frame >= frames.length - 1) { preview.playing = false; clearInterval(preview.timer); }
      decorate();
    }, 33);
  }
  decorate();
}

function barHtml() {
  const scenes = preview.sources?.scenes ?? [], saved = localStorage.getItem('btsmtl.run-preview.scene');
  const frames = preview.route?.frames ?? [], frame = currentFrame();
  return `<div class="run-preview-bar"><strong>运行预览</strong><span class="run-preview-mode" title="快速档未提供：网页只回放宿主封存的真实运行">真实档</span>
    <select data-run-preview-scene title="运行使用的离线包">${scenes.map(scene => `<option value="${escapeHtml(scene.path)}" ${scene.path === saved ? 'selected' : ''}>${escapeHtml(scene.path.split('/')[0])}</option>`).join('')}</select>
    <button data-run-preview-start title="用本根输入方案在所选离线包新建真实运行">按输入方案运行</button>
    <select data-run-preview-run title="已封存运行"><option value="">选择已有运行</option>${preview.runs.map(run => `<option value="${run.jobId}" ${run.jobId === preview.runId ? 'selected' : ''}>${escapeHtml(run.acceptedAt.slice(5, 19).replace('T', ' '))} · ${run.jobId.slice(0, 8)}</option>`).join('')}</select>
    <button data-run-preview-step="-1" ${frames.length ? '' : 'disabled'}>◀</button><button data-run-preview-play ${frames.length ? '' : 'disabled'}>${preview.playing ? '暂停' : '播放'}</button><button data-run-preview-step="1" ${frames.length ? '' : 'disabled'}>▶</button>
    <input type="range" data-run-preview-frame min="0" max="${Math.max(0, frames.length - 1)}" value="${preview.frame}" ${frames.length ? '' : 'disabled'}>
    <span data-run-preview-time>${frame ? `${frame.seconds.toFixed(3)} 秒 · 帧 ${escapeHtml(frame.hostFrame)}` : '无运行'}</span>
    <small data-run-preview-status>${escapeHtml(preview.status)}</small></div>`;
}

function bindBar(bar) {
  const guarded = action => async () => { try { await action(); } catch (error) { report(error.message); } finally { mount(); } };
  bar.querySelector('[data-run-preview-start]').onclick = guarded(() => startRun(bar.querySelector('[data-run-preview-scene]').value));
  bar.querySelector('[data-run-preview-run]').onchange = event => guarded(() => loadRoute(event.target.value))();
  bar.querySelector('[data-run-preview-play]').onclick = togglePlay;
  bar.querySelectorAll('[data-run-preview-step]').forEach(button => { button.onclick = () => setFrame(preview.frame + Number(button.dataset.runPreviewStep)); });
  bar.querySelector('[data-run-preview-frame]').oninput = event => { preview.playing = false; clearInterval(preview.timer); setFrame(Number(event.target.value)); };
}

// 输入方案组头：只读显示输入 Timeline，按本 Timeline 在运行中的实际起点对齐。
function inputRows(context, start) {
  const plan = preview.plans.get(`ability:${context.root.displayName}`);
  if (!plan?.timeline) return '';
  const pixels = Number(context.view.querySelector('.timeline-content').dataset.pixels);
  const local = raw => Number(raw) / plan.timeScale - start;
  const width = context.view.querySelector('.timeline-lane')?.style.width ?? '900px';
  const rows = plan.timeline.tracks.map(track => `<div class="timeline-row run-preview-input"><div class="timeline-track"><small>预览输入 · ${escapeHtml(plan.schemeName)}</small><strong>${escapeHtml(track.displayName)}</strong><span>${track.clips.length} 片段 · ${track.markers.length} 标记</span></div><div class="timeline-lane" style="width:${width}">${track.clips.map(clip => {
    const from = local(clip.timeRange.startRaw), to = local(clip.timeRange.endRaw);
    return to < 0 ? '' : `<span class="timeline-clip" style="left:${Math.max(0, from) * pixels}px;width:${Math.max(4, (to - Math.max(0, from)) * pixels)}px" title="${escapeHtml(clip.displayName)}"><span>${escapeHtml(clip.displayName)}</span></span>`;
  }).join('')}${track.markers.map(marker => local(marker.timeRaw) < 0 ? '' : `<span class="timeline-marker" style="left:${local(marker.timeRaw) * pixels}px" title="${escapeHtml(marker.displayName)}">◆</span>`).join('')}</div></div>`).join('');
  return `<div class="run-preview-inputs" title="${start ? '已按运行记录中本 Timeline 的起播时刻对齐' : '未选择运行，按方案零点显示'}">${rows}</div>`;
}

function timelineStart(documentId) {
  const frame = preview.route?.frames.find(item => item.playbacks.some(playback => playback.documentId === documentId));
  return frame ? frame.seconds - frame.playbacks.find(playback => playback.documentId === documentId).cursorSeconds : 0;
}

async function nodeValues(context) {
  const panel = context.inspector.querySelector('.run-preview-values') ?? context.inspector.appendChild(document.createElement('section'));
  panel.className = 'run-preview-values';
  const frame = currentFrame();
  if (!frame || !context.selectedNode) { panel.innerHTML = frame ? '' : '<h3>运行值</h3><p class="muted">选择运行后显示本帧值。</p>'; return; }
  const index = preview.frame;
  if (!preview.frameRecords.has(index)) {
    panel.innerHTML = '<h3>运行值</h3><p class="muted">正在读取本帧记录…</p>';
    const page = await request(`/api/preview/runs/${preview.runId}/frames?offset=${index}&limit=1`);
    preview.frameRecords.set(index, page.items[0]);
    if (index !== preview.frame) return;
  }
  const records = preview.frameRecords.get(index).sections.flatMap(section => section.records.map(record => ({ section: section.displayName, record })))
    .filter(entry => entry.record.locations.some(location => location.elementId === context.selectedNode ||
      (location.documentId === context.document.id && entry.section.includes('黑板'))));
  panel.innerHTML = `<h3>运行值 · 帧 ${escapeHtml(frame.hostFrame)}</h3>${records.length ? records.slice(0, 40).map(({ section, record }) =>
    `<details><summary>${escapeHtml(section)} · ${escapeHtml(record.displayName)}</summary>${record.fields.map(field =>
      `<div class="field"><label>${escapeHtml(field.displayName)}</label><code>${escapeHtml(field.value)}</code></div>`).join('')}</details>`).join('')
    : '<p class="muted">本帧没有该节点的执行或黑板记录。</p>'}`;
}

function decorate() {
  const context = preview.context;
  if (!context?.view.isConnected) return;
  const bar = context.view.querySelector('.run-preview-bar');
  const frame = currentFrame();
  if (bar) {
    bar.querySelector('[data-run-preview-frame]').value = preview.frame;
    bar.querySelector('[data-run-preview-play]').textContent = preview.playing ? '暂停' : '播放';
    bar.querySelector('[data-run-preview-time]').textContent = frame ? `${frame.seconds.toFixed(3)} 秒 · 帧 ${frame.hostFrame}` : '无运行';
  }
  if (context.kind === 'timeline') {
    const content = context.view.querySelector('.timeline-content');
    const pixels = Number(content.dataset.pixels);
    const playback = frame?.playbacks.find(item => item.documentId === context.document.id);
    let cursor = content.querySelector('.run-preview-cursor');
    if (!cursor) { cursor = document.createElement('i'); cursor.className = 'run-preview-cursor'; content.appendChild(cursor); }
    cursor.hidden = !playback;
    if (playback) { cursor.style.left = `${224 + playback.cursorSeconds * pixels}px`; cursor.title = `${playback.cursorSeconds.toFixed(3)} 秒 · ${playback.status}`; }
    report(!frame ? preview.status : playback ? `本 Timeline 游标 ${playback.cursorSeconds.toFixed(3)} 秒 · ${playback.status}`
      : frame.playbacks.length ? `本帧播放的是其它 Timeline（${frame.playbacks.length} 个）` : '本帧没有活动 Timeline');
  }
  else if (context.kind === 'graph') {
    const active = new Set(frame?.active.filter(location => location.documentId === context.document.id).map(location => location.elementId) ?? []);
    context.view.querySelectorAll('.graph-node[data-node]').forEach(node => node.classList.toggle('route-active', active.has(node.dataset.node)));
    if (!preview.playing) nodeValues(context).catch(error => report(error.message));
  }
}

async function mount() {
  const context = preview.context;
  if (!context?.view.isConnected) return;
  context.view.querySelector('.run-preview-bar')?.remove();
  context.view.insertAdjacentHTML('afterbegin', barHtml());
  bindBar(context.view.querySelector('.run-preview-bar'));
  if (context.kind === 'timeline') {
    context.view.querySelector('.run-preview-inputs')?.remove();
    await inputPlan(context.root);
    const ruler = context.view.querySelector('.timeline-ruler');
    if (ruler && context.view === preview.context?.view) ruler.insertAdjacentHTML('afterend', inputRows(context, timelineStart(context.document.id)));
  }
  decorate();
}

export function mountRunPreview(context) {
  if (context.root.kind === 'TimelinePreviewInputSchemes' || !['timeline', 'graph'].includes(context.kind)) return;
  preview.context = context;
  const ready = preview.loading ??= ensureHost().then(() => preview.runId && !preview.route ? loadRoute(preview.runId) : null)
    .catch(error => { preview.runId = ''; report(error.message); });
  ready.then(() => { if (preview.host?.available) mount(); });
}
