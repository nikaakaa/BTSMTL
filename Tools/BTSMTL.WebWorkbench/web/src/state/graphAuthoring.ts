// 节点图画布编辑：节点、连线、分组、复制粘贴与组合步骤，经作者文档原地编辑并进入撤销。
import type { GraphReferenceTarget, GraphTemplate, SkillElements } from '../api/authoring';
import type { VariableDeclarationChanges } from '../api/variables';
import {
  authoringActions, connectionsOf, createGraph, currentDocument, dropOrphans, get, graphLayoutKey, graphLayoutPath, graphPath, isDocumentGraph, nodesOf,
  referencedFiles, setDocument, skillElements, sortedObject, type AddedPort, type JsonKey, type JsonMutation,
} from './authoring';
import { editVariable } from './authoringVariables';
import { JsonFloat, cloneJson, numberOf, parseSkillJson, setIn, type JsonObject, type JsonValue } from './skillJson';

// 节点身份为 32 位十六进制，连线身份为带连字符的 UUID，与现有文件一致。
const nodeUid = () => crypto.randomUUID().replaceAll('-', '');
const connectionUid = () => crypto.randomUUID();

// 组合节点的一个步骤就是一个执行出口（身份 m_Id 即端口），步骤的条件与调度从该出口唯一的连线编辑。
export interface EdgeStep { nodeId: string; nodeType: string; member: string; index: number; step: JsonObject }

export function edgeStep(document: JsonValue | null, connectionId: string): EdgeStep | null {
  for (const node of document ? nodesOf(document) : []) {
    const connection = connectionsOf(node).find(item => item.UID === connectionId);
    if (!connection) continue;
    for (const [member, value] of Object.entries(node)) {
      if (!Array.isArray(value)) continue;
      const index = value.findIndex(item => item !== null && typeof item === 'object' && (item as JsonObject).m_Id === connection.sourcePortID);
      if (index >= 0) return { nodeId: node.UID as string, nodeType: node.type as string, member, index, step: value[index] as JsonObject };
    }
    return null;
  }
  return null;
}

export function edgeCondition(document: JsonValue | null, edgeId: string, machine: boolean) {
  if (!machine) {
    const step = edgeStep(document, edgeId);
    return step && { condition: (step.step.m_Condition as string | null | undefined) ?? null, step };
  }
  const connection = (document ? nodesOf(document).flatMap(connectionsOf) : []).find(item => item.UID === edgeId);
  return connection ? { condition: ((connection.Payload as JsonObject).m_Condition as string | null | undefined) ?? null, step: null } : null;
}

function graphLayout(document: JsonValue, graphId: string): JsonObject {
  const layout = document as JsonObject;
  if (isDocumentGraph(graphId)) return layout.layout as JsonObject;
  return (layout[graphId] ??= { allNodes: {}, canvasGroups: [], translation: { x: new JsonFloat(0), y: new JsonFloat(0) }, zoomFactor: new JsonFloat(1) }) as JsonObject;
}

const layoutNodes = (document: JsonValue, graphId: string) => graphLayout(document, graphId).allNodes as JsonObject;

const positionValue = (graphId: string, position: { x: number; y: number }): JsonObject =>
  ({ comments: null, ...(isDocumentGraph(graphId) ? { isBreakpoint: false } : {}),
    position: { x: new JsonFloat(Math.round(position.x)), y: new JsonFloat(Math.round(position.y)) } });

export interface GroupRect { x: number; y: number; width: number; height: number }

const rectValue = (rect: GroupRect): JsonObject => ({
  height: new JsonFloat(Math.round(rect.height)), width: new JsonFloat(Math.round(rect.width)),
  x: new JsonFloat(Math.round(rect.x)), y: new JsonFloat(Math.round(rect.y)),
});

function groupsOf(document: JsonValue, graphId: string): JsonObject[] {
  return (graphLayout(document, graphId).canvasGroups ??= []) as JsonObject[];
}

export interface CanvasGroup { index: number; name: string; rect: GroupRect; color: string | null }

// 布局中的分组；颜色全透明为 NodeCanvas 默认色。
export function graphGroups(layout: JsonValue | null, graphId: string): CanvasGroup[] {
  const groups = ((layout as JsonObject | null)?.[graphLayoutKey(graphId)] as JsonObject | undefined)?.canvasGroups as JsonObject[] | undefined;
  return (groups ?? []).map((group, index) => {
    const rect = group.rect as JsonObject, color = group.color as JsonObject | null;
    const alpha = color ? numberOf(color.a) : 0;
    return {
      index, name: (group.name as string | null) ?? '',
      rect: { x: numberOf(rect.x), y: numberOf(rect.y), width: numberOf(rect.width), height: numberOf(rect.height) },
      color: alpha > 0 ? `rgb(${numberOf(color!.r) * 255} ${numberOf(color!.g) * 255} ${numberOf(color!.b) * 255} / ${alpha})` : null,
    };
  });
}

// 复制缓冲：节点原对象、相对位置与端口，跨图粘贴时生成新身份。
let clipboard: { nodes: JsonObject[]; positions: Record<string, { x: number; y: number }>; ports: Record<string, AddedPort[]> } | null = null;

export const graphAuthoring = {
  async createOwnedReference({ graphId, nodeId, path, target }: {
    graphId: string; nodeId: string; path: string[];
    target: Extract<GraphReferenceTarget, { ownerBinding: 'ReferenceNode' }>;
  }): Promise<string> {
    const elements = await skillElements();
    const id = createGraph(elements, target.role, null, null, { OwnerGraphId: graphId, OwnerNodeId: nodeId });
    graphAuthoring.setNodeValues(graphId, nodeId, [{ keys: path, value: id }]);
    return id;
  },

  async createPage(role: string, ownership: GraphTemplate['ownership'], name: string,
    context?: { OwnerGraphId: string; OwnerNodeId: string }): Promise<string> {
    const elements = await skillElements();
    return createGraph(elements, role, name, ownership, context);
  },
  // 按类型描述新建节点：配置取描述的默认配置，端口取描述结果。
  addNode(graphId: string, type: string, configuration: JsonObject, ports: AddedPort[], position: { x: number; y: number }): string {
    const uid = nodeUid();
    authoringActions.edit(graphPath(graphId), document => {
      nodesOf(document).push(sortedObject({ ...cloneJson(configuration), UID: uid, outConnections: [], type }));
    });
    authoringActions.edit(graphLayoutPath(graphId), document => { layoutNodes(document, graphId)[uid] = positionValue(graphId, position); });
    setDocument({ addedPorts: { ...get().addedPorts, [uid]: ports } });
    return uid;
  },

  // 删除节点及所有指向它们的连线与布局；它们专属的状态体、条件图、状态机与 Timeline 不再被引用时一并删除。
  deleteNodes(graphId: string, nodeIds: string[]) {
    const removed = new Set(nodeIds);
    const source = currentDocument(graphPath(graphId));
    const seeds = source ? nodesOf(source).flatMap(node => removed.has(node.UID as string) ? referencedFiles(node)
      : connectionsOf(node).filter(connection => removed.has(connection.targetNode as string)).flatMap(referencedFiles)) : [];
    authoringActions.edit(graphPath(graphId), document => {
      const graph = document as JsonObject;
      graph.allNodes = nodesOf(graph).filter(node => !removed.has(node.UID as string));
      for (const node of nodesOf(graph)) node.outConnections = connectionsOf(node).filter(connection => !removed.has(connection.targetNode as string));
    });
    authoringActions.edit(graphLayoutPath(graphId), document => {
      const nodes = layoutNodes(document, graphId);
      for (const id of nodeIds) delete nodes[id];
    });
    dropOrphans(seeds);
  },

  // ── 状态机（照 Unity 原生 Skill FSM）：新状态带专属状态体；转移按序号排在源状态之后，条件图按需创建 ──

  async addState(graphId: string, configuration: JsonObject, position: { x: number; y: number }): Promise<string> {
    const elements = await skillElements();
    const uid = nodeUid();
    const body = createGraph(elements, 'StateBody', '状态内容');
    authoringActions.edit(graphPath(graphId), document => {
      nodesOf(document).push(sortedObject({
        ...cloneJson(configuration), UID: uid, _name: '新状态', m_Body: body, outConnections: [], type: 'BtsmtlSkillNativeState',
      }));
    });
    authoringActions.edit(graphLayoutPath(graphId), document => { layoutNodes(document, graphId)[uid] = positionValue(graphId, position); });
    return uid;
  },

  connectStates(graphId: string, elements: SkillElements, sourceId: string, targetId: string) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = nodesOf(document).find(item => item.UID === sourceId);
      if (!node) throw new Error(`状态机 ${graphId} 中没有状态 ${sourceId}`);
      const order = Math.max(-1, ...connectionsOf(node).map(connection => numberOf((connection.Payload as JsonObject).m_Order))) + 1;
      connectionsOf(node).push({
        Payload: sortedObject({ ...cloneJson(elements.transition.value), m_Order: order }), UID: connectionUid(), targetNode: targetId,
      });
    });
  },

  // 转移字段：Payload 内按键写值。
  setTransitionValue(graphId: string, connectionId: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(graphPath(graphId), document => {
      const connection = nodesOf(document).flatMap(connectionsOf).find(item => item.UID === connectionId);
      if (!connection) throw new Error(`状态机 ${graphId} 中没有转移 ${connectionId}`);
      setIn(connection.Payload as JsonObject, keys, value);
    });
  },

  // 给状态建状态体、给转移建条件图；移除时原图不再被引用就删除。
  async createStateBody(graphId: string, nodeId: string) {
    const elements = await skillElements();
    const body = createGraph(elements, 'StateBody', '状态内容');
    graphAuthoring.setNodeValues(graphId, nodeId, [{ keys: ['m_Body'], value: body }]);
  },

  async createCondition(graphId: string, connectionId: string) {
    const elements = await skillElements();
    const condition = createGraph(elements, 'ConditionRule', '转移条件');
    graphAuthoring.setTransitionValue(graphId, connectionId, ['m_Condition'], condition);
  },

  removeStateBody(graphId: string, nodeId: string, body: string) {
    graphAuthoring.setNodeValues(graphId, nodeId, [{ keys: ['m_Body'], value: null }]);
    dropOrphans([body]);
  },

  removeCondition(graphId: string, connectionId: string, condition: string) {
    graphAuthoring.setTransitionValue(graphId, connectionId, ['m_Condition'], null);
    dropOrphans([condition]);
  },

  // 组合步骤的条件从该步骤出发的连线新建；新条件默认成立，原本无条件的步骤在改图前照常进入。
  async createStepCondition(graphId: string, step: EdgeStep): Promise<string> {
    const elements = await skillElements();
    const condition = createGraph(elements, 'ConditionRule', '步骤条件');
    authoringActions.edit(graphPath(condition), document => {
      for (const node of nodesOf(document)) node._inputPortValues = { m_Result: true };
    });
    graphAuthoring.setNodeValues(graphId, step.nodeId, [{ keys: [step.member, step.index, 'm_Condition'], value: condition }]);
    return condition;
  },

  removeStepCondition(graphId: string, step: EdgeStep, condition: string) {
    graphAuthoring.setNodeValues(graphId, step.nodeId, [{ keys: [step.member, step.index, 'm_Condition'], value: null }]);
    dropOrphans([condition]);
  },

  connect(graphId: string, source: { nodeId: string; portId: string }, target: { nodeId: string; portId: string }) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = nodesOf(document).find(item => item.UID === source.nodeId);
      if (!node) throw new Error(`图 ${graphId} 中没有节点 ${source.nodeId}`);
      const duplicate = connectionsOf(node).some(connection => connection.sourcePortID === source.portId &&
        connection.targetNode === target.nodeId && connection.targetPortID === target.portId);
      if (duplicate) return;
      connectionsOf(node).push({ UID: connectionUid(), sourcePortID: source.portId, targetNode: target.nodeId, targetPortID: target.portId });
    });
  },

  reconnect(graphId: string, connectionId: string, source: { nodeId: string; portId: string }, target: { nodeId: string; portId: string }) {
    authoringActions.edit(graphPath(graphId), document => {
      const nodes = nodesOf(document);
      for (const owner of nodes) {
        const connections = connectionsOf(owner);
        const index = connections.findIndex(connection => connection.UID === connectionId);
        if (index < 0) continue;
        const connection = connections[index];
        if (owner.UID !== source.nodeId) {
          const node = nodes.find(item => item.UID === source.nodeId);
          if (!node) throw new Error(`图 ${graphId} 中没有节点 ${source.nodeId}`);
          connections.splice(index, 1);
          connectionsOf(node).push(connection);
        }
        connection.sourcePortID = source.portId;
        connection.targetNode = target.nodeId;
        connection.targetPortID = target.portId;
        return;
      }
      throw new Error(`图 ${graphId} 中没有连线 ${connectionId}`);
    });
  },

  disconnect(graphId: string, connectionIds: string[]) {
    const removed = new Set(connectionIds);
    const source = currentDocument(graphPath(graphId));
    const seeds = source ? nodesOf(source).flatMap(connectionsOf).filter(connection => removed.has(connection.UID as string)).flatMap(referencedFiles) : [];
    authoringActions.edit(graphPath(graphId), document => {
      for (const node of nodesOf(document)) node.outConnections = connectionsOf(node).filter(connection => !removed.has(connection.UID as string));
    });
    dropOrphans(seeds);
  },

  // 检查器字段：节点对象内按键路径写值（成员、端口默认值或节点名）。
  setNodeValues(graphId: string, nodeId: string, changes: readonly JsonMutation[]) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = nodesOf(document).find(item => item.UID === nodeId);
      if (!node) throw new Error(`图 ${graphId} 中没有节点 ${nodeId}`);
      for (const { keys, value } of changes) setIn(node, keys, value);
    });
  },

  // ── 分组（NodeCanvas CanvasGroup，存于布局）：名称、矩形、颜色、自动分组与备注 ──

  addGroup(graphId: string, rect: GroupRect, name: string) {
    authoringActions.edit(graphLayoutPath(graphId), document => {
      groupsOf(document, graphId).push({
        autoGroup: false, color: { a: new JsonFloat(0), b: new JsonFloat(0), g: new JsonFloat(0), r: new JsonFloat(0) },
        name, notes: null, rect: rectValue(rect),
      });
    });
  },

  updateGroup(graphId: string, index: number, patch: { name?: string; rect?: GroupRect }) {
    authoringActions.edit(graphLayoutPath(graphId), document => {
      const group = groupsOf(document, graphId)[index];
      if (!group) throw new Error(`图 ${graphId} 中没有第 ${index} 个分组`);
      if (patch.name !== undefined) group.name = patch.name;
      if (patch.rect) group.rect = rectValue(patch.rect);
    });
  },

  deleteGroups(graphId: string, indices: number[]) {
    const removed = new Set(indices);
    authoringActions.edit(graphLayoutPath(graphId), document => {
      const graph = graphLayout(document, graphId);
      graph.canvasGroups = groupsOf(document, graphId).filter((_, index) => !removed.has(index));
    });
  },

  addVariable: (ownerId: string, typeId: string, name?: string) =>
    editVariable(ownerId, { kind: 'create', typeId, ...(name === undefined ? {} : { name }) }),
  renameVariable: (ownerId: string, variableId: string, name: string) => editVariable(ownerId, { kind: 'rename', variableId, name }),
  setVariableValue: (ownerId: string, variableId: string, keys: JsonKey[], next: JsonValue) => editVariable(ownerId, source => {
    const current = source.m_Variables.find(variable => variable.m_Declaration.m_VariableId === variableId)!;
    const value = keys.length === 0 ? next : cloneJson(current.m_DefaultValue);
    if (keys.length !== 0) setIn(value, keys, next);
    return { kind: 'set-default', variableId, value };
  }),
  setVariablePublic: (ownerId: string, variableId: string, isPublic: boolean) => editVariable(ownerId, { kind: 'configure-public', variableId, isPublic }),
  setDeclarationValue: (ownerId: string, variableId: string, changes: VariableDeclarationChanges) =>
    editVariable(ownerId, { kind: 'configure-blackboard', variableId, changes }),
  removeVariable: (ownerId: string, variableId: string) => editVariable(ownerId, { kind: 'delete', variableId }),

  move(graphId: string, positions: Record<string, { x: number; y: number }>) {
    authoringActions.edit(graphLayoutPath(graphId), document => {
      const nodes = layoutNodes(document, graphId);
      for (const [id, position] of Object.entries(positions)) {
        const next = positionValue(graphId, position);
        nodes[id] = { ...next, ...(nodes[id] as JsonObject | undefined), position: next.position };
      }
    });
  },

  copy(graphId: string, nodeIds: string[], positions: Record<string, { x: number; y: number }>, ports: Record<string, AddedPort[]>) {
    const state = get();
    const path = graphPath(graphId);
    const document = state.edited[path] ?? (state.files[path] !== undefined ? parseSkillJson(state.files[path]) : null);
    if (!document) return 0;
    const wanted = new Set(nodeIds);
    clipboard = { nodes: nodesOf(document).filter(node => wanted.has(node.UID as string)).map(node => cloneJson(node)), positions, ports };
    return clipboard.nodes.length;
  },

  // 粘贴：新身份，复制集合内部的连线重接到新节点，指向集合外的连线丢弃；位置整体偏移。
  paste(graphId: string, offset: { x: number; y: number }): string[] {
    if (!clipboard) return [];
    const ids = new Map(clipboard.nodes.map(node => [node.UID as string, nodeUid()]));
    const nodes = clipboard.nodes.map(node => sortedObject({
      ...cloneJson(node), UID: ids.get(node.UID as string)!,
      outConnections: connectionsOf(node).filter(connection => ids.has(connection.targetNode as string))
        .map(connection => ({ ...connection, UID: connectionUid(), targetNode: ids.get(connection.targetNode as string)! })),
    }));
    const source = clipboard;
    authoringActions.edit(graphPath(graphId), document => { nodesOf(document).push(...nodes); });
    authoringActions.edit(graphLayoutPath(graphId), document => {
      const layout = layoutNodes(document, graphId);
      for (const [from, to] of ids) {
        const position = source.positions[from] ?? { x: 0, y: 0 };
        layout[to] = positionValue(graphId, { x: position.x + offset.x, y: position.y + offset.y });
      }
    });
    const ports = { ...get().addedPorts };
    for (const [from, to] of ids) ports[to] = source.ports[from] ?? [];
    setDocument({ addedPorts: ports });
    return [...ids.values()];
  },
};
