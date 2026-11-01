import type {
  EdgeOccurrence, ExecutionCall, GraphDefinition, GraphEdge, GraphNode, GraphPort, NodeOccurrence, PortSample, RouteFrame,
} from '../api/types';
import type { SkillDocumentGraph, SkillDocumentRelation } from '../api/authoring';

// 节点图面板与检查器共用：图与调用身份、分层排布、本帧路线记录筛选。

export const graphKey = (graph: GraphDefinition) => JSON.stringify([graph.actorId, graph.programId, graph.programRevision, graph.id]);
export const callKey = (call: ExecutionCall) => JSON.stringify([call.actorId, call.programId, call.programRevision, call.invocationPath,
  call.actionId, call.executionGeneration, call.invocationGeneration, call.playbackId]);
export const callLabel = (call: ExecutionCall) => `${call.actorId} · 动作 ${call.actionId} · 执行 ${call.executionGeneration} · 调用 ${call.invocationGeneration}` +
  (call.playbackId === '0' ? '' : ` · 播放 ${call.playbackId}`);
export interface DocumentObservationBinding {
  programId: string | null;
  sourceRevision: string | null;
  dirty: boolean;
}
export const matchesDocumentCall = (call: ExecutionCall, binding: DocumentObservationBinding) =>
  !binding.dirty && binding.sourceRevision !== null &&
  call.programId === binding.programId && call.programRevision === binding.sourceRevision;
export const belongsTo = (call: ExecutionCall, graph: GraphDefinition, binding: DocumentObservationBinding) =>
  graph.programId === binding.programId && matchesDocumentCall(call, binding);
export const compareSequence = (left: { sequence: string }, right: { sequence: string }) => {
  const a = BigInt(left.sequence), b = BigInt(right.sequence);
  return a < b ? -1 : a > b ? 1 : 0;
};
export const graphName = (graph: Pick<GraphDefinition, 'id' | 'displayName'>) =>
  graph.displayName === null ? '未命名图' : graph.displayName === graph.id ? graph.id.slice(0, 8) : graph.displayName;
export const nodeName = (node: GraphNode) => node.displayName ?? '未命名节点';
export const nodeOccurrenceLabel = (item: NodeOccurrence) => item.kind === 'LoopIterationEntered' || item.kind === 'LoopIterationCompleted'
  ? `${item.kind} · 记录迭代号 ${item.loopIteration ?? '未记录'}` : item.kind;

// 调用路径已含程序与图身份。
export const nodeSelectId = (graph: GraphDefinition, node: Pick<GraphNode, 'invocationPath' | 'id'>) => `${graph.actorId}/${node.invocationPath}/${node.id}`;

export const edgeSelectId = (graph: GraphDefinition, edge: GraphEdge) => `${graph.actorId}/${edge.invocationPath}/${edge.id}`;

export function findGraphEdge(graphs: GraphDefinition[] | null, selectId: string) {
  for (const graph of graphs ?? [])
    for (const edge of graph.edges)
      if (edgeSelectId(graph, edge) === selectId) return { graph, edge };
  return null;
}

export function findGraphNode(graphs: GraphDefinition[] | null, selectId: string) {
  for (const graph of graphs ?? [])
    for (const node of graph.nodes)
      if (nodeSelectId(graph, node) === selectId) return { graph, node };
  return null;
}

export type RouteRecord = (NodeOccurrence | EdgeOccurrence | PortSample) & { call: ExecutionCall };

export function singleCall(records: RouteRecord[]): ExecutionCall | null {
  const calls = new Set(records.map(record => callKey(record.call)));
  return calls.size === 1 ? records[0].call : null;
}

export function graphRecords(route: RouteFrame | null | undefined, graph: GraphDefinition, binding: DocumentObservationBinding): RouteRecord[] {
  if (!route) return [];
  return [...route.nodes, ...route.edges, ...route.ports.filter(sample => sample.sourceAvailable)]
    .filter((item): item is RouteRecord => item.call !== null && item.graphId === graph.id && belongsTo(item.call, graph, binding));
}

export interface RecordedGraphCall {
  graph: GraphDefinition;
  call: ExecutionCall;
}

export function recordedGraphCalls(frames: RouteFrame[], current: RouteFrame | null, graphs: GraphDefinition[], binding: DocumentObservationBinding): RecordedGraphCall[] {
  if (current === null) return [];
  const calls = new Map<string, RecordedGraphCall>();
  const byId = new Map<string, GraphDefinition[]>();
  for (const graph of graphs.filter(item => item.programId === binding.programId)) {
    const matches = byId.get(graph.id) ?? [];
    matches.push(graph);
    byId.set(graph.id, matches);
  }
  for (const frame of [...frames, current]) {
    if (frame.seconds > current.seconds) continue;
    for (const record of [...frame.nodes, ...frame.edges, ...frame.ports.filter(item => item.sourceAvailable)]) {
      if (record.call === null || !matchesDocumentCall(record.call, binding)) continue;
      for (const graph of byId.get(record.graphId) ?? [])
        calls.set(`${graphKey(graph)}:${callKey(record.call)}`, { graph, call: record.call });
    }
  }
  return [...calls.values()];
}

export const parentCallMatches = (parent: ExecutionCall, call: ExecutionCall) =>
  call.parentInvocationPath !== null && call.parentInvocationPath !== '' && call.parentInvocationGeneration !== null &&
  parent.actorId === call.actorId && parent.programId === call.programId && parent.programRevision === call.programRevision &&
  parent.actionId === call.actionId && parent.executionGeneration === call.executionGeneration &&
  parent.invocationPath === call.parentInvocationPath && parent.invocationGeneration === call.parentInvocationGeneration;

export const parentGraphCalls = (call: ExecutionCall, calls: RecordedGraphCall[]) => calls.filter(item => parentCallMatches(item.call, call));

export const isNodeOccurrence = (item: RouteRecord): item is NodeOccurrence & { call: ExecutionCall } => 'kind' in item && 'nodeId' in item;
export const isEdgeOccurrence = (item: RouteRecord): item is EdgeOccurrence & { call: ExecutionCall } => 'edgeId' in item;
export const isPortSample = (item: RouteRecord): item is PortSample & { call: ExecutionCall } => 'portId' in item;

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

export type GraphNavigationTarget = Pick<SkillDocumentRelation['target'], 'id' | 'displayName' | 'role' | 'owner' | 'ownerName' | 'file'>;

export interface GraphHierarchy {
  graphs: Map<string, SkillDocumentGraph>;
  nodeTargets: Map<string, GraphNavigationTarget[]>;
  parents: Map<string, SkillDocumentGraph>;
  referrers: Map<string, SkillDocumentRelation[]>;
}

const authorGraphKey = (owner: string, id: string) => JSON.stringify([owner, id]);
const nodeTargetKey = (graph: GraphDefinition, nodeId: string) => JSON.stringify([graphKey(graph), nodeId]);

export function graphHierarchy(graphs: SkillDocumentGraph[], relations: SkillDocumentRelation[]): GraphHierarchy {
  const byAuthor = new Map(graphs.map(graph => [authorGraphKey(graph.owner, graph.id), graph]));
  const nodeTargets = new Map<string, GraphNavigationTarget[]>();
  const parents = new Map<string, SkillDocumentGraph>();
  const referrers = new Map<string, SkillDocumentRelation[]>();
  const addTarget = (graph: SkillDocumentGraph, nodeId: string, target: GraphNavigationTarget) => {
    const key = nodeTargetKey(graph, nodeId);
    const targets = nodeTargets.get(key) ?? [];
    if (!targets.some(item => item.owner === target.owner && item.id === target.id)) targets.push(target);
    nodeTargets.set(key, targets);
  };
  for (const relation of relations) {
    const source = byAuthor.get(authorGraphKey(relation.source.owner, relation.source.graphId));
    if (source) addTarget(source, relation.source.nodeId, relation.target);
    const key = authorGraphKey(relation.target.owner, relation.target.id);
    const uses = referrers.get(key) ?? [];
    uses.push(relation);
    referrers.set(key, uses);
  }
  for (const graph of graphs) {
    if (graph.declaredOwner === null) continue;
    const parent = byAuthor.get(authorGraphKey(graph.owner, graph.declaredOwner.graphId));
    if (!parent) continue;
    parents.set(graphKey(graph), parent);
    addTarget(parent, graph.declaredOwner.nodeId, graph);
  }
  return { graphs: new Map(graphs.map(graph => [graphKey(graph), graph])), nodeTargets, parents, referrers };
}

export const nodeGraphs = (hierarchy: GraphHierarchy, graph: GraphDefinition, nodeId: string) =>
  hierarchy.nodeTargets.get(nodeTargetKey(graph, nodeId)) ?? [];

export function graphReferrers(hierarchy: GraphHierarchy, graph: GraphDefinition) {
  const document = hierarchy.graphs.get(graphKey(graph))!;
  return hierarchy.referrers.get(authorGraphKey(document.owner, document.id)) ?? [];
}

export interface Crumb {
  graph: SkillDocumentGraph;
  label: string;
}

export function graphCrumbs(hierarchy: GraphHierarchy, graph: GraphDefinition): Crumb[] {
  const crumbs: Crumb[] = [];
  let current: SkillDocumentGraph | undefined = hierarchy.graphs.get(graphKey(graph))!;
  while (current !== undefined) {
    crumbs.unshift({ graph: current, label: graphName(current) });
    current = hierarchy.parents.get(graphKey(current));
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
