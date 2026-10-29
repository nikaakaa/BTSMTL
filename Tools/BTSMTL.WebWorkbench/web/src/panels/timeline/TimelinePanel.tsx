import {
  TimelineContext, useItem, useRow, useTimelineContext, type Range, type UsePanStrategy,
} from 'dnd-timeline';
import {
  memo, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type PointerEvent,
  type ReactNode,
} from 'react';
import { useShallow } from 'zustand/react/shallow';
import type { FrozenInputTimeline } from '../../api/types';
import { usePanelId, usePanelParams, usePanelVisible, usePanelWindow } from '../../shell/panel';
import { clearRendered, frameKey, reportRendered } from '../../shell/readiness';
import { FrameField, useTransportState } from '../../shell/Toolbar';
import {
  getState, inputRootKey, runActions, selectionActions, useWorkbench, type RouteFrameSpan,
} from '../../state/store';
import {
  inputClipSelectId, inputTrackSelectId, nearestFrame, selectedInputRoot, timelineRows, trackColors,
  type ClipInstance, type GroupRow, type TrackKind, type TrackRow,
} from '../../state/timelineModel';
import { transport } from '../../state/transport';
import { BarSeparator, Empty } from '../../ui/blocks';
import { Button, MenuButton, ObjectField, Select, TextInput, type MenuEntry } from '../../ui/controls';
import styles from './timeline.module.css';

// Timeline 面板（D5）：dnd-timeline 管时间与像素换算、缩放、平移；刻度、片段、输入标记、游标自绘。
// 游标是独立覆盖线，订阅 cursor 后直接改 transform；片段只按自身是否处于游标下切换 class。

const sidebarWidth = 290;
const minSpan = 0.05;
const rowHeights = { group: 24, anim: 30, act: 24, in: 26, other: 24, inputGroup: 24 } as const;
const overscan = 120;

interface InputGroupRow { kind: 'inputGroup'; id: string }
interface InputTrackRow { kind: 'inputTrack'; id: string; selectId: string; track: FrozenInputTimeline['tracks'][number] }
type Row = GroupRow | TrackRow | InputGroupRow | InputTrackRow;

interface Params {
  range: Range | null;
  collapsed: string[];
  follow: boolean;
  filter: string;
}
const defaults: Params = { range: null, collapsed: [], follow: true, filter: '' };

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
    <TimelineContext range={range} onRangeChanged={onRangeChanged} onResizeEnd={() => {}} sidebarWidth={sidebarWidth}
      usePanStrategy={panStrategy}>
      <TimelineBody params={params} setParams={setParams} bounds={bounds} frames={frames} onRangeChanged={onRangeChanged} />
    </TimelineContext>
  );
}

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
  const { timelines, inputTimeline, inputRoot } = useWorkbench(useShallow(state => ({
    timelines: state.timelines, inputTimeline: state.inputTimeline, inputRoot: state.inputRoot,
  })));

  const rows = useMemo(() => {
    const filter = params.filter.trim().toLowerCase();
    const collapsed = new Set(params.collapsed);
    const result: Row[] = [];
    const root = selectedInputRoot(inputTimeline?.roots, inputRoot);
    if (inputTimeline) {
      result.push({ kind: 'inputGroup', id: 'input' });
      if (root && !collapsed.has('input'))
        for (const track of root.tracks)
          if (!filter || track.displayName.toLowerCase().includes(filter))
            result.push({ kind: 'inputTrack', id: `input:${track.id}`, selectId: inputTrackSelectId(track.id), track });
    }
    let group: GroupRow | null = null;
    let groupRows: TrackRow[] = [];
    const flush = () => {
      if (!group) return;
      const titleMatched = !filter || group.title.toLowerCase().includes(filter);
      const children = titleMatched ? groupRows : groupRows.filter(row => row.track.displayName.toLowerCase().includes(filter));
      if (titleMatched || children.length) result.push(group, ...(collapsed.has(group.id) ? [] : children));
    };
    for (const row of timelineRows(frames, timelines)) {
      if (row.kind === 'group') {
        flush();
        group = row;
        groupRows = [];
      } else groupRows.push(row);
    }
    flush();
    return result;
  }, [frames, timelines, inputTimeline, inputRoot, params.filter, params.collapsed]);

  const toggle = useCallback((id: string) => {
    const collapsed = new Set(params.collapsed);
    if (!collapsed.delete(id)) collapsed.add(id);
    setParams({ collapsed: [...collapsed] });
  }, [params.collapsed, setParams]);

  const seekAt = useCallback((clientX: number) => {
    transport.seek(nearestFrame(getState().routeFrames, getValueFromScreenX(clientX)));
  }, [getValueFromScreenX]);

  const settings: MenuEntry[] = [
    { label: '适配全部', onSelect: () => setParams({ range: fitRange(bounds) }) },
    { kind: 'check', label: '游标离开视图时跟随', checked: params.follow, onChange: follow => setParams({ follow }) },
    { kind: 'separator' },
    { label: '展开全部分组', onSelect: () => setParams({ collapsed: [] }) },
    {
      label: '折叠全部分组',
      onSelect: () => setParams({ collapsed: rows.filter(row => row.kind === 'group' || row.kind === 'inputGroup').map(row => row.id) }),
    },
  ];

  return (
    <div className={styles.window}>
      <TimelineToolbar settings={settings} />
      <div className={styles.timeline} ref={setTimelineRef}>
        <div className={styles.header}>
          <div className={styles.filter}>
            <TextInput className={styles.filterInput} placeholder="筛选轨道" value={params.filter}
              onChange={event => setParams({ filter: event.currentTarget.value })} aria-label="筛选轨道" />
          </div>
          <Ruler bounds={bounds} seekAt={seekAt} />
        </div>
        <div className={styles.markersRow}>
          <div className={styles.markersHead}>Markers</div>
          <PlaybackMarkers frames={frames} timelines={timelines} />
        </div>
        <Rows rows={rows} collapsed={params.collapsed} toggle={toggle} seekAt={seekAt} inputRoots={inputTimeline?.roots} />
        <Overlay bounds={bounds} follow={params.follow} onRangeChanged={onRangeChanged} />
      </div>
    </div>
  );
}

// ── 窗口工具条：Preview 开关、到开头、上一帧、播放、下一帧、到结尾、帧号；右侧资产名与设置 ──

function TimelineToolbar({ settings }: { settings: MenuEntry[] }) {
  const { playing, blocked, previous, next } = useTransportState();
  const { mode, modes } = useWorkbench(useShallow(state => ({ mode: state.mode, modes: state.host?.modes })));
  const lastMode = useRef('real');
  if (mode !== 'off') lastMode.current = mode;
  const canToggle = !!modes?.some(item => item.id === 'off' && item.available);
  const asset = useWorkbench(state => {
    const frame = state.routeFrames[state.frameIndex];
    const documentId = frame?.playbacks[0]?.documentId;
    return documentId ? state.timelines.find(item => item.documentId === documentId)?.displayName ?? '' : '';
  });
  return (
    <div className={styles.toolbar}>
      <Button variant="ghost" className={styles.preview} pressed={mode !== 'off'} disabled={!canToggle}
        tip={canToggle ? '预览开关' : '宿主没有提供关闭档位'}
        onClick={() => { if (mode === 'off') runActions.setMode(lastMode.current); else { transport.pause(); runActions.setMode('off'); } }}>
        Preview
      </Button>
      <BarSeparator />
      <Button variant="ghost" aria-label="到开头" tip={previous ?? '到开头（Home）'} disabled={!!previous} onClick={() => transport.home()}>⏮</Button>
      <Button variant="ghost" aria-label="上一帧" tip={previous ?? '上一帧（←）'} disabled={!!previous} onClick={() => transport.step(-1)}>◀|</Button>
      <Button variant="ghost" aria-label={playing ? '暂停' : '播放'} pressed={playing} tip={blocked ?? '播放／暂停（空格）'}
        disabled={!!blocked} onClick={() => transport.toggle()}>{playing ? '❚❚' : '▶'}</Button>
      <Button variant="ghost" aria-label="下一帧" tip={next ?? '下一帧（→）'} disabled={!!next} onClick={() => transport.step(1)}>|▶</Button>
      <Button variant="ghost" aria-label="到结尾" tip={next ?? '到结尾（End）'} disabled={!!next} onClick={() => transport.end()}>⏭</Button>
      <FrameField showSeconds={false} />
      <span className={styles.grow} />
      <span className={styles.asset}>{asset}</span>
      <MenuButton label="Timeline 设置" variant="ghost" entries={settings}>⚙</MenuButton>
    </div>
  );
}

// ── 刻度：间隔随缩放调整，点击与拖动经 transport 跳帧 ──

const tickSteps = [1 / 60, 1 / 30, 0.05, 0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10, 30, 60];

function Ruler({ bounds, seekAt }: { bounds: Range; seekAt(clientX: number): void }) {
  const { range, valueToPixels } = useTimelineContext();
  const width = valueToPixels(range.end - range.start);
  const step = tickSteps.find(item => valueToPixels(item) >= 64) ?? tickSteps[tickSteps.length - 1];
  const digits = Number.isInteger(step) ? 0 : Number.isInteger(Math.round(step * 1e6) / 1e5) ? 1 : 2;
  const ticks: ReactNode[] = [];
  if (width > 0) {
    const minor = step / 5;
    const first = Math.ceil(range.start / minor);
    for (let index = first; index * minor <= range.end; index++) {
      const value = index * minor;
      const major = index % 5 === 0;
      const x = valueToPixels(value - range.start);
      ticks.push(<i key={index} className={major ? styles.majorTick : styles.minorTick} style={{ left: x }} />);
      if (major) ticks.push(<span key={`l${index}`} className={styles.tickLabel} style={{ left: x }}>{value.toFixed(digits)}</span>);
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
    </div>
  );
}

// Markers 行：每段 Timeline 播放的起点画成五边形旗。
function PlaybackMarkers({ frames, timelines }: { frames: RouteFrameSpan[]; timelines: { documentId: string; displayName: string }[] }) {
  const { range, valueToPixels } = useTimelineContext();
  const names = useMemo(() => new Map(timelines.map(item => [item.documentId, item.displayName])), [timelines]);
  const starts = useMemo(() => {
    const result: { key: string; seconds: number; name: string }[] = [];
    const active = new Set<string>();
    for (const frame of frames) {
      const now = new Set(frame.playbacks.map(playback => playback.documentId));
      for (const id of now) if (!active.has(id)) result.push({ key: `${id}@${frame.hostFrame}`, seconds: frame.seconds, name: names.get(id) ?? id.slice(0, 8) });
      active.clear();
      for (const id of now) active.add(id);
    }
    return result;
  }, [frames, names]);
  return (
    <div className={styles.markersLane}>
      {starts.filter(item => item.seconds >= range.start && item.seconds <= range.end).map(item => (
        <span key={item.key} className={styles.flag} style={{ left: valueToPixels(item.seconds - range.start) }} title={item.name}>
          <i /><span>{item.name}</span>
        </span>
      ))}
    </div>
  );
}

// ── 行：只渲染可见行 ──

function Rows({ rows, collapsed, toggle, seekAt, inputRoots }: {
  rows: Row[];
  collapsed: string[];
  toggle(id: string): void;
  seekAt(clientX: number): void;
  inputRoots: FrozenInputTimeline[] | undefined;
}) {
  const scroller = useRef<HTMLDivElement>(null);
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
    <div className={styles.rows} ref={scroller} onScroll={event => {
      const top = event.currentTarget.scrollTop;
      setView(previous => previous.top === top ? previous : { ...previous, top });
    }}>
      <div style={{ height: offsets[first] }} />
      {rows.slice(first, last).map(row => {
        switch (row.kind) {
          case 'group': return <GroupLine key={row.id} row={row} open={!collapsedSet.has(row.id)} toggle={toggle} seekAt={seekAt} />;
          case 'track': return <TrackLine key={row.id} row={row} seekAt={seekAt} />;
          case 'inputGroup':
            return <InputGroupLine key={row.id} open={!collapsedSet.has(row.id)} toggle={toggle} roots={inputRoots} seekAt={seekAt} />;
          case 'inputTrack': return <InputTrackLine key={row.id} row={row} seekAt={seekAt} />;
        }
      })}
      <div style={{ height: total - offsets[last] }} />
    </div>
  );
}

function Line({ id, height, head, headClass, children, seekAt, laneClass }: {
  id: string;
  height: number;
  head: ReactNode;
  headClass?: string;
  children?: ReactNode;
  seekAt(clientX: number): void;
  laneClass?: string;
}) {
  const { setNodeRef, rowWrapperStyle, rowStyle, rowSidebarStyle } = useRow({ id, disabled: true });
  return (
    <div className={styles.line} style={{ ...rowWrapperStyle, height }}>
      <div className={headClass} style={rowSidebarStyle}>{head}</div>
      <div ref={setNodeRef} className={laneClass ?? styles.lane} style={rowStyle}
        onPointerDown={event => { if (event.button === 0 && event.target === event.currentTarget) seekAt(event.clientX); }}>
        {children}
      </div>
    </div>
  );
}

const Triangle = ({ open }: { open: boolean }) => <i className={open ? styles.foldOpen : styles.foldClosed} />;

const GroupLine = memo(function GroupLine({ row, open, toggle, seekAt }: {
  row: GroupRow; open: boolean; toggle(id: string): void; seekAt(clientX: number): void;
}) {
  const value = useWorkbench(state => {
    const playback = state.routeFrames[state.frameIndex]?.playbacks.find(item => item.documentId === row.documentId);
    return playback ? `${playback.cursorSeconds.toFixed(3)} s` : '';
  });
  const { range, valueToPixels } = useTimelineContext();
  return (
    <Line id={row.id} height={rowHeights.group} seekAt={seekAt} laneClass={styles.groupLane} headClass={styles.groupHead} head={
      <>
        <button className={styles.fold} onClick={() => toggle(row.id)} aria-expanded={open} aria-label={open ? '折叠' : '展开'}>
          <Triangle open={open} />
        </button>
        <span className={styles.name}>{row.title}</span>
        <span className={styles.value}>{value}</span>
      </>
    }>
      {row.segments.map(segment => (
        <span key={segment.index} className={styles.segment} title={`实际播放 ${segment.start.toFixed(3)} — ${segment.end.toFixed(3)} s`}
          style={{ left: valueToPixels(segment.start - range.start), width: Math.max(1, valueToPixels(segment.end - segment.start)) }} />
      ))}
    </Line>
  );
});

function useSelected(selectId: string, kind: 'clip' | 'track') {
  return useWorkbench(state => state.selection?.kind === kind && state.selection.id === selectId);
}

const TrackLine = memo(function TrackLine({ row, seekAt }: { row: TrackRow; seekAt(clientX: number): void }) {
  const selected = useSelected(row.selectId, 'track');
  const value = useWorkbench(state => {
    const seconds = state.seconds;
    if (seconds === null) return null;
    return row.clips.find(clip => clip.start <= seconds && (seconds < clip.end || (clip.cut && seconds <= clip.end)))?.clip.displayName ?? null;
  });
  const { range } = useTimelineContext();
  const visible = row.clips.filter(clip => clip.end >= range.start && clip.start <= range.end);
  const blends = useMemo(() => blendRegions(row.clips), [row.clips]);
  return (
    <Line id={row.id} height={rowHeights[row.trackKind]} seekAt={seekAt} headClass={styles.trackHead} head={
      <button className={styles.headButton} data-selected={selected || undefined}
        style={{ ['--tc' as string]: trackColors[row.trackKind] }}
        onClick={() => selectionActions.select({ kind: 'track', id: row.selectId })}>
        <span className={styles.name}>{row.track.displayName}</span>
        <ObjectField className={styles.binding} kind={trackColors[row.trackKind]} text={value ?? 'None'} />
        {row.track.muted && <span className={styles.muted}>静音</span>}
      </button>
    }>
      {visible.map(clip => <Clip key={clip.key} clip={clip} kind={row.trackKind} />)}
      {blends.filter(blend => blend.end >= range.start && blend.start <= range.end).map(blend => (
        <Blend key={blend.key} start={blend.start} end={blend.end} />
      ))}
      {row.markers.filter(marker => marker.seconds >= range.start && marker.seconds <= range.end).map(marker => (
        <Marker key={marker.key} seconds={marker.seconds} title={marker.graphId} />
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

function Positioned({ start, end, className, children, style }: {
  start: number; end: number; className: string; children?: ReactNode; style?: CSSProperties;
}) {
  const { range, valueToPixels } = useTimelineContext();
  return (
    <div className={className} style={{ ...style, left: valueToPixels(start - range.start), width: Math.max(1, valueToPixels(end - start)) }}>
      {children}
    </div>
  );
}

const Clip = memo(function Clip({ clip, kind }: { clip: ClipInstance; kind: TrackKind }) {
  const { setNodeRef, itemStyle, itemContentStyle } = useItem({ id: clip.key, span: { start: clip.start, end: clip.end }, disabled: true });
  const active = useWorkbench(state => state.seconds !== null && clip.start <= state.seconds &&
    (state.seconds < clip.end || (clip.cut && state.seconds <= clip.end)));
  const selected = useSelected(clip.selectId, 'clip');
  const label = kind === 'act' ? 'Active' : clip.clip.displayName ?? clip.clip.contract;
  return (
    <div ref={setNodeRef} style={itemStyle} className={styles.item}>
      <div style={itemContentStyle}>
        <button className={styles.clip} data-kind={kind} data-active={active || undefined} data-selected={selected || undefined}
          data-cut={clip.cut || undefined} style={{ ['--tc' as string]: trackColors[kind] }}
          title={`${clip.clip.displayName ?? clip.clip.contract}\n${clip.start.toFixed(3)} — ${clip.end.toFixed(3)} s${clip.cut ? '（播放中断）' : ''}`}
          onClick={() => selectionActions.select({ kind: 'clip', id: clip.selectId })}>
          <span>{label}</span>
        </button>
      </div>
    </div>
  );
});

function Blend({ start, end }: { start: number; end: number }) {
  return (
    <Positioned start={start} end={end} className={styles.blend}>
      <svg viewBox="0 0 100 100" preserveAspectRatio="none" aria-hidden>
        <path d="M0 8 C40 8 60 92 100 92" /><path d="M0 92 C40 92 60 8 100 8" />
      </svg>
    </Positioned>
  );
}

function Marker({ seconds, title }: { seconds: number; title: string }) {
  const { range, valueToPixels } = useTimelineContext();
  return <span className={styles.marker} style={{ left: valueToPixels(seconds - range.start) }} title={title} />;
}

// ── 预览输入组 ──

function InputGroupLine({ open, toggle, roots, seekAt }: {
  open: boolean; toggle(id: string): void; roots: FrozenInputTimeline[] | undefined; seekAt(clientX: number): void;
}) {
  const inputRoot = useWorkbench(state => state.inputRoot);
  const reason = useWorkbench(state => state.inputTimeline?.availability[0]?.reason ?? '');
  const options = (roots ?? []).map(root => ({ value: inputRootKey(root), label: root.displayName }));
  return (
    <Line id="input" height={rowHeights.inputGroup} seekAt={seekAt} laneClass={styles.groupLane} headClass={styles.groupHead} head={
      <>
        <button className={styles.fold} onClick={() => toggle('input')} aria-expanded={open} disabled={!options.length}
          aria-label={open ? '折叠' : '展开'}>
          <Triangle open={open && !!options.length} />
        </button>
        <span className={styles.name}>预览输入</span>
        <span className={styles.scheme}>
          <Select label="已采用输入方案" placeholder="方案" value={inputRoot} options={options}
            disabled={options.length < 2} tip={options.length ? undefined : reason || '本次运行没有预览输入'}
            onChange={key => runActions.selectInputRoot(key)} />
        </span>
      </>
    } />
  );
}

const InputTrackLine = memo(function InputTrackLine({ row, seekAt }: { row: InputTrackRow; seekAt(clientX: number): void }) {
  const selected = useSelected(row.selectId, 'track');
  const value = useWorkbench(state => {
    const seconds = state.seconds;
    if (seconds === null) return '';
    const clip = row.track.clips.find(item => item.startSeconds <= seconds && seconds <= item.endSeconds);
    return clip ? [...new Set(clip.writes.map(write => write.keyId))].join(' / ') : '';
  });
  const { range } = useTimelineContext();
  return (
    <Line id={row.id} height={rowHeights.in} seekAt={seekAt} headClass={styles.trackHead} head={
      <button className={`${styles.headButton} ${styles.inputHead}`} data-selected={selected || undefined}
        style={{ ['--tc' as string]: trackColors.in }} onClick={() => selectionActions.select({ kind: 'track', id: row.selectId })}>
        <span className={styles.name}>{row.track.displayName}</span>
        <span className={styles.frameValue}>{value || '—'}</span>
      </button>
    }>
      {row.track.clips.filter(clip => clip.endSeconds >= range.start && clip.startSeconds <= range.end).map(clip => (
        <InputHold key={clip.id} trackId={row.track.id} clip={clip} />
      ))}
      {row.track.markers.filter(marker => marker.seconds >= range.start && marker.seconds <= range.end).map(marker => (
        <InputKey key={marker.id} trackId={row.track.id} id={marker.id} seconds={marker.seconds}
          title={`${marker.seconds.toFixed(3)} s\n${marker.writes.map(write => write.keyId).join(' / ')}`} />
      ))}
    </Line>
  );
});

type InputClip = FrozenInputTimeline['tracks'][number]['clips'][number];

const InputHold = memo(function InputHold({ trackId, clip }: { trackId: string; clip: InputClip }) {
  const { setNodeRef, itemStyle } = useItem({ id: `input/${trackId}/${clip.id}`, span: { start: clip.startSeconds, end: clip.endSeconds }, disabled: true });
  const active = useWorkbench(state => state.seconds !== null && clip.startSeconds <= state.seconds && state.seconds <= clip.endSeconds);
  const selectId = inputClipSelectId(trackId, clip.id);
  const selected = useSelected(selectId, 'clip');
  const keys = [...new Set(clip.writes.map(write => write.keyId))].join(' / ');
  return (
    <div ref={setNodeRef} style={itemStyle} className={styles.item}>
      <button className={styles.hold} data-active={active || undefined} data-selected={selected || undefined}
        title={`${clip.startSeconds.toFixed(3)} — ${clip.endSeconds.toFixed(3)} s\n${keys}`}
        onClick={() => selectionActions.select({ kind: 'clip', id: selectId })}>
        <i className={styles.holdBar} /><i className={styles.key} /><i className={styles.keyEnd} />
        <span>{keys}</span>
      </button>
    </div>
  );
});

function InputKey({ trackId, id, seconds, title }: { trackId: string; id: string; seconds: number; title: string }) {
  const { range, valueToPixels } = useTimelineContext();
  const active = useWorkbench(state => {
    const current = state.routeFrames[state.frameIndex]?.seconds;
    const previous = state.frameIndex > 0 ? state.routeFrames[state.frameIndex - 1].seconds : -Infinity;
    return current !== undefined && seconds > previous && seconds <= current;
  });
  const selectId = inputClipSelectId(trackId, id);
  return (
    <button className={styles.inputKey} data-active={active || undefined} title={title}
      style={{ left: valueToPixels(seconds - range.start) }}
      onClick={() => selectionActions.select({ kind: 'clip', id: selectId })} />
  );
}

// ── 游标覆盖线与结束线：直接改 transform，不经过 React ──

function Overlay({ bounds, follow, onRangeChanged }: {
  bounds: Range; follow: boolean; onRangeChanged(update: (previous: Range) => Range): void;
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
    tagElement.textContent = state.hostFrame ?? '';
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
  }, [range, valueToPixels, follow, onRangeChanged, visible, panelWindow, panelId]);

  useLayoutEffect(() => {
    apply();
    return useWorkbench.subscribe((state, previous) => {
      if (state.seconds !== previous.seconds || state.hostFrame !== previous.hostFrame || state.frameKey !== previous.frameKey) apply();
    });
  }, [apply]);

  useEffect(() => () => clearRendered(panelId), [panelId]);

  const endX = valueToPixels(bounds.end - range.start);
  return (
    <div className={styles.overlay} style={{ left: sidebarWidth }}>
      <div className={styles.endLine} style={{ transform: `translateX(${endX}px)` }} />
      <div ref={line} className={styles.cursorLine} hidden />
      <div ref={tag} className={styles.cursorTag} hidden />
    </div>
  );
}
