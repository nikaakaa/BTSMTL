// 预览输入（inputs/<方案>.json）：文档固定的一份，带一条输入 Timeline。
import { authoringApi, describedPortsAvailable, type AuthoringElements } from '../api/authoring';
import {
  authoringActions, currentDocument, get, graphPath, setDocument, skillElements, timelinePath, tracksOf,
  type JsonKey, type JsonMutation,
} from './authoring';
import { cloneJson, fpRaw, numberOf, setIn, type JsonObject, type JsonValue } from './skillJson';
import { connectionSourcePort, connectionTarget, connectionsOf, findNode, nodeType, nodesOf } from './graphDocument';
import { graphAuthoring } from './graphAuthoring';
import { selectionActions } from './store';
import { inputTimelineName, trackSelectId } from './timelineModel';


function planFrom(elements: AuthoringElements, name: string) {
  if (!elements.previewInput.availability.available)
    throw new Error(elements.previewInput.availability.reason!);
  const plan = cloneJson(elements.previewInput.plan!.value);
  for (const scheme of plan.Schemes as JsonObject[]) scheme.AuthoringId = crypto.randomUUID();
  const scheme = (plan.Schemes as JsonObject[])[numberOf(plan.Selected)];
  const timeline = authoringActions.createTimeline(elements.timeline, inputTimelineName);
  scheme.Name = name;
  scheme.InputTimeline = timeline;
  const id = crypto.randomUUID();
  authoringActions.setFile(`inputs/${id}.json`, plan);
  setDocument({ inputActorId: plan.Actor as string | null });
  return { id, timeline };
}

// 每个文档只有一份固定的预览输入，不另建输入 Timeline。
export const inputAuthoring = {
  // 文档的预览输入在添加第一条轨道时才建：输入方案、它的 Timeline 与这条轨道同一步写入。
  async createWithTrack(track: JsonObject, name: string) {
    const target = get().target;
    const elements = await skillElements({ kind: 'document' });
    if (get().target !== target) return;
    const { timeline } = planFrom(elements, '默认');
    const id = authoringActions.addTrack(timeline, track, name);
    selectionActions.select({ kind: 'track', id: trackSelectId(timeline, id) });
  },

  setNodeValues(graphId: string, nodeId: string, changes: readonly JsonMutation[], contract: AuthoringElements['previewInput']) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = findNode(document, nodeId)!;
      const at = (item: JsonObject) => contract.keyPath.reduce<JsonValue>((current, key) => (current as JsonObject)[key], item);
      for (const { keys, value } of changes) {
        if (keys.join('/') !== contract.keyPath.join('/')) setIn(node, keys, value);
        else {
          const previous = at(node);
          for (const writer of nodesOf(document).filter(item => nodeType(item) === contract.nodeType && at(item) === previous))
            setIn(writer, contract.keyPath, value);
        }
      }
    });
  },

  setValue(path: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(path, document => setIn(document, keys, value));
  },

  // 输入轨“外部事件”：在预览输入轨该时刻的标记图里写一串“预览条件”（成立／不成立）。没有预览输入时先建；
  // 该时刻已写过的含义直接改值，其余接在标记图执行链末尾。
  async writeConditions(timelineId: string | null, seconds: number, writes: { key: string; label: string; satisfied: boolean }[]) {
    const target = get().target;
    if (target === null) throw new Error('没有打开的作者文档');
    const elements = await skillElements({ kind: 'document' });
    if (get().target !== target) return;
    const timeline = timelineId ?? planFrom(elements, '默认').timeline;
    let tracks = tracksOf(currentDocument(timelinePath(timeline))!);
    if (tracks.length === 0) {
      // 输入轨承载片段图与标记图：取片段带图的逻辑轨道合同。
      const template = elements.tracks.find(item => item.contract.domains.includes('Logic') &&
        elements.clips.some(clip => item.contract.clipKinds.includes(clip.kind ?? clip.clrType) && 'm_AssetTree' in clip.value));
      if (template === undefined) throw new Error('作者合同没有可承载预览输入的逻辑轨道');
      authoringActions.addTrack(timeline, { ...template.value, m_ExecutionDomain: 'Logic' }, '预览条件');
      tracks = tracksOf(currentDocument(timelinePath(timeline))!);
    }
    const track = tracks[0];
    const raw = fpRaw(seconds);
    let marker = ((track.m_Markers as JsonObject[] | undefined) ?? []).find(item => item.m_TimeFPRawValue === raw);
    if (marker === undefined) {
      const id = await authoringActions.addMarkerAt(timeline, track.m_AuthoringId as string, seconds);
      marker = ((tracksOf(currentDocument(timelinePath(timeline))!)[0].m_Markers as JsonObject[]))
        .find(item => item.m_AuthoringId === id)!;
    }
    const graphId = marker.m_Graph as string;
    const graph = await authoringActions.resolveGraph(target, graphId);
    if (graph === undefined) throw new Error(`标记图 ${graphId} 读取失败`);
    // 执行链末尾：从标记图的回调入口沿执行连线走到最后一个节点。
    const stored = currentDocument(graphPath(graphId)) as JsonObject;
    const flowOut = (nodeId: string) => graph.ports.find(port => port.nodeId === nodeId && port.isTrigger && port.direction === 'output');
    const anchor = graph.nodes.find(node => flowOut(node.id) !== undefined && !nodesOf(stored)
      .some(item => connectionsOf(item).some(connection => connectionTarget(connection) === node.id)));
    if (anchor === undefined) throw new Error('标记图没有回调入口');
    const contract = elements.previewInput.condition;
    const type = contract.nodeType;
    // 链上已有的预览条件按含义记下，同一含义后写的生效。
    const written = new Map<string, string>();
    let tail = { nodeId: anchor.id, portId: flowOut(anchor.id)!.id };
    for (let next = connectionsOf(findNode(stored, tail.nodeId)!).find(item => connectionSourcePort(item) === tail.portId);
      next !== undefined; next = connectionsOf(findNode(stored, tail.nodeId)!).find(item => connectionSourcePort(item) === tail.portId)) {
      const nodeId = connectionTarget(next);
      const node = findNode(stored, nodeId)!;
      if (nodeType(node) === type)
        written.set(contract.conditionPath.reduce<JsonValue>((current, key) => (current as JsonObject)[key], node) as string, nodeId);
      const port = (get().addedPorts[nodeId] ?? []).find(item => item.isTrigger && item.direction === 'output')?.id ?? flowOut(nodeId)?.id;
      if (port === undefined) break;
      tail = { nodeId, portId: port };
    }
    writes = writes.filter(write => {
      const nodeId = written.get(write.key);
      if (nodeId === undefined) return true;
      graphAuthoring.setNodeValues(graphId, nodeId, [
        { keys: contract.labelPath, value: write.label }, { keys: contract.satisfiedPath, value: write.satisfied }]);
      return false;
    });
    if (writes.length === 0) return;
    const description = await authoringApi.describeType(target, type, graphId, authoringActions.editingView());
    if (!description.availability.available) throw new Error(description.availability.reason ?? '预览条件节点不可创建');
    if (!describedPortsAvailable(description.ports)) throw new Error(description.ports.availability.reason);
    const ports = description.ports.items.map(entry => ({
      id: entry.port.ID, displayName: entry.port.name, direction: entry.direction, isTrigger: entry.port.type === 'Flow', valueType: entry.port.type,
      objectKind: entry.port.objectKind, objectKindName: entry.port.objectKindName, acceptedObjectKinds: entry.port.acceptedObjectKinds,
    }));
    const input = ports.find(port => port.isTrigger && port.direction === 'input')!.id;
    const output = ports.find(port => port.isTrigger && port.direction === 'output')!.id;
    const origin = graph.nodes.find(node => node.id === anchor.id)?.position ?? { x: 0, y: 0 };
    writes.forEach((write, index) => {
      const configuration = cloneJson(description.configuration);
      setIn(configuration, contract.conditionPath, write.key);
      setIn(configuration, contract.labelPath, write.label);
      setIn(configuration, contract.satisfiedPath, write.satisfied);
      const id = graphAuthoring.addNode(graphId, type, configuration, ports, { x: origin.x + 260 * (index + 1), y: origin.y });
      graphAuthoring.connect(graphId, tail, { nodeId: id, portId: input });
      tail = { nodeId: id, portId: output };
    });
  },
};
