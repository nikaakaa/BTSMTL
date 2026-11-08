import { PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import {
  TimelineContext, useItem, useRow, useTimelineContext, useTimelineMonitor, type DragItemData, type Range,
  type ResizeEndEvent, type ResizeItemData, type ResizeMoveEvent, type UsePanStrategy,
} from 'dnd-timeline';
import {
  createContext, Fragment, memo, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type MouseEvent as ReactMouseEvent,
  type PointerEvent, type ReactNode, type RefObject,
} from 'react';
import { create } from 'zustand';
import { useShallow } from 'zustand/react/shallow';
import type { SkillElements } from '../../api/authoring';
import type { Frame, FrozenInputTimeline, InputWrite } from '../../api/types';
import type { PreviewQuickPlan } from '../../api/previewQuickRecords';
import { samePreviewLoad } from '../../api/playerRecords';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered, useFrameReport } from '../../shell/readiness';
import { openPanel } from '../../shell/layout';
import {
  applyTimelineEdits, authoringActions, authoringDocument, currentDocument, graphPath, markerTimeTaken, useAuthoring, useAuthorTimelines, useInputContext, useInputSchemes,
  useSkillElements, useDocumentObservationBinding, useTimelineObservationBinding,
} from '../../state/authoring';
import { inputAuthoring } from '../../state/authoringInputs';
import {
  clipCurves, clipMoveIssue, clipReferenceSource, clipSnapSource, clipTemplatesFor, markersTaken, trackHasCurves, trackOverlap, useReferenceCurves,
} from '../../state/authoringTimeline';
import { consoleActions, getState, inputRootKey, isRealArchive, runActions, selectedScene, selectionActions, useWorkbench } from '../../state/store';
import {
  clipGraph, clipMix, clipSelectId, clipUnions, documentSegments, inputClipSelectId, overlapIssue, inputTrackSelectId, timelineLength, segmentRows, segmentTitle, selectedInputRoot,
  timelineObservationNotice, trackColors, type ClipInstance, type GroupRow, type TimelineSegment, type Span, type TrackKind, type TrackRow,
} from '../../state/timelineModel';
import { callKey, documentObservationNotice, graphCallsAtFrame, graphKey, matchesDocumentCall, parentCallNotice, parentCallMatches, parentGraphCalls, recordedGraphCalls, routeObservation, type RecordedGraphCall } from '../../state/graphModel';
import { transport } from '../../state/transport';
import { quickBounds, quickInputSourceKey, quickPlanStale, quickPreview } from '../../state/quickPreview';
import { useInputObservation } from '../../state/inputModel';
import { useReceivedInput, type ReceivedInput, type ReceivedKey, type ReceivedSpan } from '../../state/inputReceipts';
import { archive } from '../../state/archive';
import { cachedFrame } from '../../state/frames';
import { animationReferenceKind, readAnimationDrop, useAnimationLibrary } from '../../state/resources';
import { acceptsReferenceDrag } from '../../state/referenceSelection';
import { useTimelineResourceCatalog } from '../../state/resourceScope';
import { cloneJson, setIn, type JsonObject } from '../../state/skillJson';
import { Empty } from '../../ui/blocks';
import { ContextMenu, createTipGroup, GroupTip, MenuButton, Select, TextInput, Tip, TipGroup, type MenuEntry } from '../../ui/controls';
import { AddIcon, CurveIcon, FoldoutIcon, MuteIcon, OptionsIcon, SearchIcon } from '../../ui/icons';
import { ClipCurves, CurveParameters, noMix, type ClipMix } from './ClipCurves';
import { MarkerTriangle, PlayheadShape, TrackIcon } from './icons';
import { QuickBar, locateQuickSource, useQuickName } from './QuickPreview';
import { RunBar } from './RunBar';
import { quickRoute, realRoute, type RouteBar, type RouteView } from './routeRows';
import { AddTrack, Ruler, TimelineToolbar, TimeScroller, type Subject } from './TimelineToolbar';
import {
  ClipGridContext, CurveClockContext, CurveViewContext, PreviewClockContext, SnapContext, authorCursorOf, clipLabelMinWidth, clipLabelPadding, curveHeight,
  curveInspectHeight, curveListHeight, curveMaxHeight, defaults, fitRange, groupKinds, minSpan, minimumStep, observationKey, overscan, playbackFor, previewSeconds,
  recordedLocalPosition, rowHeight, rowNesting, rowSpacing, rowsPadding, segmentRange, selectTimelineObject, sidebarFor, snapGrid, snapStep, snapTo, timeText,
  trackHeight, useAuthorCursor, useClipGrid, usePreviewSelect,
  type CurveClock, type CurveRow, type CurveView, type InputTrackRow, type Nest, type Params, type PreviewClock, type QuickPlaybackChoice, type ReceivedRow,
  type Row, type RouteLaneRow, type TimelineObservation,
} from './timelineShared';
import styles from './timeline.module.css';

// Timeline 面板（D5）：照 Unity Timeline 窗口（com.unity.timeline 1.7.7 Dark 皮肤），技能里的 Timeline 按组列出，每组可折叠。
// dnd-timeline 管时间与像素换算、缩放、平移；刻度、片段、输入标记、游标自绘。
// 游标是独立覆盖线，订阅 cursor 后直接改 transform，片段不随游标重绘。

// 轨道头宽度，约占窗口三分之一，面板窄时收窄让出时间区；CSS 经 --sidebar 读取。
export function TimelinePanel() {
  const quick = useWorkbench(state => state.previewMode === 'quick');
  return <div className={styles.window}>{quick ? <QuickBar /> : <RunBar />}<DocumentTimeline /></div>;
}

// 快档所选输入方案的输入 Timeline；路线、输入轨与按全局时间排开的各次播放都画在它的视图里。
// 保存后重读来源期间方案不变，继续看原输入 Timeline，视图不跳回技能自身的 Timeline。
function useQuickInputTimeline() {
  const last = useRef<string | null>(null);
  const value = useWorkbench(state => {
    const description = state.quickPreview.description;
    if (state.previewMode !== 'quick') return null;
    if (description.kind === 'loading' || description.kind === 'stale') return undefined;
    return description.kind === 'ready' ? description.reply.data.root?.inputSchemes
      .find(scheme => quickInputSourceKey(scheme.source) === quickInputSourceKey(state.quickPreview.scheme))?.timelineId ?? null : null;
  });
  if (value !== undefined) last.current = value;
  return value === undefined ? last.current : value;
}
const useQuickRouteEnd = () => useWorkbench(state => state.previewMode === 'quick' && state.quickPreview.route !== null
  ? quickBounds(state.quickPreview.route.plan).last : 0);

// 技能在顶栏选择；打开后固定看技能里的一个 Timeline（默认第一个），也可切到全部。
function DocumentTimeline() {
  const { target, loading, available, reason, error } = useAuthoring(useShallow(state => ({
    target: state.target, loading: authoringDocument(state).loading, available: state.available,
    reason: state.unavailableReason, error: authoringDocument(state).documentError,
  })));
  const count = useAuthorTimelines().length;
  if (available === false && count === 0) return <ReportEmpty text={reason ?? '作者接口不可用'} />;
  if (loading && count === 0) return <Empty text="正在读取内容" />;
  if (!target || !count) return <ReportEmpty text={error ?? (target ? '此内容没有 Timeline' : '在顶栏选择技能')} />;
  return <TimelineWindow />;
}

function ReportEmpty({ text }: { text: string }) {
  useFrameReport(text);
  return <Empty text={text} />;
}

// 下拉中的“全部 Timeline”；focus 为 null 时看第一个 Timeline。
const allTimelines = '*';

function TimelineWindow() {
  const [params, setParams] = usePanelParams<Params>(defaults);
  const elements = useSkillElements(useAuthoring(state => state.target !== null));
  const schemes = useInputSchemes();
  const authorTimelines = useAuthorTimelines();
  // 快档与真档默认看路线：输入方案的输入 Timeline；作者档默认看第一个 Timeline。
  const quickInputTimeline = useQuickInputTimeline();
  // 路线来源：快档，或回看某份运行档案；切到新的来源时回到路线视图。
  const routeKey = useWorkbench(state => state.previewMode === 'quick' ? 'quick' : state.runId !== null ? `run:${state.runId}` : null);
  const routeTimeline = routeKey === 'quick' ? quickInputTimeline : routeKey !== null ? schemes[0]?.documentId ?? null : null;
  const fallback = routeTimeline !== null && authorTimelines.some(item => item.documentId === routeTimeline) ? routeTimeline
    : authorTimelines[0]?.documentId ?? null;
  const focusId = params.focus === allTimelines ? null
    : authorTimelines.some(item => item.documentId === params.focus) ? params.focus : fallback;
  const switched = useRef(routeKey);
  useEffect(() => {
    if (switched.current === routeKey) return;
    switched.current = routeKey;
    if (routeKey !== null) setParams({ focus: null, range: null, call: null, pin: null, caller: null, origin: null });
  }, [routeKey, setParams]);
  const quickRouteEnd = useQuickRouteEnd();
  const documentLength = useMemo(() => {
    const selected = authorTimelines.find(item => item.documentId === focusId);
    return Math.max(0, ...(selected ? [selected] : authorTimelines).map(timeline =>
      Math.max(timelineLength(timeline), schemes.find(scheme => scheme.documentId === timeline.documentId)?.rangeEndSeconds ?? 0,
        timeline.documentId === quickInputTimeline ? quickRouteEnd : 0)));
  }, [authorTimelines, focusId, schemes, quickInputTimeline, quickRouteEnd]);
  const tickRate = useAuthoring(state => authoringDocument(state).tickRate);
  const scenario = useAuthoring(state => state.target?.kind === 'scenario');
  const inputRate = useWorkbench(state => {
    const scheme = schemes.find(item => item.documentId === focusId);
    return scheme !== undefined && scheme.scene === selectedScene(state)?.path && state.inputCatalog?.players.some(player => player.actorId === scheme.actorId)
      ? state.inputCatalog.tickRate : null;
  });
  const rate = scenario ? inputRate : tickRate;
  const frameStep = rate === null ? null : 1 / rate;
  const step = snapStep(params.snap, frameStep);
  const clipGrid = useClipGrid(params.snap ?? defaults.snap, frameStep, elements);
  const bounds = useMemo(() => ({ start: 0, end: Math.max(minSpan, documentLength) }), [documentLength]);
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
            <TimelineBody elements={elements} params={params} setParams={setParams} focusId={focusId} bounds={bounds} rate={rate} onRangeChanged={onRangeChanged} />
          </ClipGridContext.Provider>
        </SnapContext.Provider>
      </TimelineContext>
    </div>
  );
}

// 拖动中的片段（照 MoveItemHandler）：原位置隐藏，Rows 在目标行按吸附后的时间画虚影，不合规时虚影变红、松手放回原处；
// 拖片段两端不合规时片段原地变红、松手恢复原长。tip 为虚影上方的提示：不合规或指到不能放的轨道时写明原因，否则为起点时刻。
// 框选中一起移动的标记（markers，框选键）不画虚影，原地按 shift 秒平移显示。
interface DropGhost { box: string; row: string; start: number; end: number; label: string; kind: TrackKind }
const useClipEditing = create<{
  drag: { panelId: string; ghosts: DropGhost[]; markers: string[]; shift: number; invalid: boolean; tip: string } | null;
  resize: { panelId: string; box: string } | null;
}>(() => ({ drag: null, resize: null }));

interface DragSession {
  documentId: string;
  segmentId: string;
  // 拖动的片段（单个片段可换轨）；拖动的是框选中的标记时为 null。
  edit: ClipEdit | null;
  // 拖动对象的本地起点与吸附：片段按片段网格，标记按吸附步长。
  start: number;
  snap(local: number): number;
  // 一起移动的片段与原轨道（拖动片段时第一个是它）、标记与本地时刻；框选多个时各自留在原轨道。
  moving: { clip: ClipInstance; track: string }[];
  markers: { key: string; id: string; local: number }[];
  multi: boolean;
  target: string;
  activator: { x: number; y: number };
  // 拖动中的指针位置；尚未移动时按拖动位移推算。
  pointer: { x: number; y: number } | null;
  release(): void;
}

// 作者模式拖动、拖端写回片段时间：原起止加上拖动距离换成的秒数，按吸附步长取整，再减去本段零点得到 Timeline 本地秒数。
// 单个片段可拖到同一 Timeline 的另一条轨道（照 TimelineData.MoveClip：轨道能放这种片段且执行域相同），
// 指到不能放的轨道时留在上一条能放的轨道；框选的多个片段与标记整体平移、不换轨，拖其中的片段或标记都一样。
// 放下后的同轨重叠按轨道合同判断：树轨不许重叠，动画与位移曲线轨只许两两部分重叠形成混合；标记不许落到同轨其它标记的时刻。
function ClipEditing({ elements, rows, offsets, scroller, step, mover }: {
  elements: SkillElements | null;
  rows: Row[]; offsets: number[]; scroller: RefObject<HTMLDivElement | null>; step: number; mover: RefObject<GroupMove | null>;
}) {
  const panelId = usePanelId();
  const panelWindow = usePanelWindow();
  const { range, timelineRef, sidebarWidth: sidebar, valueToPixels, getValueFromScreenX } = useTimelineContext();
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
    const { edit, documentId, segmentId } = current;
    const pointer = current.pointer ?? { x: current.activator.x + delta.x, y: current.activator.y + delta.y };
    const moved = getValueFromScreenX(pointer.x) - getValueFromScreenX(current.activator.x);
    const shift = Math.max(current.snap(current.start + moved) - current.start,
      -Math.min(...current.moving.map(item => item.clip.clip.startSeconds), ...current.markers.map(item => item.local)));
    let hover: string | null = null;
    if (edit && !current.multi) {
      const row = rowAt(pointer.y);
      if (row?.kind === 'track' && row.segment.id === segmentId && row.track.id !== current.target) {
        const reason = row.track.id === edit.trackId ? null
          : elements ? clipMoveIssue(elements, documentId, current.moving[0].clip.clip.contract, edit.trackId, row.track.id) : '正在读取轨道合同';
        if (reason === null) current.target = row.track.id;
        else hover = reason;
      }
    }
    const trackOf = (item: DragSession['moving'][number]) => current.multi ? item.track : current.target;
    const ids = new Set(current.moving.map(item => item.clip.clip.id));
    let issue: string | null = null;
    for (const trackId of new Set(current.moving.map(trackOf))) {
      const track = trackRow(segmentId, trackId);
      issue = !track ? '目标轨道不在当前视图' : placementIssue(documentId, track, [
        ...track.track.clips.filter(clip => !ids.has(clip.id)).map(clip => ({ start: clip.startSeconds, end: clip.endSeconds })),
        ...current.moving.filter(item => trackOf(item) === trackId)
          .map(item => ({ start: item.clip.clip.startSeconds + shift, end: item.clip.clip.endSeconds + shift })),
      ]);
      if (issue) break;
    }
    if (!issue && current.markers.length && markersTaken(documentId, new Set(current.markers.map(item => item.id)), shift))
      issue = '标记落到同轨已有标记的时刻，标记不能重叠';
    const ghosts = current.moving.map((item): DropGhost => ({
      box: item.clip.key, row: `track:${segmentId}/${trackOf(item)}`, start: item.clip.start + shift, end: item.clip.end + shift,
      label: item.clip.label, kind: item.clip.kind,
    }));
    return { shift, issue, ghosts, tip: issue ?? hover ?? `${(current.start + shift).toFixed(3)} s` };
  };
  const publish = (current: DragSession, { ghosts, shift, issue, tip }: ReturnType<typeof preview>) =>
    useClipEditing.setState({ drag: { panelId, ghosts, markers: current.markers.map(item => item.key), shift, invalid: issue !== null, tip } });
  // 框选中同一次 Timeline 播放里的片段与标记。
  const boxedIn = (segmentId: string) => {
    const boxed = new Set((getState().timelineSelections[panelId] ?? []).map(item => item.box));
    const tracks = rows.filter((row): row is TrackRow => row.kind === 'track' && row.segment.id === segmentId);
    return {
      clips: tracks.flatMap(row => row.clips.filter(clip => boxed.has(clip.key)).map(clip => ({ clip, track: row.track.id }))),
      markers: tracks.flatMap(row => row.markers.filter(marker => boxed.has(marker.key))
        .map(marker => ({ key: marker.key, id: marker.selectId.split('/')[1], local: marker.seconds - row.segment.origin }))),
    };
  };
  // 开始拖动：捕获阶段记录指针，先于拖动库的处理，且不受行列表自动滚动影响。
  const begin = (current: DragSession) => {
    const follow = (move: globalThis.PointerEvent) => { current.pointer = { x: move.clientX, y: move.clientY }; };
    panelWindow.addEventListener('pointermove', follow, true);
    current.release = () => {
      panelWindow.removeEventListener('pointermove', follow, true);
      if (session.current === current) session.current = null;
      useClipEditing.setState({ drag: null });
    };
    session.current = current;
    publish(current, preview(current, { x: 0, y: 0 }));
  };
  const finish = (current: DragSession, delta: { x: number; y: number }) => {
    const result = preview(current, delta);
    current.release();
    if (result.issue) {
      consoleActions.log('warn', 'Timeline', `${result.issue}，放回原处`);
      return;
    }
    // 平移只按拖动对象的起点吸附，保持时长与间隔。
    const retarget = !!current.edit && !current.multi && current.target !== current.edit.trackId;
    if (result.shift || retarget)
      authoringActions.moveItems(current.documentId, [...current.moving.map(item => item.clip.clip.id), ...current.markers.map(item => item.id)],
        result.shift, retarget ? current.target : null);
  };
  // 拖动框选中的标记：与拖动框选中的片段同样整体平移，标记由 Marker 转交。
  useLayoutEffect(() => {
    mover.current = {
      start(box, x, y) {
        const row = rows.find((item): item is TrackRow => item.kind === 'track' && item.markers.some(marker => marker.key === box));
        const marker = row?.markers.find(item => item.key === box);
        if (!row || !marker) return;
        const group = boxedIn(row.segment.id);
        begin({
          documentId: row.timeline.documentId, segmentId: row.segment.id, edit: null,
          start: marker.seconds - row.segment.origin, snap: local => Math.max(0, snapTo(local, step)),
          moving: group.clips, markers: group.markers, multi: true, target: row.track.id,
          activator: { x, y }, pointer: null, release: () => {},
        });
      },
      move() { if (session.current) publish(session.current, preview(session.current, { x: 0, y: 0 })); },
      end() { if (session.current) finish(session.current, { x: 0, y: 0 }); },
      cancel() { session.current?.release(); },
    };
    return () => { mover.current = null; };
  });
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
      const group = multi ? boxedIn(edit.segmentId) : { clips: [], markers: [] };
      const activator = event.activatorEvent as globalThis.PointerEvent;
      begin({
        documentId: edit.documentId, segmentId: edit.segmentId, edit,
        start: own.clip.startSeconds, snap: local => snapGrid(local, clipGrid(edit.documentId, own.clip.id)),
        moving: [{ clip: own, track: edit.trackId }, ...group.clips.filter(item => item.clip !== own)], markers: group.markers,
        multi, target: edit.trackId, activator: { x: activator.clientX, y: activator.clientY }, pointer: null, release: () => {},
      });
    },
    onDragMove(event) {
      if (session.current) publish(session.current, preview(session.current, event.delta));
    },
    onDragCancel() {
      session.current?.release();
    },
    onDragEnd(event) {
      if (session.current) finish(session.current, event.delta);
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
                style={{ left: Math.max(0, left), top: top >= 22 ? top - 22 : top + trackHeight + 2 }}>{drag.tip}</span>
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

function TimelineBody({ elements, params, setParams, focusId, bounds, rate, onRangeChanged }: {
  elements: SkillElements | null;
  params: Params;
  setParams(patch: Partial<Params>): void;
  // 所看的 Timeline；null 为全部 Timeline。
  focusId: string | null;
  bounds: Range;
  // 作者时间网格决定刻度数字的小数位；未知时为 null。
  rate: number | null;
  onRangeChanged(update: (previous: Range) => Range): void;
}) {
  const binding = useDocumentObservationBinding();
  const timelineBinding = useTimelineObservationBinding(binding.dirty);
  const quickMode = useWorkbench(state => state.previewMode === 'quick');
  const realArchive = useWorkbench(isRealArchive);
  const runId = useWorkbench(state => state.runId);
  const trace = useWorkbench(useShallow(routeObservation));
  const routeFrames = useWorkbench(state => state.routeFrames);
  const currentRoute = trace.kind === 'ready' ? trace.route : null;
  const frameSource = useWorkbench(state => state.frame?.hostFrame === state.hostFrame ? state.frame.source : null);
  const schemes = useInputSchemes();
  const { setTimelineRef, getValueFromScreenX, sidebarWidth } = useTimelineContext();
  const { inputTimeline, inputRoot, graphs, curves, selectedCurves } = useWorkbench(useShallow(state => ({
    inputTimeline: state.inputTimeline, inputRoot: state.inputRoot, graphs: state.graphs,
    curves: state.previewMode === 'quick' ? null : state.archiveCurves, selectedCurves: state.selectedCurves,
  })));
  // 作者模式下未保存的片段时间叠加到运行的作者 Timeline 上。
  const edited = useAuthoring(state => authoringDocument(state).edited);
  const files = useAuthoring(state => authoringDocument(state).files);
  const timelines = useAuthorTimelines();
  const selectedClip = useWorkbench(state => state.selection?.kind === 'clip' ? state.selection.id : null);
  const references = useReferenceCurves();
  // 快档保留上一份路线直到新路线采用：过期时照常显示与定位，状态栏注明过期。
  const quickPlan = useWorkbench(state => state.previewMode === 'quick' ? state.quickPreview.route?.plan ?? null : null);
  const quickStale = useWorkbench(quickPlanStale);
  const quickInputTimeline = useQuickInputTimeline();
  const quickRouteEnd = useQuickRouteEnd();
  const segments = useMemo(() => documentSegments(timelines).map(segment => {
    const scheme = schemes.find(item => item.documentId === segment.documentId);
    return scheme ? { ...segment, length: Math.max(segment.length, scheme.rangeEndSeconds,
      segment.documentId === quickInputTimeline ? quickRouteEnd : 0) } : segment;
  }), [timelines, schemes, quickInputTimeline, quickRouteEnd]);
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

  // 技能文档看单个 Timeline：所选的那条，否则第一条；选“全部 Timeline”时按组列出。
  const focused = focusId === null ? null : segments.find(segment => segment.documentId === focusId) ?? null;
  const quickSegments = useMemo(() => quickPlan?.segments.map((segment, index) => ({ segment, index }))
    .filter(item => item.segment.timelineId === focused?.documentId) ?? [], [quickPlan, focused?.documentId]);
  const quickEnd = useMemo(() => quickPlan === null ? null : quickBounds(quickPlan).last, [quickPlan]);
  const [quickSelection, setQuickSelection] = useState<{ plan: PreviewQuickPlan; documentId: string; index: number } | null>(null);
  const selectedQuick = quickSelection?.plan === quickPlan && quickSelection?.documentId === focused?.documentId ? quickSelection.index : null;
  const activeQuick = useWorkbench(state => {
    const seconds = state.quickPreview.seconds;
    const active = quickSegments.filter(({ segment }) => segment.start <= seconds &&
      (seconds < segment.end || seconds === quickEnd && seconds === segment.end));
    return active.length === 1 ? active[0].index : null;
  });
  const quickSegment = quickSegments.find(item => item.index === (selectedQuick ?? activeQuick))?.segment
    ?? (selectedQuick === null && quickSegments.length === 1 ? quickSegments[0].segment : null);
  const quickInput = quickPlan !== null && focused !== null && focused.documentId === quickInputTimeline;
  const quickPlayback: QuickPlaybackChoice | null = quickPlan === null || focused === null || quickSegments.length === 0 ? null : {
    value: selectedQuick === null ? 'follow' : String(selectedQuick),
    options: [{ value: 'follow', label: '跟随当前路线' }, ...quickSegments.map(({ segment, index }) => ({ value: String(index),
      label: `播放 ${segment.playback} · 循环 ${segment.cycle} · 出现 ${segment.occurrence} · ${segment.start}—${segment.end} s` }))],
    select: value => setQuickSelection(value === 'follow' ? null : { plan: quickPlan, documentId: focused.documentId, index: Number(value) }),
  };
  const inputScheme = schemes.find(scheme => scheme.documentId === focused?.documentId);
  const inputObservation = useInputObservation(inputScheme?.documentId ?? null);
  const receivedSource = useMemo(() => !quickMode && runId !== null && inputObservation.kind === 'ready' ? {
    jobId: runId, actorId: inputObservation.input.actorId, stepSeconds: inputObservation.stepSeconds, keys: inputObservation.declarations.keys,
  } : null, [quickMode, runId, inputObservation.kind === 'ready' ? inputObservation.input.actorId : null,
    inputObservation.kind === 'ready' ? inputObservation.stepSeconds : null, inputObservation.kind === 'ready' ? inputObservation.declarations : null]);
  const received = useReceivedInput(receivedSource);
  // 路线动态轨：快档取推导路线，真档取已读档案帧中与本方案同一次载入的实际播放。
  const routeInput = !quickMode && runId !== null && (inputObservation.kind === 'ready' || inputObservation.kind === 'before-start') ? inputObservation : null;
  const routeSource = useRef(routeInput?.source ?? null);
  routeSource.current = routeInput?.source ?? null;
  const routeLoad = routeInput === null ? null : JSON.stringify([routeInput.input.actorId, routeInput.stepSeconds,
    routeInput.source?.loadCommandId, routeInput.source?.contentRevision, routeInput.source?.inputRevision]);
  const routeActor = routeInput?.input.actorId ?? null, routeStep = routeInput?.stepSeconds ?? null;
  const stateName = useQuickName();
  const route = useMemo<RouteView | null>(() => {
    if (focused === null || !schemes.some(scheme => scheme.documentId === focused.documentId)) return null;
    if (quickMode) return quickPlan !== null && quickInput ? quickRoute(quickPlan, timelines, focused.documentId, quickStale, stateName) : null;
    if (runId === null || routeLoad === null || routeActor === null || routeStep === null) return null;
    const samples = archive.inputFrames(runId, routeActor).filter(sample => samePreviewLoad(sample.source, routeSource.current));
    return realRoute(routeFrames, samples, timelines, routeStep);
  }, [focused?.documentId, schemes, quickMode, quickPlan, quickInput, quickStale, stateName, timelines, runId, routeLoad, routeActor, routeStep, routeFrames]);
  const playbacks = currentRoute?.playbacks;
  const caller = params.caller;
  const callerNotice = caller !== null && caller.runId !== runId ? '来源调用属于另一份运行档案，未选择其它实例' : null;
  const observations = useMemo(() => {
    const result = new Map<string, TimelineObservation>();
    if (quickMode || callerNotice !== null) return [];
    for (const playback of playbacks ?? [])
      if (playback.documentId === focused?.documentId && playback.call !== null && playback.domain !== null && matchesDocumentCall(playback.call, binding) &&
        (realArchive || timelineObservationNotice(timelineBinding, frameSource, playback.documentId, playback.call) === null) &&
        (caller === null || parentCallMatches(caller.call, playback.call))) {
        const observation = { call: playback.call, domain: playback.domain };
        result.set(observationKey(observation), observation);
      }
    return [...result.values()];
  }, [quickMode, realArchive, playbacks, focused?.documentId, binding, timelineBinding, frameSource, caller, callerNotice]);
  const pin = params.pin;
  const pinNotice = pin === null ? null : pin.runId !== runId ? '固定播放属于另一份运行档案，固定身份已保留'
    : pin.documentId !== focused?.documentId ? '固定播放不属于当前 Timeline，固定身份已保留'
    : documentObservationNotice(currentRoute, binding, pin.observation.call)
      ?? (caller !== null && !parentCallMatches(caller.call, pin.observation.call) ? '固定播放不属于来源调用'
        : realArchive ? null : timelineObservationNotice(timelineBinding, frameSource, pin.documentId, pin.observation.call));
  const observed = quickMode || (currentRoute === null && !(realArchive && pin?.recordIndex !== undefined)) ||
    pinNotice !== null || callerNotice !== null ? null
    : pin !== null ? pin.observation : params.call !== null ? observations.find(observation => observationKey(observation) === params.call) ?? null
      : observations.length === 1 ? observations[0] : null;
  const timelineNotice = timelineObservationNotice(timelineBinding, frameSource, focused?.documentId ?? null, observed?.call ?? null);
  const authorObservation = timelineNotice === null ? observed : null;
  const playbackNotice = trace.kind === 'unavailable' ? trace.reason
    : params.call !== null && observed === null ? '此帧没有所选播放的记录，未选择其它实例'
      : caller !== null && observations.length === 0 && observed === null ? [...new Set((playbacks ?? []).filter(playback =>
        playback.documentId === focused?.documentId && playback.call !== null)
        .map(playback => parentCallNotice(playback.call!)).filter(reason => reason !== null))].join('；') || '本帧没有完整父身份与来源调用匹配的播放'
        : observations.length > 1 && observed === null ? '本帧存在多个实际播放，请显式固定发生实例' : null;
  const choices = observations;
  const pinCall = (observation: TimelineObservation, call: string | null) => {
    const state = getState();
    if (runId === null || focused === null || state.runId !== runId || state.frame?.hostFrame !== state.hostFrame ||
      !playbackFor(state.frame, focused.documentId, observation)) return;
    setParams({ call, pin: { runId, documentId: focused.documentId, observation, recordIndex: state.frameIndex } });
  };
  const togglePin = pin ? () => setParams({ pin: null, call: null }) : observed === null || runId === null || focused === null ? undefined
    : () => pinCall(observed, observationKey(observed));
  const loadedCalls = useMemo(() => recordedGraphCalls(routeFrames, graphs ?? [], binding), [routeFrames, graphs, binding]);
  const currentCalls = useMemo(() => recordedGraphCalls(currentRoute === null ? [] : [currentRoute], graphs ?? [], binding), [currentRoute, graphs, binding]);
  const recorded = useMemo(() => graphCallsAtFrame(loadedCalls, currentRoute, currentCalls), [loadedCalls, currentRoute, currentCalls]);
  const parents = useMemo(() => observed === null ? [] : parentGraphCalls(observed.call, recorded), [observed, recorded]);
  const openParent = runId === null ? undefined : ({ graph, call }: RecordedGraphCall) => {
    selectionActions.showGraph({ key: graphKey(graph), panelId: openPanel('graph').id,
      observation: { runId, call }, caller: null, origin: null });
  };
  const selectCall = runId === null || focused === null ? undefined : (value: string) => {
    const observation = choices.find(item => observationKey(item) === value)!;
    pinCall(observation, null);
  };
  useEffect(() => {
    const selection = getState().selection;
    if ((selection?.kind === 'clip' || selection?.kind === 'track') && selection.id.startsWith(`${focused?.documentId}/`)) {
      const previous = selection.call ? callKey(selection.call) : null;
      const next = authorObservation ? callKey(authorObservation.call) : null;
      if (previous !== next || selection.call === undefined) selectionActions.select({ ...selection, call: authorObservation?.call ?? null });
    }
  }, [focused?.documentId, authorObservation]);

  const enter = useCallback((documentId: string, call: string | null = null) => {
    const segment = segments.find(item => item.documentId === documentId);
    if (!segment) return;
    const selection = getState().selection;
    if ((selection?.kind === 'clip' || selection?.kind === 'track') && !selection.id.startsWith(`${documentId}/`))
      selectionActions.select(null);
    setParams({ focus: documentId, range: segmentRange(segment), call, pin: null, caller: null, origin: null });
  }, [segments, setParams]);
  // 路线条：单击定位，双击进入所播 Timeline 并选中这次播放或实际调用。
  const routeActions = useMemo(() => ({
    seek(bar: RouteBar) {
      if (bar.target.kind === 'quick') quickPreview.seek(bar.target.seconds);
      else transport.seekRecord(bar.target.index);
    },
    open(bar: RouteBar) {
      if (bar.documentId === null) {
        if (bar.source) void locateQuickSource(bar.source).catch(error => consoleActions.error('快预览', error));
        return;
      }
      enter(bar.documentId, bar.observation === null ? null : observationKey(bar.observation));
      if (bar.quickIndex !== null && quickPlan !== null) setQuickSelection({ plan: quickPlan, documentId: bar.documentId, index: bar.quickIndex });
    },
  }), [enter, quickPlan]);
  const showAll = useCallback(() => setParams({ focus: allTimelines, range: null, call: null, pin: null, caller: null, origin: null }), [setParams]);

  // 节点图双击播放 Timeline 的节点时进入该 Timeline。
  const request = useWorkbench(state => state.timelineRequest);
  const handled = useRef<typeof request>(null);
  useEffect(() => {
    if (!request || request === handled.current) return;
    handled.current = request;
    const owner = segments.find(segment => segment.documentId === request.documentId);
    if (!owner) return;
    enter(request.documentId);
    setParams({ caller: request.observation, origin: request.origin });
  }, [request, segments, setParams, enter]);

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
      if (input && route) {
        result.push({ kind: 'routeGroup', id: 'route', route });
        if (!collapsed.has('route')) {
          route.lanes.forEach((bars, index) =>
            result.push({ kind: 'routeLane', id: `route:lane:${index}`, name: route.lanes.length > 1 ? `播放 ${index + 1}` : '播放', bars }));
        }
        // 路线上的每次播放按全局时间摆出所播 Timeline 的轨道，拖动、改长仍写回该 Timeline 的作者内容。
        for (const segment of route.segments) {
          const group: GroupRow = { kind: 'group', id: `group:${segment.id}`, segment, title: segmentTitle(segment) };
          const titleMatched = !filter || group.title.toLowerCase().includes(filter);
          const tracks = collapsed.has(group.id) ? [] : segmentRows(segment, graphNames).map(expand);
          const children = titleMatched ? tracks : tracks.filter(row => row.track.displayName.toLowerCase().includes(filter));
          if (titleMatched || children.length) result.push(group, ...children);
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
  }, [segments, focused, graphNames, inputTimeline, inputRoot, schemes, curves, selectedCurves, received, route, params.filter, params.collapsed,
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
    if (!focused) return;
    const limit = quickInput ? Math.max(focused.length, quickRouteEnd) : focused.length;
    const raw = Math.max(0, Math.min(limit, value));
    const seconds = step && !exact ? Math.min(limit, snapTo(raw, step)) : raw;
    if (quickPlan !== null) {
      if (quickInput) quickPreview.seek(seconds);
      else if (quickSegment === null) consoleActions.log('warn', 'Timeline', '当前路线里这个 Timeline 播了不止一次，请选择具体是哪一次');
      else if (seconds < quickSegment.localStart || seconds > quickSegment.localEnd)
        consoleActions.log('warn', 'Timeline', `所选这次播放只覆盖本地 ${timeText(quickSegment.localStart)}—${timeText(quickSegment.localEnd)} 秒，不能跨圈或外推定位`);
      else quickPreview.seek(quickSegment.start + seconds - quickSegment.localStart);
      return;
    }
    if (!quickMode && inputScheme) {
      if (inputObservation.kind === 'before-start' && !realArchive) {
        consoleActions.log('warn', 'Timeline', `${inputObservation.reason}，当前没有正式输入时间；请先按录制帧定位`);
        return;
      }
      if (inputObservation.kind === 'ready' || inputObservation.kind === 'before-start') {
        const state = getState();
        const samples = runId === null ? [] : archive.inputFrames(runId, inputObservation.input.actorId)
          .filter(sample => samePreviewLoad(sample.source, inputObservation.source));
        const times = new Map(samples.map(sample => [sample.index, sample.seconds]));
        const first = inputObservation.kind === 'before-start' ? samples.find(sample => sample.seconds !== null) : undefined;
        const position = recordedLocalPosition(state, seconds, (frame, index) =>
          samePreviewLoad(frame.source, inputObservation.source) ? times.get(index) ?? null : null, first?.index);
        if (position.kind === 'record') transport.seekRecord(position.index);
        else consoleActions.log('warn', 'Timeline', position.reason);
        return;
      }
      if (realArchive) {
        consoleActions.log('warn', 'Timeline', inputObservation.reason);
        return;
      }
    }
    if (observed === null) {
      if (realArchive) consoleActions.log('warn', 'Timeline', pinNotice ?? callerNotice ?? playbackNotice
        ?? '本帧没有可定位的实际播放，请先按录制帧定位到播放区间');
      else useAuthorCursor.setState({ documentId: focused.documentId, seconds });
      return;
    }
    const state = getState();
    const anchor = realArchive ? pin?.recordIndex : undefined;
    if (anchor !== undefined && state.runId !== null) {
      const frame = cachedFrame(state.runId, anchor) ?? null;
      const anchorRoute = routeObservation({ ...state, frame, hostFrame: frame?.hostFrame ?? null });
      if (anchorRoute.kind === 'unavailable') {
        consoleActions.log('warn', 'Timeline', frame === null ? '固定播放的原始录制帧尚未读取，请先按录制帧回到该播放区间' : anchorRoute.reason);
        return;
      }
    }
    const position = recordedLocalPosition(state, seconds,
      frame => playbackFor(frame, focused.documentId, observed)?.cursorSeconds ?? null, anchor);
    if (position.kind === 'record') transport.seekRecord(position.index);
    else consoleActions.log('warn', 'Timeline', position.reason);
  }, [quickMode, realArchive, quickPlan, quickInput, quickRouteEnd, quickSegment, observed, focused, step, inputScheme, inputObservation,
    runId, pin?.recordIndex, pinNotice, callerNotice, playbackNotice]);
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
      onSelect: () => setParams({ collapsed: rows.filter(row => groupKinds.has(row.kind)).map(row => row.id) }),
    },
  ];

  const origin = focused?.origin ?? 0;
  const timelineName = (segment: TimelineSegment) =>
    schemes.find(scheme => scheme.documentId === segment.documentId)?.name ?? segment.timeline.displayName ?? segment.documentId.slice(0, 8);
  const subject: Subject = {
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
  const clock = useMemo<PreviewClock>(() => {
    const documentId = focused?.documentId ?? null;
    if (quickPlan !== null) return { kind: 'quick', documentId,
      source: quickInput ? { kind: 'input' } : { kind: 'timeline', segment: quickSegment } };
    if (inputScheme) return { kind: 'input', documentId: inputScheme.documentId, source:
      !quickMode && (inputObservation.kind === 'ready' || realArchive && inputObservation.kind === 'before-start') ? 'archive'
        : realArchive ? 'unavailable' : 'author' };
    if (realArchive && authorObservation === null) return { kind: 'archive', documentId, observation: observed };
    return { kind: 'document', documentId, observation: authorObservation };
  }, [quickMode, realArchive, quickPlan, quickInput, quickSegment, focused?.documentId, observed, authorObservation, inputScheme, inputObservation.kind]);
  const curveClock = useWorkbench(useShallow((state): CurveClock => {
    if (clock.kind === 'archive' && timelineNotice !== null) return { kind: 'unavailable', reason: timelineNotice };
    if (quickMode || (clock.kind === 'input' && clock.source !== 'archive')) return { kind: 'unavailable', reason: '当前没有对应的运行观察' };
    const frame = state.frame;
    if (frame === null || frame.hostFrame !== state.hostFrame) return { kind: 'unavailable', reason: '正在读取当前档案帧的来源时钟' };
    if (frame.source === null) return { kind: 'unavailable', reason: '此档案帧没有记录来源时钟' };
    if (frame.source.execution.kind === null || frame.source.execution.identity === null)
      return { kind: 'unavailable', reason: '此档案帧没有记录实际执行身份' };
    const seconds = previewSeconds(state, clock, null);
    return seconds === null ? { kind: 'unavailable', reason: '当前帧没有所选 Timeline 或输入方案的观察时间' }
      : { kind: 'ready', source: frame.source, seconds };
  }));

  return (
    <PreviewClockContext.Provider value={clock}>
    <CurveClockContext.Provider value={curveClock}>
    <div className={styles.window} style={{ ['--sidebar' as string]: `${sidebarWidth}px` }}>
      <TimelineToolbar settings={settings} subject={subject} origin={origin} seek={seek}
        quickPlayback={quickPlayback}
        navigationOrigin={params.origin}
        observations={choices} selectCall={selectCall} pin={quickMode || pin === null ? null : pin.observation} togglePin={togglePin} parents={parents} openParent={openParent}
        observationNotice={quickMode || realArchive && inputScheme ? null : pinNotice ?? callerNotice
          ?? (realArchive ? playbackNotice ?? timelineNotice : (inputScheme ? null : timelineNotice) ?? playbackNotice)} />
      <div className={styles.timeline} ref={setTimelineRef}>
        <div className={styles.header}>
          <div className={styles.timeHead}>
            <AddTrack elements={elements} documentId={focused?.documentId ?? null} caret />
            <label className={styles.search}>
              <SearchIcon className={styles.searchIcon} />
              <TextInput className={styles.searchInput} placeholder="搜索轨道" value={params.filter}
                onChange={event => setParams({ filter: event.currentTarget.value })} aria-label="搜索轨道" />
            </label>
          </div>
          <Ruler bounds={bounds} origin={origin} rate={rate} seekAt={seekAt} />
        </div>
        <CurveViewContext.Provider value={curveView}>
          <RouteActionsContext.Provider value={routeActions}>
            <Rows elements={elements} rows={rows} collapsed={params.collapsed} toggle={toggle} toggleCurves={toggleCurves} enter={enter} frameAll={frameAll} inputRoots={inputTimeline?.roots} />
          </RouteActionsContext.Provider>
        </CurveViewContext.Provider>
        <div className={styles.footer}>
          <div className={styles.footHead} />
          <TimeScroller bounds={focused ? { start: focused.origin, end: focused.origin + focused.length } : bounds}
            onRangeChanged={onRangeChanged} />
        </div>
        <Overlay bounds={bounds} origin={origin} follow={params.follow} onRangeChanged={onRangeChanged} />
      </div>
    </div>
    </CurveClockContext.Provider>
    </PreviewClockContext.Provider>
  );
}

// ── 行：只渲染可见行 ──

// 框选中的片段与标记（data-box 键）；照 Timeline Editor 在轨道空白处左键拖出选框。
const BoxedContext = createContext<ReadonlySet<string>>(new Set());

// 框选多个后拖动其中的标记：由 ClipEditing 按拖动片段的同一流程整体平移（指针位置由它在捕获阶段记录）。
interface GroupMove { start(box: string, x: number, y: number): void; move(): void; end(): void; cancel(): void }
const GroupMoveContext = createContext<RefObject<GroupMove | null>>({ current: null });
interface Box { x0: number; y0: number; x1: number; y1: number }

function Rows({ elements, rows, collapsed, toggle, toggleCurves, enter, frameAll, inputRoots }: {
  elements: SkillElements | null;
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
  const groupMove = useRef<GroupMove | null>(null);
  // 框选后选中的是第一个片段或标记；其它面板或单击换了选中时，框选结果作废。
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
        // 作者模式 Delete：删除框选的片段与标记，否则删除选中的片段、标记或轨道。
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
      <GroupMoveContext.Provider value={groupMove}>
      <div style={{ height: offsets[first] }} />
      {rows.slice(first, last).map((row, offset) => {
        const nest = nesting[first + offset];
        switch (row.kind) {
          case 'group': return <GroupLine key={row.id} elements={elements} row={row} nest={nest} open={!collapsedSet.has(row.id)} toggle={toggle} enter={enter} />;
          case 'track': return <TrackLine key={row.id} elements={elements} row={row} nest={nest} toggleCurves={toggleCurves} />;
          case 'inputGroup':
            return <InputGroupLine key={row.id} nest={nest} open={!collapsedSet.has(row.id)} toggle={toggle} roots={inputRoots} />;
          case 'inputTrack': return <InputTrackLine key={row.id} row={row} nest={nest} />;
          case 'curves': return <CurveGroupLine key={row.id} nest={nest} open={!collapsedSet.has(row.id)} toggle={toggle} />;
          case 'curve': return <CurveLine key={row.id} row={row} nest={nest} />;
          case 'receivedGroup':
            return <ReceivedGroupLine key={row.id} nest={nest} received={row.received} open={!collapsedSet.has(row.id)} toggle={toggle} />;
          case 'received': return <ReceivedLine key={row.id} row={row} nest={nest} />;
          case 'routeGroup':
            return <RouteGroupLine key={row.id} nest={nest} route={row.route} open={!collapsedSet.has(row.id)} toggle={toggle} />;
          case 'routeLane': return <RouteLaneLine key={row.id} row={row} nest={nest} />;
        }
      })}
      <div style={{ height: total - offsets[last] }} />
      </GroupMoveContext.Provider>
      </BoxedContext.Provider>
      {step !== null && <ClipEditing elements={elements} rows={rows} offsets={offsets} scroller={scroller} step={step} mover={groupMove} />}
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
// meta 为淡色的补充信息（如时间范围）。悬停补全被截断的名字；有 tip 时改为名字加这条说明。
function GroupToggle({ open, name, meta, tip, disabled, onToggle, onDoubleClick }: {
  open: boolean; name: string; meta?: string; tip?: string; disabled?: boolean;
  onToggle(): void; onDoubleClick?(): void;
}) {
  return (
    <Tip name={name} content={tip}>
      <button className={styles.groupToggle} onClick={onToggle} onDoubleClick={onDoubleClick} aria-expanded={open} disabled={disabled}>
        <span className={styles.fold}><FoldoutIcon open={open} /></span>
        <span className={styles.groupName} data-name>{name}</span>
        {meta && <span className={styles.groupMeta}>{meta}</span>}
      </button>
    </Tip>
  );
}

// 轨道头（TimelineTrackGUI.DrawTrackHeader）：左侧 4 像素类别色条、16 像素类型图标、轨道名，右侧为按钮组与选项按钮。
// rename 为改名输入框，给出时代替轨道名；below 为展开曲线时轨道头下部的内容（曲线通道列表）。
// 悬停补全被截断的轨道名；有 tip 时改为轨道名加这条说明。
function TrackHead({ kind, color, selected, name, tip, onSelect, onRename, rename, children, below }: {
  kind: TrackKind; color?: string; selected?: boolean; name: string; tip?: string;
  onSelect?(): void; onRename?(): void; rename?: ReactNode; children?: ReactNode; below?: ReactNode;
}) {
  const label = <><span className={styles.trackIcon}><TrackIcon kind={kind} /></span><span className={styles.name} data-name>{name}</span></>;
  const head = <>
    {rename ? <><span className={styles.trackIcon}><TrackIcon kind={kind} /></span>{rename}</>
      : <Tip name={name} content={tip}>
        {onSelect ? <button className={styles.headSelect} onClick={onSelect} onDoubleClick={onRename}>{label}</button>
          : <span className={styles.headSelect}>{label}</span>}
      </Tip>}
    {children}
  </>;
  return (
    <div className={styles.headButton} data-selected={selected || undefined} style={{ ['--tc' as string]: color ?? trackColors[kind] }}>
      {below ? <div className={styles.headColumn}><div className={styles.headRow}>{head}</div>{below}</div> : head}
    </div>
  );
}

const GroupLine = memo(function GroupLine({ elements, row, nest, open, toggle, enter }: {
  elements: SkillElements | null;
  row: GroupRow; nest: Nest | undefined; open: boolean; toggle(id: string): void; enter(documentId: string): void;
}) {
  const segment = row.segment;
  const { range } = useTimelineContext();
  return (
    <Line id={row.id} nest={nest} laneClass={styles.groupLane} headClass={styles.groupHead} head={
      <>
        <GroupToggle open={open} onToggle={() => toggle(row.id)}
          name={segment.timeline.displayName ?? segment.documentId.slice(0, 8)}
          meta={`${segment.start.toFixed(2)}–${segment.end.toFixed(2)} s`}
          tip={row.focused ? undefined : '双击进入这个 Timeline'}
          onDoubleClick={row.focused ? undefined : () => enter(segment.documentId)} />
        {!row.focused && <AddTrack elements={elements} documentId={segment.documentId} />}
      </>
    }>
      {!open && clipUnions(segment).filter(span => span.end >= range.start && span.start <= range.end).map((span, index) => (
        <Positioned key={index} start={span.start} end={span.end} className={styles.clipUnion}
          tip={`${span.start.toFixed(3)}–${span.end.toFixed(3)} s`} />
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
  applyTimelineEdits(getState().timelines, authoringDocument(useAuthoring.getState()).edited, authoringDocument(useAuthoring.getState()).removed).find(item => item.documentId === documentId)
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
  const document = authoringDocument(useAuthoring.getState());
  const timeline = applyTimelineEdits(getState().timelines, document.edited, document.removed).find(item => item.documentId === documentId);
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
function useTrackMenu(row: TrackRow, at: { current: number }, rename: () => void, elements: SkillElements | null): MenuEntry[] {
  const author = useAuthoring(state => state.author && state.target !== null);
  const version = useAuthoring(state => authoringDocument(state).version);
  // 右键片段时先选中它，菜单随选中重新给出复制与切分。
  useWorkbench(state => state.selection);
  const snap = useContext(SnapContext);
  const step = snap ?? 1 / 65536;
  const schemes = useInputSchemes();
  const input = useInputContext(row.timeline.documentId);
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
        void authoringActions.addClipAt(documentId, row.track.id, template, start, start + Math.max(step, Math.round(0.5 / step) * step))
          .then(select, error => consoleActions.error('Timeline', error));
      },
    })),
    { label: '标记', onSelect: () => {
      void authoringActions.addMarkerAt(documentId, row.track.id, local())
        .then(id => { if (id) select(id); }, error => consoleActions.error('Timeline', error));
    } },
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

const TrackLine = memo(function TrackLine({ elements, row, nest, toggleCurves }: {
  elements: SkillElements | null; row: TrackRow; nest: Nest | undefined; toggleCurves(id: string): void;
}) {
  const clock = useContext(PreviewClockContext);
  const selected = useSelected(row.selectId, 'track');
  const { range, getValueFromScreenX } = useTimelineContext();
  const at = useRef(0);
  const [naming, setNaming] = useState(false);
  const author = useAuthoring(state => state.author && state.target !== null);
  const menu = useTrackMenu(row, at, () => setNaming(true), elements);
  const documentId = row.timeline.documentId;
  const animationCatalog = useTimelineResourceCatalog(documentId, animationReferenceKind);
  const step = useContext(SnapContext) ?? 1 / 60;
  // 运行秒数换成本 Timeline 的本地秒数，按吸附步长取整。
  const snapLocal = (seconds: number) => Math.max(0, snapTo(seconds - row.segment.origin, step));
  const visible = row.clips.filter(clip => clip.end >= range.start && clip.start <= range.end);
  const markers = row.markers.filter(marker => marker.seconds >= range.start && marker.seconds <= range.end);
  // 照 Clip.UpdateMix 只给可混合的片段算混合：轨道合同为 Blend 的动画与位移曲线轨。
  const blend = elements !== null && trackOverlap(elements, documentId, row.track.id) === 'Blend';
  const mixes = useMemo(() => new Map(row.clips.map(clip => [clip.key, blend ? clipMix(clip, row.clips) : noMix])), [row.clips, blend]);
  // 能放带曲线通道片段的轨道可展开曲线（预览输入轨没有）。
  const hasCurves = elements !== null && row.trackKind !== 'in' && trackHasCurves(elements, documentId, row.track.id);
  const animationLibrary = useAnimationLibrary(author && row.trackKind === 'anim');
  const curves = hasCurves && row.curves ? row.curves : 0;
  const segment = row.segment;
  const select = () => selectTimelineObject(clock, 'track', row.selectId);
  const muted = row.track.muted;
  // 轨道头的选项按钮（⋮）与右键菜单相同，在游标处添加；打开前选中这条轨道。
  const openOptions = () => {
    select();
    const seconds = previewSeconds(getState(), clock, authorCursorOf(useAuthorCursor.getState(), clock));
    at.current = segment.origin + (seconds ?? 0);
  };
  return (
    <ContextMenu entries={menu}>
    <div className={styles.menuArea} onDragOver={event => {
      if (author && row.trackKind === 'anim' && acceptsReferenceDrag(event.dataTransfer, animationReferenceKind)) {
        event.preventDefault();
        event.dataTransfer.dropEffect = 'copy';
      }
    }} onDrop={event => {
      if (!author || row.trackKind !== 'anim') return;
      event.preventDefault();
      const result = readAnimationDrop(event.dataTransfer, animationLibrary, animationCatalog);
      if (result.kind === 'rejected') { consoleActions.log('warn', 'Timeline', result.reason); return; }
      if (!elements) { consoleActions.log('warn', 'Timeline', '片段作者合同尚未取得'); return; }
      const template = clipTemplatesFor(elements, documentId, row.track.id)
        .find(item => item.sourceAsset?.choiceKind === animationReferenceKind);
      if (!template?.sourceAsset) { consoleActions.log('warn', 'Timeline', '当前轨道没有提供正式动画源片段合同'); return; }
      const value = cloneJson(template.value);
      setIn(value, template.sourceAsset.serializedMemberPath, result.clip.value);
      const start = snapLocal(getValueFromScreenX(event.clientX));
      void authoringActions.addClipAt(documentId, row.track.id, { ...template, value }, start, start + result.clip.duration)
        .then(id => selectTimelineObject(clock, 'clip', clipSelectId(documentId, id)), error => consoleActions.error('Timeline', error));
    }} onContextMenuCapture={event => {
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
        onRename={author ? () => setNaming(true) : undefined} tip={author ? '单击选中 · 双击改名 · 右键打开轨道菜单' : undefined}
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
          {hasCurves && <Tip content={curves ? '收起片段曲线' : '展开片段曲线'}>
            <button className={styles.suiteButton} aria-pressed={curves > 0} onClick={() => toggleCurves(row.id)}>
              <CurveIcon />
            </button>
          </Tip>}
          {author && <Tip content={muted ? '取消静音' : '静音'}>
            <button className={styles.suiteButton} aria-pressed={muted}
              onClick={() => authoringActions.setTrackValue(documentId, row.track.id, ['m_PersistentMuted'], !muted)}>
              <MuteIcon muted={muted} />
            </button>
          </Tip>}
        </span>}
        {author && <span className={styles.options} onPointerDown={openOptions}>
          <MenuButton label="轨道菜单" variant="ghost" className={styles.optionsButton} iconOnly entries={menu}><OptionsIcon /></MenuButton>
        </span>}
      </TrackHead>
    }>
      {visible.map(clip => <Clip key={clip.key} elements={elements} clip={clip} kind={row.trackKind} mix={mixes.get(clip.key)!}
        documentId={row.timeline.documentId} segmentId={segment.id} trackId={row.track.id} />)}
      {curves > 0 && elements && <ClipCurves row={row} elements={elements} mixes={mixes} author={author} height={curves} />}
      {markers.map(marker => {
        const markerId = marker.selectId.split('/')[1];
        return (
          <Marker key={marker.key} boxKey={marker.key} seconds={marker.seconds} kind={row.trackKind} label={`${row.track.displayName} 标记`} text={marker.label} graphId={marker.graphId}
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

function Positioned({ start, end, className, children, style, tip }: {
  start: number; end: number; className: string; children?: ReactNode; style?: CSSProperties; tip?: string;
}) {
  const { range, valueToPixels } = useTimelineContext();
  return (
    <Tip content={tip}>
      <div className={className} style={{ ...style, left: valueToPixels(start - range.start), width: Math.max(1, valueToPixels(end - start)) }}>
        {children}
      </div>
    </Tip>
  );
}

const Clip = memo(function Clip({ elements, clip, kind, mix, documentId, segmentId, trackId }: {
  elements: SkillElements | null;
  clip: ClipInstance; kind: TrackKind; mix: ClipMix; documentId: string; segmentId: string; trackId: string;
}) {
  const author = useAuthoring(state => state.author && state.target !== null);
  useAuthoring(state => authoringDocument(state).version);
  const source = author && elements ? clipSnapSource(elements, documentId, clip.clip.id)?.source : null;
  const animation = source?.choiceKind === animationReferenceKind ? source : null;
  const library = useAnimationLibrary(animation !== null);
  const catalog = useTimelineResourceCatalog(documentId, animationReferenceKind);
  const resource = animation && library.kind === 'ready'
    ? library.library.clips.find(item => item.value === animation.value) : undefined;
  const choices = animation && catalog.kind === 'ready' ? catalog.items.filter(item => item.value === animation.value) : [];
  const referenceInvalid = animation !== null && catalog.kind === 'ready' && (choices.length !== 1 || choices[0].conflict !== null);
  const referenceIssue = !animation ? null : catalog.kind === 'unavailable' ? catalog.reason : referenceInvalid ? catalog.invalidReason
    : library.kind === 'loading' ? '动画素材库清单尚未取得' : library.kind === 'unavailable' ? library.reason
      : resource ? null : '作者引用有效，当前素材库尚未提供该资源的预览片段。';
  const label = resource?.name ?? (referenceInvalid ? `失效动画 · ${String(animation?.value)}` : choices[0]?.name ?? clip.label);
  const edit = useMemo<ClipEdit>(() => ({ documentId, segmentId, trackId, clipId: clip.clip.id, origin: clip.start - clip.clip.startSeconds }),
    [documentId, segmentId, trackId, clip]);
  // 淡入淡出只在打开的技能 JSON 中有值（运行回执不带）。
  const ease = useMemo(() => author && clip.clip.easeInSeconds !== undefined ? {
    in: clip.clip.easeInSeconds, out: clip.clip.easeOutSeconds ?? 0,
    set: (easeIn: number | null, easeOut: number | null) => authoringActions.setClipEase(documentId, clip.clip.id, easeIn, easeOut),
  } : undefined, [author, clip, documentId]);
  return (
    <ClipBox boxKey={clip.key} selectId={clip.selectId} kind={kind} label={label} start={clip.start} end={clip.end}
      enter={clip.enter} exit={clip.exit} graphId={clip.clip.graphId} edit={author ? edit : undefined} ease={ease} mix={mix}
      referenceInvalid={referenceInvalid} referenceIssue={referenceIssue}
      onAnimationDrop={author && kind === 'anim' ? transfer => {
        if (!animation) { consoleActions.log('warn', 'Timeline', '当前片段未提供正式动画源资产字段，原值已保留'); return; }
        const result = readAnimationDrop(transfer, library, catalog);
        if (result.kind === 'rejected') { consoleActions.log('warn', 'Timeline', result.reason); return; }
        authoringActions.setClipValues(documentId, clip.clip.id, [{ keys: animation.serializedMemberPath, value: result.clip.value }]);
      } : undefined} />
  );
});

// 片段拖动写回作者 JSON 所需的位置：所属 Timeline、所在路线段、轨道、片段身份，以及本段的运行秒数零点。
interface ClipEdit { documentId: string; segmentId: string; trackId: string; clipId: string; origin: number }

interface ClipEase { in: number; out: number; set(easeIn: number | null, easeOut: number | null): void }
type EaseDraft = { side: 'in' | 'out'; seconds: number };
// 片段一端的过渡：与相邻片段重叠为混合，否则有淡入淡出时为淡变（BlendKind）；width 为像素宽。
type ClipBlend = { kind: 'mix' | 'ease'; width: number } | null;

// 淡入淡出手柄（作者模式）：照 Unity 不画出来，顶边两端 7 像素见方的热区换成调整光标，左右拖动改时长，按片段网格的步长取整（时长不带偏移）。
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
  // 热区中心对准淡入终点、淡出起点，贴边时收在片段内。
  return (
    <>
      {sides.in && <Tip content={`淡入 ${easeIn.toFixed(3)} s · 左右拖动`}>
        <span className={styles.easeHandle} style={{ left: Math.max(0, Math.min(width - 7, inPx - 3.5)) }} {...handle('in')} />
      </Tip>}
      {sides.out && <Tip content={`淡出 ${easeOut.toFixed(3)} s · 左右拖动`}>
        <span className={styles.easeHandle} style={{ left: Math.max(0, Math.min(width - 7, width - outPx - 3.5)) }} {...handle('out')} />
      </Tip>}
    </>
  );
}

// 片段底形（ClipDrawer.DrawDefaultClip）：中段底色由按钮背景画（CSS 按 --in/--out 在两端留空），
// 这里画两端的混合与淡变三角、淡变暗底、底边 4 像素类别色和压在色条上的默认边线。视口高度与轨道同为 30，横向 1 单位即 1 像素。
function ClipShape({ width, blendIn, blendOut }: { width: number; blendIn: ClipBlend; blendOut: ClipBlend }) {
  const w = Math.max(1, width), h = trackHeight;
  const inW = blendIn?.width ?? 0, outW = blendOut?.width ?? 0;
  return (
    <svg className={styles.clipShape} viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" aria-hidden>
      {blendIn?.kind === 'ease' && <rect className={styles.easeBack} width={inW} height={h} />}
      {blendOut?.kind === 'ease' && <rect className={styles.easeBack} x={w - outW} width={outW} height={h} />}
      {blendIn && <polygon className={styles.blendShape} points={blendIn.kind === 'mix' ? `0,0 ${inW},${h} ${inW},0` : `0,${h} ${inW},${h} ${inW},0`} />}
      {blendOut && <polygon className={styles.blendShape} points={`${w - outW},0 ${w - outW},${h} ${w},${h}`} />}
      <rect className={styles.clipSwatch} y={h - 4} width={w} height={4} />
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
// 照 Unity 片段：中段底色、底边 4 像素类别色、居中的白字片段名，放不下时不写；名字没写或被截断时悬停提示完整名字（Tip name），写全时不提示；enter 之前与 exit 之后的部分压暗。
// 作者模式下给出 edit 时可拖动平移、拖两端改起止。
function ClipBox({ boxKey, selectId, kind, label, start, end, enter = -Infinity, exit, graphId, edit, ease, mix, referenceInvalid, referenceIssue, onAnimationDrop }: {
  boxKey: string; selectId: string; kind: TrackKind; label: string; start: number; end: number; enter?: number; exit: number;
  graphId: string | null; edit?: ClipEdit; ease?: ClipEase; mix?: ClipMix;
  referenceInvalid?: boolean; referenceIssue?: string | null; onAnimationDrop?(transfer: DataTransfer): void;
}) {
  const { setNodeRef, itemStyle, listeners, attributes } = useItem({
    id: boxKey, span: { start, end }, disabled: !edit, data: edit ? { edit } : undefined, resizeHandleWidth: 12,
  });
  const { range, valueToPixels } = useTimelineContext();
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
  const dimmed = start >= exit || end <= enter;
  const cut = !dimmed && end > exit ? valueToPixels(exit - start) : null;
  const skipped = !dimmed && start < enter ? valueToPixels(enter - start) : null;
  const mixIn = mix?.in ?? 0, mixOut = mix?.out ?? 0;
  // 照 Clip.UpdateMix：自身淡入不超过时长减去混出，淡出不超过时长减去混入。
  const selfIn = Math.min(ease?.in ?? 0, end - start - mixOut), selfOut = Math.min(ease?.out ?? 0, end - start - mixIn);
  const easeIn = easeDraft?.side === 'in' ? easeDraft.seconds : selfIn;
  const easeOut = easeDraft?.side === 'out' ? easeDraft.seconds : selfOut;
  const blendIn: ClipBlend = mixIn > 0 ? { kind: 'mix', width: valueToPixels(mixIn) } : easeIn > 0 ? { kind: 'ease', width: valueToPixels(easeIn) } : null;
  const blendOut: ClipBlend = mixOut > 0 ? { kind: 'mix', width: valueToPixels(mixOut) } : easeOut > 0 ? { kind: 'ease', width: valueToPixels(easeOut) } : null;
  const inWidth = blendIn?.width ?? 0, outWidth = blendOut?.width ?? 0;
  // 照 Unity 片段按整段画、伸出视口的部分被裁掉；名字只在可见的中段里居中。
  const labelStart = Math.max(inWidth, valueToPixels(range.start - start)), labelEnd = Math.max(outWidth, valueToPixels(end - range.end));
  const labelWidth = width - labelStart - labelEnd - clipLabelPadding * 2;
  return (
    // 片段固定高 30：轨道展开曲线时行变高，曲线画在片段下方。
    <div ref={setNodeRef} style={{ ...itemStyle, height: trackHeight, visibility: moving ? 'hidden' : undefined }} className={styles.item}
      {...(edit ? { ...listeners, ...attributes } : {})}>
      <div className={styles.itemContent}>
        <Tip name={label} content={referenceIssue || undefined}>
        <button className={styles.clip} data-box={boxKey} data-select={selectId} data-selected={selected || undefined}
          data-dim={dimmed || undefined} data-editable={edit ? true : undefined} data-invalid={invalid || undefined}
          data-reference-invalid={referenceInvalid || undefined} aria-invalid={referenceInvalid || undefined}
          style={{ ['--tc' as string]: trackColors[kind], ['--in' as string]: `${inWidth}px`, ['--out' as string]: `${outWidth}px`,
            ['--label-start' as string]: `${labelStart}px`, ['--label-end' as string]: `${labelEnd}px` }}
          onDragOver={event => {
            if (onAnimationDrop && acceptsReferenceDrag(event.dataTransfer, animationReferenceKind)) {
              event.preventDefault();
              event.stopPropagation();
              event.dataTransfer.dropEffect = 'copy';
            }
          }}
          onDrop={event => {
            if (!onAnimationDrop) return;
            event.preventDefault();
            event.stopPropagation();
            onAnimationDrop(event.dataTransfer);
          }}
          onClick={() => selectTimelineObject(clock, 'clip', selectId)}
          onDoubleClick={() => openClipGraph(graphId, label, clock)}>
          <ClipShape width={width} blendIn={blendIn} blendOut={blendOut} />
          {labelWidth > clipLabelMinWidth && <span className={styles.clipLabel} data-name>{label}</span>}
          {ease && <EaseHandles ease={{ ...ease, in: mixIn > 0 ? mixIn : selfIn, out: mixOut > 0 ? mixOut : selfOut }}
            start={start} end={end} draft={easeDraft} setDraft={setEaseDraft} sides={{ in: mixIn <= 0, out: mixOut <= 0 }}
            step={(edit && clipGrid(edit.documentId, edit.clipId)?.step) ?? minimumStep} />}
          {cut !== null && <i className={styles.clipCut} style={{ left: cut }} />}
          {skipped !== null && <i className={styles.clipCut} style={{ left: 0, right: 'auto', width: skipped }} />}
          {edit && <><i className={styles.clipResize} data-side="start" /><i className={styles.clipResize} data-side="end" /></>}
        </button>
        </Tip>
      </div>
    </div>
  );
}

// 照 Timeline Editor：双击片段或标记打开它的节点图。
function openClipGraph(graphId: string | null, label: string, clock: PreviewClock) {
  const state = getState();
  const graph = clipGraph(state.graphs, graphId);
  if (!graph) {
    consoleActions.log('warn', 'Timeline', graphId ? `${label} 的节点图不在本次运行中` : `${label} 没有节点图`);
    return;
  }
  const caller = state.runId === null || clock.kind !== 'document' || clock.observation === null ? null
    : { runId: state.runId, call: clock.observation.call };
  selectionActions.showGraph({ key: graphKey(graph), panelId: openPanel('graph').id, observation: null, caller, origin: null });
}

// 标记：作者标记与预览输入的单次按键共用；单击选中（有 selectId 时），双击打开节点图。
// 时刻处一根 1 像素类别色细线贯穿整行，顶端一个朝下的小三角（MarkerTriangle）尖端对准时刻；所触发的内容（text）
// 照 Unity 不写名字，悬停提示（Tip name）。未绑定节点图时三角只留轮廓。
// 作者模式给出 move 时可左右拖动（MoveMarker），拖动中按 snap 取整显示，松开写回；标记不许重叠，
// 拖到同轨已有标记的时刻（taken）时细线变红，松开不写回。框选中的标记与片段同样显示选中；
// 框选了多个时拖动它与框选中的片段、标记整体平移（GroupMove），平移中按 shift 原地显示。
function Marker({ boxKey, seconds: shown, kind, label, text, graphId, selectId, move, snap, taken }: {
  boxKey: string; seconds: number; kind: TrackKind; label: string; text: string | null; graphId: string; selectId?: string;
  move?(seconds: number): void; snap?(seconds: number): number; taken?(seconds: number): boolean;
}) {
  const { range, valueToPixels, getValueFromScreenX } = useTimelineContext();
  const panelId = usePanelId();
  const boxedSet = useContext(BoxedContext);
  const boxed = boxedSet.has(boxKey);
  const group = useContext(GroupMoveContext);
  const [dragging, setDragging] = useState<{ x: number; y: number; seconds: number; group: boolean; started: boolean } | null>(null);
  const moving = useClipEditing(useShallow(state => state.drag?.panelId === panelId && state.drag.markers.includes(boxKey)
    ? { shift: state.drag.shift, invalid: state.drag.invalid } : null));
  const seconds = moving ? shown + moving.shift : dragging?.seconds ?? shown;
  const blocked = moving ? moving.invalid : !!dragging && dragging.seconds !== shown && !!taken?.(dragging.seconds);
  const clock = useContext(PreviewClockContext);
  const selected = useWorkbench(state => !!selectId && state.selection?.kind === 'clip' && state.selection.id === selectId) || boxed;
  const dragged = useRef(false);
  const left = valueToPixels(seconds - range.start);
  return (
    <Tip name={text ?? '未绑定节点图'}>
    <button className={styles.marker} data-box={boxKey} data-selected={selected || undefined} data-unbound={!text || undefined}
      data-invalid={blocked || undefined} data-select={selectId} style={{ left, height: trackHeight, ['--tc' as string]: trackColors[kind] }}
      onPointerDown={event => {
        dragged.current = false;
        if (!move || event.button !== 0) return;
        event.currentTarget.setPointerCapture(event.pointerId);
        setDragging({ x: event.clientX, y: event.clientY, seconds: shown, group: boxed && boxedSet.size > 1, started: false });
      }}
      onPointerMove={event => {
        // 按下后移动超过 3 像素才算拖动，单击仍是选中。
        if (!dragging || !snap || (!dragging.started && Math.abs(event.clientX - dragging.x) < 3)) return;
        if (!dragging.group) setDragging({ ...dragging, seconds: snap(getValueFromScreenX(event.clientX)), started: true });
        else if (dragging.started) group.current?.move();
        else {
          group.current?.start(boxKey, dragging.x, dragging.y);
          setDragging({ ...dragging, started: true });
        }
      }}
      onPointerUp={() => {
        if (dragging?.group && dragging.started) {
          dragged.current = true;
          group.current?.end();
        } else if (dragging && dragging.seconds !== shown && !blocked) move!(dragging.seconds);
        setDragging(null);
      }}
      onPointerCancel={() => {
        if (dragging?.group && dragging.started) group.current?.cancel();
        setDragging(null);
      }}
      // 整体平移后保留框选，不改成单选这个标记。
      onClick={() => { if (selectId && !dragged.current) selectTimelineObject(clock, 'clip', selectId); }}
      onDoubleClick={() => openClipGraph(graphId, label, clock)}>
      <MarkerTriangle className={styles.markerHead} />
    </button>
    </Tip>
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
    {reason ? <Tip name={reason}><span className={styles.curveNotice} data-name>{reason}</span></Tip>
      : !curves?.length ? <span className={styles.curveNotice}>此档案未声明曲线</span> : null}
  </Line>;
}

function CurveLine({ row, nest }: { row: CurveRow; nest: Nest | undefined }) {
  const clock = useContext(CurveClockContext);
  const { range } = useTimelineContext();
  const { runId, frame, hostFrame, image, curveVersion } = useWorkbench(useShallow(state => ({
    runId: state.runId, frame: state.archiveFrame, hostFrame: state.hostFrame,
    image: state.archiveImage, curveVersion: state.archiveCurveVersion,
  })));
  const selectId = runId === null ? null : `curve/${runId}/${row.curve.id}`;
  const selected = useSelected(selectId ?? '', 'track');
  const records = useMemo(() => runId === null ? [] : archive.curveSamples(runId, row.curve.id), [runId, row.curve.id, curveVersion]);
  const matching = useMemo(() => clock.kind === 'unavailable' ? [] : records.filter(point => samePreviewLoad(point.sample.source, clock.source)), [records, clock]);
  const samples = useMemo(() => runId === null || clock.kind === 'unavailable' ? [] : matching.flatMap(({ sample, index }) => {
    const seconds = clock.seconds + sample.source.clock.seconds - clock.source.clock.seconds;
    return seconds < range.start || seconds > range.end ? [] : [{ sample, seconds,
      position: transport.recordPosition(runId, index, sample.source),
    }];
  }), [matching, runId, clock, range]);
  const min = samples.length ? Math.min(...samples.map(point => point.sample.value)) : 0;
  const max = samples.length ? Math.max(...samples.map(point => point.sample.value)) : 0;
  const record = frame?.curves[row.curve.id];
  const reason = clock.kind === 'unavailable' ? clock.reason : record?.kind === 'failed' ? record.reason
    : record?.kind === 'sampled' ? record.availability.find(item =>
      (item.channel === row.curve.channel || item.channel === 'data-curves') && !item.available)?.reason ??
      (records.length > 0 && matching.length === 0
        ? '曲线来源与当前帧的执行或内容身份不匹配' : null) : '正在读取曲线';
  const select = () => { if (selectId !== null) selectionActions.select({ kind: 'track', id: selectId }); };
  // 采样点可能很多，共用一个提示。
  const [sampleTips] = useState(createTipGroup);
  return <Line id={row.id} nest={nest} height={curveHeight} selected={selected} headClass={styles.trackHead} head={
    <TrackHead kind="motion" selected={selected} name={row.curve.displayName} onSelect={selectId === null ? undefined : select}
      tip={`${row.curve.clock} · ${row.curve.coordinates} · ${row.curve.unit}`} />
  }>
    {reason ? <Tip name={reason}><span className={styles.curveNotice} data-name>{reason}</span></Tip> : <>
      <svg className={styles.curvePlot} viewBox="0 0 1000 64" preserveAspectRatio="none" aria-label={row.curve.displayName}>
        {samples.map(({ sample, seconds, position }, index) => {
          const matchingImage = sample.presentationSequence === null || image !== null && image.kind !== 'unavailable' &&
            sample.presentationSequence === image.metadata.presentationSequence;
          return <GroupTip key={`${sample.source.clock.position.hostFrame}/${sample.presentationSequence ?? 'logic'}/${index}`} group={sampleTips}
            content={`${sample.value} ${row.curve.unit}\n${sample.source.clock.positionDisplayName} ${sample.source.clock.position.hostFrame} · ${row.curve.clock}\n来源时间 ${sample.source.clock.seconds} s\n${position.kind === 'record' ? `录制位置 ${position.seconds} s` : position.reason}\n原值 ${sample.rawValue}${sample.presentationSequence === null ? '' : `\n表现序号 ${sample.presentationSequence} · Tick ${sample.presentationTick}`}`}>
            <circle cx={(seconds - range.start) / (range.end - range.start) * 1000}
              cy={max === min ? 32 : 57 - (sample.value - min) / (max - min) * 50} r={2.5}
              data-current={sample.source.clock.position.hostFrame === hostFrame && matchingImage || undefined}
              aria-disabled={position.kind === 'unavailable' || undefined}
              onClick={position.kind === 'unavailable' ? undefined : () => {
                select();
                if (getState().runId !== runId) { consoleActions.log('warn', 'Timeline', '所选档案已变化，未定位旧采样点'); return; }
                transport.seekRecord(position.index);
              }} />
          </GroupTip>;
        })}
      </svg>
      <TipGroup group={sampleTips} />
      {samples.length > 0 && <span className={styles.curveScale}>{max.toPrecision(4)} / {min.toPrecision(4)} {row.curve.unit}</span>}
    </>}
  </Line>;
}

function ReceivedGroupLine({ nest, received, open, toggle }: {
  nest: Nest | undefined; received: ReceivedInput; open: boolean; toggle(id: string): void;
}) {
  const frameName = useWorkbench(state => state.summary?.frameDisplayName ?? '宿主帧');
  const notice = !received.keys.length && !received.missing.length ? '已读帧内没有非中性输入' : null;
  const scope = received.ranges.map(range => range.first === range.last ? range.first : `${range.first}—${range.last}`).join('、');
  return <Line id="received" nest={nest} headClass={styles.groupHead} laneClass={styles.groupLane} head={<>
    <GroupToggle open={open} name="实际收到的输入" meta="已读帧" onToggle={() => toggle('received')}
      tip={`已读${frameName} ${scope}，随游标分页读取`} />
  </>}>
    {notice && <Tip name={notice}><span className={styles.curveNotice} data-name>{notice}</span></Tip>}
  </Line>;
}

function ReceivedLine({ row, nest }: { row: ReceivedRow; nest: Nest | undefined }) {
  const { range } = useTimelineContext();
  const visible = (span: ReceivedSpan) => span.end >= range.start && span.start <= range.end;
  return <Line id={row.id} nest={nest} headClass={styles.trackHead} head={<TrackHead kind="in" name={row.name} />}>
    {row.spans.filter(visible).map(span => (
      <Positioned key={`s${span.start}`} start={span.start} end={span.end} tip={`${span.label} · ${span.start.toFixed(3)}—${span.end.toFixed(3)} s`}
        className={row.missing ? styles.receivedMissing : styles.receivedSpan} />
    ))}
    {row.requests.filter(visible).map(span => (
      <Positioned key={`r${span.start}`} start={span.start} end={span.end} tip={`请求 ${span.label} · ${span.start.toFixed(3)} s`}
        className={styles.receivedRequest} />
    ))}
  </Line>;
}

// ── 路线动态轨：路线组头与路线条（快档推导、真档已读实际播放） ──

const RouteActionsContext = createContext<{ seek(bar: RouteBar): void; open(bar: RouteBar): void }>({ seek: () => {}, open: () => {} });

function RouteGroupLine({ nest, route, open, toggle }: { nest: Nest | undefined; route: RouteView; open: boolean; toggle(id: string): void }) {
  return <Line id="route" nest={nest} headClass={styles.groupHead} laneClass={styles.groupLane} head={
    <GroupToggle open={open} name={route.title} meta={route.meta} tip={route.tip} onToggle={() => toggle('route')} />
  }>
    {route.notice && <Tip name={route.notice}><span className={styles.curveNotice} data-name>{route.notice}</span></Tip>}
  </Line>;
}

const RouteLaneLine = memo(function RouteLaneLine({ row, nest }: { row: RouteLaneRow; nest: Nest | undefined }) {
  const { range, valueToPixels } = useTimelineContext();
  const clock = useContext(PreviewClockContext);
  const actions = useContext(RouteActionsContext);
  const bars = row.bars.filter(bar => bar.end >= range.start && bar.start <= range.end);
  // 游标所在的路线条高亮；只比较键串，游标移动不重画整行。
  const active = usePreviewSelect(clock, seconds => seconds === null ? ''
    : bars.filter(bar => bar.start <= seconds && seconds < bar.end).map(bar => bar.key).join('\n'));
  const current = new Set(active.split('\n'));
  return <Line id={row.id} nest={nest} headClass={styles.trackHead} head={
    <TrackHead kind="act" name={row.name} tip="单击定位到该次播放 · 双击进入所播 Timeline" />
  }>
    {bars.map(bar => {
      const left = valueToPixels(bar.start - range.start), width = Math.max(2, valueToPixels(bar.end - bar.start));
      return <Tip key={bar.key} name={bar.label} content={bar.tip}>
        <button className={styles.routeBar} data-active={current.has(bar.key) || undefined}
          style={{ left, width }} onClick={() => actions.seek(bar)} onDoubleClick={() => actions.open(bar)}>
          {width > clipLabelMinWidth + clipLabelPadding * 2 && <span className={styles.routeLabel} data-name>{bar.label}</span>}
        </button>
      </Tip>;
    })}
  </Line>;
});

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
      {markers.map(marker => {
        const selectId = inputClipSelectId(row.track.id, marker.id);
        return (
          <Marker key={marker.id} boxKey={selectId} seconds={marker.seconds} kind="in" label={inputLabel(marker.writes, false)}
            text={inputLabel(marker.writes, false)} graphId={marker.graphId} selectId={selectId} />
        );
      })}
    </Line>
  );
});

// ── 覆盖层：游标（TimelineTimeCursor）、终点（End Marker）与段边界，盖在刻度与轨道上 ──
// 游标订阅时刻后直接改 transform，不经过 React。游标头在刻度区底部对齐，线从时长条处向下；
// 拖动刻度时在刻度上方显示游标时刻。终点由片段决定、不能拖动，照 Unity 用暗蓝线与灰色头。

function Overlay({ bounds, origin, follow, onRangeChanged }: {
  bounds: Range; origin: number; follow: boolean;
  onRangeChanged(update: (previous: Range) => Range): void;
}) {
  const { range, valueToPixels, sidebarWidth } = useTimelineContext();
  const line = useRef<HTMLDivElement>(null);
  const head = useRef<HTMLDivElement>(null);
  const tip = useRef<HTMLDivElement>(null);
  const panelId = usePanelId();
  const visible = usePanelVisible();
  const panelWindow = usePanelWindow();
  const pending = useRef<number | null>(null);
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
    if (visible && pending.current === null) {
      const key = frameKey(state.runId, state.frameIndex);
      pending.current = panelWindow.requestAnimationFrame(() => {
        pending.current = null;
        const current = getState();
        if (key !== frameKey(current.runId, current.frameIndex) || current.frameKey !== key ||
          current.selectedCurves.some(id => current.archiveFrame?.curves[id] === undefined)) {
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
        state.archiveFrame !== previous.archiveFrame || state.selectedCurves !== previous.selectedCurves ||
        state.quickPreview.seconds !== previous.quickPreview.seconds) apply();
    });
    const stopAuthor = useAuthorCursor.subscribe(apply);
    return () => {
      stopRun(); stopAuthor();
      if (pending.current !== null) { panelWindow.cancelAnimationFrame(pending.current); pending.current = null; }
      clearRendered(panelId);
    };
  }, [apply, panelWindow, panelId]);

  const x = (seconds: number) => `translateX(${Math.floor(valueToPixels(seconds - range.start))}px)`;
  return (
    <div className={styles.overlay} style={{ left: sidebarWidth }}>
      <div className={styles.endLine} style={{ transform: x(bounds.end) }} />
      <div className={styles.endHead} style={{ transform: x(bounds.end) }} />
      <div ref={line} className={styles.cursorLine} hidden />
      <div ref={head} className={styles.cursorHead} hidden><PlayheadShape /></div>
      <div ref={tip} className={styles.cursorTip} hidden />
    </div>
  );
}
