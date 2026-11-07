// 节点图文档的存储格式（NodeCanvas 形式，对应 C# BtsmtlGraphDocument）：节点、连线、身份、名称、
// 端口默认值、转移载荷与执行出口身份的键只在这里出现；业务字段一律按宿主合同读写。
import { JsonFloat, cloneJson, sortedObject, type JsonObject, type JsonValue } from './skillJson';

// ── 节点 ──

export const nodesOf = (document: JsonValue) => (document as JsonObject).allNodes as JsonObject[];
export const replaceNodes = (document: JsonValue, nodes: JsonObject[]) => { (document as JsonObject).allNodes = nodes; };
export const nodeId = (node: JsonObject) => node.UID as string;
export const nodeType = (node: JsonObject) => node.type as string;
export const storedNodeName = (node: JsonObject) => node._name as string | undefined;
export const nodeLabel = (node: JsonObject) => storedNodeName(node) ?? nodeType(node);
export const findNode = (document: JsonValue | null, id: string): JsonObject | null =>
  document ? nodesOf(document).find(node => nodeId(node) === id) ?? null : null;

// 节点对象内由格式而不是类型合同持有的成员：名称与未连线输入端口的默认值。
export const nodeNameKey = '_name';
export const inputValuesKey = '_inputPortValues';
export const inputValueKeys = (portId: string) => [inputValuesKey, portId];

export function createNode(configuration: JsonObject, id: string, type: string, name?: string): JsonObject {
  return sortedObject({ ...cloneJson(configuration), UID: id, outConnections: [], type, ...(name === undefined ? {} : { _name: name }) });
}

// 复制节点：换身份，只保留指向复制集合内的连线并改指新身份。
export function copyNode(node: JsonObject, ids: Map<string, string>, connectionUid: () => string): JsonObject {
  return sortedObject({
    ...cloneJson(node), UID: ids.get(nodeId(node))!,
    outConnections: connectionsOf(node).filter(connection => ids.has(connectionTarget(connection)))
      .map(connection => ({ ...connection, UID: connectionUid(), targetNode: ids.get(connectionTarget(connection))! })),
  });
}

// ── 连线：端口连线带源／目标端口；状态机转移不带端口，带 Payload ──

export const connectionsOf = (node: JsonObject) => node.outConnections as JsonObject[];
export const replaceConnections = (node: JsonObject, connections: JsonObject[]) => { node.outConnections = connections; };
export const connectionId = (connection: JsonObject) => connection.UID as string;
export const connectionTarget = (connection: JsonObject) => connection.targetNode as string;
export const connectionSourcePort = (connection: JsonObject) => connection.sourcePortID as string | undefined;
export const connectionTargetPort = (connection: JsonObject) => connection.targetPortID as string | undefined;
export const transitionPayload = (connection: JsonObject) => (connection.Payload as JsonObject | undefined) ?? null;
export const payloadKey = 'Payload';

export function findConnection(document: JsonValue | null, id: string): { node: JsonObject; connection: JsonObject } | null {
  for (const node of document ? nodesOf(document) : []) {
    const connection = connectionsOf(node).find(item => connectionId(item) === id);
    if (connection) return { node, connection };
  }
  return null;
}

export const createPortConnection = (id: string, sourcePort: string, target: string, targetPort: string): JsonObject =>
  ({ UID: id, sourcePortID: sourcePort, targetNode: target, targetPortID: targetPort });

export const createTransition = (id: string, payload: JsonObject, target: string): JsonObject =>
  ({ Payload: sortedObject(payload), UID: id, targetNode: target });

export function retarget(connection: JsonObject, sourcePort: string, target: string, targetPort: string) {
  connection.sourcePortID = sourcePort;
  connection.targetNode = target;
  connection.targetPortID = targetPort;
}

// ── 执行出口列表（FlowOutputList）：每个元素以 m_Id 作为出口端口身份 ──

export const outputPortId = (element: JsonValue) =>
  element !== null && typeof element === 'object' && !Array.isArray(element) ? (element as JsonObject).m_Id as string | undefined : undefined;
export const withOutputPortId = (element: JsonObject, id: string): JsonObject => ({ ...element, m_Id: id });

// ── 布局文档：按节点身份存放位置，另有分组、平移与缩放 ──

export const layoutNodesOf = (layout: JsonValue) => (layout as JsonObject).allNodes as JsonObject;
export const emptyLayout = (): JsonObject =>
  ({ allNodes: {}, canvasGroups: [], translation: { x: new JsonFloat(0), y: new JsonFloat(0) }, zoomFactor: new JsonFloat(1) });
