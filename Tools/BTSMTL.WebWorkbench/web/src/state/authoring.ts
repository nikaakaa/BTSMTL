import { useEffect, useMemo, useState } from 'react';
import { create } from 'zustand';
import { useShallow } from 'zustand/react/shallow';
import { authoringApi, type ElementTemplate, type PlayerSkillRefreshReceipt, type PlayerSkillRefreshTarget, type SkillElements, type SkillFormatError, type SkillSaveRejection, type SkillSaveResult, type SkillSnapshot } from '../api/authoring';
import { ApiError, isAbort } from '../api/http';
import { playerApi } from '../api/player';
import type { PlayerCommandReceipt } from '../api/playerRecords';
import type { AuthorTimeline, GraphDefinition, GraphNode, GraphPort } from '../api/types';
import {
  JsonFloat, cloneJson, fpRaw, fpSeconds, numberOf, parseSkillJson, shapeOf, writeSkillJson, type JsonObject, type JsonValue,
} from './skillJson';
import { isWeighted, splitCurve } from './curveSplit';
import { consoleActions, selectionActions, useWorkbench } from './store';
import { findGraphEdge, findGraphNode } from './graphModel';
import type { DocumentObservationBinding } from './graphModel';
import { findTimelineClip as findTimelineClipInViews, findTimelineMarker, findTimelineTrack } from './timelineModel';
import { playerService } from './player';
import { session } from './session';

// 作者编辑（共享文档 9.x）：当前运行所属技能的 JSON 文件。原文按路径保存；编辑过的文件解析后改写，
// 保存时整份写回。宿主未启用作者接口时 available=false，作者模式不可用。

export interface AuthoringState {
  // null 为尚未探测；false 为宿主未启用作者接口。
  available: boolean | null;
  unavailableReason: string | null;
  // 作者模式：Timeline、节点图可编辑；运行观察时只读。
  author: boolean;
  programId: string | null;
  skill: string | null;
  revision: string | null;
  sourceRevision: string | null;
  files: Record<string, string>;
  // 编辑过及新建的文件（路径 → 解析后的文档），逐次修改递增 version。
  edited: Record<string, JsonValue>;
  // 待删除的原文件（删除状态、节点或片段后不再被引用的专属图与 Timeline）。
  removed: string[];
  version: number;
  // 可撤销与可重做的步数。
  undoDepth: number;
  redoDepth: number;
  loading: boolean;
  // 外部修改了技能文件而本地有未保存修改。
  externalChange: boolean;
  // 网页新增（含粘贴）节点的端口：来自类型描述或复制源，保存并重跑前用于显示与连线。
  addedPorts: Record<string, AddedPort[]>;
  saving: boolean;
  // 最近一次保存的编译结论；文件写入后由正式编译器判断。
  lastSave: SaveOutcome | null;
  // 技能文件夹；null 为尚未读取。
  skills: string[] | null;
  // 不经运行从 JSON 展开的全部图实例（未叠加网页修改）；严格读取被拒时为空并给出原因。
  documentGraphs: GraphDefinition[] | null;
  documentError: string | null;
  tickRate: number | null;
}

interface SaveOutcomeMessage {
  ok: boolean;
  message: string;
  errors: SkillFormatError[];
}

export type SaveOutcome = SaveOutcomeMessage & (
  | { saved: true; compilationState: 'succeeded'; preview: PlayerSkillRefreshReceipt | null }
  | { saved: true; compilationState: 'not-required'; preview: PlayerCommandReceipt | null }
  | { saved: true; compilationState: 'not-started' | 'failed'; preview: null }
  | { saved: null; compilationState: null; preview: null }
);

export interface AddedPort {
  id: string;
  displayName: string;
  direction: 'input' | 'output';
  isTrigger: boolean;
}

export const useAuthoring = create<AuthoringState>()(() => ({
  available: null, unavailableReason: null, author: false, programId: null, skill: null, revision: null, sourceRevision: null,
  files: {}, edited: {}, removed: [], version: 0, undoDepth: 0, redoDepth: 0, loading: false, externalChange: false, addedPorts: {},
  saving: false, lastSave: null,
  skills: null, documentGraphs: null, documentError: null, tickRate: null,
}));

const get = useAuthoring.getState;
const set = useAuthoring.setState;

export const useDocumentObservationBinding = (): DocumentObservationBinding => useAuthoring(useShallow(state => ({
  programId: state.programId, sourceRevision: state.sourceRevision,
  dirty: Object.keys(state.edited).length > 0 || state.removed.length > 0,
})));

const abilityOf = (programId: string) => programId.slice(programId.indexOf(':') + 1);

export const timelinePath = (documentId: string) => `timelines/${documentId}.json`;

let loadController: AbortController | null = null;
let previewController: AbortController | null = null;

useAuthoring.subscribe((state, previous) => {
  if (state.skill !== previous.skill) previewController?.abort();
});

type PreviewSelection = { kind: 'selected'; target: PlayerSkillRefreshTarget } | { kind: 'unavailable'; reason: string };
type SavedCompilation = Pick<Extract<SkillSaveResult, { compilationState: 'succeeded' }>, 'saved' | 'compilationState'>;

async function selectedPreview(): Promise<PreviewSelection> {
  const liveId = useWorkbench.getState().liveId;
  if (!liveId) return { kind: 'unavailable', reason: '未选择 Player，预览未刷新' };
  try {
    const live = await playerApi.live(liveId);
    if (live.previewTarget === null)
      return { kind: 'unavailable', reason: '所选 Player 尚无已确认载入的内容，预览未刷新' };
    const { liveId: targetLiveId, scene, sceneRevision, source } = live.previewTarget;
    return { kind: 'selected', target: { requestId: crypto.randomUUID(), liveId: targetLiveId, scene, sceneRevision, source } };
  } catch (error) {
    if (!(error instanceof ApiError)) throw error;
    return { kind: 'unavailable', reason: `无法读取所选 Player，预览未刷新：${error.reason}` };
  }
}

const previewMessages = {
  accepted: '预览已受理，等待 Player 采用',
  adopted: 'Player 已采用本次内容；本次运行档案尚未提供',
  failed: 'Player 采用失败',
  unavailable: '预览不可用',
} satisfies Record<PlayerSkillRefreshReceipt['stage'], string>;

function savedOutcome(save: SavedCompilation, preview: PlayerSkillRefreshReceipt | null, noPreviewReason: string): SaveOutcome {
  const detail = preview === null ? noPreviewReason : `${previewMessages[preview.stage]}${preview.error === null ? '' : `：${preview.error}`}`;
  return {
    saved: save.saved, compilationState: save.compilationState, preview,
    ok: preview === null || preview.stage === 'accepted' || preview.stage === 'adopted',
    message: `已保存，编译通过；${detail}`, errors: [],
  };
}

async function trackPreview(input: { save: SavedCompilation; receipt: PlayerSkillRefreshReceipt; controller: AbortController }) {
  let receipt = input.receipt;
  try {
    while (receipt.stage === 'accepted') {
      await new Promise(resolve => setTimeout(resolve, 1000));
      input.controller.signal.throwIfAborted();
      const next = await authoringApi.refreshStatus(receipt.statusPath, input.controller.signal);
      if (next.stage !== receipt.stage) {
        const outcome = savedOutcome(input.save, next, '');
        set({ lastSave: outcome });
        consoleActions.log(outcome.ok ? 'info' : 'error', '预览', outcome.message, JSON.stringify(next));
      }
      receipt = next;
    }
    if (receipt.stage === 'adopted' && useWorkbench.getState().liveId === receipt.liveId) {
      session.selectRun(null);
      await Promise.all([playerService.refresh(), session.refresh()]);
    }
  } catch (error) {
    if (isAbort(error)) return;
    set({ lastSave: {
      ...savedOutcome(input.save, receipt, ''), ok: false,
      message: `已保存，编译通过；预览状态读取失败：${error instanceof Error ? error.message : String(error)}`,
    } });
    consoleActions.error('预览', error);
  }
}

// 技能 JSON 中的 Timeline：轨道、片段与标记，时间由 FP 原始值换算；片段与标记的图为 m_AssetTree／m_Graph。
export function documentTimelines(files: Record<string, string>, sourceRevision: string | null): AuthorTimeline[] {
  return Object.entries(files).filter(([path]) => path.startsWith('timelines/') && path.endsWith('.json'))
    .map(([path, text]) => timelineOf(path.slice('timelines/'.length, -'.json'.length), parseSkillJson(text) as JsonObject, sourceRevision));
}

function timelineOf(documentId: string, document: JsonObject, sourceRevision: string | null): AuthorTimeline {
  return {
    documentId, contentIdentity: documentId, contentRevision: sourceRevision, displayName: document.m_Name as string,
    tracks: (document.m_Tracks as JsonObject[]).map(track => ({
      id: track.m_AuthoringId as string, displayName: track.Name as string, muted: track.m_PersistentMuted === true,
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
  const { documentGraphs, files, skill, sourceRevision } = get();
  useWorkbench.setState({
    graphs: skill ? documentGraphs : null, graphAvailability: [], timelines: skill ? documentTimelines(files, sourceRevision) : [],
  });
}

useWorkbench.subscribe((state, previous) => {
  if (state.generation !== previous.generation) publishDocument();
});

async function readDocument(skill: string, signal?: AbortSignal) {
  const revision = get().revision;
  try {
    const document = await authoringApi.document(skill, signal);
    if (get().skill !== skill || get().revision !== revision) return;
    set({ documentGraphs: document.graphs, sourceRevision: document.sourceRevision,
      documentError: null, tickRate: document.tickRate, programId: document.programId });
  } catch (error) {
    if (isAbort(error)) throw error;
    if (get().skill !== skill || get().revision !== revision) return;
    set({ documentGraphs: [], sourceRevision: null, documentError: error instanceof ApiError ? error.reason : String(error) });
    consoleActions.error('作者', error);
  }
}

function reconcileSelection() {
  const { selection, graphs, timelines } = useWorkbench.getState();
  if (!selection || selection.kind === 'actor' || selection.id.startsWith('input/')) return;
  const { kind, id } = selection;
  const exists = kind === 'node' ? findGraphNode(graphs, id) : kind === 'edge' ? findGraphEdge(graphs, id)
    : kind === 'track' ? findTimelineTrack(timelines, id) : findTimelineClipInViews(timelines, id) ?? findTimelineMarker(timelines, id);
  if (!exists) selectionActions.select(null);
}

// 保存或外部修改后重新展开图（端口、新节点与连线以宿主读取为准）。
async function reloadDocument() {
  const skill = get().skill;
  if (!skill) return;
  await readDocument(skill);
  if (get().skill === skill) publishDocument();
}

export const authoringActions = {
  async listSkills() {
    try {
      set({ skills: await authoringApi.list(), available: true, unavailableReason: null });
    } catch (error) {
      set({ skills: [], available: false, unavailableReason: error instanceof ApiError && error.status === 404
        ? '宿主未启用作者接口（启动时需 --SkillAuthoring true）' : error instanceof Error ? error.message : String(error) });
    }
  },

  // 直接打开技能文件夹：读原文件与图展开，不需要运行；进入作者模式。
  async openSkill(skill: string) {
    if (get().skill === skill && get().documentGraphs) return;
    if (hasChanges()) {
      consoleActions.log('warn', '作者', `有未保存的修改，未切换到 ${skill}`);
      return;
    }
    loadController?.abort();
    const controller = loadController = new AbortController();
    clearHistory();
    useWorkbench.setState({ graphRequest: null, timelineRequest: null });
    set({ skill: null, revision: null, sourceRevision: null, files: {}, edited: {}, removed: [], addedPorts: {}, documentGraphs: null, documentError: null,
      loading: true, externalChange: false, lastSave: null });
    try {
      const snapshot = await authoringApi.read(skill, controller.signal);
      controller.signal.throwIfAborted();
      set({ skill: snapshot.skill, revision: snapshot.revision, files: Object.fromEntries(snapshot.files.map(file => [file.path, file.content])) });
      await readDocument(skill, controller.signal);
      controller.signal.throwIfAborted();
      set({ loading: false, author: true });
      publishDocument();
      reconcileSelection();
    } catch (error) {
      if (isAbort(error)) return;
      set({ loading: false });
      consoleActions.error('作者', error);
    }
  },

  async probe() {
    try {
      await authoringApi.list();
      set({ available: true, unavailableReason: null });
    } catch (error) {
      set({ available: false, unavailableReason: error instanceof ApiError && error.status === 404
        ? '宿主未启用作者接口（启动时需 --SkillAuthoring true）' : error instanceof Error ? error.message : String(error) });
    }
  },

  // 打开运行所属技能：技能文件夹按 skill.json 的 m_AbilityId 与程序身份对应。
  async open(programId: string) {
    if (get().programId === programId && get().skill) return;
    if (hasChanges()) {
      consoleActions.log('warn', '作者', `有未保存的修改，未切换到 ${programId}`);
      return;
    }
    loadController?.abort();
    const controller = loadController = new AbortController();
    clearHistory();
    set({ programId, skill: null, revision: null, files: {}, edited: {}, removed: [], loading: true, externalChange: false });
    try {
      const ability = abilityOf(programId);
      const folders = await authoringApi.list(controller.signal);
      controller.signal.throwIfAborted();
      const ordered = [...folders.filter(name => name.endsWith(ability)), ...folders.filter(name => !name.endsWith(ability))];
      for (const folder of ordered) {
        const snapshot = await authoringApi.read(folder, controller.signal);
        controller.signal.throwIfAborted();
        const root = snapshot.files.find(file => file.path === 'skill.json');
        const declared = root ? (parseSkillJson(root.content) as JsonObject).m_AbilityId : null;
        if (declared !== ability) continue;
        set({
          skill: snapshot.skill, revision: snapshot.revision, loading: false,
          files: Object.fromEntries(snapshot.files.map(file => [file.path, file.content])),
        });
        return;
      }
      set({ loading: false });
      consoleActions.log('warn', '作者', `没有找到 m_AbilityId 为 ${ability} 的技能文件夹`);
    } catch (error) {
      if (isAbort(error)) return;
      set({ loading: false });
      consoleActions.error('作者', error);
    }
  },

  setAuthor(author: boolean) {
    set({ author });
  },

  // 取可编辑的文档：首次编辑时从原文解析。同一轮事件里的多次编辑记为一步撤销。
  edit(path: string, mutate: (document: JsonValue) => void) {
    const state = get();
    const document = state.removed.includes(path) ? null
      : state.edited[path] ?? (state.files[path] !== undefined ? parseSkillJson(state.files[path]) : null);
    if (document === null) {
      consoleActions.log('error', '作者', `技能 ${state.skill ?? '—'} 中没有文件 ${path}`);
      return;
    }
    record();
    try {
      mutate(document);
    } catch (error) {
      consoleActions.error('作者', error);
      return;
    }
    set({ edited: { ...get().edited, [path]: document }, version: get().version + 1 });
  },

  // 新建技能文件（状态体、条件图、Timeline 片段图）；保存时一并写出。
  create(path: string, document: JsonValue) {
    record();
    const state = get();
    set({ edited: { ...state.edited, [path]: document }, removed: state.removed.filter(item => item !== path), version: state.version + 1 });
  },

  // 删除文件：网页新建的直接丢弃，原有的保存时删除。
  remove(paths: string[]) {
    if (!paths.length) return;
    record();
    const state = get();
    const edited = { ...state.edited };
    for (const path of paths) delete edited[path];
    set({
      edited, removed: [...new Set([...state.removed, ...paths.filter(path => state.files[path] !== undefined)])], version: state.version + 1,
    });
  },

  revert() {
    clearHistory();
    set({ edited: {}, removed: [], version: get().version + 1, externalChange: false, addedPorts: {} });
  },

  undo() { step(undoStack, redoStack); },
  redo() { step(redoStack, undoStack); },

  // 编辑过及新建的文件按原文格式写出（新文件取 skill.json 的格式）；与原文相同的不算修改。
  changedFiles(): { path: string; content: string }[] {
    const state = get();
    const fallback = shapeOf(state.files['skill.json'] ?? '\n');
    return Object.entries(state.edited).filter(([path]) => !state.removed.includes(path)).map(([path, document]) => ({
      path, content: writeSkillJson(document, state.files[path] !== undefined ? shapeOf(state.files[path]) : fallback),
    })).filter(file => file.content !== state.files[file.path]);
  },

  // 写回编辑过的文件；宿主保存后编译。被拒绝时文件也已写入，网页改用宿主回读的内容并列出诊断。
  async save() {
    const { skill, saving, removed } = get();
    const files = authoringActions.changedFiles();
    if (!skill || saving || (!files.length && !removed.length)) return;
    previewController?.abort();
    set({ saving: true });
    const accept = (content: SkillSnapshot, lastSave: SaveOutcome) => {
      if (get().skill !== skill) {
        set({ saving: false });
        return false;
      }
      clearHistory();
      set({
        revision: content.revision, sourceRevision: null, files: Object.fromEntries(content.files.map(file => [file.path, file.content])),
        edited: {}, removed: [], addedPorts: {}, externalChange: false, version: get().version + 1, saving: false, lastSave,
      });
      void reloadDocument();
      return true;
    };
    try {
      const selection = await selectedPreview();
      const result = await authoringApi.save(skill, {
        content: { files, removedFiles: removed }, preview: selection.kind === 'selected' ? selection.target : null,
      });
      if (result.compilationState === 'not-required') {
        const preview = result.preview;
        const stage = preview?.stage;
        const message = preview === null ? `已保存 ${result.inputs.length} 项输入方案，无需编译技能；${selection.kind === 'unavailable' ? selection.reason : '未请求重载'}`
          : `已保存 ${result.inputs.length} 项输入方案，无需编译技能；所选 Player 来源重载：${stage}`;
        const currentSkill = accept(result.content, { saved: true, compilationState: 'not-required', preview,
          ok: stage !== 'rejected' && stage !== 'not-applied', message, errors: [] });
        consoleActions.log(stage === 'rejected' || stage === 'not-applied' ? 'error' : 'info', '作者', message, JSON.stringify(result));
        if (currentSkill && stage === 'applied') {
          session.selectRun(null);
          await playerService.refresh();
        }
        if (currentSkill) await session.loadInputs();
        if (currentSkill && result.job !== null) session.selectRun(result.job.jobId);
        return;
      }
      const outcome = savedOutcome(result, result.preview, selection.kind === 'unavailable' ? selection.reason : '');
      const currentSkill = accept(result.content, outcome);
      consoleActions.log('info', '作者', `${skill} 已保存并编译：${result.compilation.sourceRevision}`);
      if (currentSkill && result.preview !== null) {
        consoleActions.log(outcome.ok ? 'info' : 'error', '预览', outcome.message, JSON.stringify(result.preview));
        const controller = previewController = new AbortController();
        void trackPreview({ save: result, receipt: result.preview, controller });
      }
    } catch (error) {
      if (error instanceof ApiError && error.status === 422) {
        const rejection = JSON.parse(error.raw) as SkillSaveRejection;
        const errors = rejection.diagnostic?.errors ?? [];
        const message = rejection.stage === 'source-rejected'
          ? `已保存，${rejection.compilationState === 'not-required' ? '输入方案' : '技能内容'}严格读取拒绝 ${errors.length || 1} 项：${rejection.diagnostic?.message ?? rejection.error ?? ''}`
          : `已保存，编译失败（结束代码 ${rejection.exitCode}）`;
        accept(rejection.content, {
          ok: false, message, errors, saved: rejection.saved, compilationState: rejection.compilationState, preview: null,
        });
        consoleActions.log('error', '作者', message, [...errors.map(item => `${item.file ?? ''} ${item.path ?? ''}：${item.message}`),
          rejection.error ?? ''].filter(Boolean).join('\n'));
        return;
      }
      set({ saving: false, lastSave: {
        ok: false, message: error instanceof Error ? error.message : String(error), errors: [],
        saved: null, compilationState: null, preview: null,
      } });
      consoleActions.error('作者', error);
    }
  },

  // 外部修改检测：无本地修改时直接重读，有修改时标出冲突。
  async checkExternal() {
    const { skill, revision } = get();
    if (!skill || !revision) return;
    try {
      const changes = await authoringApi.changes(skill, revision);
      if (!changes.changed) return;
      if (hasChanges()) set({ externalChange: true });
      else {
        const snapshot = await authoringApi.read(skill);
        set({ revision: snapshot.revision, sourceRevision: null, files: Object.fromEntries(snapshot.files.map(file => [file.path, file.content])) });
        await reloadDocument();
      }
    } catch (error) {
      consoleActions.error('作者', error);
    }
  },

  // ── Timeline ──

  setClipTime(documentId: string, clipId: string, startSeconds: number, endSeconds: number) {
    authoringActions.edit(timelinePath(documentId), document => {
      const clip = findTimelineClip(document as JsonObject, clipId);
      if (!clip) throw new Error(`Timeline ${documentId} 中没有片段 ${clipId}`);
      clip.m_StartTimeFPRawValue = fpRaw(startSeconds);
      clip.m_EndTimeFPRawValue = fpRaw(endSeconds);
    });
  },

  // 检查器字段：片段对象内按键路径写值。
  setClipValue(documentId: string, clipId: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(timelinePath(documentId), document => {
      const clip = findTimelineClip(document as JsonObject, clipId);
      if (!clip) throw new Error(`Timeline ${documentId} 中没有片段 ${clipId}`);
      setIn(clip, keys, value);
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

  // 片段平移与换轨：多选时整体移动同一位移（按帧步长取整后不小于 0）；换轨只移到同一 Timeline 的另一轨道。
  moveClips(documentId: string, clipIds: string[], deltaSeconds: number, targetTrackId: string | null) {
    const ids = new Set(clipIds);
    const delta = fpRaw(deltaSeconds);
    authoringActions.edit(timelinePath(documentId), document => {
      const tracks = tracksOf(document);
      const moving = tracks.flatMap(track => (track.m_Clips as JsonObject[]).filter(clip => ids.has(clip.m_AuthoringId as string))
        .map(clip => ({ track, clip })));
      const shift = Math.max(delta, -Math.min(...moving.map(item => numberOf(item.clip.m_StartTimeFPRawValue))));
      const target = targetTrackId === null ? null : tracks.find(track => track.m_AuthoringId === targetTrackId);
      if (targetTrackId !== null && !target) throw new Error(`Timeline ${documentId} 中没有轨道 ${targetTrackId}`);
      for (const { track, clip } of moving) {
        clip.m_StartTimeFPRawValue = numberOf(clip.m_StartTimeFPRawValue) + shift;
        clip.m_EndTimeFPRawValue = numberOf(clip.m_EndTimeFPRawValue) + shift;
        if (target && target !== track) {
          track.m_Clips = (track.m_Clips as JsonObject[]).filter(item => item !== clip);
          (target.m_Clips as JsonObject[]).push(clip);
        }
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
    authoringActions.edit(timelinePath(documentId), document => {
      const marker = tracksOf(document).flatMap(track => (track.m_Markers as JsonObject[] | undefined) ?? []).find(item => item.m_AuthoringId === markerId);
      if (!marker) throw new Error(`Timeline ${documentId} 中没有标记 ${markerId}`);
      marker.m_TimeFPRawValue = fpRaw(seconds);
    });
  },

  // 片段图种类（tree.clip）带专属 TimelineBody 图；标记带专属 TimelineTrigger 图。
  addClipAt(documentId: string, trackId: string, elements: SkillElements, template: ElementTemplate, startSeconds: number, endSeconds: number): string {
    const graph = 'm_AssetTree' in template.value ? createGraph(elements, 'TimelineBody', '片段内容') : null;
    return authoringActions.addClip(documentId, trackId, template.value, startSeconds, endSeconds, graph);
  },

  addMarkerAt(documentId: string, trackId: string, elements: SkillElements, seconds: number): string {
    return authoringActions.addMarker(documentId, trackId, elements.marker.value, seconds, createGraph(elements, 'TimelineTrigger', '标记触发'));
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

const tracksOf = (document: JsonValue) => (document as JsonObject).m_Tracks as JsonObject[];

let clipClipboard: JsonObject | null = null;

// 轨道的合同种类（ContractKind），取当前（可能未保存的）Timeline 文档。
export function trackContract(documentId: string, trackId: string): string | null {
  const document = currentDocument(timelinePath(documentId));
  return (document ? tracksOf(document).find(track => track.m_AuthoringId === trackId)?.ContractKind as string | undefined : undefined) ?? null;
}

// 新轨道的执行域：Unity 取放置目录里该轨道合同的默认域，.NET 侧没有目录，
// 按技能现有同种轨道用过的域给出；没有先例时列出枚举的全部域。由作者在添加时选择。
export function trackDomains(template: ElementTemplate): string[] {
  const used = new Set<string>();
  for (const path of livePaths().filter(item => item.startsWith('timelines/')))
    for (const track of tracksOf(currentDocument(path)!))
      if (track.ContractKind === template.kind && typeof track.m_ExecutionDomain === 'string') used.add(track.m_ExecutionDomain);
  const all = template.shape.members?.find(member => member.member === 'm_ExecutionDomain')?.enumValues ?? [];
  return used.size ? all.filter(domain => used.has(domain)) : all;
}

// 轨道可放的片段种类：Unity 的放置目录（TimelineContractCatalog）在 .NET 侧不可得，
// 按技能现有 Timeline 中同种轨道已放过的片段种类给出；没有先例的轨道列出全部种类，由保存时的正式编译判断。
export function clipTemplatesFor(elements: SkillElements, contract: string | null): ElementTemplate[] {
  const paired = new Set<string>();
  for (const path of livePaths().filter(item => item.startsWith('timelines/')))
    for (const track of tracksOf(currentDocument(path)!))
      if (track.ContractKind === contract)
        for (const clip of track.m_Clips as JsonObject[]) paired.add(clip.ContractKind as string);
  return paired.size ? elements.clips.filter(template => paired.has(template.kind ?? '')) : elements.clips;
}

export const hasChanges = () => Object.keys(get().edited).length + get().removed.length > 0;

// ── 撤销：每步前保存编辑副本、待删文件与新节点端口；编辑在文档上原地进行，快照取深拷贝 ──

interface Snapshot {
  edited: Record<string, JsonValue>;
  removed: string[];
  addedPorts: Record<string, AddedPort[]>;
}

const undoStack: Snapshot[] = [];
const redoStack: Snapshot[] = [];
const historyLimit = 100;
let recording = false;

const snapshot = (): Snapshot => {
  const state = get();
  return { edited: Object.fromEntries(Object.entries(state.edited).map(([path, document]) => [path, cloneJson(document)])),
    removed: [...state.removed], addedPorts: state.addedPorts };
};

function record() {
  if (recording) return;
  recording = true;
  queueMicrotask(() => { recording = false; });
  undoStack.push(snapshot());
  if (undoStack.length > historyLimit) undoStack.shift();
  redoStack.length = 0;
  set({ undoDepth: undoStack.length, redoDepth: 0 });
}

function step(from: Snapshot[], to: Snapshot[]) {
  const target = from.pop();
  if (!target) return;
  to.push(snapshot());
  set({ ...target, version: get().version + 1, undoDepth: undoStack.length, redoDepth: redoStack.length });
}

function clearHistory() {
  undoStack.length = 0;
  redoStack.length = 0;
  set({ undoDepth: 0, redoDepth: 0 });
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
    if (path === layoutPath || path === 'references.json' || (id && candidates.has(id))) continue;
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
function dropOrphans(seeds: string[]) {
  const orphans = orphanFiles(seeds);
  if (!orphans.length) return;
  const graphs = orphans.filter(path => path.startsWith('graphs/')).map(path => fileIdOf(path)!);
  if (graphs.length && currentDocument(layoutPath))
    authoringActions.edit(layoutPath, document => { for (const id of graphs) delete (document as JsonObject)[id]; });
  authoringActions.remove(orphans);
}

export type JsonKey = string | number;

// 按键路径写值；路径上缺的对象按需建出。
function setIn(target: JsonValue, keys: JsonKey[], value: JsonValue) {
  let current = target as Record<JsonKey, JsonValue>;
  for (const key of keys.slice(0, -1)) {
    current[key] ??= {};
    current = current[key] as Record<JsonKey, JsonValue>;
  }
  current[keys[keys.length - 1]] = value;
}

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
  const { edited, text, removed, version } = useAuthoring(useShallow(state => ({
    edited: path ? state.edited[path] : undefined, text: path ? state.files[path] : undefined,
    removed: !!path && state.removed.includes(path), version: state.version,
  })));
  return useMemo(() => ({ document: removed ? null : edited ?? (text !== undefined ? parseSkillJson(text) : null), version }),
    [edited, text, removed, version]);
}

// ── 节点图（流图）：节点在 graphs/<图>.json 的 allNodes，连线在源节点的 outConnections，位置在 layout.json ──

export const graphPath = (graphId: string) => `graphs/${graphId}.json`;
export const layoutPath = 'layout.json';

// 节点身份为 32 位十六进制，连线身份为带连字符的 UUID，与现有文件一致。
const nodeUid = () => crypto.randomUUID().replaceAll('-', '');
const connectionUid = () => crypto.randomUUID();

// 节点对象的键按序号顺序排列，与宿主存盘一致。
const sortedObject = (value: JsonObject): JsonObject =>
  Object.fromEntries(Object.keys(value).sort((left, right) => left < right ? -1 : left > right ? 1 : 0).map(key => [key, value[key]]));

const nodesOf = (document: JsonValue) => (document as JsonObject).allNodes as JsonObject[];
const connectionsOf = (node: JsonObject) => node.outConnections as JsonObject[];

function layoutNodes(document: JsonValue, graphId: string): JsonObject {
  const layout = document as JsonObject;
  const graph = (layout[graphId] ??= { allNodes: {}, canvasGroups: [], translation: { x: new JsonFloat(0), y: new JsonFloat(0) }, zoomFactor: new JsonFloat(1) }) as JsonObject;
  return graph.allNodes as JsonObject;
}

const positionValue = (position: { x: number; y: number }) =>
  ({ comments: null, position: { x: new JsonFloat(Math.round(position.x)), y: new JsonFloat(Math.round(position.y)) } });

export interface GroupRect { x: number; y: number; width: number; height: number }

const rectValue = (rect: GroupRect): JsonObject => ({
  height: new JsonFloat(Math.round(rect.height)), width: new JsonFloat(Math.round(rect.width)),
  x: new JsonFloat(Math.round(rect.x)), y: new JsonFloat(Math.round(rect.y)),
});

function groupsOf(document: JsonValue, graphId: string): JsonObject[] {
  layoutNodes(document, graphId);
  const graph = (document as JsonObject)[graphId] as JsonObject;
  return (graph.canvasGroups ??= []) as JsonObject[];
}

export interface CanvasGroup { index: number; name: string; rect: GroupRect; color: string | null }

// 布局中的分组；颜色全透明为 NodeCanvas 默认色。
export function graphGroups(layout: JsonValue | null, graphId: string): CanvasGroup[] {
  const groups = ((layout as JsonObject | null)?.[graphId] as JsonObject | undefined)?.canvasGroups as JsonObject[] | undefined;
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

// 技能黑板支持的原生变量类型（与 Unity BtsmtlSkillBlackboardValueContract 一致）及其默认值。
export const variableTypes = ['Boolean', 'Int32', 'UInt64', 'Single', 'String', 'Vector2', 'Vector3'];

function variableDefault(varType: string): JsonValue {
  switch (varType) {
    case 'Boolean': return false;
    case 'Int32': case 'UInt64': return 0;
    case 'Single': return new JsonFloat(0);
    case 'String': return '';
    case 'Vector2': return { x: new JsonFloat(0), y: new JsonFloat(0) };
    case 'Vector3': return { x: new JsonFloat(0), y: new JsonFloat(0), z: new JsonFloat(0) };
  }
  throw new Error(`技能黑板不支持 ${varType}`);
}

const variablesOf = (document: JsonValue) => (((document as JsonObject).localBlackboard as JsonObject).variables ?? {}) as JsonObject;
const setVariables = (document: JsonValue, variables: JsonObject) => {
  ((document as JsonObject).localBlackboard as JsonObject).variables = sortedObject(variables);
};
const declarationsOf = (document: JsonValue) => (document as JsonObject).BlackboardDeclarations as JsonObject[];

// ── 预览输入方案（inputs/<方案>.json）：每个方案带一条输入 Timeline ──

export const inputAuthoring = {
  setValue(path: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(path, document => setIn(document, keys, value));
  },

  // 新方案复制所选方案的设置，配一条新的空输入 Timeline。
  addScheme(path: string, name: string) {
    const timeline = crypto.randomUUID();
    authoringActions.create(timelinePath(timeline), {
      m_AuthoringId: timeline, m_ExternalBindings: [], m_Loop: false, m_Name: 'Timeline', m_Tracks: [],
    });
    authoringActions.edit(path, document => {
      const plan = document as JsonObject;
      const schemes = plan.Schemes as JsonObject[];
      const source = schemes[numberOf(plan.Selected)] ?? schemes[0];
      schemes.push(sortedObject({ ...cloneJson(source), InputTimeline: timeline, Name: name }));
      plan.Selected = schemes.length - 1;
    });
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

// 新建元素用的锚点与默认值：每个技能读一次。
let elementCache: { skill: string; items: Promise<SkillElements> } | null = null;
export function skillElements(): Promise<SkillElements> {
  const skill = get().skill;
  if (!skill) return Promise.reject(new Error('没有打开的技能'));
  if (elementCache?.skill !== skill) {
    const items = authoringApi.elements(skill);
    elementCache = { skill, items };
    items.catch(() => { elementCache = null; });
  }
  return elementCache.items;
}

export function useSkillElements(enabled: boolean): SkillElements | null {
  const skill = useAuthoring(state => state.skill);
  const [elements, setElements] = useState<{ skill: string; items: SkillElements } | null>(null);
  useEffect(() => {
    if (!enabled || !skill) return;
    let live = true;
    skillElements().then(items => { if (live) setElements({ skill, items }); }, (error: unknown) => consoleActions.error('作者', error));
    return () => { live = false; };
  }, [enabled, skill]);
  return enabled && elements?.skill === skill ? elements.items : null;
}

// 新建一张专属图（状态体、转移条件、Timeline 片段图）：带角色的系统锚点，锚点按默认坐标写入布局。
export function createGraph(elements: SkillElements, role: string, name: string): string {
  const id = nodeUid();
  const anchors = elements.anchors[role] ?? [];
  authoringActions.create(graphPath(id), sortedObject({
    AuthoringId: id, BlackboardDeclarations: [], Role: role, localBlackboard: { variables: {} }, name,
    allNodes: anchors.map(anchor => ({ UID: anchor.uid, outConnections: [], type: anchor.type })),
  }));
  if (currentDocument(layoutPath))
    authoringActions.edit(layoutPath, document => {
      const nodes = layoutNodes(document, id);
      // 锚点坐标按技能 JSON 规则解析，可能是 1.0 形式的浮点对象。
      for (const anchor of anchors) nodes[anchor.uid] = positionValue({ x: numberOf(anchor.x), y: numberOf(anchor.y) });
    });
  return id;
}

// 复制缓冲：节点原对象、相对位置与端口，跨图粘贴时生成新身份。
let clipboard: { nodes: JsonObject[]; positions: Record<string, { x: number; y: number }>; ports: Record<string, AddedPort[]> } | null = null;

export const graphAuthoring = {
  // 按类型描述新建节点：配置取描述的默认配置，端口取描述结果。
  addNode(graphId: string, type: string, configuration: JsonObject, ports: AddedPort[], position: { x: number; y: number }): string {
    const uid = nodeUid();
    authoringActions.edit(graphPath(graphId), document => {
      nodesOf(document).push(sortedObject({ ...cloneJson(configuration), UID: uid, outConnections: [], type }));
    });
    authoringActions.edit(layoutPath, document => { layoutNodes(document, graphId)[uid] = positionValue(position); });
    set({ addedPorts: { ...get().addedPorts, [uid]: ports } });
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
    authoringActions.edit(layoutPath, document => {
      const nodes = layoutNodes(document, graphId);
      for (const id of nodeIds) delete nodes[id];
    });
    dropOrphans(seeds);
  },

  // ── 状态机（照 Unity 原生 Skill FSM）：新状态带专属状态体；转移按序号排在源状态之后，条件图按需创建 ──

  addState(graphId: string, elements: SkillElements, configuration: JsonObject, position: { x: number; y: number }): string {
    const uid = nodeUid();
    const body = createGraph(elements, 'StateBody', '状态内容');
    authoringActions.edit(graphPath(graphId), document => {
      nodesOf(document).push(sortedObject({
        ...cloneJson(configuration), UID: uid, _name: '新状态', m_Body: body, outConnections: [], type: 'BtsmtlSkillNativeState',
      }));
    });
    authoringActions.edit(layoutPath, document => { layoutNodes(document, graphId)[uid] = positionValue(position); });
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
  createStateBody(graphId: string, elements: SkillElements, nodeId: string) {
    const body = createGraph(elements, 'StateBody', '状态内容');
    graphAuthoring.setNodeValue(graphId, nodeId, ['m_Body'], body);
  },

  createCondition(graphId: string, elements: SkillElements, connectionId: string) {
    const condition = createGraph(elements, 'ConditionRule', '转移条件');
    graphAuthoring.setTransitionValue(graphId, connectionId, ['m_Condition'], condition);
  },

  removeStateBody(graphId: string, nodeId: string, body: string) {
    graphAuthoring.setNodeValue(graphId, nodeId, ['m_Body'], null);
    dropOrphans([body]);
  },

  removeCondition(graphId: string, connectionId: string, condition: string) {
    graphAuthoring.setTransitionValue(graphId, connectionId, ['m_Condition'], null);
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
  setNodeValue(graphId: string, nodeId: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(graphPath(graphId), document => {
      const node = nodesOf(document).find(item => item.UID === nodeId);
      if (!node) throw new Error(`图 ${graphId} 中没有节点 ${nodeId}`);
      setIn(node, keys, value);
    });
  },

  // ── 分组（NodeCanvas CanvasGroup，存于布局）：名称、矩形、颜色、自动分组与备注 ──

  addGroup(graphId: string, rect: GroupRect, name: string) {
    authoringActions.edit(layoutPath, document => {
      groupsOf(document, graphId).push({
        autoGroup: false, color: { a: new JsonFloat(0), b: new JsonFloat(0), g: new JsonFloat(0), r: new JsonFloat(0) },
        name, notes: null, rect: rectValue(rect),
      });
    });
  },

  updateGroup(graphId: string, index: number, patch: { name?: string; rect?: GroupRect }) {
    authoringActions.edit(layoutPath, document => {
      const group = groupsOf(document, graphId)[index];
      if (!group) throw new Error(`图 ${graphId} 中没有第 ${index} 个分组`);
      if (patch.name !== undefined) group.name = patch.name;
      if (patch.rect) group.rect = rectValue(patch.rect);
    });
  },

  deleteGroups(graphId: string, indices: number[]) {
    const removed = new Set(indices);
    authoringActions.edit(layoutPath, document => {
      const graph = (document as JsonObject)[graphId] as JsonObject;
      graph.canvasGroups = groupsOf(document, graphId).filter((_, index) => !removed.has(index));
    });
  },

  // ── 黑板（照 Unity 技能黑板声明）：原生变量在 localBlackboard.variables（按名称索引），
  // BTSMTL 作用域声明在 BlackboardDeclarations，两者以变量身份一一对应 ──

  addVariable(graphId: string, name: string, varType: string, declaration: { m_Scope: string; m_Lifetime: string; m_Category: string }): string {
    const id = crypto.randomUUID();
    authoringActions.edit(graphPath(graphId), document => {
      const variables = variablesOf(document);
      if (variables[name]) throw new Error(`黑板已有变量 ${name}`);
      setVariables(document, { ...variables, [name]: { ID: id, name, serializedValue: variableDefault(varType), varType } });
      declarationsOf(document).push(sortedObject({
        ...declaration, m_InputBinding: { m_InputValueId: null, m_Projection: 'Direct' }, m_VariableId: id,
      }));
    });
    return id;
  },

  renameVariable(graphId: string, variableId: string, name: string) {
    authoringActions.edit(graphPath(graphId), document => {
      const variables = variablesOf(document);
      const [key, variable] = Object.entries(variables).find(([, item]) => (item as JsonObject).ID === variableId) ?? [];
      if (!variable) throw new Error(`黑板没有变量 ${variableId}`);
      if (name !== key && variables[name]) throw new Error(`黑板已有变量 ${name}`);
      const next = { ...variables };
      delete next[key!];
      setVariables(document, { ...next, [name]: { ...(variable as JsonObject), name } });
    });
  },

  setVariableValue(graphId: string, variableId: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(graphPath(graphId), document => {
      const variable = Object.values(variablesOf(document)).find(item => (item as JsonObject).ID === variableId);
      if (!variable) throw new Error(`黑板没有变量 ${variableId}`);
      setIn(variable, ['serializedValue', ...keys], value);
    });
  },

  setDeclarationValue(graphId: string, variableId: string, keys: JsonKey[], value: JsonValue) {
    authoringActions.edit(graphPath(graphId), document => {
      const declaration = declarationsOf(document).find(item => item.m_VariableId === variableId);
      if (!declaration) throw new Error(`黑板没有变量 ${variableId} 的声明`);
      setIn(declaration, keys, value);
    });
  },

  removeVariable(graphId: string, variableId: string) {
    authoringActions.edit(graphPath(graphId), document => {
      setVariables(document, Object.fromEntries(Object.entries(variablesOf(document)).filter(([, item]) => (item as JsonObject).ID !== variableId)));
      (document as JsonObject).BlackboardDeclarations = declarationsOf(document).filter(item => item.m_VariableId !== variableId);
    });
  },

  move(graphId: string, positions: Record<string, { x: number; y: number }>) {
    authoringActions.edit(layoutPath, document => {
      const nodes = layoutNodes(document, graphId);
      for (const [id, position] of Object.entries(positions)) nodes[id] = { ...(nodes[id] as JsonObject | undefined), ...positionValue(position) };
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
    authoringActions.edit(layoutPath, document => {
      const layout = layoutNodes(document, graphId);
      for (const [from, to] of ids) {
        const position = source.positions[from] ?? { x: 0, y: 0 };
        layout[to] = positionValue({ x: position.x + offset.x, y: position.y + offset.y });
      }
    });
    const ports = { ...get().addedPorts };
    for (const [from, to] of ids) ports[to] = source.ports[from] ?? [];
    set({ addedPorts: ports });
    return [...ids.values()];
  },
};

// 运行的图叠加未保存修改：节点与连线以编辑后的图文件为准，新节点的端口取 addedPorts，位置取编辑后的布局。
// 只处理当前显示的图实例（invocationPath）。
export function applyGraphEdits(graph: GraphDefinition, path: string, edited: Record<string, JsonValue>,
  addedPorts: Record<string, AddedPort[]>): GraphDefinition {
  const document = edited[graphPath(graph.id)] as JsonObject | undefined;
  const layout = (edited[layoutPath] as JsonObject | undefined)?.[graph.id] as JsonObject | undefined;
  if (!document && !layout) return graph;
  const positions = (layout?.allNodes ?? {}) as Record<string, JsonObject>;
  const positionOf = (id: string, fallback: GraphNode['position']) => {
    const position = positions[id]?.position as JsonObject | undefined;
    return position ? { x: numberOf(position.x), y: numberOf(position.y) } : fallback;
  };
  const sourceNodes = document ? nodesOf(document) : null;
  const sources = new Map((sourceNodes ?? []).map(node => [node.UID as string, node]));
  const kept = sourceNodes ? new Set(sources.keys()) : null;
  const known = new Set(graph.nodes.filter(node => node.invocationPath === path).map(node => node.id));
  const nodes: GraphNode[] = graph.nodes.filter(node => node.invocationPath !== path || !kept || kept.has(node.id))
    .map(node => node.invocationPath === path ? {
      ...node, position: positionOf(node.id, node.position),
      displayName: (sources.get(node.id)?._name as string | undefined) || node.displayName,
    } : node);
  const ports: GraphPort[] = graph.ports.filter(port => port.invocationPath !== path || !kept || kept.has(port.nodeId));
  for (const node of sourceNodes ?? []) {
    const id = node.UID as string;
    if (known.has(id)) continue;
    nodes.push({ id, displayName: (node._name as string | undefined) ?? (node.type as string), invocationPath: path, position: positionOf(id, { x: 0, y: 0 }) });
    for (const port of addedPorts[id] ?? [])
      ports.push({ nodeId: id, id: port.id, displayName: port.displayName, direction: port.direction, isTrigger: port.isTrigger, invocationPath: path });
  }
  let edges = graph.edges;
  if (sourceNodes) {
    const triggers = new Set(ports.filter(port => port.isTrigger).map(port => `${port.nodeId}/${port.direction}/${port.id}`));
    const connections = sourceNodes.flatMap(node => connectionsOf(node).map(connection => ({ node, connection })));
    const ids = new Set(connections.map(item => item.connection.UID as string));
    const existing = new Set(graph.edges.filter(edge => edge.invocationPath === path).map(edge => edge.id));
    edges = [
      ...graph.edges.filter(edge => edge.invocationPath !== path || ids.has(edge.id)),
      // 状态机转移没有端口，带 Payload。
      ...connections.filter(item => !existing.has(item.connection.UID as string)).map(({ node, connection }) => ({
        id: connection.UID as string,
        kind: connection.Payload ? 'Transition' : triggers.has(`${node.UID}/output/${connection.sourcePortID}`) ? 'Child' : 'Value',
        invocationPath: path, available: true, reason: '',
        source: { graphId: graph.id, nodeId: node.UID as string, portId: (connection.sourcePortID as string | undefined) ?? '' },
        target: { graphId: graph.id, nodeId: connection.targetNode as string, portId: (connection.targetPortID as string | undefined) ?? '' },
      })),
    ];
  }
  return { ...graph, nodes, ports, edges };
}

// 检查器按选中身份（角色/调用路径/元素）找图：所在图有未保存修改时先叠加，新建的状态与转移也能检查。
export function findEditedGraph(graphs: GraphDefinition[] | null, selectId: string, edited: Record<string, JsonValue>,
  addedPorts: Record<string, AddedPort[]>): { graph: GraphDefinition; path: string; id: string } | null {
  for (const graph of graphs ?? [])
    for (const path of new Set(graph.nodes.map(node => node.invocationPath))) {
      const prefix = `${graph.actorId}/${path}/`;
      const id = selectId.slice(prefix.length);
      if (!selectId.startsWith(prefix) || id.includes('/')) continue;
      return { graph: applyGraphEdits(graph, path, edited, addedPorts), path, id };
    }
  return null;
}

function findTimelineClip(timeline: JsonObject, clipId: string): JsonObject | null {
  for (const track of (timeline.m_Tracks as JsonObject[] | undefined) ?? [])
    for (const clip of (track.m_Clips as JsonObject[] | undefined) ?? [])
      if (clip.m_AuthoringId === clipId) return clip;
  return null;
}

// 作者 Timeline 叠加未保存修改：轨道、片段与标记取编辑后的文档，宿主给出的片段显示名按身份保留。
export function applyTimelineEdits(timelines: AuthorTimeline[], edited: Record<string, JsonValue>): AuthorTimeline[] {
  if (!Object.keys(edited).length) return timelines;
  return timelines.map(timeline => {
    const document = edited[timelinePath(timeline.documentId)] as JsonObject | undefined;
    if (!document) return timeline;
    const names = new Map(timeline.tracks.flatMap(track => track.clips.map(clip => [clip.id, clip.displayName])));
    const tracks = timelineOf(timeline.documentId, document, null).tracks;
    return {
      ...timeline,
      contentRevision: null,
      tracks: tracks.map(track => ({ ...track, clips: track.clips.map(clip => ({ ...clip, displayName: names.get(clip.id) ?? null })) })),
    };
  });
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
