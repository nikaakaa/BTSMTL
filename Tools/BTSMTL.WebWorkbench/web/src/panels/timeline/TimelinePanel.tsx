import { PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import {
  TimelineContext, useItem, useRow, useTimelineContext, useTimelineMonitor, type DragItemData, type Range,
  type ResizeEndEvent, type ResizeItemData, type ResizeMoveEvent, type UsePanStrategy,
} from 'dnd-timeline';
import {
  createContext, Fragment, memo, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type MouseEvent as ReactMouseEvent,
  type KeyboardEvent, type PointerEvent, type ReactNode, type RefObject,
} from 'react';
import { create } from 'zustand';
import { useShallow } from 'zustand/react/shallow';
import type { ClipCurveChannel, SkillElements } from '../../api/authoring';
import type { ExecutionCall, FrozenInputTimeline, InputWrite, RouteFrame } from '../../api/types';
import type { PlayerDataCurve, PlayerJobId } from '../../api/playerRecords';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered, useFrameReport } from '../../shell/readiness';
import { openPanel } from '../../shell/layout';
import {
  applyTimelineEdits, authoringActions, clipCurves, clipMoveIssue, clipReferenceSource, clipSnapSource, clipTemplatesFor, currentDocument, defaultChannelCurve,
  graphPath, inputAuthoring, markerTimeTaken, requestReferenceCurves, trackHasCurves, trackOverlap, useAuthoring, useAuthorTimelines, useInputContext, useInputSchemes,
  useReferenceCurves, useSkillElements, useDocumentObservationBinding, type ReferenceEntry,
} from '../../state/authoring';
import {
  consoleActions, getState, inputRootKey, runActions, selectedScene, selectionActions, useWorkbench, type WorkbenchState,
} from '../../state/store';
import {
  clipGraph, clipMix, clipSelectId, clipUnions, documentSegments, inputClipSelectId, overlapIssue, trackSelectId, inputTrackSelectId, routeSegments, timelineLength, segmentRows, segmentTitle, selectedInputRoot,
  trackColors, type ClipInstance, type GroupRow, type RouteSegment, type Span, type TrackKind, type TrackRow,
} from '../../state/timelineModel';
import { callKey, callLabel, graphKey, graphName, matchesDocumentCall, parentCallMatches, parentGraphCalls, recordedGraphCalls, type RecordedGraphCall } from '../../state/graphModel';
import { transport } from '../../state/transport';
import { scheduleZeroFrame, useInputObservation } from '../../state/inputModel';
import { useReceivedInput, type ReceivedInput, type ReceivedKey, type ReceivedSpan } from '../../state/inputReceipts';
import { archive } from '../../state/archive';
import { useAnimationFrameRates } from '../../state/resources';
import { curveWithKey, curveWithoutKey, evaluateCurve } from '../../state/curveSplit';
import {
  breakKeys, isBroken, keyTime, keyValue, moveKeys, removeKeys, setTangent, setTangentKind, smoothKeys, tangentKind, tangentOf,
  type TangentKind, type TangentSide,
} from '../../state/curveEdit';
import { numberOf, type JsonObject } from '../../state/skillJson';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, ContextMenu, MenuButton, NumberInput, Select, TextInput, type MenuEntry } from '../../ui/controls';
import { AddIcon, CrumbArrow, CurveIcon, FoldoutIcon, MuteIcon, OptionsIcon, SearchIcon, SelectorIcon, SettingsIcon } from '../../ui/icons';
import { KeyIcon, MarkerTriangle, PlayheadShape, TimelineAssetIcon, TrackIcon } from './icons';
import styles from './timeline.module.css';

// Timeline 面板（D5）：照 Unity Timeline 窗口（com.unity.timeline 1.7.7 Dark 皮肤），技能里的 Timeline 按组列出，每组可折叠。
// dnd-timeline 管时间与像素换算、缩放、平移；刻度、片段、输入标记、游标自绘。
// 游标是独立覆盖线，订阅 cursor 后直接改 transform，片段不随游标重绘。

// 轨道头宽度，约占窗口三分之一，面板窄时收窄让出时间区；CSS 经 --sidebar 读取。
const sidebarFor = (width: number) => Math.round(Math.max(150, Math.min(380, width * 0.3)));
const minSpan = 0.05;
// 照 Unity Timeline：轨道与分组默认高 30，行间隔 3；数据曲线行自定 64。
const trackHeight = 30;
const curveHeight = 64;
// 轨道展开片段曲线时片段下方的参数区（照 BtsmtlTimelineTrackBinding.GetFinalHeight 与 TrackEditorGUI）：
// 参数行高 18、间距 2、顶部留 5；查看某个参数时加控制行与完整曲线，可拖动底边加高。
const curveParamTop = 5;
const curveParamPitch = 20;
const curveParamHeight = 18;
const curveListHeight = (count: number) => Math.max(50, curveParamTop * 2 + count * curveParamPitch);
const curveInspectHeight = (count: number) => curveParamTop * 2 + count * curveParamPitch + 30 + 120;
const curveMaxHeight = 600;
const rowSpacing = 3;
// 行列表顶部内边距（.rows 的 padding-top）。
const rowsPadding = 3;
const overscan = 120;
// 片段名左右各留 6，剩余宽度不超过 10 时不画名字（ClipDrawer）。
const clipLabelPadding = 6;
const clipLabelMinWidth = 10;

interface InputGroupRow { kind: 'inputGroup'; id: string }
interface InputTrackRow { kind: 'inputTrack'; id: string; selectId: string; track: FrozenInputTimeline['tracks'][number] }
interface CurveGroupRow { kind: 'curves'; id: string }
interface CurveRow { kind: 'curve'; id: string; curve: PlayerDataCurve }
interface ReceivedGroupRow { kind: 'receivedGroup'; id: string; received: ReceivedInput }
interface ReceivedRow { kind: 'received'; id: string; name: string; spans: ReceivedSpan[]; requests: ReceivedSpan[]; missing: boolean }
type Row = GroupRow | TrackRow | InputGroupRow | InputTrackRow | CurveGroupRow | CurveRow | ReceivedGroupRow | ReceivedRow;

interface Params {
  range: Range | null;
  collapsed: string[];
  // 展开片段曲线的轨道行；正在查看完整曲线的参数（同一时间一个，照 Slate inspectedParameterId）；查看时拖动加高的参数区高度。
  curveTracks: string[];
  curveParam: { track: string; parameter: string } | null;
  curveHeights: Record<string, number>;
  follow: boolean;
  filter: string;
  // 顶栏所选技能（programId）；null 为路线上第一个技能。
  program: string | null;
  // 单个 Timeline 的 documentId；作者内容用 * 显示全部，运行路线用 null 显示总览。
  focus: string | null;
  // 进入单个 Timeline 前的路线视图范围，返回时恢复。
  routeRange: Range | null;
  // 编辑吸附（照 Timeline Editor 时间网格）：逻辑 tick、秒间隔或关闭。
  snap: Snap;
  call: string | null;
  pin: { runId: PlayerJobId; documentId: string; observation: TimelineObservation } | null;
  caller: { runId: PlayerJobId; call: ExecutionCall } | null;
}
type Snap = 'tick' | 'frame' | '0.1' | '0.01' | '0.001' | 'none';
const defaults: Params = {
  range: null, collapsed: [], curveTracks: [], curveParam: null, curveHeights: {}, follow: true, filter: '', program: null, focus: null, routeRange: null, snap: 'frame', call: null, pin: null, caller: null,
};

// 标记与游标的吸附步长（秒）；素材帧只属于片段，标记按逻辑 tick；关闭吸附时为定点时间的最小单位。没有逻辑 tick 时不能编辑。
const snapStep = (snap: Snap | undefined, frameStep: number | null) =>
  snap === 'none' ? 1 / 65536 : snap && snap !== 'tick' && snap !== 'frame' ? Number(snap) : frameStep;
const SnapContext = createContext<number | null>(null);
const minimumStep = 1 / 65536;

// 片段吸附网格（照 TimelineEditorSessionContext.SnapTime）：网格点为 offset + k·step，null 为不吸附。
// 起止按逻辑 tick 量化的片段（TickQuantized）始终按 tick；其余片段按所选网格，素材帧取片段所引用动画资源的帧率，
// 偏移为起点减 ClipIn，使网格对齐素材帧；没有素材帧的片段在素材帧模式下不吸附。
interface Grid { step: number; offset: number }
type ClipGridOf = (documentId: string, clipId: string) => Grid | null;
const ClipGridContext = createContext<ClipGridOf>(() => null);
const snapGrid = (seconds: number, grid: Grid | null) =>
  grid ? grid.offset + Math.round((seconds - grid.offset) / grid.step) * grid.step : seconds;

function useClipGrid(snap: Snap, tick: number | null): ClipGridOf {
  const enabled = useAuthoring(state => state.author && state.target !== null);
  const elements = useSkillElements(enabled);
  const rates = useAnimationFrameRates(enabled && snap === 'frame');
  return useCallback((documentId: string, clipId: string): Grid | null => {
    const source = elements && clipSnapSource(elements, documentId, clipId);
    if (!source) return null;
    const tickGrid = tick === null ? null : { step: tick, offset: 0 };
    if (source.template.tickQuantized || snap === 'tick') return tickGrid;
    if (snap === 'none') return null;
    if (snap !== 'frame') return { step: Number(snap), offset: 0 };
    const rate = source.template.clipInable ? source.references.map(reference => rates?.get(reference)).find(value => value) : undefined;
    return rate ? { step: 1 / rate, offset: source.start - source.clipIn } : null;
  }, [elements, rates, snap, tick]);
}
type TimelineObservation = { call: ExecutionCall; domain: string };
const observationKey = (observation: TimelineObservation) => JSON.stringify([callKey(observation.call), observation.domain]);
type PreviewClock = { kind: 'archive' } | { kind: 'document'; documentId: string | null; observation: TimelineObservation | null }
  | { kind: 'input'; documentId: string; observing: boolean };
const PreviewClockContext = createContext<PreviewClock>({ kind: 'document', documentId: null, observation: null });
const CurveClockContext = createContext<{ sourceSeconds: number; seconds: number } | null>(null);
function documentPlayback(state: WorkbenchState, clock: PreviewClock) {
  if (clock.kind !== 'document' || clock.observation === null) return undefined;
  const { documentId, observation } = clock;
  return state.frame?.route?.playbacks.find(playback => playback.documentId === documentId &&
    playback.domain === observation.domain && playback.call !== null && callKey(playback.call) === callKey(observation.call));
}
// 技能文档没有运行观察时的编辑游标（Timeline 本地秒）：照 Timeline Editor 可拖动定位，换 Timeline 回到 0。
const useAuthorCursor = create<{ documentId: string | null; seconds: number }>(() => ({ documentId: null, seconds: 0 }));
const authorCursorOf = (cursor: { documentId: string | null; seconds: number }, clock: PreviewClock) =>
  ((clock.kind === 'document' && clock.observation === null) || (clock.kind === 'input' && !clock.observing)) && clock.documentId !== null
    ? cursor.documentId === clock.documentId ? cursor.seconds : 0 : null;
const previewSeconds = (state: WorkbenchState, clock: PreviewClock, author: number | null): number | null => clock.kind === 'archive'
  ? state.seconds : clock.kind === 'input'
    ? author ?? (state.hostFrame !== null && state.archiveMetadata !== null
      ? Number(BigInt(state.hostFrame) - BigInt(scheduleZeroFrame(state.archiveMetadata))) / state.archiveMetadata.sdkTickRate : null)
    : author ?? documentPlayback(state, clock)?.cursorSeconds ?? null;
// 游标秒数：运行档案、运行观察或编辑游标。
function usePreviewSelect<T>(clock: PreviewClock, pick: (seconds: number | null, state: WorkbenchState) => T): T {
  const author = useAuthorCursor(cursor => authorCursorOf(cursor, clock));
  return useWorkbench(state => pick(previewSeconds(state, clock, author), state));
}
const selectTimelineObject = (clock: PreviewClock, kind: 'clip' | 'track', id: string) =>
  selectionActions.select({ kind, id, call: clock.kind === 'document' ? clock.observation?.call : undefined });
const snapTo = (seconds: number, step: number) => Math.round(seconds / step) * step;

// 一次 Timeline 播放：在显示的段里取游标所在的那次，否则第一次。
function playbackAt(segments: RouteSegment[], documentId: string, frameIndex: number) {
  const plays = segments.filter(segment => segment.documentId === documentId);
  return plays.find(segment => segment.firstIndex <= frameIndex && frameIndex <= segment.lastIndex)
    ?? (plays.length === 1 ? plays[0] : null);
}

const segmentRange = (segment: RouteSegment) => fitRange({ start: segment.origin, end: segment.origin + segment.length });

const rowHeight = (row: Row) =>
  (row.kind === 'curve' ? curveHeight : row.kind === 'track' && row.curves ? trackHeight + row.curves : trackHeight) + rowSpacing;

// 曲线参数区的查看状态、所查看曲线上选中的关键帧序号与游标跳转（轨道行是 memo 组件，经上下文取）。
// seek 不按吸附取整，跳到关键帧本身。
interface CurveView {
  inspected: Params['curveParam'];
  inspect(track: string, parameter: string | null): void;
  resize(track: string, height: number): void;
  seek(value: number): void;
  keys: number[];
  selectKeys(keys: number[]): void;
}
const CurveViewContext = createContext<CurveView>({
  inspected: null, inspect: () => {}, resize: () => {}, seek: () => {}, keys: [], selectKeys: () => {},
});

// 分组之后的行都属于最近的分组：照 Unity 分组轨道再缩进一级，由分组底色包住，最后一行收圆角。
// 分组行本身按有无子行区分只画上圆角还是整块圆角。
type Nest = 'head' | 'single' | 'child' | 'last';
const groupKinds: ReadonlySet<Row['kind']> = new Set(['group', 'inputGroup', 'curves', 'receivedGroup']);
function rowNesting(rows: Row[]): (Nest | undefined)[] {
  let grouped = false;
  return rows.map((row, index) => {
    const next = rows[index + 1];
    const nextIsChild = next !== undefined && !groupKinds.has(next.kind);
    if (groupKinds.has(row.kind)) {
      grouped = true;
      return nextIsChild ? 'head' : 'single';
    }
    return grouped ? nextIsChild ? 'child' : 'last' : undefined;
  });
}

export function TimelinePanel() {
  return <DocumentTimeline />;
}

// 技能在顶栏选择；打开后固定看技能里的一个 Timeline（默认第一个），也可切到全部。
function DocumentTimeline() {
  const { target, loading, available, reason, error } = useAuthoring(useShallow(state => ({
    target: state.target, loading: state.loading, available: state.available,
    reason: state.unavailableReason, error: state.documentError,
  })));
  const count = useAuthorTimelines().length;
  if (available === false) return <ReportEmpty text={reason ?? '作者接口不可用'} />;
  if (loading) return <Empty text="正在读取内容" />;
  if (!target || !count) return <ReportEmpty text={error ?? (target ? '此内容没有 Timeline' : '在顶栏选择技能')} />;
  return <TimelineWindow frames={null} />;
}

function ReportEmpty({ text }: { text: string }) {
  useFrameReport(text);
  return <Empty text={text} />;
}

// 下拉中的“全部 Timeline”；focus 为 null 时看第一个 Timeline。
const allTimelines = '*';

function fitRange(bounds: Range): Range {
  const pad = Math.max(minSpan, (bounds.end - bounds.start) * 0.03);
  return { start: bounds.start - pad, end: bounds.end + pad };
}

// frames 为 null 时显示打开的技能文档：范围为所看 Timeline 的长度，按编译帧率取整。
function TimelineWindow({ frames }: { frames: RouteFrame[] | null }) {
  const [params, setParams] = usePanelParams<Params>(defaults);
  const schemes = useInputSchemes();
  const authorTimelines = useAuthorTimelines();
  const focusId = params.focus === allTimelines ? null
    : authorTimelines.some(item => item.documentId === params.focus) ? params.focus : authorTimelines[0]?.documentId ?? null;
  const documentLength = useMemo(() => {
    if (frames) return 0;
    const selected = authorTimelines.find(item => item.documentId === focusId);
    return Math.max(0, ...(selected ? [selected] : authorTimelines).map(timeline =>
      Math.max(timelineLength(timeline), schemes.find(scheme => scheme.documentId === timeline.documentId)?.rangeEndSeconds ?? 0)));
  }, [frames, authorTimelines, focusId, schemes]);
  const tickRate = useAuthoring(state => state.tickRate);
  const scenario = useAuthoring(state => state.target?.kind === 'scenario');
  const inputRate = useWorkbench(state => {
    const scheme = schemes.find(item => item.documentId === focusId);
    return scheme !== undefined && scheme.scene === selectedScene(state)?.path && state.inputCatalog?.players.some(player => player.actorId === scheme.actorId)
      ? state.inputCatalog.tickRate : null;
  });
  const sdkTickRate = useWorkbench(state => state.archiveMetadata?.sdkTickRate ?? null);
  const rate = frames ? sdkTickRate : scenario ? inputRate : tickRate;
  const frameStep = rate === null ? null : 1 / rate;
  const step = snapStep(params.snap, frameStep);
  const clipGrid = useClipGrid(params.snap ?? defaults.snap, frameStep);
  const bounds = useMemo(() => frames ? { start: frames[0].seconds, end: frames[frames.length - 1].seconds }
    : { start: 0, end: Math.max(minSpan, documentLength) }, [frames, documentLength]);
  const range = params.range && params.range.end > bounds.start && params.range.start < bounds.end ? params.range : fitRange(bounds);
  const rangeRef = useRef(range);
  rangeRef.current = range;

  const onRangeChanged = useCallback((update: (previous: Range) => Range) => {
    const next = update(rangeRef.current);
    const limit = Math.max(minSpan * 4, (bounds.end - bounds.start) * 3);
    let span = Math.min(limit, Math.max(minSpan, next.end - next.start));
    let start = next.end - next.start === span ? next.start : (next.start + next.end - span) / 2;
    start = Math.min(bounds.end - span * 0.1, Math.max(bounds.start - span * 0.9, start));
    if (!Number.isFinite(span)) span = bounds.end - bounds.start;
    setParams({ range: { start, end: start + span } });
  }, [bounds, setParams]);

  // 按下后移动超过 4 像素才算拖动，单击仍选中片段、双击打开节点图。
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  // 面板宽度决定轨道头宽度。
  const panelWindow = usePanelWindow();
  const [width, setWidth] = useState(1200);
  const frame = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const element = frame.current;
    if (!element) return;
    const Observer = (panelWindow as Window & { ResizeObserver: typeof ResizeObserver }).ResizeObserver;
    const observer = new Observer(() => setWidth(element.clientWidth));
    observer.observe(element);
    return () => observer.disconnect();
  }, [panelWindow]);

  return (
    <div ref={frame} className={styles.frame}>
      <TimelineContext range={range} onRangeChanged={onRangeChanged} onResizeEnd={() => {}} sensors={sensors}
        sidebarWidth={sidebarFor(width)} usePanStrategy={panStrategy}>
        <SnapContext.Provider value={step}>
          <ClipGridContext.Provider value={clipGrid}>
            <TimelineBody params={params} setParams={setParams} bounds={bounds} rate={rate} frames={frames} onRangeChanged={onRangeChanged} />
          </ClipGridContext.Provider>
        </SnapContext.Provider>
      </TimelineContext>
    </div>
  );
}

// 拖动中的片段（照 MoveItemHandler）：原位置隐藏，Rows 在目标行按吸附后的时间画虚影，不合规时虚影变红、松手放回原处；
// 拖片段两端不合规时片段原地变红、松手恢复原长。tip 为虚影上方的提示：不合规或指到不能放的轨道时写明原因，否则为起点时刻。
interface DropGhost { box: string; row: string; start: number; end: number; label: string; kind: TrackKind }
const useClipEditing = create<{
  drag: { panelId: string; ghosts: DropGhost[]; invalid: boolean; tip: string } | null;
  resize: { panelId: string; box: string } | null;
}>(() => ({ drag: null, resize: null }));

interface DragSession {
  edit: ClipEdit;
  // 一起移动的片段与原轨道，第一个是拖动的片段；框选多个时各自留在原轨道。
  moving: { clip: ClipInstance; track: string }[];
  multi: boolean;
  target: string;
  activator: { x: number; y: number };
  // 拖动中的指针位置；尚未移动时按拖动位移推算。
  pointer: { x: number; y: number } | null;
  release(): void;
}

// 作者模式拖动、拖端写回片段时间：原起止加上拖动距离换成的秒数，按吸附步长取整，再减去本段零点得到 Timeline 本地秒数。
// 单个片段可拖到同一 Timeline 的另一条轨道（照 TimelineData.MoveClip：轨道能放这种片段且执行域相同），
// 指到不能放的轨道时留在上一条能放的轨道；框选的多个片段整体平移、不换轨。
// 放下后的同轨重叠按轨道合同判断：树轨不许重叠，动画与位移曲线轨只许两两部分重叠形成混合。
function ClipEditing({ rows, offsets, scroller }: {
  rows: Row[]; offsets: number[]; scroller: RefObject<HTMLDivElement | null>;
}) {
  const panelId = usePanelId();
  const panelWindow = usePanelWindow();
  const { range, timelineRef, sidebarWidth: sidebar, valueToPixels, getValueFromScreenX } = useTimelineContext();
  const elements = useSkillElements(useAuthoring(state => state.author && state.target !== null));
  const drag = useClipEditing(state => state.drag?.panelId === panelId ? state.drag : null);
  const clipGrid = useContext(ClipGridContext);
  const session = useRef<DragSession | null>(null);
  // 像素位移换成秒数：可见范围除以时间区宽度。
  const pixelsToValue = (pixels: number) => {
    const width = (timelineRef.current?.clientWidth ?? 0) - sidebar;
    return width > 0 ? pixels * (range.end - range.start) / width : 0;
  };
  const trackRow = (segmentId: string, trackId: string) =>
    rows.find((row): row is TrackRow => row.kind === 'track' && row.segment.id === segmentId && row.track.id === trackId);
  // 指针所在的行：按各行累计高度查找，与该行是否已渲染无关。
  const rowAt = (clientY: number) => {
    const element = scroller.current;
    if (!element) return undefined;
    const y = clientY - element.getBoundingClientRect().top + element.scrollTop - rowsPadding;
    const index = rows.findIndex((_, i) => y >= offsets[i] && y < offsets[i + 1]);
    return index < 0 ? undefined : rows[index];
  };
  // 一条轨道上的片段（本地秒数）放下后的重叠是否合规。
  const placementIssue = (documentId: string, track: TrackRow, spans: Span[]) => {
    if (!elements) return '正在读取轨道合同';
    const policy = trackOverlap(elements, documentId, track.track.id);
    return policy === null ? '轨道种类不在元素查询里' : overlapIssue(policy, spans);
  };
  const preview = (current: DragSession, delta: { x: number; y: number }) => {
    const { edit } = current;
    const pointer = current.pointer ?? { x: current.activator.x + delta.x, y: current.activator.y + delta.y };
    const anchor = current.moving[0].clip.clip;
    const moved = getValueFromScreenX(pointer.x) - getValueFromScreenX(current.activator.x);
    const shift = Math.max(snapGrid(anchor.startSeconds + moved, clipGrid(edit.documentId, anchor.id)) - anchor.startSeconds,
      -Math.min(...current.moving.map(item => item.clip.clip.startSeconds)));
    let hover: string | null = null;
    if (!current.multi) {
      const row = rowAt(pointer.y);
      if (row?.kind === 'track' && row.segment.id === edit.segmentId && row.track.id !== current.target) {
        const reason = row.track.id === edit.trackId ? null
          : elements ? clipMoveIssue(elements, edit.documentId, anchor.contract, edit.trackId, row.track.id) : '正在读取轨道合同';
        if (reason === null) current.target = row.track.id;
        else hover = reason;
      }
    }
    const trackOf = (item: DragSession['moving'][number]) => current.multi ? item.track : current.target;
    const ids = new Set(current.moving.map(item => item.clip.clip.id));
    let issue: string | null = null;
    for (const trackId of new Set(current.moving.map(trackOf))) {
      const track = trackRow(edit.segmentId, trackId);
      issue = !track ? '目标轨道不在当前视图' : placementIssue(edit.documentId, track, [
        ...track.track.clips.filter(clip => !ids.has(clip.id)).map(clip => ({ start: clip.startSeconds, end: clip.endSeconds })),
        ...current.moving.filter(item => trackOf(item) === trackId)
          .map(item => ({ start: item.clip.clip.startSeconds + shift, end: item.clip.clip.endSeconds + shift })),
      ]);
      if (issue) break;
    }
    const ghosts = current.moving.map((item): DropGhost => ({
      box: item.clip.key, row: `track:${edit.segmentId}/${trackOf(item)}`, start: item.clip.start + shift, end: item.clip.end + shift,
      label: item.clip.label, kind: item.clip.kind,
    }));
    return { shift, issue, ghosts, tip: issue ?? hover ?? `${(anchor.startSeconds + shift).toFixed(3)} s` };
  };
  const publish = ({ ghosts, issue, tip }: ReturnType<typeof preview>) =>
    useClipEditing.setState({ drag: { panelId, ghosts, invalid: issue !== null, tip } });
  // 拖端：起点不早于 0、不晚于终点前一步，终点不早于起点后一步；按片段网格吸附。
  // 可设 ClipIn 的片段裁起点时 ClipIn 随起点移动同样秒数（不小于 0），画面内容留在原处。
  const resized = (event: ResizeMoveEvent | ResizeEndEvent) => {
    const data = event.active.data.current as ResizeItemData & { edit?: ClipEdit };
    const edit = data.edit;
    const track = edit && trackRow(edit.segmentId, edit.trackId);
    if (!edit || !track) return null;
    const grid = clipGrid(edit.documentId, edit.clipId);
    const unit = grid?.step ?? minimumStep;
    const delta = pixelsToValue(event.delta.x);
    const originalStart = data.span.start - edit.origin;
    const originalEnd = data.span.end - edit.origin;
    const start = event.direction === 'start' ? Math.max(0, Math.min(originalEnd - unit, snapGrid(originalStart + delta, grid))) : originalStart;
    const end = event.direction === 'end' ? Math.max(start + unit, snapGrid(originalEnd + delta, grid)) : originalEnd;
    const issue = placementIssue(edit.documentId, track, track.track.clips.map(clip =>
      clip.id === edit.clipId ? { start, end } : { start: clip.startSeconds, end: clip.endSeconds }));
    const source = elements && start !== originalStart ? clipSnapSource(elements, edit.documentId, edit.clipId) : null;
    const clipIn = source?.template.clipInable ? Math.max(0, source.clipIn + start - originalStart) : undefined;
    return { edit, box: String(event.active.id), start, end, clipIn, issue, changed: delta !== 0 };
  };
  useTimelineMonitor({
    onDragStart(event) {
      const edit = (event.active.data.current as (DragItemData & { edit?: ClipEdit }) | undefined)?.edit;
      const own = edit && trackRow(edit.segmentId, edit.trackId)?.clips.find(clip => clip.clip.id === edit.clipId);
      if (!edit || !own) return;
      const boxed = new Set((getState().timelineSelections[panelId] ?? []).map(item => item.box));
      const multi = boxed.size > 1 && boxed.has(own.key);
      const others = multi ? rows.flatMap(row => row.kind === 'track' && row.segment.id === edit.segmentId
        ? row.clips.filter(clip => clip !== own && boxed.has(clip.key)).map(clip => ({ clip, track: row.track.id })) : []) : [];
      const activator = event.activatorEvent as globalThis.PointerEvent;
      const current: DragSession = {
        edit, moving: [{ clip: own, track: edit.trackId }, ...others], multi, target: edit.trackId,
        activator: { x: activator.clientX, y: activator.clientY }, pointer: null, release: () => {},
      };
      // 捕获阶段记录指针：先于拖动库的处理，且不受行列表自动滚动影响。
      const follow = (move: globalThis.PointerEvent) => { current.pointer = { x: move.clientX, y: move.clientY }; };
      panelWindow.addEventListener('pointermove', follow, true);
      current.release = () => {
        panelWindow.removeEventListener('pointermove', follow, true);
        if (session.current === current) session.current = null;
        useClipEditing.setState({ drag: null });
      };
      session.current = current;
      publish(preview(current, { x: 0, y: 0 }));
    },
    onDragMove(event) {
      if (session.current) publish(preview(session.current, event.delta));
    },
    onDragCancel() {
      session.current?.release();
    },
    onDragEnd(event) {
      const current = session.current;
      if (!current) return;
      const result = preview(current, event.delta);
      current.release();
      if (result.issue) {
        consoleActions.log('warn', 'Timeline', `${result.issue}，片段放回原处`);
        return;
      }
      // 平移只按拖动片段的起点吸附，保持时长。
      const retarget = !current.multi && current.target !== current.edit.trackId;
      if (result.shift || retarget)
        authoringActions.moveClips(current.edit.documentId, current.moving.map(item => item.clip.clip.id), result.shift, retarget ? current.target : null);
    },
    onResizeMove(event: ResizeMoveEvent) {
      const result = resized(event);
      const invalid = result?.issue ? { panelId, box: result.box } : null;
      const previous = useClipEditing.getState().resize;
      if (previous?.box !== invalid?.box || previous?.panelId !== invalid?.panelId) useClipEditing.setState({ resize: invalid });
    },
    onResizeEnd(event: ResizeEndEvent) {
      const result = resized(event);
      useClipEditing.setState({ resize: null });
      if (!result?.changed) return;
      if (result.issue) consoleActions.log('warn', 'Timeline', `${result.issue}，片段恢复原长`);
      else authoringActions.setClipTime(result.edit.documentId, result.edit.clipId, result.start, result.end, result.clipIn);
    },
  });
  if (!drag) return null;
  const rowIndex = new Map(rows.map((row, index) => [row.id, index]));
  return (
    <div className={styles.dropLayer} style={{ height: offsets[rows.length] }}>
      {drag.ghosts.map((ghost, index) => {
        const row = rowIndex.get(ghost.row);
        if (row === undefined) return null;
        const top = offsets[row] + rowSpacing;
        const left = valueToPixels(ghost.start - range.start);
        const width = valueToPixels(ghost.end - ghost.start);
        return (
          <Fragment key={ghost.box}>
            <div className={styles.dropGhost} data-invalid={drag.invalid || undefined}
              style={{ top, left, width: Math.max(1, width), ['--tc' as string]: trackColors[ghost.kind] }}>
              {width - clipLabelPadding * 2 > clipLabelMinWidth && <span className={styles.clipLabel}>{ghost.label}</span>}
            </div>
            {index === 0 && (
              <span className={styles.dropTip} data-invalid={drag.invalid || undefined}
                style={{ left: Math.max(0, left), top: top >= 18 ? top - 18 : top + trackHeight + 2 }}>{drag.tip}</span>
            )}
          </Fragment>
        );
      })}
    </div>
  );
}

// 平移：Ctrl＋滚轮以鼠标为中心缩放；普通滚轮在时间区横移，在轨道列表上纵向滚动；
// 照 Slate 中键拖动：左右平移时间，上下滚动轨道列表。
const panStrategy: UsePanStrategy = (bag, onPanEnd) => {
  useLayoutEffect(() => {
    const element = bag.timelineRef.current;
    if (!element) return;
    const overLanes = (event: MouseEvent) => event.clientX - element.getBoundingClientRect().left > bag.sidebarWidth;
    const wheel = (event: WheelEvent) => {
      if (event.ctrlKey || event.metaKey) {
        event.preventDefault();
        onPanEnd({ clientX: event.clientX, clientY: event.clientY, deltaX: 0, deltaY: event.deltaY });
      } else if (overLanes(event)) {
        event.preventDefault();
        onPanEnd({ deltaX: event.deltaX || event.deltaY, deltaY: 0 });
      }
    };
    let drag: { x: number; y: number; rows: HTMLElement | null } | null = null;
    // 阻止浏览器的中键自动滚动。
    const mouseDown = (event: MouseEvent) => { if (event.button === 1) event.preventDefault(); };
    const down = (event: globalThis.PointerEvent) => {
      if (event.button !== 1) return;
      event.preventDefault();
      drag = { x: event.clientX, y: event.clientY, rows: element.querySelector<HTMLElement>(`.${styles.rows}`) };
      element.dataset.panning = '';
      element.setPointerCapture(event.pointerId);
    };
    const move = (event: globalThis.PointerEvent) => {
      if (drag === null) return;
      onPanEnd({ deltaX: drag.x - event.clientX, deltaY: 0 });
      if (drag.rows) drag.rows.scrollTop += drag.y - event.clientY;
      drag.x = event.clientX;
      drag.y = event.clientY;
    };
    const up = (event: globalThis.PointerEvent) => {
      if (drag === null) return;
      drag = null;
      delete element.dataset.panning;
      if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId);
    };
    element.addEventListener('wheel', wheel, { passive: false });
    element.addEventListener('mousedown', mouseDown);
    element.addEventListener('pointerdown', down);
    element.addEventListener('pointermove', move);
    element.addEventListener('pointerup', up);
    element.addEventListener('pointercancel', up);
    return () => {
      element.removeEventListener('wheel', wheel);
      element.removeEventListener('mousedown', mouseDown);
      element.removeEventListener('pointerdown', down);
      element.removeEventListener('pointermove', move);
      element.removeEventListener('pointerup', up);
      element.removeEventListener('pointercancel', up);
    };
  }, [bag.timelineRef, bag.sidebarWidth, onPanEnd]);
};

function TimelineBody({ params, setParams, bounds, rate, frames, onRangeChanged }: {
  params: Params;
  setParams(patch: Partial<Params>): void;
  bounds: Range;
  // 时间轴帧率（逻辑 tick 或运行档案帧率），决定刻度数字的小数位；未知时为 null。
  rate: number | null;
  frames: RouteFrame[] | null;
  onRangeChanged(update: (previous: Range) => Range): void;
}) {
  const binding = useDocumentObservationBinding();
  const runId = useWorkbench(state => state.runId);
  const trace = useWorkbench(useShallow(state => ({ current: state.frame?.route ?? null, frames: state.routeFrames })));
  const schemes = useInputSchemes();
  const { setTimelineRef, getValueFromScreenX, sidebarWidth } = useTimelineContext();
  const { inputTimeline, inputRoot, graphs, curves, selectedCurves } = useWorkbench(useShallow(state => ({
    inputTimeline: state.inputTimeline, inputRoot: state.inputRoot, graphs: state.graphs,
    curves: state.archiveCurves, selectedCurves: state.selectedCurves,
  })));
  // 作者模式下未保存的片段时间叠加到运行的作者 Timeline 上。
  const edited = useAuthoring(state => state.edited);
  const files = useAuthoring(state => state.files);
  const timelines = useAuthorTimelines();
  const elements = useSkillElements(useAuthoring(state => state.target !== null));
  const selectedClip = useWorkbench(state => state.selection?.kind === 'clip' ? state.selection.id : null);
  const references = useReferenceCurves();
  const allSegments = useMemo(() => frames ? routeSegments(frames, timelines, graphs) : documentSegments(timelines).map(segment => {
    const scheme = schemes.find(item => item.documentId === segment.documentId);
    return scheme ? { ...segment, length: Math.max(segment.length, scheme.rangeEndSeconds) } : segment;
  }), [frames, timelines, graphs, schemes]);
  const graphNames = useMemo(() => {
    const names = new Map((graphs ?? []).filter(graph => graph.displayName && graph.displayName !== graph.id)
      .map(graph => [graph.id, graph.displayName!]));
    for (const timeline of timelines.filter(item => schemes.some(scheme => scheme.documentId === item.documentId)))
      for (const track of timeline.tracks)
        for (const clip of track.clips)
          if (clip.graphId) {
            const graph = currentDocument(graphPath(clip.graphId)) as import('../../state/skillJson').JsonObject;
            names.set(clip.graphId, graph.name as string);
          }
    return names;
  }, [graphs, timelines, schemes, files, edited]);

  // 照 Timeline Editor 顶栏的“技能 / 技能根”：路线只显示所选技能根状态机上的播放。
  const programOf = useMemo(() => {
    const byGraph = new Map((graphs ?? []).map(graph => [graph.id, graph.programId]));
    return (segment: RouteSegment) => segment.state ? byGraph.get(segment.state.graphId) ?? null : null;
  }, [graphs]);
  const programs = useMemo(() => [...new Set(allSegments.map(programOf).filter((item): item is string => item !== null))], [allSegments, programOf]);
  const program = params.program !== null && programs.includes(params.program) ? params.program : programs[0] ?? null;
  const segments = useMemo(() => allSegments.filter(segment => {
    const owner = programOf(segment);
    return owner === null || owner === program;
  }), [allSegments, programOf, program]);

  const focusId = useWorkbench(state => params.focus ? playbackAt(segments, params.focus, state.frameIndex)?.id ?? null : null);
  // 技能文档看单个 Timeline：所选的那条，否则第一条；选“全部 Timeline”时按组列出。
  const focused = frames ? (focusId ? segments.find(segment => segment.id === focusId) ?? null : null)
    : params.focus === allTimelines ? null : segments.find(segment => segment.documentId === params.focus) ?? segments[0] ?? null;
  const inputScheme = schemes.find(scheme => scheme.documentId === focused?.documentId);
  const inputObservation = useInputObservation(inputScheme?.documentId ?? null);
  const receivedSource = useMemo(() => runId !== null && inputObservation.kind === 'ready' ? {
    jobId: runId, actorId: inputObservation.input.actorId, metadata: inputObservation.metadata, keys: inputObservation.declarations.keys,
  } : null, [runId, inputObservation.kind === 'ready' ? inputObservation.input.actorId : null,
    inputObservation.kind === 'ready' ? inputObservation.metadata : null, inputObservation.kind === 'ready' ? inputObservation.declarations : null]);
  const received = useReceivedInput(receivedSource);
  const playbacks = useWorkbench(state => state.frame?.route?.playbacks);
  const caller = params.caller?.runId === runId && matchesDocumentCall(params.caller.call, binding) ? params.caller : null;
  const observations = useMemo(() => {
    const result = new Map<string, TimelineObservation>();
    for (const playback of playbacks ?? [])
      if (playback.documentId === focused?.documentId && playback.call !== null && playback.domain !== null && matchesDocumentCall(playback.call, binding) &&
        (caller === null || parentCallMatches(caller.call, playback.call))) {
        const observation = { call: playback.call, domain: playback.domain };
        result.set(observationKey(observation), observation);
      }
    return [...result.values()];
  }, [playbacks, focused?.documentId, binding, caller]);
  const pin = params.pin?.runId === runId && params.pin.documentId === focused?.documentId && matchesDocumentCall(params.pin.observation.call, binding)
    ? params.pin : null;
  const observed = pin?.observation ?? observations.find(observation => observationKey(observation) === params.call)
    ?? (observations.length === 1 ? observations[0] : null);
  const choices = pin && !observations.some(item => observationKey(item) === observationKey(pin.observation))
    ? [pin.observation, ...observations] : observations;
  const togglePin = pin ? () => setParams({ pin: null }) : observed === null || runId === null || focused === null ? undefined
    : () => setParams({ call: observationKey(observed), pin: { runId, documentId: focused.documentId, observation: observed } });
  const recorded = useMemo(() => recordedGraphCalls(trace.frames, trace.current, graphs ?? [], binding), [trace, graphs, binding]);
  const parents = useMemo(() => observed === null ? [] : parentGraphCalls(observed.call, recorded), [observed, recorded]);
  const openParent = runId === null ? undefined : ({ graph, call }: RecordedGraphCall) => {
    selectionActions.showGraph(graphKey(graph), { runId, call });
    openPanel('graph');
  };
  const selectCall = (value: string) => {
    setParams({ call: value, pin: null });
  };
  useEffect(() => {
    const selection = getState().selection;
    if ((selection?.kind === 'clip' || selection?.kind === 'track') && selection.id.startsWith(`${focused?.documentId}/`)) {
      const previous = selection.call ? callKey(selection.call) : null;
      const next = observed ? callKey(observed.call) : null;
      if (previous !== next) selectionActions.select({ ...selection, call: observed?.call });
    }
  }, [focused?.documentId, observed]);

  const enter = useCallback((documentId: string) => {
    const segment = playbackAt(segments, documentId, getState().frameIndex);
    if (!segment) return;
    const selection = getState().selection;
    if ((selection?.kind === 'clip' || selection?.kind === 'track') && !selection.id.startsWith(`${documentId}/`))
      selectionActions.select(null);
    if (!frames) {
      setParams({ focus: documentId, range: segmentRange(segment), call: null, pin: null, caller: null });
      return;
    }
    const frameIndex = getState().frameIndex;
    if (frameIndex < segment.firstIndex || frameIndex > segment.lastIndex) transport.seek(segment.firstIndex);
    setParams({ focus: documentId, routeRange: params.focus ? params.routeRange : params.range, range: segmentRange(segment), call: null, pin: null, caller: null });
  }, [frames, segments, setParams, params.focus, params.routeRange, params.range]);
  const back = useCallback(() => setParams({ focus: null, range: params.routeRange, routeRange: null, call: null, pin: null, caller: null }), [setParams, params.routeRange]);
  const showAll = useCallback(() => setParams({ focus: allTimelines, range: null, call: null, pin: null, caller: null }), [setParams]);

  // 节点图双击播放 Timeline 的节点时进入该 Timeline。
  const request = useWorkbench(state => state.timelineRequest);
  const handled = useRef(0);
  useEffect(() => {
    if (!request || request.sequence === handled.current) return;
    handled.current = request.sequence;
    const owner = allSegments.find(segment => segment.documentId === request.documentId);
    if (!owner) return;
    const ownerProgram = programOf(owner);
    if (ownerProgram !== null && ownerProgram !== program) setParams({ program: ownerProgram });
    enter(request.documentId);
    setParams({ caller: request.observation });
  }, [request, allSegments, programOf, program, setParams, enter]);

  // 转移标记：路线上每次换段的时刻，悬停说明转移到的状态。技能文档的各 Timeline 都从 0 开始，没有转移。
  const marks = useMemo(() => frames ? segments.slice(1).map(segment => ({
    key: segment.id, seconds: segment.start,
    label: `转移到 ${segment.state?.name ?? segment.timeline?.displayName ?? segment.documentId.slice(0, 8)} · ${segment.start.toFixed(3)} s`,
  })) : [], [frames, segments]);

  const rows = useMemo(() => {
    const filter = params.filter.trim().toLowerCase();
    const collapsed = new Set(params.collapsed);
    const curveTracks = new Set(params.curveTracks);
    // 参数区按本轨选中片段的参数个数（通道与已读到的参考曲线）定高，照 BtsmtlTimelineTrackBinding.GetNaturalExpandedHeight。
    const expand = (row: TrackRow): TrackRow => {
      if (!curveTracks.has(row.id) || !elements || !trackHasCurves(elements, row.timeline.documentId, row.track.id)) return row;
      const clip = row.clips.find(item => item.selectId === selectedClip);
      const source = clip ? clipReferenceSource(elements, row.timeline.documentId, clip.clip.id) : null;
      const entry = source ? references[source.identity] : undefined;
      const count = clip ? clipCurves(elements, row.timeline.documentId, clip.clip.id).length + (entry && 'curves' in entry ? entry.curves.length : 0) : 0;
      return {
        ...row, curves: params.curveParam?.track === row.id
          ? Math.min(curveMaxHeight, Math.max(curveInspectHeight(count), params.curveHeights[row.id] ?? 0)) : curveListHeight(count),
      };
    };
    const result: Row[] = [];
    const addCurves = () => {
      if (curves === null) return;
      result.push({ kind: 'curves', id: 'curves' });
      if (!collapsed.has('curves'))
        for (const curve of curves)
          if (selectedCurves.includes(curve.id) && (!filter || curve.displayName.toLowerCase().includes(filter)))
            result.push({ kind: 'curve', id: `curve:${curve.id}`, curve });
    };
    if (focused) {
      const input = schemes.some(scheme => scheme.documentId === focused.documentId);
      // 照 BTSMTL Timeline 编辑器：所看的 Timeline 是一个可折叠的分组，轨道在组内。
      const group: GroupRow = { kind: 'group', id: `group:${focused.id}`, segment: focused, title: segmentTitle(focused), focused: true };
      const tracks = collapsed.has(group.id) ? []
        : segmentRows(focused, graphNames).map(row => input ? { ...row, trackKind: 'in' as const } : expand(row));
      result.push(group, ...(filter ? tracks.filter(row => row.track.displayName.toLowerCase().includes(filter)) : tracks));
      if (input && received) {
        result.push({ kind: 'receivedGroup', id: 'received', received });
        if (!collapsed.has('received')) {
          const lines: (ReceivedKey & { missing: boolean })[] = received.keys.map(key => ({ ...key, missing: false }));
          if (received.missing.length) lines.push({ name: '未收到', spans: received.missing, requests: [], missing: true });
          for (const line of lines)
            if (!filter || line.name.toLowerCase().includes(filter))
              result.push({ kind: 'received', id: `received:${line.missing ? '' : 'key:'}${line.name}`, ...line });
        }
      }
      addCurves();
      return result;
    }
    const root = selectedInputRoot(inputTimeline?.roots, inputRoot);
    if (inputTimeline) {
      result.push({ kind: 'inputGroup', id: 'input' });
      if (root && !collapsed.has('input'))
        for (const track of root.tracks)
          if (!filter || track.displayName.toLowerCase().includes(filter))
            result.push({ kind: 'inputTrack', id: `input:${track.id}`, selectId: inputTrackSelectId(track.id), track });
    }
    for (const segment of segments) {
      const group: GroupRow = { kind: 'group', id: `group:${segment.id}`, segment, title: segmentTitle(segment) };
      const titleMatched = !filter || group.title.toLowerCase().includes(filter);
      const tracks = collapsed.has(group.id) ? [] : segmentRows(segment, graphNames).map(expand);
      const children = titleMatched ? tracks : tracks.filter(row => row.track.displayName.toLowerCase().includes(filter));
      if (titleMatched || children.length) result.push(group, ...children);
    }
    addCurves();
    return result;
  }, [segments, focused, graphNames, inputTimeline, inputRoot, schemes, curves, selectedCurves, received, params.filter, params.collapsed,
    params.curveTracks, params.curveParam, params.curveHeights, elements, selectedClip, references]);

  const toggle = useCallback((id: string) => {
    const collapsed = new Set(params.collapsed);
    if (!collapsed.delete(id)) collapsed.add(id);
    setParams({ collapsed: [...collapsed] });
  }, [params.collapsed, setParams]);
  const toggleCurves = useCallback((id: string) => {
    const expanded = new Set(params.curveTracks);
    const collapsing = expanded.delete(id);
    if (!collapsing) expanded.add(id);
    setParams({ curveTracks: [...expanded], ...(collapsing && params.curveParam?.track === id ? { curveParam: null } : {}) });
  }, [params.curveTracks, params.curveParam, setParams]);

  const step = useContext(SnapContext);
  // 游标跳到时间轴上的一点（运行档案为运行秒数，技能文档为 Timeline 本地秒数）：刻度点击拖动与时间框共用。
  const seek = useCallback((value: number, exact = false) => {
    if (frames) { void transport.seekTime(value); return; }
    if (!focused) return;
    const raw = Math.max(0, Math.min(focused.length, value));
    const seconds = step && !exact ? Math.min(focused.length, snapTo(raw, step)) : raw;
    if (inputScheme && inputObservation.kind === 'ready') {
      const metadata = inputObservation.metadata;
      void transport.seekTime((scheduleZeroFrame(metadata) - metadata.firstRecordedSdkFrame) / metadata.sdkTickRate + seconds);
      return;
    }
    if (!observed) {
      useAuthorCursor.setState({ documentId: focused.documentId, seconds });
      return;
    }
    // 运行观察：本次播放的起点 = 当前帧运行秒数 − 本地游标，按运行秒数跳到对应帧，画面随档案帧更新。
    const state = getState();
    const playback = documentPlayback(state, { kind: 'document', documentId: focused.documentId, observation: observed });
    if (transport.source() === 'sealed' && playback && state.seconds !== null)
      void transport.seekTime(state.seconds - playback.cursorSeconds + seconds);
    else consoleActions.log('warn', 'Timeline', '只有已封存的运行能按 Timeline 时间跳帧');
  }, [frames, observed, focused, step, inputScheme, inputObservation]);
  const seekAt = useCallback((clientX: number) => seek(getValueFromScreenX(clientX)), [seek, getValueFromScreenX]);
  const [pickedKeys, setPickedKeys] = useState<number[]>([]);
  const curveView = useMemo<CurveView>(() => ({
    inspected: params.curveParam,
    inspect: (track, parameter) => {
      setPickedKeys([]);
      setParams({ curveParam: parameter === null ? null : { track, parameter } });
    },
    resize: (track, height) => setParams({ curveHeights: { ...params.curveHeights, [track]: Math.round(height) } }),
    seek: value => seek(value, true),
    keys: pickedKeys,
    selectKeys: setPickedKeys,
  }), [params.curveParam, params.curveHeights, setParams, seek, pickedKeys]);

  // 显示全部（照 Timeline 的 Frame All，快捷键 A）：所看的 Timeline，否则整条时间轴。
  const frameAll = useCallback(() => setParams({ range: focused ? segmentRange(focused) : fitRange(bounds) }), [setParams, focused, bounds]);
  const snap = params.snap ?? defaults.snap;
  // 齿轮菜单（照 Timeline 窗口右上角的设置）：显示范围、游标跟随、编辑吸附、分组展开。
  const settings: MenuEntry[] = [
    { label: <>显示全部<kbd className={styles.shortcut}>A</kbd></>, onSelect: frameAll },
    { kind: 'check', label: '游标离开视图时跟随', checked: params.follow, onChange: follow => setParams({ follow }) },
    { kind: 'separator' },
    { kind: 'label', label: '编辑吸附' },
    // 逻辑树等按 tick 量化的片段在任何模式下都按逻辑 tick；素材帧模式下标记与游标按逻辑 tick。
    ...([['frame', '素材帧（动画按源帧）'], ['tick', '逻辑 tick'], ['0.1', '0.1 秒'], ['0.01', '0.01 秒'], ['0.001', '0.001 秒'], ['none', '关闭吸附']] as const)
      .map(([value, label]): MenuEntry => ({ kind: 'radio', label, checked: snap === value, onSelect: () => setParams({ snap: value }) })),
    { kind: 'separator' },
    { label: '展开全部分组', onSelect: () => setParams({ collapsed: [] }) },
    {
      label: '折叠全部分组',
      onSelect: () => setParams({ collapsed: rows.filter(row => row.kind === 'group' || row.kind === 'inputGroup' || row.kind === 'curves').map(row => row.id) }),
    },
  ];

  const origin = focused?.origin ?? 0;
  const programLabel = (item: string) => `${item.slice(item.indexOf(':') + 1)} / 技能根`;
  const timelineName = (segment: RouteSegment) =>
    schemes.find(scheme => scheme.documentId === segment.documentId)?.name ?? segment.timeline?.displayName ?? segment.documentId.slice(0, 8);
  const routeFocus = focused?.timeline?.displayName ?? (params.focus ? params.focus.slice(0, 8) : null);
  const subject: Subject = frames ? {
    selector: programs.length > 1 ? {
      label: '选择技能根', entries: programs.map((item): MenuEntry => ({
        kind: 'radio', label: programLabel(item), checked: item === program,
        onSelect: () => setParams({ program: item, focus: null, routeRange: null, call: null, pin: null, caller: null }),
      })),
    } : null,
    crumbs: [
      { label: program ? programLabel(program) : '技能根', onSelect: routeFocus === null ? undefined : back },
      ...(routeFocus === null ? [] : [{ label: routeFocus }]),
    ],
  } : {
    selector: segments.length > 1 ? {
      label: '选择 Timeline', entries: [
        { kind: 'radio', label: '全部 Timeline', checked: focused === null, onSelect: showAll },
        ...segments.map((segment): MenuEntry => ({
          kind: 'radio', checked: segment.documentId === focused?.documentId, onSelect: () => enter(segment.documentId),
          label: <>{timelineName(segment)}<span className={styles.subjectLength}>{segment.length.toFixed(2)} s</span></>,
        })),
      ],
    } : null,
    crumbs: [{ label: focused ? timelineName(focused) : '全部 Timeline' }],
  };
  const clock = useMemo<PreviewClock>(() => frames ? { kind: 'archive' }
    : inputScheme ? { kind: 'input', documentId: inputScheme.documentId, observing: inputObservation.kind === 'ready' }
      : { kind: 'document', documentId: focused?.documentId ?? null, observation: observed },
  [frames, focused?.documentId, observed, inputScheme, inputObservation.kind]);
  const curveClock = useWorkbench(useShallow(state => {
    if (state.seconds === null || (clock.kind === 'input' && !clock.observing)) return null;
    const seconds = previewSeconds(state, clock, null);
    return seconds === null ? null : { sourceSeconds: state.seconds, seconds };
  }));

  return (
    <PreviewClockContext.Provider value={clock}>
    <CurveClockContext.Provider value={curveClock}>
    <div className={styles.window} style={{ ['--sidebar' as string]: `${sidebarWidth}px` }}>
      <TimelineToolbar settings={settings} subject={subject} origin={origin} seek={seek}
        observations={choices} selectCall={selectCall} pinned={pin !== null} togglePin={togglePin} parents={parents} openParent={openParent} />
      <div className={styles.timeline} ref={setTimelineRef}>
        <div className={styles.header}>
          <div className={styles.timeHead}>
            <AddTrack documentId={focused?.timeline ? focused.documentId : null} caret />
            <label className={styles.search}>
              <SearchIcon className={styles.searchIcon} />
              <TextInput className={styles.searchInput} placeholder="搜索轨道" value={params.filter}
                onChange={event => setParams({ filter: event.currentTarget.value })} aria-label="搜索轨道" />
            </label>
          </div>
          <Ruler bounds={bounds} origin={origin} rate={rate} marks={marks} seekAt={seekAt} />
        </div>
        <CurveViewContext.Provider value={curveView}>
          <Rows rows={rows} collapsed={params.collapsed} toggle={toggle} toggleCurves={toggleCurves} enter={enter} frameAll={frameAll} inputRoots={inputTimeline?.roots} />
        </CurveViewContext.Provider>
        <div className={styles.footer}>
          <div className={styles.footHead} />
          <TimeScroller bounds={focused ? { start: focused.origin, end: focused.origin + focused.length } : bounds}
            onRangeChanged={onRangeChanged} />
        </div>
        <Overlay bounds={bounds} origin={origin} segments={frames && !focused ? segments : []} follow={params.follow} onRangeChanged={onRangeChanged} />
      </div>
    </div>
    </CurveClockContext.Provider>
    </PreviewClockContext.Provider>
  );
}

// ── 窗口工具条（照 Timeline 窗口顶行）：时间框、Timeline 选择器与面包屑、本次实际调用，右侧齿轮设置；播放与保存在顶栏 ──

interface Subject {
  // 选择器（照 Sequence Selector）：技能里的 Timeline 或路线所属的技能根；只有一项可选时为 null。
  selector: { label: string; entries: MenuEntry[] } | null;
  // 面包屑：前面的层级可点击返回，最后一项是所看的内容。
  crumbs: { label: ReactNode; onSelect?(): void }[];
}

// 在 Timeline 上添加轨道（编辑行带下拉三角的＋，分组头的＋）：种类与默认值取原 C# 轨道类型。
function AddTrack({ documentId, caret }: { documentId: string | null; caret?: boolean }) {
  const author = useAuthoring(state => state.author && state.target !== null);
  const elements = useSkillElements(author && documentId !== null);
  const schemes = useInputSchemes();
  if (!author || !documentId) return null;
  if (schemes.some(scheme => scheme.documentId === documentId)) return <AddInput documentId={documentId} caret={caret} />;
  // 每种轨道按轨道合同允许的执行域各列一项。
  const entries: MenuEntry[] = elements ? elements.tracks.flatMap(template => template.contract.domains.map((domain): MenuEntry => ({
    label: `${template.kind ?? template.clrType} · ${domain}`,
    onSelect: () => {
      const id = authoringActions.addTrack(documentId, { ...template.value, m_ExecutionDomain: domain }, template.kind ?? '新轨道');
      selectionActions.select({ kind: 'track', id: trackSelectId(documentId, id) });
    },
  }))) : [{ label: '读取中', disabled: true, onSelect: () => {} }];
  return <MenuButton label="添加轨道" variant="ghost" className={styles.addButton} iconOnly caret={caret} entries={entries}><AddIcon /></MenuButton>;
}

function AddInput({ documentId, caret }: { documentId: string; caret?: boolean }) {
  const context = useInputContext(documentId);
  const [pending, setPending] = useState(false);
  const clock = useContext(PreviewClockContext);
  const cursor = usePreviewSelect(clock, seconds => seconds);
  if (context.kind !== 'ready') {
    return <Button variant="ghost" className={styles.addButton} disabled tip={`添加输入：${context.reason}`} aria-label="添加输入"><AddIcon /></Button>;
  }
  const entries = context.actor.keys.flatMap<MenuEntry>(key => ([
    { kind: 'label', label: key.name },
    ...(['hold', 'press'] as const).map(operation => ({
      label: operation === 'hold' ? '按住' : '单按', disabled: pending,
      onSelect: () => {
        const startSeconds = Math.max(0, cursor ?? 0);
        setPending(true);
        void inputAuthoring.add(documentId, {
          scene: context.scene.path, sceneRevision: context.scene.revision, actorId: context.actor.actorId,
          key: key.name, operation, startSeconds, endSeconds: startSeconds + 1,
        }, null).catch(error => consoleActions.error('输入', error)).finally(() => setPending(false));
      },
    })),
  ]));
  return <MenuButton label="添加输入" variant="ghost" className={styles.addButton} iconOnly caret={caret} entries={entries}><AddIcon /></MenuButton>;
}

function TimelineToolbar({ settings, subject, origin, seek, observations, selectCall, pinned, togglePin, parents, openParent }: {
  settings: MenuEntry[]; subject: Subject; origin: number; seek(value: number): void;
  observations: TimelineObservation[]; selectCall(value: string): void;
  pinned: boolean; togglePin: (() => void) | undefined; parents: RecordedGraphCall[];
  openParent: ((item: RecordedGraphCall) => void) | undefined;
}) {
  const binding = useDocumentObservationBinding();
  const clock = useContext(PreviewClockContext);
  const input = useInputObservation(clock.kind === 'input' ? clock.documentId : null);
  // 运行叠加读不到时的原因，一行；正常时不显示（游标头已给出时刻）。
  const notice = useWorkbench(state => {
    if (clock.kind === 'input') return input.kind === 'ready' ? null : input.reason;
    if (!state.runId) return null;
    if (binding.dirty) return '内容未保存，运行叠加已停用';
    if (!state.frame?.route) return '档案播放记录尚未提供';
    return documentPlayback(state, clock) ? null : '本帧没有这个 Timeline 的播放记录';
  });
  return (
    <PanelBar>
      <TimeField origin={origin} seek={seek} />
      {subject.selector && (
        <MenuButton label={subject.selector.label} variant="ghost" className={styles.selector} iconOnly caret entries={subject.selector.entries}>
          <SelectorIcon />
        </MenuButton>
      )}
      <span className={styles.crumbs}>
        <TimelineAssetIcon className={styles.crumbIcon} />
        {subject.crumbs.map((crumb, index) => (
          <Fragment key={index}>
            {index > 0 && <CrumbArrow className={styles.crumbArrow} />}
            {crumb.onSelect
              ? <button className={styles.crumbLink} onClick={crumb.onSelect}>{crumb.label}</button>
              : <span className={styles.crumb}>{crumb.label}</span>}
          </Fragment>
        ))}
      </span>
      {observations.length > 1 && <Select variant="ghost" label="实际调用" placeholder="选择实际调用" onChange={selectCall}
        value={clock.kind === 'document' && clock.observation ? observationKey(clock.observation) : null}
        options={observations.map(observation => ({ value: observationKey(observation), label: `${callLabel(observation.call)} · ${observation.domain}` }))} />}
      {clock.kind === 'document' && clock.observation !== null && <>
        <Button variant="ghost" pressed={pinned} disabled={togglePin === undefined} onClick={togglePin}
          tip={`${pinned ? '解除固定，按当前帧选择调用' : '固定本次实际调用和执行域'}\n${callLabel(clock.observation.call)} · ${clock.observation.domain}`}>固定调用</Button>
        {clock.observation.call.parentInvocationPath && (parents.length > 1 && openParent
          ? <MenuButton label="选择父调用" variant="ghost" entries={parents.map(item => ({ label: `${graphName(item.graph)} · ${callLabel(item.call)}`,
            onSelect: () => openParent(item) }))}>上层调用</MenuButton>
          : <Button variant="ghost" disabled={parents.length !== 1 || openParent === undefined}
            onClick={parents.length === 1 && openParent ? () => openParent(parents[0]) : undefined}
            tip={parents.length === 1 ? '在节点图返回同一次实际父调用'
              : clock.observation.call.parentInvocationGeneration === null ? '本记录没有父调用代次' : '已加载记录中没有匹配的父调用'}>上层调用</Button>)}
      </>}
      {notice && <span className={styles.hint} title={notice}>{notice}</span>}
      <span className={styles.grow} />
      <MenuButton label="Timeline 设置" variant="ghost" className={styles.cog} iconOnly caret entries={settings}><SettingsIcon /></MenuButton>
    </PanelBar>
  );
}

// 游标时刻的文字：秒，至多四位小数（照 Timeline 时间框的 0.####）。
const timeText = (seconds: number) => String(Math.round(seconds * 1e4) / 1e4);

// 时间框（TimeCodeGUI，宽 100）：显示游标在所看 Timeline 里的秒数；回车或离开输入框后跳到输入的时刻，Esc 放弃输入。
function TimeField({ origin, seek }: { origin: number; seek(value: number): void }) {
  const clock = useContext(PreviewClockContext);
  const seconds = usePreviewSelect(clock, value => value);
  const [draft, setDraft] = useState<string | null>(null);
  const commit = () => {
    if (draft === null) return;
    setDraft(null);
    const value = Number(draft);
    if (draft.trim() && Number.isFinite(value) && value >= 0) seek(origin + value);
  };
  return (
    <TextInput className={styles.timeField} aria-label="游标时间（秒）" disabled={seconds === null}
      value={draft ?? (seconds === null ? '' : timeText(seconds - origin))}
      onChange={event => setDraft(event.currentTarget.value)} onBlur={commit}
      onKeyDown={event => {
        if (event.key === 'Enter') commit();
        else if (event.key === 'Escape') setDraft(null);
      }} />
  );
}

// ── 刻度（照 Unity TimeArea.TimeRuler）：点击与拖动经 transport 跳帧 ──

// 刻度级别（秒）：相邻级别成整数倍，与编辑吸附的 0.1／0.01／0.001 秒网格对齐。
const tickModulos = [0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10, 30, 60, 300, 600, 1800, 3600];
const rulerHeight = 22;
// 小数位：能区分 modulo 间隔所需的位数（MathUtils.GetNumberOfDecimalsForMinimumDifference）。
const decimalsFor = (modulo: number) => Math.max(0, Math.ceil(-Math.log10(modulo) - 1e-9));

// 底部时间滚动条（照 ZoomableArea 的横向滑条）：滑块是当前可见范围，拖滑块平移，拖两端缩放。
function TimeScroller({ bounds, onRangeChanged }: { bounds: Range; onRangeChanged(update: (previous: Range) => Range): void }) {
  const { range } = useTimelineContext();
  const track = useRef<HTMLDivElement>(null);
  const total = { start: Math.min(bounds.start, range.start), end: Math.max(bounds.end, range.end) };
  const span = total.end - total.start || 1;
  const left = (range.start - total.start) / span * 100;
  const width = (range.end - range.start) / span * 100;
  const drag = (mode: 'move' | 'start' | 'end') => (event: PointerEvent<HTMLElement>) => {
    if (event.button !== 0 || !track.current) return;
    event.stopPropagation();
    const element = event.currentTarget;
    element.setPointerCapture(event.pointerId);
    const scale = span / track.current.clientWidth;
    let lastX = event.clientX;
    const move = (next: globalThis.PointerEvent) => {
      const delta = (next.clientX - lastX) * scale;
      lastX = next.clientX;
      onRangeChanged(previous => mode === 'move' ? { start: previous.start + delta, end: previous.end + delta }
        : mode === 'start' ? { start: Math.min(previous.end - minSpan, previous.start + delta), end: previous.end }
          : { start: previous.start, end: Math.max(previous.start + minSpan, previous.end + delta) });
    };
    const up = () => {
      element.removeEventListener('pointermove', move);
      element.removeEventListener('pointerup', up);
      element.removeEventListener('pointercancel', up);
    };
    element.addEventListener('pointermove', move);
    element.addEventListener('pointerup', up);
    element.addEventListener('pointercancel', up);
  };
  return (
    <div className={styles.scroller} ref={track}>
      <div className={styles.scrollThumb} style={{ left: `${left}%`, width: `${width}%` }} onPointerDown={drag('move')}>
        <i className={styles.scrollEnd} data-side="start" onPointerDown={drag('start')} />
        <i className={styles.scrollEnd} data-side="end" onPointerDown={drag('end')} />
      </div>
    </div>
  );
}

// origin：刻度零点，单个 Timeline 视图按该次播放的本地时间标注，0 之前不画刻度。
// 每级刻度按间距定强度：3 像素为 0、80 像素为 1，开方渐显，高至多为刻度高的 0.9×0.7，自底向上画；
// 首个间距不小于 40 像素的级别在刻度右侧 3 像素标数字，小数位取帧率与该级间隔中较细的一个。
// 底部 5 像素蓝条为 Timeline 时长（HighlightTimeAreaRange）。拖动刻度时 data-scrubbing 让覆盖层显示游标时刻。
function Ruler({ bounds, origin, rate, marks, seekAt }: {
  bounds: Range; origin: number; rate: number | null; marks: { key: string; seconds: number; label: string }[]; seekAt(clientX: number): void;
}) {
  const { range, valueToPixels } = useTimelineContext();
  const width = valueToPixels(range.end - range.start);
  const scale = width / (range.end - range.start);
  const first = Math.max(0, range.start - origin), last = range.end - origin;
  const x = (local: number) => Math.floor(valueToPixels(local + origin - range.start));
  const levels: { path: string; opacity: number }[] = [];
  const labels: { x: number; text: string }[] = [];
  if (width > 0 && last > first) {
    // 最细的可见级别与最细的满强度级别（TickHandler.SetTickStrengths）。
    const strengths = tickModulos.map(modulo => (modulo * scale - 3) / (80 - 3));
    let smallest = 0, biggest = tickModulos.length - 1;
    for (let index = tickModulos.length - 1; index >= 0; index--) {
      if (strengths[index] >= 1) biggest = index;
      if (tickModulos[index] * scale <= 3) { smallest = index; break; }
    }
    for (let index = smallest; index <= biggest; index++) {
      const strength = Math.sqrt(Math.min(1, Math.max(0, strengths[index]))) * 0.9;
      if (strength <= 0) continue;
      const modulo = tickModulos[index];
      // 与上一级重合的刻度留给上一级画。
      const coarser = index < biggest ? Math.round(tickModulos[index + 1] / modulo) : 0;
      const top = rulerHeight - rulerHeight * Math.min(1, strength) * 0.7;
      let path = '';
      for (let tick = Math.ceil(first / modulo); tick * modulo <= last; tick++)
        if (!coarser || tick % coarser) path += `M${x(tick * modulo) + 0.5} ${top}V${rulerHeight}`;
      if (path) levels.push({ path, opacity: Math.min(1, strength / 0.5 * 0.75) });
    }
    const labelModulo = tickModulos.find(modulo => modulo * scale >= 40);
    if (labelModulo !== undefined) {
      const digits = Math.max(decimalsFor(labelModulo), rate ? decimalsFor(1 / rate) : 0);
      for (let tick = Math.ceil(first / labelModulo); tick * labelModulo <= last; tick++)
        labels.push({ x: x(tick * labelModulo) + 3, text: (tick * labelModulo).toFixed(digits) });
    }
  }
  const durationStart = Math.max(0, valueToPixels(bounds.start - range.start));
  const durationEnd = Math.min(width, valueToPixels(bounds.end - range.start));
  return (
    <div className={styles.ruler} role="slider" aria-label="记录游标" aria-valuemin={bounds.start} aria-valuemax={bounds.end}
      onPointerDown={event => {
        if (event.button !== 0) return;
        event.currentTarget.setPointerCapture(event.pointerId);
        event.currentTarget.dataset.scrubbing = '';
        seekAt(event.clientX);
      }}
      onPointerMove={event => { if (event.currentTarget.hasPointerCapture(event.pointerId)) seekAt(event.clientX); }}
      onLostPointerCapture={event => { delete event.currentTarget.dataset.scrubbing; }}>
      <svg className={styles.ticks} width={Math.max(0, width)} height={rulerHeight} aria-hidden>
        {levels.map((level, index) => <path key={index} d={level.path} strokeOpacity={level.opacity} />)}
      </svg>
      {labels.map(label => <span key={label.text} className={styles.tickLabel} style={{ left: label.x }}>{label.text}</span>)}
      {durationEnd > durationStart && <i className={styles.duration} style={{ left: durationStart, width: durationEnd - durationStart }} />}
      {marks.filter(mark => mark.seconds >= range.start && mark.seconds <= range.end).map(mark => (
        <i key={mark.key} className={styles.routeMark} style={{ left: valueToPixels(mark.seconds - range.start) }} title={mark.label} />
      ))}
    </div>
  );
}

// ── 行：只渲染可见行 ──

// 框选中的片段（data-box 键）；照 Timeline Editor 在轨道空白处左键拖出选框。
const BoxedContext = createContext<ReadonlySet<string>>(new Set());
interface Box { x0: number; y0: number; x1: number; y1: number }

function Rows({ rows, collapsed, toggle, toggleCurves, enter, frameAll, inputRoots }: {
  rows: Row[];
  collapsed: string[];
  toggle(id: string): void;
  toggleCurves(id: string): void;
  enter(documentId: string): void;
  frameAll(): void;
  inputRoots: FrozenInputTimeline[] | undefined;
}) {
  const clock = useContext(PreviewClockContext);
  const step = useContext(SnapContext);
  const panelId = usePanelId();
  const scroller = useRef<HTMLDivElement>(null);
  const selectedClips = useWorkbench(state => state.timelineSelections[panelId]);
  const boxed = useMemo(() => new Set(selectedClips?.map(item => item.box)), [selectedClips]);
  const [box, setBox] = useState<Box | null>(null);
  const boxStart = useRef<{ x: number; y: number } | null>(null);
  // 框选后选中的是第一个片段；其它面板或单击换了选中时，框选结果作废。
  const selection = useWorkbench(state => state.selection);
  useEffect(() => {
    if (selectedClips?.length && !(selection?.kind === 'clip' && selection.id === selectedClips[0].id))
      selectionActions.selectTimelineClips(panelId, []);
  }, [selection, selectedClips, panelId]);

  const boxDown = (event: PointerEvent<HTMLDivElement>) => {
    const target = event.target as HTMLElement;
    if (event.button !== 0 || !target.closest('[data-lane]') || target.closest(`button, .${styles.curvePlot}, .${styles.clipCurves}`)) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    boxStart.current = { x: event.clientX, y: event.clientY };
  };
  const boxMove = (event: PointerEvent<HTMLDivElement>) => {
    const start = boxStart.current;
    if (!start) return;
    setBox({ x0: start.x, y0: start.y, x1: event.clientX, y1: event.clientY });
  };
  const boxUp = (event: PointerEvent<HTMLDivElement>) => {
    const start = boxStart.current;
    if (!start) return;
    boxStart.current = null;
    setBox(null);
    const left = Math.min(start.x, event.clientX), right = Math.max(start.x, event.clientX);
    const top = Math.min(start.y, event.clientY), bottom = Math.max(start.y, event.clientY);
    // 单击空白：取消选中。
    if (right - left < 3 && bottom - top < 3) {
      selectionActions.selectTimelineClips(panelId, []);
      selectionActions.select(null);
      return;
    }
    const hits = [...event.currentTarget.querySelectorAll<HTMLElement>('[data-box]')].filter(element => {
      const rect = element.getBoundingClientRect();
      return rect.right >= left && rect.left <= right && rect.bottom >= top && rect.top <= bottom;
    });
    selectionActions.selectTimelineClips(panelId, hits.map(element => ({ box: element.dataset.box!, id: element.dataset.select! })));
    if (hits.length) selectTimelineObject(clock, 'clip', hits[0].dataset.select!);
    else selectionActions.select(null);
  };
  const panelWindow = usePanelWindow();
  const [view, setView] = useState({ top: 0, height: 600 });
  const offsets = useMemo(() => {
    const result: number[] = [];
    let total = 0;
    for (const row of rows) { result.push(total); total += rowHeight(row); }
    result.push(total);
    return result;
  }, [rows]);
  const total = offsets[offsets.length - 1];
  const nesting = useMemo(() => rowNesting(rows), [rows]);
  useLayoutEffect(() => {
    const element = scroller.current!;
    const measure = () => setView({ top: element.scrollTop, height: element.clientHeight });
    const Observer = (panelWindow as Window & { ResizeObserver: typeof ResizeObserver }).ResizeObserver;
    const observer = new Observer(measure);
    measure();
    observer.observe(element);
    return () => observer.disconnect();
  }, [panelWindow, total]);
  let first = 0;
  while (first < rows.length && offsets[first + 1] < view.top - overscan) first++;
  let last = first;
  while (last < rows.length && offsets[last] < view.top + view.height + overscan) last++;
  const collapsedSet = new Set(collapsed);

  return (
    <div className={styles.rows} ref={scroller} tabIndex={-1} onPointerDown={boxDown} onPointerMove={boxMove} onPointerUp={boxUp}
      onKeyDown={event => {
        if ((event.target as HTMLElement).closest('input, textarea')) return;
        // A：显示全部（Frame All），不分作者与运行。
        if ((event.key === 'a' || event.key === 'A') && !event.ctrlKey && !event.metaKey && !event.altKey) {
          event.preventDefault();
          frameAll();
          return;
        }
        if (!useAuthoring.getState().author) return;
        if ((event.ctrlKey || event.metaKey) && (event.key === 'c' || event.key === 'v')) {
          if (event.key === 'c' ? copySelection() : pasteAfterSelection()) event.preventDefault();
          return;
        }
        // 作者模式 Delete：删除框选的片段，否则删除选中的片段、标记或轨道。
        if (event.key !== 'Delete') return;
        event.preventDefault();
        if (!boxed.size) { deleteSelection(); return; }
        deleteClips([...event.currentTarget.querySelectorAll<HTMLElement>('[data-box]')]
          .filter(element => boxed.has(element.dataset.box!)).map(element => element.dataset.select!));
        selectionActions.selectTimelineClips(panelId, []);
      }}
      onPointerCancel={() => { boxStart.current = null; setBox(null); }}
      onScroll={event => {
        const top = event.currentTarget.scrollTop;
        setView(previous => previous.top === top ? previous : { ...previous, top });
      }}>
      <BoxedContext.Provider value={boxed}>
      <div style={{ height: offsets[first] }} />
      {rows.slice(first, last).map((row, offset) => {
        const nest = nesting[first + offset];
        switch (row.kind) {
          case 'group': return <GroupLine key={row.id} row={row} nest={nest} open={!collapsedSet.has(row.id)} toggle={toggle} enter={enter} />;
          case 'track': return <TrackLine key={row.id} row={row} nest={nest} toggleCurves={toggleCurves} />;
          case 'inputGroup':
            return <InputGroupLine key={row.id} nest={nest} open={!collapsedSet.has(row.id)} toggle={toggle} roots={inputRoots} />;
          case 'inputTrack': return <InputTrackLine key={row.id} row={row} nest={nest} />;
          case 'curves': return <CurveGroupLine key={row.id} nest={nest} open={!collapsedSet.has(row.id)} toggle={toggle} />;
          case 'curve': return <CurveLine key={row.id} row={row} nest={nest} />;
          case 'receivedGroup':
            return <ReceivedGroupLine key={row.id} nest={nest} received={row.received} open={!collapsedSet.has(row.id)} toggle={toggle} />;
          case 'received': return <ReceivedLine key={row.id} row={row} nest={nest} />;
        }
      })}
      <div style={{ height: total - offsets[last] }} />
      </BoxedContext.Provider>
      {step !== null && <ClipEditing rows={rows} offsets={offsets} scroller={scroller} />}
      {box && (
        <div className={styles.selectBox} style={{
          left: Math.min(box.x0, box.x1), top: Math.min(box.y0, box.y1),
          width: Math.abs(box.x1 - box.x0), height: Math.abs(box.y1 - box.y0),
        }} />
      )}
    </div>
  );
}

// 一行：上方 3 像素行间隔，左侧轨道头（缩进 15，分组内再缩进 15），右侧时间区。
// 静音时照 Unity 压暗时间区并在中间写明状态。
function Line({ id, height = trackHeight, nest, head, headClass, children, laneClass, selected, muted }: {
  id: string;
  height?: number;
  nest: Nest | undefined;
  head: ReactNode;
  headClass: string;
  children?: ReactNode;
  laneClass?: string;
  selected?: boolean;
  muted?: boolean;
}) {
  const { setNodeRef, rowStyle, rowSidebarStyle } = useRow({ id, disabled: true });
  // 行是块级 flex：拖动库默认的 inline-flex 会落在文字行上，行距多出行高余量，与按 rowHeight 累计的偏移不符。
  return (
    <div className={styles.line} data-nest={nest} data-selected={selected || undefined} data-muted={muted || undefined}
      style={{ display: 'flex', height: height + rowSpacing }}>
      <div className={styles.head} style={rowSidebarStyle}><div className={headClass}>{head}</div></div>
      <div ref={setNodeRef} className={laneClass ?? styles.lane} style={rowStyle} data-lane>
        {children}
        {muted && <span className={styles.stateBox}>已静音</span>}
      </div>
    </div>
  );
}

// 分组标题：折叠三角在分组框内左侧（TimelineTreeView.DrawFoldout），整条标题单击即折叠或展开；
// meta 为淡色的补充信息（如时间范围）。
function GroupToggle({ open, name, meta, title, now, disabled, onToggle, onDoubleClick }: {
  open: boolean; name: string; meta?: string; title?: string; now?: boolean; disabled?: boolean;
  onToggle(): void; onDoubleClick?(): void;
}) {
  return (
    <button className={styles.groupToggle} onClick={onToggle} onDoubleClick={onDoubleClick} aria-expanded={open} disabled={disabled}
      title={title ?? `单击${open ? '折叠' : '展开'}`}>
      <span className={styles.fold}><FoldoutIcon open={open} /></span>
      <span className={styles.groupName} data-now={now || undefined}>{name}</span>
      {meta && <span className={styles.groupMeta}>{meta}</span>}
    </button>
  );
}

// 轨道头（TimelineTrackGUI.DrawTrackHeader）：左侧 4 像素类别色条、16 像素类型图标、轨道名，右侧为按钮组与选项按钮。
// rename 为改名输入框，给出时代替轨道名；below 为展开曲线时轨道头下部的内容（曲线通道列表）。
function TrackHead({ kind, color, selected, name, title, onSelect, onRename, rename, children, below }: {
  kind: TrackKind; color?: string; selected?: boolean; name: string; title?: string;
  onSelect?(): void; onRename?(): void; rename?: ReactNode; children?: ReactNode; below?: ReactNode;
}) {
  const label = <><span className={styles.trackIcon}><TrackIcon kind={kind} /></span><span className={styles.name}>{name}</span></>;
  const head = <>
    {rename ? <><span className={styles.trackIcon}><TrackIcon kind={kind} /></span>{rename}</>
      : onSelect ? <button className={styles.headSelect} title={title} onClick={onSelect} onDoubleClick={onRename}>{label}</button>
        : <span className={styles.headSelect} title={title}>{label}</span>}
    {children}
  </>;
  return (
    <div className={styles.headButton} data-selected={selected || undefined} style={{ ['--tc' as string]: color ?? trackColors[kind] }}>
      {below ? <div className={styles.headColumn}><div className={styles.headRow}>{head}</div>{below}</div> : head}
    </div>
  );
}

const GroupLine = memo(function GroupLine({ row, nest, open, toggle, enter }: {
  row: GroupRow; nest: Nest | undefined; open: boolean; toggle(id: string): void; enter(documentId: string): void;
}) {
  const segment = row.segment;
  // 运行路线上游标所在的段名高亮；技能文档的各 Timeline 都从 0 开始，不分先后。
  const route = useContext(PreviewClockContext).kind === 'archive';
  const now = useWorkbench(state => route && state.frameIndex >= segment.firstIndex && state.frameIndex <= segment.lastIndex);
  const { range } = useTimelineContext();
  return (
    <Line id={row.id} nest={nest} laneClass={styles.groupLane} headClass={styles.groupHead} head={
      <>
        <GroupToggle open={open} now={now} onToggle={() => toggle(row.id)}
          name={segment.timeline?.displayName ?? segment.documentId.slice(0, 8)}
          meta={`${segment.state ? `${segment.state.name} · ` : ''}${segment.start.toFixed(2)}–${segment.end.toFixed(2)} s`}
          title={`单击${open ? '折叠' : '展开'}${row.focused ? '' : '；双击进入这个 Timeline'}`}
          onDoubleClick={row.focused ? undefined : () => enter(segment.documentId)} />
        {segment.timeline && !row.focused && <AddTrack documentId={segment.documentId} />}
      </>
    }>
      {!open && clipUnions(segment).filter(span => span.end >= range.start && span.start <= range.end).map((span, index) => (
        <Positioned key={index} start={span.start} end={span.end} className={styles.clipUnion}
          title={`${span.start.toFixed(3)}–${span.end.toFixed(3)} s`} />
      ))}
    </Line>
  );
});

function useSelected(selectId: string, kind: 'clip' | 'track') {
  return useWorkbench(state => state.selection?.kind === kind && state.selection.id === selectId);
}

// 删除选中的作者片段与标记（选中身份为 `Timeline/元素`）；预览输入不在作者文件里。
function deleteClips(selectIds: string[]) {
  const byDocument = new Map<string, string[]>();
  for (const id of selectIds) {
    if (id.startsWith('input/')) continue;
    const [documentId, itemId] = id.split('/');
    byDocument.set(documentId, [...byDocument.get(documentId) ?? [], itemId]);
  }
  for (const [documentId, ids] of byDocument) authoringActions.deleteClips(documentId, ids);
}

function deleteSelection() {
  const selection = getState().selection;
  if (selection?.id.startsWith('curve/')) {
    runActions.selectCurve(selection.id.split('/').slice(2).join('/'), false);
    selectionActions.select(null);
    return;
  }
  if (selection?.kind === 'clip') deleteClips([selection.id]);
  else if (selection?.kind === 'track' && !selection.id.startsWith('input/')) {
    const [documentId, trackId] = selection.id.split('/');
    authoringActions.deleteTracks(documentId, [trackId]);
  }
}

// 选中的作者片段（不含标记与预览输入）：`Timeline/片段`。
function selectedClip(): { documentId: string; clipId: string } | null {
  const selection = getState().selection;
  if (selection?.kind !== 'clip' || selection.id.startsWith('input/')) return null;
  const [documentId, clipId] = selection.id.split('/');
  return currentClip(documentId, clipId) ? { documentId, clipId } : null;
}

const currentClip = (documentId: string, clipId: string) =>
  applyTimelineEdits(getState().timelines, useAuthoring.getState().edited, useAuthoring.getState().removed).find(item => item.documentId === documentId)
    ?.tracks.flatMap(track => track.clips.map(clip => ({ track, clip }))).find(item => item.clip.id === clipId) ?? null;

// Ctrl+C 复制选中片段；Ctrl+V 粘贴到选中片段所在轨道、紧接其后（选中轨道时接在轨道末尾）。
function copySelection() {
  const picked = selectedClip();
  if (picked) authoringActions.copyClip(picked.documentId, picked.clipId);
  return !!picked;
}

function pasteAfterSelection() {
  const selection = getState().selection;
  if (!selection || selection.id.startsWith('input/') || !authoringActions.canPasteClip()) return false;
  const [documentId, itemId] = selection.id.split('/');
  const timeline = applyTimelineEdits(getState().timelines, useAuthoring.getState().edited, useAuthoring.getState().removed).find(item => item.documentId === documentId);
  const track = selection.kind === 'track' ? timeline?.tracks.find(item => item.id === itemId)
    : timeline?.tracks.find(item => item.clips.some(clip => clip.id === itemId));
  if (!track) return false;
  const after = selection.kind === 'clip' ? track.clips.find(clip => clip.id === itemId)!.endSeconds
    : Math.max(0, ...track.clips.map(clip => clip.endSeconds));
  const id = authoringActions.pasteClip(documentId, track.id, after);
  if (id) selectionActions.select({ kind: 'clip', id: clipSelectId(documentId, id) });
  return true;
}

// 作者模式右键轨道（照 Timeline Editor 轨道与片段菜单）：在点击处添加片段、标记或粘贴，
// 复制与删除所选，轨道改名、静音、排序与删除。时间按帧步长取整。
function useTrackMenu(row: TrackRow, at: { current: number }, rename: () => void): MenuEntry[] {
  const author = useAuthoring(state => state.author && state.target !== null);
  const version = useAuthoring(state => state.version);
  // 右键片段时先选中它，菜单随选中重新给出复制与切分。
  useWorkbench(state => state.selection);
  const snap = useContext(SnapContext);
  const step = snap ?? 1 / 65536;
  const schemes = useInputSchemes();
  const input = useInputContext(row.timeline.documentId);
  const elements = useSkillElements(author);
  const documentId = row.timeline.documentId;
  const templates = useMemo(() => elements ? clipTemplatesFor(elements, documentId, row.track.id) : [],
    // version：轨道与片段的配对随编辑变化。
    [elements, documentId, row.track.id, version]);
  if (!author) return [{ label: '进入作者模式后可编辑', disabled: true, onSelect: () => {} }];
  if (!elements) return [{ label: '读取中', disabled: true, onSelect: () => {} }];
  const local = () => Math.max(0, snapTo(at.current - row.segment.origin, step));
  const select = (id: string) => selectionActions.select({ kind: 'clip', id: clipSelectId(documentId, id) });
  const index = row.timeline.tracks.findIndex(track => track.id === row.track.id);
  const picked = selectedClip();
  // 切分：在点击处切开本轨道上选中的片段（点击位置在打开菜单时才知道，执行时判断）。
  const splitTarget = picked?.documentId === documentId ? row.track.clips.find(clip => clip.id === picked.clipId) : undefined;
  const split = () => {
    const at = local();
    if (!splitTarget || at <= splitTarget.startSeconds || at >= splitTarget.endSeconds) {
      consoleActions.log('warn', 'Timeline', '切分位置要在选中片段内部');
      return;
    }
    const id = authoringActions.splitClip(documentId, splitTarget.id, at);
    if (id) select(id);
  };
  if (schemes.some(scheme => scheme.documentId === documentId)) {
    const entries: MenuEntry[] = input.kind === 'ready' && snap !== null ? input.actor.keys.flatMap<MenuEntry>(key => [
      { kind: 'label' as const, label: key.name },
      ...(['hold', 'press'] as const).map(operation => ({
        label: operation === 'hold' ? '按住' : '单按',
        onSelect: () => {
          const startSeconds = local();
          void inputAuthoring.add(documentId, {
            scene: input.scene.path, sceneRevision: input.scene.revision, actorId: input.actor.actorId,
            key: key.name, operation, startSeconds, endSeconds: startSeconds + 1,
          }, row.track.id).catch(error => consoleActions.error('输入', error));
        },
      })),
    ]) : [{ label: input.kind === 'unavailable' ? input.reason : '尚无实际 Tick 频率', disabled: true, onSelect: () => {} }];
    return [...entries, { kind: 'separator' },
      { label: '复制所选片段', disabled: !picked, onSelect: copySelection },
      { label: '删除所选', onSelect: deleteSelection },
      { label: '重命名轨道', onSelect: rename },
      { label: '删除轨道', onSelect: () => authoringActions.deleteTracks(documentId, [row.track.id]) }];
  }
  return [
    { kind: 'label', label: '在此处添加' },
    ...templates.map((template): MenuEntry => ({
      label: `${template.kind ?? template.clrType} 片段`,
      onSelect: () => {
        const start = local();
        select(authoringActions.addClipAt(documentId, row.track.id, elements, template, start, start + Math.max(step, Math.round(0.5 / step) * step)));
      },
    })),
    { label: '标记', onSelect: () => { const id = authoringActions.addMarkerAt(documentId, row.track.id, elements, local()); if (id) select(id); } },
    {
      label: '粘贴片段', disabled: !authoringActions.canPasteClip(), tip: '粘贴复制的片段，起点在此处',
      onSelect: () => { const id = authoringActions.pasteClip(documentId, row.track.id, local()); if (id) select(id); },
    },
    { kind: 'separator' },
    {
      label: '在此处切分', disabled: !splitTarget, tip: '右键点在选中片段内；动画与位移曲线片段可切分', onSelect: split,
    },
    { label: '复制所选片段（Ctrl+C）', disabled: !picked, onSelect: copySelection },
    { label: '删除所选（Delete）', onSelect: deleteSelection },
    { kind: 'separator' },
    { label: '重命名轨道', onSelect: rename },
    {
      label: row.track.muted ? '取消静音' : '静音',
      onSelect: () => authoringActions.setTrackValue(documentId, row.track.id, ['m_PersistentMuted'], !row.track.muted),
    },
    { label: '上移轨道', disabled: index <= 0, onSelect: () => authoringActions.moveTrack(documentId, row.track.id, index - 1) },
    {
      label: '下移轨道', disabled: index >= row.timeline.tracks.length - 1,
      onSelect: () => authoringActions.moveTrack(documentId, row.track.id, index + 1),
    },
    { label: '删除轨道', onSelect: () => authoringActions.deleteTracks(documentId, [row.track.id]) },
  ];
}

const TrackLine = memo(function TrackLine({ row, nest, toggleCurves }: { row: TrackRow; nest: Nest | undefined; toggleCurves(id: string): void }) {
  const clock = useContext(PreviewClockContext);
  const selected = useSelected(row.selectId, 'track');
  const { range, getValueFromScreenX } = useTimelineContext();
  const at = useRef(0);
  const [naming, setNaming] = useState(false);
  const author = useAuthoring(state => state.author && state.target !== null);
  const menu = useTrackMenu(row, at, () => setNaming(true));
  const documentId = row.timeline.documentId;
  const step = useContext(SnapContext) ?? 1 / 60;
  // 运行秒数换成本 Timeline 的本地秒数，按吸附步长取整。
  const snapLocal = (seconds: number) => Math.max(0, snapTo(seconds - row.segment.origin, step));
  const visible = row.clips.filter(clip => clip.end >= range.start && clip.start <= range.end);
  const markers = row.markers.filter(marker => marker.seconds >= range.start && marker.seconds <= range.end);
  // 照 Clip.UpdateMix 只给可混合的片段算混合：轨道合同为 Blend 的动画与位移曲线轨。
  const elements = useSkillElements(useAuthoring(state => state.target !== null));
  const blend = elements !== null && trackOverlap(elements, documentId, row.track.id) === 'Blend';
  const mixes = useMemo(() => new Map(row.clips.map(clip => [clip.key, blend ? clipMix(clip, row.clips) : noMix])), [row.clips, blend]);
  // 能放带曲线通道片段的轨道可展开曲线（预览输入轨没有）。
  const hasCurves = elements !== null && row.trackKind !== 'in' && trackHasCurves(elements, documentId, row.track.id);
  const curves = hasCurves && row.curves ? row.curves : 0;
  const segment = row.segment;
  const select = () => selectTimelineObject(clock, 'track', row.selectId);
  const muted = row.track.muted;
  // 轨道头的选项按钮（⋮）与右键菜单相同，在游标处添加；打开前选中这条轨道。
  const openOptions = () => {
    select();
    const seconds = previewSeconds(getState(), clock, authorCursorOf(useAuthorCursor.getState(), clock));
    at.current = seconds === null ? segment.origin : clock.kind === 'archive' ? seconds : segment.origin + seconds;
  };
  return (
    <ContextMenu entries={menu}>
    <div className={styles.menuArea} onContextMenuCapture={event => {
      at.current = getValueFromScreenX(event.clientX);
      // 右键片段或标记先选中它，菜单的复制、删除作用于它；右键轨道头选中轨道。
      const item = (event.target as HTMLElement).closest<HTMLElement>('[data-select]');
      if (item?.dataset.select) selectTimelineObject(clock, 'clip', item.dataset.select);
      else if ((event.target as HTMLElement).closest(`.${styles.trackHead}`)) select();
    }}>
    <Line id={row.id} nest={nest} selected={selected} muted={muted} headClass={styles.trackHead}
      height={trackHeight + curves} head={
      <TrackHead kind={row.trackKind} selected={selected} name={row.track.displayName} onSelect={select}
        below={curves && elements ? <CurveParameters row={row} elements={elements} mixes={mixes} author={author} height={curves} /> : undefined}
        onRename={author ? () => setNaming(true) : undefined} title={author ? '单击选中 · 双击改名 · 右键打开轨道菜单' : undefined}
        rename={naming ? (
          <TextInput autoFocus defaultValue={row.track.displayName} aria-label="轨道名称" className={styles.nameInput}
            onBlur={event => {
              setNaming(false);
              const name = event.currentTarget.value.trim();
              if (name && name !== row.track.displayName) authoringActions.setTrackValue(documentId, row.track.id, ['Name'], name);
            }}
            onKeyDown={event => {
              event.stopPropagation();
              if (event.key === 'Enter') event.currentTarget.blur();
              else if (event.key === 'Escape') { event.currentTarget.value = row.track.displayName; event.currentTarget.blur(); }
            }} />
        ) : undefined}>
        {(author || hasCurves) && <span className={styles.suite}>
          {hasCurves && <button className={styles.suiteButton} aria-pressed={curves > 0} title={curves ? '收起片段曲线' : '展开片段曲线'}
            onClick={() => toggleCurves(row.id)}>
            <CurveIcon />
          </button>}
          {author && <button className={styles.suiteButton} aria-pressed={muted} title={muted ? '取消静音' : '静音'}
            onClick={() => authoringActions.setTrackValue(documentId, row.track.id, ['m_PersistentMuted'], !muted)}>
            <MuteIcon muted={muted} />
          </button>}
        </span>}
        {author && <span className={styles.options} onPointerDown={openOptions}>
          <MenuButton label="轨道菜单" variant="ghost" className={styles.optionsButton} iconOnly entries={menu}><OptionsIcon /></MenuButton>
        </span>}
      </TrackHead>
    }>
      {visible.map(clip => <Clip key={clip.key} clip={clip} kind={row.trackKind} mix={mixes.get(clip.key)!}
        documentId={row.timeline.documentId} segmentId={segment.id} trackId={row.track.id} />)}
      {curves > 0 && elements && <ClipCurves row={row} elements={elements} mixes={mixes} author={author} height={curves} />}
      {markers.map(marker => {
        const markerId = marker.selectId.split('/')[1];
        return (
          <Marker key={marker.key} seconds={marker.seconds} kind={row.trackKind} label={`${row.track.displayName} 标记`} text={marker.label} graphId={marker.graphId}
            selectId={marker.selectId}
            move={author ? seconds => authoringActions.setMarkerTime(documentId, markerId, snapLocal(seconds)) : undefined}
            snap={author ? seconds => segment.origin + snapLocal(seconds) : undefined}
            taken={author ? seconds => markerTimeTaken(documentId, row.track.id, snapLocal(seconds), markerId) : undefined} />
        );
      })}
    </Line>
    </div>
    </ContextMenu>
  );
});

// 片段两端与同轨片段的混合（Mix）秒数。
interface ClipMix { in: number; out: number }
const noMix: ClipMix = { in: 0, out: 0 };

// 通道曲线在时间轴上的区间（照 TimelineRuntimeClipSampler.Weight）：Weight 覆盖整段；Ease In / Ease Out 只在
// 生效的缓动区间内取值，有混合时为混合秒数，否则为自身淡入淡出（不超过片段长减去另一端的混合）。
function channelSpan(role: ClipCurveChannel['role'], clip: ClipInstance, mix: ClipMix): Span | null {
  const duration = clip.end - clip.start;
  if (role === 'Weight') return { start: clip.start, end: clip.end };
  const ease = role === 'EaseIn'
    ? mix.in > 0 ? mix.in : Math.min(clip.clip.easeInSeconds ?? 0, duration - mix.out)
    : mix.out > 0 ? mix.out : Math.min(clip.clip.easeOutSeconds ?? 0, duration - mix.in);
  if (!(ease > 0)) return null;
  return role === 'EaseIn' ? { start: clip.start, end: clip.start + ease } : { start: clip.end - ease, end: clip.end };
}

const roundCurve = (value: number) => Math.round(value * 1e4) / 1e4;
const clamp01 = (value: number) => Math.max(0, Math.min(1, value));
const curveDiamond = (x: number, y: number, r: number) => `M${x} ${y - r}L${x + r} ${y}L${x} ${y + r}L${x - r} ${y}Z`;
const curveKeys = (curve: JsonObject | null) => curve ? curve.keys as JsonObject[] : [];
// 参考曲线照 Unity 曲线编辑器按分量序号着色：x 红、y 绿、z 蓝，第四条黄。
const referenceColors = ['#e86a6a', '#7cc96f', '#5f9df0', '#e3c04f'];

// 片段的一个曲线参数（照 BtsmtlTimelineParameterBinding），在 span 上按归一化时间 0–1 取值。
// 通道参数是片段自身的正式曲线通道，可编辑；参考参数是所引用源资产的曲线（BtsmtlTimelineReferenceParameterBinding），
// 只读，片段时间 τ 取源时间 源起点 + τ，超过源终点保持终值（照 MotionCurveClip.UpdateSourceDisplayCurve）。
interface CurveParameter {
  id: string; name: string; color: string; span: Span | null;
  times: number[]; value(t: number): number;
  channel: ClipCurveChannel | null; curve: JsonObject | null; reference: string | null;
}

function clipParameters(elements: SkillElements, documentId: string, clip: ClipInstance, mix: ClipMix,
  source: ReturnType<typeof clipReferenceSource>, entry: ReferenceEntry | undefined): CurveParameter[] {
  const channels = clipCurves(elements, documentId, clip.clip.id).map(({ channel, curve }): CurveParameter => ({
    id: channel.member, name: channel.name, color: channel.color, span: channelSpan(channel.role, clip, mix),
    times: curveKeys(curve).map(key => numberOf(key.time)), value: t => curve ? evaluateCurve(curve, t) : 0,
    channel, curve, reference: null,
  }));
  if (!source || !entry || 'failed' in entry) return channels;
  const length = clip.end - clip.start;
  const toSource = (t: number) => Math.min(source.sourceStart + t * length, source.sourceEnd);
  return [...channels, ...entry.curves.map((item, index): CurveParameter => {
    const curve = { keys: item.keys } as unknown as JsonObject;
    return {
      id: item.id, name: item.name, color: referenceColors[index % referenceColors.length], span: { start: clip.start, end: clip.end },
      times: item.keys.filter(key => key.time >= source.sourceStart && key.time <= source.sourceEnd)
        .map(key => (key.time - source.sourceStart) / length),
      value: t => evaluateCurve(curve, toSource(t)), channel: null, curve: null, reference: entry.label,
    };
  })];
}

// 展开曲线的轨道只看本轨选中的片段（照 IEmbeddedTimelineTrackBinding.SelectedClip）。
function useCurveClip(row: TrackRow, elements: SkillElements, mixes: Map<string, ClipMix>) {
  useAuthoring(state => state.version);
  const selection = useWorkbench(state => state.selection?.kind === 'clip' ? state.selection.id : null);
  const view = useContext(CurveViewContext);
  const clip = row.clips.find(item => item.selectId === selection) ?? null;
  const source = clip ? clipReferenceSource(elements, row.timeline.documentId, clip.clip.id) : null;
  const entry = useReferenceCurves(state => source ? state[source.identity] : undefined);
  useEffect(() => { if (source) requestReferenceCurves(source.identity); }, [source?.identity]);
  const parameters = clip ? clipParameters(elements, row.timeline.documentId, clip, mixes.get(clip.key) ?? noMix, source, entry) : [];
  const inspected = view.inspected?.track === row.id ? parameters.find(item => item.id === view.inspected!.parameter) ?? null : null;
  const failure = entry && 'failed' in entry ? entry.failed : null;
  return { clip, parameters, inspected, view, failure };
}

// 展开曲线的轨道头下部（照 TrackEditorGUI.DrawParametersInfoGUI）：本轨选中片段的各曲线参数，单击查看完整曲线，再次单击收起。
// 查看时下方是上一关键帧、在游标处加或删关键帧（参考曲线只读）、下一关键帧与游标处的值；拖动底边加高曲线区。
function CurveParameters({ row, elements, mixes, author, height }: {
  row: TrackRow; elements: SkillElements; mixes: Map<string, ClipMix>; author: boolean; height: number;
}) {
  const clock = useContext(PreviewClockContext);
  const tick = useContext(SnapContext) ?? 1 / 60;
  const { clip, parameters, inspected, view, failure } = useCurveClip(row, elements, mixes);
  const cursor = usePreviewSelect(clock, seconds => seconds === null ? null : clock.kind === 'archive' ? seconds : row.segment.origin + seconds);
  const drag = useRef<{ y: number; height: number } | null>(null);
  if (!clip) return <div className={styles.curveParams}><span className={styles.curveNote}>未选中本轨片段</span></div>;
  const write = (channel: ClipCurveChannel, curve: JsonObject) =>
    authoringActions.setClipValue(row.timeline.documentId, clip.clip.id, [channel.member], curve);
  let controls: ReactNode = null;
  if (inspected) {
    const { span, curve, channel } = inspected;
    if (channel && !curve) controls = <div className={styles.curveKeys}>{author
      ? <Button variant="ghost" onClick={() => write(channel, defaultChannelCurve(channel))}>按默认直线创建</Button>
      : <span className={styles.curveNote}>未设置，运行时不起作用</span>}</div>;
    else if (!span) controls = <div className={styles.curveKeys}><span className={styles.curveNote}>片段没有淡入淡出或混合，不取值</span></div>;
    else {
      // 游标在参数区间里的归一化时间；离关键帧不到半个 tick 视为在关键帧上。
      const length = span.end - span.start;
      const t = cursor === null ? null : (cursor - span.start) / length;
      const tolerance = tick / 2 / length;
      const atKey = t !== null && inspected.times.some(time => Math.abs(time - t) <= tolerance);
      const previous = t === null ? undefined : inspected.times.filter(time => time < t - tolerance).at(-1);
      const next = t === null ? undefined : inspected.times.find(time => time > t + tolerance);
      const jump = (time: number) => view.seek(span.start + time * length);
      const inside = t !== null && t >= -tolerance && t <= 1 + tolerance;
      // 单选一个关键帧时可直接输入它的时间（参数区间内的秒数）与值（照 Unity Edit Key）。
      const single = author && channel && curve && view.keys.length === 1 ? curveKeys(curve)[view.keys[0]] : undefined;
      const place = (time: number, value: number) => {
        const result = moveKeys(curve!, view.keys, () => [roundCurve(clamp01(time)), roundCurve(Math.max(channel!.min, Math.min(channel!.max, value)))]);
        if (!result) return;
        write(channel!, result.curve);
        view.selectKeys(result.indices);
      };
      controls = <>
        <div className={styles.curveKeys}>
          <button className={styles.curveKeyButton} disabled={previous === undefined} title="上一关键帧" onClick={() => jump(previous!)}>
            <KeyIcon kind="previous" />
          </button>
          <button className={styles.curveKeyButton} data-on={atKey || undefined} disabled={!author || !inside || !channel}
            title={!channel ? `参考曲线只读（${inspected.reference}）` : atKey ? '删除游标处的关键帧' : '在游标处添加关键帧'}
            onClick={() => {
              view.selectKeys([]);
              write(channel!, atKey ? curveWithoutKey(curve!, t!, tolerance) : curveWithKey(curve!, clamp01(t!)));
            }}>
            <KeyIcon kind="key" />
          </button>
          <button className={styles.curveKeyButton} disabled={next === undefined} title="下一关键帧" onClick={() => jump(next!)}>
            <KeyIcon kind="next" />
          </button>
          <span className={styles.curveValue} title="游标处的值">{t === null ? '—' : inspected.value(clamp01(t)).toFixed(3)}</span>
        </div>
        {single && <div className={styles.curveKeys}>
          <span className={styles.curveKeyLabel}>时间</span>
          <NumberInput label="关键帧时间（秒）" value={roundCurve(keyTime(single) * length)} min={0} max={length} step={tick}
            onChange={value => { if (value !== null) place(value / length, keyValue(single)); }} />
          <span className={styles.curveKeyLabel}>值</span>
          <NumberInput label="关键帧值" value={keyValue(single)} min={channel!.min} max={channel!.max} step={0.01}
            onChange={value => { if (value !== null) place(keyTime(single), value); }} />
        </div>}
      </>;
    }
  }
  return (
    <div className={styles.curveParams}>
      {parameters.map(parameter => (
        <button key={parameter.id} className={styles.curveParam} aria-pressed={parameter === inspected}
          style={{ ['--cc' as string]: parameter.color }}
          title={`${parameter.name}${parameter.reference ? `（参考：${parameter.reference}，只读）` : ''} · ${parameter === inspected ? '单击收起' : '单击查看完整曲线'}`}
          onClick={() => view.inspect(row.id, parameter === inspected ? null : parameter.id)}>
          <span className={styles.curveParamName}>{parameter.name}</span>
          {parameter.reference ? <small>参考</small> : !parameter.curve ? <small>未设置</small> : !parameter.span ? <small>无缓动</small> : null}
        </button>
      ))}
      {failure && <span className={styles.curveNote} title={failure}>参考曲线读取失败</span>}
      {inspected && controls}
      {inspected && <div className={styles.curveResize} title="拖动改变曲线区高度"
        onPointerDown={event => {
          if (event.button !== 0) return;
          event.currentTarget.setPointerCapture(event.pointerId);
          drag.current = { y: event.clientY, height };
        }}
        onPointerMove={event => { if (drag.current) view.resize(row.id, Math.min(curveMaxHeight, drag.current.height + event.clientY - drag.current.y)); }}
        onPointerUp={() => { drag.current = null; }} onPointerCancel={() => { drag.current = null; }} />}
    </div>
  );
}

// 纵向范围 min–max 含留白；low–high 为曲线实际取值的上下端。
interface CurveScale { min: number; max: number; low: number; high: number }
// 曲线编辑的拖动草稿：移动所选关键帧、拖切线手柄（预览曲线与原曲线、纵向范围冻结），或框选关键帧（曲线区像素坐标）。
type CurveDraft =
  | { kind: 'move'; from: { t: number; v: number }; base: JsonObject; preview: JsonObject; indices: number[]; moved: number[]; scale: CurveScale }
  | { kind: 'tangent'; index: number; side: TangentSide; base: JsonObject; preview: JsonObject; scale: CurveScale }
  | { kind: 'box'; x0: number; y0: number; x1: number; y1: number; add: boolean };
const tangentNames: Record<TangentKind, string> = { free: 'Free', linear: 'Linear', constant: 'Constant' };
const tangentHandle = 36;

// 曲线的纵向范围（照 Slate FrameAllCurvesOf）：曲线实际取值（通道参数并上通道取值域），上下留白。
function curveScale(parameter: CurveParameter): CurveScale {
  let low = Infinity, high = -Infinity;
  for (const t of [...Array.from({ length: 97 }, (_, index) => index / 96), ...parameter.times.map(clamp01)]) {
    const value = parameter.value(t);
    low = Math.min(low, value);
    high = Math.max(high, value);
  }
  let min = Math.min(low, parameter.channel?.min ?? low), max = Math.max(high, parameter.channel?.max ?? high);
  if (!(max > min)) { min -= 0.5; max += 0.5; }
  const pad = (max - min) * 0.08;
  return { min: min - pad, max: max + pad, low, high };
}

const scaleLabel = (value: number) => Number(value.toPrecision(4)).toString();

// 片段下方的参数区（照 TrackEditorGUI.DrawClipCurves）：只在选中片段的时间范围内画。未查看参数时每个参数一行，
// 菱形为关键帧，单击一行查看该参数；查看时上方一行关键帧，下面是完整曲线。
// 作者模式下通道参数照 Unity 曲线编辑器编辑：单击选中、Shift 加选、空白处拖动框选，拖动所选关键帧改时间与值
// （时间按 tick 吸附并限在 0–1，值限在通道取值域，取到万分位），选中关键帧显示切线手柄，双击空白处添加关键帧，
// 右键关键帧删除或设切线模式，Delete 删除所选；参考曲线只读。
function ClipCurves({ row, elements, mixes, author, height }: {
  row: TrackRow; elements: SkillElements; mixes: Map<string, ClipMix>; author: boolean; height: number;
}) {
  const { range, valueToPixels, getValueFromScreenX } = useTimelineContext();
  const step = useContext(SnapContext);
  const { clip, parameters, inspected, view } = useCurveClip(row, elements, mixes);
  const [draft, setDraft] = useState<CurveDraft | null>(null);
  const [menu, setMenu] = useState<{ key: number | null; t: number }>({ key: null, t: 0 });
  const svg = useRef<SVGSVGElement>(null);
  const inspecting = inspected !== null;
  useEffect(() => { if (inspecting) view.selectKeys([]); }, [clip?.key]);
  const x = (seconds: number) => valueToPixels(seconds - range.start);
  if (!clip) return (
    <div className={styles.clipCurves} style={{ top: trackHeight, height }}>
      <span className={styles.curveHint}>选中本轨片段后在这里查看它的曲线</span>
    </div>
  );
  const left = x(clip.start), width = Math.max(1, x(clip.end) - left);
  const spanX = (span: Span, t: number) => x(span.start + t * (span.end - span.start));

  if (!inspected) return (
    <svg className={styles.clipCurves} style={{ top: trackHeight, height }}>
      <rect className={styles.curveClipArea} x={left} y={1} width={width} height={height - 4} />
      {parameters.map((parameter, index) => {
        const y = curveParamTop + index * curveParamPitch;
        const span = parameter.span;
        return (
          <g key={parameter.id} className={styles.curveRow} style={{ ['--cc' as string]: parameter.color }}
            data-unset={(parameter.channel && !parameter.curve) || !span || undefined}
            onPointerDown={event => { if (event.button === 0) view.inspect(row.id, parameter.id); }}>
            <title>{`${parameter.name} · 单击查看完整曲线`}</title>
            <rect className={styles.curveRowBand} x={left} y={y} width={width} height={curveParamHeight} />
            {span && parameter.times.map((time, key) => (
              <path key={key} className={styles.curveDope} d={curveDiamond(spanX(span, clamp01(time)), y + curveParamHeight / 2, 4)} />
            ))}
          </g>
        );
      })}
    </svg>
  );

  const { span, curve, channel } = inspected;
  const plotTop = 26, plotBottom = height - 6;
  const scale = draft && draft.kind !== 'box' ? draft.scale : curveScale(inspected);
  const pxPerV = (plotBottom - plotTop) / (scale.max - scale.min);
  const y = (value: number) => plotTop + (scale.max - value) * pxPerV;
  const editable = author && channel !== null && curve !== null && span !== null;
  const shown = draft && draft.kind !== 'box' ? draft.preview : curve;
  const keys = curveKeys(shown);
  const picked = draft?.kind === 'move' ? draft.moved : view.keys.filter(index => index < keys.length);
  const valueAt = shown ? (t: number) => evaluateCurve(shown, t) : inspected.value;
  const length = span ? span.end - span.start : 1;
  const pxPerT = span ? spanX(span, 1) - spanX(span, 0) : 1;
  const snapT = (t: number) => clamp01(step ? Math.round(t * length / step) * step / length : t);
  const clampV = (v: number) => channel ? Math.max(channel.min, Math.min(channel.max, v)) : v;
  const at = (event: { clientX: number; clientY: number }) => {
    const box = svg.current!.getBoundingClientRect();
    const px = event.clientX - box.left, py = event.clientY - box.top;
    return { px, py, t: span ? (getValueFromScreenX(event.clientX) - span.start) / length : 0, v: scale.max - (py - plotTop) / pxPerV };
  };
  const write = (next: JsonObject) => authoringActions.setClipValue(row.timeline.documentId, clip.clip.id, [channel!.member], next);
  const commit = (result: { curve: JsonObject; indices: number[] } | null) => {
    if (!result) return;
    write(result.curve);
    view.selectKeys(result.indices);
  };
  const addAt = (t: number) => {
    if (!curve || t < 0 || t > 1) return;
    const time = roundCurve(snapT(t));
    if (keys.some(key => Math.abs(keyTime(key) - time) < 1e-6)) return;
    const next = curveWithKey(curve, time);
    write(next);
    view.selectKeys([curveKeys(next).findIndex(key => keyTime(key) === time)]);
  };
  const remove = (indices: number[]) => {
    if (!curve || !indices.length) return;
    write(removeKeys(curve, indices));
    view.selectKeys([]);
  };
  const capture = (event: PointerEvent<Element>) => {
    event.stopPropagation();
    svg.current!.focus();
    svg.current!.setPointerCapture(event.pointerId);
  };
  const down = (event: PointerEvent<SVGSVGElement>) => {
    if (event.button !== 0) return;
    event.stopPropagation();
    if (!editable) return;
    capture(event);
    const p = at(event);
    setDraft({ kind: 'box', x0: p.px, y0: p.py, x1: p.px, y1: p.py, add: event.shiftKey });
  };
  const grabKey = (event: PointerEvent<SVGElement>, index: number) => {
    if (event.button !== 0 || !editable) return;
    capture(event);
    if (event.shiftKey) {
      view.selectKeys(picked.includes(index) ? picked.filter(item => item !== index) : [...picked, index]);
      return;
    }
    const indices = picked.includes(index) ? picked : [index];
    view.selectKeys(indices);
    const p = at(event);
    setDraft({ kind: 'move', from: { t: p.t, v: p.v }, base: curve!, preview: curve!, indices, moved: indices, scale });
  };
  const grabTangent = (event: PointerEvent<SVGElement>, index: number, side: TangentSide) => {
    if (event.button !== 0) return;
    capture(event);
    setDraft({ kind: 'tangent', index, side, base: curve!, preview: curve!, scale });
  };
  const move = (event: PointerEvent<SVGSVGElement>) => {
    if (!draft) return;
    const p = at(event);
    if (draft.kind === 'box') { setDraft({ ...draft, x1: p.px, y1: p.py }); return; }
    if (!span) return;
    if (draft.kind === 'move') {
      const dt = p.t - draft.from.t, dv = p.v - draft.from.v;
      const result = moveKeys(draft.base, draft.indices, (t, v) => [roundCurve(snapT(t + dt)), roundCurve(clampV(v + dv))]);
      if (result) setDraft({ ...draft, preview: result.curve, moved: result.indices });
      return;
    }
    // 切线为手柄方向在曲线坐标里的斜率（值 / 归一化时间）；左手柄只取向左、右手柄只取向右。
    const key = curveKeys(draft.base)[draft.index];
    const dx = (p.px - spanX(span, keyTime(key))) / pxPerT;
    const run = draft.side === 'right' ? Math.max(dx, 1e-4) : Math.min(dx, -1e-4);
    const rise = (y(keyValue(key)) - p.py) / pxPerV;
    setDraft({ ...draft, preview: setTangent(draft.base, draft.index, draft.side, roundCurve(rise / run)).curve });
  };
  const release = () => {
    if (!draft) return;
    setDraft(null);
    if (draft.kind === 'box') {
      const [x0, x1] = [Math.min(draft.x0, draft.x1), Math.max(draft.x0, draft.x1)];
      const [y0, y1] = [Math.min(draft.y0, draft.y1), Math.max(draft.y0, draft.y1)];
      const inside = span && x1 - x0 + y1 - y0 > 3 ? keys.flatMap((key, index) => {
        const kx = spanX(span, keyTime(key)), ky = y(keyValue(key));
        return kx >= x0 && kx <= x1 && ky >= y0 && ky <= y1 ? [index] : [];
      }) : [];
      view.selectKeys(draft.add ? [...new Set([...picked, ...inside])] : inside);
      return;
    }
    if (draft.preview === draft.base) return;
    write(draft.preview);
    view.selectKeys(draft.kind === 'move' ? draft.moved : [draft.index]);
  };
  const keyDown = (event: KeyboardEvent<SVGSVGElement>) => {
    if ((event.key !== 'Delete' && event.key !== 'Backspace') || !editable || !picked.length) return;
    event.preventDefault();
    event.stopPropagation();
    remove(picked);
  };
  // 右键关键帧时先选中它（已在所选中则作用于全部所选），菜单随所选给出。
  const openMenu = (event: ReactMouseEvent<SVGSVGElement>) => {
    event.stopPropagation();
    const hit = (event.target as Element).closest('[data-key]');
    const key = hit ? Number(hit.getAttribute('data-key')) : null;
    if (key !== null && !picked.includes(key)) view.selectKeys([key]);
    setMenu({ key, t: at(event).t });
  };
  const menuEntries = (): MenuEntry[] => {
    const note = (label: string): MenuEntry[] => [{ label, disabled: true, onSelect: () => {} }];
    if (!author) return note('进入作者模式后可编辑');
    if (!channel) return note(`参考曲线只读（${inspected.reference}）`);
    if (!editable) return note('这条曲线现在不取值，不能编辑');
    const add: MenuEntry = { label: '在此处添加关键帧', disabled: menu.t < 0 || menu.t > 1, onSelect: () => addAt(menu.t) };
    if (menu.key === null) return [add];
    const targets = picked.includes(menu.key) ? picked : [menu.key];
    const items = targets.map(index => keys[index]).filter(Boolean);
    const all = (test: (key: JsonObject) => boolean) => items.length > 0 && items.every(test);
    const sideEntries = (label: string, sides: TangentSide[]): MenuEntry[] => [
      { kind: 'label', label },
      ...(['free', 'linear', 'constant'] as const).map((kind): MenuEntry => ({
        kind: 'radio', label: tangentNames[kind], checked: all(key => sides.every(side => tangentKind(key, side) === kind)),
        onSelect: () => commit(setTangentKind(curve!, targets, sides, kind)),
      })),
    ];
    return [
      { label: targets.length > 1 ? `删除 ${targets.length} 个关键帧` : '删除关键帧', onSelect: () => remove(targets) },
      { kind: 'separator' },
      {
        kind: 'radio', label: 'Free Smooth', onSelect: () => commit(smoothKeys(curve!, targets, false)),
        checked: all(key => !isBroken(key) && tangentKind(key, 'left') === 'free' && tangentKind(key, 'right') === 'free'),
      },
      { label: 'Flat', onSelect: () => commit(smoothKeys(curve!, targets, true)) },
      { kind: 'check', label: 'Broken', checked: all(isBroken), onChange: checked => commit(breakKeys(curve!, targets, checked)) },
      { kind: 'separator' },
      ...sideEntries('左切线', ['left']), ...sideEntries('右切线', ['right']), ...sideEntries('两侧切线', ['left', 'right']),
    ];
  };
  const samples = span ? Math.max(16, Math.min(240, Math.round(pxPerT / 3))) : 0;
  const points = span && (curve || !channel) ? Array.from({ length: samples + 1 }, (_, index) => index / samples)
    .map(t => `${spanX(span, t)},${y(valueAt(t))}`).join(' ') : null;
  // 参考线：通道参数为取值域两端，参考曲线为实际取值的上下端。
  const guides = channel ? [channel.max, channel.min] : scale.high > scale.low ? [scale.high, scale.low] : [scale.high];
  const times = editable ? keys.map(keyTime) : inspected.times;
  // 所选关键帧的切线手柄：沿切线方向固定长度；首帧没有左手柄、末帧没有右手柄，阶跃（无穷）切线不画。
  const handles = editable ? picked.flatMap(index => {
    const key = keys[index];
    const kx = spanX(span, keyTime(key)), ky = y(keyValue(key));
    return (['left', 'right'] as const).filter(side => side === 'left' ? index > 0 : index < keys.length - 1).flatMap(side => {
      const slope = tangentOf(key, side);
      if (!Number.isFinite(slope)) return [];
      const dy = -slope * pxPerV, norm = Math.hypot(pxPerT, dy) || 1, sign = side === 'left' ? -1 : 1;
      return [{ index, side, kx, ky, hx: kx + sign * pxPerT / norm * tangentHandle, hy: ky + sign * dy / norm * tangentHandle, slope }];
    });
  }) : [];
  return (
    <ContextMenu entries={menuEntries()}>
      <svg ref={svg} className={styles.clipCurves} tabIndex={-1} style={{ top: trackHeight, height, ['--cc' as string]: inspected.color }}
        onPointerDown={down} onPointerMove={move} onPointerUp={release} onPointerCancel={() => setDraft(null)}
        onDoubleClick={event => { if (editable && !(event.target as Element).closest('[data-key], [data-handle]')) addAt(at(event).t); }}
        onKeyDown={keyDown} onContextMenu={openMenu}>
        <rect className={styles.curveClipArea} x={left} y={1} width={width} height={height - 4} />
        <rect className={styles.curveRowBand} x={left} y={4} width={width} height={16} />
        {span && times.map((time, key) => <path key={key} className={styles.curveDope} data-active={(editable && picked.includes(key)) || undefined}
          d={curveDiamond(spanX(span, clamp01(time)), 12, 4)} />)}
        <line className={styles.curvePlotEdge} x1={left} x2={left + width} y1={plotTop - 3} y2={plotTop - 3} />
        {guides.map((value, index) => (
          <g key={index}>
            <line className={styles.curveGuide} x1={left} x2={left + width} y1={y(value)} y2={y(value)} />
            <text className={styles.curveScaleText} x={left + 4} y={y(value) - 3}>{scaleLabel(value)}</text>
          </g>
        ))}
        {points && <polyline className={styles.curvePlotLine} points={points} />}
        {handles.map(handle => (
          <g key={`${handle.index}-${handle.side}`} className={styles.curveHandle} data-handle="">
            <line x1={handle.kx} y1={handle.ky} x2={handle.hx} y2={handle.hy} />
            <circle cx={handle.hx} cy={handle.hy} r={3} onPointerDown={event => grabTangent(event, handle.index, handle.side)}>
              <title>{`${handle.side === 'left' ? '左' : '右'}切线 ${handle.slope.toFixed(3)} · 拖动修改`}</title>
            </circle>
          </g>
        ))}
        {editable && keys.map((key, index) => (
          <circle key={index} data-key={index} className={styles.curvePlotKey} cx={spanX(span, clamp01(keyTime(key)))} cy={y(keyValue(key))} r={3.5}
            data-active={picked.includes(index) || undefined} onPointerDown={event => grabKey(event, index)}>
            <title>{`${inspected.name} 关键帧 ${index} · ${(keyTime(key) * length).toFixed(3)} s, ${keyValue(key).toFixed(3)} · 拖动修改，右键设切线`}</title>
          </circle>
        ))}
        {draft?.kind === 'box' && <rect className={styles.curveBox} x={Math.min(draft.x0, draft.x1)} y={Math.min(draft.y0, draft.y1)}
          width={Math.abs(draft.x1 - draft.x0)} height={Math.abs(draft.y1 - draft.y0)} />}
        {editable && <text className={styles.curveScaleText} x={left + width - 4} y={plotTop + 10} textAnchor="end">
          双击添加 · 拖动框选 · 右键切线 · Delete 删除
        </text>}
        {inspected.reference && <text className={styles.curveScaleText} x={left + width - 4} y={plotBottom - 2} textAnchor="end">
          参考：{inspected.reference}（只读）
        </text>}
        {channel && !curve ? <text className={styles.curveScaleText} x={left + 4} y={plotTop + 12}>未设置，运行时不起作用</text>
          : !span ? <text className={styles.curveScaleText} x={left + 4} y={plotTop + 12}>片段没有淡入淡出或混合，这条缓动曲线不取值</text> : null}
      </svg>
    </ContextMenu>
  );
}

function Positioned({ start, end, className, children, style, title }: {
  start: number; end: number; className: string; children?: ReactNode; style?: CSSProperties; title?: string;
}) {
  const { range, valueToPixels } = useTimelineContext();
  return (
    <div className={className} title={title}
      style={{ ...style, left: valueToPixels(start - range.start), width: Math.max(1, valueToPixels(end - start)) }}>
      {children}
    </div>
  );
}

const Clip = memo(function Clip({ clip, kind, mix, documentId, segmentId, trackId }: {
  clip: ClipInstance; kind: TrackKind; mix: ClipMix; documentId: string; segmentId: string; trackId: string;
}) {
  const author = useAuthoring(state => state.author && state.target !== null);
  const edit = useMemo<ClipEdit>(() => ({ documentId, segmentId, trackId, clipId: clip.clip.id, origin: clip.start - clip.clip.startSeconds }),
    [documentId, segmentId, trackId, clip]);
  // 淡入淡出只在打开的技能 JSON 中有值（运行回执不带）。
  const ease = useMemo(() => author && clip.clip.easeInSeconds !== undefined ? {
    in: clip.clip.easeInSeconds, out: clip.clip.easeOutSeconds ?? 0,
    set: (easeIn: number | null, easeOut: number | null) => authoringActions.setClipEase(documentId, clip.clip.id, easeIn, easeOut),
  } : undefined, [author, clip, documentId]);
  return (
    <ClipBox boxKey={clip.key} selectId={clip.selectId} kind={kind} label={clip.label} start={clip.start} end={clip.end}
      exit={clip.exit} graphId={clip.clip.graphId} edit={author ? edit : undefined} ease={ease} mix={mix} />
  );
});

// 片段拖动写回作者 JSON 所需的位置：所属 Timeline、所在路线段、轨道、片段身份，以及本段的运行秒数零点。
interface ClipEdit { documentId: string; segmentId: string; trackId: string; clipId: string; origin: number }

interface ClipEase { in: number; out: number; set(easeIn: number | null, easeOut: number | null): void }
type EaseDraft = { side: 'in' | 'out'; seconds: number };
// 片段一端的过渡：与相邻片段重叠为混合，否则有淡入淡出时为淡变（BlendKind）；width 为像素宽。
type ClipBlend = { kind: 'mix' | 'ease'; width: number } | null;

// 淡入淡出手柄（作者模式）：顶边两端的小方块左右拖动改时长，按片段网格的步长取整（时长不带偏移）；悬停或选中片段时显示。
// 拖动中的草稿由片段持有，片段按草稿画淡变形状；已与相邻片段混合的一端没有手柄。
function EaseHandles({ ease, start, end, draft, setDraft, sides, step }: {
  ease: ClipEase; start: number; end: number; draft: EaseDraft | null; setDraft(draft: EaseDraft | null): void;
  sides: { in: boolean; out: boolean }; step: number;
}) {
  const { valueToPixels, getValueFromScreenX } = useTimelineContext();
  const easeIn = draft?.side === 'in' ? draft.seconds : ease.in;
  const easeOut = draft?.side === 'out' ? draft.seconds : ease.out;
  const width = valueToPixels(end - start);
  const inPx = valueToPixels(easeIn), outPx = valueToPixels(easeOut);
  const handle = (side: 'in' | 'out') => ({
    onPointerDown: (event: PointerEvent<HTMLSpanElement>) => {
      if (event.button !== 0) return;
      event.stopPropagation();
      event.currentTarget.setPointerCapture(event.pointerId);
      setDraft({ side, seconds: side === 'in' ? ease.in : ease.out });
    },
    onPointerMove: (event: PointerEvent<HTMLSpanElement>) => {
      if (draft?.side !== side) return;
      const at = getValueFromScreenX(event.clientX);
      const other = side === 'in' ? ease.out : ease.in;
      const seconds = snapTo(side === 'in' ? at - start : end - at, step);
      setDraft({ side, seconds: Math.max(0, Math.min(end - start - other, seconds)) });
    },
    onPointerUp: () => {
      if (draft?.side === side && draft.seconds !== (side === 'in' ? ease.in : ease.out))
        ease.set(side === 'in' ? draft.seconds : null, side === 'out' ? draft.seconds : null);
      setDraft(null);
    },
    onClick: (event: ReactMouseEvent) => event.stopPropagation(),
  });
  // 方块中心对准淡入终点、淡出起点，贴边时收在片段内。
  return (
    <>
      {sides.in && <span className={styles.easeHandle} style={{ left: Math.max(0, Math.min(width - 7, inPx - 3.5)) }}
        title={`淡入 ${easeIn.toFixed(3)} s · 左右拖动`} {...handle('in')} />}
      {sides.out && <span className={styles.easeHandle} style={{ left: Math.max(0, Math.min(width - 7, width - outPx - 3.5)) }}
        title={`淡出 ${easeOut.toFixed(3)} s · 左右拖动`} {...handle('out')} />}
    </>
  );
}

// 片段底形（ClipDrawer.DrawDefaultClip）：中段底色由按钮背景画（CSS 按 --in/--out 在两端留空），
// 这里画两端的混合与淡变三角、淡变暗底和默认边线。视口高度与轨道同为 30，横向 1 单位即 1 像素。
function ClipShape({ width, blendIn, blendOut }: { width: number; blendIn: ClipBlend; blendOut: ClipBlend }) {
  const w = Math.max(1, width), h = trackHeight;
  const inW = blendIn?.width ?? 0, outW = blendOut?.width ?? 0;
  return (
    <svg className={styles.clipShape} viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" aria-hidden>
      {blendIn?.kind === 'ease' && <rect className={styles.easeBack} width={inW} height={h} />}
      {blendOut?.kind === 'ease' && <rect className={styles.easeBack} x={w - outW} width={outW} height={h} />}
      {blendIn && <polygon className={styles.blendShape} points={blendIn.kind === 'mix' ? `0,0 ${inW},${h} ${inW},0` : `0,${h} ${inW},${h} ${inW},0`} />}
      {blendOut && <polygon className={styles.blendShape} points={`${w - outW},0 ${w - outW},${h} ${w},${h}`} />}
      <rect className={styles.clipEdge} x={inW} width={1} height={h} />
      {blendOut?.kind !== 'mix' && <rect className={styles.clipEdge} x={w - outW - 1} width={1} height={h} />}
      {blendIn?.kind === 'mix' && <>
        <rect className={styles.clipEdge} width={1} height={h} />
        <line className={styles.blendLine} x1={0} y1={0} x2={inW} y2={h} />
      </>}
    </svg>
  );
}

// 片段框：作者片段与预览输入的按住段共用同一外观、选中、框选与双击打开节点图。
// 照 Unity 片段：中段底色、底边 4 像素类别色、居中的白字片段名，放不下时不写（看轨道头与悬停提示）；exit 之后的部分压暗。
// 作者模式下给出 edit 时可拖动平移、拖两端改起止。
function ClipBox({ boxKey, selectId, kind, label, start, end, exit, graphId, edit, ease, mix }: {
  boxKey: string; selectId: string; kind: TrackKind; label: string; start: number; end: number; exit: number;
  graphId: string | null; edit?: ClipEdit; ease?: ClipEase; mix?: ClipMix;
}) {
  const { setNodeRef, itemStyle, itemContentStyle, listeners, attributes } = useItem({
    id: boxKey, span: { start, end }, disabled: !edit, data: edit ? { edit } : undefined, resizeHandleWidth: 12,
  });
  const { valueToPixels } = useTimelineContext();
  const clock = useContext(PreviewClockContext);
  const panelId = usePanelId();
  // 拖动中由虚影代替；拖端不合规时原地变红。
  const moving = useClipEditing(state => state.drag?.panelId === panelId && state.drag.ghosts.some(ghost => ghost.box === boxKey));
  const invalid = useClipEditing(state => state.resize?.panelId === panelId && state.resize.box === boxKey);
  const boxed = useContext(BoxedContext).has(boxKey);
  const selected = useSelected(selectId, 'clip') || boxed;
  const [easeDraft, setEaseDraft] = useState<EaseDraft | null>(null);
  const clipGrid = useContext(ClipGridContext);
  const width = valueToPixels(end - start);
  const dimmed = start >= exit;
  const cut = !dimmed && end > exit ? valueToPixels(exit - start) : null;
  const mixIn = mix?.in ?? 0, mixOut = mix?.out ?? 0;
  // 照 Clip.UpdateMix：自身淡入不超过时长减去混出，淡出不超过时长减去混入。
  const selfIn = Math.min(ease?.in ?? 0, end - start - mixOut), selfOut = Math.min(ease?.out ?? 0, end - start - mixIn);
  const easeIn = easeDraft?.side === 'in' ? easeDraft.seconds : selfIn;
  const easeOut = easeDraft?.side === 'out' ? easeDraft.seconds : selfOut;
  const blendIn: ClipBlend = mixIn > 0 ? { kind: 'mix', width: valueToPixels(mixIn) } : easeIn > 0 ? { kind: 'ease', width: valueToPixels(easeIn) } : null;
  const blendOut: ClipBlend = mixOut > 0 ? { kind: 'mix', width: valueToPixels(mixOut) } : easeOut > 0 ? { kind: 'ease', width: valueToPixels(easeOut) } : null;
  const inWidth = blendIn?.width ?? 0, outWidth = blendOut?.width ?? 0;
  const labelWidth = width - inWidth - outWidth - clipLabelPadding * 2;
  return (
    // 片段固定高 30：轨道展开曲线时行变高，曲线画在片段下方。
    <div ref={setNodeRef} style={{ ...itemStyle, height: trackHeight, visibility: moving ? 'hidden' : undefined }} className={styles.item}
      {...(edit ? { ...listeners, ...attributes } : {})}>
      <div style={itemContentStyle}>
        <button className={styles.clip} data-box={boxKey} data-select={selectId} data-selected={selected || undefined}
          data-dim={dimmed || undefined} data-editable={edit ? true : undefined} data-invalid={invalid || undefined}
          style={{ ['--tc' as string]: trackColors[kind], ['--in' as string]: `${inWidth}px`, ['--out' as string]: `${outWidth}px` }}
          title={`${label} · ${start.toFixed(3)}–${end.toFixed(3)} s${end > exit ? `（${exit.toFixed(3)} s 离开本段）` : ''}\n双击打开节点图${edit ? '\n拖动平移，拖两端改起止' : ''}`}
          onClick={() => selectTimelineObject(clock, 'clip', selectId)}
          onDoubleClick={() => openClipGraph(graphId, label)}>
          <ClipShape width={width} blendIn={blendIn} blendOut={blendOut} />
          {labelWidth > clipLabelMinWidth && <span className={styles.clipLabel}>{label}</span>}
          {ease && <EaseHandles ease={{ ...ease, in: mixIn > 0 ? mixIn : selfIn, out: mixOut > 0 ? mixOut : selfOut }}
            start={start} end={end} draft={easeDraft} setDraft={setEaseDraft} sides={{ in: mixIn <= 0, out: mixOut <= 0 }}
            step={(edit && clipGrid(edit.documentId, edit.clipId)?.step) ?? minimumStep} />}
          {cut !== null && <i className={styles.clipCut} style={{ left: cut }} />}
          {edit && <><i className={styles.clipResize} data-side="start" /><i className={styles.clipResize} data-side="end" /></>}
        </button>
      </div>
    </div>
  );
}

// 照 Timeline Editor：双击片段或标记打开它的节点图。
function openClipGraph(graphId: string | null, label: string) {
  const graph = clipGraph(getState().graphs, graphId);
  if (!graph) {
    consoleActions.log('warn', 'Timeline', graphId ? `${label} 的节点图不在本次运行中` : `${label} 没有节点图`);
    return;
  }
  selectionActions.showGraph(graphKey(graph), null);
  openPanel('graph');
}

// 标记：作者标记与预览输入的单次按键共用；单击选中（有 selectId 时），双击打开节点图。
// 时刻处一根 1 像素类别色细线贯穿整行，顶端一个朝下的小三角（MarkerTriangle）尖端对准时刻；所触发的内容（text）
// 照 Unity 不写名字，写在悬停提示里。未绑定节点图时三角只留轮廓。
// 作者模式给出 move 时可左右拖动（MoveMarker），拖动中按 snap 取整显示，松开写回；标记不许重叠，
// 拖到同轨已有标记的时刻（taken）时细线变红，松开不写回。
function Marker({ seconds: shown, kind, label, text, graphId, selectId, move, snap, taken }: {
  seconds: number; kind: TrackKind; label: string; text: string | null; graphId: string; selectId?: string;
  move?(seconds: number): void; snap?(seconds: number): number; taken?(seconds: number): boolean;
}) {
  const { range, valueToPixels, getValueFromScreenX } = useTimelineContext();
  const [dragging, setDragging] = useState<{ x: number; seconds: number } | null>(null);
  const seconds = dragging?.seconds ?? shown;
  const blocked = !!dragging && dragging.seconds !== shown && !!taken?.(dragging.seconds);
  const clock = useContext(PreviewClockContext);
  const selected = useWorkbench(state => !!selectId && state.selection?.kind === 'clip' && state.selection.id === selectId);
  const left = valueToPixels(seconds - range.start);
  return (
    <button className={styles.marker} data-selected={selected || undefined} data-unbound={!text || undefined}
      data-invalid={blocked || undefined} data-select={selectId} style={{ left, height: trackHeight, ['--tc' as string]: trackColors[kind] }}
      title={`${label} · ${seconds.toFixed(3)} s\n${text ?? '未绑定节点图'}${blocked ? '\n此刻已有标记，标记不能重叠' : ''}\n双击打开节点图${move ? '\n左右拖动改时刻' : ''}`}
      onPointerDown={event => {
        if (!move || event.button !== 0) return;
        event.currentTarget.setPointerCapture(event.pointerId);
        setDragging({ x: event.clientX, seconds: shown });
      }}
      onPointerMove={event => {
        // 按下后移动超过 3 像素才算拖动，单击仍是选中。
        if (!dragging || !snap || Math.abs(event.clientX - dragging.x) < 3) return;
        setDragging({ ...dragging, seconds: snap(getValueFromScreenX(event.clientX)) });
      }}
      onPointerUp={() => {
        if (dragging && dragging.seconds !== shown && !blocked) move!(dragging.seconds);
        setDragging(null);
      }}
      onPointerCancel={() => setDragging(null)}
      onClick={() => { if (selectId) selectTimelineObject(clock, 'clip', selectId); }}
      onDoubleClick={() => openClipGraph(graphId, label)}>
      <MarkerTriangle className={styles.markerHead} />
    </button>
  );
}

// ── 预览输入组 ──

function CurveGroupLine({ nest, open, toggle }: { nest: Nest | undefined; open: boolean; toggle(id: string): void }) {
  const { curves, selected, availability } = useWorkbench(useShallow(state => ({
    curves: state.archiveCurves, selected: state.selectedCurves, availability: state.curveAvailability,
  })));
  const reason = availability.find(item => item.channel === 'data-curves' && !item.available)?.reason;
  const entries: MenuEntry[] = (curves ?? []).map(curve => ({
    kind: 'check', label: `${curve.actorId ? `${curve.actorId} · ` : ''}${curve.displayName}`,
    checked: selected.includes(curve.id), onChange: checked => runActions.selectCurve(curve.id, checked),
  }));
  return <Line id="curves" nest={nest} headClass={styles.groupHead} laneClass={styles.groupLane} head={<>
    <GroupToggle open={open} name="项目曲线" onToggle={() => toggle('curves')} />
    {entries.length > 0 && <MenuButton label="选择项目曲线" variant="ghost" className={styles.addButton} iconOnly entries={entries}><AddIcon /></MenuButton>}
  </>}>
    {reason ? <span className={styles.curveNotice} title={reason}>{reason}</span>
      : !curves?.length ? <span className={styles.curveNotice}>此档案未声明曲线</span> : null}
  </Line>;
}

function CurveLine({ row, nest }: { row: CurveRow; nest: Nest | undefined }) {
  const clock = useContext(CurveClockContext);
  const { range } = useTimelineContext();
  const { runId, frame, hostFrame, image } = useWorkbench(useShallow(state => ({
    runId: state.runId, frame: state.archiveFrame, hostFrame: state.hostFrame,
    image: state.archiveImage,
  })));
  const selectId = runId === null ? null : `curve/${runId}/${row.curve.id}`;
  const selected = useSelected(selectId ?? '', 'track');
  const samples = useMemo(() => !runId || !clock ? [] : archive.curveSamples(runId, row.curve.id).map(sample => ({
    sample, seconds: clock.seconds + sample.source.clock.seconds - clock.sourceSeconds,
  })).filter(point => point.seconds >= range.start && point.seconds <= range.end), [runId, row.curve.id, frame, clock, range]);
  const min = samples.length ? Math.min(...samples.map(point => point.sample.value)) : 0;
  const max = samples.length ? Math.max(...samples.map(point => point.sample.value)) : 0;
  const record = frame?.curves[row.curve.id];
  const reason = !clock ? '没有匹配的运行时钟' : record?.kind === 'failed' ? record.reason
    : record?.kind === 'sampled' ? record.availability.find(item =>
      (item.channel === row.curve.channel || item.channel === 'data-curves') && !item.available)?.reason ?? null : '正在读取曲线';
  const select = () => { if (selectId !== null) selectionActions.select({ kind: 'track', id: selectId }); };
  return <Line id={row.id} nest={nest} height={curveHeight} selected={selected} headClass={styles.trackHead} head={
    <TrackHead kind="motion" selected={selected} name={row.curve.displayName} onSelect={selectId === null ? undefined : select}
      title={`${row.curve.clock} · ${row.curve.coordinates} · ${row.curve.unit}`} />
  }>
    {reason ? <span className={styles.curveNotice} title={reason}>{reason}</span> : <>
      <svg className={styles.curvePlot} viewBox="0 0 1000 64" preserveAspectRatio="none" aria-label={row.curve.displayName}>
        {samples.map(({ sample, seconds }, index) => {
          const matchingImage = sample.presentationSequence === null || image !== null && image.kind !== 'unavailable' &&
            sample.presentationSequence === image.metadata.presentationSequence;
          return <circle key={`${sample.source.clock.position.hostFrame}/${sample.presentationSequence ?? 'logic'}/${index}`}
            cx={(seconds - range.start) / (range.end - range.start) * 1000}
            cy={max === min ? 32 : 57 - (sample.value - min) / (max - min) * 50} r={2.5}
            data-current={sample.source.clock.position.hostFrame === hostFrame && matchingImage || undefined}
            onClick={() => {
              select();
              void transport.seekTime(sample.source.clock.seconds);
            }}>
            <title>{`${sample.value} ${row.curve.unit}\n${sample.source.clock.positionDisplayName} ${sample.source.clock.position.hostFrame} · ${row.curve.clock}\n原值 ${sample.rawValue}${sample.presentationSequence === null ? '' : `\n表现序号 ${sample.presentationSequence} · Tick ${sample.presentationTick}`}`}</title>
          </circle>;
        })}
      </svg>
      {samples.length > 0 && <span className={styles.curveScale}>{max.toPrecision(4)} / {min.toPrecision(4)} {row.curve.unit}</span>}
    </>}
  </Line>;
}

function ReceivedGroupLine({ nest, received, open, toggle }: {
  nest: Nest | undefined; received: ReceivedInput; open: boolean; toggle(id: string): void;
}) {
  const notice = !received.keys.length && !received.missing.length ? '已读帧内没有非中性输入' : null;
  const scope = received.ranges.map(range => range.first === range.last ? range.first : `${range.first}—${range.last}`).join('、');
  return <Line id="received" nest={nest} headClass={styles.groupHead} laneClass={styles.groupLane} head={<>
    <GroupToggle open={open} name="实际收到的输入" meta="已读帧" onToggle={() => toggle('received')}
      title={`单击${open ? '折叠' : '展开'}；已读 SDK ${scope}，随游标分页读取`} />
  </>}>
    {notice && <span className={styles.curveNotice} title={notice}>{notice}</span>}
  </Line>;
}

function ReceivedLine({ row, nest }: { row: ReceivedRow; nest: Nest | undefined }) {
  const { range } = useTimelineContext();
  const visible = (span: ReceivedSpan) => span.end >= range.start && span.start <= range.end;
  return <Line id={row.id} nest={nest} headClass={styles.trackHead} head={<TrackHead kind="in" name={row.name} />}>
    {row.spans.filter(visible).map(span => (
      <Positioned key={`s${span.start}`} start={span.start} end={span.end} title={`${span.label} · ${span.start.toFixed(3)}—${span.end.toFixed(3)} s`}
        className={row.missing ? styles.receivedMissing : styles.receivedSpan} />
    ))}
    {row.requests.filter(visible).map(span => (
      <Positioned key={`r${span.start}`} start={span.start} end={span.end} title={`请求 ${span.label} · ${span.start.toFixed(3)} s`}
        className={styles.receivedRequest} />
    ))}
  </Line>;
}

function InputGroupLine({ nest, open, toggle, roots }: {
  nest: Nest | undefined; open: boolean; toggle(id: string): void; roots: FrozenInputTimeline[] | undefined;
}) {
  const inputRoot = useWorkbench(state => state.inputRoot);
  const reason = useWorkbench(state => state.inputTimeline?.availability[0]?.reason ?? '');
  const options = (roots ?? []).map(root => ({ value: inputRootKey(root), label: `方案：${root.displayName}` }));
  return (
    <Line id="input" nest={nest} laneClass={styles.groupLane} headClass={styles.groupHead} head={
      <>
        <GroupToggle open={open && !!options.length} name="预览输入" disabled={!options.length} onToggle={() => toggle('input')} />
        <span className={styles.scheme}>
          <Select label="已采用输入方案" placeholder="方案" value={inputRoot} options={options}
            disabled={options.length < 2} tip={options.length ? undefined : reason || '本次运行没有预览输入'}
            onChange={key => runActions.selectInputRoot(key)} />
        </span>
      </>
    } />
  );
}

// 预览输入的按住段名字：所写的键，与 Timeline Editor 的“Fire 按住”一致。
const inputLabel = (writes: InputWrite[], hold: boolean) =>
  `${[...new Set(writes.map(write => write.keyId))].join(' / ') || '输入'}${hold ? ' 按住' : ''}`;

// 预览输入轨与作者轨同一外观：轨道头、片段框、标记；路线结束后的部分压暗。
const InputTrackLine = memo(function InputTrackLine({ row, nest }: { row: InputTrackRow; nest: Nest | undefined }) {
  const selected = useSelected(row.selectId, 'track');
  const routeEnd = useWorkbench(() => transport.duration());
  const { range } = useTimelineContext();
  const clips = row.track.clips.filter(clip => clip.endSeconds >= range.start && clip.startSeconds <= range.end);
  const markers = row.track.markers.filter(marker => marker.seconds >= range.start && marker.seconds <= range.end);
  if (routeEnd === null) return null;
  return (
    <Line id={row.id} nest={nest} selected={selected} headClass={styles.trackHead} head={
      <TrackHead kind="in" selected={selected} name={row.track.displayName}
        onSelect={() => selectionActions.select({ kind: 'track', id: row.selectId })} />
    }>
      {clips.map(clip => {
        const selectId = inputClipSelectId(row.track.id, clip.id);
        return (
          <ClipBox key={clip.id} boxKey={selectId} selectId={selectId} kind="in" label={inputLabel(clip.writes, true)}
            start={clip.startSeconds} end={clip.endSeconds} exit={routeEnd} graphId={clip.graphId} />
        );
      })}
      {markers.map(marker => (
        <Marker key={marker.id} seconds={marker.seconds} kind="in" label={inputLabel(marker.writes, false)} text={inputLabel(marker.writes, false)}
          graphId={marker.graphId} selectId={inputClipSelectId(row.track.id, marker.id)} />
      ))}
    </Line>
  );
});

// ── 覆盖层：游标（TimelineTimeCursor）、终点（End Marker）与段边界，盖在刻度与轨道上 ──
// 游标订阅时刻后直接改 transform，不经过 React。游标头在刻度区底部对齐，线从时长条处向下；
// 拖动刻度时在刻度上方显示游标时刻。终点由片段决定、不能拖动，照 Unity 用暗蓝线与灰色头。

function Overlay({ bounds, origin, segments, follow, onRangeChanged }: {
  bounds: Range; origin: number; segments: RouteSegment[]; follow: boolean;
  onRangeChanged(update: (previous: Range) => Range): void;
}) {
  const { range, valueToPixels, sidebarWidth } = useTimelineContext();
  const line = useRef<HTMLDivElement>(null);
  const head = useRef<HTMLDivElement>(null);
  const tip = useRef<HTMLDivElement>(null);
  const panelId = usePanelId();
  const visible = usePanelVisible();
  const panelWindow = usePanelWindow();
  const pending = useRef(false);
  const clock = useContext(PreviewClockContext);

  const apply = useCallback(() => {
    const state = getState();
    const lineElement = line.current, headElement = head.current, tipElement = tip.current;
    if (!lineElement || !headElement || !tipElement) return;
    const seconds = previewSeconds(state, clock, authorCursorOf(useAuthorCursor.getState(), clock));
    lineElement.hidden = headElement.hidden = tipElement.hidden = seconds === null;
    if (seconds !== null) {
      if (follow && transport.isPlaying() && (seconds > range.end || seconds < range.start)) {
        const span = range.end - range.start;
        onRangeChanged(() => ({ start: seconds - span * 0.1, end: seconds + span * 0.9 }));
      }
      // 取整到像素：1 像素的线与 11 像素宽的头共用同一中线。
      const x = Math.floor(valueToPixels(seconds - range.start));
      lineElement.style.transform = headElement.style.transform = `translateX(${x}px)`;
      tipElement.textContent = timeText(seconds - origin);
      tipElement.style.transform = `translateX(calc(${x + 0.5}px - 50%))`;
    }
    // 下一次绘制后报告就绪：片段的游标 class 由 React 提交，等到绘制完成。
    if (visible && !pending.current) {
      pending.current = true;
      panelWindow.requestAnimationFrame(() => {
        pending.current = false;
        const current = getState();
        const key = frameKey(current.runId, current.frameIndex);
        if (current.selectedCurves.some(id => current.archiveFrame?.curves[id] === undefined)) {
          clearRendered(panelId);
          return;
        }
        if (key) reportRendered(panelId, key);
      });
    }
  }, [range, origin, valueToPixels, follow, onRangeChanged, visible, panelWindow, panelId, clock]);

  useLayoutEffect(() => {
    apply();
    const stopRun = useWorkbench.subscribe((state, previous) => {
      if (state.seconds !== previous.seconds || state.hostFrame !== previous.hostFrame || state.frameKey !== previous.frameKey ||
        state.archiveFrame !== previous.archiveFrame || state.selectedCurves !== previous.selectedCurves) apply();
    });
    const stopAuthor = useAuthorCursor.subscribe(apply);
    return () => { stopRun(); stopAuthor(); };
  }, [apply]);

  useEffect(() => () => clearRendered(panelId), [panelId]);

  const x = (seconds: number) => `translateX(${Math.floor(valueToPixels(seconds - range.start))}px)`;
  return (
    <div className={styles.overlay} style={{ left: sidebarWidth }}>
      {segments.filter(segment => segment.start > range.start && segment.start < range.end).map(segment => (
        <div key={segment.id} className={styles.segmentLine} style={{ transform: x(segment.start) }} />
      ))}
      <div className={styles.endLine} style={{ transform: x(bounds.end) }} />
      <div className={styles.endHead} style={{ transform: x(bounds.end) }} />
      <div ref={line} className={styles.cursorLine} hidden />
      <div ref={head} className={styles.cursorHead} hidden><PlayheadShape /></div>
      <div ref={tip} className={styles.cursorTip} hidden />
    </div>
  );
}
