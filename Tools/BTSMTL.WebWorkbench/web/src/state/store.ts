import { create } from 'zustand';
import { ApiError, isAbort } from '../api/http';
import type {
  ArchiveLogicRecord, ArchivePoseLayout, ArchivePresentationRecord, AuthorTimeline, Availability, ExecutionCall, Frame, FrozenInputTimeline, Geometry, GraphDefinition, HostDescription, ImageSourceAvailability,
  InputCatalog, InputTimeline, LiveCommandStage, LivePlayer, PlayerBuild, PlayerInputChoice, RouteFrame,
  Sources, ViewerLive,
} from '../api/types';
import type {
  PlayerArchive, PlayerArchiveImage, PlayerFrameImage, PlayerCameraPage, PlayerInputPage, PlayerJobId,
  PlayerJobReceipt, PlayerSummary, PlayerUnavailablePage,
} from '../api/playerRecords';

// ── connection ──
export interface ConnectionSlice {
  hostAvailable: boolean | null;
  host: HostDescription | null;
  images: ImageSourceAvailability['description'];
  connected: boolean;
  lastError: string | null;
}

// ── content ──
export interface ContentSlice {
  sources: Sources | null;
  scenePath: string | null;
  inputSource: { kind: 'recorded'; id: string } | { kind: 'local' } | null;
  localLastSdkFrame: number | null;
  inputCatalog: InputCatalog | null;
  inputChoices: PlayerInputChoice[];
}

// ── run ──
export interface RunData {
  // 帧序号对应的宿主帧、秒数与 Timeline 播放状态。
  routeFrames: RouteFrame[];
  routeProtocol: string | null;
  routeAvailability: Availability[];
  graphs: GraphDefinition[] | null;
  graphAvailability: Availability[];
  inputTimeline: InputTimeline | null;
  // Timeline 输入组与工具栏共同显示的已采用输入方案（inputRootKey）。
  inputRoot: string | null;
  timelines: AuthorTimeline[];
  metadataAvailability: Availability[];
  geometry: Geometry | null;
  // 游标所在帧的完整记录；读取中保留上一帧。
  frame: Frame | null;
  frameKey: string | null;
  // 运行失败或未启动时的原因与控制台记录。
  failure: { reason: string; entryId: number } | null;
  // 播放器档案：骨架布局与游标所在 SDK 帧的逻辑、骨骼记录；读取中保留上一帧。
  archiveLayout: ArchivePoseLayout | null;
  archiveFrame: ArchiveFrame | null;
  archiveMetadata: PlayerArchive | null;
  archiveImage: ArchiveImage | null;
}

export interface ArchiveFrame {
  hostFrame: string;
  logic: ArchiveLogicRecord | null;
  presentations: ArchivePresentationRecord[];
  cameras: PlayerCameraPage['page']['items'];
  input: PlayerInputPage['page']['items'][number] | null;
  unavailable: PlayerUnavailablePage['page']['items'];
  availability: Availability[];
}

interface ArchiveImageObservation {
  presentation: ArchivePresentationRecord | null;
  camera: PlayerCameraPage['page']['items'][number] | null;
  overlayReason: string | null;
}

export type ArchiveImage = (
  | { kind: 'capture'; metadata: PlayerArchiveImage; image: Blob }
  | { kind: 'frame'; metadata: PlayerFrameImage; image: Blob }
) & ArchiveImageObservation | { kind: 'unavailable'; hostFrame: string; reason: string };

export interface RunSlice extends RunData {
  archiveJobs: PlayerJobReceipt[];
  runId: PlayerJobId | null;
  receipt: PlayerJobReceipt | null;
  summary: PlayerSummary | null;
  sealed: boolean;
  // 已封存运行在本页回看播放。
  reviewing: boolean;
  generation: number;
}

export const emptyRunData: RunData = {
  routeFrames: [], routeProtocol: null, routeAvailability: [], graphs: null, graphAvailability: [], inputTimeline: null, inputRoot: null,
  timelines: [], metadataAvailability: [], geometry: null, frame: null, frameKey: null, failure: null,
  archiveLayout: null, archiveFrame: null, archiveMetadata: null, archiveImage: null,
};

// ── cursor：只能由 transport 写入 ──
export interface CursorSlice {
  frameIndex: number;
  hostFrame: string | null;
  seconds: number | null;
}

// ── player ──
export interface PlayerCommandRecord {
  type: string;
  commandId: string | null;
  stage: LiveCommandStage;
  hostFrame: string | null;
  error: string | null;
}
export interface PlayerSlice {
  playerBuilds: PlayerBuild[] | null;
  lives: LivePlayer[];
  liveId: string | null;
  live: ViewerLive | null;
  viewerOpen: boolean;
  playerViewport: { width: number; height: number } | null;
  targetFrame: number | null;
  actualFrame: string | null;
  pendingSeek: { commandId: string; frame: number } | null;
  lastCommand: PlayerCommandRecord | null;
  // 画面面板正在接管键鼠时，运行快捷键不响应。
  capturing: boolean;
}

// ── resource：素材秒数，与运行游标无关 ──
export interface ResourceSlice {
  identity: string | null;
  time: number;
  playing: boolean;
}

// ── selection ──
export type SelectionKind = 'node' | 'edge' | 'clip' | 'track' | 'actor';
export interface Selection {
  kind: SelectionKind;
  // 稳定身份，地址 select 参数原样使用。
  id: string;
  call?: ExecutionCall;
}
export interface SelectionSlice {
  selection: Selection | null;
  // 其它面板请求节点图面板显示的图（graphKey）；sequence 让同一张图可重复请求。
  graphRequest: { key: string; sequence: number } | null;
  // 其它面板请求 Timeline 面板进入的单个 Timeline（documentId）。
  timelineRequest: { documentId: string; sequence: number } | null;
}

// ── console ──
export type ConsoleLevel = 'error' | 'warn' | 'info';
export interface ConsoleEntry {
  id: number;
  time: number;
  level: ConsoleLevel;
  source: string;
  reason: string;
  raw?: string;
}
export interface ConsoleSlice {
  entries: ConsoleEntry[];
  unread: number;
  nextEntryId: number;
  // 其它面板的一行错误提示点击后要定位的记录。
  focusedEntry: number | null;
}

// ── layout：布局本身由 dockview 持有 ──
export interface LayoutSlice {
  openPanels: string[];
  activePanel: string | null;
}

export type WorkbenchState = ConnectionSlice & ContentSlice & RunSlice & CursorSlice & PlayerSlice &
  ResourceSlice & SelectionSlice & ConsoleSlice & LayoutSlice;

export const consoleLimit = 500;

export const initialCursor: CursorSlice = { frameIndex: 0, hostFrame: null, seconds: null };

export const initialPlayerTarget = {
  targetFrame: null, actualFrame: null, pendingSeek: null, lastCommand: null,
} satisfies Partial<PlayerSlice>;

export const useWorkbench = create<WorkbenchState>()(() => ({
  hostAvailable: null, host: null, images: null, connected: false, lastError: null,

  sources: null, scenePath: null, inputSource: null, localLastSdkFrame: null, inputCatalog: null, inputChoices: [],

  archiveJobs: [], runId: null, receipt: null, summary: null, sealed: false, reviewing: false,
  generation: 0, ...emptyRunData,

  ...initialCursor,

  playerBuilds: null, lives: [], liveId: null, live: null, viewerOpen: false, playerViewport: null, ...initialPlayerTarget, capturing: false,

  identity: null, time: 0, playing: false,

  selection: null, graphRequest: null, timelineRequest: null,

  entries: [], unread: 0, nextEntryId: 1, focusedEntry: null,

  openPanels: [], activePanel: null,
}));

export const getState = useWorkbench.getState;
const set = useWorkbench.setState;

// ── 请求代次：切换运行或场景时递增，旧结果丢弃 ──

export function currentGeneration(): number {
  return getState().generation;
}

export function isCurrent(generation: number): boolean {
  return getState().generation === generation;
}

export function bumpGeneration(): number {
  const generation = getState().generation + 1;
  set({ generation });
  return generation;
}

// ── 各分片的写入入口（cursor 除外，见 transport） ──

export const connectionActions = {
  setHost(available: boolean, host: HostDescription | null) {
    set({ hostAvailable: available, host, connected: true, lastError: null });
  },
  setImages(images: ImageSourceAvailability['description']) {
    set({ images });
  },
  setConnected(connected: boolean, reason: string | null = null) {
    if (getState().connected === connected && getState().lastError === reason) return;
    set({ connected, lastError: reason });
  },
};

export const contentActions = {
  setSources(sources: Sources) {
    const state = getState();
    const before = state.sources?.scenes.find(scene => scene.path === state.scenePath)?.revision;
    const after = sources.scenes.find(scene => scene.path === state.scenePath)?.revision;
    set({ sources, ...(before === after ? {} : { inputCatalog: null, inputChoices: [] }) });
  },
  // 场景只决定新建运行的内容，不打断当前运行；输入声明按场景路径丢弃旧结果。
  selectScene(scenePath: string | null) {
    if (getState().scenePath === scenePath) return;
    set({ scenePath, inputCatalog: null, inputChoices: [] });
  },
  selectInputSource(inputSource: ContentSlice['inputSource']) {
    set({ inputSource });
  },
  setLocalLastSdkFrame(localLastSdkFrame: number | null) {
    set({ localLastSdkFrame });
  },
  setInputCatalog(scenePath: string, inputCatalog: InputCatalog) {
    const state = getState();
    const scene = state.sources?.scenes.find(item => item.path === scenePath);
    if (state.scenePath !== scenePath || scene?.revision !== inputCatalog.revision) return;
    const inputChoices = state.inputChoices.flatMap(choice => {
      const plan = inputCatalog.players.find(player => player.actorId === choice.actorId)?.localPlans
        .find(item => item.path === choice.inputPlanRoot && item.schemes.some(scheme => scheme.index === choice.schemeIndex));
      return plan ? [{ ...choice, inputPlanRevision: plan.revision }] : [];
    });
    set({ inputCatalog, inputChoices });
  },
  selectInputPlan(choice: PlayerInputChoice) {
    set({ inputChoices: [...getState().inputChoices.filter(item => item.actorId !== choice.actorId), choice] });
  },
};

export const runActions = {
  setArchiveJobs(archiveJobs: PlayerJobReceipt[]) {
    set({ archiveJobs });
  },
  // 切换运行：递增代次，清空依赖旧运行的状态；cursor 由 transport.reset 归零。
  selectRun(runId: PlayerJobId | null): number {
    const generation = getState().generation + 1;
    set({
      generation, runId, receipt: null, summary: null, sealed: false, reviewing: false,
      selection: null, ...initialCursor, ...emptyRunData,
    });
    return generation;
  },
  setReceipt(generation: number, receipt: PlayerJobReceipt) {
    if (isCurrent(generation)) set({ receipt, runId: receipt.jobId });
  },
  setSummary(generation: number, summary: PlayerSummary, sealed: boolean) {
    if (isCurrent(generation)) set({ summary, sealed });
  },
  setData(generation: number, data: Partial<RunData>) {
    if (isCurrent(generation)) set(data);
  },
  selectInputRoot(inputRoot: string) {
    set({ inputRoot });
  },
};

export const inputRootKey = (root: FrozenInputTimeline) =>
  JSON.stringify([root.actorId, root.programId, root.programRevision, root.planId, root.planRevision, root.schemeIndex]);

export const resourceActions = {
  select(identity: string | null) {
    set({ identity, time: 0, playing: false });
  },
  setTime(time: number) {
    set({ time });
  },
  setPlaying(playing: boolean) {
    set({ playing });
  },
};

export const selectionActions = {
  select(selection: Selection | null) {
    set({ selection });
  },
  showGraph(key: string) {
    set({ graphRequest: { key, sequence: (getState().graphRequest?.sequence ?? 0) + 1 } });
  },
  showTimeline(documentId: string) {
    set({ timelineRequest: { documentId, sequence: (getState().timelineRequest?.sequence ?? 0) + 1 } });
  },
};

export const consoleActions = {
  log(level: ConsoleLevel, source: string, reason: string, raw?: string): number {
    const state = getState();
    const entry: ConsoleEntry = { id: state.nextEntryId, time: Date.now(), level, source, reason, raw };
    const entries = state.entries.length >= consoleLimit
      ? [...state.entries.slice(state.entries.length - consoleLimit + 1), entry]
      : [...state.entries, entry];
    set({ entries, nextEntryId: entry.id + 1, unread: state.unread + (level === 'info' ? 0 : 1) });
    return entry.id;
  },
  // 请求失败统一入口：取消不记录；宿主错误带状态码与原始回执。
  error(source: string, error: unknown): number | null {
    if (isAbort(error)) return null;
    if (error instanceof ApiError)
      return consoleActions.log('error', source, error.status ? `${error.status} · ${error.reason}` : error.reason, error.raw);
    return consoleActions.log('error', source, error instanceof Error ? error.message : String(error));
  },
  markRead() {
    if (getState().unread) set({ unread: 0 });
  },
  focus(entryId: number | null) {
    set({ focusedEntry: entryId });
  },
  clear() {
    set({ entries: [], unread: 0, focusedEntry: null });
  },
};

export const layoutActions = {
  setOpenPanels(openPanels: string[]) {
    set({ openPanels });
  },
  setActivePanel(activePanel: string | null) {
    set({ activePanel });
  },
};
