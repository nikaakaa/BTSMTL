const $ = id => document.getElementById(id);
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const state = { roots: [], nextOffset: 0, selectedRoot: null, snapshot: null, selectedFile: null, selectedNode: null, selectedTimeline: null, tab: 'document', source: 'workspace', timeScale: null, query: '', view: { x: 35, y: 45, scale: 1 } };
const kindNames = { Graph: '图', Timeline: '时间轴', InputPlan: '输入方案', Layout: '布局' };
const api = () => `/api/${state.source}`;
const typeName = value => value.split(',')[0].split('.').pop();

async function request(path) {
  const response = await fetch(path);
  const text = await response.text();
  if (!response.ok) throw new Error(`服务返回 ${response.status}：${text}`);
  return JSON.parse(text);
}

function report(message, error = false) {
  $('status-message').textContent = message;
  $('status-message').classList.toggle('error', error);
}

async function loadRoots(reset = false) {
  if (reset) { state.roots = []; state.nextOffset = 0; }
  const page = await request(`${api()}/roots?offset=${state.nextOffset}&limit=100`);
  state.roots.push(...page.items);
  state.nextOffset = page.nextOffset;
  $('root-count').textContent = `${page.total} 个内容根`;
  $('load-more').hidden = page.nextOffset === null;
  renderRoots();
}

function renderRoots() {
  const roots = state.roots.filter(root => `${root.displayName} ${root.id} ${root.kind}`.toLowerCase().includes(state.query));
  $('roots').innerHTML = roots.map(root => `<button class="root-item ${state.selectedRoot?.path === root.path ? 'selected' : ''}" data-root="${escapeHtml(root.path)}"><span class="root-symbol">${root.problems.length ? '!' : '◇'}</span><span><strong>${escapeHtml(root.displayName)}</strong><small>${escapeHtml(root.kind || root.path)}</small></span>${root.problems.length ? '<i class="error-dot"></i>' : ''}</button>`).join('');
  $('empty-roots').hidden = state.roots.length !== 0;
}

async function openRoot(path) {
  const entry = state.roots.find(root => root.path === path);
  if (entry.problems.length) {
    state.selectedRoot = entry;
    state.snapshot = null;
    state.selectedFile = null;
    state.selectedNode = null;
    $('document-title').textContent = entry.displayName;
    $('root-kind').textContent = '外部文件尚未接收';
    $('files').innerHTML = '';
    $('file-path').textContent = entry.path;
    $('file-revision').textContent = '';
    $('selection-name').textContent = '无法解析对象';
    $('inspector-content').innerHTML = '';
    $('document-actions').hidden = true;
    $('source-text').value = '';
    $('document-view').innerHTML = `<div class="notice error"><h2>作者文件需要修正</h2>${entry.problems.map(problem => `<p>${escapeHtml(problem.message)}</p><code>${escapeHtml(problem.path)}</code>`).join('')}</div>`;
    renderRoots();
    const source = await request(`${api()}/source?path=${encodeURIComponent(entry.path)}`);
    $('source-text').value = source.text;
    $('file-revision').textContent = source.revision.slice(0, 12);
    report('原文件已保留，未接收为可编译内容。', true);
    return;
  }
  report('正在读取正式文件…');
  const snapshot = await request(`${api()}/root?path=${encodeURIComponent(path)}`);
  const root = JSON.parse(snapshot.root);
  state.selectedRoot = entry;
  const files = snapshot.files.map(file => {
    try { return { ...file, document: JSON.parse(file.content), problem: null }; }
    catch (error) { return { ...file, document: null, problem: error.message }; }
  });
  state.snapshot = { ...snapshot, root, files };
  state.selectedNode = null;
  state.selectedFile = files[0];
  $('document-title').textContent = root.displayName;
  $('root-kind').textContent = `${root.domain} · ${root.kind}`;
  $('revision').textContent = snapshot.revision.slice(0, 12);
  $('revision').title = snapshot.revision;
  $('saved-state').textContent = state.source === 'migrations' ? '迁移候选 · 尚未切换正式来源' : '已读取的文件版本';
  $('document-actions').hidden = false;
  renderRoots();
  renderFiles();
  selectFile(state.selectedFile.path);
  report(state.source === 'migrations' ? '正在查看从原资产读取的候选。当前修改仍由原 Unity 作者资产持有。' : '文件已读取。当前尚未完成领域接收，未编译、发布或运行。');
}

function renderFiles() {
  const root = state.snapshot.root;
  const files = state.snapshot.files.map((file, index) => ({ ...file, declaration: index === 0 ? null : root.files[index - 1] }));
  const query = $('search-files').value.trim().toLowerCase();
  $('files').innerHTML = files.filter(file => `${file.document?.displayName} ${file.document?.role} ${file.path}`.toLowerCase().includes(query)).map(file => `<button class="file-item ${state.selectedFile?.path === file.path ? 'selected' : ''}" data-file="${escapeHtml(file.path)}" title="${escapeHtml(file.path)}"><span>${file.problem ? '!' : file.declaration?.kind === 'Graph' ? '⌘' : file.declaration?.kind === 'Timeline' ? '◷' : '▤'}</span><span>${escapeHtml(file.document?.displayName || file.path)}<small>${escapeHtml(file.problem ? '文件格式错误' : file.document?.role || (file.declaration ? kindNames[file.declaration.kind] : '内容根'))}</small></span></button>`).join('');
}

function selectFile(path) {
  state.selectedFile = state.snapshot.files.find(file => file.path === path);
  state.selectedNode = null;
  state.selectedTimeline = null;
  state.view = { x: 35, y: 45, scale: 1 };
  const locationQuery = new URLSearchParams({ source: state.source, root: state.selectedRoot.path, file: path, tab: state.tab });
  history.replaceState(null, '', `?${locationQuery}`);
  renderFiles();
  renderDocument();
}

function renderDocument() {
  if (!state.snapshot) return;
  const file = state.selectedFile;
  $('file-path').textContent = file.path;
  $('file-revision').textContent = file.revision.slice(0, 12);
  $('source-text').value = file.content;
  try {
    if (file.problem) throw new Error(file.problem);
    const document = file.document;
    if (state.tab === 'variables') renderVariables();
    else if (Array.isArray(document.nodes) && Array.isArray(document.edges)) renderGraph(document);
    else if (Array.isArray(document.tracks)) renderTimeline(document);
    else renderRoot(document);
    renderInspector(document);
  }
  catch (error) {
    $('document-view').innerHTML = `<div class="notice error"><h2>文档格式无效</h2><p>${escapeHtml(error.message)}</p><p>原始内容保留在“源文件”页，未被旧缓存覆盖。</p></div>`;
    $('inspector-content').innerHTML = '';
    return;
  }
}

function renderRoot(document) {
  const root = state.snapshot.root;
  $('document-view').innerHTML = `<div class="overview"><div class="eyebrow">AUTHORING DOCUMENT</div><h1>${escapeHtml(document.displayName || root.displayName)}</h1><p class="intro">编辑来源保存在项目作者目录。各阶段分别记录实际完成的修订。</p><div class="phase-grid"><div><span class="phase-state saved">已读取</span><strong>磁盘内容</strong><small>${state.snapshot.files.length} 个文件</small></div><div><span class="phase-state pending">未完成</span><strong>领域接收</strong><small>尚未接入完整作者定义</small></div><div><span class="phase-state pending">未执行</span><strong>编译与发布</strong><small>没有本修订的正式产物</small></div><div><span class="phase-state pending">未准备</span><strong>运行预览</strong><small>独立会话尚未采用</small></div></div><h2>引用与归属</h2><dl><dt>稳定身份</dt><dd><code>${escapeHtml(document.id)}</code></dd><dt>定义版本</dt><dd>${escapeHtml(document.definitionVersion)}</dd><dt>外部引用</dt><dd>${root.externalReferences.length} 项</dd><dt>Unity 对象映射</dt><dd>${root.unityOutputs.length} 项</dd></dl><div class="notice">当前读取入口已接通。创建、业务属性修改及保存需共同领域接收完成后开放。</div></div>`;
}

function renderGraph(graph) {
  const layoutFile = state.snapshot.files.find(file => state.snapshot.root.files.find(declaration => declaration.path === file.path)?.kind === 'Layout');
  const layout = layoutFile ? layoutFile.document : { elements: [] };
  const positions = new Map();
  graph.nodes.forEach((node, index) => {
    const saved = layout.elements?.find(element => element.id === node.id);
    positions.set(node.id, saved ? { x: saved.x, y: saved.y } : { x: (index % 4) * 270, y: Math.floor(index / 4) * 155 });
  });
  const links = graph.edges.map(edge => {
    const source = positions.get(edge.sourceNode), target = positions.get(edge.targetNode);
    if (!source || !target) return '';
    const x1 = source.x + 214, y1 = source.y + 53, x2 = target.x, y2 = target.y + 53;
    return `<g><path d="M${x1},${y1} C${x1 + 80},${y1} ${x2 - 80},${y2} ${x2},${y2}"/><title>${escapeHtml(edge.id)} · ${escapeHtml(edge.sourcePort)} → ${escapeHtml(edge.targetPort)} · 顺序 ${edge.order}</title></g>`;
  }).join('');
  const nodes = graph.nodes.map(node => {
    const position = positions.get(node.id);
    const fieldCount = Object.keys(node.fields).length;
    return `<button class="graph-node ${state.selectedNode === node.id ? 'selected' : ''}" data-node="${escapeHtml(node.id)}" style="left:${escapeHtml(position.x)}px;top:${escapeHtml(position.y)}px"><span class="node-type">${escapeHtml(node.capability)}</span><strong>${escapeHtml(node.displayName)}</strong><small>${fieldCount} 个作者字段</small><i class="port in"></i><i class="port out"></i></button>`;
  }).join('');
  $('document-view').innerHTML = `<div class="graph-surface" id="graph-surface"><div class="graph-caption">${escapeHtml(graph.role)}<span>${graph.nodes.length} 个节点 · ${graph.edges.length} 条连接</span></div><div class="graph-world" id="graph-world"><svg width="6000" height="4000">${links}</svg>${nodes}</div><div class="canvas-tools"><button id="fit-graph">复位视图</button><span id="zoom-value">100%</span></div><div class="canvas-hint">空白处拖动画布 · 滚轮缩放 · 选择节点查看实际字段</div></div>`;
  updateView();
  const surface = $('graph-surface');
  let drag = null;
  surface.addEventListener('pointerdown', event => {
    if (event.target.closest('button')) return;
    drag = { x: event.clientX - state.view.x, y: event.clientY - state.view.y };
    surface.setPointerCapture(event.pointerId);
  });
  surface.addEventListener('pointermove', event => {
    if (!drag) return;
    state.view.x = event.clientX - drag.x;
    state.view.y = event.clientY - drag.y;
    updateView();
  });
  surface.addEventListener('pointerup', () => { drag = null; });
  surface.addEventListener('wheel', event => {
    event.preventDefault();
    const bounds = surface.getBoundingClientRect();
    const old = state.view.scale, next = Math.min(2.2, Math.max(0.25, old * Math.exp(-event.deltaY * 0.001)));
    state.view.x = event.clientX - bounds.left - (event.clientX - bounds.left - state.view.x) * next / old;
    state.view.y = event.clientY - bounds.top - (event.clientY - bounds.top - state.view.y) * next / old;
    state.view.scale = next;
    updateView();
  }, { passive: false });
  $('fit-graph').onclick = () => { state.view = { x: 35, y: 45, scale: 1 }; updateView(); };
}

function updateView() {
  $('graph-world').style.transform = `translate(${state.view.x}px,${state.view.y}px) scale(${state.view.scale})`;
  $('zoom-value').textContent = `${Math.round(state.view.scale * 100)}%`;
}

function renderInspector(document) {
  const target = state.selectedTimeline
    ? document.tracks.flatMap(track => [track, ...track.clips, ...track.markers]).find(item => item.id === state.selectedTimeline)
    : state.selectedNode ? document.nodes.find(node => node.id === state.selectedNode) : document;
  $('selection-name').textContent = target.displayName || target.id;
  $('inspector-content').innerHTML = `<div class="identity">${escapeHtml(target.id)}</div><div class="read-state">${state.source === 'migrations' ? '迁移候选中的实际值' : '磁盘作者值'}</div>${target.timeRange ? `<div class="time-details"><strong>${seconds(target.timeRange.startRaw).toFixed(4)} ～ ${seconds(target.timeRange.endRaw).toFixed(4)} 秒</strong><details><summary>无损时间与素材范围</summary>${Object.entries(target.timeRange).map(([key, value]) => `<div><label>${escapeHtml(key)}</label><code>${escapeHtml(value)}</code></div>`).join('')}</details></div>` : ''}${Object.entries(target.fields || {}).map(([id, value]) => `<div class="field"><label>${escapeHtml(id)}</label>${renderValue(value)}</div>`).join('') || '<p class="muted">此对象没有显式作者字段。</p>'}`;
}

function renderValue(value) {
  switch (value.kind) {
    case 'scalar': return `<div class="field-value">${escapeHtml(value.value)}</div><small title="${escapeHtml(value.type)}">${escapeHtml(typeName(value.type))} · ${escapeHtml(value.valueKind)}</small>`;
    case 'null': return `<div class="field-value null">null</div><small>${escapeHtml(value.type)}</small>`;
    case 'reference': {
      const target = state.snapshot.root.files.find(file => file.id === value.target.documentId);
      const mapping = state.snapshot.root.unityOutputs.find(item => item.documentId === value.target.documentId && item.elementId === value.target.elementId);
      return `<div class="reference-value">${target && value.target.rootId === state.snapshot.root.id ? `<button data-document="${escapeHtml(target.path)}">打开 ${escapeHtml(kindNames[target.kind])} ↗</button>` : '<strong>只读资源引用</strong>'}<span>${escapeHtml(mapping?.assetPath || value.target.rootId)}</span><details><summary>稳定引用</summary><code>${escapeHtml(value.target.rootId)} / ${escapeHtml(value.target.documentId)} / ${escapeHtml(value.target.elementId)}</code></details></div>`;
    }
    case 'object': return `<details><summary title="${escapeHtml(value.type)}">${escapeHtml(typeName(value.type))}</summary>${Object.entries(value.members).map(([key, member]) => `<div class="nested-value"><label>${escapeHtml(key)}</label>${renderValue(member)}</div>`).join('')}</details>`;
    case 'sequence': return `<details><summary>${value.items.length} 项 · ${escapeHtml(typeName(value.type))}</summary>${value.items.map((item, index) => `<div class="nested-value"><label>${index + 1}</label>${renderValue(item)}</div>`).join('')}</details>`;
    default: return `<pre class="error">无法识别作者值 ${escapeHtml(value.kind)}</pre>`;
  }
}

function seconds(raw) {
  const value = BigInt(raw);
  return Number(value / state.timeScale) + Number(value % state.timeScale) / Number(state.timeScale);
}

function renderTimeline(timeline) {
  const times = timeline.tracks.flatMap(track => [...track.clips.map(clip => seconds(clip.timeRange.endRaw)), ...track.markers.map(marker => seconds(marker.timeRaw))]);
  const duration = Math.max(1, ...times), width = Math.max(900, duration * 120), pixels = width / duration;
  const tick = duration > 20 ? Math.ceil(duration / 20) : duration > 5 ? 1 : 0.25;
  const ticks = Array.from({ length: Math.floor(duration / tick) + 1 }, (_, index) => index * tick);
  const rows = timeline.tracks.map((track, index) => `<div class="timeline-row"><button class="timeline-track ${state.selectedTimeline === track.id ? 'selected' : ''}" data-timeline="${escapeHtml(track.id)}"><small>${index + 1} · ${escapeHtml(track.contract)}</small><strong>${escapeHtml(track.displayName)}</strong><span>${track.clips.length} 片段 · ${track.markers.length} 标记</span></button><div class="timeline-lane" style="width:${width}px">${ticks.map(time => `<i class="timeline-grid" style="left:${time * pixels}px"></i>`).join('')}${track.clips.map(clip => `<button class="timeline-clip ${state.selectedTimeline === clip.id ? 'selected' : ''}" data-timeline="${escapeHtml(clip.id)}" style="left:${seconds(clip.timeRange.startRaw) * pixels}px;width:${Math.max(4, (seconds(clip.timeRange.endRaw) - seconds(clip.timeRange.startRaw)) * pixels)}px" title="${escapeHtml(clip.displayName)} · ${seconds(clip.timeRange.startRaw).toFixed(4)}—${seconds(clip.timeRange.endRaw).toFixed(4)} 秒"><span>${escapeHtml(clip.displayName)}</span><small>${escapeHtml(clip.contract)}</small></button>`).join('')}${track.markers.map(marker => `<button class="timeline-marker ${state.selectedTimeline === marker.id ? 'selected' : ''}" data-timeline="${escapeHtml(marker.id)}" style="left:${seconds(marker.timeRaw) * pixels}px" title="标记 · ${seconds(marker.timeRaw).toFixed(4)} 秒">◆</button>`).join('')}</div></div>`).join('');
  $('document-view').innerHTML = `<div class="timeline-heading"><div><small>正式时间数据</small><h2>${escapeHtml(timeline.displayName)}</h2></div><span>${timeline.loop ? '循环' : '单次'} · ${duration.toFixed(3)} 秒 · ${timeline.tracks.length} 条轨道</span></div><div class="timeline-scroll"><div class="timeline-content" style="width:${width + 224}px"><div class="timeline-ruler"><div>作者时间 · 秒</div><div style="width:${width}px">${ticks.map(time => `<span style="left:${time * pixels}px">${time.toFixed(2)}</span>`).join('')}</div></div>${rows}</div></div><p class="timeline-note">选择片段或标记查看实际参数及图引用。时间轴显示作者时间；真实模拟结果位于独立 Quantum 预览。</p>`;
}

function renderVariables() {
  const graphs = state.snapshot.root.files.filter(file => file.kind === 'Graph').map(file => ({
    path: file.path, document: state.snapshot.files.find(source => source.path === file.path).document
  }));
  const variables = graphs.flatMap(graph => graph.document.variables.map(variable => ({ ...variable, graph })));
  $('document-view').innerHTML = `<div class="variables-page"><div class="eyebrow">BLACKBOARD DECLARATIONS</div><h1>变量与归属</h1><p>${variables.length} 条实际声明。默认值与声明分别显示，所属图保持原 owner。</p><div class="variable-list">${variables.map(variable => `<article><header><strong>${escapeHtml(variable.key)}</strong><span title="${escapeHtml(variable.type)}">${escapeHtml(typeName(variable.type))}</span><button data-document="${escapeHtml(variable.graph.path)}">所属图 ↗</button></header><code>${escapeHtml(variable.id)}</code><div class="variable-columns"><div><h3>默认值</h3>${renderValue(variable.defaultValue)}</div><div><h3>作用域、生命周期与输入声明</h3>${Object.entries(variable.declaration).map(([field, value]) => `<div class="nested-value"><label>${escapeHtml(field)}</label>${renderValue(value)}</div>`).join('')}</div></div></article>`).join('') || '<p class="muted">本内容根没有黑板声明。</p>'}</div></div>`;
}

async function guarded(action) {
  try { await action(); }
  catch (error) { report(error.message, true); }
}

$('roots').onclick = event => { const item = event.target.closest('[data-root]'); if (item) guarded(() => openRoot(item.dataset.root)); };
$('files').onclick = event => { const item = event.target.closest('[data-file]'); if (item) guarded(() => selectFile(item.dataset.file)); };
$('document-view').onclick = event => {
  const reference = event.target.closest('[data-document]');
  if (reference) { state.tab = 'document'; showTab(); selectFile(reference.dataset.document); return; }
  const timeline = event.target.closest('[data-timeline]');
  if (timeline) { state.selectedTimeline = timeline.dataset.timeline; renderDocument(); return; }
  const node = event.target.closest('[data-node]');
  if (!node) return;
  state.selectedNode = node.dataset.node;
  renderDocument();
};
$('inspector-content').onclick = event => { const reference = event.target.closest('[data-document]'); if (reference) { state.tab = 'document'; showTab(); selectFile(reference.dataset.document); } };
$('search-roots').oninput = event => { state.query = event.target.value.trim().toLowerCase(); renderRoots(); };
$('search-files').oninput = renderFiles;
$('refresh-roots').onclick = () => guarded(() => loadRoots(true));
$('load-more').onclick = () => guarded(() => loadRoots());
$('reload-root').onclick = () => guarded(() => openRoot(state.selectedRoot.path));
function showTab() {
  document.querySelectorAll('[data-tab]').forEach(item => item.classList.toggle('active', item.dataset.tab === state.tab));
  $('source-view').hidden = state.tab !== 'source';
  $('document-view').hidden = state.tab === 'source';
}

document.querySelectorAll('[data-tab]').forEach(button => button.onclick = () => {
  state.tab = button.dataset.tab;
  showTab();
  renderDocument();
});
async function selectSource(source) {
  state.source = source;
  $('content-source').value = source;
  const info = await request(api());
  state.timeScale = BigInt(info.timelineTimeScale);
  $('service-state').textContent = '服务已连接';
  $('workspace-path').textContent = info.path;
  state.selectedRoot = null;
  state.snapshot = null;
  state.selectedNode = null;
  state.selectedTimeline = null;
  $('search-files').value = '';
  $('files').innerHTML = '';
  $('document-actions').hidden = true;
  $('inspector-content').innerHTML = '';
  $('document-title').textContent = source === 'migrations' ? '选择迁移候选' : '选择内容根';
  $('document-view').innerHTML = `<div class="welcome"><div class="eyebrow">${source === 'migrations' ? 'MIGRATION REVIEW' : 'AUTHORING DOCUMENTS'}</div><h1>${source === 'migrations' ? '检查原资产的完整内容' : '打开正式作者文档'}</h1><p>${source === 'migrations' ? '候选用于核对迁移读取结果，原 Unity 作者资产仍是当前正式来源。选择左侧内容查看图、状态、时间轴和变量。' : '选择左侧作者根。尚未接收的迁移候选可从“内容来源”切换查看。'}</p></div>`;
  await loadRoots(true);
  report(source === 'migrations' ? '迁移候选只读检查；尚未切换正式来源。' : '请选择作者内容根。');
}

$('content-source').onchange = event => guarded(() => selectSource(event.target.value));

guarded(async () => {
  const query = new URLSearchParams(location.search);
  await selectSource(query.get('source') === 'migrations' ? 'migrations' : 'workspace');
  if (query.has('root')) {
    await openRoot(query.get('root'));
    if (query.get('tab') === 'variables' || query.get('tab') === 'source') { state.tab = query.get('tab'); showTab(); }
    if (query.has('file')) selectFile(query.get('file'));
    else renderDocument();
  }
});
