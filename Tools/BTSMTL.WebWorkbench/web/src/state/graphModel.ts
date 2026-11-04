import type {
  EdgeOccurrence, ExecutionCall, GraphDefinition, GraphEdge, GraphNode, GraphPort, LoopScope, NodeOccurrence, PortSample, RouteFrame,
} from '../api/types';
import type { SkillDocumentGraph, SkillDocumentRelation } from '../api/authoring';
import type { WorkbenchState } from './store';

// 节点图面板与检查器共用：图与调用身份、分层排布、本帧路线记录筛选。

export const graphKey = (graph: GraphDefinition) => JSON.stringify([graph.actorId, graph.programId, graph.programRevision, graph.id]);
export const callKey = (call: ExecutionCall) => JSON.stringify([call.actorId, call.programId, call.programRevision, call.invocationPath,
  call.objectSlot, call.objectHandle, call.objectKind, call.definitionIndex, call.programIndex,
  call.actionId, call.executionGeneration, call.invocationGeneration, call.playbackId,
  call.parentInvocationPath, call.parentInvocationGeneration]);
export const callOwnerLabel = (call: ExecutionCall) => call.actorId ??
  `${call.objectKind ?? '对象类型未记录'} · ${call.objectHandle ?? '句柄未记录'}`;
export const callLabel = (call: ExecutionCall) => `${callOwnerLabel(call)} · 对象 ${call.objectHandle ?? '句柄未记录'} · 动作 ${call.actionId} · 执行 ${call.executionGeneration} · 调用 ${call.invocationGeneration} · ${call.invocationPath}` +
  (call.playbackId === '0' ? '' : ` · 播放 ${call.playbackId}`);
export interface DocumentObservationBinding {
  programs: ReadonlyMap<string, string> | null;
  dirty: boolean;
}
export const matchesDocumentCall = (call: ExecutionCall, binding: DocumentObservationBinding) =>
  !binding.dirty && binding.programs?.get(call.programId) === call.programRevision;

export function documentObservationNotice(frame: RouteFrame | null, binding: DocumentObservationBinding,
  selected: ExecutionCall | null = null): string | null {
  if (binding.dirty) return '作者内容已有修改，运行叠加已过期；原档案保留。';
  if (binding.programs === null || binding.programs.size === 0) return '尚未取得当前图的正式运行修订，运行叠加不可用。';
  const expired = '运行档案与当前内容修订不同，叠加已过期；原档案保留。';
  if (selected !== null) return matchesDocumentCall(selected, binding) ? null
    : binding.programs.has(selected.programId) ? expired : '所选调用不属于当前作者内容，运行叠加不可用。';
  if (frame === null) return null;
  let outdated = false;
  for (const records of [frame.nodes, frame.edges, frame.ports, frame.playbacks, frame.active])
    for (const record of records)
      if (record.call !== null && binding.programs.has(record.call.programId)) {
        if (record.call.programRevision === binding.programs.get(record.call.programId)) return null;
        outdated = true;
      }
  return outdated ? expired : '此帧未记录当前作者内容的实际调用，运行叠加不可用。';
}
export const compareSequence = (left: { sequence: string }, right: { sequence: string }) => {
  const a = BigInt(left.sequence), b = BigInt(right.sequence);
  return a < b ? -1 : a > b ? 1 : 0;
};
export const graphName = (graph: Pick<GraphDefinition, 'id' | 'displayName'>) =>
  graph.displayName === null ? '未命名图' : graph.displayName === graph.id ? graph.id.slice(0, 8) : graph.displayName;
// 作者没起名字时宿主给的是 C# 类型名（如 BtsmtlSkillConditionResultFlowNode）：照 Unity 显示类型名的习惯拆成单词，
// 去掉项目前缀 Btsmtl 与 FlowNode／Node 后缀；完整类型名由 nodeTypeName 给提示用。
const typeNamePattern = /^[A-Z][A-Za-z0-9]*Node$/;
export const nodeTypeName = (node: GraphNode) =>
  node.displayName !== null && typeNamePattern.test(node.displayName) ? node.displayName : null;
export function nodeName(node: GraphNode) {
  const type = nodeTypeName(node);
  if (type === null) return node.displayName ?? '未命名节点';
  return type.replace(/^Btsmtl/, '').replace(/(Flow)?Node$/, '')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2') || type;
}
export const nodeOccurrenceLabel = (item: NodeOccurrence) => item.kind === 'LoopIterationEntered' || item.kind === 'LoopIterationCompleted'
  ? `${item.kind} · 激活 ${item.generation ?? '未记录'} · 记录迭代号 ${item.loopIteration ?? '未记录'}` : `${item.kind} · 激活 ${item.generation ?? '未记录'}`;
export const loopScopeKey = (scope: LoopScope) => JSON.stringify([callKey(scope.call), scope.graphId, scope.nodeId, scope.generation, scope.iteration]);
export const loopScopeLabel = (scope: LoopScope) => `${scope.graphId}/${scope.nodeId} · 激活 ${scope.generation ?? '未记录'} · 迭代 ${scope.iteration}`;
export const portSampleLabel = (sample: PortSample) => `记录 ${sample.sequence} · 观察 ${sample.observationSequence ?? '未记录'} · 激活 ${sample.generation ?? '未记录'}`;
export const portSampleKey = (sample: PortSample) => JSON.stringify([sample.call === null ? null : callKey(sample.call),
  sample.graphId, sample.nodeId, sample.direction, sample.portId, sample.sequence, sample.observationSequence,
  sample.generation, sample.loopScopes?.map(loopScopeKey)]);

export function routeObservation({ runId, frame, hostFrame, sources, contentSelection, routeAvailability }:
  Pick<WorkbenchState, 'runId' | 'frame' | 'hostFrame' | 'sources' | 'contentSelection' | 'routeAvailability'>) {
  if (runId === null) return { kind: 'unavailable', reason: '选择运行以查看实际调用' } as const;
  if (frame === null || frame.hostFrame !== hostFrame)
    return { kind: 'unavailable', reason: routeAvailability.filter(item => !item.available || !item.complete)
      .map(item => item.reason).join('；') || '正在读取当前档案帧，尚无该帧的实际调用' } as const;
  if (frame.source === null || frame.source.execution.kind === null || frame.source.execution.identity === null)
    return { kind: 'unavailable', reason: '此档案帧未记录实际执行身份，调用观察不可用' } as const;
  const content = frame.source.content;
  if (content === null)
    return { kind: 'unavailable', reason: '此档案帧未记录正式内容身份，调用观察不可用；原档案保留' } as const;
  if (contentSelection !== null) {
    const expected = contentSelection.kind === 'scene'
      ? sources?.scenes.find(item => item.path === contentSelection.path)
      : sources?.timelines.find(item => item.content.package === contentSelection.package)?.content;
    if (expected === undefined)
      return { kind: 'unavailable', reason: '当前选择的正式内容来源尚未取得，运行叠加不可用' } as const;
    if (content.kind !== contentSelection.kind || content.revision !== expected.revision)
      return { kind: 'unavailable', reason: '运行帧采用的内容与当前来源种类或修订不同，叠加已过期；原档案保留' } as const;
    if ('timeline' in expected && content.documentId !== expected.timeline)
      return { kind: 'unavailable', reason: '运行帧采用的 Timeline 与当前选择不同，叠加已过期；原档案保留' } as const;
  }
  if (frame.route === null)
    return { kind: 'unavailable', reason: frame.availability.filter(item => !item.available || !item.complete)
      .map(item => item.reason).join('；') || '此档案帧没有调用记录' } as const;
  return { kind: 'ready', route: frame.route } as const;
}

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
  return [...route.nodes, ...route.edges, ...route.ports]
    .filter((item): item is RouteRecord => item.call !== null && item.graphId === graph.id && matchesDocumentCall(item.call, binding));
}

export interface RecordedGraphCall {
  graph: GraphDefinition;
  call: ExecutionCall;
}

export function recordedGraphCalls(frames: RouteFrame[], graphs: GraphDefinition[], binding: DocumentObservationBinding) {
  const calls = new Map<string, RecordedGraphCall & { frame: RouteFrame }>();
  const byId = new Map<string, GraphDefinition[]>();
  for (const graph of graphs) {
    const matches = byId.get(graph.id) ?? [];
    matches.push(graph);
    byId.set(graph.id, matches);
  }
  for (const frame of frames) {
    for (const record of [...frame.nodes, ...frame.edges, ...frame.ports]) {
      if (record.call === null || !matchesDocumentCall(record.call, binding)) continue;
      for (const graph of byId.get(record.graphId) ?? []) {
        const key = `${graphKey(graph)}:${callKey(record.call)}`;
        const previous = calls.get(key);
        if (previous === undefined || frame.seconds < previous.frame.seconds) calls.set(key, { graph, call: record.call, frame });
      }
    }
  }
  return [...calls.values()];
}

export function graphCallsAtFrame(recorded: ReturnType<typeof recordedGraphCalls>, current: RouteFrame | null,
  currentCalls: RecordedGraphCall[]): RecordedGraphCall[] {
  if (current === null) return [];
  const calls = new Map<string, RecordedGraphCall>(recorded.filter(item => item.frame.seconds <= current.seconds)
    .map(item => [`${graphKey(item.graph)}:${callKey(item.call)}`, item] as const));
  for (const item of currentCalls) {
    const key = `${graphKey(item.graph)}:${callKey(item.call)}`;
    if (!calls.has(key)) calls.set(key, item);
  }
  return [...calls.values()];
}

export const parentCallMatches = (parent: ExecutionCall, call: ExecutionCall) =>
  call.parentInvocationPath !== null && call.parentInvocationPath !== '' && call.parentInvocationGeneration !== null &&
  parent.actorId === call.actorId && parent.programId === call.programId && parent.programRevision === call.programRevision &&
  parent.objectSlot === call.objectSlot && parent.objectHandle === call.objectHandle &&
  parent.objectKind === call.objectKind && parent.definitionIndex === call.definitionIndex && parent.programIndex === call.programIndex &&
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

export type GraphNavigationTarget = Pick<Extract<SkillDocumentRelation['target'], { kind: 'graph' }>, 'id' | 'displayName' | 'role' | 'owner' | 'ownerName' | 'file'>;
export type NodeNavigationTarget = (GraphNavigationTarget & { kind: 'graph' })
  | Pick<Extract<SkillDocumentRelation['target'], { kind: 'timeline' }>, 'kind' | 'id' | 'displayName' | 'owner' | 'ownerName' | 'file'>;

export interface GraphHierarchy {
  graphs: Map<string, SkillDocumentGraph>;
  nodeTargets: Map<string, NodeNavigationTarget[]>;
  parents: Map<string, SkillDocumentGraph>;
  referrers: Map<string, SkillDocumentRelation[]>;
}

const authorGraphKey = (owner: string, id: string) => JSON.stringify([owner, id]);
const nodeTargetKey = (graph: GraphDefinition, nodeId: string) => JSON.stringify([graphKey(graph), nodeId]);

export function graphHierarchy(graphs: SkillDocumentGraph[], relations: SkillDocumentRelation[]): GraphHierarchy {
  const byAuthor = new Map(graphs.map(graph => [authorGraphKey(graph.owner, graph.id), graph]));
  const nodeTargets = new Map<string, NodeNavigationTarget[]>();
  const parents = new Map<string, SkillDocumentGraph>();
  const referrers = new Map<string, SkillDocumentRelation[]>();
  const addTarget = (graph: SkillDocumentGraph, nodeId: string, target: NodeNavigationTarget) => {
    const key = nodeTargetKey(graph, nodeId);
    const targets = nodeTargets.get(key) ?? [];
    if (!targets.some(item => item.kind === target.kind && item.owner === target.owner && item.id === target.id)) targets.push(target);
    nodeTargets.set(key, targets);
  };
  for (const relation of relations) {
    const source = byAuthor.get(authorGraphKey(relation.source.owner, relation.source.graphId));
    if (source) addTarget(source, relation.source.nodeId, relation.target);
    if (relation.target.kind !== 'graph') continue;
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
    addTarget(parent, graph.declaredOwner.nodeId, { ...graph, kind: 'graph' });
  }
  return { graphs: new Map(graphs.map(graph => [graphKey(graph), graph])), nodeTargets, parents, referrers };
}

export const nodeReferences = (hierarchy: GraphHierarchy, graph: GraphDefinition, nodeId: string) =>
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

export const isStateMachine = (graph: Pick<SkillDocumentGraph, 'role'>) => graph.role === 'StateMachine';

// 端口值类型色按样本的值类型判断。
export function portColor(valueType: string | undefined): string {
  const type = (valueType ?? '').toLowerCase();
  if (/bool/.test(type)) return 'var(--port-bool)';
  if (/vector|quaternion/.test(type)) return 'var(--port-vector)';
  if (/single|double|float|int|fp|number/.test(type)) return 'var(--port-float)';
  if (/enum|tag/.test(type)) return 'var(--port-enum)';
  if (type) return 'var(--port-asset)';
  // 没有采样值时不知道类型，不冒充浮点。
  return 'var(--port-unknown)';
}
