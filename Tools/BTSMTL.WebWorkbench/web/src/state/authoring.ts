import { useEffect, useMemo, useState } from 'react';
import { create } from 'zustand';
import { useShallow } from 'zustand/react/shallow';
import {
  authoringApi, authoringOwner, authoringRootFiles, authoringTarget, authoringTargetKey, readSkillSaveConflict, readSkillSaveRejection, type AuthoringTarget, type ReferenceReplacementCompleted, type ReferenceReplacementPrepared, type ReferenceReplacementResult, type ElementTemplate,
  type GraphTemplate, type InputPlanContext, type InputRevision, type InputSaveResult, type PlayerSkillRefreshReceipt, type PlayerSkillRefreshTarget, type ProjectItem,
  type ScenarioDraftRequest, type SkillCreationRequest, type SkillDocumentGraph, type SkillDocumentRelation, type SkillEditingView, type SkillElements,
  type SkillDeleteResult, type SkillFormatError, type SkillSaveResult, type SkillSnapshot,
} from '../api/authoring';
import { ApiError, isAbort } from '../api/http';
import { workspaceDocumentsApi, type WorkspaceDocumentElement, type WorkspaceDocumentInfo, type WorkspaceDocumentSnapshot, type WorkspaceDocumentWrite } from '../api/workspaceDocuments';
import type { VariablesQuery, VariablesValue } from '../api/variables';
import { playerApi } from '../api/player';
import { resourceRegistrationApi, scopeQueryKey, type AuthoringScope, type ScopeQuery } from '../api/resourceRegistration';
import type { PlayerJobReceipt } from '../api/playerRecords';
import type { AuthorTimeline, ExecutionCall, GraphNode, GraphPort } from '../api/types';
import {
  JsonFloat, cloneJson, fpRaw, fpSeconds, numberOf, parseSkillJson, setIn, shapeOf, sortedObject, stringifySkillJson, writeSkillJson, type JsonObject, type JsonValue,
} from './skillJson';
import {
  connectionId, connectionSourcePort, connectionTarget, connectionTargetPort, connectionsOf, layoutNodesOf, nodeId, nodeLabel, nodesOf,
  storedNodeName, transitionPayload,
} from './graphDocument';
import { isWeighted, splitCurve } from './curveSplit';
import { adapterCatalogCache } from './adapterCatalog';
import { consoleActions, selectedScene, selectionActions, useWorkbench, type SelectionSlice } from './store';
import { documentObservationNotice, graphHierarchy, routeObservation, type DocumentObservationBinding } from './graphModel';
import { findTimelineClip as findTimelineClipInViews, findTimelineMarker, findTimelineTrack, type TimelineObservationBinding } from './timelineModel';
import { playerService } from './player';
import { session, stageNames } from './session';

// 作者编辑（共享文档 9.x）：当前运行所属技能的 JSON 文件。原文按路径保存；编辑过的文件解析后改写，
// 保存时整份写回。宿主未启用作者接口时 available=false，作者模式不可用。

export interface AuthoringState {
  // null 为尚未探测；false 为宿主未启用作者接口。
  available: boolean | null;
  unavailableReason: string | null;
  // 作者模式：Timeline、节点图可编辑；运行观察时只读。
  author: boolean;
  target: AuthoringTarget | null;
  documents: Record<string, AuthoringDocument>;
  directory: WorkspaceDocumentInfo[] | null;
  elementCatalog: { kind: 'loading' | 'ready'; items: WorkspaceDocumentElement[] }
    | { kind: 'unavailable'; items: WorkspaceDocumentElement[]; reason: string };
  carrierRevision: string | null;
  resourceTarget: { kind: 'scope'; query: ScopeQuery } | null;
  scopes: Record<string, ScopeDocument>;
}

export interface ScopeDocument {
  source: AuthoringScope;
}
export const scopeDocument = (state: AuthoringState, target = state.resourceTarget?.kind === 'scope' ? state.resourceTarget.query : null) =>
  target === null ? undefined : state.scopes[scopeQueryKey(target)];

export interface AuthoringDocument {
  kind: WorkspaceDocumentInfo['kind'];
  rootFile: WorkspaceDocumentInfo['rootFile'];
  identity: WorkspaceDocumentInfo['identity'] | null;
  authoringType: string | null;
  projectRoot: Extract<ProjectItem, { kind: 'graph' }> | null;
  variables: VariablesQuery | null;
  variablesWriting: boolean;
  documentSources: string[];
  programId: string | null;
  inputActorId: string | null;
  inputRevision: { kind: 'catalog' } | { kind: 'saved'; items: InputRevision[] } | { kind: 'rejected'; reason: string };
  saveRun: { kind: 'waiting' | 'terminal'; receipt: PlayerJobReceipt } | null;
  revision: string | null;
  sourceRevision: string | null;
  documentRevision: string | null;
  files: Record<string, string>;
  sourceFiles: Record<string, string>;
  // 编辑过及新建的文件（路径 → 解析后的文档），逐次修改递增 version。
  edited: Record<string, JsonValue>;
  // 待删除的原文件（删除状态、节点或片段后不再被引用的专属图与 Timeline）。
  removed: string[];
  version: number;
  undo: Snapshot[];
  redo: Snapshot[];
  recording: boolean;
  loading: boolean;
  // 外部修改了技能文件而本地有未保存修改。
  externalChange: boolean;
  // 网页新增（含粘贴）节点的端口：来自类型描述或复制源，保存并重跑前用于显示与连线。
  addedPorts: Record<string, AddedPort[]>;
  saving: boolean;
  // 最近一次保存的编译结论；文件写入后由正式编译器判断。
  lastSave: SaveOutcome | null;
  // 作者 JSON 与当前未保存修改的图投影；来源被拒时给出原因。
  documentGraphs: SkillDocumentGraph[] | null;
  documentRelations: SkillDocumentRelation[];
  documentError: string | null;
  tickRate: number | null;
  execution: SkillEditingView['execution'];
  product: SkillEditingView['product'];
}

interface SaveOutcomeMessage {
  ok: boolean;
  message: string;
  errors: SkillFormatError[];
}

export type SaveOutcome = SaveOutcomeMessage & (
  | (SavedCompilation & { preview: PlayerSkillRefreshReceipt | null })
  | Pick<InputSaveResult, 'saved' | 'compilationState' | 'preview' | 'previewError' | 'jobError'>
  | { saved: true; compilationState: 'not-requested'; runtimeState: 'pending'; preview: null }
  | { saved: boolean; compilationState: 'not-started'; preview: null }
  | { saved: true; compilationState: 'failed'; preview: null }
  | (SavedCompilation & { publicationState: 'failed'; preview: null })
  | { saved: false | null; compilationState: null; preview: null }
);

export interface AddedPort {
  id: string;
  displayName: string;
  direction: 'input' | 'output';
  isTrigger: boolean;
  valueType: string;
}

export const newDocument = (): Omit<AuthoringDocument, 'kind'> => ({
  rootFile: null, identity: null, authoringType: null, projectRoot: null, variables: null, variablesWriting: false, documentSources: [],
  programId: null, inputActorId: null,
  inputRevision: { kind: 'catalog' }, saveRun: null, revision: null, sourceRevision: null, documentRevision: null,
  files: {}, sourceFiles: {}, edited: {}, removed: [], version: 0, undo: [], redo: [], recording: false,
  loading: false, externalChange: false, addedPorts: {},
  saving: false, lastSave: null,
  documentGraphs: null, documentRelations: [], documentError: null, tickRate: null, execution: { previewScope: null, runtimeScope: null }, product: null,
});

export const variablesDrafts = (values: readonly VariablesValue[]): Record<string, AuthoringDocument> =>
  Object.fromEntries(values.map((value): [string, AuthoringDocument] => [authoringTarget('variables', value.m_OwnerId).documentPath,
    { ...newDocument(), kind: 'variables', rootFile: authoringRootFiles.variables, identity: value.m_OwnerId,
      edited: { [authoringRootFiles.variables]: cloneJson(value) }, version: 1 }]));

const closedDocument = newDocument();

export const useAuthoring = create<AuthoringState>()(() => ({
  available: null, unavailableReason: null, author: false, target: null, documents: {}, directory: null,
  elementCatalog: { kind: 'loading', items: [] }, carrierRevision: null,
  resourceTarget: null, scopes: {},
}));

export const authoringDocument = (state: AuthoringState, target = state.target): Omit<AuthoringDocument, 'kind'> =>
  target === null ? closedDocument : state.documents[authoringTargetKey(target)];

export const ownsAuthoringGraph = (state: AuthoringState, id: string): boolean => {
  const target = state.target;
  return target !== null && authoringDocument(state).documentGraphs?.some(graph => graph.id === id && graph.owner === target.documentPath) === true;
};

export const get = () => {
  const state = useAuthoring.getState();
  return { ...state, ...authoringDocument(state) };
};
export const set = useAuthoring.setState;
export const setDocument = (patch: Partial<AuthoringDocument>, target = get().target!) => {
  const state = useAuthoring.getState();
  const key = authoringTargetKey(target);
  set({ documents: { ...state.documents, [key]: { ...state.documents[key], ...patch } } });
};
const isActiveTarget = (target: AuthoringTarget) => {
  const current = get().target;
  return current !== null && authoringTargetKey(current) === authoringTargetKey(target);
};

export function useDocumentObservationBinding(): DocumentObservationBinding {
  const { graphs, dirty } = useAuthoring(useShallow(state => {
    const document = authoringDocument(state);
    const owners = new Set(document.documentGraphs?.map(graph => graph.owner));
    if (state.target !== null) owners.add(state.target.documentPath);
    return { graphs: document.documentError === null && document.sourceRevision !== null ? document.documentGraphs : null, dirty: Object.entries(state.documents).some(([key, owner]) =>
      owners.has(key) &&
      (Object.keys(owner.edited).length > 0 || owner.removed.length > 0)) };
  }));
  return useMemo(() => ({ programs: graphs === null ? null : new Map(graphs.map(graph => [graph.programId, graph.programRevision])), dirty }),
    [graphs, dirty]);
}

export function useDocumentObservationNotice(call: ExecutionCall | null = null): string | null {
  const binding = useDocumentObservationBinding();
  return useWorkbench(state => {
    if (state.runId === null) return null;
    const observation = routeObservation(state);
    return documentObservationNotice(observation.kind === 'ready' ? observation.route : null, binding, call)
      ?? (observation.kind === 'unavailable' ? observation.reason : null);
  });
}

export function useTimelineObservationBinding(dirty: boolean): TimelineObservationBinding {
  const { lastSave, sourceRevision } = useAuthoring(useShallow(state => {
    const document = authoringDocument(state);
    return { lastSave: document.lastSave, sourceRevision: document.sourceRevision };
  }));
  return useMemo(() => {
    if (dirty) return { kind: 'unavailable', reason: '作者内容已有修改，Timeline 运行叠加已过期；原档案保留。' };
    const receipt = lastSave?.compilationState === 'succeeded' ? lastSave.preview : null;
    if (receipt === null) return { kind: 'unavailable', reason: '尚未取得当前编辑内容的 Timeline 预览采用回执，运行叠加不可用。' };
    if (sourceRevision === null || receipt.sourceRevision !== sourceRevision)
      return { kind: 'unavailable', reason: '预览回执尚未对应当前完整编辑内容，Timeline 运行叠加不可用。' };
    switch (receipt.stage) {
      case 'accepted': return { kind: 'unavailable', reason: '当前 Timeline 已准备，Player 尚未确认采用，运行叠加不可用。' };
      case 'failed':
      case 'unavailable': return { kind: 'unavailable', reason: receipt.error };
      case 'adopted': return { kind: 'ready', sceneRevision: receipt.adoptedSceneRevision, programs: receipt.package.programs };
    }
  }, [dirty, lastSave, sourceRevision]);
}

export function useDocumentGraphHierarchy() {
  const { graphs, relations } = useAuthoring(useShallow(state => ({
    graphs: authoringDocument(state).documentGraphs, relations: authoringDocument(state).documentRelations,
  })));
  return useMemo(() => graphHierarchy(graphs ?? [], relations), [graphs, relations]);
}

export const isTimelineFile = (path: string) => path === 'timeline.json' || path.startsWith('timelines/') && path.endsWith('.json');
const usesGraphModels = (kind: AuthoringTarget['kind']) =>
  kind === 'skill' || kind === 'timeline' || kind === 'graph' || kind === 'scenario';

export function timelinePath(documentId: string) {
  const root = currentDocument('timeline.json') as JsonObject | null;
  return root?.m_AuthoringId === documentId ? 'timeline.json' : `timelines/${documentId}.json`;
}

let loadController: AbortController | null = null;
let previewController: AbortController | null = null;
type DocumentOpenResult = 'opened' | 'cancelled' | 'failed';

useAuthoring.subscribe((state, previous) => {
  if (state.target !== previous.target) previewController?.abort();
});

type PreviewSelection = { kind: 'selected'; target: PlayerSkillRefreshTarget } | { kind: 'unavailable'; reason: string };
type SavedCompilation = Pick<Extract<SkillSaveResult, { compilationState: 'succeeded' }>, 'saved' | 'compilationState' | 'compilation' | 'jobError'>;

async function selectedPreview(): Promise<PreviewSelection> {
  const state = useWorkbench.getState();
  const mode = state.host?.modes.find(item => item.id === state.previewMode);
  if (!mode) return { kind: 'unavailable', reason: '未选择预览档位，运行未应用' };
  if (!mode.available || !mode.createsRun) return { kind: 'unavailable', reason: mode.reason };
  const liveId = state.liveId;
  if (!liveId) return { kind: 'unavailable', reason: '未选择 Player，预览未刷新' };
  try {
    const live = await playerApi.live(liveId);
    if (live.previewTarget === null)
      return { kind: 'unavailable', reason: '所选 Player 尚无已确认载入的内容，预览未刷新' };
    const { liveId: targetLiveId, content, channels } = live.previewTarget;
    if (content.kind !== 'scene')
      return { kind: 'unavailable', reason: '所选 Player 当前载入 Timeline，技能刷新需要场景内容' };
    if (channels === null)
      return { kind: 'unavailable', reason: '所选 Player 上次载入未声明录制通道，重新载入内容后才能刷新预览' };
    return { kind: 'selected', target: { requestId: crypto.randomUUID(), liveId: targetLiveId,
      scene: content.scene, sceneRevision: content.revision, source: content.source, channels } };
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    return { kind: 'unavailable', reason: `无法读取所选 Player，预览未刷新：${error.reason}` };
  }
}

const previewMessages = {
  accepted: '预览已受理，等待 Player 采用',
  adopted: 'Player 已采用本次内容',
  failed: 'Player 采用失败',
  unavailable: '预览不可用',
} satisfies Record<PlayerSkillRefreshReceipt['stage'], string>;

function savedOutcome(save: SavedCompilation, preview: PlayerSkillRefreshReceipt | null, noPreviewReason: string): SaveOutcome {
  const detail = preview === null ? noPreviewReason : `${previewMessages[preview.stage]}${preview.error === null ? '' : `：${preview.error}`}`;
  return {
    saved: save.saved, compilationState: save.compilationState, compilation: save.compilation, jobError: save.jobError, preview,
    ok: preview === null || preview.stage === 'accepted' || preview.stage === 'adopted',
    message: `已保存，${save.compilation.artifactReused ? '本次复用已编译产物' : '本次编译完成'}；${detail}${preview?.package?.program === null ? '；本技能没有执行图' : ''}${save.jobError === null ? '' : `；作业提交失败：${save.jobError}`}`, errors: [],
  };
}

function inputSaveMessage(outcome: Pick<InputSaveResult, 'preview' | 'previewError' | 'jobError'>) {
  return ['输入方案已保存，无需编译技能',
    outcome.previewError === null ? outcome.preview === null ? null : `所选 Player 重载：${outcome.preview.stage}`
      : `预览提交失败：${outcome.previewError}`,
    outcome.jobError === null ? null : `作业提交失败：${outcome.jobError}`,
  ].filter(item => item !== null).join('；');
}

function acceptSaveJob(receipt: PlayerJobReceipt) {
  const waiting = receipt.stage === 'queued' || receipt.stage === 'running';
  const outcome = get().lastSave;
  setDocument({ saveRun: { kind: waiting ? 'waiting' : 'terminal', receipt } });
  if (outcome) {
    const source = outcome.compilationState === 'succeeded'
      ? savedOutcome(outcome, outcome.preview, '').message
      : outcome.compilationState === 'not-required' ? inputSaveMessage(outcome) : outcome.message;
    setDocument({ lastSave: { ...outcome, ok: outcome.compilationState === 'not-required' || waiting || receipt.stage === 'completed',
      message: `${source}；封存作业${stageNames[receipt.stage]}${receipt.error === null ? '' : `：${receipt.error}`}`,
    } });
  }
  if (receipt.stage === 'completed') session.selectRun(receipt.jobId);
}

async function trackInputPreview(target: AuthoringTarget, preview: NonNullable<InputSaveResult['preview']>,
  liveId: string, controller: AbortController) {
  let receipt = preview;
  const commandId = receipt.commandId;
  if (commandId === null) return;
  try {
    while (receipt.stage === 'sent' || receipt.stage === 'submitted') {
      await new Promise(resolve => setTimeout(resolve, 1000));
      controller.signal.throwIfAborted();
      receipt = await playerApi.readCommand(liveId, commandId, controller.signal);
      controller.signal.throwIfAborted();
      const document = authoringDocument(useAuthoring.getState(), target);
      const outcome = document.lastSave;
      if (outcome?.compilationState !== 'not-required') return;
      const next = { ...outcome, preview: receipt };
      const job = document.saveRun?.receipt;
      setDocument({ lastSave: { ...next, message: inputSaveMessage(next) + (job === undefined ? ''
        : `；封存作业${stageNames[job.stage]}${job.error === null ? '' : `：${job.error}`}`) } }, target);
    }
    if (receipt.stage === 'applied') await playerService.refresh();
  } catch (error) {
    if (isAbort(error)) return;
    const outcome = authoringDocument(useAuthoring.getState(), target).lastSave;
    if (outcome?.compilationState !== 'not-required') return;
    setDocument({ lastSave: { ...outcome,
      message: `${inputSaveMessage(outcome)}；预览状态读取失败：${error instanceof Error ? error.message : String(error)}` } }, target);
    consoleActions.error('预览', error);
  }
}

async function trackPreview(input: {
  save: SavedCompilation; receipt: PlayerSkillRefreshReceipt; controller: AbortController;
  viewport: { width: number; height: number } | null;
}) {
  let receipt = input.receipt;
  try {
    while (receipt.stage === 'accepted') {
      await new Promise(resolve => setTimeout(resolve, 1000));
      input.controller.signal.throwIfAborted();
      const next = await authoringApi.refreshStatus(receipt.statusPath, input.controller.signal);
      input.controller.signal.throwIfAborted();
      if (next.stage !== receipt.stage) {
        const outcome = savedOutcome(input.save, next, '');
        setDocument({ lastSave: outcome });
        consoleActions.log(outcome.ok ? 'info' : 'error', '预览', outcome.message, JSON.stringify(next));
      }
      receipt = next;
    }
    input.controller.signal.throwIfAborted();
    if (receipt.stage !== 'adopted' || input.save.jobError !== null) return;
    const live = await playerApi.live(receipt.liveId, input.controller.signal);
    input.controller.signal.throwIfAborted();
    const target = live.previewTarget;
    if (target === null || target.content.kind !== 'scene' || target.content.revision !== receipt.package.sceneRevision)
      throw new Error('Player 当前确认内容与本次采用回执不一致，未提交封存作业');
    const content = target.content;
    if (input.viewport === null || target.channels === null || content.source.kind === 'device') {
      const reason = input.viewport === null ? '没有本次画面尺寸' : target.channels === null ? 'Player 未声明录制通道'
        : '设备输入没有固定修订';
      setDocument({ lastSave: { ...savedOutcome(input.save, receipt, ''), message: `已保存、编译并采用；${reason}，未提交封存作业` } });
      session.selectRun(null);
      await playerService.refresh();
      return;
    }
    const request = await session.prepareRun({ kind: 'adopted',
      target: { ...target, channels: target.channels, content: { ...content, source: content.source } }, viewport: input.viewport,
    }, input.controller.signal);
    input.controller.signal.throwIfAborted();
    const job = await playerApi.startJob(request, input.controller.signal);
    input.controller.signal.throwIfAborted();
    setDocument({ lastSave: savedOutcome(input.save, receipt, '') });
    acceptSaveJob(job);
    await Promise.all([playerService.refresh(), session.refresh()]);
  } catch (error) {
    if (isAbort(error)) return;
    const outcome = savedOutcome(input.save, receipt, '');
    setDocument({ lastSave: {
      ...outcome, ok: false,
      message: `${outcome.message}；${receipt.stage === 'adopted' ? '采用后封存' : '预览状态读取'}失败：${error instanceof Error ? error.message : String(error)}`,
    } });
    consoleActions.error('预览', error);
  }
}

// Timeline 文档中的轨道、片段与标记，时间由 FP 原始值换算；片段与标记的图为 m_AssetTree／m_Graph。
export function documentTimelines(files: Record<string, string>): AuthorTimeline[] {
  return Object.entries(files).filter(([path]) => isTimelineFile(path)).map(([, text]) => {
    const document = parseSkillJson(text) as JsonObject;
    return timelineOf(document.m_AuthoringId as string, document);
  });
}

export interface AuthorInputScheme {
  path: string;
  planId: string;
  schemeIndex: number;
  name: string;
  documentId: string;
  scene: string;
  actorId: string;
  rangeEndSeconds: number;
}

export function useInputSchemes(): AuthorInputScheme[] {
  const { files, edited, removed } = useAuthoring(useShallow(state => {
    const document = authoringDocument(state);
    return { files: document.files, edited: document.edited, removed: document.removed };
  }));
  return useMemo(() => [...new Set([...Object.keys(files), ...Object.keys(edited)])]
    .filter(path => path.startsWith('inputs/') && path.endsWith('.json') && !removed.includes(path))
    .flatMap(path => {
      const plan = (edited[path] ?? parseSkillJson(files[path])) as JsonObject;
      return (plan.Schemes as JsonObject[]).map((scheme, schemeIndex) => ({
        path, planId: path.slice('inputs/'.length, -'.json'.length), schemeIndex, name: scheme.Name as string,
        documentId: scheme.InputTimeline as string, scene: plan.Scene as string, actorId: plan.Actor as string,
        rangeEndSeconds: numberOf(scheme.RangeEnd),
      }));
    }), [files, edited, removed]);
}

export function useInputContext(documentId: string | null) {
  const schemes = useInputSchemes();
  const scheme = schemes.find(item => item.documentId === documentId);
  const actorId = useAuthoring(state => authoringDocument(state).inputActorId);
  const { catalog, scene, reading, failure } = useWorkbench(useShallow(state => ({
    catalog: state.inputCatalog, scene: selectedScene(state),
    reading: state.inputCatalogReading, failure: state.inputCatalogFailure,
  })));
  if (!scheme) return { kind: 'unavailable', reason: '当前不是输入 Timeline' } as const;
  if (!scene) return { kind: 'unavailable', reason: '先在工具栏或内容面板选择场景' } as const;
  if (!catalog || scene.revision !== catalog.revision)
    return { kind: 'unavailable', reason: failure !== null ? '输入声明读取失败，见控制台；可在内容面板重新读取'
      : reading ? '正在读取场景的输入声明' : '场景的输入声明尚未读取' } as const;
  if (scheme.scene && scheme.scene !== scene.path)
    return { kind: 'unavailable', reason: '所选场景与输入方案的 Scene 不一致' } as const;
  const actor = catalog.players.find(player => player.actorId === (scheme.actorId || actorId));
  if (!actor) return { kind: 'unavailable', reason: '从内容面板为具体角色打开输入方案' } as const;
  return { kind: 'ready', scheme, scene, actor, tickRate: catalog.tickRate } as const;
}

export function timelineOf(documentId: string, document: JsonObject): AuthorTimeline {
  return {
    documentId, contentIdentity: null, contentRevision: null, displayName: document.m_Name as string,
    tracks: (document.m_Tracks as JsonObject[]).map(track => ({
      id: track.m_AuthoringId as string, contract: track.ContractKind as string, displayName: track.Name as string,
      muted: track.m_PersistentMuted === true,
      clips: (track.m_Clips as JsonObject[]).map(clip => ({
        id: clip.m_AuthoringId as string, contract: clip.ContractKind as string, displayName: null,
        startSeconds: fpSeconds(numberOf(clip.m_StartTimeFPRawValue)), endSeconds: fpSeconds(numberOf(clip.m_EndTimeFPRawValue)),
        graphId: (clip.m_AssetTree as string | undefined) ?? null,
        easeInSeconds: fpSeconds(numberOf(clip.m_SelfEaseInTimeFPRawValue ?? 0)), easeOutSeconds: fpSeconds(numberOf(clip.m_SelfEaseOutTimeFPRawValue ?? 0)),
      })),
      markers: ((track.m_Markers as JsonObject[] | undefined) ?? []).map(marker => ({
        id: marker.m_AuthoringId as string, seconds: fpSeconds(numberOf(marker.m_TimeFPRawValue)), graphId: marker.m_Graph as string,
      })),
    })),
  };
}

// 结构始终来自当前技能 JSON；Player 档案只提供运行叠加。
function publishDocument() {
  const { documentGraphs, files, target } = get();
  useWorkbench.setState({
    graphs: target === null ? null : documentGraphs, graphAvailability: [],
    timelines: target ? documentTimelines(files) : [],
  });
  reconcileSelection();
}

useWorkbench.subscribe((state, previous) => {
  if (state.liveId !== previous.liveId) {
    previewController?.abort();
    if (get().target !== null) setDocument({ saveRun: null });
  }
  if (state.generation !== previous.generation) publishDocument();
  if (JSON.stringify(selectedScene(state)) !== JSON.stringify(selectedScene(previous))) {
    previewController?.abort();
    scheduleDocumentReload(get().target);
  }
  if (state.archiveJobs === previous.archiveJobs) return;
  const saved = get().saveRun;
  if (saved?.kind !== 'waiting') return;
  const jobId = saved.receipt.jobId;
  const receipt = state.archiveJobs.find(job => job.jobId === jobId);
  if (!receipt) return;
  if (receipt.stage !== saved.receipt.stage) acceptSaveJob(receipt);
});

export function captureEditingView() {
  const capturedOwners = Object.entries(useAuthoring.getState().documents)
    .map(([key, document]) => ({ key, revision: document.revision, version: document.version }));
  const editingView = authoringActions.editingView();
  const isCurrentRequest = () => {
    const current = useAuthoring.getState().documents;
    return Object.keys(current).length === capturedOwners.length &&
      capturedOwners.every(owner => current[owner.key].revision === owner.revision && current[owner.key].version === owner.version) &&
      JSON.stringify(editingView.scene) === JSON.stringify(selectedScene(useWorkbench.getState()));
  };
  return { editingView, isCurrentRequest };
}

async function readDocument(target: AuthoringTarget, signal: AbortSignal) {
  const { editingView, isCurrentRequest } = captureEditingView();
  try {
    const [document, view, project] = await Promise.all([
      authoringApi.document(target.documentPath, editingView, signal),
      workspaceDocumentsApi.editingView(editingView, signal),
      authoringApi.project(target, editingView, signal),
    ]);
    signal.throwIfAborted();
    if (!isCurrentRequest()) return;
    const documents = { ...useAuthoring.getState().documents };
    for (const source of document.sources) {
      const owner = authoringOwner(source.skill);
      const key = owner.documentPath;
      const projectRoot = project.items.filter(item => item.owner === key && item.file === authoringRootFiles[owner.kind])
        .find(item => item.kind === 'graph') ?? null;
      const current = documents[key];
      if (current === undefined) {
        documents[key] = { ...newDocument(), kind: owner.kind, rootFile: authoringRootFiles[owner.kind],
          projectRoot,
          identity: view.elements.find(element => element.documentPath === key && element.relativePath === authoringRootFiles[owner.kind] && element.jsonPath === '/')?.identity ?? null,
          revision: source.revision, product: editingView.product, execution: editingView.execution,
          sourceFiles: source.sourceFiles, files: source.files };
      } else {
        const edited = { ...current.edited };
        const files = { ...source.files };
        for (const [path, content] of Object.entries(source.files)) {
          if (Object.hasOwn(edited, path)) edited[path] = parseSkillJson(content);
        }
        for (const path of [...Object.keys(edited), ...current.removed]) {
          if (current.files[path] === undefined) delete files[path];
          else files[path] = current.files[path];
        }
        documents[key] = { ...current, files, edited, projectRoot };
      }
    }
    const key = authoringTargetKey(target);
    documents[key] = { ...documents[key], documentGraphs: document.graphs, documentRelations: document.relations,
      documentSources: document.sources.map(source => source.skill),
      sourceRevision: document.sourceRevision, documentRevision: document.inputRevision,
      documentError: null, tickRate: document.tickRate, programId: document.programId };
    set({ documents, elementCatalog: { kind: 'ready', items: view.elements } });
  } catch (error) {
    if (isAbort(error)) throw error;
    if (!isCurrentRequest()) return;
    const reason = error instanceof ApiError ? error.reason : String(error);
    setDocument({ sourceRevision: null, documentError: reason }, target);
    set({ elementCatalog: { kind: 'unavailable', items: useAuthoring.getState().elementCatalog.items, reason } });
    consoleActions.error('作者', error);
  }
}

function reconcileSelection() {
  const { selection, graphs, timelines } = useWorkbench.getState();
  if (!selection || selection.kind === 'actor' || selection.kind === 'resource' || selection.kind === 'content' || selection.id.startsWith('input/') || selection.id.startsWith('curve/')) return;
  const { edited, removed, addedPorts } = get();
  const { kind, id } = selection;
  let exists: boolean;
  if (kind === 'node' || kind === 'edge') {
    const found = findEditedGraph(graphs, id, edited, addedPorts);
    exists = found !== null && !removed.includes(graphPath(found.graph.id)) && (kind === 'node'
      ? found.graph.nodes.some(node => node.id === found.id && node.invocationPath === found.path)
      : found.graph.edges.some(edge => edge.id === found.id && edge.invocationPath === found.path));
  } else {
    const current = applyTimelineEdits(timelines, edited, removed);
    exists = kind === 'track' ? findTimelineTrack(current, id) !== null
      : (findTimelineClipInViews(current, id) ?? findTimelineMarker(current, id)) !== null;
  }
  if (!exists) selectionActions.select(null);
}

// 保存或外部修改后重新展开图（端口、新节点与连线以宿主读取为准）。
async function reloadDocument(target: AuthoringTarget, signal: AbortSignal) {
  await readDocument(target, signal);
  if (isActiveTarget(target)) publishDocument();
}

let documentTimer: ReturnType<typeof setTimeout> | null = null;
let documentController: AbortController | null = null;
function scheduleDocumentReload(target: AuthoringTarget | null) {
  if (target === null) return;
  setDocument({ sourceRevision: null }, target);
  if (documentTimer !== null) clearTimeout(documentTimer);
  documentController?.abort();
  const controller = documentController = new AbortController();
  documentTimer = setTimeout(() => {
    void reloadDocument(target, controller.signal).catch(error => consoleActions.error('作者', error));
  }, 150);
}
useAuthoring.subscribe((state, previous) => {
  if (state.target !== previous.target) {
    if (documentTimer !== null) clearTimeout(documentTimer);
    documentController?.abort();
    return;
  }
  const target = state.target;
  if (target === null || !Object.entries(previous.documents).some(([key, document]) =>
    document.version !== state.documents[key]?.version)) return;
  scheduleDocumentReload(target);
});

useWorkbench.subscribe((state, previous) => {
  if (state.selection !== null && state.selection !== previous.selection && useAuthoring.getState().resourceTarget !== null)
    useAuthoring.setState({ resourceTarget: null });
});

export const authoringActions = {
  async createSkill(request: SkillCreationRequest) {
    const target = authoringTarget('skill', request.parameters.name);
    const key = authoringTargetKey(target);
    const state = useAuthoring.getState();
    if (state.directory?.some(document => document.documentPath === key) || state.documents[key] !== undefined)
      throw new Error(`技能目录 ${target.documentPath} 已存在或已经打开，请使用其他名称`);
    const identityOwner = Object.entries(state.documents).find(([, document]) => document.revision === null &&
      (document.edited['skill.json'] as JsonObject | undefined)?.m_AbilityId === request.parameters.abilityId);
    if (identityOwner !== undefined)
      throw new Error(`技能 ID ${request.parameters.abilityId} 已用于未保存草稿 ${identityOwner[0]}，请继续编辑该草稿或使用其他 ID`);
    const draft = await authoringApi.createSkillDraft(request);
    const edited = Object.fromEntries(draft.files.map(file => [file.path, parseSkillJson(file.content)]));
    set({ documents: { ...useAuthoring.getState().documents, ...variablesDrafts(draft.variables === null ? [] : [draft.variables]),
      [key]: { ...newDocument(), kind: target.kind, rootFile: 'skill.json', edited, version: 1,
      sourceFiles: Object.fromEntries(draft.files.map(file => [file.path, file.content])) } } });
    await authoringActions.open(target);
    selectionActions.select(null);
    return target;
  },

  async createScenario(name: string, request: ScenarioDraftRequest) {
    const target = authoringTarget('scenario', name);
    const draft = await authoringApi.scenarioDraft(name, request);
    await workspaceDocumentsApi.save({ writes: [{ kind: 'scenario', documentPath: target.documentPath, expectedRevision: '',
      files: Object.fromEntries(draft.files.map(file => [file.path, file.content])) },
      ...Object.entries(variablesDrafts(draft.variables)).map(([path, document]) => documentWrite(path, document))], reads: [] });
    await authoringActions.open(target);
    setDocument({ inputActorId: request.actorId }, target);
    await Promise.all([authoringActions.listDocuments(), session.loadInputs()]);
  },

  async listDocuments() {
    const { editingView, isCurrentRequest } = captureEditingView();
    try {
      const [directory, view] = await Promise.allSettled([
        workspaceDocumentsApi.list(), workspaceDocumentsApi.editingView(editingView),
      ]);
      if (directory.status === 'rejected') throw directory.reason;
      let elementCatalog: AuthoringState['elementCatalog'];
      if (view.status === 'fulfilled') elementCatalog = { kind: 'ready', items: view.value.elements };
      else {
        const error: unknown = view.reason;
        const reason = error instanceof Error ? error.message : String(error);
        elementCatalog = { kind: 'unavailable', items: useAuthoring.getState().elementCatalog.items, reason };
        consoleActions.error('作者元素目录', error);
      }
      set({ directory: directory.value, available: true, unavailableReason: null,
        ...(isCurrentRequest() ? { elementCatalog } : {}),
      });
    } catch (error) {
      const reason = error instanceof ApiError && error.status === 404
        ? '宿主未启用作者接口（启动时需 --SkillAuthoring true）' : error instanceof Error ? error.message : String(error);
      set({ directory: [], available: false, unavailableReason: reason,
        ...(isCurrentRequest() ? { elementCatalog: { kind: 'unavailable', items: useAuthoring.getState().elementCatalog.items, reason } as const } : {}),
      });
    }
  },

  // 直接打开技能文件夹：读原文件与图展开，不需要运行；进入作者模式。
  async open(target: AuthoringTarget): Promise<DocumentOpenResult> {
    const current = get().target;
    if (current && authoringTargetKey(current) === authoringTargetKey(target)) {
      if (authoringDocument(useAuthoring.getState(), current).documentGraphs !== null) {
        set({ author: true, resourceTarget: null });
        return 'opened';
      }
      target = current;
    }
    loadController?.abort();
    if (current !== null && authoringDocument(useAuthoring.getState()).loading)
      setDocument({ loading: false }, current);
    const controller = loadController = new AbortController();
    useWorkbench.setState({ graphRequest: null, timelineRequest: null });
    const state = useAuthoring.getState();
    const key = authoringTargetKey(target);
    const opened = state.documents[key];
    set({ target, author: true, resourceTarget: null, documents: opened ? state.documents
      : { ...state.documents, [key]: { ...newDocument(), kind: target.kind, loading: true } } });
    publishDocument();
    if (!opened || opened.documentGraphs === null) setDocument({ loading: true }, target);
    try {
      const snapshot = await workspaceDocumentsApi.open(target.documentPath, controller.signal);
      controller.signal.throwIfAborted();
      setDocument({ kind: snapshot.kind, rootFile: snapshot.rootFile, identity: snapshot.identity, authoringType: snapshot.authoringType ?? null,
        ...(!opened || (opened.revision === null && !hasChanges(target))
          ? { revision: snapshot.revision, sourceFiles: snapshot.files,
            ...(usesGraphModels(snapshot.kind) ? {} : { files: snapshot.files }) } : {}),
      }, target);
      await reloadDocument(target, controller.signal);
      controller.signal.throwIfAborted();
      setDocument({ loading: false }, target);
      return 'opened';
    } catch (error) {
      if (isAbort(error)) return 'cancelled';
      setDocument({ loading: false }, target);
      consoleActions.error('作者', error);
      return 'failed';
    }
  },

  async openInput(target: AuthoringTarget, path: string, schemeIndex: number, actorId: string) {
    if (await authoringActions.open(target) !== 'opened') return;
    const current = get().target;
    if (current === null || authoringTargetKey(current) !== authoringTargetKey(target)) return;
    const plan = currentDocument(path) as JsonObject;
    const scheme = (plan.Schemes as JsonObject[])[schemeIndex];
    setDocument({ inputActorId: actorId });
    selectionActions.select(null);
    selectionActions.showTimeline(scheme.InputTimeline as string, null, null);
  },

  // 取可编辑的文档：首次编辑时从原文解析。同一轮事件里的多次编辑记为一步撤销。
  edit(path: string, mutate: (document: JsonValue) => void) {
    const state = get();
    const document = state.removed.includes(path) ? null
      : state.edited[path] ?? (state.files[path] !== undefined ? parseSkillJson(state.files[path]) : null);
    if (document === null) {
      consoleActions.log('error', '作者', `${state.target?.documentPath ?? '—'} 中没有文件 ${path}`);
      return;
    }
    record();
    try {
      mutate(document);
    } catch (error) {
      consoleActions.error('作者', error);
      return;
    }
    setDocument({ edited: { ...get().edited, [path]: document }, version: get().version + 1 });
  },

  setFile(path: string, document: JsonValue, createdDocuments: Record<string, AuthoringDocument> = {}) {
    record(Object.keys(createdDocuments));
    const state = get();
    const key = state.target!.documentPath;
    set({ documents: { ...state.documents, ...createdDocuments, [key]: { ...state.documents[key],
      edited: { ...state.edited, [path]: document }, removed: state.removed.filter(item => item !== path), version: state.version + 1 } } });
  },

  // 删除文件：网页新建的直接丢弃，原有的保存时删除。
  remove(paths: string[]) {
    if (!paths.length) return;
    const graphIds = new Set<string | null>(paths.flatMap(path => /^graphs\/(.+)\.json$/.exec(path)?.slice(1) ?? []));
    const related = Object.entries(get().documents).filter(([, document]) => document.kind === 'variables' &&
      document.revision === null && graphIds.has(document.identity)).map(([key]) => key);
    record(related);
    const state = get();
    const edited = { ...state.edited };
    for (const path of paths) delete edited[path];
    const key = state.target!.documentPath;
    const documents = { ...state.documents, [key]: { ...state.documents[key],
      edited, removed: [...new Set([...state.removed, ...paths.filter(path => state.files[path] !== undefined)])], version: state.version + 1,
    } };
    for (const path of related) delete documents[path];
    set({ documents });
  },

  acceptSkillDeletion(receipt: Extract<SkillDeleteResult, { deleted: true }>) {
    const state = get();
    const target = authoringTarget('skill', receipt.skill);
    const documents = { ...state.documents };
    delete documents[authoringTargetKey(target)];
    const closing = isActiveTarget(target);
    if (closing) loadController?.abort();
    set({ documents, directory: state.directory === null ? null : state.directory.filter(document => document.documentPath !== authoringTargetKey(target)),
      carrierRevision: receipt.authoringRevision, ...(closing ? { target: null } : {}) });
    if (closing) {
      useWorkbench.setState({ graphRequest: null, timelineRequest: null });
      publishDocument();
    }
  },

  async commitReferenceReplacement(plan: ReferenceReplacementPrepared) {
    const conflicts = referenceReplacementConflicts(plan);
    if (conflicts.length !== 0)
      throw new Error(`先处理本次引用替换涉及的网页文档：${conflicts.map(item => `${item.owner}${item.path === null ? '' : `/${item.path}`}（${item.reason}）`).join('；')}`);
    return authoringApi.commitReferenceReplacement(plan);
  },

  async refreshReferenceReplacement(result: ReferenceReplacementCompleted) {
    const owners = referenceReplacementOwners(result).filter(owner => useAuthoring.getState().documents[authoringTargetKey(owner.target)] !== undefined);
    const [catalogResults, snapshots, resourceCatalog] = await Promise.all([
      Promise.allSettled([authoringApi.carriers()]),
      Promise.allSettled(owners.map(owner => workspaceDocumentsApi.open(owner.target.documentPath))),
      adapterCatalogCache.refresh(authoringApi.catalog),
    ]);
    const failures: { text: string; entryId: number | null }[] = [];
    if (resourceCatalog.kind === 'failed') failures.push(resourceCatalog.failure);
    const catalogResult = catalogResults[0];
    const catalog = catalogResult.status === 'fulfilled' ? catalogResult.value : null;
    if (catalogResult.status === 'rejected') failures.push({ text: '作者引用已替换，载体目录重新读取失败',
      entryId: consoleActions.error('Project', catalogResult.reason) });
    const conflicts = referenceReplacementConflicts(result, useAuthoring.getState(), 'refresh');
    const blocked = new Set(conflicts.map(item => item.owner));
    const documents = { ...useAuthoring.getState().documents };
    snapshots.forEach((snapshot, index) => {
      const { target, paths } = owners[index];
      const key = authoringTargetKey(target);
      const document = documents[key];
      if (document === undefined) return;
      if (snapshot.status === 'rejected') {
        documents[key] = { ...document, externalChange: true };
        failures.push({ text: `作者引用已替换，${target.documentPath} 文档重新读取失败`, entryId: consoleActions.error('Project', snapshot.reason) });
      } else {
        const savedFiles = snapshot.value.files;
        const changedBaselines = [...new Set([...Object.keys(document.edited), ...document.removed])]
          .filter(path => !paths.has(path) && savedFiles[path] !== document.sourceFiles[path]);
        for (const path of changedBaselines)
          conflicts.push({ owner: target.documentPath, path, reason: '该未保存文件的磁盘内容也已变化' });
        if (blocked.has(target.documentPath) || changedBaselines.length !== 0) {
          documents[key] = { ...document, externalChange: true };
          return;
        }
        if (isActiveTarget(target)) {
          loadController?.abort();
          loadController = new AbortController();
        }
        const history = (items: Snapshot[]) => items.map(item => item.owner.documentPath !== key ? item : ({ ...item,
          edited: Object.fromEntries(Object.entries(item.edited).filter(([path]) => !paths.has(path))),
          removed: item.removed.filter(path => !paths.has(path)),
        }));
        documents[key] = { ...document, kind: snapshot.value.kind, rootFile: snapshot.value.rootFile, identity: snapshot.value.identity, authoringType: snapshot.value.authoringType ?? null, revision: snapshot.value.revision,
          sourceFiles: snapshot.value.files,
          files: usesGraphModels(snapshot.value.kind) ? document.files : savedFiles,
          sourceRevision: null, externalChange: false,
          undo: history(document.undo), redo: history(document.redo), version: document.version + 1,
        };
      }
    });
    set({ documents, ...(catalog === null ? {} : { carrierRevision: catalog.authoringRevision }) });
    return { catalog, conflicts, failures };
  },

  revert() {
    const state = get();
    if (state.revision === null) {
      loadController?.abort();
      documentController?.abort();
      if (documentTimer !== null) clearTimeout(documentTimer);
      const documents = { ...state.documents };
      delete documents[authoringTargetKey(state.target!)];
      set({ target: null, documents });
      publishDocument();
      return;
    }
    set({ documents: Object.fromEntries(Object.entries(state.documents).map(([key, document]) => [key, {
      ...document, edited: {}, removed: [], version: document.version + 1, externalChange: false,
      addedPorts: {}, undo: [], redo: [], variables: null,
    }])) });
    reconcileSelection();
  },

  undo() { void step('undo', 'redo'); },
  redo() { void step('redo', 'undo'); },

  activateScope(query: ScopeQuery) { set({ resourceTarget: { kind: 'scope', query } }); },
  releaseScope(query: ScopeQuery) {
    const current = get().resourceTarget;
    if (current?.kind === 'scope' && scopeQueryKey(current.query) === scopeQueryKey(query)) set({ resourceTarget: null });
  },
  async openScope(query: ScopeQuery, signal?: AbortSignal, reload = false) {
    set({ resourceTarget: { kind: 'scope', query } });
    if (scopeDocument(useAuthoring.getState(), query) !== undefined && !reload) return;
    const source = await resourceRegistrationApi.readScope(query, signal);
    signal?.throwIfAborted();
    updateScope(query, { source });
  },

  editingView(): SkillEditingView {
    const state = get();
    const scene = selectedScene(useWorkbench.getState());
    const writes = Object.entries(state.documents).map(([path, document]) => documentWrite(path, document));
    return {
      writes,
      reads: Object.entries(state.documents).flatMap(([documentPath, document]) =>
        document.revision !== null ? [{ documentPath, revision: document.revision }] : []),
      execution: state.execution,
      product: state.product,
      ...(scene === undefined ? {} : { scene }),
    };
  },

  selectExecutionScope(kind: 'previewScope' | 'runtimeScope', scope: string | null) {
    set({ resourceTarget: null });
    setDocument({ execution: { ...get().execution, [kind]: scope }, version: get().version + 1 });
  },

  selectProduct(product: SkillEditingView['product']) {
    set({ resourceTarget: null });
    setDocument({ product, version: get().version + 1 });
  },

  async save(operation: 'save' | 'compile' | 'apply' = 'save') {
    const variableResults = await Promise.allSettled(variableRequests.values());
    const variableFailure = variableResults.find(result => result.status === 'rejected');
    if (variableFailure?.status === 'rejected') {
      const error: unknown = variableFailure.reason;
      setDocument({ lastSave: { ok: false, saved: false, compilationState: null, preview: null, errors: [],
        message: `变量修改未完成，作者内容尚未保存：${error instanceof Error ? error.message : String(error)}` } });
      consoleActions.error('变量保存', error);
      return;
    }
    const owner = get().resourceTarget;
    if (owner !== null) return;
    const { target, documents } = get();
    if (!target || Object.values(documents).some(document => document.saving)) return;
    const editingView = authoringActions.editingView();
    const apply = operation === 'apply';
    const submittedDocuments = new Map(editingView.writes.map(write => {
      const document = useAuthoring.getState().documents[write.documentPath];
      return [write.documentPath, { revision: document.revision,
        files: workingFiles(document.files, document, document.rootFile),
        history: new Set([...document.undo, ...document.redo]) }] as const;
    }));
    previewController?.abort();
    const controller = previewController = new AbortController();
    const viewport = useWorkbench.getState().playerViewport;
    const scene = editingView.scene;
    setDocument({ saving: true, inputRevision: { kind: 'catalog' }, saveRun: null }, target);
    const accept = (receipts: WorkspaceDocumentSnapshot[], content: SkillSnapshot | null, lastSave: SaveOutcome) => {
      if (!receipts.some(document => document.documentPath === target.documentPath))
        throw new Error(`保存回执没有返回当前文档 ${target.documentPath}。`);
      const currentScene = JSON.stringify(scene) === JSON.stringify(selectedScene(useWorkbench.getState()));
      const previousDocuments = useAuthoring.getState().documents;
      const documents = { ...previousDocuments };
      const savedFiles = new Map(receipts.map(receipt => [receipt.documentPath,
        receipt.documentPath === target.documentPath && content !== null ? content.files :
          usesGraphModels(receipt.kind) ? submittedDocuments.get(receipt.documentPath)!.files : receipt.files]));
      for (const receipt of receipts) {
        const submitted = submittedDocuments.get(receipt.documentPath)!;
        const current = documents[receipt.documentPath];
        const selected = receipt.documentPath === target.documentPath;
        const files = savedFiles.get(receipt.documentPath)!;
        const rebase = (document: Pick<Snapshot, 'edited' | 'removed' | 'addedPorts'>) =>
          rebaseSavedEdits(current.files, submitted.files, files, document, current.rootFile);
        const history = (items: Snapshot[]) => items.filter(item => !submitted.history.has(item)).map(item => {
          const owner = item.owner.documentPath;
          const files = savedFiles.get(owner);
          return files === undefined ? item : { ...item, ...rebaseSavedEdits(previousDocuments[owner].files,
            submittedDocuments.get(owner)!.files, files, item, previousDocuments[owner].rootFile) };
        });
        documents[receipt.documentPath] = {
          ...current, kind: receipt.kind, rootFile: receipt.rootFile, identity: receipt.identity, authoringType: receipt.authoringType ?? null, revision: receipt.revision,
          sourceRevision: null, documentRevision: null, files, sourceFiles: receipt.files, variables: null,
          ...rebase(current), externalChange: false,
          undo: history(current.undo), redo: history(current.redo), version: current.version + 1,
          ...(selected ? { saving: false, lastSave: currentScene ? lastSave : { ...lastSave,
            message: `${lastSave.message}；此回执对应保存时场景 ${scene?.path ?? '未选择'}，当前场景选择已改变` } } : { lastSave: null }),
        };
      }
      set({ documents });
      if (receipts.some(document => submittedDocuments.get(document.documentPath)!.revision === null))
        void authoringActions.listDocuments();
      return isActiveTarget(target) && currentScene && !controller.signal.aborted;
    };
    try {
      if (operation === 'save') {
        const result = await workspaceDocumentsApi.save(editingView);
        const message = '作者内容已保存；未请求编译与发布，运行未应用。';
        accept(result.documents, null,
          { saved: true, compilationState: 'not-requested', runtimeState: 'pending', preview: null, ok: true, message, errors: [] });
        consoleActions.log('info', '作者', `${target.documentPath}：${message}`, JSON.stringify(result));
        return;
      }
      const jobBlocked = apply ? session.canStart() : null;
      const [selection, job] = apply ? await Promise.all([
        selectedPreview(),
        jobBlocked === null ? session.prepareRun({ kind: 'selection' }) : Promise.resolve(undefined),
      ]) : [null, undefined] as const;
      const result = await authoringApi.save(target, {
        editingView,
        preview: selection?.kind === 'selected' ? selection.target : null,
        job,
      });
      if (result.compilationState === 'not-required') {
        const preview = result.preview;
        const stage = preview?.stage;
        const detail = !apply ? null : result.job === null ? result.jobError === null ? `未提交封存作业：${jobBlocked ?? '宿主未返回作业'}` : null
          : '新输入封存作业已受理，等待完成；旧观察层已过期';
        const message = `${inputSaveMessage(result)}（${result.inputs.length} 项）${detail === null ? '' : `；${detail}`}`;
        const currentSkill = accept(result.documents, result.content, { saved: true, compilationState: 'not-required', preview,
          previewError: result.previewError, jobError: result.jobError, ok: true, message, errors: [] });
        consoleActions.log(result.previewError !== null || result.jobError !== null || stage === 'rejected' || stage === 'not-applied'
          ? 'error' : 'info', '作者', message, JSON.stringify(result));
        setDocument({ inputRevision: { kind: 'saved', items: result.inputs } }, target);
        if (apply && currentSkill) {
          if (result.job !== null && !controller.signal.aborted) acceptSaveJob(result.job);
          if (stage === 'applied') await playerService.refresh();
          if (preview !== null && (stage === 'sent' || stage === 'submitted') && selection?.kind === 'selected')
            void trackInputPreview(target, preview, selection.target.liveId, controller);
          await session.loadInputs();
          if (result.job !== null) await session.refresh();
        }
        return;
      }
      const outcome = savedOutcome(result, result.preview,
        selection === null ? '未请求运行应用' : selection.kind === 'unavailable' ? selection.reason : '');
      const currentSkill = accept(result.documents, result.content, outcome);
      consoleActions.log(outcome.ok ? 'info' : 'error', '作者', `${target.documentPath}：${outcome.message}`, JSON.stringify(result, null, 2));
      if (apply && currentSkill && result.preview !== null && !controller.signal.aborted) {
        consoleActions.log(outcome.ok ? 'info' : 'error', '预览', outcome.message, JSON.stringify(result.preview));
        void trackPreview({ save: result, receipt: result.preview, controller, viewport });
      }
    } catch (error) {
      if (operation !== 'save' && error instanceof ApiError && [400, 422].includes(error.status)) {
        const rejection = readSkillSaveRejection(JSON.parse(error.raw));
        const errors = rejection.stage === 'source-rejected' ? rejection.detail.errors : [];
        const detail = rejection.stage === 'source-rejected' ? rejection.detail.message : rejection.error;
        const message = rejection.stage === 'source-rejected'
          ? `${rejection.saved ? '已保存' : '尚未保存'}，${rejection.compilationState === 'not-required' ? '输入方案' : '文档内容'}严格读取拒绝 ${errors.length || 1} 项：${detail}`
          : rejection.stage === 'compilation-failed' ? `已保存，编译失败（结束代码 ${rejection.exitCode}）`
            : `已保存且编译成功，Unity 资产发布失败：${detail}`;
        const outcome = { ok: false, message, errors, preview: null } as const;
        let lastSave: SaveOutcome;
        if (rejection.stage === 'source-rejected') {
          lastSave = rejection.compilationState === 'not-required'
            ? { ...outcome, saved: true, compilationState: 'not-required', previewError: null, jobError: null }
            : { ...outcome, saved: rejection.saved, compilationState: 'not-started' };
        } else if (rejection.stage === 'compilation-failed') {
          lastSave = { ...outcome, saved: true, compilationState: 'failed' };
        } else {
          lastSave = { ...outcome, saved: true, compilationState: 'succeeded', compilation: rejection.compilation,
            publicationState: 'failed', jobError: null };
        }
        if (rejection.content === null) setDocument({ saving: false, lastSave }, target);
        else accept(rejection.documents, rejection.content, lastSave);
        if (rejection.compilationState === 'not-required') setDocument({ inputRevision: { kind: 'rejected', reason: message } }, target);
        consoleActions.log('error', '作者', message, [...errors.map(item => `${item.file ?? ''} ${item.path ?? ''}：${item.message}`),
          detail, error.raw].filter(Boolean).join('\n'));
        return;
      }
      if (operation !== 'save' && error instanceof ApiError && error.status === 409) {
        const conflict = readSkillSaveConflict(JSON.parse(error.raw));
        const outcome = { ok: false, message: conflict.message, errors: [], preview: null } satisfies SaveOutcomeMessage & { preview: null };
        if (conflict.saved) accept(conflict.documents, conflict.content,
          { ...outcome, saved: true, compilationState: 'not-started' });
        else setDocument({ saving: false, externalChange: true,
          lastSave: { ...outcome, saved: false, compilationState: null } }, target);
        consoleActions.log('error', '作者', conflict.message, error.raw);
        return;
      }
      const revisionConflict = error instanceof ApiError && error.status === 409;
      const sourceRejected = operation === 'save' && error instanceof ApiError && [400, 409, 422].includes(error.status);
      setDocument({ saving: false, ...(revisionConflict ? { externalChange: true } : {}), lastSave: {
        ok: false, message: error instanceof Error ? error.message : String(error), errors: [],
        saved: revisionConflict || sourceRejected ? false : null, compilationState: null, preview: null,
      } }, target);
      consoleActions.error('作者', error);
    }
  },

  // 外部修改检测：无本地修改时直接重读，有修改时标出冲突。
  async checkExternal() {
    const { target, revision } = get();
    if (!target || !revision) return;
    const signal = loadController!.signal;
    try {
      const snapshot = await workspaceDocumentsApi.open(target.documentPath, signal);
      signal.throwIfAborted();
      if (snapshot.revision === revision) return;
      if (hasChanges(target)) setDocument({ externalChange: true }, target);
      else {
        setDocument({ identity: snapshot.identity, authoringType: snapshot.authoringType ?? null, revision: snapshot.revision, sourceRevision: null,
          version: authoringDocument(useAuthoring.getState(), target).version + 1,
          sourceFiles: snapshot.files,
          ...(usesGraphModels(snapshot.kind) ? {} : { files: snapshot.files }) }, target);
      }
    } catch (error) {
      consoleActions.error('作者', error);
    }
  },

  // ── Timeline ──

  createTimeline(template: ElementTemplate, name: string): string {
    const id = crypto.randomUUID();
    authoringActions.setFile(timelinePath(id), sortedObject({ ...cloneJson(template.value), m_AuthoringId: id, m_Name: name }));
    return id;
  },

  // 改起止；给出 clipInSeconds 时一并写 ClipIn（照 Slate 裁剪可设 ClipIn 片段的起点）。
  setClipTime(documentId: string, clipId: string, startSeconds: number, endSeconds: number, clipInSeconds?: number) {
    authoringActions.edit(timelinePath(documentId), document => {
      const clip = findTimelineClip(document as JsonObject, clipId);
      if (!clip) throw new Error(`Timeline ${documentId} 中没有片段 ${clipId}`);
      clip.m_StartTimeFPRawValue = fpRaw(startSeconds);
      clip.m_EndTimeFPRawValue = fpRaw(endSeconds);
      if (clipInSeconds !== undefined) clip.m_ClipInTimeFPRawValue = fpRaw(clipInSeconds);
    });
  },

  // 检查器字段：片段对象内按键路径写值。
  setClipValues(documentId: string, clipId: string, changes: readonly JsonMutation[]) {
    authoringActions.edit(timelinePath(documentId), document => {
      const clip = findTimelineClip(document as JsonObject, clipId);
      if (!clip) throw new Error(`Timeline ${documentId} 中没有片段 ${clipId}`);
      for (const change of changes) setIn(clip, change.keys, change.value);
    });
  },

  setTrackValue(documentId: string, trackId: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(timelinePath(documentId), document => {
      const track = tracksOf(document).find(item => item.m_AuthoringId === trackId);
      if (!track) throw new Error(`Timeline ${documentId} 中没有轨道 ${trackId}`);
      setIn(track, keys, value);
    });
  },

  // 轨道、片段与标记取原 C# 默认值，身份新建；删除时专属的片段图、标记图不再被引用就一并删除。
  addTrack(documentId: string, template: JsonObject, name: string): string {
    const id = crypto.randomUUID();
    authoringActions.edit(timelinePath(documentId), document => {
      tracksOf(document).push(sortedObject({ ...cloneJson(template), Name: name, m_AuthoringId: id, m_Clips: [], m_Markers: [] }));
    });
    return id;
  },

  deleteTracks(documentId: string, trackIds: string[]) {
    const removed = new Set(trackIds);
    const source = currentDocument(timelinePath(documentId));
    const seeds = source ? tracksOf(source).filter(track => removed.has(track.m_AuthoringId as string)).flatMap(referencedFiles) : [];
    authoringActions.edit(timelinePath(documentId), document => {
      (document as JsonObject).m_Tracks = tracksOf(document).filter(track => !removed.has(track.m_AuthoringId as string));
    });
    dropOrphans(seeds);
  },

  addClip(documentId: string, trackId: string, template: JsonObject, startSeconds: number, endSeconds: number, graph: string | null): string {
    const id = crypto.randomUUID();
    authoringActions.edit(timelinePath(documentId), document => {
      const track = tracksOf(document).find(item => item.m_AuthoringId === trackId);
      if (!track) throw new Error(`Timeline ${documentId} 中没有轨道 ${trackId}`);
      const clip: JsonObject = { ...cloneJson(template), m_AuthoringId: id, m_StartTimeFPRawValue: fpRaw(startSeconds), m_EndTimeFPRawValue: fpRaw(endSeconds) };
      if (graph !== null) clip.m_AssetTree = graph;
      (track.m_Clips as JsonObject[]).push(sortedObject(clip));
    });
    return id;
  },

  deleteClips(documentId: string, clipIds: string[]) {
    const removed = new Set(clipIds);
    const source = currentDocument(timelinePath(documentId));
    const seeds = source ? tracksOf(source).flatMap(track => [...track.m_Clips as JsonObject[], ...(track.m_Markers as JsonObject[] | undefined) ?? []])
      .filter(item => removed.has(item.m_AuthoringId as string)).flatMap(referencedFiles) : [];
    authoringActions.edit(timelinePath(documentId), document => {
      for (const track of tracksOf(document)) {
        track.m_Clips = (track.m_Clips as JsonObject[]).filter(clip => !removed.has(clip.m_AuthoringId as string));
        if (track.m_Markers) track.m_Markers = (track.m_Markers as JsonObject[]).filter(marker => !removed.has(marker.m_AuthoringId as string));
      }
    });
    dropOrphans(seeds);
  },

  // 轨道排序（照 Unity MoveTrack）：移到 index 位置。
  moveTrack(documentId: string, trackId: string, index: number) {
    authoringActions.edit(timelinePath(documentId), document => {
      const tracks = tracksOf(document);
      const from = tracks.findIndex(track => track.m_AuthoringId === trackId);
      if (from < 0) throw new Error(`Timeline ${documentId} 中没有轨道 ${trackId}`);
      const [track] = tracks.splice(from, 1);
      tracks.splice(Math.max(0, Math.min(index, tracks.length)), 0, track);
    });
  },

  // 复制粘贴片段（照 Unity CopyClip／PasteClip）：新身份、保留时长，tree 片段与原片段共用同一张图。
  copyClip(documentId: string, clipId: string): boolean {
    const document = currentDocument(timelinePath(documentId));
    const clip = document ? findTimelineClip(document as JsonObject, clipId) : null;
    clipClipboard = clip ? cloneJson(clip) : null;
    return clip !== null;
  },

  canPasteClip: () => clipClipboard !== null,

  pasteClip(documentId: string, trackId: string, startSeconds: number): string | null {
    const source = clipClipboard;
    if (!source) return null;
    const id = crypto.randomUUID();
    const length = numberOf(source.m_EndTimeFPRawValue) - numberOf(source.m_StartTimeFPRawValue);
    authoringActions.edit(timelinePath(documentId), document => {
      const track = tracksOf(document).find(item => item.m_AuthoringId === trackId);
      if (!track) throw new Error(`Timeline ${documentId} 中没有轨道 ${trackId}`);
      const start = fpRaw(startSeconds);
      (track.m_Clips as JsonObject[]).push(sortedObject({
        ...cloneJson(source), m_AuthoringId: id, m_StartTimeFPRawValue: start, m_EndTimeFPRawValue: start + length,
      }));
    });
    return id;
  },

  // 切分（照 Unity SplitClip）：原片段止于切点、去掉淡出，副本从切点开始、去掉淡入，权重与淡入淡出曲线按切点拆分；
  // 动画片段副本的起始偏移后移，位移曲线片段的素材区间在切点分开。tree 等片段没有正式切分。
  splitClip(documentId: string, clipId: string, seconds: number): string | null {
    const document = currentDocument(timelinePath(documentId));
    const source = document ? findTimelineClip(document as JsonObject, clipId) : null;
    if (!source) throw new Error(`Timeline ${documentId} 中没有片段 ${clipId}`);
    const kind = source.ContractKind as string;
    if (kind !== 'animation.clip' && kind !== 'motion-curve.clip') {
      consoleActions.log('warn', 'Timeline', `片段种类 ${kind} 没有正式切分`);
      return null;
    }
    const start = numberOf(source.m_StartTimeFPRawValue), end = numberOf(source.m_EndTimeFPRawValue), split = fpRaw(seconds);
    if (split <= start || split >= end) return null;
    const curves = ['WeightCurve', 'EaseInCurve', 'EaseOutCurve'].filter(key => source[key] && typeof source[key] === 'object');
    if (curves.some(key => isWeighted(source[key] as JsonObject))) {
      consoleActions.log('warn', 'Timeline', '片段含权重切线曲线，网页无法与 Unity 一致地拆分，请在 Unity 中切分');
      return null;
    }
    const ratio = (split - start) / (end - start);
    const id = crypto.randomUUID();
    authoringActions.edit(timelinePath(documentId), target => {
      const track = tracksOf(target).find(item => (item.m_Clips as JsonObject[]).some(clip => clip.m_AuthoringId === clipId))!;
      const first = (track.m_Clips as JsonObject[]).find(clip => clip.m_AuthoringId === clipId)!;
      const second: JsonObject = { ...cloneJson(first), m_AuthoringId: id, m_StartTimeFPRawValue: split, m_SelfEaseInTimeFPRawValue: 0 };
      if (kind === 'animation.clip') second.m_ClipInTimeFPRawValue = numberOf(first.m_ClipInTimeFPRawValue) + (split - start);
      else {
        const sourceSplit = numberOf(first.m_SourceStartTime) + fpSeconds(split - start);
        second.m_SourceStartTime = new JsonFloat(sourceSplit);
        first.m_SourceEndTime = new JsonFloat(sourceSplit);
      }
      for (const key of curves) {
        second[key] = splitCurve(first[key] as JsonObject, ratio, 1);
        first[key] = splitCurve(first[key] as JsonObject, 0, ratio);
      }
      first.m_EndTimeFPRawValue = split;
      first.m_SelfEaseOutTimeFPRawValue = 0;
      (track.m_Clips as JsonObject[]).push(sortedObject(second));
    });
    return id;
  },

  // 片段与标记平移、片段换轨：框选多个时整体移动同一位移，最早的不早于 0；标记落到同轨其它标记的时刻时不写回。
  // targetTrackId 只用于单个片段换到同一 Timeline 的另一轨道。
  moveItems(documentId: string, itemIds: string[], deltaSeconds: number, targetTrackId: string | null) {
    const ids = new Set(itemIds);
    const source = currentDocument(timelinePath(documentId));
    if (!source) throw new Error(`没有 Timeline ${documentId}`);
    const shift = Math.max(fpRaw(deltaSeconds), -Math.min(...tracksOf(source).flatMap(track => [
      ...(track.m_Clips as JsonObject[]).filter(clip => ids.has(clip.m_AuthoringId as string)).map(clip => numberOf(clip.m_StartTimeFPRawValue)),
      ...markersOf(track).filter(marker => ids.has(marker.m_AuthoringId as string)).map(marker => numberOf(marker.m_TimeFPRawValue)),
    ])));
    if (markersCollide(source, ids, shift)) {
      consoleActions.log('warn', '作者', '标记落到同轨已有标记的时刻，标记不能重叠');
      return;
    }
    authoringActions.edit(timelinePath(documentId), document => {
      const tracks = tracksOf(document);
      const target = targetTrackId === null ? null : tracks.find(track => track.m_AuthoringId === targetTrackId);
      if (targetTrackId !== null && !target) throw new Error(`Timeline ${documentId} 中没有轨道 ${targetTrackId}`);
      for (const track of tracks) {
        for (const clip of (track.m_Clips as JsonObject[]).filter(item => ids.has(item.m_AuthoringId as string))) {
          clip.m_StartTimeFPRawValue = numberOf(clip.m_StartTimeFPRawValue) + shift;
          clip.m_EndTimeFPRawValue = numberOf(clip.m_EndTimeFPRawValue) + shift;
          if (target && target !== track) {
            track.m_Clips = (track.m_Clips as JsonObject[]).filter(item => item !== clip);
            (target.m_Clips as JsonObject[]).push(clip);
          }
        }
        for (const marker of markersOf(track).filter(item => ids.has(item.m_AuthoringId as string)))
          marker.m_TimeFPRawValue = numberOf(marker.m_TimeFPRawValue) + shift;
      }
    });
  },

  // 淡入淡出（自身）时长，秒；不超过片段时长。
  setClipEase(documentId: string, clipId: string, easeIn: number | null, easeOut: number | null) {
    authoringActions.edit(timelinePath(documentId), document => {
      const clip = findTimelineClip(document as JsonObject, clipId);
      if (!clip) throw new Error(`Timeline ${documentId} 中没有片段 ${clipId}`);
      const length = numberOf(clip.m_EndTimeFPRawValue) - numberOf(clip.m_StartTimeFPRawValue);
      if (easeIn !== null) clip.m_SelfEaseInTimeFPRawValue = Math.max(0, Math.min(length - numberOf(clip.m_SelfEaseOutTimeFPRawValue), fpRaw(easeIn)));
      if (easeOut !== null) clip.m_SelfEaseOutTimeFPRawValue = Math.max(0, Math.min(length - numberOf(clip.m_SelfEaseInTimeFPRawValue), fpRaw(easeOut)));
    });
  },

  setMarkerTime(documentId: string, markerId: string, seconds: number) {
    const track = markerTrack(documentId, markerId);
    if (track && markerTimeTaken(documentId, track, seconds, markerId)) {
      consoleActions.log('warn', '作者', `同一轨道在 ${seconds.toFixed(3)} s 已有标记，标记不能重叠`);
      return;
    }
    authoringActions.edit(timelinePath(documentId), document => {
      const marker = tracksOf(document).flatMap(track => (track.m_Markers as JsonObject[] | undefined) ?? []).find(item => item.m_AuthoringId === markerId);
      if (!marker) throw new Error(`Timeline ${documentId} 中没有标记 ${markerId}`);
      marker.m_TimeFPRawValue = fpRaw(seconds);
    });
  },

  // 片段图种类（tree.clip）带专属 TimelineBody 图；标记带专属 TimelineTrigger 图。
  async addClipAt(documentId: string, trackId: string, template: ElementTemplate, startSeconds: number, endSeconds: number): Promise<string> {
    const elements = await skillElements();
    const graph = 'm_AssetTree' in template.value ? createGraph(elements, { role: 'TimelineBody', name: '片段内容' }) : null;
    return authoringActions.addClip(documentId, trackId, template.value, startSeconds, endSeconds, graph);
  },

  async addMarkerAt(documentId: string, trackId: string, seconds: number): Promise<string | null> {
    const elements = await skillElements();
    if (markerTimeTaken(documentId, trackId, seconds, null)) {
      consoleActions.log('warn', '作者', `同一轨道在 ${seconds.toFixed(3)} s 已有标记，标记不能重叠`);
      return null;
    }
    return authoringActions.addMarker(documentId, trackId, elements.marker.value, seconds, createGraph(elements, { role: 'TimelineTrigger', name: '标记触发' }));
  },

  addMarker(documentId: string, trackId: string, template: JsonObject, seconds: number, graph: string): string {
    const id = crypto.randomUUID();
    authoringActions.edit(timelinePath(documentId), document => {
      const track = tracksOf(document).find(item => item.m_AuthoringId === trackId);
      if (!track) throw new Error(`Timeline ${documentId} 中没有轨道 ${trackId}`);
      ((track.m_Markers ??= []) as JsonObject[]).push(sortedObject({
        ...cloneJson(template), m_AuthoringId: id, m_Graph: graph, m_TimeFPRawValue: fpRaw(seconds),
      }));
    });
    return id;
  },
};

export const tracksOf = (document: JsonValue) => (document as JsonObject).m_Tracks as JsonObject[];

const markersOf = (track: JsonObject) => (track.m_Markers as JsonObject[] | undefined) ?? [];

// 标记所在轨道的作者身份；当前文档里找不到时为 null。
function markerTrack(documentId: string, markerId: string): string | null {
  const document = currentDocument(timelinePath(documentId));
  const track = document ? tracksOf(document).find(item => markersOf(item).some(marker => marker.m_AuthoringId === markerId)) : undefined;
  return (track?.m_AuthoringId as string | undefined) ?? null;
}

// 标记不许重叠：同一轨道上除 except 外是否已有标记在这一时刻（按定点时刻比较）。
export function markerTimeTaken(documentId: string, trackId: string, seconds: number, except: string | null): boolean {
  const document = currentDocument(timelinePath(documentId));
  const track = document ? tracksOf(document).find(item => item.m_AuthoringId === trackId) : undefined;
  return !!track && markersOf(track).some(marker => marker.m_AuthoringId !== except && numberOf(marker.m_TimeFPRawValue) === fpRaw(seconds));
}

// 一起平移 shift（定点值）的标记是否落到同轨其它标记的时刻；一起移动的标记之间不算。
export function markersCollide(document: JsonValue, ids: ReadonlySet<string>, shift: number): boolean {
  return tracksOf(document).some(track => {
    const fixed = new Set(markersOf(track).filter(marker => !ids.has(marker.m_AuthoringId as string)).map(marker => numberOf(marker.m_TimeFPRawValue)));
    return markersOf(track).some(marker => ids.has(marker.m_AuthoringId as string) && fixed.has(numberOf(marker.m_TimeFPRawValue) + shift));
  });
}


let clipClipboard: JsonObject | null = null;


export const hasChanges = (target = get().target) => {
  const document = authoringDocument(useAuthoring.getState(), target);
  return Object.keys(document.edited).length + document.removed.length > 0;
};

function referenceReplacementOwners(plan: ReferenceReplacementResult) {
  return plan.affectedSkills.map(name => {
    const root = plan.readFiles.find(file => file.path.replaceAll('\\', '/').endsWith(`/Skills/${name}/skill.json`))!;
    const prefix = root.path.slice(0, -'skill.json'.length);
    return { target: authoringTarget('skill', name),
      paths: new Set(plan.files.filter(file => file.path.startsWith(prefix)).map(file => file.path.slice(prefix.length).replaceAll('\\', '/'))) };
  });
}

export function referenceReplacementConflicts(plan: ReferenceReplacementResult, state = useAuthoring.getState(), phase: 'commit' | 'refresh' = 'commit') {
  const conflicts: { owner: string; path: string | null; reason: string }[] = [];
  for (const { target, paths } of referenceReplacementOwners(plan)) {
    const document = state.documents[authoringTargetKey(target)];
    if (document === undefined) continue;
    const documentReason = document.saving ? '正在保存' : document.loading ? '正在读取'
      : phase === 'commit' && document.externalChange ? '磁盘来源已变化' : null;
    if (documentReason !== null) {
      conflicts.push({ owner: target.documentPath, path: null, reason: documentReason });
      continue;
    }
    for (const path of paths) {
      if (Object.hasOwn(document.edited, path) || document.removed.includes(path))
        conflicts.push({ owner: target.documentPath, path, reason: '有未保存修改' });
    }
  }
  return conflicts;
}

// ── 撤销：每步前保存编辑副本、待删文件与新节点端口；编辑在文档上原地进行，快照取深拷贝 ──

interface Snapshot extends Pick<SelectionSlice, 'selection' | 'canvasSelections' | 'timelineSelections'> {
  owner: AuthoringTarget;
  edited: Record<string, JsonValue>;
  removed: string[];
  addedPorts: Record<string, AddedPort[]>;
  relatedDocuments: Record<string, AuthoringDocument | null>;
}

export const historyLimit = 100;

const snapshotDocuments = (paths: readonly string[]): Record<string, AuthoringDocument | null> =>
  Object.fromEntries(paths.map((path): [string, AuthoringDocument | null] => {
    const document = useAuthoring.getState().documents[path];
    return [path, document === undefined ? null : { ...document,
      edited: Object.fromEntries(Object.entries(document.edited).map(([file, value]) => [file, cloneJson(value)])),
      removed: [...document.removed] }];
  }));

export const snapshot = (owner = get().target!, relatedDocuments: readonly string[] = []): Snapshot => {
  const state = authoringDocument(useAuthoring.getState(), owner);
  const { selection, canvasSelections, timelineSelections } = useWorkbench.getState();
  return { owner, edited: Object.fromEntries(Object.entries(state.edited).map(([path, document]) => [path, cloneJson(document)])),
    removed: [...state.removed], addedPorts: state.addedPorts, relatedDocuments: snapshotDocuments(relatedDocuments),
    selection, canvasSelections, timelineSelections };
};

function workingFiles(files: Record<string, string>, document: Pick<Snapshot, 'edited' | 'removed'>, rootFile: string | null) {
  const result = { ...files };
  const format = shapeOf(rootFile === null ? '\n' : files[rootFile] ?? '\n');
  for (const [path, value] of Object.entries(document.edited))
    result[path] = writeSkillJson(value, files[path] !== undefined ? shapeOf(files[path]) : format);
  for (const path of document.removed) delete result[path];
  return result;
}

function documentWrite(documentPath: string, document: AuthoringDocument): WorkspaceDocumentWrite {
  const files = { ...document.sourceFiles };
  for (const [path, content] of Object.entries(workingFiles(document.files, document, document.rootFile)))
    if (content !== document.files[path]) files[path] = content;
  for (const path of document.removed) delete files[path];
  return { kind: document.kind, documentPath, expectedRevision: document.revision ?? '', files };
}

function rebaseSavedEdits(previousFiles: Record<string, string>, submittedFiles: Record<string, string>,
  savedFiles: Record<string, string>, document: Pick<Snapshot, 'edited' | 'removed' | 'addedPorts'>, rootFile: string | null) {
  const working = workingFiles(previousFiles, document, rootFile);
  const edited: Record<string, JsonValue> = {};
  const removed: string[] = [];
  for (const path of new Set([...Object.keys(submittedFiles), ...Object.keys(working)])) {
    if (working[path] === submittedFiles[path]) continue;
    if (working[path] === undefined) {
      if (savedFiles[path] !== undefined) removed.push(path);
    } else if (working[path] !== savedFiles[path]) edited[path] = parseSkillJson(working[path]);
  }
  return { edited, removed, addedPorts: Object.keys(edited).length === 0 ? {} : document.addedPorts };
}

function record(relatedDocuments: readonly string[] = []) {
  if (get().resourceTarget !== null) set({ resourceTarget: null });
  const state = get();
  if (state.recording) {
    if (relatedDocuments.length !== 0) {
      const previous = state.undo.at(-1)!;
      setDocument({ undo: [...state.undo.slice(0, -1), { ...previous,
        relatedDocuments: { ...snapshotDocuments(relatedDocuments), ...previous.relatedDocuments } }] });
    }
    return;
  }
  const target = state.target!;
  setDocument({ undo: [...state.undo, snapshot(target, relatedDocuments)].slice(-historyLimit), redo: [], recording: true }, target);
  queueMicrotask(() => {
    setDocument({ recording: false }, target);
    if (isActiveTarget(target)) reconcileSelection();
  });
}

function updateScope(query: ScopeQuery, document: ScopeDocument) {
  set({ scopes: { ...useAuthoring.getState().scopes, [scopeQueryKey(query)]: document } });
}

async function step(from: 'undo' | 'redo', to: 'undo' | 'redo') {
  const owner = get().resourceTarget;
  if (owner !== null) return;
  const state = get();
  const previous = state[from].at(-1);
  if (!previous || state.saving) return;
  const target = state.target!;
  const { owner: source, selection, canvasSelections, timelineSelections, relatedDocuments, ...content } = previous;
  const document = state.documents[source.documentPath];
  const related = Object.keys(relatedDocuments);
  if (document.variablesWriting || related.some(path => state.documents[path]?.variablesWriting)) return;
  const next = snapshot(source, related);
  const documents = { ...state.documents, [source.documentPath]: { ...document, ...content, variables: null, version: document.version + 1 } };
  for (const [path, restored] of Object.entries(relatedDocuments)) {
    if (restored === null) delete documents[path];
    else documents[path] = { ...restored, variables: null };
  }
  documents[target.documentPath] = { ...documents[target.documentPath], [from]: state[from].slice(0, -1), [to]: [...state[to], next] };
  set({ documents });
  if (isActiveTarget(target)) {
    useWorkbench.setState({ selection, canvasSelections, timelineSelections });
    reconcileSelection();
  }
}

// ── 专属内容：图与 Timeline 文件以文件名为身份，被删除的元素引用、且别处不再引用的文件随之删除 ──

const fileIdOf = (path: string) => /^(?:graphs|timelines)\/(.+)\.json$/.exec(path)?.[1] ?? null;
// 文件自身与所属关系的身份字段不算引用。
const selfKeys = new Set(['AuthoringId', 'OwnerGraphId', 'OwnerNodeId']);

function collectIds(value: JsonValue | undefined, ids: ReadonlySet<string>, out: Set<string>) {
  if (typeof value === 'string') { if (ids.has(value)) out.add(value); return; }
  if (value === null || typeof value !== 'object' || value instanceof JsonFloat) return;
  if (Array.isArray(value)) { for (const item of value) collectIds(item, ids, out); return; }
  for (const [key, item] of Object.entries(value)) if (!selfKeys.has(key)) collectIds(item, ids, out);
}

export function referencedFiles(value: JsonValue): string[] {
  const out = new Set<string>();
  collectIds(value, new Set(livePaths().map(fileIdOf).filter((id): id is string => id !== null)), out);
  return [...out];
}

function livePaths(): string[] {
  const state = get();
  return [...new Set([...Object.keys(state.files), ...Object.keys(state.edited)])].filter(path => !state.removed.includes(path));
}

// seeds 为删掉的元素引用到的文件身份；沿引用找出它们的专属内容，排除仍被其它文件引用的部分。
function orphanFiles(seeds: string[]): string[] {
  const paths = livePaths();
  const byId = new Map(paths.flatMap(path => { const id = fileIdOf(path); return id ? [[id, path] as const] : []; }));
  const ids = new Set(byId.keys());
  const candidates = new Set<string>();
  const queue = seeds.filter(id => ids.has(id));
  while (queue.length) {
    const id = queue.pop()!;
    if (candidates.has(id)) continue;
    candidates.add(id);
    const found = new Set<string>();
    collectIds(currentDocument(byId.get(id)!) ?? null, ids, found);
    queue.push(...found);
  }
  const kept = new Set<string>();
  for (const path of paths) {
    const id = fileIdOf(path);
    if (path === layoutPath || (id && candidates.has(id))) continue;
    collectIds(currentDocument(path) ?? null, candidates, kept);
  }
  const keep = [...kept];
  while (keep.length) {
    const found = new Set<string>();
    collectIds(currentDocument(byId.get(keep.pop()!)!) ?? null, candidates, found);
    for (const id of found) if (!kept.has(id)) { kept.add(id); keep.push(id); }
  }
  return [...candidates].filter(id => !kept.has(id)).map(id => byId.get(id)!);
}

// 删除不再被引用的专属文件及其布局。
export function dropOrphans(seeds: string[]) {
  const orphans = orphanFiles(seeds);
  if (!orphans.length) return;
  const graphs = orphans.filter(path => path.startsWith('graphs/')).map(path => fileIdOf(path)!);
  if (graphs.length && currentDocument(layoutPath))
    authoringActions.edit(layoutPath, document => { for (const id of graphs) delete (document as JsonObject)[id]; });
  authoringActions.remove(orphans);
}

export type JsonKey = string | number;
export interface JsonMutation { keys: JsonKey[]; value: JsonValue }

// 文件的当前内容：编辑过的取编辑版本，否则解析原文；只读使用。
// 原文解析结果按文本缓存；调用方只读。
const parsedFiles = new Map<string, { text: string; document: JsonValue }>();

export function currentDocument(path: string): JsonValue | null {
  const state = get();
  if (state.removed.includes(path)) return null;
  if (state.edited[path]) return state.edited[path];
  const text = state.files[path];
  if (text === undefined) return null;
  const cached = parsedFiles.get(path);
  if (cached?.text === text) return cached.document;
  const document = parseSkillJson(text);
  parsedFiles.set(path, { text, document });
  return document;
}

// 编辑在原文档对象上原地修改，version 变化即重新读取。
export function useCurrentDocument(path: string | null): { document: JsonValue | null; version: number } {
  const { edited, text, removed, version } = useAuthoring(useShallow(state => {
    const document = authoringDocument(state);
    return { edited: path ? document.edited[path] : undefined, text: path ? document.files[path] : undefined,
      removed: !!path && document.removed.includes(path), version: document.version };
  }));
  return useMemo(() => ({ document: removed ? null : edited ?? (text !== undefined ? parseSkillJson(text) : null), version }),
    [edited, text, removed, version]);
}

export const isDocumentGraph = (graphId: string) => {
  const { target, identity } = get();
  return target?.kind === 'graph' && identity === graphId;
};
export const graphPath = (graphId: string) => isDocumentGraph(graphId) ? authoringRootFiles.graph : `graphs/${graphId}.json`;
export const layoutPath = 'layout.json';
export const graphLayoutPath = (graphId: string) => isDocumentGraph(graphId) ? authoringRootFiles.graph : layoutPath;
export const graphLayoutKey = (graphId: string) => isDocumentGraph(graphId) ? 'layout' : graphId;



export const variableRequests = new Map<string, Promise<string>>();


export function skillElements(context?: InputPlanContext): Promise<SkillElements> {
  const target = get().target;
  if (!target) return Promise.reject(new Error('没有打开的作者目录'));
  const signal = loadController!.signal;
  return authoringApi.elements(target, authoringActions.editingView(), signal, context).then(elements => { signal.throwIfAborted(); return elements; });
}

// 各面板共用同一作者视图的一次元素查询；结果对应的视图已过期时丢弃，等新视图重新查询。
let elementsRequest: { key: string; promise: Promise<SkillElements | null> } | null = null;

function sharedElements(key: string, target: AuthoringTarget) {
  if (elementsRequest?.key === key) return elementsRequest.promise;
  const { editingView, isCurrentRequest } = captureEditingView();
  const promise = authoringApi.elements(target, editingView, loadController?.signal).then(items => isCurrentRequest() ? items : null);
  const request = elementsRequest = { key, promise };
  promise.then(items => { if (items === null && elementsRequest === request) elementsRequest = null; }, (error: unknown) => {
    if (elementsRequest === request) elementsRequest = null;
    if (!isAbort(error)) consoleActions.error('作者', error);
  });
  return promise;
}

// 文档改动后重新查询期间，同一技能、载体与场景下继续给出上一份结果，连线标签与图槽位不随每次编辑清空；
// 新建等写入操作经 skillElements 读取当前视图。
export function useSkillElements(enabled: boolean): SkillElements | null {
  const { target, loading, owners, carrierRevision } = useAuthoring(useShallow(state => ({
    target: state.target, loading: authoringDocument(state).loading, carrierRevision: state.carrierRevision,
    owners: JSON.stringify(Object.entries(state.documents).map(([path, document]) => [path, document.revision, document.version])),
  })));
  const scene = useWorkbench(state => selectedScene(state));
  const base = JSON.stringify([target === null ? null : authoringTargetKey(target), carrierRevision, scene]);
  const key = JSON.stringify([base, owners]);
  const [elements, setElements] = useState<{ base: string; items: SkillElements } | null>(null);
  useEffect(() => {
    if (!enabled || loading || target === null) return;
    let current = true;
    sharedElements(key, target).then(items => { if (current && items) setElements({ base, items }); }, () => {});
    return () => { current = false; };
  }, [enabled, key, loading]);
  return enabled && !loading && elements?.base === base ? elements.items : null;
}

// 新建图：按角色、初始内容与归属选宿主模板；名称省略时用模板（合同默认）名称。
export interface GraphCreation {
  role: string;
  seed?: GraphTemplate['seed'];
  ownership?: GraphTemplate['ownership'];
  name?: string | null;
  context?: { OwnerGraphId: string; OwnerNodeId: string };
}

export function createGraph(elements: SkillElements, { role, seed = 'Anchors', ownership = null, name = null, context }: GraphCreation): string {
  const template = elements.graphs.find(item => item.role === role && item.seed === seed && item.ownership === ownership);
  if (!template) throw new Error(`正式作者接口未提供 ${role} / ${seed} / ${ownership ?? '专属'} 图的创建合同`);
  const graph = cloneJson(template.value);
  const id = graph.AuthoringId as string;
  if (name !== null) graph.name = name;
  if (template.requiredContext.length !== 0 && context === undefined)
    throw new Error(`${role} 必须从实际拥有节点创建：${template.requiredContext.join('、')}`);
  for (const field of template.requiredContext) graph[field] = context![field];
  authoringActions.setFile(graphPath(id), graph, variablesDrafts(template.variables === null ? [] : [template.variables]));
  authoringActions.edit(layoutPath, document => { (document as JsonObject)[id] = cloneJson(template.layout); });
  return id;
}


export function applyGraphEdits(graph: SkillDocumentGraph, edited: Record<string, JsonValue>,
  addedPorts: Record<string, AddedPort[]>): SkillDocumentGraph {
  const path = graph.authoringPath;
  const document = edited[graphPath(graph.id)] as JsonObject | undefined;
  const layout = (edited[graphLayoutPath(graph.id)] as JsonObject | undefined)?.[graphLayoutKey(graph.id)] as JsonObject | undefined;
  if (!document && !layout) return graph;
  const positions = (layout ? layoutNodesOf(layout) ?? {} : {}) as Record<string, JsonObject>;
  const positionOf = (id: string, fallback: GraphNode['position']) => {
    const position = positions[id]?.position as JsonObject | undefined;
    return position ? { x: numberOf(position.x), y: numberOf(position.y) } : fallback;
  };
  const sourceNodes = document ? nodesOf(document) : null;
  const sources = new Map((sourceNodes ?? []).map(node => [nodeId(node), node]));
  const kept = sourceNodes ? new Set(sources.keys()) : null;
  const known = new Set(graph.nodes.map(node => node.id));
  const nodes: GraphNode[] = graph.nodes.filter(node => !kept || kept.has(node.id))
    .map(node => {
      const source = sources.get(node.id);
      return { ...node, position: positionOf(node.id, node.position), displayName: (source && storedNodeName(source)) || node.displayName };
    });
  const ports: GraphPort[] = graph.ports.filter(port => !kept || kept.has(port.nodeId));
  for (const node of sourceNodes ?? []) {
    const id = nodeId(node);
    if (known.has(id)) continue;
    nodes.push({ id, displayName: nodeLabel(node), invocationPath: path, position: positionOf(id, { x: 0, y: 0 }) });
    for (const port of addedPorts[id] ?? [])
      ports.push({ nodeId: id, id: port.id, displayName: port.displayName, direction: port.direction, isTrigger: port.isTrigger, valueType: port.valueType, invocationPath: path });
  }
  let edges = graph.edges;
  if (sourceNodes) {
    const triggers = new Set(ports.filter(port => port.isTrigger).map(port => `${port.nodeId}/${port.direction}/${port.id}`));
    const connections = sourceNodes.flatMap(node => connectionsOf(node).map(connection => ({ node, connection })));
    const ids = new Set(connections.map(item => connectionId(item.connection)));
    const existing = new Set(graph.edges.map(edge => edge.id));
    edges = [
      ...graph.edges.filter(edge => ids.has(edge.id)),
      // 状态机转移没有端口，带转移载荷。
      ...connections.filter(item => !existing.has(connectionId(item.connection))).map(({ node, connection }) => ({
        id: connectionId(connection),
        kind: transitionPayload(connection) ? 'Transition' : triggers.has(`${nodeId(node)}/output/${connectionSourcePort(connection)}`) ? 'Child' : 'Value',
        invocationPath: path, available: true, reason: '',
        source: { graphId: graph.id, nodeId: nodeId(node), portId: connectionSourcePort(connection) ?? '' },
        target: { graphId: graph.id, nodeId: connectionTarget(connection), portId: connectionTargetPort(connection) ?? '' },
      })),
    ];
  }
  return { ...graph, nodes, ports, edges };
}

export function findEditedGraph(graphs: SkillDocumentGraph[] | null, selectId: string, edited: Record<string, JsonValue>,
  addedPorts: Record<string, AddedPort[]>): { graph: SkillDocumentGraph; path: string; id: string } | null {
  for (const graph of graphs ?? []) {
    const path = graph.authoringPath;
    const prefix = `${graph.actorId}/${path}/`;
    const id = selectId.slice(prefix.length);
    if (!selectId.startsWith(prefix) || id.includes('/')) continue;
    return { graph: applyGraphEdits(graph, edited, addedPorts), path, id };
  }
  return null;
}

export function findTimelineClip(timeline: JsonObject, clipId: string): JsonObject | null {
  for (const track of (timeline.m_Tracks as JsonObject[] | undefined) ?? [])
    for (const clip of (track.m_Clips as JsonObject[] | undefined) ?? [])
      if (clip.m_AuthoringId === clipId) return clip;
  return null;
}

// 作者 Timeline 叠加未保存修改：轨道、片段与标记取编辑后的文档，宿主给出的片段显示名按身份保留。
export function applyTimelineEdits(timelines: AuthorTimeline[], edited: Record<string, JsonValue>, removed: readonly string[]): AuthorTimeline[] {
  const source = timelines.filter(timeline => !removed.includes(timelinePath(timeline.documentId)));
  const result = source.map(timeline => {
    const document = edited[timelinePath(timeline.documentId)] as JsonObject | undefined;
    if (!document) return timeline;
    const names = new Map(timeline.tracks.flatMap(track => track.clips.map(clip => [clip.id, clip.displayName])));
    const tracks = timelineOf(timeline.documentId, document).tracks;
    return {
      ...timeline,
      contentIdentity: null,
      contentRevision: null,
      tracks: tracks.map(track => ({ ...track, clips: track.clips.map(clip => ({ ...clip, displayName: names.get(clip.id) ?? null })) })),
    };
  });
  for (const [path, document] of Object.entries(edited))
    if (isTimelineFile(path) && !removed.includes(path)) {
      const id = (document as JsonObject).m_AuthoringId as string;
      if (!source.some(timeline => timeline.documentId === id)) result.push(timelineOf(id, document as JsonObject));
    }
  return result;
}

export function useAuthorTimelines(): AuthorTimeline[] {
  const timelines = useWorkbench(state => state.timelines);
  const { edited, removed } = useAuthoring(useShallow(state => ({
    edited: authoringDocument(state).edited, removed: authoringDocument(state).removed,
  })));
  return useMemo(() => applyTimelineEdits(timelines, edited, removed), [timelines, edited, removed]);
}

// 已编辑过的片段（`documentId/clipId`），Timeline 上标出未保存。
export function editedClipKeys(timelines: AuthorTimeline[], edited: Record<string, JsonValue>, files: Record<string, string>): Set<string> {
  const result = new Set<string>();
  for (const timeline of timelines) {
    const path = timelinePath(timeline.documentId);
    const document = edited[path] as JsonObject | undefined;
    if (!document || files[path] === undefined) continue;
    const original = parseSkillJson(files[path]) as JsonObject;
    for (const track of timeline.tracks)
      for (const clip of track.clips) {
        const now = findTimelineClip(document, clip.id), before = findTimelineClip(original, clip.id);
        if (now && before && (numberOf(now.m_StartTimeFPRawValue) !== numberOf(before.m_StartTimeFPRawValue) ||
          numberOf(now.m_EndTimeFPRawValue) !== numberOf(before.m_EndTimeFPRawValue)))
          result.add(`${timeline.documentId}/${clip.id}`);
      }
  }
  return result;
}
