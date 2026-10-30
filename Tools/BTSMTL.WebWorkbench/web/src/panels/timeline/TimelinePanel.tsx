import { PointerSensor, useSensor, useSensors } from '@dnd-kit/core';
import {
  TimelineContext, useItem, useRow, useTimelineContext, useTimelineMonitor, type DragItemData, type Range,
  type ResizeEndEvent, type ResizeItemData, type UsePanStrategy,
} from 'dnd-timeline';
import {
  createContext, memo, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type MouseEvent as ReactMouseEvent, type PointerEvent,
  type ReactNode,
} from 'react';
import { create } from 'zustand';
import { useShallow } from 'zustand/react/shallow';
import type { ExecutionCall, FrozenInputTimeline, InputWrite, RouteFrame } from '../../api/types';
import type { PlayerDataCurve, PlayerJobId } from '../../api/playerRecords';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered, useFrameReport } from '../../shell/readiness';
import { FrameField, useTransportState } from '../../shell/Toolbar';
import { openPanel } from '../../shell/layout';
import {
  applyTimelineEdits, authoringActions, clipTemplatesFor, currentDocument, graphPath, inputAuthoring, timelinePath, trackContract, trackDomains, useAuthoring, useAuthorTimelines, useInputContext, useInputSchemes, useSkillElements, useDocumentObservationBinding,
} from '../../state/authoring';
import {
  consoleActions, getState, inputRootKey, runActions, selectionActions, useWorkbench, type WorkbenchState,
} from '../../state/store';
import {
  clipGraph, clipSelectId, documentSegments, inputClipSelectId, trackSelectId, inputTrackSelectId, routeSegments, timelineLength, segmentRows, segmentTitle, selectedInputRoot,
  trackColors, type ClipInstance, type GroupRow, type RouteSegment, type TrackKind, type TrackRow,
} from '../../state/timelineModel';
import { callKey, callLabel, graphKey, graphName, matchesDocumentCall, nodeSelectId, parentGraphCalls, recordedGraphCalls, type RecordedGraphCall } from '../../state/graphModel';
import { transport } from '../../state/transport';
import { useInputObservation } from '../../state/inputModel';
import { archive } from '../../state/archive';
import { Empty, PanelBar } from '../../ui/blocks';
import { Button, ContextMenu, MenuButton, Select, TextInput, type MenuEntry } from '../../ui/controls';
import styles from './timeline.module.css';

// Timeline 面板（D5）：照 BTSMTL Timeline Editor，按路线段（状态 → Timeline）分组，每段可折叠。
// dnd-timeline 管时间与像素换算、缩放、平移；刻度、片段、输入标记、游标自绘。
// 游标是独立覆盖线，订阅 cursor 后直接改 transform；片段只按自身是否处于游标下切换 class。

// 轨道头宽度，照 Timeline Editor 约占窗口三分之一，面板窄时收窄让出时间区；CSS 经 --sidebar 读取。
const sidebarFor = (width: number) => Math.round(Math.max(150, Math.min(380, width * 0.3)));
const minSpan = 0.05;
const rowHeights = { group: 30, anim: 46, motion: 46, act: 42, in: 42, other: 42, inputGroup: 30 } as const;
const overscan = 120;
// 窄于此宽度的片段把名字画在片段后面。
const labelInside = 46;

interface InputGroupRow { kind: 'inputGroup'; id: string }
interface InputTrackRow { kind: 'inputTrack'; id: string; selectId: string; track: FrozenInputTimeline['tracks'][number] }
interface CurveGroupRow { kind: 'curves'; id: string }
interface CurveRow { kind: 'curve'; id: string; curve: PlayerDataCurve }
type Row = GroupRow | TrackRow | InputGroupRow | InputTrackRow | CurveGroupRow | CurveRow;

interface Params {
  range: Range | null;
  collapsed: string[];
  follow: boolean;
  filter: string;
  // 顶栏所选技能（programId）；null 为路线上第一个技能。
  program: string | null;
  // 双击组头进入的单个 Timeline（documentId）；null 为路线视图。
  focus: string | null;
  // 进入单个 Timeline 前的路线视图范围，返回时恢复。
  routeRange: Range | null;
  // 编辑吸附（照 Timeline Editor 时间网格）：逻辑 tick、秒间隔或关闭。
  snap: Snap;
  call: string | null;
  pin: { runId: PlayerJobId; documentId: string; observation: TimelineObservation } | null;
}
type Snap = 'tick' | '0.1' | '0.01' | '0.001' | 'none';
const defaults: Params = {
  range: null, collapsed: [], follow: true, filter: '', program: null, focus: null, routeRange: null, snap: 'tick', call: null, pin: null,
};

// 吸附步长（秒）；关闭吸附时为定点时间的最小单位。没有逻辑 tick 时不能编辑。
const snapStep = (snap: Snap | undefined, frameStep: number | null) =>
  snap === 'none' ? 1 / 65536 : snap && snap !== 'tick' ? Number(snap) : frameStep;
const SnapContext = createContext<number | null>(null);
type TimelineObservation = { call: ExecutionCall; domain: string };
const observationKey = (observation: TimelineObservation) => JSON.stringify([callKey(observation.call), observation.domain]);
type PreviewClock = { kind: 'archive' } | { kind: 'document'; documentId: string | null; observation: TimelineObservation | null }
  | { kind: 'input'; documentId: string; observing: boolean };
const PreviewClockContext = createContext<PreviewClock>({ kind: 'document', documentId: null, observation: null });
const CurveClockContext = createContext<{ sourceFrame: string; seconds: number; sdkTickRate: number } | null>(null);
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
      ? Number(BigInt(state.hostFrame) - BigInt(state.archiveMetadata.scheduleStartFrame)) / state.archiveMetadata.sdkTickRate : null)
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

function rowHeight(row: Row): number {
  switch (row.kind) {
    case 'group': return rowHeights.group;
    case 'inputGroup': return rowHeights.inputGroup;
    case 'inputTrack': return rowHeights.in;
    case 'curves': return rowHeights.inputGroup;
    case 'curve': return 64;
    case 'track': return rowHeights[row.trackKind];
  }
}

export function TimelinePanel() {
  return <DocumentTimeline />;
}

// 未打开技能时只有技能选择；打开后与运行视图同一窗口，固定为单个 Timeline。
function DocumentTimeline() {
  const { skills, target, loading, available, reason, error } = useAuthoring(useShallow(state => ({
    skills: state.skills, target: state.target, loading: state.loading, available: state.available,
    reason: state.unavailableReason, error: state.documentError,
  })));
  const count = useAuthorTimelines().length;
  useEffect(() => { if (skills === null) void authoringActions.listSkills(); }, [skills]);
  if (available === false) return <ReportEmpty text={reason ?? '作者接口不可用'} />;
  if (!target || loading || !count) {
    return (
      <div className={styles.window}>
        <PanelBar wrap>
          <SkillPicker />
        </PanelBar>
        {loading ? <Empty text="正在读取内容" />
          : <ReportEmpty text={error ?? (target ? '此内容没有 Timeline' : '选择要编辑的内容')} />}
      </div>
    );
  }
  return <TimelineWindow frames={null} />;
}

function ReportEmpty({ text }: { text: string }) {
  useFrameReport(text);
  return <Empty text={text} />;
}

function SkillPicker() {
  const { skills, target } = useAuthoring(useShallow(state => ({ skills: state.skills, target: state.target })));
  if (target?.kind === 'scenario') return <span className={styles.subject}>{target.name}</span>;
  return (
    <span className={styles.subject}>
      <Select label="技能" placeholder="选择技能" value={target?.name ?? null} options={(skills ?? []).map(item => ({ value: item, label: item }))}
        disabled={!skills?.length} tip="技能文件夹；不需要先运行" onChange={value => void authoringActions.open({ kind: 'skill', name: value })} />
    </span>
  );
}

function fitRange(bounds: Range): Range {
  const pad = Math.max(minSpan, (bounds.end - bounds.start) * 0.03);
  return { start: bounds.start - pad, end: bounds.end + pad };
}

// frames 为 null 时显示打开的技能文档：范围为所看 Timeline 的长度，按编译帧率取整。
function TimelineWindow({ frames }: { frames: RouteFrame[] | null }) {
  const [params, setParams] = usePanelParams<Params>(defaults);
  const runId = useWorkbench(state => state.runId);
  const trace = useWorkbench(useShallow(state => ({ current: state.frame?.route ?? null, frames: state.routeFrames, graphs: state.graphs })));
  const schemes = useInputSchemes();
  const authorTimelines = useAuthorTimelines();
  const documentLength = useMemo(() => {
    if (frames) return 0;
    const selected = authorTimelines.find(item => item.documentId === params.focus);
    return Math.max(0, ...(selected ? [selected] : authorTimelines).map(timeline =>
      Math.max(timelineLength(timeline), schemes.find(scheme => scheme.documentId === timeline.documentId)?.rangeEndSeconds ?? 0)));
  }, [frames, authorTimelines, params.focus, schemes]);
  const tickRate = useAuthoring(state => state.tickRate);
  const scenario = useAuthoring(state => state.target?.kind === 'scenario');
  const inputRate = useWorkbench(state => {
    const timelineId = params.focus ?? (state.timelines.length === 1 ? state.timelines[0].documentId : null);
    const scheme = schemes.find(item => item.documentId === timelineId);
    return scheme?.scene === state.scenePath && state.inputCatalog?.players.some(player => player.actorId === scheme.actorId)
      ? state.inputCatalog.tickRate : null;
  });
  const sdkTickRate = useWorkbench(state => state.archiveMetadata?.sdkTickRate ?? null);
  const rate = frames ? sdkTickRate : scenario ? inputRate : tickRate;
  const frameStep = rate === null ? null : 1 / rate;
  const step = snapStep(params.snap, frameStep);
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
          {step !== null && <ClipEditing step={step} />}
          <TimelineBody params={params} setParams={setParams} bounds={bounds} frames={frames} onRangeChanged={onRangeChanged} />
        </SnapContext.Provider>
      </TimelineContext>
    </div>
  );
}

// 作者模式拖动、拖端写回片段时间：原起止加上拖动的像素位移换成的秒数，按吸附步长取整，
// 再减去本段零点得到 Timeline 本地秒数。拖到同一 Timeline 的另一条轨道时换轨（目标轨道须能放该种片段）；
// 拖动框选中的片段时，框选的同一 Timeline 片段整体平移。
function ClipEditing({ step }: { step: number }) {
  const { range, timelineRef, sidebarWidth: sidebar } = useTimelineContext();
  const elements = useSkillElements(useAuthoring(state => state.author && state.target !== null));
  // 像素位移换成秒数：可见范围除以时间区宽度。
  const pixelsToValue = (pixels: number) => {
    const width = (timelineRef.current?.clientWidth ?? 0) - sidebar;
    return width > 0 ? pixels * (range.end - range.start) / width : 0;
  };
  const write = (edit: ClipEdit | undefined, span: Range, deltaStart: number, deltaEnd: number) => {
    if (!edit) return;
    const local = (value: number) => snapTo(value - edit.origin, step);
    const start = Math.max(0, local(span.start + deltaStart));
    const end = Math.max(start + step, local(span.end + deltaEnd));
    authoringActions.setClipTime(edit.documentId, edit.clipId, start, end);
  };
  // 松手处所在的作者轨道（`Timeline/轨道`）。
  const trackAt = (x: number, y: number) =>
    (timelineRef.current?.ownerDocument ?? document).elementsFromPoint(x, y)
      .map(element => element.closest<HTMLElement>('[data-track]')?.dataset.track).find(Boolean) ?? null;
  useTimelineMonitor({
    onDragEnd(event) {
      const data = event.active.data.current as (DragItemData & { edit?: ClipEdit }) | undefined;
      const edit = data?.edit;
      if (!data || !edit) return;
      const delta = pixelsToValue(event.delta.x);
      const local = snapTo(data.span.start + delta - edit.origin, step) - (data.span.start - edit.origin);
      const boxed = boxedClips.filter(id => id.startsWith(`${edit.documentId}/`));
      if (boxed.length > 1 && boxed.includes(`${edit.documentId}/${edit.clipId}`)) {
        if (local) authoringActions.moveClips(edit.documentId, boxed.map(id => id.split('/')[1]), local, null);
        return;
      }
      const pointer = event.activatorEvent as PointerEvent | MouseEvent | null;
      const target = pointer ? trackAt(pointer.clientX + event.delta.x, pointer.clientY + event.delta.y) : null;
      const [targetDocument, targetTrack] = target?.split('/') ?? [];
      if (targetDocument === edit.documentId && targetTrack !== edit.trackId) {
        const kind = currentClip(edit.documentId, edit.clipId)?.clip.contract;
        const allowed = !!elements && clipTemplatesFor(elements, trackContract(edit.documentId, targetTrack)).some(template => template.kind === kind);
        if (allowed) {
          authoringActions.moveClips(edit.documentId, [edit.clipId], local, targetTrack);
          return;
        }
        consoleActions.log('warn', 'Timeline', `目标轨道不能放 ${kind ?? '该'} 片段，只按时间平移`);
      }
      // 平移只按起点吸附，保持时长。
      if (local) authoringActions.moveClips(edit.documentId, [edit.clipId], local, null);
    },
    onResizeEnd(event: ResizeEndEvent) {
      const data = event.active.data.current as ResizeItemData & { edit?: ClipEdit };
      const delta = pixelsToValue(event.delta.x);
      if (delta) write(data.edit, data.span, event.direction === 'start' ? delta : 0, event.direction === 'end' ? delta : 0);
    },
  });
  return null;
}

// 框选中的片段（选中身份），拖动其中一个时整体平移。
let boxedClips: string[] = [];

// 平移：Ctrl＋滚轮以鼠标为中心缩放；普通滚轮在时间区横移，在轨道列表上纵向滚动；中键拖动横移。
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
    let dragX: number | null = null;
    const down = (event: globalThis.PointerEvent) => {
      if (event.button !== 1) return;
      event.preventDefault();
      dragX = event.clientX;
      element.setPointerCapture(event.pointerId);
    };
    const move = (event: globalThis.PointerEvent) => {
      if (dragX === null) return;
      onPanEnd({ deltaX: dragX - event.clientX, deltaY: 0 });
      dragX = event.clientX;
    };
    const up = (event: globalThis.PointerEvent) => {
      if (dragX === null) return;
      dragX = null;
      element.releasePointerCapture(event.pointerId);
    };
    element.addEventListener('wheel', wheel, { passive: false });
    element.addEventListener('pointerdown', down);
    element.addEventListener('pointermove', move);
    element.addEventListener('pointerup', up);
    element.addEventListener('pointercancel', up);
    return () => {
      element.removeEventListener('wheel', wheel);
      element.removeEventListener('pointerdown', down);
      element.removeEventListener('pointermove', move);
      element.removeEventListener('pointerup', up);
      element.removeEventListener('pointercancel', up);
    };
  }, [bag.timelineRef, bag.sidebarWidth, onPanEnd]);
};

function TimelineBody({ params, setParams, bounds, frames, onRangeChanged }: {
  params: Params;
  setParams(patch: Partial<Params>): void;
  bounds: Range;
  frames: RouteFrame[] | null;
  onRangeChanged(update: (previous: Range) => Range): void;
}) {
  const binding = useDocumentObservationBinding();
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
  // 技能文档固定看单个 Timeline：所选的那条，否则第一条。
  const focused = frames ? (focusId ? segments.find(segment => segment.id === focusId) ?? null : null)
    : segments.find(segment => segment.documentId === params.focus) ?? (segments.length === 1 ? segments[0] : null);
  const inputScheme = schemes.find(scheme => scheme.documentId === focused?.documentId);
  const inputObservation = useInputObservation(inputScheme?.documentId ?? null);
  const playbacks = useWorkbench(state => state.frame?.route?.playbacks);
  const observations = useMemo(() => {
    const result = new Map<string, TimelineObservation>();
    for (const playback of playbacks ?? [])
      if (playback.documentId === focused?.documentId && playback.call !== null && playback.domain !== null && matchesDocumentCall(playback.call, binding)) {
        const observation = { call: playback.call, domain: playback.domain };
        result.set(observationKey(observation), observation);
      }
    return [...result.values()];
  }, [playbacks, focused?.documentId, binding]);
  const pin = params.pin?.runId === runId && params.pin.documentId === focused?.documentId && matchesDocumentCall(params.pin.observation.call, binding)
    ? params.pin : null;
  const observed = pin?.observation ?? observations.find(observation => observationKey(observation) === params.call)
    ?? (observations.length === 1 ? observations[0] : null);
  const choices = pin && !observations.some(item => observationKey(item) === observationKey(pin.observation))
    ? [pin.observation, ...observations] : observations;
  const togglePin = pin ? () => setParams({ pin: null }) : observed === null || runId === null || focused === null ? undefined
    : () => setParams({ call: observationKey(observed), pin: { runId, documentId: focused.documentId, observation: observed } });
  const recorded = useMemo(() => recordedGraphCalls(trace.frames, trace.current, trace.graphs ?? [], binding), [trace, binding]);
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
    if (!frames) {
      setParams({ focus: documentId, range: segmentRange(segment), call: null, pin: null });
      return;
    }
    const frameIndex = getState().frameIndex;
    if (frameIndex < segment.firstIndex || frameIndex > segment.lastIndex) transport.seek(segment.firstIndex);
    setParams({ focus: documentId, routeRange: params.focus ? params.routeRange : params.range, range: segmentRange(segment), call: null, pin: null });
  }, [frames, segments, setParams, params.focus, params.routeRange, params.range]);
  const back = useCallback(() => setParams({ focus: null, range: params.routeRange, routeRange: null, call: null, pin: null }), [setParams, params.routeRange]);

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
  }, [request, allSegments, programOf, program, setParams, enter]);

  // 转移标记：路线上每次换段的时刻，悬停说明转移到的状态。
  const marks = useMemo(() => segments.slice(1).map(segment => ({
    key: segment.id, seconds: segment.start,
    label: `转移到 ${segment.state?.name ?? segment.timeline?.displayName ?? segment.documentId.slice(0, 8)} · ${segment.start.toFixed(3)} s`,
  })), [segments]);

  const rows = useMemo(() => {
    const filter = params.filter.trim().toLowerCase();
    const collapsed = new Set(params.collapsed);
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
      const tracks = segmentRows(focused, graphNames).map(row => input ? { ...row, trackKind: 'in' as const } : row);
      result.push(...(filter ? tracks.filter(row => row.track.displayName.toLowerCase().includes(filter)) : tracks));
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
      const tracks = collapsed.has(group.id) ? [] : segmentRows(segment, graphNames);
      const children = titleMatched ? tracks : tracks.filter(row => row.track.displayName.toLowerCase().includes(filter));
      if (titleMatched || children.length) result.push(group, ...children);
    }
    addCurves();
    return result;
  }, [segments, focused, graphNames, inputTimeline, inputRoot, schemes, curves, selectedCurves, params.filter, params.collapsed]);

  const toggle = useCallback((id: string) => {
    const collapsed = new Set(params.collapsed);
    if (!collapsed.delete(id)) collapsed.add(id);
    setParams({ collapsed: [...collapsed] });
  }, [params.collapsed, setParams]);

  const step = useContext(SnapContext);
  const seekAt = useCallback((clientX: number) => {
    if (frames) { void transport.seekTime(getValueFromScreenX(clientX)); return; }
    if (!focused) return;
    const raw = Math.max(0, Math.min(focused.length, getValueFromScreenX(clientX)));
    const seconds = step ? Math.min(focused.length, snapTo(raw, step)) : raw;
    if (inputScheme && inputObservation.kind === 'ready') {
      const metadata = inputObservation.metadata;
      void transport.seekTime((metadata.scheduleStartFrame - metadata.firstRecordedSdkFrame) / metadata.sdkTickRate + seconds);
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
  }, [frames, observed, focused, step, getValueFromScreenX, inputScheme, inputObservation]);

  const snap = params.snap ?? 'tick';
  const settings: MenuEntry[] = [
    { kind: 'check', label: '游标离开视图时跟随', checked: params.follow, onChange: follow => setParams({ follow }) },
    { kind: 'separator' },
    { kind: 'label', label: '编辑吸附' },
    ...([['tick', '逻辑 tick'], ['0.1', '0.1 秒'], ['0.01', '0.01 秒'], ['0.001', '0.001 秒'], ['none', '关闭吸附']] as const)
      .map(([value, label]): MenuEntry => ({ kind: 'check', label, checked: snap === value, onChange: () => setParams({ snap: value }) })),
    { kind: 'separator' },
    { label: '展开全部分组', onSelect: () => setParams({ collapsed: [] }) },
    {
      label: '折叠全部分组',
      onSelect: () => setParams({ collapsed: rows.filter(row => row.kind === 'group' || row.kind === 'inputGroup' || row.kind === 'curves').map(row => row.id) }),
    },
  ];

  const origin = focused?.origin ?? 0;
  const subject: Subject = frames ? {
    options: programs.map(item => ({ value: item, label: `${item.slice(item.indexOf(':') + 1)} / 技能根` })),
    program, setProgram: next => setParams({ program: next, focus: null, routeRange: null, call: null, pin: null }),
    focus: focused?.timeline?.displayName ?? (params.focus ? params.focus.slice(0, 8) : null), back, timelines: null,
  } : {
    options: [], program: null, setProgram: () => {}, focus: focused?.timeline?.displayName ?? null, back,
    timelines: { value: focused?.documentId ?? null, options: segments.map(segment => ({
      value: segment.documentId, label: schemes.find(scheme => scheme.documentId === segment.documentId)?.name ?? segmentTitle(segment),
    })), select: enter },
  };
  const clock = useMemo<PreviewClock>(() => frames ? { kind: 'archive' }
    : inputScheme ? { kind: 'input', documentId: inputScheme.documentId, observing: inputObservation.kind === 'ready' }
      : { kind: 'document', documentId: focused?.documentId ?? null, observation: observed },
  [frames, focused?.documentId, observed, inputScheme, inputObservation.kind]);
  const curveClock = useWorkbench(useShallow(state => {
    if (state.hostFrame === null || state.archiveMetadata === null || (clock.kind === 'input' && !clock.observing)) return null;
    const seconds = previewSeconds(state, clock, null);
    return seconds === null ? null : { sourceFrame: state.hostFrame, seconds, sdkTickRate: state.archiveMetadata.sdkTickRate };
  }));

  return (
    <PreviewClockContext.Provider value={clock}>
    <CurveClockContext.Provider value={curveClock}>
    <div className={styles.window} style={{ ['--sidebar' as string]: `${sidebarWidth}px` }}>
      <TimelineToolbar settings={settings} subject={subject} documentId={focused?.timeline ? focused.documentId : null}
        observations={choices} selectCall={selectCall} pinned={pin !== null} togglePin={togglePin} parents={parents} openParent={openParent}
        fit={() => setParams({ range: focused ? segmentRange(focused) : fitRange(bounds) })} />
      <div className={styles.timeline} ref={setTimelineRef}>
        <div className={styles.header}>
          <div className={styles.timeHead}>时间</div>
          <div className={styles.headerLane}>
            <TimeScroller bounds={focused ? { start: focused.origin, end: focused.origin + focused.length } : bounds}
              onRangeChanged={onRangeChanged} />
            <Ruler bounds={bounds} origin={origin} marks={marks} seekAt={seekAt} />
          </div>
        </div>
        <div className={styles.filterRow}>
          <div className={styles.filter}>
            <TextInput className={styles.filterInput} placeholder="搜索轨道" value={params.filter}
              onChange={event => setParams({ filter: event.currentTarget.value })} aria-label="搜索轨道" />
          </div>
          <div className={styles.filterLane} />
        </div>
        <Rows rows={rows} collapsed={params.collapsed} toggle={toggle} enter={enter} inputRoots={inputTimeline?.roots} />
        <Overlay bounds={bounds} origin={origin} seekAt={seekAt} segments={focused ? [] : segments} follow={params.follow} onRangeChanged={onRangeChanged} />
      </div>
    </div>
    </CurveClockContext.Provider>
    </PreviewClockContext.Provider>
  );
}

// ── 窗口工具条：Preview 开关、到开头、上一帧、播放、下一帧、到结尾、帧号；右侧资产名与设置 ──

interface Subject {
  options: { value: string; label: string }[];
  program: string | null;
  setProgram(program: string): void;
  // 单个 Timeline 视图的名字；路线视图为 null。
  focus: string | null;
  back(): void;
  // 技能文档：所看的 Timeline 与技能内全部 Timeline；运行视图为 null。
  timelines: { value: string | null; options: { value: string; label: string }[]; select(documentId: string): void } | null;
}

// 编辑行右侧：照 Timeline Editor 的未保存提示与保存（只在有未保存修改时出现），作者／运行观察切换。
function AuthoringControls() {
  const state = useAuthoring(useShallow(current => ({
    available: current.available, reason: current.unavailableReason, author: current.author, target: current.target,
    loading: current.loading, edited: current.edited, removed: current.removed, externalChange: current.externalChange,
    saving: current.saving, lastSave: current.lastSave, undoDepth: current.undoDepth, redoDepth: current.redoDepth,
  })));
  const dirty = Object.keys(state.edited).length + state.removed.length;
  // 作者模式下定时检查技能文件是否在别处（Unity、AI、编辑器）被修改。
  useEffect(() => {
    if (!state.author || !state.target) return;
    const timer = setInterval(() => void authoringActions.checkExternal(), 3000);
    return () => clearInterval(timer);
  }, [state.author, state.target]);
  const enter = async () => {
    if (state.available === null) await authoringActions.probe();
    if (!useAuthoring.getState().available) {
      consoleActions.log('warn', '作者', useAuthoring.getState().unavailableReason ?? '作者接口不可用');
      return;
    }
    authoringActions.setAuthor(true);
  };
  return (
    <>
      {state.author && state.target && (
        <>
          <Button variant="ghost" aria-label="撤销" disabled={!state.undoDepth} tip="撤销（Ctrl+Z）" onClick={() => authoringActions.undo()}>↶</Button>
          <Button variant="ghost" aria-label="重做" disabled={!state.redoDepth} tip="重做（Ctrl+Y／Ctrl+Shift+Z）" onClick={() => authoringActions.redo()}>↷</Button>
        </>
      )}
      {state.externalChange && <span className={styles.authorWarn} title="技能文件在别处被修改；保存会覆盖外部修改，还原会丢弃网页修改">外部已修改</span>}
      {dirty > 0 && (
        <>
          <span className={styles.authorDirty}>{dirty} 个文件未保存{state.removed.length ? `（含 ${state.removed.length} 个待删除）` : ''}</span>
          <Button variant="ghost" tip="丢弃网页中的全部未保存修改" onClick={() => authoringActions.revert()}>还原</Button>
          <Button variant="ghost" disabled={state.saving} tip="保存并编译当前技能"
            onClick={() => void authoringActions.save()}>{state.saving ? '保存中' : '保存'}</Button>
        </>
      )}
      {!dirty && state.lastSave && (
        <span className={state.lastSave.ok ? styles.authorDirty : styles.authorWarn} title={state.lastSave.message}>
          {state.lastSave.ok ? '已保存' : state.lastSave.compilationState === 'failed' ? '编译未通过'
            : state.lastSave.saved ? '已保存，尚未应用' : '保存未完成'}
        </span>
      )}
      <Button variant="ghost" pressed={state.author} disabled={state.available === false}
        tip={state.available === false ? state.reason ?? '作者接口不可用'
          : state.author ? (state.loading ? '正在读取内容' : `编辑 ${state.target?.name ?? '—'}：拖动片段改时间，拖两端改起止`)
            : '进入作者模式，编辑当前技能'}
        onClick={() => void enter()}>作者</Button>
      <Button variant="ghost" pressed={!state.author} tip="只观察已确认的运行记录" onClick={() => authoringActions.setAuthor(false)}>运行观察</Button>
    </>
  );
}

// 照 Timeline Editor：编辑行返回、技能根与面包屑，右侧作者／运行观察；预览行播放、全局秒数与结论；下行逐帧、适配与设置。
// 作者模式在所看的单个 Timeline 上添加轨道：种类与默认值取原 C# 轨道类型。
function AddTrack({ documentId }: { documentId: string | null }) {
  const author = useAuthoring(state => state.author && state.target !== null);
  const elements = useSkillElements(author && documentId !== null);
  const schemes = useInputSchemes();
  if (!author || !documentId) return null;
  if (schemes.some(scheme => scheme.documentId === documentId)) return <AddInput documentId={documentId} />;
  // 每种轨道按可用执行域各列一项。
  const entries: MenuEntry[] = elements ? elements.tracks.flatMap(template => trackDomains(template).map((domain): MenuEntry => ({
    label: `${template.kind ?? template.clrType} · ${domain}`,
    onSelect: () => {
      const id = authoringActions.addTrack(documentId, { ...template.value, m_ExecutionDomain: domain }, template.kind ?? '新轨道');
      selectionActions.select({ kind: 'track', id: trackSelectId(documentId, id) });
    },
  }))) : [{ label: '读取中', disabled: true, onSelect: () => {} }];
  return <MenuButton label="添加轨道" caret entries={entries}>添加轨道</MenuButton>;
}

function AddInput({ documentId }: { documentId: string }) {
  const context = useInputContext(documentId);
  const [pending, setPending] = useState(false);
  const clock = useContext(PreviewClockContext);
  const cursor = usePreviewSelect(clock, seconds => seconds);
  if (context.kind !== 'ready') return <Button disabled tip={context.reason}>添加输入</Button>;
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
  return <MenuButton label="添加输入" caret entries={entries}>添加输入</MenuButton>;
}

function TimelineToolbar({ settings, fit, subject, documentId, observations, selectCall, pinned, togglePin, parents, openParent }: {
  settings: MenuEntry[]; fit(): void; subject: Subject; documentId: string | null;
  observations: TimelineObservation[]; selectCall(value: string): void;
  pinned: boolean; togglePin: (() => void) | undefined; parents: RecordedGraphCall[];
  openParent: ((item: RecordedGraphCall) => void) | undefined;
}) {
  const binding = useDocumentObservationBinding();
  const { playing, blocked, previous, next } = useTransportState();
  const clock = useContext(PreviewClockContext);
  const seconds = usePreviewSelect(clock, current => current);
  const input = useInputObservation(clock.kind === 'input' ? clock.documentId : null);
  const segment = useWorkbench(state => {
    if (clock.kind === 'input') return input.kind === 'ready'
      ? `${input.input.actorId} · ${input.input.received ? '本帧已收到输入' : '本帧未收到输入'}` : input.reason;
    if (binding.dirty) return '当前内容未保存，旧档案叠加已停用';
    const route = state.frame?.route;
    if (!route) return state.runId ? '档案播放记录尚未提供' : '';
    const playback = documentPlayback(state, clock);
    if (!playback) return '当前帧没有匹配来源修订与调用的 Timeline 播放记录';
    const name = state.timelines.find(item => item.documentId === playback.documentId)?.displayName ?? playback.documentId.slice(0, 8);
    return `${playback.call!.actorId} · ${playback.domain} · ${name} · 本地 ${playback.cursorSeconds.toFixed(3)} 秒`;
  });
  const root = subject.options.find(item => item.value === subject.program)?.label ?? '技能根';
  return (
    <>
      <PanelBar wrap>
        {subject.timelines ? (
          <>
            <SkillPicker />
            <span className={styles.subject}>
              <Select label="Timeline" value={subject.timelines.value} options={subject.timelines.options}
                tip="技能内的 Timeline；节点图双击播放它的节点也会切到这里" onChange={subject.timelines.select} />
            </span>
          </>
        ) : subject.focus !== null && <Button variant="ghost" tip="回到技能根路线" onClick={subject.back}>‹ 返回</Button>}
        {subject.timelines ? null : subject.focus === null ? (
          <span className={styles.subject}>
            <Select label="技能根" placeholder="没有技能根" value={subject.program} options={subject.options}
              disabled={!subject.options.length} tip={subject.options.length ? '路线所属的技能根状态机' : '路线没有经过状态机'}
              onChange={subject.setProgram} />
          </span>
        ) : (
          <span className={styles.crumbs}>
            <button className={styles.crumbLink} onClick={subject.back}>{root} ›</button>
            <span>{subject.focus}</span>
          </span>
        )}
        <span className={styles.grow} />
        <AuthoringControls />
        {observations.length > 0 && <Select label="实际调用" value={clock.kind === 'document' && clock.observation ? observationKey(clock.observation) : null}
          options={observations.map(observation => ({ value: observationKey(observation), label: `${callLabel(observation.call)} · ${observation.domain}` }))}
          disabled={observations.length < 2} placeholder="选择实际调用" onChange={selectCall} />}
        {clock.kind === 'document' && clock.observation !== null && <>
          <Button variant="ghost" pressed={pinned} disabled={togglePin === undefined} onClick={togglePin}
            tip={pinned ? '解除固定，按当前帧选择调用' : '固定本次实际调用和执行域'}>固定调用</Button>
          {clock.observation.call.parentInvocationPath && (parents.length > 1 && openParent
            ? <MenuButton label="选择父调用" entries={parents.map(item => ({ label: `${graphName(item.graph)} · ${callLabel(item.call)}`,
              onSelect: () => openParent(item) }))}>上层调用</MenuButton>
            : <Button variant="ghost" disabled={parents.length !== 1 || openParent === undefined}
              onClick={parents.length === 1 && openParent ? () => openParent(parents[0]) : undefined}
              tip={parents.length === 1 ? '在节点图返回同一次实际父调用'
                : clock.observation.call.parentInvocationGeneration === null ? '本记录没有父调用代次' : '已加载记录中没有匹配的父调用'}>上层调用</Button>)}
        </>}
      </PanelBar>
      <PanelBar wrap>
        <Button variant="ghost" aria-label={playing ? '暂停' : '播放'} pressed={playing} tip={blocked ?? '播放／暂停（空格）'}
          disabled={!!blocked} onClick={() => transport.toggle()}>
          <i className={playing ? styles.pauseGlyph : styles.playGlyph} />
        </Button>
        <span className={styles.seconds}>{seconds === null ? '—' : `${seconds.toFixed(3)} 秒`}</span>
        <span className={styles.hint}>{segment}</span>
        <span className={styles.grow} />
        <FrameField showSeconds={false} />
      </PanelBar>
      <PanelBar wrap>
        <Button aria-label="上一帧" tip={previous ?? '上一帧（←）'} disabled={!!previous} onClick={() => transport.step(-1)}>‹</Button>
        <Button aria-label="下一帧" tip={next ?? '下一帧（→）'} disabled={!!next} onClick={() => transport.step(1)}>›</Button>
        <Button tip="显示整条运行" onClick={fit}>适配</Button>
        <span className={styles.grow} />
        <AddTrack documentId={documentId} />
        <MenuButton label="Timeline 设置" caret entries={settings}>显示设置</MenuButton>
      </PanelBar>
    </>
  );
}

// ── 刻度：间隔随缩放调整，点击与拖动经 transport 跳帧 ──

const tickSteps = [1 / 60, 1 / 30, 0.05, 0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10, 30, 60];

// 照 Timeline Editor 刻度上方的时间滚动条：滑块是当前可见范围，拖滑块平移，拖两端缩放。
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

// origin：刻度零点。单个 Timeline 视图按该次播放的本地时间标注。
function Ruler({ bounds, origin, marks, seekAt }: {
  bounds: Range; origin: number; marks: { key: string; seconds: number; label: string }[]; seekAt(clientX: number): void;
}) {
  const { range, valueToPixels } = useTimelineContext();
  const width = valueToPixels(range.end - range.start);
  const step = tickSteps.find(item => valueToPixels(item) >= 64) ?? tickSteps[tickSteps.length - 1];
  const digits = Number.isInteger(step) ? 0 : Number.isInteger(Math.round(step * 1e6) / 1e5) ? 1 : 2;
  const ticks: ReactNode[] = [];
  if (width > 0) {
    const minor = step / 5;
    const first = Math.ceil((range.start - origin) / minor);
    for (let index = first; index * minor + origin <= range.end; index++) {
      const value = index * minor;
      const major = index % 5 === 0;
      const x = valueToPixels(value + origin - range.start);
      ticks.push(<i key={index} className={major ? styles.majorTick : styles.minorTick} style={{ left: x }} />);
      if (major) ticks.push(<span key={`l${index}`} className={styles.tickLabel} style={{ left: x }}>{value.toFixed(digits)} s</span>);
    }
  }
  const drag = (event: PointerEvent<HTMLDivElement>) => {
    if (event.button !== 0) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    seekAt(event.clientX);
  };
  return (
    <div className={styles.ruler} role="slider" aria-label="记录游标" aria-valuemin={bounds.start} aria-valuemax={bounds.end}
      onPointerDown={drag}
      onPointerMove={event => { if (event.currentTarget.hasPointerCapture(event.pointerId)) seekAt(event.clientX); }}>
      {ticks}
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

function Rows({ rows, collapsed, toggle, enter, inputRoots }: {
  rows: Row[];
  collapsed: string[];
  toggle(id: string): void;
  enter(documentId: string): void;
  inputRoots: FrozenInputTimeline[] | undefined;
}) {
  const clock = useContext(PreviewClockContext);
  const scroller = useRef<HTMLDivElement>(null);
  const [boxed, setBoxed] = useState<ReadonlySet<string>>(() => new Set());
  const [box, setBox] = useState<Box | null>(null);
  const boxStart = useRef<{ x: number; y: number } | null>(null);
  // 框选后选中的是第一个片段；其它面板或单击换了选中时，框选结果作废。
  const boxedSelect = useRef<string | null>(null);
  const selection = useWorkbench(state => state.selection);
  useEffect(() => {
    if (boxed.size && !(selection?.kind === 'clip' && selection.id === boxedSelect.current)) setBoxed(new Set());
    if (!boxed.size) boxedClips = [];
  }, [selection, boxed]);

  const boxDown = (event: PointerEvent<HTMLDivElement>) => {
    const target = event.target as HTMLElement;
    if (event.button !== 0 || !target.closest('[data-lane]') || target.closest('button')) return;
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
      setBoxed(new Set());
      selectionActions.select(null);
      return;
    }
    const hits = [...event.currentTarget.querySelectorAll<HTMLElement>('[data-box]')].filter(element => {
      const rect = element.getBoundingClientRect();
      return rect.right >= left && rect.left <= right && rect.bottom >= top && rect.top <= bottom;
    });
    boxedSelect.current = hits[0]?.dataset.select ?? null;
    setBoxed(new Set(hits.map(element => element.dataset.box!)));
    boxedClips = hits.map(element => element.dataset.select!);
    if (hits.length) selectTimelineObject(clock, 'clip', hits[0].dataset.select!);
    else selectionActions.select(null);
  };
  const panelWindow = usePanelWindow();
  const [view, setView] = useState({ top: 0, height: 600 });
  useLayoutEffect(() => {
    const element = scroller.current;
    if (!element) return;
    const Observer = (panelWindow as Window & { ResizeObserver: typeof ResizeObserver }).ResizeObserver;
    const observer = new Observer(() => setView(previous => ({ ...previous, height: element.clientHeight })));
    observer.observe(element);
    return () => observer.disconnect();
  }, [panelWindow]);

  const offsets = useMemo(() => {
    const result: number[] = [];
    let total = 0;
    for (const row of rows) { result.push(total); total += rowHeight(row); }
    result.push(total);
    return result;
  }, [rows]);
  const total = offsets[offsets.length - 1];
  let first = 0;
  while (first < rows.length && offsets[first + 1] < view.top - overscan) first++;
  let last = first;
  while (last < rows.length && offsets[last] < view.top + view.height + overscan) last++;
  const collapsedSet = new Set(collapsed);

  return (
    <div className={styles.rows} ref={scroller} tabIndex={-1} onPointerDown={boxDown} onPointerMove={boxMove} onPointerUp={boxUp}
      onKeyDown={event => {
        if (!useAuthoring.getState().author || (event.target as HTMLElement).closest('input, textarea')) return;
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
        setBoxed(new Set());
      }}
      onPointerCancel={() => { boxStart.current = null; setBox(null); }}
      onScroll={event => {
        const top = event.currentTarget.scrollTop;
        setView(previous => previous.top === top ? previous : { ...previous, top });
      }}>
      <BoxedContext.Provider value={boxed}>
      <div style={{ height: offsets[first] }} />
      {rows.slice(first, last).map(row => {
        switch (row.kind) {
          case 'group': return <GroupLine key={row.id} row={row} open={!collapsedSet.has(row.id)} toggle={toggle} enter={enter} />;
          case 'track': return <TrackLine key={row.id} row={row} />;
          case 'inputGroup':
            return <InputGroupLine key={row.id} open={!collapsedSet.has(row.id)} toggle={toggle} roots={inputRoots} />;
          case 'inputTrack': return <InputTrackLine key={row.id} row={row} />;
          case 'curves': return <CurveGroupLine key={row.id} open={!collapsedSet.has(row.id)} toggle={toggle} />;
          case 'curve': return <CurveLine key={row.id} row={row} />;
        }
      })}
      <div style={{ height: total - offsets[last] }} />
      </BoxedContext.Provider>
      {box && (
        <div className={styles.selectBox} style={{
          left: Math.min(box.x0, box.x1), top: Math.min(box.y0, box.y1),
          width: Math.abs(box.x1 - box.x0), height: Math.abs(box.y1 - box.y0),
        }} />
      )}
    </div>
  );
}

function Line({ id, height, head, headClass, children, laneClass, track }: {
  id: string;
  height: number;
  head: ReactNode;
  headClass?: string;
  children?: ReactNode;
  laneClass?: string;
  // 作者轨道（`Timeline/轨道`），拖片段换轨时按松手处查找。
  track?: string;
}) {
  const { setNodeRef, rowWrapperStyle, rowStyle, rowSidebarStyle } = useRow({ id, disabled: true });
  return (
    <div className={styles.line} style={{ ...rowWrapperStyle, height }}>
      <div className={headClass} style={rowSidebarStyle}>{head}</div>
      <div ref={setNodeRef} className={laneClass ?? styles.lane} style={rowStyle} data-lane data-track={track}>
        {children}
      </div>
    </div>
  );
}

const Triangle = ({ open }: { open: boolean }) => <i className={open ? styles.foldOpen : styles.foldClosed} />;

const GroupLine = memo(function GroupLine({ row, open, toggle, enter }: {
  row: GroupRow; open: boolean; toggle(id: string): void; enter(documentId: string): void;
}) {
  const segment = row.segment;
  const now = useWorkbench(state => state.frameIndex >= segment.firstIndex && state.frameIndex <= segment.lastIndex);
  const stateNode = useWorkbench(useShallow(state => {
    if (!segment.state) return null;
    for (const graph of state.graphs ?? []) {
      if (graph.id !== segment.state.graphId) continue;
      const node = graph.nodes.find(item => item.id === segment.state!.nodeId);
      if (node) return { selectId: nodeSelectId(graph, node), graph: graphKey(graph) };
    }
    return null;
  }));
  const authoredEnd = segment.origin + segment.length;
  return (
    <Line id={row.id} height={rowHeights.group} laneClass={styles.groupLane} headClass={styles.groupHead} head={
      <>
        <button className={styles.fold} onClick={() => toggle(row.id)} aria-expanded={open} aria-label={open ? '折叠' : '展开'}>
          <Triangle open={open} />
        </button>
        <button className={styles.segmentTitle} data-now={now || undefined}
          title={`${stateNode ? '单击选中该状态并在节点图显示、跳到段首' : '单击跳到段首'}；双击进入这个 Timeline`}
          onClick={() => {
            if (stateNode) {
              selectionActions.select({ kind: 'node', id: stateNode.selectId });
              selectionActions.showGraph(stateNode.graph, null);
            }
            transport.seek(segment.firstIndex);
          }}
          onDoubleClick={() => enter(segment.documentId)}>{row.title}</button>
      </>
    }>
      {open
        ? <Positioned start={segment.origin} end={authoredEnd} className={styles.segmentBracket} />
        : (
          <>
            <Positioned start={segment.start} end={segment.end} className={styles.segmentPlayed}
              title={`实际播放 ${segment.start.toFixed(3)}–${segment.end.toFixed(3)} s`} />
            {authoredEnd > segment.end && (
              <Positioned start={segment.end} end={authoredEnd} className={styles.segmentRest} title="离开后未播放的部分" />
            )}
          </>
        )}
    </Line>
  );
});

// 轨道头右侧照 Timeline Editor 的锁与曲线钮；网页未接入这两项，只按 Unity 外观显示为不可用。
function TrackTools() {
  return (
    <span className={styles.trackTools} title="轨道锁定与曲线编辑在 Unity Timeline Editor 中使用" aria-hidden>
      <i className={styles.lockIcon} />
      <span className={styles.curveButton}>∿</span>
    </span>
  );
}

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
  const templates = useMemo(() => elements ? clipTemplatesFor(elements, trackContract(documentId, row.track.id)) : [],
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
    { label: '标记', onSelect: () => select(authoringActions.addMarkerAt(documentId, row.track.id, elements, local())) },
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

const TrackLine = memo(function TrackLine({ row }: { row: TrackRow }) {
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
  const blends = useMemo(() => blendRegions(row.clips), [row.clips]);
  const segment = row.segment;
  // 极短片段的名字画到下一个片段之前。
  const nextStarts = useMemo(() => {
    const starts = row.clips.map(clip => clip.start).sort((left, right) => left - right);
    return new Map(row.clips.map(clip => [clip.key, starts.find(start => start > clip.start) ?? Infinity]));
  }, [row.clips]);
  return (
    <ContextMenu entries={menu}>
    <div className={styles.menuArea} onContextMenuCapture={event => {
      at.current = getValueFromScreenX(event.clientX);
      // 右键片段或标记先选中它，菜单的复制、删除作用于它；右键轨道头选中轨道。
      const item = (event.target as HTMLElement).closest<HTMLElement>('[data-select]');
      if (item?.dataset.select) selectTimelineObject(clock, 'clip', item.dataset.select);
      else if ((event.target as HTMLElement).closest(`.${styles.trackHead}`)) selectTimelineObject(clock, 'track', row.selectId);
    }}>
    <Line id={row.id} height={rowHeights[row.trackKind]} headClass={styles.trackHead} track={`${documentId}/${row.track.id}`} head={naming ? (
      <span className={styles.headButton} style={{ ['--tc' as string]: trackColors[row.trackKind] }}>
        <span className={styles.slot} />
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
      </span>
    ) : (
      <button className={styles.headButton} data-selected={selected || undefined}
        style={{ ['--tc' as string]: trackColors[row.trackKind] }}
        title={author ? '单击选中 · 双击改名 · 右键轨道菜单' : undefined}
        onClick={() => selectTimelineObject(clock, 'track', row.selectId)}
        onDoubleClick={() => { if (author) setNaming(true); }}>
        <span className={styles.slot} />
        <span className={styles.name}>{row.track.displayName}</span>
        {row.track.muted && <span className={styles.muted}>静音</span>}
        <TrackTools />
      </button>
    )}>
      <Positioned start={segment.origin} end={segment.origin + segment.length} className={styles.authoredRange} />
      <Positioned start={segment.start} end={segment.end} className={styles.playedRange} />
      {visible.map(clip => <Clip key={clip.key} clip={clip} kind={row.trackKind} next={nextStarts.get(clip.key)!}
        documentId={row.timeline.documentId} trackId={row.track.id} />)}
      {blends.filter(blend => blend.end >= range.start && blend.start <= range.end).map(blend => (
        <Blend key={blend.key} start={blend.start} end={blend.end} />
      ))}
      {row.markers.filter(marker => marker.seconds >= range.start && marker.seconds <= range.end).map(marker => (
        <Marker key={marker.key} seconds={marker.seconds} label={`${row.track.displayName} 标记`} graphId={marker.graphId}
          selectId={marker.selectId}
          move={author ? seconds => authoringActions.setMarkerTime(documentId, marker.selectId.split('/')[1], snapLocal(seconds)) : undefined}
          snap={author ? seconds => segment.origin + snapLocal(seconds) : undefined} />
      ))}
    </Line>
    </div>
    </ContextMenu>
  );
});

function blendRegions(clips: ClipInstance[]) {
  const sorted = [...clips].sort((left, right) => left.start - right.start);
  const result: { key: string; start: number; end: number }[] = [];
  for (let index = 1; index < sorted.length; index++) {
    const previous = sorted[index - 1], current = sorted[index];
    if (previous.end > current.start) result.push({ key: `${previous.key}|${current.key}`, start: current.start, end: Math.min(previous.end, current.end) });
  }
  return result;
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

const Clip = memo(function Clip({ clip, kind, next, documentId, trackId }: {
  clip: ClipInstance; kind: TrackKind; next: number; documentId: string; trackId: string;
}) {
  const author = useAuthoring(state => state.author && state.target !== null);
  const edit = useMemo<ClipEdit>(() => ({ documentId, trackId, clipId: clip.clip.id, origin: clip.start - clip.clip.startSeconds }),
    [documentId, trackId, clip]);
  const changed = useAuthoring(state => author && !!state.edited[timelinePath(documentId)]);
  // 淡入淡出只在打开的技能 JSON 中有值（运行回执不带）。
  const ease = useMemo(() => author && clip.clip.easeInSeconds !== undefined ? {
    in: clip.clip.easeInSeconds, out: clip.clip.easeOutSeconds ?? 0,
    set: (easeIn: number | null, easeOut: number | null) => authoringActions.setClipEase(documentId, clip.clip.id, easeIn, easeOut),
  } : undefined, [author, clip, documentId]);
  return (
    <ClipBox boxKey={clip.key} selectId={clip.selectId} kind={kind} label={clip.label} start={clip.start} end={clip.end}
      exit={clip.exit} next={next} graphId={clip.clip.graphId} edit={author ? edit : undefined} changed={changed} ease={ease} />
  );
});

// 片段拖动写回作者 JSON 所需的位置：所属 Timeline、轨道、片段身份，以及本段的运行秒数零点。
interface ClipEdit { documentId: string; trackId: string; clipId: string; origin: number }

interface ClipEase { in: number; out: number; set(easeIn: number | null, easeOut: number | null): void }

// 淡入淡出（照 Timeline Editor）：片段内画出淡入淡出斜线，顶边两端的小方块左右拖动改时长，按吸附步长取整。
function EaseHandles({ ease, start, end }: { ease: ClipEase; start: number; end: number }) {
  const { valueToPixels, getValueFromScreenX } = useTimelineContext();
  const step = useContext(SnapContext) ?? 1 / 60;
  const [draft, setDraft] = useState<{ side: 'in' | 'out'; seconds: number } | null>(null);
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
  return (
    <>
      <svg className={styles.easeShape} viewBox={`0 0 ${Math.max(1, width)} 10`} preserveAspectRatio="none" aria-hidden>
        {inPx > 0 && <polygon points={`0,10 ${inPx},0 0,0`} />}
        {outPx > 0 && <polygon points={`${width},10 ${width - outPx},0 ${width},0`} />}
      </svg>
      {/* 方块中心对准淡入终点、淡出起点，贴边时收在片段内 */}
      <span className={styles.easeHandle} style={{ left: Math.max(0, Math.min(width - 7, inPx - 3.5)) }}
        title={`淡入 ${easeIn.toFixed(3)} s · 左右拖动`} {...handle('in')} />
      <span className={styles.easeHandle} style={{ left: Math.max(0, Math.min(width - 7, width - outPx - 3.5)) }}
        title={`淡出 ${easeOut.toFixed(3)} s · 左右拖动`} {...handle('out')} />
    </>
  );
}

// 片段框：作者片段与预览输入的按住段共用同一外观、选中、框选与双击打开节点图。
// exit 之后的部分压暗；next 为同轨下一个片段的起点，窄片段的名字画在两者之间。
// 作者模式下给出 edit 时可拖动平移、拖两端改起止（照 Timeline Editor）。
function ClipBox({ boxKey, selectId, kind, label, start, end, exit, next, graphId, edit, changed, ease }: {
  boxKey: string; selectId: string; kind: TrackKind; label: string; start: number; end: number; exit: number; next: number;
  graphId: string | null; edit?: ClipEdit; changed?: boolean; ease?: ClipEase;
}) {
  const { setNodeRef, itemStyle, itemContentStyle, listeners, attributes } = useItem({
    id: boxKey, span: { start, end }, disabled: !edit, data: edit ? { edit } : undefined, resizeHandleWidth: 6,
  });
  const { valueToPixels } = useTimelineContext();
  const clock = useContext(PreviewClockContext);
  const active = usePreviewSelect(clock, seconds => seconds !== null && start <= seconds && seconds < Math.min(end, exit));
  const boxed = useContext(BoxedContext).has(boxKey);
  const selected = useSelected(selectId, 'clip') || boxed;
  const width = valueToPixels(end - start);
  const dimmed = start >= exit;
  const cut = !dimmed && end > exit ? valueToPixels(exit - start) : null;
  const band = kind === 'anim' || kind === 'motion';
  const inside = band || width >= labelInside;
  const outsideWidth = Number.isFinite(next) ? valueToPixels(next - end) - 10 : 400;
  return (
    <div ref={setNodeRef} style={itemStyle} className={styles.item} {...(edit ? { ...listeners, ...attributes } : {})}>
      <div style={itemContentStyle}>
        <button className={styles.clip} data-box={boxKey} data-select={selectId} data-kind={kind} data-active={active || undefined}
          data-selected={selected || undefined} data-dim={dimmed || undefined} data-editable={edit ? true : undefined}
          data-changed={changed || undefined}
          title={`${label} · ${start.toFixed(3)}–${end.toFixed(3)} s${end > exit ? `（${exit.toFixed(3)} s 离开本段）` : ''}\n双击打开节点图${edit ? '\n拖动平移，拖两端改起止' : ''}`}
          onClick={() => selectTimelineObject(clock, 'clip', selectId)}
          onDoubleClick={() => openClipGraph(graphId, label)}>
          {inside && <span className={styles.clipLabel}>{label}</span>}
          {band && (ease ? <EaseHandles ease={ease} start={start} end={end} /> : <><i className={styles.easeIn} /><i className={styles.easeOut} /></>)}
          {cut !== null && <i className={styles.clipCut} style={{ left: cut }} />}
        </button>
        {!inside && outsideWidth > 12 && (
          <span className={styles.clipOut} data-dim={dimmed || undefined} style={{ left: width + 5, maxWidth: outsideWidth }}>{label}</span>
        )}
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

function Blend({ start, end }: { start: number; end: number }) {
  return (
    <Positioned start={start} end={end} className={styles.blend}>
      <svg viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden>
        <path d="M0 8 C40 8 60 92 100 92" /><path d="M0 92 C40 92 60 8 100 8" />
      </svg>
    </Positioned>
  );
}

// 标记：作者标记与预览输入的单次按键共用；单击选中（有 selectId 时），双击打开节点图。
// 作者模式给出 move 时可左右拖动（照 Timeline Editor MoveMarker），拖动中按 snap 取整显示，松开写回。
function Marker({ seconds: shown, label, graphId, selectId, move, snap }: {
  seconds: number; label: string; graphId: string; selectId?: string; move?(seconds: number): void; snap?(seconds: number): number;
}) {
  const { range, valueToPixels, getValueFromScreenX } = useTimelineContext();
  const [dragging, setDragging] = useState<{ x: number; seconds: number } | null>(null);
  const seconds = dragging?.seconds ?? shown;
  const clock = useContext(PreviewClockContext);
  const active = usePreviewSelect(clock, (current, state) => {
    const previous = clock.kind === 'archive' ? transport.secondsAt(state.frameIndex - 1) : null;
    return current !== null && (seconds === current || previous !== null && seconds > previous && seconds <= current);
  });
  const selected = useWorkbench(state => !!selectId && state.selection?.kind === 'clip' && state.selection.id === selectId);
  return (
    <button className={styles.marker} data-active={active || undefined} data-selected={selected || undefined}
      data-select={selectId} data-editable={move ? true : undefined}
      style={{ left: valueToPixels(seconds - range.start) }}
      title={`${label} · ${seconds.toFixed(3)} s\n双击打开节点图${move ? '\n左右拖动改时刻' : ''}`}
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
        if (dragging && dragging.seconds !== shown) move!(dragging.seconds);
        setDragging(null);
      }}
      onPointerCancel={() => setDragging(null)}
      onClick={() => { if (selectId) selectTimelineObject(clock, 'clip', selectId); }}
      onDoubleClick={() => openClipGraph(graphId, label)} />
  );
}

// ── 预览输入组 ──

function CurveGroupLine({ open, toggle }: { open: boolean; toggle(id: string): void }) {
  const { curves, selected, availability } = useWorkbench(useShallow(state => ({
    curves: state.archiveCurves, selected: state.selectedCurves, availability: state.curveAvailability,
  })));
  const reason = availability.find(item => item.channel === 'data-curves' && !item.available)?.reason;
  const entries: MenuEntry[] = (curves ?? []).map(curve => ({
    kind: 'check', label: `${curve.actorId ? `${curve.actorId} · ` : ''}${curve.displayName}`,
    checked: selected.includes(curve.id), onChange: checked => runActions.selectCurve(curve.id, checked),
  }));
  return <Line id="curves" height={30} headClass={styles.groupHead} laneClass={styles.groupLane} head={<>
    <button className={styles.fold} onClick={() => toggle('curves')} aria-expanded={open} aria-label={open ? '折叠曲线' : '展开曲线'}>
      <Triangle open={open} />
    </button>
    <span className={styles.segmentTitle}>项目曲线</span>
    {entries.length > 0 && <MenuButton label="选择项目曲线" caret entries={entries}>选择曲线</MenuButton>}
  </>}>
    {reason ? <span className={styles.curveNotice} title={reason}>{reason}</span>
      : !curves?.length ? <span className={styles.curveNotice}>此档案未声明曲线</span> : null}
  </Line>;
}

function CurveLine({ row }: { row: CurveRow }) {
  const clock = useContext(CurveClockContext);
  const { range } = useTimelineContext();
  const { runId, frame, hostFrame, image, metadata } = useWorkbench(useShallow(state => ({
    runId: state.runId, frame: state.archiveFrame, hostFrame: state.hostFrame,
    image: state.archiveImage, metadata: state.archiveMetadata,
  })));
  const selectId = runId === null ? null : `curve/${runId}/${row.curve.id}`;
  const selected = useSelected(selectId ?? '', 'track');
  const samples = useMemo(() => !runId || !clock ? [] : archive.curveSamples(runId, row.curve.id).map(sample => ({
    sample, seconds: clock.seconds + Number(BigInt(sample.source.sdkFrame) - BigInt(clock.sourceFrame)) / clock.sdkTickRate,
  })).filter(point => point.seconds >= range.start && point.seconds <= range.end), [runId, row.curve.id, frame, clock, range]);
  const min = samples.length ? Math.min(...samples.map(point => point.sample.value)) : 0;
  const max = samples.length ? Math.max(...samples.map(point => point.sample.value)) : 0;
  const record = frame?.curves[row.curve.id];
  const reason = !clock ? '没有匹配的运行时钟' : record?.kind === 'failed' ? record.reason
    : record?.kind === 'sampled' ? record.availability.find(item =>
      (item.channel === row.curve.channel || item.channel === 'data-curves') && !item.available)?.reason ?? null : '正在读取曲线';
  const select = () => { if (selectId !== null) selectionActions.select({ kind: 'track', id: selectId }); };
  return <Line id={row.id} height={64} headClass={styles.trackHead} head={
    <button className={styles.headButton} data-selected={selected || undefined}
      style={{ ['--tc' as string]: 'var(--track-motion)' }} disabled={selectId === null}
      title={`${row.curve.clock} · ${row.curve.coordinates} · ${row.curve.unit}`} onClick={select}>
      <span className={styles.slot} /><span className={styles.name}>{row.curve.displayName}</span>
    </button>
  }>
    {reason ? <span className={styles.curveNotice} title={reason}>{reason}</span> : <>
      <svg className={styles.curvePlot} viewBox="0 0 1000 64" preserveAspectRatio="none" aria-label={row.curve.displayName}>
        {samples.map(({ sample, seconds }, index) => {
          const matchingImage = sample.presentationSequence === null || image !== null && image.kind !== 'unavailable' &&
            sample.presentationSequence === image.metadata.presentationSequence;
          return <circle key={`${sample.source.sdkFrame}/${sample.presentationSequence ?? 'logic'}/${index}`}
            cx={(seconds - range.start) / (range.end - range.start) * 1000}
            cy={max === min ? 32 : 57 - (sample.value - min) / (max - min) * 50} r={2.5}
            data-current={sample.source.sdkFrame === hostFrame && matchingImage || undefined}
            onClick={() => {
              select();
              if (metadata) void transport.seekTime(Number(BigInt(sample.source.sdkFrame) - BigInt(metadata.firstRecordedSdkFrame)) / metadata.sdkTickRate);
            }}>
            <title>{`${sample.value} ${row.curve.unit}\nSDK ${sample.source.sdkFrame} · ${row.curve.clock}\n原值 ${sample.rawValue}${sample.presentationSequence === null ? '' : `\n表现序号 ${sample.presentationSequence} · Tick ${sample.presentationTick}`}`}</title>
          </circle>;
        })}
      </svg>
      {samples.length > 0 && <span className={styles.curveScale}>{max.toPrecision(4)} / {min.toPrecision(4)} {row.curve.unit}</span>}
    </>}
  </Line>;
}

function InputGroupLine({ open, toggle, roots }: {
  open: boolean; toggle(id: string): void; roots: FrozenInputTimeline[] | undefined;
}) {
  const inputRoot = useWorkbench(state => state.inputRoot);
  const reason = useWorkbench(state => state.inputTimeline?.availability[0]?.reason ?? '');
  const options = (roots ?? []).map(root => ({ value: inputRootKey(root), label: `方案：${root.displayName}` }));
  return (
    <Line id="input" height={rowHeights.inputGroup} laneClass={styles.groupLane} headClass={styles.groupHead} head={
      <>
        <button className={styles.fold} onClick={() => toggle('input')} aria-expanded={open} disabled={!options.length}
          aria-label={open ? '折叠' : '展开'}>
          <Triangle open={open && !!options.length} />
        </button>
        <span className={styles.segmentTitle}>预览输入</span>
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
const InputTrackLine = memo(function InputTrackLine({ row }: { row: InputTrackRow }) {
  const selected = useSelected(row.selectId, 'track');
  const routeEnd = useWorkbench(() => transport.duration());
  const { range } = useTimelineContext();
  const clips = row.track.clips;
  const nextStarts = useMemo(() => {
    const starts = clips.map(clip => clip.startSeconds).sort((left, right) => left - right);
    return new Map(clips.map(clip => [clip.id, starts.find(start => start > clip.startSeconds) ?? Infinity]));
  }, [clips]);
  if (routeEnd === null) return null;
  return (
    <Line id={row.id} height={rowHeights.in} headClass={styles.trackHead} head={
      <button className={styles.headButton} data-selected={selected || undefined}
        style={{ ['--tc' as string]: trackColors.in }} onClick={() => selectionActions.select({ kind: 'track', id: row.selectId })}>
        <span className={styles.slot} />
        <span className={styles.name}>{row.track.displayName}</span>
        <TrackTools />
      </button>
    }>
      {clips.filter(clip => clip.endSeconds >= range.start && clip.startSeconds <= range.end).map(clip => {
        const selectId = inputClipSelectId(row.track.id, clip.id);
        return (
          <ClipBox key={clip.id} boxKey={selectId} selectId={selectId} kind="in" label={inputLabel(clip.writes, true)}
            start={clip.startSeconds} end={clip.endSeconds} exit={routeEnd} next={nextStarts.get(clip.id)!} graphId={clip.graphId} />
        );
      })}
      {row.track.markers.filter(marker => marker.seconds >= range.start && marker.seconds <= range.end).map(marker => (
        <Marker key={marker.id} seconds={marker.seconds} label={inputLabel(marker.writes, false)} graphId={marker.graphId}
          selectId={inputClipSelectId(row.track.id, marker.id)} />
      ))}
    </Line>
  );
});

// ── 游标覆盖线与结束线：直接改 transform，不经过 React ──

function Overlay({ bounds, origin, seekAt, segments, follow, onRangeChanged }: {
  bounds: Range; origin: number; seekAt(clientX: number): void; segments: RouteSegment[]; follow: boolean;
  onRangeChanged(update: (previous: Range) => Range): void;
}) {
  const { range, valueToPixels, sidebarWidth } = useTimelineContext();
  const line = useRef<HTMLDivElement>(null);
  const tag = useRef<HTMLDivElement>(null);
  const panelId = usePanelId();
  const visible = usePanelVisible();
  const panelWindow = usePanelWindow();
  const pending = useRef(false);
  const clock = useContext(PreviewClockContext);

  const apply = useCallback(() => {
    const state = getState();
    const lineElement = line.current, tagElement = tag.current;
    if (!lineElement || !tagElement) return;
    const seconds = previewSeconds(state, clock, authorCursorOf(useAuthorCursor.getState(), clock));
    if (seconds === null) {
      lineElement.hidden = tagElement.hidden = true;
    } else {
      if (follow && transport.isPlaying() && (seconds > range.end || seconds < range.start)) {
        const span = range.end - range.start;
        onRangeChanged(() => ({ start: seconds - span * 0.1, end: seconds + span * 0.9 }));
      }
      const x = valueToPixels(seconds - range.start);
      lineElement.hidden = tagElement.hidden = false;
      lineElement.style.transform = tagElement.style.transform = `translateX(${x}px)`;
      tagElement.textContent = `${(seconds - origin).toFixed(6)} s`;
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

  const x = (seconds: number) => `translateX(${valueToPixels(seconds - range.start)}px)`;
  return (
    <div className={styles.overlay} style={{ left: sidebarWidth }}>
      <div className={styles.startLine} style={{ transform: x(bounds.start) }} />
      {segments.filter(segment => segment.start > range.start && segment.start < range.end).map(segment => (
        <div key={segment.id} className={styles.segmentLine} style={{ transform: x(segment.start) }} />
      ))}
      <div className={styles.endLine} style={{ transform: x(bounds.end) }} />
      <div ref={line} className={styles.cursorLine} hidden />
      {/* 游标头：按住拖动移动游标 */}
      <div ref={tag} className={styles.cursorTag} hidden title="拖动移动游标"
        onPointerDown={event => {
          if (event.button !== 0) return;
          event.currentTarget.setPointerCapture(event.pointerId);
          event.currentTarget.dataset.dragging = '';
        }}
        onPointerMove={event => { if (event.currentTarget.hasPointerCapture(event.pointerId)) seekAt(event.clientX); }}
        onPointerUp={event => { delete event.currentTarget.dataset.dragging; }}
        onPointerCancel={event => { delete event.currentTarget.dataset.dragging; }} />
    </div>
  );
}
