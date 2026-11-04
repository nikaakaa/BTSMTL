import type { PreviewAuthorProgram, TrackOverlap } from '../api/authoring';
import type { PreviewSampleSource } from '../api/playerRecords';
import type { AuthorClip, AuthorTimeline, AuthorTrack, ExecutionCall, FrozenInputTimeline, GraphDefinition } from '../api/types';
import { inputRootKey } from './store';

export type TimelineObservationBinding =
  | { kind: 'ready'; sceneRevision: string; programs: PreviewAuthorProgram[] }
  | { kind: 'unavailable'; reason: string };

export function timelineObservationNotice(binding: TimelineObservationBinding, source: PreviewSampleSource | null,
  documentId: string | null, call: ExecutionCall | null): string | null {
  if (binding.kind === 'unavailable') return binding.reason;
  if (source === null || source.content === null) return '当前帧未记录实际采用内容，Timeline 运行叠加不可用。';
  if (source.content.kind !== 'scene' || source.content.revision !== binding.sceneRevision)
    return '当前帧属于另一版预览场景，Timeline 运行叠加已过期；原档案保留。';
  if (documentId === null) return '请选择要观察的 Timeline。';
  if (call !== null && call.programIndex === null) return '当前调用未记录程序槽位，无法核对 Timeline 内容修订。';
  const units = binding.programs.filter(program => call === null ||
    (program.programId === call.programId && program.programRevision === call.programRevision && program.programIndex === call.programIndex))
    .flatMap(program => program.timelines.filter(timeline => timeline.documentId === documentId));
  if (units.length === 0) return '已采用的预览包未声明此调用的 Timeline 内容修订，运行叠加不可用。';
  const revisions = new Set(units.map(unit => JSON.stringify([unit.contentIdentity, unit.contentRevision])));
  return revisions.size === 1 ? null : '此 Timeline 对应多个内容修订，请选择实际调用后再观察。';
}

export type TrackKind = 'anim' | 'motion' | 'act' | 'in' | 'other';

// 轨道与片段类型只按正式合同判断，不按名称推断。
const trackKinds: Record<string, TrackKind> = {
  'animation.track': 'anim',
  'motion-curve.track': 'motion',
  'tree.track': 'act',
};
const clipKinds: Record<string, TrackKind> = {
  'animation.clip': 'anim',
  'motion-curve.clip': 'motion',
  'tree.clip': 'act',
};

export const trackColors: Record<TrackKind, string> = {
  anim: 'var(--track-anim)', motion: 'var(--track-motion)', act: 'var(--track-act)', in: 'var(--track-in)',
  other: 'var(--text-disabled)',
};

export interface TimelineSegment {
  id: string;
  documentId: string;
  origin: number;
  start: number;
  end: number;
  timeline: AuthorTimeline;
  length: number;
}

export interface ClipInstance {
  key: string;
  selectId: string;
  clip: AuthorClip;
  kind: TrackKind;
  label: string;
  start: number;
  end: number;
  // 段离开时刻；之后的部分压暗显示。
  exit: number;
}

export interface TrackRow {
  kind: 'track';
  id: string;
  selectId: string;
  segment: TimelineSegment;
  timeline: AuthorTimeline;
  track: AuthorTrack;
  trackKind: TrackKind;
  clips: ClipInstance[];
  // label：所触发节点图的名称，本次没有这张图时为 null。
  markers: { key: string; selectId: string; seconds: number; graphId: string; label: string | null }[];
  // Timeline 面板展开了这条轨道的片段曲线：片段下方参数区的高度。
  curves?: number;
}

export interface GroupRow {
  kind: 'group';
  id: string;
  segment: TimelineSegment;
  title: string;
  // 单个 Timeline 视图的 Timeline 分组：添加轨道在编辑行，分组头不再重复。
  focused?: boolean;
}

// 折叠的分组照 TimelineClipUnion 画各轨道片段的并集：同一轨道上相互重叠的片段合成一段（运行秒数）。
export function clipUnions(segment: TimelineSegment): Span[] {
  const result: Span[] = [];
  for (const track of segment.timeline.tracks) {
    let current: Span | null = null;
    for (const clip of [...track.clips].sort((left, right) => left.startSeconds - right.startSeconds)) {
      const start = segment.origin + clip.startSeconds, end = segment.origin + clip.endSeconds;
      if (current && start < current.end) current.end = Math.max(current.end, end);
      else result.push(current = { start, end });
    }
  }
  return result;
}

export const trackKind = (track: AuthorTrack): TrackKind => trackKinds[track.contract] ?? 'other';

export function clipKind(clip: AuthorClip): TrackKind {
  return clipKinds[clip.contract] ?? 'other';
}

export interface Span { start: number; end: number }

// 照 Clip.UpdateMix：与同轨片段部分重叠的一端混合，时长为重叠秒数；遇到盖住它或被它盖住的片段即停止，保留已算出的值。
export function clipMix(clip: Span, track: readonly Span[]): { in: number; out: number } {
  const mix = { in: 0, out: 0 };
  for (const other of track) {
    if (other === clip) continue;
    if ((other.start < clip.start && other.end > clip.end) || (other.start > clip.start && other.end < clip.end)) return mix;
    if (other.start < clip.start && other.end > clip.start) mix.in = other.end - clip.start;
    if (other.start > clip.start && other.start < clip.end) mix.out = clip.end - other.start;
    if (other.start === clip.start) {
      if (other.end < clip.end) mix.in = other.end - clip.start;
      else if (other.end > clip.end) mix.out = clip.end - clip.start;
    }
  }
  return mix;
}

// 同轨片段的重叠是否合规：Reject 轨道照 TimelineClipTimeEditBuffer 不许任何重叠；Blend 轨道只许两两部分重叠，
// 即 Clip.UpdateMix 能混合的形状，不许盖住整段、共用起点或终点，也不许三个片段叠在一处。合规时为 null。
export function overlapIssue(policy: TrackOverlap, spans: readonly Span[]): string | null {
  if (policy === 'Parallel') return null;
  for (let i = 0; i < spans.length; i++)
    for (let j = i + 1; j < spans.length; j++) {
      const a = spans[i], b = spans[j];
      if (!(a.end > b.start && b.end > a.start)) continue;
      if (policy === 'Reject') return '这条轨道不允许片段重叠';
      if ((a.start <= b.start && a.end >= b.end) || (b.start <= a.start && b.end >= a.end)) return '片段不能盖住同轨的另一片段';
    }
  if (policy === 'Blend')
    for (const at of spans)
      if (spans.filter(span => span.start <= at.start && at.start < span.end).length > 2) return '同一时刻至多两个片段混合';
  return null;
}

export const clipSelectId = (documentId: string, clipId: string) => `${documentId}/${clipId}`;
export const trackSelectId = (documentId: string, trackId: string) => `${documentId}/${trackId}`;
export const inputClipSelectId = (trackId: string, clipId: string) => `input/${trackId}/${clipId}`;
export const inputTrackSelectId = (trackId: string) => `input/${trackId}`;

// 不经运行打开技能时，每条 Timeline 自成一段，本地零点即运行零点。
export const documentSegments = (timelines: AuthorTimeline[]): TimelineSegment[] => timelines.map(timeline => {
  const length = timelineLength(timeline);
  return {
    id: `document:${timeline.documentId}`, documentId: timeline.documentId,
    origin: 0, start: 0, end: length, timeline, length,
  };
});

export function timelineLength(timeline: AuthorTimeline): number {
  let length = 0;
  for (const track of timeline.tracks) {
    for (const clip of track.clips) length = Math.max(length, clip.endSeconds);
    for (const marker of track.markers) length = Math.max(length, marker.seconds);
  }
  return length;
}

export const segmentTitle = (segment: TimelineSegment) =>
  `${segment.timeline.displayName ?? segment.documentId.slice(0, 8)} · ${segment.start.toFixed(2)}–${segment.end.toFixed(2)} s`;

// 片段引用的节点图（graphId 形如 `类别:图身份`）；本次运行没有这张图时为 null。
export function clipGraph(graphs: GraphDefinition[] | null, graphId: string | null): GraphDefinition | null {
  if (!graphId) return null;
  const id = graphId.slice(graphId.indexOf(':') + 1);
  return graphs?.find(graph => graph.id === id) ?? null;
}

// 片段显示名：作者名，其次所调用图的名称，最后轨道名。
export function clipLabel(clip: AuthorClip, track: AuthorTrack, graphNames: Map<string, string>): string {
  if (clip.displayName) return clip.displayName;
  const graphId = clip.graphId?.slice(clip.graphId.indexOf(':') + 1);
  return (graphId && graphNames.get(graphId)) || track.displayName;
}

export function segmentRows(segment: TimelineSegment, graphNames: Map<string, string>): TrackRow[] {
  const timeline = segment.timeline;
  return timeline.tracks.map(track => ({
    kind: 'track', id: `track:${segment.id}/${track.id}`, selectId: trackSelectId(timeline.documentId, track.id),
    segment, timeline, track, trackKind: trackKind(track),
    clips: track.clips.map(clip => ({
      key: `${segment.id}/${clip.id}`, selectId: clipSelectId(timeline.documentId, clip.id), clip,
      kind: clipKind(clip), label: clipLabel(clip, track, graphNames),
      start: segment.origin + clip.startSeconds, end: segment.origin + clip.endSeconds, exit: segment.end,
    })),
    markers: track.markers.map(marker => ({
      key: `${segment.id}/${marker.id}`, selectId: clipSelectId(timeline.documentId, marker.id), seconds: segment.origin + marker.seconds,
      graphId: marker.graphId, label: graphNames.get(marker.graphId.slice(marker.graphId.indexOf(':') + 1)) ?? null,
    })),
  }));
}

export function selectedInputRoot(roots: FrozenInputTimeline[] | undefined, key: string | null) {
  return roots?.find(root => inputRootKey(root) === key) ?? null;
}

// 检查器按选中身份查找片段与轨道。
export function findTimelineClip(timelines: AuthorTimeline[], selectId: string) {
  for (const timeline of timelines)
    for (const track of timeline.tracks)
      for (const clip of track.clips)
        if (clipSelectId(timeline.documentId, clip.id) === selectId) return { timeline, track, clip };
  return null;
}

export function findTimelineMarker(timelines: AuthorTimeline[], selectId: string) {
  for (const timeline of timelines)
    for (const track of timeline.tracks)
      for (const marker of track.markers)
        if (clipSelectId(timeline.documentId, marker.id) === selectId) return { timeline, track, marker };
  return null;
}

export function findTimelineTrack(timelines: AuthorTimeline[], selectId: string) {
  for (const timeline of timelines)
    for (const track of timeline.tracks)
      if (trackSelectId(timeline.documentId, track.id) === selectId) return { timeline, track };
  return null;
}
