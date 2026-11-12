// 预览输入方案（inputs/<方案>.json）：每个方案带一条输入 Timeline。
import type { InputPlanContext, AuthoringElements } from '../api/authoring';
import {
  authoringActions, currentDocument, dropOrphans, get, graphPath, setDocument, skillElements,
  type JsonKey, type JsonMutation,
} from './authoring';
import { cloneJson, numberOf, setIn, sortedObject, type JsonObject, type JsonValue } from './skillJson';
import { findNode, nodeType, nodesOf } from './graphDocument';
import { selectionActions } from './store';
import { trackSelectId } from './timelineModel';


function planFrom(elements: AuthoringElements, name: string) {
  if (!elements.previewInput.availability.available)
    throw new Error(elements.previewInput.availability.reason!);
  const plan = cloneJson(elements.previewInput.plan!.value);
  for (const scheme of plan.Schemes as JsonObject[]) scheme.AuthoringId = crypto.randomUUID();
  const scheme = (plan.Schemes as JsonObject[])[numberOf(plan.Selected)];
  const timeline = authoringActions.createTimeline(elements.timeline, '预览输入');
  scheme.Name = name;
  scheme.InputTimeline = timeline;
  const id = crypto.randomUUID();
  authoringActions.setFile(`inputs/${id}.json`, plan);
  setDocument({ inputActorId: plan.Actor as string | null });
  return { id, timeline };
}

export const inputAuthoring = {
  async createPlan(name: string, context: InputPlanContext) {
    const created = planFrom(await skillElements(context), name);
    selectionActions.select(null);
    selectionActions.showTimeline(created.timeline, null, null);
    return created;
  },

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

  // 新方案复制所选方案的设置，配一条新的空输入 Timeline。
  async addScheme(path: string, name: string) {
    const elements = await skillElements();
    const timeline = authoringActions.createTimeline(elements.timeline, 'Timeline');
    authoringActions.edit(path, document => {
      const plan = document as JsonObject;
      const schemes = plan.Schemes as JsonObject[];
      const source = schemes[numberOf(plan.Selected)];
      schemes.push(sortedObject({ ...cloneJson(source), AuthoringId: crypto.randomUUID(), InputTimeline: timeline, Name: name }));
      plan.Selected = schemes.length - 1;
    });
    selectionActions.showTimeline(timeline, null, null);
  },

  removeScheme(path: string, index: number) {
    const plan = currentDocument(path) as JsonObject | null;
    const timeline = ((plan?.Schemes as JsonObject[] | undefined)?.[index]?.InputTimeline as string | undefined) ?? null;
    authoringActions.edit(path, document => {
      const target = document as JsonObject;
      const schemes = target.Schemes as JsonObject[];
      schemes.splice(index, 1);
      target.Selected = Math.min(numberOf(target.Selected), schemes.length - 1);
    });
    if (timeline) dropOrphans([timeline]);
  },
};
