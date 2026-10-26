const $ = id => document.getElementById(id);
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const state = { roots: [], nextOffset: 0, selectedRoot: null, snapshot: null, selectedFile: null, selectedNode: null, tab: 'document', query: '', view: { x: 35, y: 45, scale: 1 } };
const kindNames = { Graph: '图', Timeline: '时间轴', InputPlan: '输入方案', Layout: '布局' };

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
  const page = await request(`/api/workspace/roots?offset=${state.nextOffset}&limit=100`);
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
    $('document-title').textContent = entry.displayName;
    $('document-view').innerHTML = `<div class="notice error"><h2>作者文件需要修正</h2>${entry.problems.map(problem => `<p>${escapeHtml(problem.message)}</p><code>${escapeHtml(problem.path)}</code>`).join('')}</div>`;
    report('原文件已保留，未接收为可编译内容。', true);
    return;
  }
  report('正在读取正式文件…');
  const snapshot = await request(`/api/workspace/root?path=${encodeURIComponent(path)}`);
  const root = JSON.parse(snapshot.root);
  state.selectedRoot = entry;
  state.snapshot = { ...snapshot, root };
  state.selectedNode = null;
  state.selectedFile = snapshot.files[0];
  $('document-title').textContent = root.displayName;
  $('root-kind').textContent = `${root.domain} · ${root.kind}`;
  $('revision').textContent = snapshot.revision.slice(0, 12);
  $('revision').title = snapshot.revision;
  $('saved-state').textContent = '已读取磁盘文件';
  $('document-actions').hidden = false;
  renderRoots();
  renderFiles();
  selectFile(state.selectedFile.path);
  report('文件已读取。当前尚未完成领域接收，未编译、发布或运行。');
}

function renderFiles() {
  const root = state.snapshot.root;
  const files = state.snapshot.files.map((file, index) => ({ ...file, declaration: index === 0 ? null : root.files[index - 1] }));
  $('files').innerHTML = files.map(file => `<button class="file-item ${state.selectedFile?.path === file.path ? 'selected' : ''}" data-file="${escapeHtml(file.path)}"><span>${file.declaration?.kind === 'Graph' ? '⌘' : file.declaration?.kind === 'Timeline' ? '◷' : '▤'}</span><span>${escapeHtml(file.path)}<small>${escapeHtml(file.declaration ? kindNames[file.declaration.kind] : '内容根')}</small></span></button>`).join('');
}

function selectFile(path) {
  state.selectedFile = state.snapshot.files.find(file => file.path === path);
  state.selectedNode = null;
  state.view = { x: 35, y: 45, scale: 1 };
  renderFiles();
  renderDocument();
}

function renderDocument() {
  if (!state.snapshot) return;
  const file = state.selectedFile;
  $('file-path').textContent = file.path;
  $('file-revision').textContent = file.revision.slice(0, 12);
  $('source-text').value = file.content;
  let document;
  try { document = JSON.parse(file.content); }
  catch (error) {
    $('document-view').innerHTML = `<div class="notice error"><h2>文档格式无效</h2><p>${escapeHtml(error.message)}</p><p>原始内容保留在“源文件”页，未被旧缓存覆盖。</p></div>`;
    $('inspector-content').innerHTML = '';
    return;
  }
  if (Array.isArray(document.nodes) && Array.isArray(document.edges)) renderGraph(document);
  else renderRoot(document);
  renderInspector(document);
}

function renderRoot(document) {
  const root = state.snapshot.root;
  $('document-view').innerHTML = `<div class="overview"><div class="eyebrow">AUTHORING DOCUMENT</div><h1>${escapeHtml(document.displayName || root.displayName)}</h1><p class="intro">编辑来源保存在项目作者目录。各阶段分别记录实际完成的修订。</p><div class="phase-grid"><div><span class="phase-state saved">已读取</span><strong>磁盘内容</strong><small>${state.snapshot.files.length} 个文件</small></div><div><span class="phase-state pending">未完成</span><strong>领域接收</strong><small>尚未接入完整作者定义</small></div><div><span class="phase-state pending">未执行</span><strong>编译与发布</strong><small>没有本修订的正式产物</small></div><div><span class="phase-state pending">未准备</span><strong>运行预览</strong><small>独立会话尚未采用</small></div></div><h2>引用与归属</h2><dl><dt>稳定身份</dt><dd><code>${escapeHtml(document.id)}</code></dd><dt>定义版本</dt><dd>${escapeHtml(document.definitionVersion)}</dd><dt>外部引用</dt><dd>${root.externalReferences.length} 项</dd><dt>Unity 对象映射</dt><dd>${root.unityOutputs.length} 项</dd></dl><div class="notice">当前读取入口已接通。创建、业务属性修改及保存需共同领域接收完成后开放。</div></div>`;
}

function renderGraph(graph) {
  const layoutFile = state.snapshot.files.find(file => state.snapshot.root.files.find(declaration => declaration.path === file.path)?.kind === 'Layout');
  const layout = layoutFile ? JSON.parse(layoutFile.content) : { elements: [] };
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
    return `<button class="graph-node ${state.selectedNode === node.id ? 'selected' : ''}" data-node="${escapeHtml(node.id)}" style="left:${position.x}px;top:${position.y}px"><span class="node-type">${escapeHtml(node.capability)}</span><strong>${escapeHtml(node.displayName)}</strong><small>${fieldCount} 个作者字段</small><i class="port in"></i><i class="port out"></i></button>`;
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
  const target = state.selectedNode ? document.nodes.find(node => node.id === state.selectedNode) : document;
  $('selection-name').textContent = target.displayName || target.id;
  $('inspector-content').innerHTML = `<div class="identity">${escapeHtml(target.id)}</div><div class="read-state">磁盘作者值</div>${Object.entries(target.fields || {}).map(([id, value]) => `<div class="field"><label>${escapeHtml(id)}</label>${renderValue(value)}</div>`).join('') || '<p class="muted">此对象没有显式作者字段。</p>'}`;
}

function renderValue(value) {
  switch (value.kind) {
    case 'scalar': return `<div class="field-value">${escapeHtml(value.value)}</div><small>${escapeHtml(value.type)} · ${escapeHtml(value.valueKind)}</small>`;
    case 'null': return `<div class="field-value null">null</div><small>${escapeHtml(value.type)}</small>`;
    case 'reference': return `<div class="reference-value"><strong>${escapeHtml(value.target.rootId)}</strong><code>${escapeHtml(value.target.documentId)} / ${escapeHtml(value.target.elementId)}</code></div>`;
    case 'object': return `<details open><summary>${escapeHtml(value.type)}</summary>${Object.entries(value.members).map(([key, member]) => `<div class="nested-value"><label>${escapeHtml(key)}</label>${renderValue(member)}</div>`).join('')}</details>`;
    case 'sequence': return `<details><summary>${value.items.length} 项 · ${escapeHtml(value.type)}</summary>${value.items.map((item, index) => `<div class="nested-value"><label>${index}</label>${renderValue(item)}</div>`).join('')}</details>`;
    default: return `<pre class="error">无法识别作者值 ${escapeHtml(value.kind)}</pre>`;
  }
}

async function guarded(action) {
  try { await action(); }
  catch (error) { report(error.message, true); }
}

$('roots').onclick = event => { const item = event.target.closest('[data-root]'); if (item) guarded(() => openRoot(item.dataset.root)); };
$('files').onclick = event => { const item = event.target.closest('[data-file]'); if (item) guarded(() => selectFile(item.dataset.file)); };
$('document-view').onclick = event => {
  const node = event.target.closest('[data-node]');
  if (!node) return;
  state.selectedNode = node.dataset.node;
  renderDocument();
};
$('search-roots').oninput = event => { state.query = event.target.value.trim().toLowerCase(); renderRoots(); };
$('refresh-roots').onclick = () => guarded(() => loadRoots(true));
$('load-more').onclick = () => guarded(() => loadRoots());
$('reload-root').onclick = () => guarded(() => openRoot(state.selectedRoot.path));
document.querySelectorAll('[data-tab]').forEach(button => button.onclick = () => {
  state.tab = button.dataset.tab;
  document.querySelectorAll('[data-tab]').forEach(item => item.classList.toggle('active', item === button));
  $('source-view').hidden = state.tab !== 'source';
  $('document-view').hidden = state.tab !== 'document';
});
guarded(async () => {
  const info = await request('/api/workspace');
  $('service-state').textContent = '服务已连接';
  $('workspace-path').textContent = info.path;
  await loadRoots(true);
  report('请选择作者内容根。旧原型草稿仍保留在浏览器，未作为正式内容读取。');
});
