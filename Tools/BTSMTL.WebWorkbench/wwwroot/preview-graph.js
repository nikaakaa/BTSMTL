import { html } from './preview-records.js';

const graphKey = graph => JSON.stringify([graph.actorId, graph.programId, graph.programRevision, graph.id]);
const callKey = call => JSON.stringify([call.actorId, call.programId, call.programRevision, call.invocationPath,
  call.parentInvocationPath, call.actionId, call.executionGeneration, call.invocationGeneration,
  call.parentInvocationGeneration, call.playbackId]);
const belongsTo = (call, graph) => call.actorId === graph.actorId && call.programId === graph.programId && call.programRevision === graph.programRevision;
const shortName = graph => graph.displayName === graph.id ? graph.id.slice(0, 8) : graph.displayName;

function layout(nodes, edges, ports) {
  const outgoing = new Map(nodes.map(node => [node.id, []]));
  const incoming = new Set();
  for (const edge of edges) {
    outgoing.get(edge.source.nodeId).push(edge.target.nodeId);
    incoming.add(edge.target.nodeId);
  }
  const ranks = new Map();
  const place = root => {
    if (ranks.has(root)) return;
    ranks.set(root, 0);
    const queue = [root];
    for (let cursor = 0; cursor < queue.length; cursor++) {
      const id = queue[cursor];
      for (const target of outgoing.get(id)) {
        if (ranks.has(target)) continue;
        ranks.set(target, ranks.get(id) + 1);
        queue.push(target);
      }
    }
  };
  nodes.filter(node => !incoming.has(node.id)).forEach(node => place(node.id));
  nodes.forEach(node => place(node.id));
  const rows = new Map();
  return nodes.map(node => {
    const column = ranks.get(node.id);
    const inputs = ports.filter(port => port.nodeId === node.id && port.direction === 'input');
    const outputs = ports.filter(port => port.nodeId === node.id && port.direction === 'output');
    const height = 38 + Math.max(inputs.length, outputs.length) * 24;
    const y = rows.get(column) ?? 30;
    rows.set(column, y + height + 35);
    return { ...node, x: 30 + column * 305, y, width: 230, height, inputs, outputs };
  });
}

export class PreviewGraph {
  constructor(container) {
    this.container = container;
    this.definitions = [];
    this.selectedGraph = '';
    this.selectedCall = '';
    this.selectedNode = '';
    this.viewKey = '';
    this.frame = null;
    container.innerHTML = `<header><strong>编译图</strong><select data-graph-choice aria-label="节点图" disabled></select><select data-graph-call aria-label="实际调用" disabled></select><button data-graph-fit title="适配节点图" aria-label="适配节点图" disabled>⊡</button></header>
      <div class="observed-graph-body"><div class="graph-viewport"><div class="graph-world" hidden><svg class="graph-edges" aria-label="编译连线"></svg><div class="graph-nodes"></div></div><div class="observation-empty">等待确认帧</div></div><section class="graph-inspector" aria-label="所选节点的本帧记录" hidden></section></div>`;
    this.canvas = container.querySelector('.graph-viewport');
    this.world = container.querySelector('.graph-world');
    this.inspector = container.querySelector('.graph-inspector');
    container.querySelector('[data-graph-choice]').onchange = event => {
      this.selectedGraph = event.target.value; this.selectedCall = ''; this.selectedNode = ''; this.render();
    };
    container.querySelector('[data-graph-call]').onchange = event => {
      this.selectedCall = event.target.value; this.selectedNode = ''; this.render();
    };
    container.querySelector('[data-graph-fit]').onclick = () => this.fit();
    this.canvas.onpointerdown = event => {
      if (this.world.hidden) return;
      if (event.button !== 1 && (event.button !== 0 || event.target.closest('.graph-node'))) return;
      event.preventDefault(); this.canvas.setPointerCapture(event.pointerId);
      this.drag = { x: event.clientX, y: event.clientY, originX: this.x, originY: this.y };
    };
    this.canvas.onpointermove = event => {
      if (!this.canvas.hasPointerCapture(event.pointerId)) return;
      this.x = this.drag.originX + event.clientX - this.drag.x;
      this.y = this.drag.originY + event.clientY - this.drag.y;
      this.transform();
    };
    this.canvas.onpointerup = event => {
      if (this.canvas.hasPointerCapture(event.pointerId)) this.canvas.releasePointerCapture(event.pointerId);
    };
    this.canvas.onwheel = event => {
      if (this.world.hidden) return;
      event.preventDefault();
      const bounds = this.canvas.getBoundingClientRect();
      const x = event.clientX - bounds.left, y = event.clientY - bounds.top;
      const scale = Math.max(0.2, Math.min(2, this.scale * Math.exp(-event.deltaY * 0.001)));
      this.x = x - (x - this.x) * scale / this.scale;
      this.y = y - (y - this.y) * scale / this.scale;
      this.scale = scale; this.transform();
    };
    this.resize = new ResizeObserver(() => { if (!this.world.hidden) this.fit(); });
    this.resize.observe(this.canvas);
  }

  close() { this.resize.disconnect(); }

  setDefinitions(definitions, availability) {
    this.definitions = definitions;
    const select = this.container.querySelector('[data-graph-choice]');
    select.innerHTML = definitions.map(graph => `<option value="${html(graphKey(graph))}" title="${html(`${graph.displayName}\n角色 ${graph.actorId}\n程序 ${graph.programId}\n版本 ${graph.programRevision}`)}">${html(shortName(graph))}</option>`).join('');
    select.disabled = !definitions.length;
    this.container.querySelector('[data-graph-fit]').disabled = !definitions.length;
    this.container.querySelector('.observation-empty').textContent = definitions.length ? '等待确认帧' :
      availability.find(channel => channel.channel === 'graph-definitions').reason;
    this.render();
  }

  showFrame(frame) { this.frame = frame; this.render(); }

  render() {
    if (!this.definitions.length) return;
    const route = this.frame?.route;
    const occurrences = route ? [...route.nodes, ...route.edges, ...route.ports.filter(sample => sample.sourceAvailable)] : [];
    if (!this.selectedGraph && occurrences.length) {
      const graph = this.definitions.find(graph => occurrences.some(item => item.graphId === graph.id && belongsTo(item.call, graph)));
      if (graph) this.selectedGraph = graphKey(graph);
    }
    const graph = this.definitions.find(graph => graphKey(graph) === this.selectedGraph) ?? this.definitions[0];
    const select = this.container.querySelector('[data-graph-choice]');
    select.value = graphKey(graph); select.title = `${graph.displayName}\n角色 ${graph.actorId}\n程序 ${graph.programId}`;
    if (!graph.nodes.length) {
      this.world.hidden = true;
      this.container.querySelector('.observation-empty').hidden = false;
      this.container.querySelector('.observation-empty').textContent = '此图未保留作者节点来源';
      return;
    }
    const records = occurrences.filter(item => item.graphId === graph.id && belongsTo(item.call, graph));
    this.calls = new Map(records.map(item => [callKey(item.call), item.call]));
    const paths = [...new Set(graph.nodes.map(node => node.invocationPath))];
    const callSelect = this.container.querySelector('[data-graph-call]');
    callSelect.innerHTML = this.calls.size ? [...this.calls].map(([key, call]) =>
      `<option value="${html(key)}" title="${html(key)}">${html(call.invocationPath)} · ${html(call.invocationGeneration)}</option>`).join('') :
      paths.map(path => `<option value="${html(path)}">${html(path)}</option>`).join('');
    if (![...callSelect.options].some(option => option.value === this.selectedCall)) this.selectedCall = callSelect.options[0].value;
    callSelect.value = this.selectedCall; callSelect.title = this.selectedCall;
    callSelect.disabled = callSelect.options.length < 2;
    const call = this.calls.get(this.selectedCall);
    const path = call ? call.invocationPath : this.selectedCall;
    this.records = records.filter(item => callKey(item.call) === this.selectedCall);
    const key = JSON.stringify([graphKey(graph), path]);
    if (this.viewKey !== key) {
      this.viewKey = key;
      const nodes = graph.nodes.filter(node => node.invocationPath === path);
      const ids = new Set(nodes.map(node => node.id));
      const edges = graph.edges.filter(edge => edge.invocationPath === path && edge.available &&
        edge.source.graphId === graph.id && edge.target.graphId === graph.id && ids.has(edge.source.nodeId) && ids.has(edge.target.nodeId));
      this.nodes = layout(nodes, edges, graph.ports.filter(port => port.invocationPath === path));
      this.edges = edges;
      this.mount();
    }
    this.highlight();
    this.showInspector();
    this.container.dataset.graphId = graph.id;
    this.container.dataset.graphActor = graph.actorId;
    this.container.dataset.graphProgram = graph.programId;
    this.container.dataset.graphRevision = graph.programRevision;
    this.container.dataset.graphCall = call ? this.selectedCall : '';
    this.container.dataset.graphFrame = this.frame?.hostFrame ?? '';
  }

  mount() {
    this.world.hidden = !this.nodes.length;
    const empty = this.container.querySelector('.observation-empty');
    empty.hidden = this.nodes.length > 0;
    empty.textContent = '此调用未保留可显示的节点来源';
    this.width = Math.max(0, ...this.nodes.map(node => node.x + node.width)) + 30;
    this.height = Math.max(0, ...this.nodes.map(node => node.y + node.height)) + 30;
    this.world.style.width = `${this.width}px`; this.world.style.height = `${this.height}px`;
    const nodeMap = new Map(this.nodes.map(node => [node.id, node]));
    const point = (endpoint, direction) => {
      const node = nodeMap.get(endpoint.nodeId);
      const ports = direction === 'input' ? node.inputs : node.outputs;
      const index = ports.findIndex(port => port.id === endpoint.portId);
      return { x: node.x + (direction === 'input' ? 0 : node.width), y: node.y + (index < 0 ? 16 : 44 + index * 24) };
    };
    const svg = this.container.querySelector('.graph-edges');
    svg.setAttribute('width', this.width); svg.setAttribute('height', this.height);
    svg.innerHTML = this.edges.map((edge, index) => {
      const from = point(edge.source, 'output'), to = point(edge.target, 'input');
      const bend = Math.max(55, Math.abs(to.x - from.x) * 0.45);
      return `<path data-graph-edge="${index}" data-edge-id="${html(edge.id)}" d="M${from.x},${from.y} C${from.x + bend},${from.y} ${to.x - bend},${to.y} ${to.x},${to.y}"><title>${html(`${edge.id} · ${edge.kind}`)}</title></path>`;
    }).join('');
    const port = (item, index) => `<div class="graph-port ${item.direction}${item.isTrigger ? ' trigger' : ''}" style="top:${32 + index * 24}px" data-port-id="${html(item.id)}" data-port-direction="${item.direction}"><i></i><span>${html(item.displayName)}</span><output></output></div>`;
    this.container.querySelector('.graph-nodes').innerHTML = this.nodes.map((node, index) =>
      `<article class="graph-node" data-graph-node="${index}" data-node-id="${html(node.id)}" style="left:${node.x}px;top:${node.y}px;width:${node.width}px;height:${node.height}px"><button class="graph-node-title" data-inspect-node="${index}" title="${html(`${node.displayName}\n${node.id}\n${node.invocationPath}`)}">${html(node.displayName)}</button>${node.inputs.map(port).join('')}${node.outputs.map(port).join('')}</article>`).join('');
    this.container.querySelectorAll('[data-inspect-node]').forEach(button => {
      button.onclick = () => { this.selectedNode = this.nodes[Number(button.dataset.inspectNode)].id; this.highlight(); this.showInspector(); };
    });
    this.fit();
  }

  highlight() {
    this.container.querySelectorAll('[data-graph-node]').forEach(element => {
      const node = this.nodes[Number(element.dataset.graphNode)];
      const events = this.records.filter(item => item.nodeId === node.id && !('portId' in item)).sort((a, b) => Number(a.sequence) - Number(b.sequence));
      element.classList.toggle('occurred', events.length > 0);
      element.classList.toggle('failed', events.some(item => item.result === 'Failure'));
      element.classList.toggle('inspected', this.selectedNode === node.id);
      element.dataset.occurrences = String(events.length);
      element.querySelector('.graph-node-title').title = `${node.displayName}\n${node.id}\n${events.map(item => `${item.sequence} · ${item.kind} · ${item.result} · ${item.generation}`).join('\n')}`;
      element.querySelectorAll('[data-port-id]').forEach(port => {
        const samples = this.records.filter(item => item.nodeId === node.id && item.portId === port.dataset.portId && item.direction === port.dataset.portDirection);
        const sample = samples.at(-1);
        port.querySelector('output').textContent = sample ? sample.value : '';
        port.title = sample ? `${sample.portId}\n${sample.valueType} · ${sample.value}\n${sample.sequence}` : port.dataset.portId;
        port.classList.toggle('sampled', samples.length > 0);
      });
    });
    this.container.querySelectorAll('[data-graph-edge]').forEach(element => {
      const edge = this.edges[Number(element.dataset.graphEdge)];
      const events = this.records.filter(item => item.edgeId === edge.id);
      element.classList.toggle('selected', events.some(item => item.selected));
      element.classList.toggle('passed', events.some(item => item.passed));
      element.classList.toggle('rejected', events.length > 0 && events.every(item => !item.passed));
      element.dataset.occurrences = String(events.length);
      element.querySelector('title').textContent = `${edge.id} · ${edge.kind}\n${events.map(item => `${item.sequence} · selected=${item.selected} · passed=${item.passed}`).join('\n')}`;
    });
  }

  showInspector() {
    this.inspector.hidden = !this.selectedNode;
    if (!this.selectedNode) { this.inspector.replaceChildren(); return; }
    const node = this.nodes.find(node => node.id === this.selectedNode);
    if (!node) { this.selectedNode = ''; this.inspector.hidden = true; return; }
    const records = this.records.filter(item => item.nodeId === node.id).sort((a, b) => Number(a.sequence) - Number(b.sequence));
    this.inspector.innerHTML = `<header><strong>${html(node.displayName)}</strong><button data-inspector-close aria-label="关闭节点记录">×</button></header><div>${records.length ? records.map(item =>
      `<article data-occurrence-sequence="${html(item.sequence)}">${'portId' in item ? `<strong>${html(item.portId)}</strong><output>${html(item.value)}</output><span>${html(item.direction)} · ${html(item.valueType)}</span>` : `<strong>${html(item.kind)}</strong><output>${html(item.result)}</output>`}<details><summary>发生记录</summary><pre>${html(JSON.stringify(item, null, 2))}</pre></details></article>`).join('') : '<p>本帧无发生记录</p>'}</div>`;
    this.inspector.querySelector('[data-inspector-close]').onclick = () => { this.selectedNode = ''; this.highlight(); this.showInspector(); };
  }

  fit() {
    this.scale = Math.min(1, this.canvas.clientWidth / this.width, this.canvas.clientHeight / this.height);
    this.x = (this.canvas.clientWidth - this.width * this.scale) / 2;
    this.y = (this.canvas.clientHeight - this.height * this.scale) / 2;
    this.transform();
  }

  transform() { this.world.style.transform = `translate(${this.x}px, ${this.y}px) scale(${this.scale})`; }
}
