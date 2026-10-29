import {
  TimelineContext, useItem, useRow, useTimelineContext, useTimelineMonitor, type DragItemData, type Range,
  type ResizeEndEvent, type ResizeItemData, type TimelineContextProps, type UsePanStrategy,
} from 'dnd-timeline';
import {
  createContext, memo, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type PointerEvent,
  type ReactNode,
} from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { FrozenInputTimeline, InputWrite } from '../../api/types';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered } from '../../shell/readiness';
import { FrameField, useTransportState } from '../../shell/Toolbar';
import { openPanel } from '../../shell/layout';
import {
  applyTimelineEdits, authoringActions, timelinePath, useAuthoring,
} from '../../state/authoring';
import {
  consoleActions, getState, inputRootKey, runActions, selectionActions, useWorkbench, type RouteFrameSpan,
} from '../../state/store';
import {
  clipGraph, inputClipSelectId, inputTrackSelectId, nearestFrame, routeSegments, segmentRows, segmentTitle, selectedInputRoot,
  trackColors, type ClipInstance, type GroupRow, type RouteSegment, type TrackKind, type TrackRow,
} from '../../state/timelineModel';
import { graphKey, nodeSelectId } from '../../state/graphModel';
import { transport } from '../../state/transport';
import { Empty } from '../../ui/blocks';
import { Button, MenuButton, Select, TextInput, type MenuEntry } from '../../ui/controls';
import styles from './timeline.module.css';

// Timeline 面板（D5）：照 BTSMTL Timeline Editor，按路线段（状态 → Timeline）分组，每段可折叠。
// dnd-timeline 管时间与像素换算、缩放、平移；刻度、片段、输入标记、游标自绘。
// 游标是独立覆盖线，订阅 cursor 后直接改 transform；片段只按自身是否处于游标下切换 class。

// 轨道头宽度，照 Timeline Editor 约占窗口三分之一；CSS 经 --sidebar 读取。
const sidebarWidth = 380;
const minSpan = 0.05;
const rowHeights = { group: 30, anim: 46, motion: 46, act: 42, in: 42, other: 42, inputGroup: 30 } as const;
const overscan = 120;
// 窄于此宽度的片段把名字画在片段后面。
const labelInside = 46;

interface InputGroupRow { kind: 'inputGroup'; id: string }
interface InputTrackRow { kind: 'inputTrack'; id: string; selectId: string; track: FrozenInputTimeline['tracks'][number] }
type Row = GroupRow | TrackRow | InputGroupRow | InputTrackRow;

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
}
const defaults: Params = { range: null, collapsed: [], follow: true, filter: '', program: null, focus: null, routeRange: null };

// 一次 Timeline 播放：在显示的段里取游标所在的那次，否则第一次。
function playbackAt(segments: RouteSegment[], documentId: string, frameIndex: number) {
  const plays = segments.filter(segment => segment.documentId === documentId);
  return plays.find(segment => segment.firstIndex <= frameIndex && frameIndex <= segment.lastIndex) ?? plays[0] ?? null;
}

const segmentRange = (segment: RouteSegment) => fitRange({ start: segment.origin, end: segment.origin + segment.length });

function rowHeight(row: Row): number {
  switch (row.kind) {
    case 'group': return rowHeights.group;
    case 'inputGroup': return rowHeights.inputGroup;
    case 'inputTrack': return rowHeights.in;
    case 'track': return rowHeights[row.trackKind];
  }
}

export function TimelinePanel() {
  const { runId, frames } = useWorkbench(useShallow(state => ({ runId: state.runId, frames: state.routeFrames })));
  if (!runId) return <Empty text="选择一个运行" />;
  if (!frames.length) return <Empty text="还没有确认帧" />;
  return <TimelineWindow frames={frames} />;
}

function fitRange(bounds: Range): Range {
  const pad = Math.max(minSpan, (bounds.end - bounds.start) * 0.03);
  return { start: bounds.start - pad, end: bounds.end + pad };
}

function TimelineWindow({ frames }: { frames: RouteFrameSpan[] }) {
  const [params, setParams] = usePanelParams<Params>(defaults);
  const bounds = useMemo(() => ({ start: frames[0].seconds, end: frames[frames.length - 1].seconds }), [frames]);
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

  return (
    <TimelineContext range={range} onRangeChanged={onRangeChanged} onResizeEnd={() => {}} modifiers={horizontalOnly}
      sidebarWidth={sidebarWidth} usePanStrategy={panStrategy}>
      <ClipEditing frames={frames} />
      <TimelineBody params={params} setParams={setParams} bounds={bounds} frames={frames} onRangeChanged={onRangeChanged} />
    </TimelineContext>
  );
}

// 作者模式拖动、拖端写回片段时间：原起止加上拖动的像素位移换成的秒数，按运行帧步长取整，
// 再减去本段零点得到 Timeline 本地秒数。
function ClipEditing({ frames }: { frames: RouteFrameSpan[] }) {
  const { range, timelineRef, sidebarWidth: sidebar } = useTimelineContext();
  // 像素位移换成秒数：可见范围除以时间区宽度。
  const pixelsToValue = (pixels: number) => {
    const width = (timelineRef.current?.clientWidth ?? 0) - sidebar;
    return width > 0 ? pixels * (range.end - range.start) / width : 0;
  };
  const frameStep = frames.length > 1 ? frames[1].seconds - frames[0].seconds : 1 / 60;
  const write = (edit: ClipEdit | undefined, span: Range, deltaStart: number, deltaEnd: number) => {
    if (!edit) return;
    const local = (value: number) => Math.round((value - edit.origin) / frameStep) * frameStep;
    const start = Math.max(0, local(span.start + deltaStart));
    const end = Math.max(start + frameStep, local(span.end + deltaEnd));
    authoringActions.setClipTime(edit.documentId, edit.clipId, start, end);
  };
  useTimelineMonitor({
    onDragEnd(event) {
      const data = event.active.data.current as (DragItemData & { edit?: ClipEdit }) | undefined;
      const delta = pixelsToValue(event.delta.x);
      if (data && delta) write(data.edit, data.span, delta, delta);
    },
    onResizeEnd(event: ResizeEndEvent) {
      const data = event.active.data.current as ResizeItemData & { edit?: ClipEdit };
      const delta = pixelsToValue(event.delta.x);
      if (delta) write(data.edit, data.span, event.direction === 'start' ? delta : 0, event.direction === 'end' ? delta : 0);
    },
  });
  return null;
}

// 片段只在本轨内左右拖动。
const horizontalOnly: NonNullable<TimelineContextProps['modifiers']> = [({ transform }) => ({ ...transform, y: 0 })];

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
  frames: RouteFrameSpan[];
  onRangeChanged(update: (previous: Range) => Range): void;
}) {
  const { setTimelineRef, getValueFromScreenX } = useTimelineContext();
  const { timelines: runTimelines, inputTimeline, inputRoot, graphs } = useWorkbench(useShallow(state => ({
    timelines: state.timelines, inputTimeline: state.inputTimeline, inputRoot: state.inputRoot, graphs: state.graphs,
  })));
  // 作者模式下未保存的片段时间叠加到运行的作者 Timeline 上。
  const edited = useAuthoring(state => state.edited);
  const timelines = useMemo(() => applyTimelineEdits(runTimelines, edited), [runTimelines, edited]);
  const allSegments = useMemo(() => routeSegments(frames, timelines, graphs), [frames, timelines, graphs]);
  const graphNames = useMemo(() => new Map((graphs ?? []).filter(graph => graph.displayName && graph.displayName !== graph.id)
    .map(graph => [graph.id, graph.displayName!])), [graphs]);

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
  const focused = focusId ? segments.find(segment => segment.id === focusId) ?? null : null;

  const enter = useCallback((documentId: string) => {
    const segment = playbackAt(segments, documentId, getState().frameIndex);
    if (!segment) return;
    const frameIndex = getState().frameIndex;
    if (frameIndex < segment.firstIndex || frameIndex > segment.lastIndex) transport.seek(segment.firstIndex);
    setParams({ focus: documentId, routeRange: params.focus ? params.routeRange : params.range, range: segmentRange(segment) });
  }, [segments, setParams, params.focus, params.routeRange, params.range]);
  const back = useCallback(() => setParams({ focus: null, range: params.routeRange, routeRange: null }), [setParams, params.routeRange]);

  // 节点图双击播放 Timeline 的节点时进入该 Timeline。
  const request = useWorkbench(state => state.timelineRequest);
  const handled = useRef(request?.sequence ?? 0);
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
    if (focused) {
      const tracks = segmentRows(focused, graphNames);
      return filter ? tracks.filter(row => row.track.displayName.toLowerCase().includes(filter)) : tracks;
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
    return result;
  }, [segments, focused, graphNames, inputTimeline, inputRoot, params.filter, params.collapsed]);

  const toggle = useCallback((id: string) => {
    const collapsed = new Set(params.collapsed);
    if (!collapsed.delete(id)) collapsed.add(id);
    setParams({ collapsed: [...collapsed] });
  }, [params.collapsed, setParams]);

  const seekAt = useCallback((clientX: number) => {
    transport.seek(nearestFrame(getState().routeFrames, getValueFromScreenX(clientX)));
  }, [getValueFromScreenX]);

  const settings: MenuEntry[] = [
    { kind: 'check', label: '游标离开视图时跟随', checked: params.follow, onChange: follow => setParams({ follow }) },
    { kind: 'separator' },
    { label: '展开全部分组', onSelect: () => setParams({ collapsed: [] }) },
    {
      label: '折叠全部分组',
      onSelect: () => setParams({ collapsed: rows.filter(row => row.kind === 'group' || row.kind === 'inputGroup').map(row => row.id) }),
    },
  ];

  const origin = focused?.origin ?? 0;
  const subject: Subject = {
    options: programs.map(item => ({ value: item, label: `${item.slice(item.indexOf(':') + 1)} / 技能根` })),
    program, setProgram: next => setParams({ program: next, focus: null, routeRange: null }),
    focus: focused?.timeline?.displayName ?? (params.focus ? params.focus.slice(0, 8) : null), back,
  };

  return (
    <div className={styles.window} style={{ ['--sidebar' as string]: `${sidebarWidth}px` }}>
      <TimelineToolbar settings={settings} subject={subject}
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
}

// 编辑行右侧：照 Timeline Editor 的未保存提示与保存（只在有未保存修改时出现），作者／运行观察切换。
function AuthoringControls({ program }: { program: string | null }) {
  const state = useAuthoring(useShallow(current => ({
    available: current.available, reason: current.unavailableReason, author: current.author, skill: current.skill,
    loading: current.loading, edited: current.edited, externalChange: current.externalChange,
    saving: current.saving, lastSave: current.lastSave,
  })));
  const dirty = Object.keys(state.edited).length;
  // 作者模式下定时检查技能文件是否在别处（Unity、AI、编辑器）被修改。
  useEffect(() => {
    if (!state.author || !state.skill) return;
    const timer = setInterval(() => void authoringActions.checkExternal(), 3000);
    return () => clearInterval(timer);
  }, [state.author, state.skill]);
  const enter = async () => {
    if (state.available === null) await authoringActions.probe();
    if (!useAuthoring.getState().available) {
      consoleActions.log('warn', '作者', useAuthoring.getState().unavailableReason ?? '作者接口不可用');
      return;
    }
    if (program) void authoringActions.open(program);
    authoringActions.setAuthor(true);
  };
  return (
    <>
      {state.externalChange && <span className={styles.authorWarn} title="技能文件在别处被修改；保存会覆盖外部修改，还原会丢弃网页修改">外部已修改</span>}
      {dirty > 0 && (
        <>
          <span className={styles.authorDirty}>{dirty} 个文件未保存</span>
          <Button variant="ghost" tip="丢弃网页中的全部未保存修改" onClick={() => authoringActions.revert()}>还原</Button>
          <Button variant="ghost" disabled={state.saving} tip="写回技能 JSON，宿主用正式编译器检查"
            onClick={() => void authoringActions.save()}>{state.saving ? '保存中' : '保存'}</Button>
        </>
      )}
      {!dirty && state.lastSave && (
        <span className={state.lastSave.ok ? styles.authorDirty : styles.authorWarn} title={state.lastSave.message}>
          {state.lastSave.ok ? '已保存' : '编译未通过'}
        </span>
      )}
      <Button variant="ghost" pressed={state.author} disabled={state.available === false}
        tip={state.available === false ? state.reason ?? '作者接口不可用'
          : state.author ? (state.loading ? '正在读取技能文件' : `编辑技能 ${state.skill ?? '—'} 的 JSON：拖动片段改时间，拖两端改起止`)
            : '进入作者模式，编辑当前技能'}
        onClick={() => void enter()}>作者</Button>
      <Button variant="ghost" pressed={!state.author} tip="只观察已确认的运行记录" onClick={() => authoringActions.setAuthor(false)}>运行观察</Button>
    </>
  );
}

// 照 Timeline Editor：编辑行返回、技能根与面包屑，右侧作者／运行观察；预览行档位、播放、全局秒数与结论；下行逐帧、适配与设置。
function TimelineToolbar({ settings, fit, subject }: { settings: MenuEntry[]; fit(): void; subject: Subject }) {
  const { playing, blocked, previous, next } = useTransportState();
  const { mode, modes } = useWorkbench(useShallow(state => ({ mode: state.mode, modes: state.host?.modes })));
  const seconds = useWorkbench(state => state.seconds);
  const segment = useWorkbench(state => {
    const frame = state.routeFrames[state.frameIndex];
    const playback = frame?.playbacks[0];
    if (!playback) return '路线未开始';
    const name = state.timelines.find(item => item.documentId === playback.documentId)?.displayName ?? playback.documentId.slice(0, 8);
    return `${name} · 本地 ${playback.cursorSeconds.toFixed(3)} 秒`;
  });
  const root = subject.options.find(item => item.value === subject.program)?.label ?? '技能根';
  return (
    <>
      <div className={styles.toolbar}>
        {subject.focus !== null && <Button variant="ghost" tip="回到技能根路线" onClick={subject.back}>‹ 返回</Button>}
        {subject.focus === null ? (
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
        <AuthoringControls program={subject.program} />
      </div>
      <div className={styles.toolbar}>
        {(modes ?? []).map(item => (
          <Button key={item.id} variant="ghost" className={styles.mode} pressed={mode === item.id} disabled={!item.available}
            tip={item.available ? `预览档位：${item.displayName}` : item.reason}
            onClick={() => { if (item.id === 'off') transport.pause(); runActions.setMode(item.id); }}>
            {item.displayName}
          </Button>
        ))}
        <Button variant="ghost" aria-label={playing ? '暂停' : '播放'} pressed={playing} tip={blocked ?? '播放／暂停（空格）'}
          disabled={!!blocked} onClick={() => transport.toggle()}>
          <i className={playing ? styles.pauseGlyph : styles.playGlyph} />
        </Button>
        <span className={styles.seconds}>{seconds === null ? '—' : `${seconds.toFixed(3)} 秒`}</span>
        <span className={styles.hint}>{segment}</span>
        <span className={styles.grow} />
        <FrameField showSeconds={false} />
      </div>
      <div className={styles.toolbar}>
        <Button aria-label="上一帧" tip={previous ?? '上一帧（←）'} disabled={!!previous} onClick={() => transport.step(-1)}>‹</Button>
        <Button aria-label="下一帧" tip={next ?? '下一帧（→）'} disabled={!!next} onClick={() => transport.step(1)}>›</Button>
        <Button tip="显示整条运行" onClick={fit}>适配</Button>
        <span className={styles.grow} />
        <MenuButton label="Timeline 设置" caret entries={settings}>显示设置</MenuButton>
      </div>
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
  const scroller = useRef<HTMLDivElement>(null);
  const [boxed, setBoxed] = useState<ReadonlySet<string>>(() => new Set());
  const [box, setBox] = useState<Box | null>(null);
  const boxStart = useRef<{ x: number; y: number } | null>(null);
  // 框选后选中的是第一个片段；其它面板或单击换了选中时，框选结果作废。
  const boxedSelect = useRef<string | null>(null);
  const selection = useWorkbench(state => state.selection);
  useEffect(() => {
    if (boxed.size && !(selection?.kind === 'clip' && selection.id === boxedSelect.current)) setBoxed(new Set());
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
    selectionActions.select(hits.length ? { kind: 'clip', id: hits[0].dataset.select! } : null);
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
    <div className={styles.rows} ref={scroller} onPointerDown={boxDown} onPointerMove={boxMove} onPointerUp={boxUp}
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

function Line({ id, height, head, headClass, children, laneClass }: {
  id: string;
  height: number;
  head: ReactNode;
  headClass?: string;
  children?: ReactNode;
  laneClass?: string;
}) {
  const { setNodeRef, rowWrapperStyle, rowStyle, rowSidebarStyle } = useRow({ id, disabled: true });
  return (
    <div className={styles.line} style={{ ...rowWrapperStyle, height }}>
      <div className={headClass} style={rowSidebarStyle}>{head}</div>
      <div ref={setNodeRef} className={laneClass ?? styles.lane} style={rowStyle} data-lane>
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
              selectionActions.showGraph(stateNode.graph);
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

const TrackLine = memo(function TrackLine({ row }: { row: TrackRow }) {
  const selected = useSelected(row.selectId, 'track');
  const { range } = useTimelineContext();
  const visible = row.clips.filter(clip => clip.end >= range.start && clip.start <= range.end);
  const blends = useMemo(() => blendRegions(row.clips), [row.clips]);
  const segment = row.segment;
  // 极短片段的名字画到下一个片段之前。
  const nextStarts = useMemo(() => {
    const starts = row.clips.map(clip => clip.start).sort((left, right) => left - right);
    return new Map(row.clips.map(clip => [clip.key, starts.find(start => start > clip.start) ?? Infinity]));
  }, [row.clips]);
  return (
    <Line id={row.id} height={rowHeights[row.trackKind]} headClass={styles.trackHead} head={
      <button className={styles.headButton} data-selected={selected || undefined}
        style={{ ['--tc' as string]: trackColors[row.trackKind] }}
        onClick={() => selectionActions.select({ kind: 'track', id: row.selectId })}>
        <span className={styles.slot} />
        <span className={styles.name}>{row.track.displayName}</span>
        {row.track.muted && <span className={styles.muted}>静音</span>}
        <TrackTools />
      </button>
    }>
      <Positioned start={segment.origin} end={segment.origin + segment.length} className={styles.authoredRange} />
      <Positioned start={segment.start} end={segment.end} className={styles.playedRange} />
      {visible.map(clip => <Clip key={clip.key} clip={clip} kind={row.trackKind} next={nextStarts.get(clip.key)!}
        documentId={row.timeline.documentId} />)}
      {blends.filter(blend => blend.end >= range.start && blend.start <= range.end).map(blend => (
        <Blend key={blend.key} start={blend.start} end={blend.end} />
      ))}
      {row.markers.filter(marker => marker.seconds >= range.start && marker.seconds <= range.end).map(marker => (
        <Marker key={marker.key} seconds={marker.seconds} label={`${row.track.displayName} 标记`} graphId={marker.graphId} />
      ))}
    </Line>
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

const Clip = memo(function Clip({ clip, kind, next, documentId }: {
  clip: ClipInstance; kind: TrackKind; next: number; documentId: string;
}) {
  const author = useAuthoring(state => state.author && !!state.skill);
  const edit = useMemo<ClipEdit>(() => ({ documentId, clipId: clip.clip.id, origin: clip.start - clip.clip.startSeconds }),
    [documentId, clip]);
  const changed = useAuthoring(state => author && !!state.edited[timelinePath(documentId)]);
  return (
    <ClipBox boxKey={clip.key} selectId={clip.selectId} kind={kind} label={clip.label} start={clip.start} end={clip.end}
      exit={clip.exit} next={next} graphId={clip.clip.graphId} edit={author ? edit : undefined} changed={changed} />
  );
});

// 片段拖动写回作者 JSON 所需的位置：所属 Timeline、片段身份，以及本段的运行秒数零点。
interface ClipEdit { documentId: string; clipId: string; origin: number }

// 片段框：作者片段与预览输入的按住段共用同一外观、选中、框选与双击打开节点图。
// exit 之后的部分压暗；next 为同轨下一个片段的起点，窄片段的名字画在两者之间。
// 作者模式下给出 edit 时可拖动平移、拖两端改起止（照 Timeline Editor）。
function ClipBox({ boxKey, selectId, kind, label, start, end, exit, next, graphId, edit, changed }: {
  boxKey: string; selectId: string; kind: TrackKind; label: string; start: number; end: number; exit: number; next: number;
  graphId: string | null; edit?: ClipEdit; changed?: boolean;
}) {
  const { setNodeRef, itemStyle, itemContentStyle, listeners, attributes } = useItem({
    id: boxKey, span: { start, end }, disabled: !edit, data: edit ? { edit } : undefined, resizeHandleWidth: 6,
  });
  const { valueToPixels } = useTimelineContext();
  const active = useWorkbench(state => state.seconds !== null && start <= state.seconds && state.seconds < Math.min(end, exit));
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
          onClick={() => selectionActions.select({ kind: 'clip', id: selectId })}
          onDoubleClick={() => openClipGraph(graphId, label)}>
          {inside && <span className={styles.clipLabel}>{label}</span>}
          {band && <><i className={styles.easeIn} /><i className={styles.easeOut} /></>}
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
  selectionActions.showGraph(graphKey(graph));
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
function Marker({ seconds, label, graphId, selectId }: { seconds: number; label: string; graphId: string; selectId?: string }) {
  const { range, valueToPixels } = useTimelineContext();
  const active = useWorkbench(state => {
    const current = state.routeFrames[state.frameIndex]?.seconds;
    const previous = state.frameIndex > 0 ? state.routeFrames[state.frameIndex - 1].seconds : -Infinity;
    return current !== undefined && seconds > previous && seconds <= current;
  });
  const selected = useWorkbench(state => !!selectId && state.selection?.kind === 'clip' && state.selection.id === selectId);
  return (
    <button className={styles.marker} data-active={active || undefined} data-selected={selected || undefined}
      style={{ left: valueToPixels(seconds - range.start) }} title={`${label} · ${seconds.toFixed(3)} s\n双击打开节点图`}
      onClick={() => { if (selectId) selectionActions.select({ kind: 'clip', id: selectId }); }}
      onDoubleClick={() => openClipGraph(graphId, label)} />
  );
}

// ── 预览输入组 ──

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
        <Button disabled tip="条件菜单按路线读到的条件生成输入片段，网页尚未接入">条件</Button>
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
  const routeEnd = useWorkbench(state => state.routeFrames.at(-1)?.seconds ?? Infinity);
  const { range } = useTimelineContext();
  const clips = row.track.clips;
  const nextStarts = useMemo(() => {
    const starts = clips.map(clip => clip.startSeconds).sort((left, right) => left - right);
    return new Map(clips.map(clip => [clip.id, starts.find(start => start > clip.startSeconds) ?? Infinity]));
  }, [clips]);
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
  const { range, valueToPixels } = useTimelineContext();
  const line = useRef<HTMLDivElement>(null);
  const tag = useRef<HTMLDivElement>(null);
  const panelId = usePanelId();
  const visible = usePanelVisible();
  const panelWindow = usePanelWindow();
  const pending = useRef(false);

  const apply = useCallback(() => {
    const state = getState();
    const lineElement = line.current, tagElement = tag.current;
    if (!lineElement || !tagElement) return;
    if (state.seconds === null) {
      lineElement.hidden = tagElement.hidden = true;
      return;
    }
    if (follow && transport.isPlaying() && (state.seconds > range.end || state.seconds < range.start)) {
      const span = range.end - range.start;
      onRangeChanged(() => ({ start: state.seconds! - span * 0.1, end: state.seconds! + span * 0.9 }));
    }
    const x = valueToPixels(state.seconds - range.start);
    lineElement.hidden = tagElement.hidden = false;
    lineElement.style.transform = tagElement.style.transform = `translateX(${x}px)`;
    tagElement.textContent = `${(state.seconds - origin).toFixed(6)} s`;
    // 下一次绘制后报告就绪：片段的游标 class 由 React 提交，等到绘制完成。
    if (visible && !pending.current) {
      pending.current = true;
      panelWindow.requestAnimationFrame(() => {
        pending.current = false;
        const current = getState();
        const key = frameKey(current.runId, current.frameIndex);
        if (key) reportRendered(panelId, key);
      });
    }
  }, [range, origin, valueToPixels, follow, onRangeChanged, visible, panelWindow, panelId]);

  useLayoutEffect(() => {
    apply();
    return useWorkbench.subscribe((state, previous) => {
      if (state.seconds !== previous.seconds || state.hostFrame !== previous.hostFrame || state.frameKey !== previous.frameKey) apply();
    });
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
