// 快档：输入方案时间即全局时间，Host 算出的每次播放按“全局＝起点＋本地－本地起点”摆出所播 Timeline 的轨道，可直接编辑。
// 路线按执行实例分道：每个实例一条泳道，其下是它所播的 Timeline 分组，分组标注所属实例。

import type { PreviewQuickInstance, PreviewQuickPlan } from '../../api/previewQuickRecords';
import type { AuthorTimeline } from '../../api/types';
import type { TimelineSegment } from '../../state/timelineModel';

// 同一载体有多个实例（如连射的多颗子弹）时附实例编号区分。
export function quickInstanceLabel(instance: PreviewQuickInstance, instances: PreviewQuickInstance[]): string {
  const shared = instances.filter(item => item.carrier === instance.carrier).length > 1;
  return `${instance.carrier}${shared ? ` #${instance.id}` : ''} · ${instance.root}`;
}

export interface QuickLane { instance: PreviewQuickInstance; label: string; parent: string | null; segments: TimelineSegment[] }

export function quickPlayedLanes(plan: PreviewQuickPlan, timelines: AuthorTimeline[], inputTimeline: string | null): QuickLane[] {
  const byId = new Map(timelines.map(timeline => [timeline.documentId, timeline]));
  const labels = new Map(plan.instances.map(instance => [instance.id, quickInstanceLabel(instance, plan.instances)]));
  const lanes = new Map(plan.instances.map(instance => [instance.id, {
    instance, label: labels.get(instance.id)!, parent: instance.parent === null ? null : labels.get(instance.parent) ?? null,
    segments: [] as TimelineSegment[],
  }]));
  plan.segments.forEach((segment, index) => {
    const timeline = segment.timelineId === null ? undefined : byId.get(segment.timelineId);
    const lane = lanes.get(segment.instance);
    if (lane === undefined) throw new Error(`快速路线第 ${index} 段属于未声明的实例 ${segment.instance}`);
    if (timeline && timeline.documentId !== inputTimeline)
      lane.segments.push({ id: `route:${index}`, documentId: timeline.documentId, quickIndex: index,
        owner: segment.documentPath ?? undefined, origin: segment.start - segment.localStart,
        start: segment.start, end: segment.end, timeline, length: segment.localEnd - segment.localStart,
        instance: { id: segment.instance, label: lane.label } });
  });
  return [...lanes.values()];
}
