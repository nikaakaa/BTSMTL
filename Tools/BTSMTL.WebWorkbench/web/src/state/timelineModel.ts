import type { AuthorClip, AuthorTimeline, AuthorTrack, FrozenInputTimeline, GraphDefinition, RouteFrame } from '../api/types';
import { inputRootKey } from './store';

// Timeline 面板与检查器共用：把路线帧中的播放记录换算成运行秒数上的片段实例。
// 运行秒数 = 段起点（该段首帧 frame.seconds − cursorSeconds）+ 片段在 Timeline 内的秒数。

export type TrackKind = 'anim' | 'motion' | 'act' | 'in' | 'other';

// 片段类型只按正式合同判断，不按名称推断。
const clipKinds: Record<string, TrackKind> = {
  'animation.clip': 'anim',
  'motion-curve.clip': 'motion',
  'tree.clip': 'act',
};

export const trackColors: Record<TrackKind, string> = {
  anim: 'var(--track-anim)', motion: 'var(--track-motion)', act: 'var(--track-act)', in: 'var(--track-in)',
  other: 'var(--text-disabled)',
};

export interface PlaySegment {
  documentId: string;
  index: number;
  firstIndex: number;
  lastIndex: number;
  origin: number;
  start: number;
  end: number;
}

// 一段路线：某个状态进入后播放一条 Timeline，到离开为止。
export interface RouteSegment extends PlaySegment {
  id: string;
  timeline: AuthorTimeline | null;
  // 段首帧进入的状态机状态；没有状态机记录时为 null。
  state: { graphId: string; nodeId: string; name: string } | null;
  // 作者 Timeline 的长度（最后一个片段或标记的结束秒数）。
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
  segment: RouteSegment;
  timeline: AuthorTimeline;
  track: AuthorTrack;
  trackKind: TrackKind;
  clips: ClipInstance[];
  markers: { key: string; selectId: string; seconds: number; graphId: string }[];
}

export interface GroupRow {
  kind: 'group';
  id: string;
  segment: RouteSegment;
  title: string;
}

export function trackKind(track: AuthorTrack): TrackKind {
  for (const clip of track.clips) {
    const kind = clipKinds[clip.contract];
    if (kind) return kind;
  }
  return 'other';
}

export function clipKind(clip: AuthorClip): TrackKind {
  return clipKinds[clip.contract] ?? 'other';
}

export function playSegments(frames: RouteFrame[]): Map<string, PlaySegment[]> {
  const result = new Map<string, PlaySegment[]>();
  frames.forEach((frame, index) => {
    for (const playback of frame.playbacks) {
      let segments = result.get(playback.documentId);
      if (!segments) result.set(playback.documentId, segments = []);
      const previous = segments.at(-1);
      if (previous && previous.lastIndex === index - 1) {
        previous.lastIndex = index;
        previous.end = frame.seconds;
      } else {
        segments.push({
          documentId: playback.documentId, index: segments.length, firstIndex: index, lastIndex: index,
          origin: frame.seconds - playback.cursorSeconds, start: frame.seconds, end: frame.seconds,
        });
      }
    }
  });
  return result;
}

export const clipSelectId = (documentId: string, clipId: string) => `${documentId}/${clipId}`;
export const trackSelectId = (documentId: string, trackId: string) => `${documentId}/${trackId}`;
export const inputClipSelectId = (trackId: string, clipId: string) => `input/${trackId}/${clipId}`;
export const inputTrackSelectId = (trackId: string) => `input/${trackId}`;

// 不经运行打开技能时，每条 Timeline 自成一段，本地零点即运行零点。
export const documentSegments = (timelines: AuthorTimeline[]): RouteSegment[] => timelines.map(timeline => {
  const length = timelineLength(timeline);
  return {
    id: `document:${timeline.documentId}`, documentId: timeline.documentId, index: 0, firstIndex: 0, lastIndex: 0,
    origin: 0, start: 0, end: length, timeline, state: null, length,
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

// 状态机：带转移连线的图。
export const stateMachineIds = (graphs: GraphDefinition[] | null) =>
  new Set((graphs ?? []).filter(graph => graph.edges.some(edge => edge.kind === 'Transition')).map(graph => graph.id));

// 按播放先后排列的路线段；段名取段首帧进入的状态机状态。
export function routeSegments(frames: RouteFrame[], timelines: AuthorTimeline[], graphs: GraphDefinition[] | null): RouteSegment[] {
  const byId = new Map(timelines.map(timeline => [timeline.documentId, timeline]));
  const machines = stateMachineIds(graphs);
  const names = new Map<string, string>();
  for (const graph of graphs ?? [])
    if (machines.has(graph.id))
      for (const node of graph.nodes) names.set(`${graph.id}/${node.id}`, node.displayName ?? node.id.slice(0, 8));
  const result: RouteSegment[] = [];
  for (const [documentId, segments] of playSegments(frames)) {
    const timeline = byId.get(documentId) ?? null;
    for (const segment of segments) {
      const enteredNode = frames[segment.firstIndex].nodes.find(node => node.kind === 'Enter' && names.has(`${node.graphId}/${node.nodeId}`));
      const entered = enteredNode ? `${enteredNode.graphId}/${enteredNode.nodeId}` : null;
      const split = entered?.indexOf('/') ?? -1;
      result.push({
        ...segment, id: `${documentId}#${segment.index}`, timeline,
        state: entered ? { graphId: entered.slice(0, split), nodeId: entered.slice(split + 1), name: names.get(entered)! } : null,
        length: timeline ? timelineLength(timeline) : segment.end - segment.origin,
      });
    }
  }
  return result.sort((left, right) => left.start - right.start);
}

export const segmentTitle = (segment: RouteSegment) =>
  `${segment.timeline?.displayName ?? segment.documentId.slice(0, 8)}${segment.state ? ` · ${segment.state.name}` : ''} · ${segment.start.toFixed(2)}–${segment.end.toFixed(2)} s`;

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

export function segmentRows(segment: RouteSegment, graphNames: Map<string, string>): TrackRow[] {
  const timeline = segment.timeline;
  if (!timeline) return [];
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
      graphId: marker.graphId,
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
