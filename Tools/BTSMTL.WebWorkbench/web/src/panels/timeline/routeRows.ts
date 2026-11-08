// 快档：输入方案时间即全局时间，Host 算出的每次播放按“全局＝起点＋本地－本地起点”摆出所播 Timeline 的轨道，可直接编辑。

import type { PreviewQuickPlan } from '../../api/previewQuickRecords';
import type { AuthorTimeline } from '../../api/types';
import type { TimelineSegment } from '../../state/timelineModel';

export function quickPlayedSegments(plan: PreviewQuickPlan, timelines: AuthorTimeline[], inputTimeline: string): TimelineSegment[] {
  const byId = new Map(timelines.map(timeline => [timeline.documentId, timeline]));
  const segments: TimelineSegment[] = [];
  plan.segments.forEach((segment, index) => {
    const timeline = segment.timelineId === null ? undefined : byId.get(segment.timelineId);
    if (timeline && timeline.documentId !== inputTimeline)
      segments.push({ id: `route:${index}`, documentId: timeline.documentId, origin: segment.start - segment.localStart,
        start: segment.start, end: segment.end, timeline, length: segment.localEnd - segment.localStart });
  });
  return segments;
}
