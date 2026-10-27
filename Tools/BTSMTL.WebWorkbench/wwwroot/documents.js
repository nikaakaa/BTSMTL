import { GraphEditSession, editField, editVariable, renameElement, createNode, createConnection, showGraphDiff } from './graph-edits.js';

const $ = id => document.getElementById(id);
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const state = { roots: [], nextOffset: 0, selectedRoot: null, snapshot: null, selectedFile: null, selectedNode: null, selectedTimeline: null, tab: 'document', source: 'workspace', timeScale: null, query: '', capabilities: new Map(), definitionRevision: null, draft: null, view: { x: 35, y: 45, scale: 1 } };
const kindNames = { Graph: '图', Timeline: '时间轴', InputPlan: '输入方案', Layout: '布局' };
const api = () => `/api/${state.source}`;
const typeName = value => value.split(',')[0].split('.').pop();

async function request(path, body) {
  const response = await fetch(path, body === undefined ? undefined : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`服务返回 ${response.status}：${text}`);
  return JSON.parse(text);
}

function report(message, error = false) {
  $('status-message').textContent = message;
  $('status-message').classList.toggle('error', error);
}

async function loadDefinitions(domain) {
  const capabilities = new Map();
  let offset = 0, revision = null;
  do {
    const page = await request(`/api/authoring/capabilities?offset=${offset}&limit=200`);
    if (page.domain !== domain) throw new Error(`服务的原能力目录属于 ${page.domain}，当前内容属于 ${domain}。`);
    if (revision !== null && revision !== page.revision) throw new Error('读取能力目录期间定义发生变更，请重新读取本根。');
    revision = page.revision;
    page.items.forEach(capability => capabilities.set(capability.id, capability));
    offset = page.nextOffset;
  } while (offset !== null);
  state.capabilities = capabilities;
  state.definitionRevision = revision;
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
  requireDraftSettled();
  const entry = state.roots.find(root => root.path === path);
  if (entry.problems.length) {
    state.selectedRoot = entry;
    state.snapshot = null;
    state.draft = null;
    $('draft-toolbar').hidden = true;
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
  await loadDefinitions(root.domain);
  state.selectedRoot = entry;
  const files = snapshot.files.map(file => {
    try { return { ...file, document: JSON.parse(file.content), problem: null }; }
    catch (error) { return { ...file, document: null, problem: error.message }; }
  });
  state.snapshot = { ...snapshot, root, files };
  state.draft = new GraphEditSession(state.snapshot, path, state.source, state.definitionRevision, request);
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
  await selectFile(state.selectedFile.path);
  report(state.source === 'migrations' ? '正在查看从原资产读取的候选。当前修改仍由原 Unity 作者资产持有。' : '文件已读取。当前尚未完成领域接收，未编译、发布或运行。');
}

function renderFiles() {
  const root = state.snapshot.root;
  const files = state.snapshot.files.map((file, index) => ({ ...file, declaration: index === 0 ? null : root.files[index - 1] }));
  const query = $('search-files').value.trim().toLowerCase();
  $('files').innerHTML = files.filter(file => `${file.document?.displayName} ${file.document?.role} ${file.path}`.toLowerCase().includes(query)).map(file => `<button class="file-item ${state.selectedFile?.path === file.path ? 'selected' : ''}" data-file="${escapeHtml(file.path)}" title="${escapeHtml(file.path)}"><span>${file.problem ? '!' : file.declaration?.kind === 'Graph' ? '⌘' : file.declaration?.kind === 'Timeline' ? '◷' : '▤'}</span><span>${escapeHtml(file.document?.displayName || file.path)}<small>${escapeHtml(file.problem ? '文件格式错误' : file.document?.role || (file.declaration ? kindNames[file.declaration.kind] : '内容根'))}</small></span></button>`).join('');
}

async function selectFile(path) {
  state.selectedFile = state.snapshot.files.find(file => file.path === path);
  const file = state.selectedFile;
  state.selectedNode = null;
  state.selectedTimeline = null;
  state.view = { x: 35, y: 45, scale: 1, needsFit: true };
  const locationQuery = new URLSearchParams({ source: state.source, root: state.selectedRoot.path, file: path, tab: state.tab });
  history.replaceState(null, '', `?${locationQuery}`);
  renderFiles();
  if (!file.problem && Array.isArray(file.document.nodes) && !file.portShapes) {
    $('document-view').innerHTML = '<div class="notice">正在从共同定义投影端口…</div>';
    const projected = await request('/api/authoring/ports', {
      content: file.content, definitionsRevision: state.definitionRevision
    });
    file.portShapes = new Map(projected.nodes.map(node => [node.nodeId, node]));
  }
  if (state.selectedFile !== file) return;
  if (file.document?.role === 'StateMachine' && !file.stateMachineReceipt && !file.stateMachineProblem) {
    try {
      file.stateMachineReceipt = await request('/api/authoring/state-machines/validate', {
        content: file.content, definitionsRevision: state.definitionRevision,
        relatedGraphs: state.snapshot.files.filter(related => related !== file && Array.isArray(related.document?.nodes))
          .map(related => related.content)
      });
    } catch (error) { file.stateMachineProblem = error.message; }
  }
  if (state.selectedFile !== file) return;
  if (!file.problem && Array.isArray(file.document.nodes) && file.document.role !== 'StateMachine' &&
      !file.topologyReceipt && !file.topologyProblem) {
    try {
      file.topologyReceipt = await request('/api/authoring/graphs/validate', {
        content: file.content, definitionsRevision: state.definitionRevision
      });
    } catch (error) { file.topologyProblem = error.message; }
  }
  if (state.selectedFile !== file) return;
  renderDocument();
}

function renderDocument() {
  if (!state.snapshot) return;
  const file = state.selectedFile;
  renderDraftToolbar();
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
  const introduction = state.source === 'migrations'
    ? '该候选从现有 Unity 资产读取，用于核对迁移结果。原资产仍是当前正式作者来源。'
    : '编辑来源保存在项目作者目录。各阶段分别记录实际完成的修订。';
  $('document-view').innerHTML = `<div class="overview"><div class="eyebrow">${state.source === 'migrations' ? 'MIGRATION CANDIDATE' : 'AUTHORING DOCUMENT'}</div><h1>${escapeHtml(document.displayName || root.displayName)}</h1><p class="intro">${introduction}</p><div class="phase-grid"><div><span class="phase-state saved">已读取</span><strong>磁盘内容</strong><small>${state.snapshot.files.length} 个文件</small></div><div><span class="phase-state pending">未完成</span><strong>领域接收</strong><small>尚未接入完整作者定义</small></div><div><span class="phase-state pending">未执行</span><strong>编译与发布</strong><small>没有本修订的正式产物</small></div><div><span class="phase-state pending">未准备</span><strong>运行预览</strong><small>独立会话尚未采用</small></div></div><h2>引用与归属</h2><dl><dt>稳定身份</dt><dd><code>${escapeHtml(document.id)}</code></dd><dt>定义版本</dt><dd>${escapeHtml(document.definitionVersion)}</dd><dt>外部引用</dt><dd>${root.externalReferences.length} 项</dd><dt>Unity 对象映射</dt><dd>${root.unityOutputs.length} 项</dd></dl><div class="notice">打开本根图文件可预览字段、节点和连线修改，撤销重做并查看差异。完整领域接收和正式保存尚未接通，原文件仍是当前修订。</div></div>`;
}

function renderGraph(graph) {
  const layoutFile = state.snapshot.files.find(file => state.snapshot.root.files.find(declaration => declaration.path === file.path)?.kind === 'Layout');
  const layout = layoutFile ? layoutFile.document : { elements: [] };
  const positions = new Map();
  graph.nodes.forEach((node, index) => {
    const saved = layout.elements?.find(element => element.documentId === graph.id && element.id === node.id);
    positions.set(node.id, saved ? { x: saved.x, y: saved.y } : { x: (index % 4) * 270, y: Math.floor(index / 4) * 155 });
  });
  const shapes = new Map(graph.nodes.map(node => {
    const definition = state.capabilities.get(node.capability);
    const projection = state.selectedFile.portShapes.get(node.id);
    const ports = projection.ports;
    return [node.id, { definition, problem: projection.problem, inputs: ports.filter(port => port.direction === 'Input'),
      outputs: ports.filter(port => port.direction === 'Output') }];
  }));
  const links = graph.edges.map(edge => {
    const source = positions.get(edge.sourceNode), target = positions.get(edge.targetNode);
    if (!source || !target) return '';
    const output = shapes.get(edge.sourceNode).outputs.findIndex(port => port.id === edge.sourcePort);
    const input = shapes.get(edge.targetNode).inputs.findIndex(port => port.id === edge.targetPort);
    if (output < 0 || input < 0) return `<text class="port-problem" x="${source.x + 8}" y="${source.y - 10}">${escapeHtml(edge.id)}：端口描述缺失</text>`;
    const x1 = source.x + 250, y1 = source.y + 85 + output * 25, x2 = target.x, y2 = target.y + 85 + input * 25;
    return `<g><path d="M${x1},${y1} C${x1 + 80},${y1} ${x2 - 80},${y2} ${x2},${y2}"/><title>${escapeHtml(edge.id)} · ${escapeHtml(edge.sourcePort)} → ${escapeHtml(edge.targetPort)} · 顺序 ${edge.order}</title></g>`;
  }).join('');
  const nodes = graph.nodes.map(node => {
    const position = positions.get(node.id);
    const fieldCount = Object.keys(node.fields).length;
    const shape = shapes.get(node.id), definition = shape.definition;
    const rows = Math.max(shape.inputs.length, shape.outputs.length);
    const portRows = Array.from({ length: rows }, (_, index) => {
      const input = shape.inputs[index], output = shape.outputs[index];
      const label = (port, side) => port ? `<span class="${side}" title="${escapeHtml(port.id)} · ${escapeHtml(port.valueType)}"><i></i><em>${escapeHtml(port.displayName || port.id)}</em></span>` : '<span></span>';
      return `<div class="node-port-row">${label(input, 'input')}${label(output, 'output')}</div>`;
    }).join('');
    return `<button class="graph-node ${shape.problem ? 'missing-definition' : ''} ${state.selectedNode === node.id ? 'selected' : ''}" data-node="${escapeHtml(node.id)}" title="${escapeHtml(shape.problem ? shape.problem.message : node.id)}" style="left:${escapeHtml(position.x)}px;top:${escapeHtml(position.y)}px"><span class="node-type">${escapeHtml(node.capability)}</span><strong>${escapeHtml(node.displayName || definition?.displayName || node.capability)}</strong><small>${fieldCount} 个作者字段${shape.problem ? ' · ' + escapeHtml(shape.problem.code) : ''}</small><div class="node-ports">${portRows}</div></button>`;
  }).join('');
  $('document-view').innerHTML = `<div class="graph-surface" id="graph-surface"><div class="graph-caption">${escapeHtml(graph.role)}<span>${graph.nodes.length} 个节点 · ${graph.edges.length} 条连接</span></div><div class="graph-world" id="graph-world"><svg width="6000" height="4000">${links}</svg>${nodes}</div><div class="canvas-tools"><button id="fit-graph">适应全图</button><span id="zoom-value">100%</span></div><div class="canvas-hint">空白处拖动画布 · 滚轮缩放 · 选择节点查看实际字段</div></div>`;
  updateView();
  const surface = $('graph-surface');
  const topology = state.selectedFile.topologyReceipt;
  if (topology) {
    surface.dataset.topologyValid = String(topology.valid);
    surface.querySelector('.graph-caption').insertAdjacentHTML('beforeend',
      '<span title="仅检查节点所属页面、固定入口、连线类型、容量与环路；不代表完整技能已接收或发布">图结构已接收</span>');
  }
  if (state.selectedFile.topologyProblem)
    surface.querySelector('.graph-caption').insertAdjacentHTML('beforeend',
      `<span class="error">图结构未接收：${escapeHtml(state.selectedFile.topologyProblem)}</span>`);
  const receipt = state.selectedFile.stateMachineReceipt;
  if (receipt) {
    surface.dataset.stateMachineValid = String(receipt.valid);
    surface.querySelector('.graph-caption').insertAdjacentHTML('beforeend',
      '<span title="仅表示 FSM 合同已接收，完整技能保存与发布尚未接通">FSM 合同已接收</span>');
  }
  if (state.selectedFile.stateMachineProblem)
    surface.querySelector('.graph-caption').insertAdjacentHTML('beforeend',
      `<span class="error">FSM 合同未接收：${escapeHtml(state.selectedFile.stateMachineProblem)}</span>`);
  function fitGraph() {
    if (graph.nodes.length === 0) { state.view = { x: 35, y: 45, scale: 1 }; updateView(); return; }
    const bounds = graph.nodes.map(node => {
      const position = positions.get(node.id), shape = shapes.get(node.id);
      return { ...position, width: 250, height: Math.max(86, 72 + Math.max(shape.inputs.length, shape.outputs.length) * 25) };
    });
    const left = Math.min(...bounds.map(node => node.x)), right = Math.max(...bounds.map(node => node.x + node.width));
    const top = Math.min(...bounds.map(node => node.y)), bottom = Math.max(...bounds.map(node => node.y + node.height));
    const scale = Math.min(1, (surface.clientWidth - 80) / (right - left), (surface.clientHeight - 120) / (bottom - top));
    state.view = { x: (surface.clientWidth - (right - left) * scale) / 2 - left * scale,
      y: (surface.clientHeight - (bottom - top) * scale) / 2 - top * scale, scale };
    updateView();
  }
  if (state.view.needsFit) fitGraph();
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
  $('fit-graph').onclick = fitGraph;
}

function updateView() {
  $('graph-world').style.transform = `translate(${state.view.x}px,${state.view.y}px) scale(${state.view.scale})`;
  $('zoom-value').textContent = `${Math.round(state.view.scale * 100)}%`;
}

function renderInspector(document) {
  const target = state.selectedTimeline
    ? document.tracks.flatMap(track => [track, ...track.clips, ...track.markers]).find(item => item.id === state.selectedTimeline)
    : state.selectedNode ? document.nodes.find(node => node.id === state.selectedNode) : document;
  const definition = target.capability ? state.capabilities.get(target.capability) : null;
  $('selection-name').textContent = target.displayName || definition?.displayName || target.id;
  $('inspector-content').innerHTML = `<div class="identity">${escapeHtml(target.id)}</div><div class="read-state">${state.source === 'migrations' ? '迁移候选中的实际值' : '磁盘作者值'}</div>${definition ? `<details class="definition-info"><summary>原能力定义</summary><code>${escapeHtml(definition.id)}</code><small>角色：${escapeHtml(definition.roles.join('、'))}</small><code>定义修订 ${escapeHtml(state.definitionRevision)}</code></details>` : ''}${target.timeRange ? `<div class="time-details"><strong>${seconds(target.timeRange.startRaw).toFixed(4)} ～ ${seconds(target.timeRange.endRaw).toFixed(4)} 秒</strong><details><summary>无损时间与素材范围</summary>${Object.entries(target.timeRange).map(([key, value]) => `<div><label>${escapeHtml(key)}</label><code>${escapeHtml(value)}</code></div>`).join('')}</details></div>` : ''}${Object.entries(target.fields || {}).map(([id, value]) => {
    const field = definition?.fields.find(field => field.id === id);
    return `<div class="field"><label title="${escapeHtml(id)}">${escapeHtml(field?.displayName || id)}${field && !field.writable ? ' · 原定义只读' : ''}</label>${renderValue(value)}${field?.constraint?.AllowedValues.length ? `<small>声明选项：${field.constraint.AllowedValues.map(escapeHtml).join('、')}</small>` : ''}</div>`;
  }).join('') || '<p class="muted">此对象没有显式作者字段。</p>'}`;
  if (Array.isArray(document.nodes) && !state.selectedTimeline) {
    const editable = definition?.fields.filter(field => field.visible && (!field.visibility ||
      (target.fields[field.visibility.fieldId] || definition.fields.find(item => item.id === field.visibility.fieldId).defaultValue).value === field.visibility.equals)) || [];
    const actions = `<div class="inspector-actions"><button data-rename-element>修改名称</button>${definition && !definition.systemOwned ? '<button class="danger" data-delete-node>删除节点及其连接</button>' : ''}</div>`;
    $('inspector-content').insertAdjacentHTML('afterbegin', actions);
    editable.forEach(field => {
      const value = target.fields[field.id] || field.defaultValue;
      const rendered = [...$('inspector-content').querySelectorAll('.field')].find(element => element.querySelector('label')?.title === field.id);
      const button = `<button class="edit-field" data-edit-field="${escapeHtml(field.id)}">编辑候选${target.fields[field.id] ? '' : ' · 默认值'}</button>`;
      if (rendered && field.writable) rendered.insertAdjacentHTML('beforeend', button);
      else if (!rendered) $('inspector-content').insertAdjacentHTML('beforeend', `<div class="field"><label title="${escapeHtml(field.id)}">${escapeHtml(field.displayName)} · 原定义默认值</label>${renderValue(value)}${field.writable ? button : ''}</div>`);
    });
    if (state.selectedNode) {
      const edges = document.edges.filter(edge => edge.sourceNode === target.id || edge.targetNode === target.id);
      $('inspector-content').insertAdjacentHTML('beforeend', `<section class="connection-list"><h3>本节点连接</h3>${edges.map(edge => `<div><code>${escapeHtml(edge.sourcePort)} → ${escapeHtml(edge.targetPort)}</code><small>${escapeHtml(edge.id)}</small><button data-delete-edge="${escapeHtml(edge.id)}">删除连接候选</button></div>`).join('') || '<small>当前没有连接</small>'}</section>`);
    }
    if (state.selectedFile.draftChanged) $('inspector-content').querySelector('.read-state').textContent = '本会话候选 · 尚未保存或发布';
  }
}

function requireDraftSettled() {
  if (state.draft?.busy) throw new Error('请等待当前图修改预览返回。');
  if (state.draft?.batches.length) throw new Error('当前有未保存的图候选。可继续查看本根文件；切换来源、内容根或重读前，请先查看差异并明确丢弃候选。');
}

function renderDraftToolbar() {
  const graph = Array.isArray(state.selectedFile?.document?.nodes);
  $('draft-toolbar').hidden = !graph && !state.draft?.receipt;
  $('draft-add-node').disabled = !graph || state.tab !== 'document';
  $('draft-add-edge').disabled = !graph || state.tab !== 'document';
  $('draft-undo').disabled = !state.draft?.batches.length;
  $('draft-redo').disabled = !state.draft?.redoBatches.length;
  $('draft-diff').disabled = !state.draft?.receipt;
  $('draft-discard').disabled = !state.draft?.receipt;
  $('draft-state').textContent = state.draft?.batches.length ? `${state.draft.batches.length} 步候选修改 · 未保存` : '共同图操作 · 预览候选';
}

async function showDraft() {
  const path = state.selectedFile.path, node = state.selectedNode;
  state.snapshot.files = state.draft.files();
  $('saved-state').textContent = state.draft.batches.length ? '本会话候选 · 未保存或发布' : '已读取的文件版本';
  await selectFile(path);
  if (state.selectedFile.document.nodes?.some(item => item.id === node)) state.selectedNode = node;
  renderDocument();
  report(state.draft.batches.length ? '共同操作已生成候选，图结构诊断来自服务。完整领域接收尚未完成，原文件保持原修订。' : '已回到本次读取的原文件内容。');
}

async function applyGraphEdits(edits) {
  const file = state.selectedFile;
  await state.draft.apply(file.document.id, edits);
  await showDraft();
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
  $('document-view').innerHTML = `<div class="variables-page"><div class="eyebrow">BLACKBOARD DECLARATIONS</div><h1>变量与归属</h1><p>${variables.length} 条实际声明。默认值与声明分别显示，所属图保持原 owner。修改生成本会话候选，尚未保存或运行。</p><div class="variable-list">${variables.map(variable => `<article><header><strong>${escapeHtml(variable.key)}</strong><span title="${escapeHtml(variable.type)}">${escapeHtml(typeName(variable.type))}</span><button data-document="${escapeHtml(variable.graph.path)}">所属图 ↗</button><button data-edit-variable="${escapeHtml(variable.id)}" data-variable-document="${escapeHtml(variable.graph.path)}">编辑声明候选</button></header><code>${escapeHtml(variable.id)}</code><div class="variable-columns"><div><h3>默认值</h3>${renderValue(variable.defaultValue)}</div><div><h3>作用域、生命周期与输入声明</h3>${Object.entries(variable.declaration).map(([field, value]) => `<div class="nested-value"><label>${escapeHtml(field)}</label>${renderValue(value)}</div>`).join('')}</div></div></article>`).join('') || '<p class="muted">本内容根没有黑板声明。</p>'}</div></div>`;
}

async function guarded(action) {
  try { await action(); }
  catch (error) { report(error.message, true); }
}

$('roots').onclick = event => { const item = event.target.closest('[data-root]'); if (item) guarded(() => openRoot(item.dataset.root)); };
$('files').onclick = event => { const item = event.target.closest('[data-file]'); if (item) guarded(() => selectFile(item.dataset.file)); };
$('document-view').onclick = event => {
  const variableButton = event.target.closest('[data-edit-variable]');
  if (variableButton) {
    const file = state.snapshot.files.find(item => item.path === variableButton.dataset.variableDocument);
    const variable = file.document.variables.find(item => item.id === variableButton.dataset.editVariable);
    editVariable(variable, async fields => {
      await state.draft.apply(file.document.id, [{ kind: 'configure-variable', variableId: variable.id, ...fields }]);
      await showDraft();
    });
    return;
  }
  const reference = event.target.closest('[data-document]');
  if (reference) { state.tab = 'document'; showTab(); guarded(() => selectFile(reference.dataset.document)); return; }
  const timeline = event.target.closest('[data-timeline]');
  if (timeline) { state.selectedTimeline = timeline.dataset.timeline; renderDocument(); return; }
  const node = event.target.closest('[data-node]');
  if (!node) return;
  state.selectedNode = node.dataset.node;
  renderDocument();
};
$('inspector-content').onclick = event => {
  const reference = event.target.closest('[data-document]');
  if (reference) { state.tab = 'document'; showTab(); guarded(() => selectFile(reference.dataset.document)); return; }
  const graph = state.selectedFile?.document;
  const node = state.selectedNode ? graph.nodes.find(item => item.id === state.selectedNode) : null;
  const fieldButton = event.target.closest('[data-edit-field]');
  if (fieldButton) {
    const field = state.capabilities.get(node.capability).fields.find(item => item.id === fieldButton.dataset.editField);
    editField(field, node.fields[field.id] || field.defaultValue,
      value => applyGraphEdits([{ kind: 'set-node-field', nodeId: node.id, fieldId: field.id, value }]));
  } else if (event.target.closest('[data-rename-element]')) renameElement((node || graph).displayName,
    displayName => applyGraphEdits([node ? { kind: 'rename-node', nodeId: node.id, displayName } : { kind: 'rename-graph', displayName }]));
  else if (event.target.closest('[data-delete-node]')) guarded(() => applyGraphEdits([{ kind: 'delete-node', nodeId: node.id }]));
  else if (event.target.closest('[data-delete-edge]')) guarded(() => applyGraphEdits([{ kind: 'delete-edge', edgeId: event.target.closest('[data-delete-edge]').dataset.deleteEdge }]));
};
$('draft-add-node').onclick = () => createNode(state.capabilities, state.selectedFile.document.role,
  node => applyGraphEdits([{ kind: 'insert-node', index: state.selectedFile.document.nodes.length, node }]));
$('draft-add-edge').onclick = () => createConnection(state.selectedFile.document, state.selectedFile.portShapes,
  edge => applyGraphEdits([{ kind: 'insert-edge', index: state.selectedFile.document.edges.length, edge }]));
$('draft-undo').onclick = () => guarded(async () => { await state.draft.undo(); await showDraft(); });
$('draft-redo').onclick = () => guarded(async () => { await state.draft.redo(); await showDraft(); });
$('draft-diff').onclick = () => showGraphDiff(state.draft);
$('draft-discard').onclick = () => guarded(async () => { state.draft.discard(); await showDraft(); });
$('search-roots').oninput = event => { state.query = event.target.value.trim().toLowerCase(); renderRoots(); };
$('search-files').oninput = renderFiles;
$('refresh-roots').onclick = () => guarded(() => loadRoots(true));
$('load-more').onclick = () => guarded(() => loadRoots());
$('reload-root').onclick = () => guarded(() => openRoot(state.selectedRoot.path));
function showTab() {
  document.querySelectorAll('[data-tab]').forEach(item => item.classList.toggle('active', item.dataset.tab === state.tab));
  $('source-view').hidden = state.tab !== 'source';
  $('document-view').hidden = state.tab === 'source';
  const query = new URLSearchParams(location.search);
  query.set('tab', state.tab);
  history.replaceState(null, '', `?${query}`);
}

document.querySelectorAll('[data-tab]').forEach(button => button.onclick = () => {
  state.tab = button.dataset.tab;
  showTab();
  renderDocument();
});
async function selectSource(source) {
  try { requireDraftSettled(); }
  catch (error) { $('content-source').value = state.source; throw error; }
  state.source = source;
  $('content-source').value = source;
  const info = await request(api());
  state.timeScale = BigInt(info.timelineTimeScale);
  $('service-state').textContent = '服务已连接';
  $('workspace-path').textContent = info.path;
  state.selectedRoot = null;
  state.snapshot = null;
  state.draft = null;
  $('draft-toolbar').hidden = true;
  state.selectedNode = null;
  state.selectedTimeline = null;
  $('search-files').value = '';
  $('files').innerHTML = '';
  $('document-actions').hidden = true;
  $('inspector-content').innerHTML = '';
  $('document-title').textContent = source === 'migrations' ? '选择迁移候选' : '选择内容根';
  $('document-view').innerHTML = `<div class="welcome"><div class="eyebrow">${source === 'migrations' ? 'MIGRATION REVIEW' : 'AUTHORING DOCUMENTS'}</div><h1>${source === 'migrations' ? '检查原资产的完整内容' : '打开正式作者文档'}</h1><p>${source === 'migrations' ? '候选用于核对迁移读取结果，原 Unity 作者资产仍是当前正式来源。选择左侧内容查看图、状态、时间轴和变量。' : '选择左侧作者根。尚未接收的迁移候选可从“内容来源”切换查看。'}</p></div>`;
  await loadRoots(true);
  report(source === 'migrations' ? '迁移候选可预览共同图修改；尚未保存或切换正式来源。' : '请选择作者内容根。');
}

$('content-source').onchange = event => guarded(() => selectSource(event.target.value));

guarded(async () => {
  const query = new URLSearchParams(location.search);
  await selectSource(query.get('source') === 'migrations' ? 'migrations' : 'workspace');
  if (query.has('root')) {
    await openRoot(query.get('root'));
    if (query.get('tab') === 'variables' || query.get('tab') === 'source') { state.tab = query.get('tab'); showTab(); }
    if (query.has('file')) await selectFile(query.get('file'));
    else renderDocument();
  }
});
