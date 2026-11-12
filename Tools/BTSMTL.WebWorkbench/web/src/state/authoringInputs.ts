// 预览输入方案（inputs/<方案>.json）：每个方案带一条输入 Timeline。
import { authoringApi, type InputFragmentRequest, type InputPlanContext, type AuthoringElements } from '../api/authoring';
import {
  authoringActions, currentDocument, dropOrphans, get, graphPath, setDocument, skillElements, timelinePath, tracksOf, variablesDrafts,
  type JsonKey, type JsonMutation,
} from './authoring';
import { cloneJson, numberOf, setIn, sortedObject, type JsonObject, type JsonValue } from './skillJson';
import { findNode, nodeType, nodesOf } from './graphDocument';
import { selectionActions } from './store';

type InputFragment = Awaited<ReturnType<typeof authoringApi.inputFragment>>;

function planFrom(elements: AuthoringElements, name: string) {
  if (!elements.previewInput.availability.available)
    throw new Error(elements.previewInput.availability.reason!);
  const plan = cloneJson(elements.previewInput.plan!.value);
  for (const scheme of plan.Schemes as JsonObject[]) scheme.AuthoringId = crypto.randomUUID();
  const scheme = (plan.Schemes as JsonObject[])[numberOf(plan.Selected)];
  const timeline = authoringActions.createTimeline(elements.timeline, name + ' 输入');
  scheme.Name = name;
  scheme.InputTimeline = timeline;
  const id = crypto.randomUUID();
  authoringActions.setFile(`inputs/${id}.json`, plan);
  setDocument({ inputActorId: plan.Actor as string | null });
  return { id, timeline };
}

function insertFragment(documentId: string, fragment: InputFragment, trackId: string | null) {
  authoringActions.setFile(graphPath(fragment.graph.AuthoringId as string), fragment.graph, variablesDrafts([fragment.variables]));
  authoringActions.edit(timelinePath(documentId), document => {
    const tracks = tracksOf(document);
    const track = trackId === null ? fragment.track : tracks.find(item => item.m_AuthoringId === trackId)!;
    if (trackId === null) tracks.push(track);
    (track.m_Clips as JsonObject[]).push(fragment.clip);
  });
  selectionActions.select({ kind: 'clip', id: `${documentId}/${fragment.clip.m_AuthoringId as string}` });
}

export const inputAuthoring = {
  async createPlan(name: string, context: InputPlanContext) {
    const created = planFrom(await skillElements(context), name);
    selectionActions.select(null);
    selectionActions.showTimeline(created.timeline, null, null);
    return created;
  },

  // 文档的预览输入在加第一个按键时才建：方案模板与按键片段都取到后同一步写入，不出现空的输入 Timeline。
  // 留在预览路线视图，新按键显示在「预览输入」行里，不跳到单独的输入 Timeline。
  async addFirst(request: InputFragmentRequest) {
    const target = get().target;
    const [elements, fragment] = await Promise.all([skillElements({ kind: 'document' }), authoringApi.inputFragment(request)]);
    if (get().target !== target) return;
    const { timeline } = planFrom(elements, '默认');
    insertFragment(timeline, fragment, null);
  },

  async add(documentId: string, request: InputFragmentRequest, trackId: string | null) {
    const target = get().target;
    const fragment = await authoringApi.inputFragment(request);
    if (get().target !== target) return;
    insertFragment(documentId, fragment, trackId);
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
