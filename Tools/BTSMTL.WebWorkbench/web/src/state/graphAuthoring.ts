// 节点图画布编辑：节点、连线、分组、复制粘贴与组合步骤，经作者文档原地编辑并进入撤销。
import type { GraphReferenceTarget, GraphTemplate, SkillElements, StorageShape } from '../api/authoring';
import type { VariableDeclarationChanges } from '../api/variables';
import {
  authoringActions, createGraph, currentDocument, dropOrphans, get, graphLayoutKey, graphLayoutPath, graphPath, isDocumentGraph,
  referencedFiles, setDocument, skillElements, type AddedPort, type JsonKey, type JsonMutation,
} from './authoring';
import { editVariable } from './authoringVariables';
import {
  connectionId, connectionSourcePort, connectionTarget, connectionTargetPort, connectionsOf, copyNode, createNode, createPortConnection, createTransition, findConnection,
  emptyLayout, findNode, layoutNodesOf, nodeId, nodeType, nodesOf, outputPortId, payloadKey, replaceConnections, replaceNodes, retarget, transitionPayload,
} from './graphDocument';
import { JsonFloat, cloneJson, numberOf, parseSkillJson, setIn, type JsonObject, type JsonValue } from './skillJson';

// 节点身份为 32 位十六进制，连线身份为带连字符的 UUID，与现有文件一致。
const nodeUid = () => crypto.randomUUID().replaceAll('-', '');
const connectionUid = () => crypto.randomUUID();

// 组合节点的一个步骤就是一个执行出口，步骤的条件与调度从该出口唯一的连线编辑；
// 出口列表成员与元素形状由宿主 flowOutputs 给出。
export interface EdgeStep { nodeId: string; nodeType: string; member: string; index: number; step: JsonObject; shape: StorageShape }

export function edgeStep(document: JsonValue | null, elements: SkillElements, edgeId: string): EdgeStep | null {
  const found = findConnection(document, edgeId);
  if (!found) return null;
  const port = connectionSourcePort(found.connection);
  for (const output of elements.flowOutputs.filter(item => item.type === nodeType(found.node))) {
    const list = found.node[output.member];
    if (!Array.isArray(list)) continue;
    const index = list.findIndex(item => outputPortId(item) === port);
    if (index >= 0) return {
      nodeId: nodeId(found.node), nodeType: nodeType(found.node), member: output.member, index, step: list[index] as JsonObject, shape: output.element,
    };
  }
  return null;
}

// 专属图槽位：节点或连线上按合同路径存放的图身份。keys 相对节点对象或连线对象；
// targets 来自字段的图引用声明，新建时按其角色与初始内容取宿主图模板。
export type SlotOwner = { kind: 'node'; nodeId: string } | { kind: 'connection'; connectionId: string };

export interface GraphSlot {
  graphId: string;
  owner: SlotOwner;
  keys: JsonKey[];
  label: string | null;
  optional: boolean;
  targets: GraphReferenceTarget[];
  value: string | null;
}

export const graphReferenceMembers = (shape: StorageShape | null | undefined) =>
  (shape?.members ?? []).filter(member => (member.authoring?.graphTargets.length ?? 0) !== 0);

const memberSlots = (graphId: string, owner: SlotOwner, base: JsonKey[], shape: StorageShape | null | undefined, value: JsonObject): GraphSlot[] =>
  graphReferenceMembers(shape).map(member => ({
    graphId, owner, keys: [...base, member.member], label: member.authoring!.displayName, optional: member.authoring!.optional,
    targets: member.authoring!.graphTargets, value: (value[member.member] as string | null | undefined) || null,
  }));

// 连线上的专属图：转移按宿主转移模板的形状，执行出口连线按所属节点出口列表的元素形状。
export function edgeGraphSlots(document: JsonValue | null, elements: SkillElements, graphId: string, edgeId: string): GraphSlot[] {
  const found = findConnection(document, edgeId);
  if (!found) return [];
  const payload = transitionPayload(found.connection);
  if (payload) return memberSlots(graphId, { kind: 'connection', connectionId: edgeId }, [payloadKey], elements.transition.shape, payload);
  const step = edgeStep(document, elements, edgeId);
  return step ? memberSlots(graphId, { kind: 'node', nodeId: step.nodeId }, [step.member, step.index], step.shape, step.step) : [];
}

// 槽位显示名：字段合同的显示名，没有时用目标角色的图模板名称（合同默认名称）。
export const slotLabel = (slot: GraphSlot, elements: SkillElements) => slot.label ??
  (elements.graphs.find(template => template.role === slot.targets[0]?.role)?.value.name as string | undefined) ?? slot.targets[0]?.role ?? '';

const ownerContext = (slot: GraphSlot, target: GraphReferenceTarget) => {
  if (target.ownerBinding !== 'ReferenceNode') return undefined;
  if (slot.owner.kind !== 'node') throw new Error(`${target.role} 必须由引用节点拥有`);
  return { OwnerGraphId: slot.graphId, OwnerNodeId: slot.owner.nodeId };
};

function graphLayout(document: JsonValue, graphId: string): JsonObject {
  const layout = document as JsonObject;
  if (isDocumentGraph(graphId)) return layout.layout as JsonObject;
  return (layout[graphId] ??= emptyLayout()) as JsonObject;
}

const layoutNodes = (document: JsonValue, graphId: string) => layoutNodesOf(graphLayout(document, graphId));

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
  // 专属图槽位新建：按目标的角色与初始内容取宿主模板（名称用合同默认名称），身份写入槽位；
  // 由引用节点拥有的图（状态机）带所属图与节点。
  async createOwnedGraph(slot: GraphSlot, target: GraphReferenceTarget): Promise<string> {
    const elements = await skillElements();
    const id = createGraph(elements, { role: target.role, seed: target.seed, context: ownerContext(slot, target) });
    graphAuthoring.setSlotValues(slot.graphId, slot.owner, [{ keys: slot.keys, value: id }]);
    return id;
  },

  // 移除：槽位清空，原图不再被引用就删除。
  removeOwnedGraph(slot: GraphSlot) {
    if (slot.value === null) return;
    graphAuthoring.setSlotValues(slot.graphId, slot.owner, [{ keys: slot.keys, value: null }]);
    dropOrphans([slot.value]);
  },

  setSlotValues(graphId: string, owner: SlotOwner, changes: readonly JsonMutation[]) {
    if (owner.kind === 'node') graphAuthoring.setNodeValues(graphId, owner.nodeId, changes);
    else graphAuthoring.setConnectionValues(graphId, owner.connectionId, changes);
  },

  async createPage(role: string, ownership: GraphTemplate['ownership'], name: string,
    context?: { OwnerGraphId: string; OwnerNodeId: string }): Promise<string> {
    const elements = await skillElements();
    return createGraph(elements, { role, ownership, name, context });
  },
  // 按类型描述新建节点：配置取描述的默认配置，端口取描述结果。
  addNode(graphId: string, type: string, configuration: JsonObject, ports: AddedPort[], position: { x: number; y: number }): string {
    const uid = nodeUid();
    authoringActions.edit(graphPath(graphId), document => {
      nodesOf(document).push(createNode(configuration, uid, type));
    });
    authoringActions.edit(graphLayoutPath(graphId), document => { layoutNodes(document, graphId)[uid] = positionValue(graphId, position); });
    setDocument({ addedPorts: { ...get().addedPorts, [uid]: ports } });
    return uid;
  },

  // 删除节点及所有指向它们的连线与布局；它们专属的状态体、条件图、状态机与 Timeline 不再被引用时一并删除。
  deleteNodes(graphId: string, nodeIds: string[]) {
    const removed = new Set(nodeIds);
    const source = currentDocument(graphPath(graphId));
    const seeds = source ? nodesOf(source).flatMap(node => removed.has(nodeId(node)) ? referencedFiles(node)
      : connectionsOf(node).filter(connection => removed.has(connectionTarget(connection))).flatMap(referencedFiles)) : [];
    authoringActions.edit(graphPath(graphId), document => {
      replaceNodes(document, nodesOf(document).filter(node => !removed.has(nodeId(node))));
      for (const node of nodesOf(document)) replaceConnections(node, connectionsOf(node).filter(connection => !removed.has(connectionTarget(connection))));
    });
    authoringActions.edit(graphLayoutPath(graphId), document => {
      const nodes = layoutNodes(document, graphId);
      for (const id of nodeIds) delete nodes[id];
    });
    dropOrphans(seeds);
  },

  // ── 状态机（照 Unity 原生 Skill FSM）：新状态按宿主状态模板创建，必填的专属图（状态内容）一并新建；
  //    转移按序号排在源状态之后，条件图按需从转移新建 ──

  async addState(graphId: string, position: { x: number; y: number }): Promise<string> {
    const elements = await skillElements();
    const uid = nodeUid();
    const { state } = elements;
    const slots = memberSlots(graphId, { kind: 'node', nodeId: uid }, [], state.shape, state.value)
      .filter(slot => !slot.optional && slot.targets.length === 1);
    const configuration = { ...state.value };
    for (const slot of slots) {
      const target = slot.targets[0];
      configuration[slot.keys[0] as string] = createGraph(elements, { role: target.role, seed: target.seed, context: ownerContext(slot, target) });
    }
    authoringActions.edit(graphPath(graphId), document => { nodesOf(document).push(createNode(configuration, uid, state.type, state.name)); });
    authoringActions.edit(graphLayoutPath(graphId), document => { layoutNodes(document, graphId)[uid] = positionValue(graphId, position); });
    return uid;
  },

  connectStates(graphId: string, elements: SkillElements, sourceId: string, targetId: string) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = findNode(document, sourceId);
      if (!node) throw new Error(`状态机 ${graphId} 中没有状态 ${sourceId}`);
      const order = Math.max(-1, ...connectionsOf(node).map(connection => numberOf(transitionPayload(connection)!.m_Order))) + 1;
      connectionsOf(node).push(createTransition(connectionUid(), { ...elements.transition.value, m_Order: order }, targetId));
    });
  },

  // 转移字段：转移载荷内按键写值。
  setTransitionValue(graphId: string, connectionId: string, keys: JsonKey[], value: JsonValue) {
    graphAuthoring.setConnectionValues(graphId, connectionId, [{ keys: [payloadKey, ...keys], value }]);
  },

  setConnectionValues(graphId: string, id: string, changes: readonly JsonMutation[]) {
    authoringActions.edit(graphPath(graphId), document => {
      const found = findConnection(document, id);
      if (!found) throw new Error(`图 ${graphId} 中没有连线 ${id}`);
      for (const { keys, value } of changes) setIn(found.connection, keys, value);
    });
  },

  connect(graphId: string, source: { nodeId: string; portId: string }, target: { nodeId: string; portId: string }) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = findNode(document, source.nodeId);
      if (!node) throw new Error(`图 ${graphId} 中没有节点 ${source.nodeId}`);
      const duplicate = connectionsOf(node).some(connection => connectionSourcePort(connection) === source.portId &&
        connectionTarget(connection) === target.nodeId && connectionTargetPort(connection) === target.portId);
      if (duplicate) return;
      connectionsOf(node).push(createPortConnection(connectionUid(), source.portId, target.nodeId, target.portId));
    });
  },

  reconnect(graphId: string, id: string, source: { nodeId: string; portId: string }, target: { nodeId: string; portId: string }) {
    authoringActions.edit(graphPath(graphId), document => {
      const found = findConnection(document, id);
      if (!found) throw new Error(`图 ${graphId} 中没有连线 ${id}`);
      if (nodeId(found.node) !== source.nodeId) {
        const node = findNode(document, source.nodeId);
        if (!node) throw new Error(`图 ${graphId} 中没有节点 ${source.nodeId}`);
        replaceConnections(found.node, connectionsOf(found.node).filter(connection => connection !== found.connection));
        connectionsOf(node).push(found.connection);
      }
      retarget(found.connection, source.portId, target.nodeId, target.portId);
    });
  },

  disconnect(graphId: string, connectionIds: string[]) {
    const removed = new Set(connectionIds);
    const source = currentDocument(graphPath(graphId));
    const seeds = source ? nodesOf(source).flatMap(connectionsOf).filter(connection => removed.has(connectionId(connection))).flatMap(referencedFiles) : [];
    authoringActions.edit(graphPath(graphId), document => {
      for (const node of nodesOf(document)) replaceConnections(node, connectionsOf(node).filter(connection => !removed.has(connectionId(connection))));
    });
    dropOrphans(seeds);
  },

  // 检查器字段：节点对象内按键路径写值（成员、端口默认值或节点名）。
  setNodeValues(graphId: string, id: string, changes: readonly JsonMutation[]) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = findNode(document, id);
      if (!node) throw new Error(`图 ${graphId} 中没有节点 ${id}`);
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
    clipboard = { nodes: nodesOf(document).filter(node => wanted.has(nodeId(node))).map(node => cloneJson(node)), positions, ports };
    return clipboard.nodes.length;
  },

  // 粘贴：新身份，复制集合内部的连线重接到新节点，指向集合外的连线丢弃；位置整体偏移。
  paste(graphId: string, offset: { x: number; y: number }): string[] {
    if (!clipboard) return [];
    const ids = new Map(clipboard.nodes.map(node => [nodeId(node), nodeUid()]));
    const nodes = clipboard.nodes.map(node => copyNode(node, ids, connectionUid));
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
