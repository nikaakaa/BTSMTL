import type { AuthorClip, AuthorTimeline, AuthorTrack, FrozenInputTimeline } from '../api/types';
import { inputRootKey, type RouteFrameSpan } from './store';

// Timeline 面板与检查器共用：把路线帧中的播放记录换算成运行秒数上的片段实例。
// 运行秒数 = 段起点（该段首帧 frame.seconds − cursorSeconds）+ 片段在 Timeline 内的秒数。

export type TrackKind = 'anim' | 'act' | 'in' | 'other';

// 片段类型只按正式合同判断，不按名称推断。
const clipKinds: Record<string, TrackKind> = {
  'animation.clip': 'anim',
  'motion-curve.clip': 'anim',
  'tree.clip': 'act',
};

export const trackColors: Record<TrackKind, string> = {
  anim: 'var(--track-anim)', act: 'var(--track-act)', in: 'var(--track-in)', other: 'var(--text-disabled)',
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

export interface ClipInstance {
  key: string;
  selectId: string;
  clip: AuthorClip;
  kind: TrackKind;
  start: number;
  end: number;
  // 片段在播放中断处被截断。
  cut: boolean;
}

export interface TrackRow {
  kind: 'track';
  id: string;
  selectId: string;
  timeline: AuthorTimeline;
  track: AuthorTrack;
  trackKind: TrackKind;
  clips: ClipInstance[];
  markers: { key: string; seconds: number; graphId: string }[];
}

export interface GroupRow {
  kind: 'group';
  id: string;
  documentId: string;
  title: string;
  segments: PlaySegment[];
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

export function playSegments(frames: RouteFrameSpan[]): Map<string, PlaySegment[]> {
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

export function timelineRows(frames: RouteFrameSpan[], timelines: AuthorTimeline[]): (GroupRow | TrackRow)[] {
  const byId = new Map(timelines.map(timeline => [timeline.documentId, timeline]));
  const rows: (GroupRow | TrackRow)[] = [];
  for (const [documentId, segments] of playSegments(frames)) {
    const timeline = byId.get(documentId);
    rows.push({ kind: 'group', id: `group:${documentId}`, documentId, title: timeline?.displayName ?? documentId.slice(0, 8), segments });
    if (!timeline) continue;
    for (const track of timeline.tracks) {
      const clips: ClipInstance[] = [];
      const markers: TrackRow['markers'] = [];
      for (const segment of segments) {
        for (const clip of track.clips) {
          const start = segment.origin + clip.startSeconds;
          if (start > segment.end) continue;
          const authoredEnd = segment.origin + clip.endSeconds;
          clips.push({
            key: `${documentId}/${clip.id}#${segment.index}`, selectId: clipSelectId(documentId, clip.id), clip,
            kind: clipKind(clip), start, end: Math.min(authoredEnd, segment.end), cut: authoredEnd > segment.end,
          });
        }
        for (const marker of track.markers) {
          const seconds = segment.origin + marker.seconds;
          if (seconds <= segment.end) markers.push({ key: `${marker.id}#${segment.index}`, seconds, graphId: marker.graphId });
        }
      }
      rows.push({
        kind: 'track', id: `track:${documentId}/${track.id}`, selectId: trackSelectId(documentId, track.id),
        timeline, track, trackKind: trackKind(track), clips, markers,
      });
    }
  }
  return rows;
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

export function findTimelineTrack(timelines: AuthorTimeline[], selectId: string) {
  for (const timeline of timelines)
    for (const track of timeline.tracks)
      if (trackSelectId(timeline.documentId, track.id) === selectId) return { timeline, track };
  return null;
}

// 帧序号：取秒数最接近的帧。
export function nearestFrame(frames: RouteFrameSpan[], seconds: number): number {
  let first = 0, last = frames.length - 1;
  while (first < last) {
    const middle = (first + last) >> 1;
    if (frames[middle].seconds < seconds) first = middle + 1;
    else last = middle;
  }
  return first > 0 && seconds - frames[first - 1].seconds < frames[first].seconds - seconds ? first - 1 : first;
}
