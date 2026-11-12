// 预览输入（inputs/<方案>.json）：文档固定的一份，带一条输入 Timeline。
import type { AuthoringElements } from '../api/authoring';
import {
  authoringActions, get, graphPath, setDocument, skillElements,
  type JsonKey, type JsonMutation,
} from './authoring';
import { cloneJson, numberOf, setIn, type JsonObject, type JsonValue } from './skillJson';
import { findNode, nodeType, nodesOf } from './graphDocument';
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
};
