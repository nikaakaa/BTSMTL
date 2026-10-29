import { create } from 'zustand';
import { ApiError, isAbort } from '../api/http';
import type {
  Alignment, AuthorTimeline, Availability, Frame, Geometry, GraphDefinition, HostDescription, ImageSourceAvailability,
  InputCatalog, InputTimeline, LiveCommandStage, PlaybackState, PlayerInputChoice, RouteFrame, RunReceipt, Sources,
  Summary, ViewerLive,
} from '../api/types';

// ── connection ──
export interface ConnectionSlice {
  hostAvailable: boolean | null;
  host: HostDescription | null;
  images: ImageSourceAvailability['description'];
  connected: boolean;
  lastError: string | null;
}

// ── content ──
export type RunSourceKind = 'replay' | 'local';
export interface ContentSlice {
  sources: Sources | null;
  scenePath: string | null;
  sourceKind: RunSourceKind;
  replayId: string | null;
  inputCatalog: InputCatalog | null;
  inputChoices: PlayerInputChoice[];
  interactive: boolean;
}

// ── run ──
export type RouteFrameSpan = Pick<RouteFrame, 'hostFrame' | 'seconds' | 'playbacks'>;

export interface RunData {
  // 帧序号对应的宿主帧、秒数与 Timeline 播放状态。
  routeFrames: RouteFrameSpan[];
  routeProtocol: string | null;
  routeAvailability: Availability[];
  graphs: GraphDefinition[] | null;
  graphAvailability: Availability[];
  inputTimeline: InputTimeline | null;
  timelines: AuthorTimeline[];
  metadataAvailability: Availability[];
  geometry: Geometry | null;
  // 游标所在帧的完整记录；读取中保留上一帧。
  frame: Frame | null;
  frameKey: string | null;
  // 运行失败或未启动时的原因与控制台记录。
  failure: { reason: string; entryId: number } | null;
}

export interface RunSlice extends RunData {
  runs: RunReceipt[];
  runId: string | null;
  receipt: RunReceipt | null;
  summary: Summary | null;
  sealed: boolean;
  // 运行中 .NET 会话的宿主播放状态；已封存运行为 null。
  hostPlayback: PlaybackState | null;
  // 已封存运行在本页回看播放。
  reviewing: boolean;
  mode: string;
  generation: number;
}

export const emptyRunData: RunData = {
  routeFrames: [], routeProtocol: null, routeAvailability: [], graphs: null, graphAvailability: [], inputTimeline: null,
  timelines: [], metadataAvailability: [], geometry: null, frame: null, frameKey: null, failure: null,
};

// ── cursor：只能由 transport 写入 ──
export interface CursorSlice {
  frameIndex: number;
  hostFrame: string | null;
  seconds: number | null;
  followLive: boolean;
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
  liveId: string | null;
  live: ViewerLive | null;
  viewerOpen: boolean;
  alignment: Alignment | null;
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
}
export interface SelectionSlice {
  selection: Selection | null;
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
}

// ── layout：布局本身由 dockview 持有 ──
export interface LayoutSlice {
  openPanels: string[];
  activePanel: string | null;
}

export type WorkbenchState = ConnectionSlice & ContentSlice & RunSlice & CursorSlice & PlayerSlice &
  ResourceSlice & SelectionSlice & ConsoleSlice & LayoutSlice;

export const consoleLimit = 500;

export const initialCursor: CursorSlice = { frameIndex: 0, hostFrame: null, seconds: null, followLive: false };

export const initialPlayerTarget = {
  targetFrame: null, actualFrame: null, pendingSeek: null, lastCommand: null,
} satisfies Partial<PlayerSlice>;

export const useWorkbench = create<WorkbenchState>()(() => ({
  hostAvailable: null, host: null, images: null, connected: false, lastError: null,

  sources: null, scenePath: null, sourceKind: 'replay', replayId: null, inputCatalog: null, inputChoices: [],
  interactive: false,

  runs: [], runId: null, receipt: null, summary: null, sealed: false, hostPlayback: null, reviewing: false,
  mode: 'real', generation: 0, ...emptyRunData,

  ...initialCursor,

  liveId: null, live: null, viewerOpen: false, alignment: null, ...initialPlayerTarget, capturing: false,

  identity: null, time: 0, playing: false,

  selection: null,

  entries: [], unread: 0, nextEntryId: 1,

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
    set({ sources });
  },
  // 场景只决定新建运行的内容，不打断当前运行；输入声明按场景路径丢弃旧结果。
  selectScene(scenePath: string | null) {
    if (getState().scenePath === scenePath) return;
    set({ scenePath, inputCatalog: null, inputChoices: [] });
  },
  setSourceKind(sourceKind: RunSourceKind) {
    set({ sourceKind });
  },
  selectReplay(replayId: string | null) {
    set({ replayId });
  },
  setInputCatalog(scenePath: string, inputCatalog: InputCatalog) {
    if (getState().scenePath === scenePath) set({ inputCatalog });
  },
  setInputChoices(inputChoices: PlayerInputChoice[]) {
    set({ inputChoices });
  },
  setInteractive(interactive: boolean) {
    set({ interactive });
  },
};

export const runActions = {
  setRuns(runs: RunReceipt[]) {
    set({ runs });
  },
  // 切换运行：递增代次，清空依赖旧运行的状态；cursor 由 transport.reset 归零。
  selectRun(runId: string | null): number {
    const generation = bumpGeneration();
    set({
      runId, receipt: null, summary: null, sealed: false, hostPlayback: null, reviewing: false,
      selection: null, ...initialCursor, ...emptyRunData,
    });
    return generation;
  },
  setReceipt(generation: number, receipt: RunReceipt) {
    if (isCurrent(generation)) set({ receipt });
  },
  setSummary(generation: number, summary: Summary, sealed: boolean) {
    if (isCurrent(generation)) set({ summary, sealed });
  },
  setHostPlayback(generation: number, hostPlayback: PlaybackState | null) {
    if (isCurrent(generation)) set({ hostPlayback });
  },
  setData(generation: number, data: Partial<RunData>) {
    if (isCurrent(generation)) set(data);
  },
  setMode(mode: string) {
    set({ mode });
  },
};

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
    const current = getState().selection;
    if (current?.kind === selection?.kind && current?.id === selection?.id) return;
    set({ selection });
  },
};

export const consoleActions = {
  log(level: ConsoleLevel, source: string, reason: string, raw?: string): number {
    const state = getState();
    const entry: ConsoleEntry = { id: state.nextEntryId, time: Date.now(), level, source, reason, raw };
    const entries = state.entries.length >= consoleLimit
      ? [...state.entries.slice(state.entries.length - consoleLimit + 1), entry]
      : [...state.entries, entry];
    set({ entries, nextEntryId: entry.id + 1, unread: state.unread + 1 });
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
  clear() {
    set({ entries: [], unread: 0 });
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
