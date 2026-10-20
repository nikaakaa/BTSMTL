const $ = id => document.getElementById(id);
const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);
const storageKey = 'btsmtl.web-workbench.v1';
const nodeStyle = {
  Root: { name: '入口', symbol: '↳', color: '#83e3cf', description: '将流程交给一个子节点，作为整张图的执行入口。', group: '流程控制' },
  Sequence: { name: '顺序执行', symbol: '→', color: '#ada0f5', description: '按照连线顺序处理子节点。子节点的执行规则由 BTSMTL 核心决定。', group: '流程控制' },
  Selector: { name: '选择执行', symbol: '⋔', color: '#ada0f5', description: '按顺序尝试子节点，使用核心选择器的成功与失败语义。', group: '流程控制' },
  Parallel: { name: '并行组合', symbol: '⇉', color: '#8dbbfa', description: '将多个子节点交给核心并行组合操作。这里不表示多线程执行。', group: '流程控制' },
  Branch: { name: '条件分支', symbol: '⑂', color: '#eab877', description: '根据 Condition 的布尔值选择 True 或 False 出口。', group: '条件与结果' },
  ConditionResult: { name: '条件结果', symbol: '?', color: '#eab877', description: '把布尔输入转为核心流程结果。可编辑的输入名称来自核心端口合同。', group: '条件与结果' },
  Succeed: { name: '成功', symbol: '✓', color: '#83e3cf', description: '向父节点返回成功结果。', group: '条件与结果' }
};

function exampleWorkspace() {
  return {
    format: 'btsmtl-web-workspace/1', title: '信号路由', id: 'signal-routing', entry: 'entry', duration: 4,
    nodes: [
      { id: 'entry', code: 'Root', name: '流程入口', x: 40, y: 180, inputs: {} },
      { id: 'sequence', code: 'Sequence', name: '处理序列', x: 280, y: 180, inputs: {} },
      { id: 'branch', code: 'Branch', name: '信号是否就绪', x: 520, y: 70, inputs: { Condition: true } },
      { id: 'accepted', code: 'Succeed', name: '接受信号', x: 760, y: 20, inputs: {} },
      { id: 'rejected', code: 'ConditionResult', name: '返回条件结果', x: 760, y: 180, inputs: { m_Result: false } },
      { id: 'complete', code: 'Succeed', name: '完成处理', x: 520, y: 340, inputs: {} }
    ],
    edges: [
      { id: 'edge-1', source: 'entry', target: 'sequence', port: 'Out' },
      { id: 'edge-2', source: 'sequence', target: 'branch', port: 'Out' },
      { id: 'edge-3', source: 'branch', target: 'accepted', port: 'True' },
      { id: 'edge-4', source: 'branch', target: 'rejected', port: 'False' },
      { id: 'edge-5', source: 'sequence', target: 'complete', port: 'Out' }
    ],
    tracks: [
      { id: 'track-1', name: '阶段 A', color: '#ada0f5' },
      { id: 'track-2', name: '阶段 B', color: '#83e3cf' },
      { id: 'track-3', name: '信号', color: '#eab877' }
    ],
    clips: [
      { id: 'clip-1', track: 'track-1', name: '准备', start: 0.15, duration: 1.25 },
      { id: 'clip-2', track: 'track-1', name: '收尾', start: 2.6, duration: 1.1 },
      { id: 'clip-3', track: 'track-2', name: '处理窗口', start: 1.1, duration: 1.7 },
      { id: 'clip-4', track: 'track-3', name: '发出信号', start: 1.45, duration: 0.55 }
    ]
  };
}

let workspace = exampleWorkspace();
let selection = { kind: 'node', id: 'branch' };
let catalog = [];
let history = [];
let future = [];
let graphRevision = 0;
let lastResult = null;
let lastRequest = null;
let compilePending = false;
let connection = null;
let drag = null;
let view = { x: 0, y: 0, scale: 1 };
let cursor = 0;
let playing = false;
let lastFrame = 0;
let animationFrame = 0;
let toastTimer = 0;
const canvas = $('canvas');

function toast(text) {
  $('toast').textContent = text;
  $('toast').hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { $('toast').hidden = true; }, 3000);
}

function checkpoint() {
  history.push(structuredClone(workspace));
  if (history.length > 60) history.shift();
  future = [];
}

function changed(graph = true) {
  if (graph) {
    graphRevision++;
    $('console-title').textContent = '图已修改';
    $('console-meta').textContent = '等待重新编译';
    $('console-dot').className = 'status-dot';
  }
  $('dirty-indicator').hidden = false;
  $('save-state').textContent = '未保存修改';
  updateHistoryButtons();
}

function updateHistoryButtons() {
  $('undo').disabled = history.length === 0;
  $('redo').disabled = future.length === 0;
}

function undo() {
  if (!history.length) return;
  stopPlayback();
  future.push(structuredClone(workspace));
  workspace = history.pop();
  selection = { kind: 'document' };
  changed();
  render();
}

function redo() {
  if (!future.length) return;
  stopPlayback();
  history.push(structuredClone(workspace));
  workspace = future.pop();
  selection = { kind: 'document' };
  changed();
  render();
}

function render() {
  $('document-title').textContent = workspace.title;
  $('breadcrumb-title').textContent = workspace.title;
  $('graph-count').textContent = `${workspace.nodes.length} 节点 · ${workspace.edges.length} 连线`;
  renderNodes();
  renderEdges();
  renderTimeline();
  renderInspector();
  updateHistoryButtons();
}

function renderPalette() {
  const query = $('search').value.trim().toLowerCase();
  let previousGroup = '';
  $('palette').innerHTML = catalog.filter(item => item.code !== 'Root').filter(item => `${item.code} ${nodeStyle[item.code].name}`.toLowerCase().includes(query)).map(item => {
    const style = nodeStyle[item.code];
    const heading = style.group !== previousGroup ? `<div class="palette-group">${style.group}</div>` : '';
    previousGroup = style.group;
    return `${heading}<button class="palette-item" data-add="${item.code}" style="--node-color:${style.color}" aria-label="添加${style.name}"><span class="node-symbol">${style.symbol}</span><span><strong>${style.name}</strong><small>${item.code}</small></span><span class="palette-add">＋</span></button>`;
  }).join('');
  $('palette-count').textContent = `${catalog.length} 种`;
}

function nodeBody(node) {
  if (node.code === 'Branch') return `Condition <span class="bool">${node.inputs.Condition}</span>`;
  if (node.code === 'ConditionResult') return `m_Result <span class="bool">${node.inputs.m_Result}</span>`;
  if (node.code === 'Succeed') return '返回成功 <span class="bool">Success</span>';
  const children = workspace.edges.filter(edge => edge.source === node.id).length;
  return `${children} 个子节点 <span>Flow</span>`;
}

function renderNodes() {
  $('nodes').innerHTML = workspace.nodes.map(node => {
    const style = nodeStyle[node.code];
    const selected = selection.kind === 'node' && selection.id === node.id;
    const branch = node.code === 'Branch';
    const leaf = node.code === 'Succeed' || node.code === 'ConditionResult';
    return `<article class="graph-node ${selected ? 'selected' : ''} ${node.id === workspace.entry ? 'entry' : ''} ${branch ? 'branch-node' : ''}" data-node="${escapeHtml(node.id)}" style="left:${node.x}px;top:${node.y}px;--node-color:${style.color}" aria-label="${escapeHtml(node.name)}，${style.name}">
      <div class="node-header"><span class="node-symbol">${style.symbol}</span><div class="node-names"><strong class="node-title">${escapeHtml(node.name)}</strong><span class="node-type">${node.code}</span></div></div>
      <div class="node-body">${nodeBody(node)}</div>
      ${node.id !== workspace.entry ? `<button class="port input" data-in="${escapeHtml(node.id)}" aria-label="连接到${escapeHtml(node.name)}"></button>` : ''}
      ${!leaf ? `<button class="port output" data-out="${escapeHtml(node.id)}" data-port="${branch ? 'True' : 'Out'}" aria-label="从${escapeHtml(node.name)}${branch ? ' True' : ''}连出"></button>` : ''}
      ${branch ? `<span class="branch-label">True</span><span class="branch-label false">False</span><button class="port output branch-false" data-out="${escapeHtml(node.id)}" data-port="False" aria-label="从${escapeHtml(node.name)} False 连出"></button>` : ''}
    </article>`;
  }).join('');
}

function renderEdges() {
  const nodes = new Map(workspace.nodes.map(node => [node.id, node]));
  const childOrders = new Map();
  $('edges').innerHTML = workspace.edges.map(edge => {
    const source = nodes.get(edge.source), target = nodes.get(edge.target);
    const x1 = source.x + 174, y1 = source.y + (edge.port === 'False' ? 81 : 49), x2 = target.x, y2 = target.y + 49;
    const bend = Math.max(55, Math.abs(x2 - x1) * .45);
    const path = `M${x1},${y1} C${x1 + bend},${y1} ${x2 - bend},${y2} ${x2},${y2}`;
    const order = (childOrders.get(edge.source) || 0) + 1;
    childOrders.set(edge.source, order);
    const label = source.code === 'Branch' ? '' : String(order).padStart(2, '0');
    return `<path class="edge ${selection.kind === 'edge' && selection.id === edge.id ? 'selected' : ''}" d="${path}"></path><path class="edge-hit" data-edge="${escapeHtml(edge.id)}" d="${path}"></path><text class="edge-label" x="${x1 + 15}" y="${y1 - 9}">${label}</text>`;
  }).join('');
}

function applyView() {
  $('world').style.transform = `translate(${view.x}px,${view.y}px) scale(${view.scale})`;
  canvas.style.backgroundSize = `${20 * view.scale}px ${20 * view.scale}px`;
  canvas.style.backgroundPosition = `${view.x}px ${view.y}px`;
  $('zoom-value').textContent = `${Math.round(view.scale * 100)}%`;
}

function fit() {
  const minX = Math.min(...workspace.nodes.map(node => node.x));
  const minY = Math.min(...workspace.nodes.map(node => node.y));
  const width = Math.max(...workspace.nodes.map(node => node.x + 174)) - minX;
  const height = Math.max(...workspace.nodes.map(node => node.y + 110)) - minY;
  view.scale = Math.max(.35, Math.min(1.15, (canvas.clientWidth - 70) / width, (canvas.clientHeight - 105) / height));
  view.x = (canvas.clientWidth - width * view.scale) / 2 - minX * view.scale;
  view.y = (canvas.clientHeight - height * view.scale) / 2 - minY * view.scale + 6;
  applyView();
}

function zoom(factor, x = canvas.clientWidth / 2, y = canvas.clientHeight / 2) {
  const next = Math.max(.35, Math.min(1.7, view.scale * factor));
  view.x = x - (x - view.x) * next / view.scale;
  view.y = y - (y - view.y) * next / view.scale;
  view.scale = next;
  applyView();
}

function select(kind, id) {
  selection = { kind, id };
  document.querySelectorAll('.graph-node').forEach(element => element.classList.toggle('selected', kind === 'node' && element.dataset.node === id));
  document.querySelectorAll('.clip').forEach(element => element.classList.toggle('selected', kind === 'clip' && element.dataset.clip === id));
  renderEdges();
  renderInspector();
}

function field(label, id, value, type = 'text', extra = '') {
  return `<label class="field"><span>${label}</span><input id="${id}" type="${type}" value="${escapeHtml(value)}" ${extra}></label>`;
}

function renderInspector() {
  const element = $('inspector-content');
  $('selection-kind').textContent = selection.kind.toUpperCase();
  if (selection.kind === 'node') {
    const node = workspace.nodes.find(value => value.id === selection.id);
    const style = nodeStyle[node.code];
    const outgoing = workspace.edges.filter(edge => edge.source === node.id);
    element.innerHTML = `<div class="selection-card" style="--node-color:${style.color}"><span class="node-symbol">${style.symbol}</span><div><h2>${style.name}</h2><p>${node.code}</p></div></div>
      ${field('名称', 'node-name', node.name)}<div class="detail-row"><span>节点 ID</span><code>${escapeHtml(node.id)}</code></div>
      <div class="inspector-rule"></div><div class="section-label">输入参数</div>
      ${Object.entries(node.inputs).map(([key, value]) => `<label class="field"><span>${escapeHtml(key)}</span><span class="boolean-control"><span>${value ? 'True · 成立' : 'False · 不成立'}</span><input type="checkbox" data-input="${escapeHtml(key)}" ${value ? 'checked' : ''} aria-label="${escapeHtml(key)}"></span></label>`).join('') || '<p class="inspector-description">此节点没有值输入。通过连线组织子节点。</p>'}
      <div class="section-label">子节点顺序 <span class="subtle">${outgoing.length}</span></div>
      <ol class="outgoing-list">${outgoing.map((edge, index) => `<li><span>${String(index + 1).padStart(2, '0')}</span>${escapeHtml(workspace.nodes.find(item => item.id === edge.target).name)}${edge.port !== 'Out' ? ` <span>${edge.port}</span>` : ''}<button class="icon-button" data-order="${escapeHtml(edge.id)}" aria-label="上移子节点" ${index === 0 ? 'disabled' : ''}>↑</button></li>`).join('')}</ol>
      <div class="inspector-rule"></div><p class="inspector-description">${style.description}</p>
      <button class="danger-button" id="delete-selection" ${node.id === workspace.entry ? 'disabled' : ''}>删除节点</button>`;
    $('node-name').addEventListener('change', event => edit(() => { node.name = event.target.value; }, false));
    element.querySelectorAll('[data-input]').forEach(input => input.addEventListener('change', () => edit(() => { node.inputs[input.dataset.input] = input.checked; })));
    element.querySelectorAll('[data-order]').forEach(button => button.addEventListener('click', () => {
      const index = workspace.edges.findIndex(edge => edge.id === button.dataset.order);
      const previous = workspace.edges.findLastIndex((edge, ordinal) => ordinal < index && edge.source === node.id);
      edit(() => { [workspace.edges[index], workspace.edges[previous]] = [workspace.edges[previous], workspace.edges[index]]; });
    }));
    $('delete-selection').addEventListener('click', deleteSelection);
  } else if (selection.kind === 'clip') {
    const clip = workspace.clips.find(value => value.id === selection.id);
    const track = workspace.tracks.find(value => value.id === clip.track);
    element.innerHTML = `<div class="selection-card" style="--node-color:${track.color}"><span class="node-symbol">▥</span><div><h2>时间片段</h2><p>Timeline draft</p></div></div>
      ${field('片段名称', 'clip-name', clip.name)}<label class="field"><span>轨道</span><select id="clip-track">${workspace.tracks.map(item => `<option value="${item.id}" ${item.id === clip.track ? 'selected' : ''}>${escapeHtml(item.name)}</option>`).join('')}</select></label>
      <div class="inspector-rule"></div><div class="section-label">时间 · 秒</div><div class="field-row">${field('开始', 'clip-start', clip.start.toFixed(2), 'number', `min="0" max="${workspace.duration - clip.duration}" step="0.05"`)}${field('持续', 'clip-duration', clip.duration.toFixed(2), 'number', `min="0.05" max="${workspace.duration - clip.start}" step="0.05"`)}</div>
      <div class="detail-row"><span>结束位置</span><code>${(clip.start + clip.duration).toFixed(2)} s</code></div><div class="detail-row"><span>吸附间隔</span><code>0.05 s</code></div>
      <div class="inspector-rule"></div><p class="inspector-description">可拖动、裁剪并预览时间位置。此草稿尚未生成 BTSMTL Timeline 内容，也不表示图的实际执行记录。</p><button class="danger-button" id="delete-selection">删除片段</button>`;
    for (const key of ['name', 'track', 'start', 'duration']) $('clip-' + key).addEventListener('change', event => {
      if (!event.target.checkValidity()) { event.target.reportValidity(); return; }
      edit(() => { clip[key] = key === 'start' || key === 'duration' ? Number(event.target.value) : event.target.value; }, false);
    });
    $('delete-selection').addEventListener('click', deleteSelection);
  } else if (selection.kind === 'edge') {
    const edge = workspace.edges.find(value => value.id === selection.id);
    const source = workspace.nodes.find(node => node.id === edge.source);
    const target = workspace.nodes.find(node => node.id === edge.target);
    element.innerHTML = `<div class="selection-card" style="--node-color:#83e3cf"><span class="node-symbol">→</span><div><h2>流程连线</h2><p>Control flow · Child</p></div></div><div class="field"><span>从</span><p>${escapeHtml(source.name)} · ${edge.port}</p></div><div class="field"><span>到</span><p>${escapeHtml(target.name)}</p></div><p class="inspector-description">连接顺序会交给核心编译器。选中父节点可以调整子节点顺序。</p><button class="danger-button" id="delete-selection">删除连线</button>`;
    $('delete-selection').addEventListener('click', deleteSelection);
  } else {
    element.innerHTML = `<div class="selection-card" style="--node-color:#83e3cf"><span class="node-symbol">⌘</span><div><h2>工作区</h2><p>Engine-independent editor</p></div></div>${field('工作区名称', 'workspace-name', workspace.title)}<div class="detail-row"><span>数值类型</span><code>Float32</code></div><div class="detail-row"><span>图节点</span><code>${workspace.nodes.length}</code></div><div class="detail-row"><span>时间片段</span><code>${workspace.clips.length}</code></div><div class="inspector-rule"></div><p class="inspector-description">图编辑通过 HTTP 调用现有 BTSMTL 编译器。时间轴是独立的交互草稿。保存与导出包含这两部分内容。</p>`;
    $('workspace-name').addEventListener('change', event => edit(() => { workspace.title = event.target.value; }, false));
  }
}

function edit(action, graph = true) {
  checkpoint();
  action();
  changed(graph);
  render();
}

function addNode(code) {
  const definition = catalog.find(item => item.code === code);
  checkpoint();
  const id = crypto.randomUUID();
  workspace.nodes.push({ id, code, name: nodeStyle[code].name,
    x: Math.round((canvas.clientWidth / 2 - view.x) / view.scale - 87),
    y: Math.round((canvas.clientHeight / 2 - view.y) / view.scale - 45),
    inputs: Object.fromEntries(definition.inputs.map(input => [input.id, true])) });
  selection = { kind: 'node', id };
  changed();
  render();
  toast('节点已添加，点击端口连接到流程');
}

function deleteSelection() {
  if (selection.kind === 'document') return;
  if (selection.kind === 'node' && selection.id === workspace.entry) return;
  const graph = selection.kind !== 'clip';
  checkpoint();
  if (selection.kind === 'node') {
    workspace.nodes = workspace.nodes.filter(node => node.id !== selection.id);
    workspace.edges = workspace.edges.filter(edge => edge.source !== selection.id && edge.target !== selection.id);
  } else if (selection.kind === 'edge') workspace.edges = workspace.edges.filter(edge => edge.id !== selection.id);
  else workspace.clips = workspace.clips.filter(clip => clip.id !== selection.id);
  selection = { kind: 'document' };
  changed(graph);
  render();
}

function cancelConnection() {
  connection = null;
  $('connection-hint').hidden = true;
  document.querySelectorAll('.port.linking').forEach(port => port.classList.remove('linking'));
}

canvas.addEventListener('click', event => {
  const output = event.target.closest('[data-out]');
  const input = event.target.closest('[data-in]');
  if (output) {
    cancelConnection();
    connection = { source: output.dataset.out, port: output.dataset.port };
    output.classList.add('linking');
    $('connection-hint').hidden = false;
  } else if (input && connection) {
    const target = input.dataset.in;
    if (target === connection.source) { toast('节点不能连接到自己'); return; }
    const sourceNode = workspace.nodes.find(node => node.id === connection.source);
    checkpoint();
    workspace.edges = workspace.edges.filter(edge => edge.target !== target &&
      !(edge.source === connection.source && (sourceNode.code === 'Root' || sourceNode.code === 'Branch' && edge.port === connection.port)));
    workspace.edges.push({ id: crypto.randomUUID(), source: connection.source, target, port: connection.port });
    cancelConnection();
    selection = { kind: 'node', id: target };
    changed();
    render();
  }
});

canvas.addEventListener('pointerdown', event => {
  if (event.target.closest('button')) return;
  const nodeElement = event.target.closest('[data-node]');
  const edgeElement = event.target.closest('[data-edge]');
  if (edgeElement) { select('edge', edgeElement.dataset.edge); return; }
  if (nodeElement) {
    const node = workspace.nodes.find(item => item.id === nodeElement.dataset.node);
    select('node', node.id);
    drag = { kind: 'node', node, x: event.clientX, y: event.clientY, startX: node.x, startY: node.y, recorded: false };
  } else {
    cancelConnection();
    drag = { kind: 'pan', x: event.clientX, y: event.clientY, startX: view.x, startY: view.y };
  }
  canvas.setPointerCapture(event.pointerId);
  event.preventDefault();
});

canvas.addEventListener('pointermove', event => {
  if (!drag) return;
  const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
  if (drag.kind === 'pan') {
    view.x = drag.startX + dx; view.y = drag.startY + dy; applyView();
  } else {
    if (!drag.recorded && Math.abs(dx) + Math.abs(dy) > 2) { checkpoint(); drag.recorded = true; }
    if (!drag.recorded) return;
    drag.node.x = Math.round(drag.startX + dx / view.scale);
    drag.node.y = Math.round(drag.startY + dy / view.scale);
    const nodeElement = document.querySelector(`[data-node="${drag.node.id}"]`);
    nodeElement.style.left = `${drag.node.x}px`; nodeElement.style.top = `${drag.node.y}px`;
    renderEdges();
  }
});

function endNodeDrag() {
  if (drag?.kind === 'node' && drag.recorded) changed(false);
  drag = null;
}
canvas.addEventListener('pointerup', endNodeDrag);
canvas.addEventListener('pointercancel', endNodeDrag);
canvas.addEventListener('wheel', event => {
  event.preventDefault();
  const bounds = canvas.getBoundingClientRect();
  zoom(event.deltaY < 0 ? 1.08 : 1 / 1.08, event.clientX - bounds.left, event.clientY - bounds.top);
}, { passive: false });

function renderTimeline() {
  $('ruler').innerHTML = Array.from({ length: 9 }, (_, index) => `<span class="tick" style="left:${index * 12.5}%">${(workspace.duration * index / 8).toFixed(1)}${index === 8 ? ' s' : ''}</span>`).join('');
  $('tracks').innerHTML = workspace.tracks.map(track => `<div class="track-row"><div class="track-label" style="--track-color:${track.color}"><span class="track-dot"></span>${escapeHtml(track.name)}</div><div class="track-lane" data-track="${track.id}">${workspace.clips.filter(clip => clip.track === track.id).map(clip => `<div class="clip ${selection.kind === 'clip' && selection.id === clip.id ? 'selected' : ''}" data-clip="${clip.id}" style="left:${clip.start / workspace.duration * 100}%;width:${clip.duration / workspace.duration * 100}%;--clip-color:${track.color}" aria-label="${escapeHtml(clip.name)}，${clip.start.toFixed(2)} 到 ${(clip.start + clip.duration).toFixed(2)} 秒"><span class="clip-name">${escapeHtml(clip.name)}</span><span class="clip-resize" data-resize="end"></span></div>`).join('')}</div></div>`).join('');
  renderCursor();
}

function renderCursor() {
  cursor = Math.min(cursor, workspace.duration);
  $('playhead').style.left = `${cursor / workspace.duration * 100}%`;
  $('time-output').innerHTML = `${cursor.toFixed(2)} <span>/ ${workspace.duration.toFixed(2)} s</span>`;
  for (const clip of workspace.clips) document.querySelector(`[data-clip="${clip.id}"]`).classList.toggle('active', cursor >= clip.start && cursor < clip.start + clip.duration);
}

let timelineDrag = null;
$('timeline-content').addEventListener('pointerdown', event => {
  const clipElement = event.target.closest('[data-clip]');
  if (!clipElement) return;
  stopPlayback();
  const clip = workspace.clips.find(item => item.id === clipElement.dataset.clip);
  select('clip', clip.id);
  timelineDrag = { clip, x: event.clientX, start: clip.start, duration: clip.duration,
    resize: Boolean(event.target.closest('[data-resize]')), width: clipElement.parentElement.clientWidth, recorded: false };
  $('timeline-content').setPointerCapture(event.pointerId);
  event.preventDefault();
});
$('timeline-content').addEventListener('pointermove', event => {
  if (!timelineDrag) return;
  const dx = event.clientX - timelineDrag.x;
  if (!timelineDrag.recorded && Math.abs(dx) > 2) { checkpoint(); timelineDrag.recorded = true; }
  if (!timelineDrag.recorded) return;
  const delta = Math.round(dx / timelineDrag.width * workspace.duration * 20) / 20;
  const clip = timelineDrag.clip;
  if (timelineDrag.resize) clip.duration = Math.round(Math.max(.05, Math.min(workspace.duration - clip.start, timelineDrag.duration + delta)) * 100) / 100;
  else clip.start = Math.round(Math.max(0, Math.min(workspace.duration - clip.duration, timelineDrag.start + delta)) * 100) / 100;
  const element = document.querySelector(`[data-clip="${clip.id}"]`);
  element.style.left = `${clip.start / workspace.duration * 100}%`;
  element.style.width = `${clip.duration / workspace.duration * 100}%`;
  renderCursor();
});
function endTimelineDrag() {
  if (timelineDrag?.recorded) { changed(false); renderTimeline(); renderInspector(); }
  timelineDrag = null;
}
$('timeline-content').addEventListener('pointerup', endTimelineDrag);
$('timeline-content').addEventListener('pointercancel', endTimelineDrag);
$('ruler').addEventListener('pointerdown', event => {
  stopPlayback();
  const bounds = $('ruler').getBoundingClientRect();
  cursor = Math.max(0, Math.min(workspace.duration, (event.clientX - bounds.left) / bounds.width * workspace.duration));
  renderCursor();
});

function stopPlayback() {
  playing = false;
  cancelAnimationFrame(animationFrame);
  $('play').textContent = '▶';
  $('play').setAttribute('aria-label', '播放时间轴草稿');
}
function frame(time) {
  cursor += (time - lastFrame) / 1000;
  lastFrame = time;
  renderCursor();
  if (cursor >= workspace.duration) stopPlayback();
  else animationFrame = requestAnimationFrame(frame);
}
$('play').addEventListener('click', () => {
  if (playing) { stopPlayback(); return; }
  if (cursor >= workspace.duration) cursor = 0;
  playing = true;
  lastFrame = performance.now();
  $('play').textContent = 'Ⅱ';
  $('play').setAttribute('aria-label', '暂停时间轴草稿');
  animationFrame = requestAnimationFrame(frame);
});
$('rewind').addEventListener('click', () => { stopPlayback(); cursor = 0; renderCursor(); });
$('add-clip').addEventListener('click', () => {
  stopPlayback();
  const track = selection.kind === 'clip' ? workspace.clips.find(item => item.id === selection.id).track : workspace.tracks[0].id;
  const id = crypto.randomUUID();
  checkpoint();
  workspace.clips.push({ id, track, name: '新片段', start: Math.min(workspace.duration - .5, Math.round(cursor * 20) / 20), duration: .5 });
  selection = { kind: 'clip', id };
  changed(false);
  render();
});

function compileRequest() {
  return { id: workspace.id, entry: workspace.entry,
    nodes: workspace.nodes.map(({ id, code, inputs }) => ({ id, code, inputs })), edges: workspace.edges };
}

async function compile() {
  if (compilePending) return;
  compilePending = true;
  const revision = graphRevision;
  lastRequest = structuredClone(compileRequest());
  $('compile').disabled = true;
  $('compile').textContent = '编译中…';
  $('console-title').textContent = '正在调用核心编译器';
  $('console-meta').textContent = 'POST /api/compile';
  try {
    const response = await fetch('/api/compile', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(lastRequest) });
    if (!response.ok) throw new Error(`接口返回 HTTP ${response.status}：${await response.text()}`);
    lastResult = await response.json();
    const stale = revision !== graphRevision;
    $('console-dot').className = `status-dot ${stale ? '' : lastResult.success ? 'ok' : 'error'}`;
    $('console-title').textContent = stale ? '图已变更，请重新编译' : lastResult.success ? '核心编译通过' : '编译未通过';
    $('console-meta').textContent = lastResult.success ? `${lastResult.operationCount} 操作 · ${lastResult.edgeCount} 连线 · ${lastResult.constantCount} 常量` : '查看下方具体原因';
    $('console-detail').className = `console-detail ${lastResult.success ? '' : 'error'}`;
    $('console-detail').textContent = lastResult.success
      ? `${lastResult.compiler} · ${lastResult.revision.slice(0, 12)} · ${stale ? '结果对应之前提交的图' : '已生成内存编排内容；尚未执行'} `
      : lastResult.diagnostics.map(item => `${item.source} · ${item.message}`).join('\n');
  } catch (error) {
    lastResult = { success: false, error: error.message };
    $('console-dot').className = 'status-dot error';
    $('console-title').textContent = '接口调用失败';
    $('console-meta').textContent = '没有生成编译结果';
    $('console-detail').className = 'console-detail error';
    $('console-detail').textContent = error.message;
  } finally {
    compilePending = false;
    $('compile').disabled = false;
    $('compile').innerHTML = '<span>◇</span> 编译图';
  }
}

function save() {
  try {
    localStorage.setItem(storageKey, JSON.stringify(workspace));
    $('dirty-indicator').hidden = true;
    $('save-state').textContent = '草稿已保存在此浏览器';
    toast('工作区草稿已保存');
  } catch (error) { toast(`保存失败：${error.message}`); }
}

function showDialog(title, description, content) {
  $('dialog-title').textContent = title;
  $('dialog-description').textContent = description;
  $('dialog-content').textContent = typeof content === 'string' ? content : JSON.stringify(content, null, 2);
  $('detail-dialog').showModal();
}

function validateWorkspace(value) {
  if (value.format !== 'btsmtl-web-workspace/1') throw new Error('需要本工作台导出的 v1 工作区文件。');
  if (typeof value.title !== 'string' || typeof value.id !== 'string' || !Number.isFinite(value.duration) || value.duration <= 0) throw new Error('工作区名称、标识或时长不正确。');
  for (const name of ['nodes', 'edges', 'tracks', 'clips']) {
    if (!Array.isArray(value[name])) throw new Error(`${name} 必须是数组。`);
    const ids = value[name].map(item => item.id);
    if (ids.some(id => typeof id !== 'string' || !/^[a-zA-Z0-9_-]+$/.test(id)) || new Set(ids).size !== ids.length) throw new Error(`${name} 含不合法或重复的 ID。`);
  }
  const nodes = new Set(value.nodes.map(node => node.id));
  const tracks = new Set(value.tracks.map(track => track.id));
  if (!nodes.has(value.entry) || !tracks.size) throw new Error('入口或轨道缺失。');
  for (const node of value.nodes) {
    if (!Object.hasOwn(nodeStyle, node.code) || typeof node.name !== 'string' || !Number.isFinite(node.x) || !Number.isFinite(node.y) || typeof node.inputs !== 'object' || node.inputs === null || Object.values(node.inputs).some(input => typeof input !== 'boolean')) throw new Error(`节点 ${node.id} 的内容不正确。`);
  }
  for (const edge of value.edges) if (!nodes.has(edge.source) || !nodes.has(edge.target) || typeof edge.port !== 'string') throw new Error(`连线 ${edge.id} 的端点或端口不正确。`);
  for (const track of value.tracks) if (typeof track.name !== 'string' || !/^#[0-9a-f]{6}$/i.test(track.color)) throw new Error(`轨道 ${track.id} 的名称或颜色不正确。`);
  for (const clip of value.clips) if (typeof clip.name !== 'string' || !tracks.has(clip.track) || !Number.isFinite(clip.start) || !Number.isFinite(clip.duration) || clip.start < 0 || clip.duration < .05 || clip.start + clip.duration > value.duration + .00001) throw new Error(`片段 ${clip.id} 的轨道或时间范围不正确。`);
  return value;
}

$('import-file').addEventListener('change', async event => {
  const file = event.target.files[0];
  if (!file) return;
  try {
    const imported = validateWorkspace(JSON.parse(await file.text()));
    checkpoint();
    stopPlayback();
    cancelConnection();
    workspace = imported;
    selection = { kind: 'document' };
    cursor = 0;
    changed();
    render();
    fit();
    toast('已导入工作区，点击编译图检查核心结果');
  } catch (error) { toast(`导入失败：${error.message}`); }
  event.target.value = '';
});

$('export').addEventListener('click', () => {
  const blob = new Blob([JSON.stringify(workspace, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'btsmtl-workspace.json';
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
});
$('import').addEventListener('click', () => $('import-file').click());
$('api-info').addEventListener('click', () => showDialog('网页直接调用纯 BTSMTL', '本地服务引用同一份 .NET 编译核心。图提交与时间轴草稿相互独立，没有 Unity 或 3C 角色依赖。', {
  endpoints: { 'GET /api/core': '读取编译核心信息和正式值输入端口', 'POST /api/compile': '调用 BtsmtlGraphSemanticBuilder → BtsmtlOrchestrationGraphCompiler' },
  scope: '首版 7 种节点，树形 Child 连线，布尔常量输入；编译内容保留在本次请求内存中，不发布、不执行。',
  request: compileRequest()
}));
$('view-result').addEventListener('click', () => showDialog('核心编译结果', '这是最近一次接口返回。界面移动与命名不改变编译输入；时间轴草稿没有参与本次图编译。', lastResult || '尚未提交编译。'));
$('close-dialog').addEventListener('click', () => $('detail-dialog').close());
$('detail-dialog').addEventListener('click', event => { if (event.target === $('detail-dialog')) $('detail-dialog').close(); });
$('palette').addEventListener('click', event => { const button = event.target.closest('[data-add]'); if (button) addNode(button.dataset.add); });
$('search').addEventListener('input', renderPalette);
$('show-document').addEventListener('click', () => select('document'));
$('fit').addEventListener('click', fit);
$('zoom-in').addEventListener('click', () => zoom(1.15));
$('zoom-out').addEventListener('click', () => zoom(1 / 1.15));
$('undo').addEventListener('click', undo);
$('redo').addEventListener('click', redo);
$('save').addEventListener('click', save);
$('compile').addEventListener('click', compile);
document.addEventListener('keydown', event => {
  if (event.key === 'Escape') cancelConnection();
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') { event.preventDefault(); save(); return; }
  if (event.target.closest('input,textarea,select,dialog')) return;
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') { event.preventDefault(); event.shiftKey ? redo() : undo(); }
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'y') { event.preventDefault(); redo(); }
  if (event.key === 'Delete' || event.key === 'Backspace') { event.preventDefault(); deleteSelection(); }
  if (event.code === 'Space') { event.preventDefault(); $('play').click(); }
});

async function boot() {
  try {
    const saved = localStorage.getItem(storageKey);
    if (saved !== null) { workspace = validateWorkspace(JSON.parse(saved)); selection = { kind: 'document' }; $('save-state').textContent = '已恢复此浏览器的草稿'; }
  } catch (error) {
    $('console-title').textContent = '保存的草稿读取失败';
    $('console-detail').textContent = error.message;
    $('console-detail').className = 'console-detail error';
    toast('保存的草稿无法读取，原数据仍保留在浏览器中');
    return;
  }
  render();
  fit();
  try {
    const response = await fetch('/api/core');
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const core = await response.json();
    catalog = core.operations;
    $('core-state').className = 'core-state connected';
    $('core-state').innerHTML = '<i></i>核心已连接';
    $('footer-core').textContent = `${core.compiler} · ${core.contract}`;
    renderPalette();
    $('compile').disabled = false;
    await compile();
  } catch (error) {
    $('core-state').className = 'core-state offline';
    $('core-state').innerHTML = '<i></i>核心未连接';
    $('console-title').textContent = '无法连接本地服务';
    $('console-detail').textContent = `请通过本地服务打开本页面。${error.message}`;
    $('console-detail').className = 'console-detail error';
  }
}

new ResizeObserver(() => { if (!drag) fit(); }).observe(canvas);
boot();
