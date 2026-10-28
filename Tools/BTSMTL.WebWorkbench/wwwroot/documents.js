import { editField, editNodeFields, editVariable, renameElement, createNode, createConnection, reorderConnections, referenceChoices, showDocumentDiff } from './graph-edits.js';
import { DocumentEditSession } from './document-session.js';
import { createTrack, createClip, editTimeRange, editMarkerTime, moveClip, bindTimelineDrag, timelineObjectDefinition, editTimelineFields } from './timeline-edits.js';
import { showReferences, showReferenceResource } from './references.js';
import { graphContext, editTransition } from './documents-fsm.js';

const $ = id => document.getElementById(id);
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const state = { roots: [], nextOffset: 0, selectedRoot: null, snapshot: null, references: [], navigation: [], selectedFile: null, selectedNode: null, selectedEdge: null, selectedTimeline: null, linkStart: null, tab: 'document', source: 'workspace', timeScale: null, timelineZoom: 1, query: '', capabilities: new Map(), definitions: null, definitionRevision: null, draft: null, authoringState: null, authoringProblem: '', view: { x: 35, y: 45, scale: 1 } };
const reads = { source: null, roots: null, root: null, file: null };
const kindNames = { Graph: '图', Timeline: '时间轴', InputPlan: '输入方案', Layout: '布局', References: '外部引用记录' };
const api = () => `/api/${state.source}`;
const typeName = value => value.split(',')[0].split('.').pop();

async function request(path, body, signal) {
  let response;
  try { response = await fetch(path, { signal, ...(body === undefined ? {} : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  }) }); }
  catch (error) {
    if (signal?.aborted) throw error;
    $('service-state').textContent = '服务连接中断';
    $('service-state').classList.add('error');
    throw new Error(`无法连接作者服务，当前候选仍保留在本页：${error.message}`, { cause: error });
  }
  $('service-state').textContent = '服务已连接';
  $('service-state').classList.remove('error');
  const text = await response.text();
  if (response.status === 404 && !text) throw new Error(path.startsWith('/api/authoring/documents/requests/')
    ? '原保存请求尚无已记录的结果，原候选仍保留。可继续查询，或按原候选重试保存。'
    : `服务尚未装配此操作入口：${path}`);
  const result = JSON.parse(text);
  if (!response.ok) {
    const conflict = result.stage === 'baseline-conflict' || result.stage === 'definitions-conflict';
    const message = conflict ? '文件或作者定义已由其它入口修改。当前候选保留；请先查看候选差异，再明确丢弃并重新读取当前版本。'
      : result.message || result.error || `作者服务返回 ${response.status}。`;
    $('request-problem').hidden = false;
    $('request-problem-title').textContent = conflict ? '当前基线已过期，未覆盖磁盘内容' : '本次操作未完成';
    $('request-problem-message').textContent = message;
    $('request-problem-details').textContent = JSON.stringify(result, null, 2);
    const error = new Error(message);
    error.result = result;
    throw error;
  }
  return result;
}

function report(message, error = false) {
  $('status-message').textContent = message;
  $('status-message').classList.toggle('error', error);
}

async function loadDefinitions(domain, signal) {
  const source = await request('/api/authoring/definitions/document', undefined, signal);
  if (source.domain !== domain) throw new Error(`服务的原能力目录属于 ${source.domain}，当前内容属于 ${domain}。`);
  state.definitions = source.definition;
  state.capabilities = new Map(source.definition.capabilities.map(capability => [capability.id, capability]));
  state.definitionRevision = source.revision;
}

async function loadRoots(reset = false) {
  reads.roots?.abort();
  const reading = new AbortController(); reads.roots = reading;
  if (reset) { state.roots = []; state.nextOffset = 0; }
  const page = await request(`${api()}/roots?offset=${state.nextOffset}&limit=100`, undefined, reading.signal);
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
  reads.root?.abort(); reads.file?.abort();
  const reading = new AbortController(); reads.root = reading;
  state.selectedRoot = { path };
  state.snapshot = null; state.draft = null; state.selectedFile = null;
  state.selectedNode = null; state.selectedEdge = null; state.selectedTimeline = null;
  state.navigation = [];
  $('draft-toolbar').hidden = true;
  $('document-title').textContent = path;
  $('root-kind').textContent = '作者文档尚未读取';
  $('files').innerHTML = '';
  $('file-path').textContent = path; $('file-revision').textContent = '';
  $('selection-name').textContent = '未选择内容';
  $('inspector-content').innerHTML = '';
  $('document-actions').hidden = true; $('document-navigation').hidden = true;
  $('source-text').value = '';
  $('document-view').innerHTML = '<div class="notice">正在读取正式作者文档…</div>';
  renderRoots();
  report('正在读取正式文件…');
  let snapshot;
  try { snapshot = await request(`${api()}/root?path=${encodeURIComponent(path)}`, undefined, reading.signal); }
  catch (error) {
    if (error.result?.stage !== 'source-read') throw error;
    const problems = error.result.diagnostics;
    $('root-kind').textContent = '外部文件尚未接收';
    $('selection-name').textContent = '无法解析对象';
    $('document-view').innerHTML = `<div class="notice error"><h2>作者文件需要修正</h2>${problems.map(problem => `<p>${escapeHtml(problem.message)}</p><code>${escapeHtml(problem.path)}</code>`).join('')}</div>`;
    const source = await request(`${api()}/source?path=${encodeURIComponent(path)}`, undefined, reading.signal);
    $('source-text').value = source.text;
    $('file-revision').textContent = source.revision.slice(0, 12);
    report('原文件已保留，未接收为可编译内容。', true);
    return;
  }
  const root = JSON.parse(snapshot.root);
  await loadDefinitions(root.domain, reading.signal);
  const files = snapshot.files.map(file => {
    try { return { ...file, document: JSON.parse(file.content), problem: null }; }
    catch (error) { return { ...file, document: null, problem: error.message }; }
  });
  state.snapshot = { ...snapshot, root, files };
  state.selectedFile = files[0];
  state.references = referenceChoices(state.snapshot);
  state.authoringState = null;
  state.authoringProblem = state.source === 'migrations' ? '迁移候选用于核对原资产；正式接收后从作者工作区编辑。' : '';
  if (state.source === 'workspace') {
    state.draft = new DocumentEditSession(state.snapshot, path, state.definitionRevision, request);
    try {
      state.authoringState = await request(`/api/authoring/documents/state?path=${encodeURIComponent(path)}`, undefined, reading.signal);
      if (!state.authoringState.adoptable)
        state.authoringProblem = state.authoringState.problems.map(problem => problem.message).join('；');
    } catch (error) {
      if (reading.signal.aborted) throw error;
      state.authoringProblem = error.message;
    }
  }
  $('document-title').textContent = root.displayName;
  $('root-kind').textContent = `${root.domain} · ${root.kind}`;
  $('revision').textContent = snapshot.revision.slice(0, 12);
  $('revision').title = snapshot.revision;
  $('saved-state').textContent = state.authoringState?.adoptable ? '正式领域已接收 · 磁盘版本'
    : state.draft ? '参数可配置 · 正式候选尚未接收' : '只读核对 · 尚未进入正式编辑';
  $('document-actions').hidden = false;
  renderRoots();
  renderFiles();
  await selectFile(state.selectedFile.path);
  report(state.authoringProblem || '完整根已由共同领域接收，可编辑并预览同一候选；保存、发布和运行采用分别处理。');
}

function renderFiles() {
  const root = state.snapshot.root;
  const files = state.snapshot.files.map((file, index) => ({ ...file, declaration: index === 0 ? null : root.files[index - 1] }));
  const query = $('search-files').value.trim().toLowerCase();
  $('files').innerHTML = files.filter(file => `${file.document?.displayName} ${file.document?.role} ${file.path}`.toLowerCase().includes(query)).map(file => `<button class="file-item ${state.selectedFile?.path === file.path ? 'selected' : ''}" data-file="${escapeHtml(file.path)}" title="${escapeHtml(file.path)}"><span>${file.problem ? '!' : file.declaration?.kind === 'Graph' ? '⌘' : file.declaration?.kind === 'Timeline' ? '◷' : '▤'}</span><span>${escapeHtml(file.document?.displayName || file.path)}<small>${escapeHtml(file.problem ? '文件格式错误' : file.document?.role || (file.declaration ? kindNames[file.declaration.kind] : '内容根'))}${file.draftChanged ? ' · 未保存' : ''}</small></span></button>`).join('');
}

async function selectFile(path, elementId = null) {
  reads.file?.abort();
  const reading = new AbortController(); reads.file = reading;
  state.selectedFile = state.snapshot.files.find(file => file.path === path);
  const file = state.selectedFile;
  state.linkStart = null;
  state.selectedNode = file.document?.nodes?.some(node => node.id === elementId) ? elementId : null;
  state.selectedEdge = file.document?.edges?.some(edge => edge.id === elementId) ? elementId : null;
  state.selectedTimeline = file.document?.tracks?.some(track => track.id === elementId ||
    [...track.clips, ...track.markers].some(item => item.id === elementId)) ||
    file.document?.bindings?.some(binding => binding.id === elementId) ? elementId : null;
  if (file.document?.variables?.some(variable => variable.id === elementId)) { state.tab = 'variables'; showTab(); }
  state.view = { x: 35, y: 45, scale: 1, needsFit: true };
  const locationQuery = new URLSearchParams({ source: state.source, root: state.selectedRoot.path, file: path, tab: state.tab });
  if (elementId) locationQuery.set('element', elementId);
  history.replaceState(null, '', `?${locationQuery}`);
  renderFiles();
  renderNavigation();
  if (!file.problem && Array.isArray(file.document.nodes) && !file.portShapes) {
    $('document-view').innerHTML = '<div class="notice">正在从共同定义投影端口…</div>';
    const projected = await request('/api/authoring/ports', {
      content: file.content, definitionsRevision: state.definitionRevision
    }, reading.signal);
    file.portShapes = new Map(projected.nodes.map(node => [node.nodeId, node]));
  }
  if (!state.draft && file.document?.role === 'StateMachine' && !file.stateMachineReceipt && !file.stateMachineProblem) {
    try {
      file.stateMachineReceipt = await request('/api/authoring/state-machines/validate', {
        content: file.content, definitionsRevision: state.definitionRevision,
        relatedGraphs: state.snapshot.files.filter(related => related !== file && Array.isArray(related.document?.nodes))
          .map(related => related.content)
      }, reading.signal);
    } catch (error) {
      if (reading.signal.aborted) throw error;
      file.stateMachineProblem = error.message;
    }
  }
  if (!state.draft && !file.problem && Array.isArray(file.document.nodes) && file.document.role !== 'StateMachine' &&
      !file.topologyReceipt && !file.topologyProblem) {
    try {
      file.topologyReceipt = await request('/api/authoring/graphs/validate', {
        content: file.content, definitionsRevision: state.definitionRevision
      }, reading.signal);
    } catch (error) {
      if (reading.signal.aborted) throw error;
      file.topologyProblem = error.message;
    }
  }
  renderDocument();
}

function renderNavigation() {
  const files = state.snapshot.files, rootFile = files[0], path = [];
  let current = state.selectedFile;
  while (current !== rootFile) {
    path.unshift(current);
    const owner = current.document?.owner;
    current = owner?.rootId === state.snapshot.root.id && owner.documentId !== current.document.id
      ? files.find(file => file.document?.id === owner.documentId) : null;
    if (!current) break;
  }
  path.unshift(rootFile);
  $('document-navigation').hidden = false;
  $('document-navigation').innerHTML = path.map((file, index) => `<button data-document="${escapeHtml(file.path)}" ${index + 1 < path.length ? `data-element="${escapeHtml(path[index + 1].document.owner.elementId)}"` : ''} ${file === state.selectedFile ? 'aria-current="page"' : ''}>${escapeHtml(file.document?.displayName || file.path)}</button>`).join('<span aria-hidden="true">›</span>');
  if (state.navigation.length) {
    const source = state.navigation.at(-1);
    $('document-navigation').insertAdjacentHTML('afterbegin',
      `<button data-navigation-back title="返回来源并恢复原对象选择">← 返回来源：${escapeHtml(source.label)}</button>`);
  }
}

async function followDocument(path, element) {
  const selected = state.selectedNode || state.selectedEdge || state.selectedTimeline;
  if (path !== state.selectedFile.path || element !== selected)
    state.navigation.push({ path: state.selectedFile.path, element: selected,
      label: $('selection-name').textContent });
  state.tab = 'document';
  showTab();
  await selectFile(path, element);
}

function selectElement(kind, id) {
  state.selectedNode = kind === 'node' ? id : null;
  state.selectedEdge = kind === 'edge' ? id : null;
  state.selectedTimeline = kind === 'timeline' ? id : null;
  const query = new URLSearchParams(location.search);
  if (id) query.set('element', id); else query.delete('element');
  history.replaceState(null, '', `?${query}`);
  renderDocument();
}

function renderDocument() {
  if (!state.snapshot) return;
  const file = state.selectedFile;
  renderDraftToolbar();
  $('file-path').textContent = file.path;
  $('file-revision').textContent = `${file.revision.slice(0, 12)}${file.draftChanged ? ' · 候选尚未保存' : ''}`;
  $('source-text').value = file.content;
  try {
    if (file.problem) throw new Error(file.problem);
    const document = file.document;
    if (state.tab === 'variables') renderVariables();
    else if (Array.isArray(document.nodes) && Array.isArray(document.edges)) renderGraph(document);
    else if (Array.isArray(document.tracks)) renderTimeline(document);
    else if (Array.isArray(document.entries)) renderReferenceTable(document);
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
  const accepted = state.authoringState?.adoptable || state.draft?.receipt;
  const introduction = state.source === 'migrations'
    ? '该候选从现有 Unity 资产读取，用于核对迁移结果。原资产仍是当前正式作者来源。'
    : '编辑来源保存在项目作者目录。各阶段分别记录实际完成的修订。';
  $('document-view').innerHTML = `<div class="overview"><div class="eyebrow">${state.source === 'migrations' ? 'MIGRATION CANDIDATE' : 'AUTHORING DOCUMENT'}</div><h1>${escapeHtml(document.displayName || root.displayName)}</h1><p class="intro">${introduction}</p><div class="phase-grid"><div><span class="phase-state saved">已读取</span><strong>磁盘内容</strong><small>${state.snapshot.files.length} 个文件 · ${state.draft?.changed ? '存在未保存候选' : '当前磁盘修订'}</small></div><div><span class="phase-state ${accepted ? 'saved' : 'pending'}">${accepted ? '已接收' : '未接收'}</span><strong>领域接收</strong><small>${escapeHtml(state.authoringProblem || '完整根经同一正式领域规则接收')}</small></div><div><span class="phase-state pending">未查询</span><strong>编译与发布</strong><small>保存不代表发布</small></div><div><span class="phase-state pending">未查询</span><strong>运行预览</strong><small>以具体运行采用回执为准</small></div></div><h2>引用与归属</h2><dl><dt>稳定身份</dt><dd><code>${escapeHtml(document.id)}</code></dd><dt>定义版本</dt><dd>${escapeHtml(document.definitionVersion)}</dd><dt>外部引用</dt><dd>${root.externalReferences.length} 项</dd><dt>Unity 对象映射</dt><dd>${root.unityOutputs.length} 项</dd></dl><div class="notice">${escapeHtml(state.authoringProblem || '图与时间轴编辑生成同一完整候选。查看差异后保存；保存后的撤销通过共同服务提交反向修改，并保留其它入口的后续改动。')}</div></div>`;
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
    const curve = `M${x1},${y1} C${x1 + 80},${y1} ${x2 - 80},${y2} ${x2},${y2}`;
    return `<g class="graph-edge ${state.selectedEdge === edge.id ? 'selected' : ''}"><path d="${curve}"/><path class="edge-hit" data-edge="${escapeHtml(edge.id)}" tabindex="0" role="button" aria-label="${escapeHtml(edge.sourcePort)} → ${escapeHtml(edge.targetPort)}，顺序 ${edge.order}" d="${curve}"/><title>${escapeHtml(edge.id)} · ${escapeHtml(edge.sourcePort)} → ${escapeHtml(edge.targetPort)} · 顺序 ${edge.order}</title></g>`;
  }).join('');
  const nodes = graph.nodes.map(node => {
    const position = positions.get(node.id);
    const fieldCount = Object.keys(node.fields).length;
    const shape = shapes.get(node.id), definition = shape.definition;
    const rows = Math.max(shape.inputs.length, shape.outputs.length);
    const portRows = Array.from({ length: rows }, (_, index) => {
      const input = shape.inputs[index], output = shape.outputs[index];
      const label = (port, side) => port ? `<span class="${side} ${state.linkStart?.nodeId === node.id && state.linkStart.portId === port.id ? 'port-start' : ''} ${state.linkStart && side === 'input' && port.valueType === state.linkStart.valueType ? 'port-target' : ''}" data-port-node="${escapeHtml(node.id)}" data-port="${escapeHtml(port.id)}" data-port-direction="${escapeHtml(port.direction)}" tabindex="0" role="button" aria-label="${escapeHtml(port.direction)} ${escapeHtml(port.displayName || port.id)}" title="${escapeHtml(port.id)} · ${escapeHtml(port.valueType)}"><i></i><em>${escapeHtml(port.displayName || port.id)}</em></span>` : '<span></span>';
      return `<div class="node-port-row">${label(input, 'input')}${label(output, 'output')}</div>`;
    }).join('');
    return `<button class="graph-node ${shape.problem ? 'missing-definition' : ''} ${state.selectedNode === node.id ? 'selected' : ''}" data-node="${escapeHtml(node.id)}" title="${escapeHtml(shape.problem ? shape.problem.message : node.id)}" style="left:${escapeHtml(position.x)}px;top:${escapeHtml(position.y)}px"><span class="node-type">${escapeHtml(node.capability)}</span><strong>${escapeHtml(node.displayName || definition?.displayName || node.capability)}</strong><small>${fieldCount} 个作者字段${shape.problem ? ' · ' + escapeHtml(shape.problem.code) : ''}</small><div class="node-ports">${portRows}</div></button>`;
  }).join('');
  $('document-view').innerHTML = `<div class="graph-surface" id="graph-surface"><div class="graph-caption">${escapeHtml(graph.role)}<span>${graph.nodes.length} 个节点 · ${graph.edges.length} 条连接</span></div><div class="graph-world" id="graph-world"><svg width="6000" height="4000">${links}</svg>${nodes}</div><div class="canvas-tools">${state.linkStart ? '<button data-cancel-connection>取消连线</button>' : ''}<button id="focus-graph" ${state.selectedNode || state.selectedEdge ? '' : 'disabled'}>定位所选对象</button><button id="fit-graph">适应全图</button><span id="zoom-value">100%</span></div><div class="canvas-hint">${state.linkStart ? '选择目标输入完成连接 · Esc 取消' : '空白处拖动画布 · 滚轮缩放 · 点击输出端口开始连接'}</div></div>`;
  if (!state.draft?.canEdit) $('document-view').querySelectorAll('[data-port]').forEach(port => {
    port.removeAttribute('data-port'); port.removeAttribute('role'); port.removeAttribute('tabindex');
  });
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
  function fitGraph(nodes) {
    if (nodes.length === 0) { state.view = { x: 35, y: 45, scale: 1 }; updateView(); return; }
    const bounds = nodes.map(node => {
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
  function focusSelection() {
    if (state.selectedNode) fitGraph(graph.nodes.filter(node => node.id === state.selectedNode));
    else {
      const edge = graph.edges.find(edge => edge.id === state.selectedEdge);
      fitGraph(graph.nodes.filter(node => node.id === edge.sourceNode || node.id === edge.targetNode));
    }
  }
  if (state.view.needsFit) {
    if (state.selectedNode || state.selectedEdge) focusSelection();
    else fitGraph(graph.nodes);
  }
  let drag = null;
  surface.addEventListener('pointerdown', event => {
    if (drag || event.button !== 0 || event.target.closest('button, [data-edge]')) return;
    drag = { pointerId: event.pointerId, x: event.clientX - state.view.x, y: event.clientY - state.view.y };
    surface.setPointerCapture(event.pointerId);
    event.preventDefault();
  });
  surface.addEventListener('pointermove', event => {
    if (!drag || drag.pointerId !== event.pointerId) return;
    state.view.x = event.clientX - drag.x;
    state.view.y = event.clientY - drag.y;
    updateView();
  });
  const endPan = event => {
    if (drag?.pointerId === event.pointerId) drag = null;
  };
  surface.addEventListener('pointerup', endPan);
  surface.addEventListener('pointercancel', endPan);
  surface.addEventListener('lostpointercapture', endPan);
  surface.addEventListener('wheel', event => {
    event.preventDefault();
    const bounds = surface.getBoundingClientRect();
    const old = state.view.scale, next = Math.min(2.2, Math.max(0.25, old * Math.exp(-event.deltaY * 0.001)));
    state.view.x = event.clientX - bounds.left - (event.clientX - bounds.left - state.view.x) * next / old;
    state.view.y = event.clientY - bounds.top - (event.clientY - bounds.top - state.view.y) * next / old;
    state.view.scale = next;
    updateView();
  }, { passive: false });
  $('fit-graph').onclick = () => fitGraph(graph.nodes);
  $('focus-graph').onclick = focusSelection;
}

function updateView() {
  $('graph-world').style.transform = `translate(${state.view.x}px,${state.view.y}px) scale(${state.view.scale})`;
  $('zoom-value').textContent = `${Math.round(state.view.scale * 100)}%`;
}

function renderInspector(document) {
  if (Array.isArray(document.entries)) {
    $('selection-name').textContent = '外部引用记录';
    $('inspector-content').innerHTML = '<p class="muted">此表保存原资源导出时提供的引用字段。内容由原 owner 维护，网页只读；当前根的业务字段仍通过原图、时间轴及变量入口编辑。</p>';
    return;
  }
  const target = state.selectedTimeline
    ? [...document.tracks.flatMap(track => [track, ...track.clips, ...track.markers]), ...document.bindings].find(item => item.id === state.selectedTimeline)
    : state.selectedEdge ? document.edges.find(edge => edge.id === state.selectedEdge)
    : state.selectedNode ? document.nodes.find(node => node.id === state.selectedNode) : document;
  const definition = target.capability ? state.capabilities.get(target.capability)
    : Array.isArray(document.tracks) ? timelineObjectDefinition(target, state.definitions) : null;
  $('selection-name').textContent = target.displayName || definition?.displayName || target.id;
  $('inspector-content').innerHTML = `<div class="identity">${escapeHtml(target.id)}</div><div class="read-state">${state.source === 'migrations' ? '迁移候选中的实际值' : '磁盘作者值'}</div>${definition ? `<details class="definition-info"><summary>原作者定义</summary><code>${escapeHtml(target.capability ? definition.id : target.contract)}</code>${target.capability ? `<small>角色：${escapeHtml(definition.roles.join('、'))}</small>` : ''}<code>定义修订 ${escapeHtml(state.definitionRevision)}</code></details>` : ''}${target.timeRange ? `<div class="time-details"><strong>${seconds(target.timeRange.startRaw).toFixed(4)} ～ ${seconds(target.timeRange.endRaw).toFixed(4)} 秒</strong><details><summary>无损时间与素材范围</summary>${Object.entries(target.timeRange).map(([key, value]) => `<div><label>${escapeHtml(key)}</label><code>${escapeHtml(value)}</code></div>`).join('')}</details></div>` : ''}${Object.entries(target.fields || {}).map(([id, value]) => {
    const field = definition?.fields.find(field => field.id === id);
    return `<div class="field"><label title="${escapeHtml(id)}">${escapeHtml(field?.displayName || id)}${field && !field.writable ? ' · 原定义只读' : ''}</label>${renderValue(value)}${field?.constraint?.AllowedValues.length ? `<small>声明选项：${field.constraint.AllowedValues.map(escapeHtml).join('、')}</small>` : ''}</div>`;
  }).join('') || '<p class="muted">此对象没有显式作者字段。</p>'}`;
  if (state.selectedFile.draftChanged) $('inspector-content').querySelector('.read-state').textContent = '本会话候选 · 尚未保存或发布';
  if (Array.isArray(document.nodes))
    $('inspector-content').insertAdjacentHTML('afterbegin', graphContext(document, target, state.snapshot.files,
      state.definitions, state.capabilities));
  if (state.draft?.canEdit && Array.isArray(document.nodes) && !state.selectedTimeline) {
    if (state.selectedEdge) {
      const source = document.nodes.find(node => node.id === target.sourceNode);
      const destination = document.nodes.find(node => node.id === target.targetNode);
      $('selection-name').textContent = `${source.displayName} → ${destination.displayName}`;
      $('inspector-content').insertAdjacentHTML('afterbegin', `<div class="inspector-actions"><button data-select-node="${escapeHtml(source.id)}">来源节点</button><button data-select-node="${escapeHtml(destination.id)}">目标节点</button><button data-edge-order>调整执行顺序</button><button data-element-dependencies>本连接引用</button><button class="danger" data-delete-edge="${escapeHtml(target.id)}">删除连接</button></div><p class="muted">同一输出的第 ${target.order + 1} 项。业务优先级、中断规则及条件取自下方实际作者字段。</p>`);
      if (document.role === state.definitions.stateMachine?.role)
        $('inspector-content').querySelector('.inspector-actions').insertAdjacentHTML('beforeend',
          '<button data-configure-transition>同时配置转移字段</button>');
      Object.keys(target.fields).forEach(id => {
        const rendered = [...$('inspector-content').querySelectorAll('.field')].find(element => element.querySelector('label').title === id);
        rendered.insertAdjacentHTML('beforeend', `<button class="edit-field" data-edit-edge-field="${escapeHtml(id)}">编辑候选</button>`);
      });
      return;
    }
    const editable = definition?.fields.filter(field => field.visible && (!field.visibility ||
      (target.fields[field.visibility.fieldId] || definition.fields.find(item => item.id === field.visibility.fieldId).defaultValue).value === field.visibility.equals)) || [];
    const actions = `<div class="inspector-actions"><button data-rename-element>修改名称</button><button data-element-dependencies>本对象引用</button>${definition && !definition.systemOwned ? '<button class="danger" data-delete-node>删除节点及其连接</button>' : ''}</div>`;
    $('inspector-content').insertAdjacentHTML('afterbegin', actions);
    if (state.selectedNode && definition.fields.some(field => field.visible && field.writable))
      $('inspector-content').querySelector('.inspector-actions').insertAdjacentHTML('beforeend', '<button data-configure-node>同时配置参数</button>');
    editable.forEach(field => {
      const value = target.fields[field.id] || field.defaultValue;
      const rendered = [...$('inspector-content').querySelectorAll('.field')].find(element => element.querySelector('label')?.title === field.id);
      const button = `<button class="edit-field" data-edit-field="${escapeHtml(field.id)}">编辑候选${target.fields[field.id] ? '' : ' · 默认值'}</button>${target.fields[field.id] ? `<button class="edit-field" data-reset-field="${escapeHtml(field.id)}">使用领域默认值</button>` : ''}`;
      if (rendered && field.writable) rendered.insertAdjacentHTML('beforeend', button);
      else if (!rendered) $('inspector-content').insertAdjacentHTML('beforeend', `<div class="field"><label title="${escapeHtml(field.id)}">${escapeHtml(field.displayName)} · 原定义默认值</label>${renderValue(value)}${field.writable ? button : ''}</div>`);
    });
    if (state.selectedNode) Object.keys(target.fields).filter(id => id.startsWith('input:')).forEach(id => {
      const port = state.selectedFile.portShapes.get(target.id).ports.find(port => port.id === id.slice(6) && port.direction === 'Input');
      const rendered = [...$('inspector-content').querySelectorAll('.field')].find(element => element.querySelector('label').title === id);
      rendered.querySelector('label').textContent = `${port.displayName || port.id} · 输入常量`;
      const connected = document.edges.some(edge => edge.targetNode === target.id && edge.targetPort === port.id);
      rendered.insertAdjacentHTML('beforeend', `<small>${connected ? '此输入已连接，运行值由上游提供；这里编辑未连接时使用的常量。' : '未连接时，此常量提供正式输入值。'}</small><button class="edit-field" data-edit-input="${escapeHtml(id)}">编辑输入常量</button>`);
    });
    if (state.selectedNode) {
      const edges = document.edges.filter(edge => edge.sourceNode === target.id || edge.targetNode === target.id);
      $('inspector-content').insertAdjacentHTML('beforeend', `<section class="connection-list"><h3>本节点连接</h3>${edges.map(edge => `<div><button data-select-edge="${escapeHtml(edge.id)}">${escapeHtml(edge.sourcePort)} → ${escapeHtml(edge.targetPort)} · 顺序 ${edge.order}</button><small>${escapeHtml(edge.id)}</small></div>`).join('') || '<small>当前没有连接</small>'}</section>`);
    }
  }
  if (Array.isArray(document.tracks) && state.draft?.canEdit) renderTimelineActions(document, target, definition);
}

function renderTimelineActions(timeline, target, definition) {
  const track = timeline.tracks.find(track => track.id === target.id || [...track.clips, ...track.markers].some(item => item.id === target.id));
  const clip = track?.clips.find(clip => clip.id === target.id);
  const marker = track?.markers.find(marker => marker.id === target.id);
  const binding = timeline.bindings.find(binding => binding.id === target.id);
  $('inspector-content').insertAdjacentHTML('afterbegin', `<div class="inspector-actions"><button data-timeline-rename>修改名称</button>${target === timeline ? `<button data-timeline-loop>${timeline.loop ? '改为单次' : '改为循环'}</button>` : ''}${clip ? '<button data-timeline-range>配置时间与素材范围</button><button data-timeline-move-clip>轨道与片段顺序</button>' : ''}${marker ? '<button data-timeline-marker-time>配置时刻</button>' : ''}${track === target ? `<button data-timeline-track-move="-1" ${timeline.tracks.indexOf(track) === 0 ? 'disabled' : ''}>轨道上移</button><button data-timeline-track-move="1" ${timeline.tracks.indexOf(track) === timeline.tracks.length - 1 ? 'disabled' : ''}>轨道下移</button>` : ''}${track || binding ? '<button class="danger" data-timeline-delete>删除当前内容</button>' : ''}</div>`);
  if (binding) $('inspector-content').querySelector('[data-timeline-rename]').remove();
  if (track) {
    const kinds = state.definitions.timelineContracts.tracks.find(contract => contract.kind === track.contract).allowedClipKinds;
    const ready = state.definitions.timelineAuthoring !== undefined;
    $('inspector-content').querySelector('.inspector-actions').insertAdjacentHTML('beforeend',
      `<button data-timeline-add-clip ${ready && kinds.length ? '' : 'disabled'}>在此轨道新建片段</button>`);
    if (!ready || !kinds.length) $('inspector-content').insertAdjacentHTML('beforeend',
      `<p class="authoring-gap">${!kinds.length ? '此轨道的正式合同没有可接收的片段类型。' : '正式片段作者字段及初始值尚未导出，新建片段暂不可用。'}</p>`);
  }
  if (definition?.fields.some(field => field.visible && field.writable))
    $('inspector-content').querySelector('.inspector-actions').insertAdjacentHTML('beforeend',
      '<button data-timeline-configure-fields>同时配置参数</button>');
  Object.keys(target.fields).forEach(id => {
    const field = definition?.fields.find(field => field.id === id);
    if (field && (!field.visible || !field.writable)) return;
    const element = [...$('inspector-content').querySelectorAll('.field')].find(item => item.querySelector('label').title === id);
    element.insertAdjacentHTML('beforeend', `<button class="edit-field" data-timeline-field="${escapeHtml(id)}">编辑作者值</button>`);
  });
  definition?.fields.filter(field => field.visible && !Object.hasOwn(target.fields, field.id)).forEach(field => {
    $('inspector-content').insertAdjacentHTML('beforeend', `<div class="field"><label title="${escapeHtml(field.id)}">${escapeHtml(field.displayName || field.id)} · 领域默认值</label>${renderValue(field.defaultValue)}${field.writable ? `<button class="edit-field" data-timeline-field="${escapeHtml(field.id)}">编辑作者值</button>` : ''}</div>`);
  });
}

function requireDraftSettled() {
  if (state.draft?.busy) throw new Error('请等待当前作者操作返回。');
  if (state.draft?.unsettled) throw new Error('当前有未保存候选或结果未确认的保存。切换根或重读前，请先保存、查询原请求结果或明确丢弃候选。');
}

function renderDraftToolbar() {
  const graph = Array.isArray(state.selectedFile?.document?.nodes);
  const ready = state.draft && !state.draft.busy && !state.draft.pendingCommit;
  $('draft-toolbar').hidden = !state.snapshot;
  $('draft-add-node').disabled = !ready || !state.draft.canEdit || !graph || state.tab !== 'document';
  $('draft-add-edge').disabled = !ready || !state.draft.canEdit || !graph || state.tab !== 'document';
  $('draft-undo').disabled = !ready || !state.draft.canUndo;
  $('draft-redo').disabled = !ready || !state.draft.canRedo;
  $('draft-diff').disabled = !state.draft?.receipt;
  $('draft-discard').disabled = !ready || !state.draft.receipt;
  $('draft-save').disabled = !ready || !state.draft.canSave;
  $('draft-query').hidden = !state.draft?.pendingCommit;
  $('draft-retry').hidden = !state.draft?.pendingCommit;
  $('draft-state').textContent = state.draft?.busy ? '正在处理正式作者操作 · 结果尚未返回'
    : state.draft?.pendingCommit ? `保存结果待查询 · ${state.draft.pendingCommit.requestId}`
    : state.draft?.result && state.draft.result.status !== 'Saved' ? '保存未完成 · 候选已保留，请查看原因'
    : state.draft?.changed ? `${state.draft.steps.length} 步修改 · 已接收未保存`
    : state.authoringProblem || (state.draft ? '正式作者版本 · 保存与发布分别处理' : '未建立作者会话');
  $('draft-query').disabled = $('draft-retry').disabled = state.draft?.busy === true;
}

async function showDraft() {
  const path = state.selectedFile.path, element = state.selectedNode || state.selectedEdge || state.selectedTimeline;
  state.snapshot.files = state.draft.files();
  state.snapshot.root = state.snapshot.files[0].document;
  state.snapshot.revision = state.draft.context.rootRevision;
  state.references = referenceChoices(state.snapshot);
  $('saved-state').textContent = state.draft.changed ? '领域已接收 · 尚未保存' : '当前磁盘作者版本';
  $('revision').textContent = state.snapshot.revision.slice(0, 12);
  $('revision').title = state.snapshot.revision;
  const selected = state.snapshot.files.find(file => file.path === path) || state.snapshot.files[0];
  await selectFile(selected.path, element);
  renderDocument();
  if (state.draft.result && state.draft.result.status !== 'Saved') {
    $('request-problem').hidden = false;
    $('request-problem-title').textContent = state.draft.result.status === 'Conflict' ? '保存冲突，未覆盖当前文件' : '文件保存失败';
    $('request-problem-message').textContent = '原请求的终态与候选仍保留。核对差异后，明确丢弃候选并重新读取当前文件，再重新操作。';
    $('request-problem-details').textContent = JSON.stringify(state.draft.result, null, 2);
  }
  report(state.draft.result?.status === 'Saved' ? '作者文件已保存并重新读取。编译、发布和运行采用另看对应回执。'
    : state.draft.result ? '保存未完成，候选已保留，请查看具体原因。'
    : state.draft.changed ? '完整候选已由共同领域接收，可查看差异后保存。' : '已回到本次读取的磁盘版本。');
}

async function applyGraphEdits(edits) {
  const file = state.selectedFile;
  await presentDraftOperation(() => state.draft.apply(file.document.id, edits));
}

async function applyTimelineEdits(edits) {
  await presentDraftOperation(() => state.draft.applyTimeline(state.selectedFile.document.id, edits));
}

async function choosePort(element) {
  const graph = state.selectedFile.document, nodeId = element.dataset.portNode, portId = element.dataset.port;
  const port = state.selectedFile.portShapes.get(nodeId).ports.find(port => port.id === portId &&
    port.direction === element.dataset.portDirection);
  if (port.direction === 'Output') {
    state.linkStart = { nodeId, portId, valueType: port.valueType };
    renderDocument();
    report('已选择输出端口；选择目标输入，服务将按原图规则接收连接。');
    return;
  }
  if (!state.linkStart) { report('请先选择一个输出端口，再选择目标输入。'); return; }
  const source = state.linkStart;
  await applyGraphEdits([{ kind: 'insert-edge', index: graph.edges.length, edge: {
    id: `local:${crypto.randomUUID()}`, sourceNode: source.nodeId, sourcePort: source.portId,
    targetNode: nodeId, targetPort: portId,
    order: Math.max(-1, ...graph.edges.filter(edge => edge.sourceNode === source.nodeId && edge.sourcePort === source.portId)
      .map(edge => edge.order)) + 1, fields: {}
  } }]);
}

function renderValue(value) {
  switch (value.kind) {
    case 'scalar': return `<div class="field-value">${escapeHtml(value.value)}</div><small title="${escapeHtml(value.type)}">${escapeHtml(typeName(value.type))} · ${escapeHtml(value.valueKind)}</small>`;
    case 'null': return `<div class="field-value null">null</div><small>${escapeHtml(value.type)}</small>`;
    case 'reference': {
      const target = state.snapshot.root.files.find(file => file.id === value.target.documentId);
      const mapping = state.snapshot.root.unityOutputs.find(item => item.documentId === value.target.documentId && item.elementId === value.target.elementId);
      return `<div class="reference-value">${target && value.target.rootId === state.snapshot.root.id ? `<button data-document="${escapeHtml(target.path)}" data-element="${escapeHtml(value.target.elementId)}">打开 ${escapeHtml(kindNames[target.kind])} ↗</button>` : `<strong>只读资源引用</strong><button data-resource-root="${escapeHtml(value.target.rootId)}" data-resource-document="${escapeHtml(value.target.documentId)}" data-resource-element="${escapeHtml(value.target.elementId)}">查看引用详情</button>`}<span>${escapeHtml(mapping?.assetPath || value.target.rootId)}</span><button data-reference-root="${escapeHtml(value.target.rootId)}" data-reference-document="${escapeHtml(value.target.documentId)}" data-reference-element="${escapeHtml(value.target.elementId)}">查看引用位置</button><details><summary>稳定引用</summary><code>${escapeHtml(value.target.rootId)} / ${escapeHtml(value.target.documentId)} / ${escapeHtml(value.target.elementId)}</code></details></div>`;
    }
    case 'object':
      if (value.type === 'UnityEngine.CoreModule:UnityEngine.AnimationCurve')
        return `<details><summary>${value.members.keys.items.length} 个关键点 · 曲线记录</summary><pre>${escapeHtml(JSON.stringify(value, null, 2))}</pre></details>`;
      return `<details><summary title="${escapeHtml(value.type)}">${escapeHtml(typeName(value.type))}</summary>${Object.entries(value.members).map(([key, member]) => `<div class="nested-value"><label>${escapeHtml(key)}</label>${renderValue(member)}</div>`).join('')}</details>`;
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
  const duration = Math.max(1, ...times), width = Math.max(900, duration * 120) * state.timelineZoom, pixels = width / duration;
  const tick = duration > 20 ? Math.ceil(duration / 20) : duration > 5 ? 1 : 0.25;
  const ticks = Array.from({ length: Math.floor(duration / tick) + 1 }, (_, index) => index * tick);
  const handles = state.draft?.canEdit ? '<span class="timeline-boundary" data-timeline-boundary="start" title="调整片段起点"></span><span class="timeline-boundary" data-timeline-boundary="end" title="调整片段终点"></span>' : '';
  const rows = timeline.tracks.map((track, index) => `<div class="timeline-row"><button class="timeline-track ${state.selectedTimeline === track.id ? 'selected' : ''}" data-timeline="${escapeHtml(track.id)}"><small>${index + 1} · ${escapeHtml(track.contract)}</small><strong>${escapeHtml(track.displayName)}</strong><span>${track.clips.length} 片段 · ${track.markers.length} 标记</span></button><div class="timeline-lane" data-timeline-track="${escapeHtml(track.id)}" style="width:${width}px">${ticks.map(time => `<i class="timeline-grid" style="left:${time * pixels}px"></i>`).join('')}${track.clips.map(clip => `<button class="timeline-clip ${state.selectedTimeline === clip.id ? 'selected' : ''}" data-timeline="${escapeHtml(clip.id)}" style="left:${seconds(clip.timeRange.startRaw) * pixels}px;width:${Math.max(4, (seconds(clip.timeRange.endRaw) - seconds(clip.timeRange.startRaw)) * pixels)}px" title="${escapeHtml(clip.displayName)} · ${seconds(clip.timeRange.startRaw).toFixed(4)}—${seconds(clip.timeRange.endRaw).toFixed(4)} 秒">${handles}<span>${escapeHtml(clip.displayName)}</span><small>${escapeHtml(clip.contract)}</small></button>`).join('')}${track.markers.map(marker => `<button class="timeline-marker ${state.selectedTimeline === marker.id ? 'selected' : ''}" data-timeline="${escapeHtml(marker.id)}" style="left:${seconds(marker.timeRaw) * pixels}px" title="标记 · ${seconds(marker.timeRaw).toFixed(4)} 秒">◆</button>`).join('')}</div></div>`).join('');
  $('document-view').innerHTML = `<div class="timeline-heading"><div><small>正式时间数据</small><h2>${escapeHtml(timeline.displayName)}</h2></div><span>${timeline.loop ? '循环' : '单次'} · ${duration.toFixed(3)} 秒 · ${timeline.tracks.length} 条轨道</span></div><div class="timeline-scroll"><div class="timeline-content" style="width:${width + 224}px"><div class="timeline-ruler"><div>作者时间 · 秒</div><div style="width:${width}px">${ticks.map(time => `<span style="left:${time * pixels}px">${time.toFixed(2)}</span>`).join('')}</div></div>${rows}</div></div><p class="timeline-note">选择片段或标记查看实际参数及图引用。时间轴显示作者时间；真实模拟结果位于运行观察。</p>`;
  $('document-view').querySelector('.timeline-heading').insertAdjacentHTML('beforeend',
    `<label class="timeline-zoom">查看比例<select data-timeline-zoom>${[0.5, 1, 2, 4, 8].map(zoom =>
      `<option value="${zoom}" ${zoom === state.timelineZoom ? 'selected' : ''}>${zoom * 100}%</option>`).join('')}</select></label>`);
  $('document-view').insertAdjacentHTML('beforeend', `<div class="timeline-bindings">${timeline.bindings.map(binding => `<button data-timeline="${escapeHtml(binding.id)}">绑定 ${escapeHtml(binding.id)}</button>`).join('')}</div>`);
  if (state.draft?.canEdit) {
    const ready = state.definitions.timelineAuthoring !== undefined;
    $('document-view').querySelector('.timeline-heading').insertAdjacentHTML('beforeend',
      `<button data-timeline-add-track ${ready ? '' : 'disabled'}>新建轨道</button>`);
    $('document-view').querySelector('.timeline-note').textContent = '拖动片段中部平移或放入可接收的轨道，跨轨插入末尾；拖动两端只调整对应起止时间，标记可平移。松开时将轨道与时间修改合为一次共同候选，取消拖动不提交。素材范围和其余时间参数保持原值，可在属性中精确编辑。';
    if (!ready) $('document-view').querySelector('.timeline-note').textContent +=
      ' 正式轨道作者字段及初始值尚未导出，新建轨道暂不可用。';
    $('document-view').insertAdjacentHTML('beforeend', '<p class="authoring-gap">新建标记、绑定及私有片段图尚缺共同创建描述；Section 尚缺共同文档结构与修改协议。现有标记、绑定和图引用可在属性中选择、编辑或下钻。</p>');
    bindTimelineDrag($('document-view').querySelector('.timeline-content'), timeline, state.definitions.timelineContracts, state.timeScale, pixels,
      applyTimelineEdits, error => report(error.message, true));
  }
  if (state.view.needsFit) {
    if (state.selectedTimeline) $('document-view').querySelector(`[data-timeline="${CSS.escape(state.selectedTimeline)}"]`)
      .scrollIntoView({ block: 'nearest', inline: 'center' });
    state.view.needsFit = false;
  }
}

function renderReferenceTable(table) {
  $('document-view').innerHTML = `<div class="variables-page reference-table"><div class="eyebrow">REFERENCED CONTENT</div><h1>外部引用记录</h1><p>${table.entries.length} 项原资源提供的记录。此页只读，字段来自当前根的引用文件；不重新求值外部资源或将其复制为本根作者内容。</p>${table.entries.map(entry => `<details><summary>${escapeHtml(entry.kind)}</summary>${renderValue(entry.source)}<div class="resource-reference-fields">${Object.entries(entry.fields).map(([key, value]) => `<div class="field"><label>${escapeHtml(key)}</label>${renderValue(value)}</div>`).join('')}</div></details>`).join('')}</div>`;
}

function renderVariables() {
  const graphs = state.snapshot.root.files.filter(file => file.kind === 'Graph').map(file => ({
    path: file.path, document: state.snapshot.files.find(source => source.path === file.path).document
  }));
  const variables = graphs.flatMap(graph => graph.document.variables.map(variable => ({ ...variable, graph })));
  $('document-view').innerHTML = `<div class="variables-page"><div class="eyebrow">BLACKBOARD DECLARATIONS</div><h1>变量与归属</h1><p>${variables.length} 条实际声明。默认值与声明分别显示，所属图保持原 owner。修改生成本会话候选，尚未保存或运行。</p><div class="variable-list">${variables.map(variable => `<article><header><strong>${escapeHtml(variable.key)}</strong><span title="${escapeHtml(variable.type)}">${escapeHtml(typeName(variable.type))}</span><button data-document="${escapeHtml(variable.graph.path)}">所属图 ↗</button></header><code>${escapeHtml(variable.id)}</code><div class="inspector-actions"><button data-edit-variable="${escapeHtml(variable.id)}" data-variable-document="${escapeHtml(variable.graph.path)}">编辑声明</button><button data-copy-variable="${escapeHtml(variable.id)}" data-variable-document="${escapeHtml(variable.graph.path)}">复制为新变量</button><button class="danger" data-delete-variable="${escapeHtml(variable.id)}" data-variable-document="${escapeHtml(variable.graph.path)}">删除声明</button></div><div class="variable-columns"><div><h3>默认值</h3>${renderValue(variable.defaultValue)}</div><div><h3>作用域、生命周期与输入声明</h3>${Object.entries(variable.declaration).map(([field, value]) => `<div class="nested-value"><label>${escapeHtml(field)}</label>${renderValue(value)}</div>`).join('')}</div></div></article>`).join('') || '<p class="muted">本内容根没有黑板声明。</p>'}</div></div>`;
  if (!state.draft?.canEdit) $('document-view').querySelectorAll('[data-edit-variable], [data-copy-variable], [data-delete-variable]')
    .forEach(button => { button.disabled = true; button.title = state.authoringProblem; });
}

async function guarded(action) {
  try { await action(); }
  catch (error) { if (error.name !== 'AbortError') report(error.message, true); }
}

$('roots').onclick = event => { const item = event.target.closest('[data-root]'); if (item) guarded(() => openRoot(item.dataset.root)); };
$('files').onclick = event => {
  const item = event.target.closest('[data-file]');
  if (item) { state.navigation = []; guarded(() => selectFile(item.dataset.file)); }
};
$('document-navigation').onclick = event => {
  if (event.target.closest('[data-navigation-back]')) {
    const source = state.navigation.pop();
    guarded(() => selectFile(source.path, source.element));
    return;
  }
  const item = event.target.closest('[data-document]');
  if (item) { state.navigation = []; guarded(() => selectFile(item.dataset.document, item.dataset.element)); }
};
$('document-view').onchange = event => {
  const zoom = event.target.closest('[data-timeline-zoom]');
  if (!zoom) return;
  const scroll = $('document-view').querySelector('.timeline-scroll');
  const horizontal = scroll.scrollLeft / state.timelineZoom, vertical = scroll.scrollTop;
  state.timelineZoom = Number(zoom.value);
  renderTimeline(state.selectedFile.document);
  const resized = $('document-view').querySelector('.timeline-scroll');
  resized.scrollLeft = horizontal * state.timelineZoom;
  resized.scrollTop = vertical;
};
$('document-view').onclick = event => {
  const port = event.target.closest('[data-port]');
  if (port) { guarded(() => choosePort(port)); return; }
  if (event.target.closest('[data-cancel-connection]')) { state.linkStart = null; renderDocument(); return; }
  if (event.target.closest('[data-timeline-add-track]')) { handleTimelineAction(event); return; }
  const variableButton = event.target.closest('[data-edit-variable], [data-copy-variable], [data-delete-variable]');
  if (variableButton) {
    const file = state.snapshot.files.find(item => item.path === variableButton.dataset.variableDocument);
    const identity = variableButton.dataset.editVariable || variableButton.dataset.copyVariable || variableButton.dataset.deleteVariable;
    const variable = file.document.variables.find(item => item.id === identity);
    if (variableButton.hasAttribute('data-delete-variable')) {
      guarded(async () => {
        await presentDraftOperation(() => state.draft.apply(file.document.id, [{ kind: 'delete-variable', variableId: identity }]));
      });
      return;
    }
    const copy = variableButton.hasAttribute('data-copy-variable');
    const edited = copy ? { ...structuredClone(variable), key: '' } : variable;
    editVariable(edited, async fields => {
      const operation = copy ? { kind: 'insert-variable', index: file.document.variables.length, variable: {
        id: `local:${crypto.randomUUID()}`, key: fields.key, type: fields.typeId,
        defaultValue: fields.defaultValue, declaration: fields.declaration
      } } : { kind: 'configure-variable', variableId: variable.id, ...fields };
      await presentDraftOperation(() => state.draft.apply(file.document.id, [operation]));
    }, state.references);
    return;
  }
  const reference = event.target.closest('[data-document]');
  if (reference) { guarded(() => followDocument(reference.dataset.document, reference.dataset.element)); return; }
  const timeline = event.target.closest('[data-timeline]');
  if (timeline) { selectElement('timeline', timeline.dataset.timeline); return; }
  const edge = event.target.closest('[data-edge]');
  if (edge) { selectElement('edge', edge.dataset.edge); return; }
  const node = event.target.closest('[data-node]');
  if (!node) return;
  selectElement('node', node.dataset.node);
};
$('document-view').onkeydown = event => {
  const port = event.target.closest('[data-port]'), edge = event.target.closest('[data-edge]');
  if ((event.key === 'Enter' || event.key === ' ') && (port || edge)) {
    event.preventDefault();
    event.stopPropagation();
    if (port) guarded(() => choosePort(port)); else selectElement('edge', edge.dataset.edge);
  } else if (event.key === 'Escape' && state.linkStart) {
    state.linkStart = null;
    renderDocument();
  }
};
$('inspector-content').onclick = event => {
  const reference = event.target.closest('[data-document]');
  if (reference) { guarded(() => followDocument(reference.dataset.document, reference.dataset.element)); return; }
  if (Array.isArray(state.selectedFile.document.tracks)) { handleTimelineAction(event); return; }
  const graph = state.selectedFile?.document;
  const node = state.selectedNode ? graph.nodes.find(item => item.id === state.selectedNode) : null;
  const edge = state.selectedEdge ? graph.edges.find(item => item.id === state.selectedEdge) : null;
  const selectNode = event.target.closest('[data-select-node]');
  if (selectNode) { selectElement('node', selectNode.dataset.selectNode); return; }
  const selectEdge = event.target.closest('[data-select-edge]');
  if (selectEdge) { selectElement('edge', selectEdge.dataset.selectEdge); return; }
  if (event.target.closest('[data-element-dependencies]')) {
    const query = new URLSearchParams({ path: state.selectedRoot.path, documentId: graph.id, elementId: (node || edge || graph).id });
    guarded(() => showReferences('本对象的实际引用', `${api()}/dependencies?${query}`, state.source, request));
    return;
  }
  if (event.target.closest('[data-configure-node]')) {
    editNodeFields(node, state.capabilities.get(node.capability), applyGraphEdits, state.references);
    return;
  }
  if (event.target.closest('[data-configure-transition]')) {
    editTransition(edge, state.definitions, state.references, applyGraphEdits);
    return;
  }
  const fieldButton = event.target.closest('[data-edit-field]');
  const inputField = event.target.closest('[data-edit-input]');
  const resetField = event.target.closest('[data-reset-field]');
  const edgeField = event.target.closest('[data-edit-edge-field]');
  if (inputField) {
    const id = inputField.dataset.editInput;
    const port = state.selectedFile.portShapes.get(node.id).ports.find(port => port.id === id.slice(6) && port.direction === 'Input');
    editField({ id, displayName: `${port.displayName || port.id} · 输入常量`, valueDefinition: state.definitions.valueTypes[port.valueType] },
      node.fields[id], value => applyGraphEdits([{ kind: 'set-node-field', nodeId: node.id, fieldId: id, value }]), state.references);
    return;
  }
  if (resetField) {
    guarded(() => applyGraphEdits([{ kind: 'remove-node-field', nodeId: node.id, fieldId: resetField.dataset.resetField }]));
    return;
  }
  if (edgeField) {
    const id = edgeField.dataset.editEdgeField;
    editField({ id }, edge.fields[id], value => applyGraphEdits([{ kind: 'set-edge-field', edgeId: edge.id, fieldId: id, value }]), state.references);
    return;
  }
  if (event.target.closest('[data-edge-order]')) {
    reorderConnections(graph, edge, edgeIds => applyGraphEdits([{ kind: 'reorder-edges',
      sourceNodeId: edge.sourceNode, sourcePortId: edge.sourcePort, edgeIds }]));
    return;
  }
  if (fieldButton) {
    const field = state.capabilities.get(node.capability).fields.find(item => item.id === fieldButton.dataset.editField);
    editField(field, node.fields[field.id] || field.defaultValue,
      value => applyGraphEdits([{ kind: 'set-node-field', nodeId: node.id, fieldId: field.id, value }]), state.references);
  } else if (event.target.closest('[data-rename-element]')) renameElement((node || graph).displayName,
    displayName => applyGraphEdits([node ? { kind: 'rename-node', nodeId: node.id, displayName } : { kind: 'rename-graph', displayName }]));
  else if (event.target.closest('[data-delete-node]')) guarded(() => applyGraphEdits([{ kind: 'delete-node', nodeId: node.id }]));
  else if (event.target.closest('[data-delete-edge]')) guarded(() => applyGraphEdits([{ kind: 'delete-edge', edgeId: event.target.closest('[data-delete-edge]').dataset.deleteEdge }]));
};
document.body.addEventListener('click', event => {
  const resource = event.target.closest('[data-resource-root]');
  const usage = event.target.closest('[data-reference-root]');
  if (resource) guarded(() => showReferenceResource(state.snapshot, { rootId: resource.dataset.resourceRoot,
    documentId: resource.dataset.resourceDocument, elementId: resource.dataset.resourceElement }, request, renderValue));
  else if (usage) {
    const query = new URLSearchParams({ rootId: usage.dataset.referenceRoot, documentId: usage.dataset.referenceDocument,
      elementId: usage.dataset.referenceElement });
    guarded(() => showReferences('实际引用位置', `${api()}/reference-uses?${query}`, state.source, request));
  } else {
    const reference = event.target.closest('.resource-dialog [data-document]');
    if (reference) {
      reference.closest('dialog').close();
      guarded(() => followDocument(reference.dataset.document, reference.dataset.element));
    }
  }
});

function handleTimelineAction(event) {
  const timeline = state.selectedFile.document;
  const target = state.selectedTimeline ? [...timeline.tracks.flatMap(track => [track, ...track.clips, ...track.markers]),
    ...timeline.bindings].find(item => item.id === state.selectedTimeline) : timeline;
  const track = timeline.tracks.find(track => track.id === target.id || [...track.clips, ...track.markers].some(item => item.id === target.id));
  const clip = track?.clips.find(clip => clip.id === target.id);
  const marker = track?.markers.find(marker => marker.id === target.id);
  const binding = timeline.bindings.find(binding => binding.id === target.id);
  const configure = (fields, displayName = target.displayName) => target === timeline ? { kind: 'configure-timeline-fields', fields }
    : clip ? { kind: 'configure-clip', trackId: track.id, clipId: clip.id, displayName, fields }
    : marker ? { kind: 'configure-marker', trackId: track.id, markerId: marker.id, displayName, timeRaw: marker.timeRaw, fields }
    : binding ? { kind: 'configure-binding', bindingId: binding.id, fields }
    : { kind: 'configure-track', trackId: track.id, displayName, fields };
  const field = event.target.closest('[data-timeline-field]');
  if (event.target.closest('[data-timeline-add-track]')) {
    createTrack(timeline, state.definitions, state.references, applyTimelineEdits);
  } else if (event.target.closest('[data-timeline-add-clip]')) {
    createClip(track, state.definitions, state.references, state.timeScale, applyTimelineEdits);
  } else if (event.target.closest('[data-timeline-configure-fields]')) {
    editTimelineFields(target, timelineObjectDefinition(target, state.definitions), state.references,
      fields => applyTimelineEdits([configure(fields)]));
  } else if (field) {
    const id = field.dataset.timelineField;
    const declaration = timelineObjectDefinition(target, state.definitions)?.fields.find(field => field.id === id);
    editField(declaration || { id }, target.fields[id] || declaration.defaultValue,
      value => applyTimelineEdits([configure({ ...target.fields, [id]: value })]), state.references);
  } else if (event.target.closest('[data-timeline-rename]')) {
    renameElement(target.displayName, displayName => applyTimelineEdits([target === timeline
      ? { kind: 'rename-timeline', displayName } : configure(target.fields, displayName)]));
  } else if (event.target.closest('[data-timeline-loop]')) guarded(() => applyTimelineEdits([{ kind: 'set-timeline-loop', loop: !timeline.loop }]));
  else if (event.target.closest('[data-timeline-range]')) editTimeRange(clip, state.timeScale, range =>
    applyTimelineEdits([{ kind: 'set-clip-range', trackId: track.id, clipId: clip.id, range }]));
  else if (event.target.closest('[data-timeline-marker-time]')) editMarkerTime(marker, state.timeScale, timeRaw =>
    applyTimelineEdits([{ ...configure(marker.fields), timeRaw }]));
  else if (event.target.closest('[data-timeline-move-clip]')) moveClip(timeline, track, clip,
    state.definitions.timelineContracts, edit => applyTimelineEdits([edit]));
  else if (event.target.closest('[data-timeline-track-move]')) guarded(() => applyTimelineEdits([{ kind: 'move-track', trackId: track.id,
    index: timeline.tracks.indexOf(track) + Number(event.target.closest('[data-timeline-track-move]').dataset.timelineTrackMove) }]));
  else if (event.target.closest('[data-timeline-delete]')) guarded(() => applyTimelineEdits([clip ? { kind: 'delete-clip', trackId: track.id, clipId: clip.id }
    : marker ? { kind: 'delete-marker', trackId: track.id, markerId: marker.id }
    : binding ? { kind: 'delete-binding', bindingId: binding.id } : { kind: 'delete-track', trackId: track.id }]));
}
$('draft-add-node').onclick = () => createNode(state.capabilities, state.selectedFile.document.role,
  node => applyGraphEdits([{ kind: 'insert-node', index: state.selectedFile.document.nodes.length, node }]));
$('draft-add-edge').onclick = () => createConnection(state.selectedFile.document, state.selectedFile.portShapes,
  edge => applyGraphEdits([{ kind: 'insert-edge', index: state.selectedFile.document.edges.length, edge }]));
$('draft-undo').onclick = () => editAction(() => state.draft.undo());
$('draft-redo').onclick = () => editAction(() => state.draft.redo());
$('draft-diff').onclick = () => showDocumentDiff(state.draft);
$('draft-discard').onclick = () => guarded(async () => { state.draft.discard(); await showDraft(); });
$('draft-save').onclick = () => editAction(() => state.draft.commit());
$('draft-query').onclick = () => editAction(() => state.draft.queryCommit());
$('draft-retry').onclick = () => editAction(() => state.draft.retryCommit());
async function editAction(action) {
  await guarded(() => presentDraftOperation(action));
}
async function presentDraftOperation(action) {
  const pending = action();
  renderDraftToolbar();
  try { await pending; await showDraft(); } finally { renderDraftToolbar(); }
}
window.addEventListener('beforeunload', event => {
  if (state.draft?.unsettled || state.draft?.busy) { event.preventDefault(); event.returnValue = ''; }
});
$('request-problem-close').onclick = () => { $('request-problem').hidden = true; };
$('search-roots').oninput = event => { state.query = event.target.value.trim().toLowerCase(); renderRoots(); };
$('search-files').oninput = renderFiles;
$('refresh-roots').onclick = () => guarded(() => loadRoots(true));
$('load-more').onclick = () => guarded(() => loadRoots());
$('reload-root').onclick = () => guarded(() => openRoot(state.selectedRoot.path));
$('root-dependencies').onclick = () => guarded(() => showReferences('本根的实际引用',
  `${api()}/dependencies?path=${encodeURIComponent(state.selectedRoot.path)}`, state.source, request));
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
  Object.values(reads).forEach(read => read?.abort());
  const reading = new AbortController(); reads.source = reading;
  $('refresh-roots').disabled = true;
  state.source = source;
  $('content-source').value = source;
  $('workspace-path').textContent = '目录尚未读取';
  state.roots = []; state.nextOffset = 0;
  state.selectedRoot = null;
  state.snapshot = null;
  state.draft = null;
  state.selectedFile = null;
  $('draft-toolbar').hidden = true;
  state.selectedNode = null;
  state.selectedEdge = null;
  state.selectedTimeline = null;
  $('search-files').value = '';
  $('files').innerHTML = '';
  $('document-actions').hidden = true;
  $('document-navigation').hidden = true;
  $('file-path').textContent = ''; $('file-revision').textContent = '';
  $('source-text').value = '';
  $('selection-name').textContent = '未选择内容';
  $('inspector-content').innerHTML = '';
  $('document-title').textContent = source === 'migrations' ? '选择迁移候选' : '选择内容根';
  $('document-view').innerHTML = `<div class="welcome"><div class="eyebrow">${source === 'migrations' ? 'MIGRATION REVIEW' : 'AUTHORING DOCUMENTS'}</div><h1>${source === 'migrations' ? '检查原资产的完整内容' : '打开正式作者文档'}</h1><p>${source === 'migrations' ? '候选用于核对迁移读取结果，原 Unity 作者资产仍是当前正式来源。选择左侧内容查看图、状态、时间轴和变量。' : '选择左侧作者根。尚未接收的迁移候选可从“内容来源”切换查看。'}</p></div>`;
  renderRoots(); $('root-count').textContent = '0 个内容根'; $('load-more').hidden = true;
  try {
    const info = await request(api(), undefined, reading.signal);
    state.timeScale = BigInt(info.timelineTimeScale);
    $('service-state').textContent = '服务已连接';
    $('workspace-path').textContent = info.path;
    await loadRoots();
    report(source === 'migrations' ? '迁移候选仅用于只读核对；正式接收后从作者工作区编辑。' : '请选择作者内容根。');
  } finally { if (reads.source === reading) $('refresh-roots').disabled = false; }
}

$('content-source').onchange = event => guarded(() => selectSource(event.target.value));

guarded(async () => {
  const query = new URLSearchParams(location.search);
  await selectSource(query.get('source') === 'migrations' ? 'migrations' : 'workspace');
  if (query.has('root')) {
    await openRoot(query.get('root'));
    if (query.get('tab') === 'variables' || query.get('tab') === 'source') { state.tab = query.get('tab'); showTab(); }
    if (query.has('file')) await selectFile(query.get('file'), query.get('element'));
    else renderDocument();
    if (query.has('element')) {
      const exact = new URLSearchParams({ path: state.selectedRoot.path,
        documentId: state.selectedFile.document.id, elementId: query.get('element') });
      const located = await request(`${api()}/element?${exact}`, undefined, reads.file.signal);
      report(`已定位磁盘文档对象 ${located.elementId}${query.has('pointer') ? ' · JSON 位置 ' + query.get('pointer') : ''}。`);
    }
  }
});
