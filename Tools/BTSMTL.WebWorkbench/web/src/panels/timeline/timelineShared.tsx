// Timeline 面板各部分共用的尺寸、行类型、吸附与时钟上下文及播放位置换算。

import type { Range } from 'dnd-timeline';
import { createContext, useCallback } from 'react';
import { create } from 'zustand';
import type { SkillElements } from '../../api/authoring';
import type { ExecutionCall, Frame, FrozenInputTimeline } from '../../api/types';
import type { PreviewQuickPlan } from '../../api/previewQuickRecords';
import { samePreviewLoad, type PlayerDataCurve, type PlayerJobId, type PreviewSampleSource } from '../../api/playerRecords';
import { useAuthoring } from '../../state/authoring';
import { clipSnapSource } from '../../state/authoringTimeline';
import { consoleActions, selectionActions, useWorkbench, type GraphNavigationOrigin, type WorkbenchState } from '../../state/store';
import type { GroupRow, TimelineSegment, TrackRow } from '../../state/timelineModel';
import { callKey } from '../../state/graphModel';
import { transport } from '../../state/transport';
import type { ReceivedInput, ReceivedSpan } from '../../state/inputReceipts';
import type { RouteBar, RouteView } from './routeRows';
import { cachedFrame } from '../../state/frames';
import { useAnimationFrameRates } from '../../state/resources';
export const sidebarFor = (width: number) => Math.round(Math.max(150, Math.min(380, width * 0.3)));
export const minSpan = 0.05;
// 照 Unity Timeline：轨道与分组默认高 30，行间隔 3；数据曲线行自定 64。
export const trackHeight = 30;
export const curveHeight = 64;
// 轨道展开片段曲线时片段下方的参数区（照 BtsmtlTimelineTrackBinding.GetFinalHeight 与 TrackEditorGUI）：
// 参数行高 18、间距 2、顶部留 5；查看某个参数时加控制行与完整曲线，可拖动底边加高。
export const curveParamTop = 5;
export const curveParamPitch = 20;
export const curveParamHeight = 18;
export const curveListHeight = (count: number) => Math.max(50, curveParamTop * 2 + count * curveParamPitch);
export const curveInspectHeight = (count: number) => curveParamTop * 2 + count * curveParamPitch + 30 + 120;
export const curveMaxHeight = 600;
export const rowSpacing = 3;
// 行列表顶部内边距（.rows 的 padding-top）。
export const rowsPadding = 3;
export const overscan = 120;
// 片段名左右各留 6，剩余宽度不超过 10 时不画名字（ClipDrawer）。
export const clipLabelPadding = 6;
export const clipLabelMinWidth = 10;

export interface InputGroupRow { kind: 'inputGroup'; id: string }
export interface InputTrackRow { kind: 'inputTrack'; id: string; selectId: string; track: FrozenInputTimeline['tracks'][number] }
export interface CurveGroupRow { kind: 'curves'; id: string }
export interface CurveRow { kind: 'curve'; id: string; curve: PlayerDataCurve }
export interface ReceivedGroupRow { kind: 'receivedGroup'; id: string; received: ReceivedInput }
export interface ReceivedRow { kind: 'received'; id: string; name: string; spans: ReceivedSpan[]; requests: ReceivedSpan[]; missing: boolean }
export interface RouteGroupRow { kind: 'routeGroup'; id: string; route: RouteView }
export interface RouteLaneRow { kind: 'routeLane'; id: string; name: string; bars: RouteBar[]; hint: boolean }
export type Row = GroupRow | TrackRow | InputGroupRow | InputTrackRow | CurveGroupRow | CurveRow | ReceivedGroupRow | ReceivedRow
  | RouteGroupRow | RouteLaneRow;

export interface Params {
  range: Range | null;
  collapsed: string[];
  // 展开片段曲线的轨道行；正在查看完整曲线的参数（同一时间一个，照 Slate inspectedParameterId）；查看时拖动加高的参数区高度。
  curveTracks: string[];
  curveParam: { track: string; parameter: string } | null;
  curveHeights: Record<string, number>;
  follow: boolean;
  filter: string;
  // 单个 Timeline 的 documentId；* 显示全部。
  focus: string | null;
  // 编辑吸附（照 Timeline Editor 时间网格）：逻辑 tick、秒间隔或关闭。
  snap: Snap;
  call: string | null;
  pin: { runId: PlayerJobId; documentId: string; observation: TimelineObservation; recordIndex?: number } | null;
  caller: { runId: PlayerJobId; call: ExecutionCall } | null;
  origin: GraphNavigationOrigin | null;
}
export type Snap = 'tick' | 'frame' | '0.1' | '0.01' | '0.001' | 'none';
export const defaults: Params = {
  range: null, collapsed: [], curveTracks: [], curveParam: null, curveHeights: {}, follow: true, filter: '', focus: null, snap: 'frame', call: null, pin: null, caller: null, origin: null,
};

// 标记与游标的吸附步长（秒）；素材帧只属于片段，标记按逻辑 tick；关闭吸附时为定点时间的最小单位。没有逻辑 tick 时不能编辑。
export const snapStep = (snap: Snap | undefined, frameStep: number | null) =>
  snap === 'none' ? 1 / 65536 : snap && snap !== 'tick' && snap !== 'frame' ? Number(snap) : frameStep;
export const SnapContext = createContext<number | null>(null);
export const minimumStep = 1 / 65536;

// 片段吸附网格（照 TimelineEditorSessionContext.SnapTime）：网格点为 offset + k·step，null 为不吸附。
// 起止按逻辑 tick 量化的片段（TickQuantized）始终按 tick；其余片段按所选网格，素材帧取片段所引用动画资源的帧率，
// 偏移为起点减 ClipIn，使网格对齐素材帧；没有素材帧的片段在素材帧模式下不吸附。
export interface Grid { step: number; offset: number }
export type ClipGridOf = (documentId: string, clipId: string) => Grid | null;
export const ClipGridContext = createContext<ClipGridOf>(() => null);
export const snapGrid = (seconds: number, grid: Grid | null) =>
  grid ? grid.offset + Math.round((seconds - grid.offset) / grid.step) * grid.step : seconds;

export function useClipGrid(snap: Snap, tick: number | null, elements: SkillElements | null): ClipGridOf {
  const enabled = useAuthoring(state => state.author && state.target !== null);
  const rates = useAnimationFrameRates(enabled && snap === 'frame');
  return useCallback((documentId: string, clipId: string): Grid | null => {
    const source = enabled && elements && clipSnapSource(elements, documentId, clipId);
    if (!source) return null;
    const tickGrid = tick === null ? null : { step: tick, offset: 0 };
    if (source.template.tickQuantized || snap === 'tick') return tickGrid;
    if (snap === 'none') return null;
    if (snap !== 'frame') return { step: Number(snap), offset: 0 };
    const rate = source.template.clipInable ? source.references.map(reference => rates?.get(reference)).find(value => value) : undefined;
    return rate ? { step: 1 / rate, offset: source.start - source.clipIn } : null;
  }, [enabled, elements, rates, snap, tick]);
}
export type TimelineObservation = { call: ExecutionCall; domain: string };
export const observationKey = (observation: TimelineObservation) => JSON.stringify([callKey(observation.call), observation.domain]);
export type QuickSegment = PreviewQuickPlan['segments'][number];
export type QuickPlaybackChoice = { value: string; options: { value: string; label: string }[]; select(value: string): void };

export type PreviewClock = { kind: 'document'; documentId: string | null; observation: TimelineObservation | null }
  | { kind: 'archive'; documentId: string | null; observation: TimelineObservation | null }
  | { kind: 'input'; documentId: string; source: 'archive' | 'author' | 'unavailable' }
  | { kind: 'quick'; documentId: string | null; source: { kind: 'input' } | { kind: 'timeline'; segment: QuickSegment | null } };
export const PreviewClockContext = createContext<PreviewClock>({ kind: 'document', documentId: null, observation: null });
export type CurveClock = { kind: 'ready'; source: PreviewSampleSource; seconds: number }
  | { kind: 'unavailable'; reason: string };
export const CurveClockContext = createContext<CurveClock>({ kind: 'unavailable', reason: '没有当前帧的来源时钟' });
export function documentPlayback(state: WorkbenchState, clock: PreviewClock) {
  if ((clock.kind !== 'document' && clock.kind !== 'archive') || clock.observation === null ||
    state.frame?.hostFrame !== state.hostFrame) return undefined;
  return playbackFor(state.frame, clock.documentId, clock.observation);
}

export function playbackFor(frame: Frame | null | undefined, documentId: string | null, observation: TimelineObservation) {
  return frame?.route?.playbacks.find(playback => playback.documentId === documentId &&
    playback.domain === observation.domain && playback.call !== null && callKey(playback.call) === callKey(observation.call));
}

export function recordedLocalPosition(state: WorkbenchState, seconds: number,
  cursorAt: (frame: Frame, index: number) => number | null, anchorIndex = state.frameIndex): ReturnType<typeof transport.recordPosition> {
  const { runId, summary } = state;
  if (runId === null || summary === null) return { kind: 'unavailable', reason: '尚未打开运行档案' };
  const current = cachedFrame(runId, anchorIndex);
  const cursor = current === undefined ? null : cursorAt(current, anchorIndex);
  if (current === undefined || cursor === null)
    return { kind: 'unavailable', reason: '当前录制帧没有所选播放或输入的实际本地时间记录' };
  const source = current.source;
  if (source === null || source.content === null)
    return { kind: 'unavailable', reason: '当前记录缺少实际采用内容身份，不能按本地时间定位' };
  const anchor = { index: anchorIndex, cursor, source };
  const samples = [anchor];
  for (const direction of [-1, 1]) {
    let previous = anchor.cursor;
    for (let index = anchorIndex + direction; index >= 0 && index < summary.frameCount; index += direction) {
      const frame = cachedFrame(runId, index);
      const local = frame === undefined ? null : cursorAt(frame, index);
      if (frame === undefined || local === null || frame.source === null || !samePreviewLoad(source, frame.source)) break;
      if (direction < 0 ? local > previous : local < previous) break;
      samples.push({ index, cursor: local, source: frame.source });
      previous = local;
    }
  }
  const start = Math.min(...samples.map(sample => sample.cursor)), end = Math.max(...samples.map(sample => sample.cursor));
  if (seconds < start || seconds > end)
    return { kind: 'unavailable', reason: `当前所选观察的已读记录只覆盖本地 ${timeText(start)}—${timeText(end)}；请先按录制帧定位，不能外推或跨圈定位` };
  const nearest = samples.reduce((chosen, sample) => {
    const distance = Math.abs(sample.cursor - seconds), previousDistance = Math.abs(chosen.cursor - seconds);
    return distance < previousDistance || distance === previousDistance && sample.index < chosen.index ? sample : chosen;
  }, anchor);
  const position = transport.recordPosition(runId, nearest.index, nearest.source);
  if (position.kind === 'record' && samples.some(sample => sample.index !== nearest.index && sample.cursor === nearest.cursor))
    consoleActions.log('info', 'Timeline', `本地游标在 ${timeText(nearest.cursor)} 秒未前进，已定位到该时刻的首个录制帧 ${position.index}`);
  return position;
}
// 技能文档没有运行观察时的编辑游标（Timeline 本地秒）：照 Timeline Editor 可拖动定位，换 Timeline 回到 0。
export const useAuthorCursor = create<{ documentId: string | null; seconds: number }>(() => ({ documentId: null, seconds: 0 }));
export const authorCursorOf = (cursor: { documentId: string | null; seconds: number }, clock: PreviewClock) =>
  ((clock.kind === 'document' && clock.observation === null) || (clock.kind === 'input' && clock.source === 'author')) && clock.documentId !== null
    ? cursor.documentId === clock.documentId ? cursor.seconds : 0 : null;
export const quickLocalSeconds = (segment: QuickSegment, seconds: number) => seconds < segment.start || seconds > segment.end ? null
  : seconds === segment.end ? segment.localEnd : segment.localStart + seconds - segment.start;
export function previewSeconds(state: WorkbenchState, clock: PreviewClock, author: number | null): number | null {
  if (clock.kind === 'quick') return clock.source.kind === 'input' ? state.quickPreview.seconds
    : clock.source.segment === null ? null : quickLocalSeconds(clock.source.segment, state.quickPreview.seconds);
  if (clock.kind === 'input') return clock.source === 'archive' && state.archiveFrame?.hostFrame === state.hostFrame
    ? state.archiveFrame?.input?.inputTimeSeconds ?? null : author;
  return author ?? documentPlayback(state, clock)?.cursorSeconds ?? null;
}
// 游标秒数：运行档案、运行观察或编辑游标。
export function usePreviewSelect<T>(clock: PreviewClock, pick: (seconds: number | null, state: WorkbenchState) => T): T {
  const author = useAuthorCursor(cursor => authorCursorOf(cursor, clock));
  return useWorkbench(state => pick(previewSeconds(state, clock, author), state));
}
export const selectTimelineObject = (clock: PreviewClock, kind: 'clip' | 'track', id: string) =>
  selectionActions.select({ kind, id, call: clock.kind === 'document' ? clock.observation?.call : undefined });
export const snapTo = (seconds: number, step: number) => Math.round(seconds / step) * step;

export const segmentRange = (segment: TimelineSegment) => fitRange({ start: segment.origin, end: segment.origin + segment.length });

export const rowHeight = (row: Row) =>
  (row.kind === 'curve' ? curveHeight : row.kind === 'track' && row.curves ? trackHeight + row.curves : trackHeight) + rowSpacing;

// 曲线参数区的查看状态、所查看曲线上选中的关键帧序号与游标跳转（轨道行是 memo 组件，经上下文取）。
// seek 不按吸附取整，跳到关键帧本身。
export interface CurveView {
  inspected: Params['curveParam'];
  inspect(track: string, parameter: string | null): void;
  resize(track: string, height: number): void;
  seek(value: number): void;
  keys: number[];
  selectKeys(keys: number[]): void;
}
export const CurveViewContext = createContext<CurveView>({
  inspected: null, inspect: () => {}, resize: () => {}, seek: () => {}, keys: [], selectKeys: () => {},
});

// 分组之后的行都属于最近的分组：照 Unity 分组轨道再缩进一级，由分组底色包住，最后一行收圆角。
// 分组行本身按有无子行区分只画上圆角还是整块圆角。
export type Nest = 'head' | 'single' | 'child' | 'last';
export const groupKinds: ReadonlySet<Row['kind']> = new Set(['group', 'inputGroup', 'curves', 'receivedGroup', 'routeGroup']);
export function rowNesting(rows: Row[]): (Nest | undefined)[] {
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

export function fitRange(bounds: Range): Range {
  const pad = Math.max(minSpan, (bounds.end - bounds.start) * 0.03);
  return { start: bounds.start - pad, end: bounds.end + pad };
}

export const timeText = (seconds: number) => String(Math.round(seconds * 1e4) / 1e4);

