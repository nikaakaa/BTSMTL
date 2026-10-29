import type {
  EdgeOccurrence, ExecutionCall, Frame, GraphDefinition, GraphEdge, GraphNode, GraphPort, NodeOccurrence, PortSample,
} from '../api/types';

// 节点图面板与检查器共用：图与调用身份、分层排布、本帧路线记录筛选（沿用 preview-graph.js）。

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

// 端口值类型色按样本的值类型判断。
export function portColor(valueType: string | undefined): string {
  const type = (valueType ?? '').toLowerCase();
  if (/bool/.test(type)) return 'var(--port-bool)';
  if (/single|double|float|int|fp|number|vector|quaternion/.test(type)) return 'var(--port-float)';
  if (/enum|tag/.test(type)) return 'var(--port-enum)';
  if (type) return 'var(--port-asset)';
  return 'var(--port-float)';
}
