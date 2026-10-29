import type {
  EdgeOccurrence, ExecutionCall, Frame, GraphDefinition, GraphEdge, GraphNode, GraphPort, NodeOccurrence, PortSample,
} from '../api/types';

// 节点图面板与检查器共用：图与调用身份、分层排布、本帧路线记录筛选。

export const graphKey = (graph: GraphDefinition) => JSON.stringify([graph.actorId, graph.programId, graph.programRevision, graph.id]);
export const callKey = (call: ExecutionCall) => JSON.stringify([call.actorId, call.programId, call.programRevision, call.invocationPath,
  call.actionId, call.executionGeneration, call.invocationGeneration, call.playbackId]);
export const belongsTo = (call: ExecutionCall, graph: GraphDefinition) =>
  call.actorId === graph.actorId && call.programId === graph.programId && call.programRevision === graph.programRevision;
export const graphName = (graph: GraphDefinition) =>
  graph.displayName === null ? '未命名图' : graph.displayName === graph.id ? graph.id.slice(0, 8) : graph.displayName;
export const nodeName = (node: GraphNode) => node.displayName ?? '未命名节点';

// 调用路径已含程序与图身份。
export const nodeSelectId = (graph: GraphDefinition, node: GraphNode) => `${graph.actorId}/${node.invocationPath}/${node.id}`;

export function findGraphNode(graphs: GraphDefinition[] | null, selectId: string) {
  for (const graph of graphs ?? [])
    for (const node of graph.nodes)
      if (nodeSelectId(graph, node) === selectId) return { graph, node };
  return null;
}

export type RouteRecord = NodeOccurrence | EdgeOccurrence | PortSample;

export function graphRecords(frame: Frame | null, graph: GraphDefinition): RouteRecord[] {
  const route = frame?.route;
  if (!route) return [];
  return [...route.nodes, ...route.edges, ...route.ports.filter(sample => sample.sourceAvailable)]
    .filter(item => item.graphId === graph.id && belongsTo(item.call, graph));
}

export const isNodeOccurrence = (item: RouteRecord): item is NodeOccurrence => 'kind' in item && 'nodeId' in item;
export const isEdgeOccurrence = (item: RouteRecord): item is EdgeOccurrence => 'edgeId' in item;
export const isPortSample = (item: RouteRecord): item is PortSample => 'portId' in item;

export interface LaidNode {
  node: GraphNode;
  x: number;
  y: number;
  inputs: GraphPort[];
  outputs: GraphPort[];
  entry: boolean;
}

export const nodeWidth = 200;
export const nodeHeaderHeight = 38;
export const portRowHeight = 22;
const nodeHeight = (rows: number) => nodeHeaderHeight + rows * portRowHeight + 10;

export function visibleEdges(graph: GraphDefinition, path: string, ids: Set<string>): GraphEdge[] {
  return graph.edges.filter(edge => edge.invocationPath === path && edge.available && edge.source && edge.target &&
    edge.source.graphId === graph.id && edge.target.graphId === graph.id && ids.has(edge.source.nodeId) && ids.has(edge.target.nodeId));
}

// 有完整作者坐标时按作者坐标放置，否则按连线分层。
export function layoutGraph(graph: GraphDefinition, path: string): { nodes: LaidNode[]; edges: GraphEdge[]; authored: boolean } {
  const nodes = graph.nodes.filter(node => node.invocationPath === path);
  const ids = new Set(nodes.map(node => node.id));
  const edges = visibleEdges(graph, path, ids);
  const layout = graph.availability.find(channel => channel.channel === 'author-layout');
  const authored = !!layout?.available && layout.complete && nodes.every(node => node.position);
  const incoming = new Set(edges.map(edge => edge.target!.nodeId));
  const ranks = new Map<string, number>();
  if (!authored) {
    const outgoing = new Map(nodes.map(node => [node.id, [] as string[]]));
    for (const edge of edges) outgoing.get(edge.source!.nodeId)!.push(edge.target!.nodeId);
    const place = (root: string) => {
      if (ranks.has(root)) return;
      ranks.set(root, 0);
      const queue = [root];
      for (let cursor = 0; cursor < queue.length; cursor++) {
        const id = queue[cursor];
        for (const target of outgoing.get(id)!) {
          if (ranks.has(target)) continue;
          ranks.set(target, ranks.get(id)! + 1);
          queue.push(target);
        }
      }
    };
    nodes.filter(node => !incoming.has(node.id)).forEach(node => place(node.id));
    nodes.forEach(node => place(node.id));
  }
  const columns = new Map<number, number>();
  const ports = graph.ports.filter(port => port.invocationPath === path);
  return {
    authored, edges,
    nodes: nodes.map(node => {
      const inputs = ports.filter(port => port.nodeId === node.id && port.direction === 'input');
      const outputs = ports.filter(port => port.nodeId === node.id && port.direction === 'output');
      let x: number, y: number;
      if (authored) ({ x, y } = node.position!);
      else {
        const column = ranks.get(node.id)!;
        x = 30 + column * (nodeWidth + 80);
        y = columns.get(column) ?? 30;
        columns.set(column, y + nodeHeight(Math.max(inputs.length, outputs.length)) + 30);
      }
      return { node, x, y, inputs, outputs, entry: !incoming.has(node.id) };
    }),
  };
}

// ── 图层级：调用路径各段为“种类:身份”，身份等于图身份的段就是该图在层级中的位置 ──

const segmentsOf = (path: string) => path.split('/');
const segmentId = (segment: string) => segment.slice(segment.indexOf(':') + 1);
const segmentKind = (segment: string) => segment.slice(0, segment.indexOf(':'));

export interface GraphHierarchy {
  // 图的完整位置路径（以该图自身的段结尾）。
  location: Map<string, string>;
  // 子图：父图位置 + 父元素段（node／state／edge）→ 子图。
  children: Map<string, GraphDefinition[]>;
  // 节点通往的 Timeline：父图位置 + node 段 → Timeline 文档身份。
  timelines: Map<string, string>;
  byId: Map<string, GraphDefinition>;
}

const programKey = (graph: GraphDefinition) => `${graph.actorId}|${graph.programId}|${graph.programRevision}`;
const graphPaths = (graph: GraphDefinition) => [...graph.nodes.map(node => node.invocationPath), ...graph.edges.map(edge => edge.invocationPath)];

export function graphHierarchy(graphs: GraphDefinition[]): GraphHierarchy {
  const byId = new Map<string, GraphDefinition>();
  for (const graph of graphs) byId.set(`${programKey(graph)}|${graph.id}`, graph);
  const location = new Map<string, string>();
  const timelines = new Map<string, string>();
  for (const graph of graphs) {
    const program = programKey(graph);
    for (const path of graphPaths(graph)) {
      const parts = segmentsOf(path);
      for (let index = 0; index < parts.length; index++) {
        const id = segmentId(parts[index]);
        const key = `${program}|${id}`;
        if (byId.has(key) && !location.has(key)) location.set(key, parts.slice(0, index + 1).join('/'));
        if (segmentKind(parts[index]) === 'timeline' && index > 0 && segmentKind(parts[index - 1]) === 'node')
          timelines.set(`${program}|${parts.slice(0, index).join('/')}`, id);
      }
    }
  }
  // 没有出现在任何调用路径里的图是程序根，位置取自身节点路径。
  for (const graph of graphs) {
    const key = `${programKey(graph)}|${graph.id}`;
    if (!location.has(key)) location.set(key, graphPaths(graph)[0] ?? graph.programId);
  }
  const children = new Map<string, GraphDefinition[]>();
  for (const graph of graphs) {
    const program = programKey(graph);
    const parts = segmentsOf(location.get(`${program}|${graph.id}`)!);
    // 自身段之前最近的元素段（node／state／edge）是父元素，再往前最近的图段是父图。
    for (let index = parts.length - 2; index > 0; index--) {
      if (!['node', 'state', 'edge'].includes(segmentKind(parts[index]))) continue;
      for (let parent = index - 1; parent >= 0; parent--) {
        if (!byId.has(`${program}|${segmentId(parts[parent])}`)) continue;
        // 中间只隔调用说明（call）的才是直接子图；隔着 Timeline、片段的属于 Timeline。
        const between = parts.slice(index + 1, parts.length - 1).map(segmentKind);
        if (between.every(kind => kind === 'call')) {
          const key = `${program}|${parts.slice(0, index + 1).join('/')}`;
          const list = children.get(key);
          if (list) list.push(graph); else children.set(key, [graph]);
        }
        break;
      }
      break;
    }
  }
  return { location, children, timelines, byId };
}

export const graphLocation = (hierarchy: GraphHierarchy, graph: GraphDefinition) =>
  hierarchy.location.get(`${programKey(graph)}|${graph.id}`) ?? graph.programId;

// 节点或连线在层级中的元素段：状态机的节点是 state，其余是 node。
function elementPath(hierarchy: GraphHierarchy, graph: GraphDefinition, kind: 'node' | 'state' | 'edge', id: string) {
  return `${programKey(graph)}|${graphLocation(hierarchy, graph)}/${kind}:${id}`;
}

export function childGraphs(hierarchy: GraphHierarchy, graph: GraphDefinition, nodeId: string): GraphDefinition[] {
  return hierarchy.children.get(elementPath(hierarchy, graph, 'node', nodeId))
    ?? hierarchy.children.get(elementPath(hierarchy, graph, 'state', nodeId)) ?? [];
}

export const edgeChildGraphs = (hierarchy: GraphHierarchy, graph: GraphDefinition, edgeId: string) =>
  hierarchy.children.get(elementPath(hierarchy, graph, 'edge', edgeId)) ?? [];

export const nodeTimeline = (hierarchy: GraphHierarchy, graph: GraphDefinition, nodeId: string) =>
  hierarchy.timelines.get(elementPath(hierarchy, graph, 'node', nodeId)) ?? null;

// 节点或状态播放的 Timeline：状态本身不播放，取状态体里播放的那条。
export const playedTimeline = (hierarchy: GraphHierarchy, graph: GraphDefinition, nodeId: string) =>
  nodeTimeline(hierarchy, graph, nodeId) ?? childGraphs(hierarchy, graph, nodeId).flatMap(child =>
    child.nodes.map(node => nodeTimeline(hierarchy, child, node.id))).find(item => item) ?? null;

export interface Crumb {
  graph: GraphDefinition | null;
  label: string;
}

// 面包屑：从程序根到当前图，经过的图可点击返回；经由 Timeline 的图标出 Timeline 一级。
export function graphCrumbs(hierarchy: GraphHierarchy, graph: GraphDefinition): Crumb[] {
  const program = programKey(graph);
  const crumbs: Crumb[] = [];
  for (const part of segmentsOf(graphLocation(hierarchy, graph))) {
    const found = hierarchy.byId.get(`${program}|${segmentId(part)}`);
    if (found) crumbs.push({ graph: found, label: graphName(found) });
    else if (segmentKind(part) === 'timeline') crumbs.push({ graph: null, label: 'Timeline' });
  }
  return crumbs;
}

export const isStateMachine = (graph: GraphDefinition) => graph.edges.some(edge => edge.kind === 'Transition');

// 端口值类型色按样本的值类型判断。
export function portColor(valueType: string | undefined): string {
  const type = (valueType ?? '').toLowerCase();
  if (/bool/.test(type)) return 'var(--port-bool)';
  if (/single|double|float|int|fp|number|vector|quaternion/.test(type)) return 'var(--port-float)';
  if (/enum|tag/.test(type)) return 'var(--port-enum)';
  if (type) return 'var(--port-asset)';
  return 'var(--port-float)';
}
