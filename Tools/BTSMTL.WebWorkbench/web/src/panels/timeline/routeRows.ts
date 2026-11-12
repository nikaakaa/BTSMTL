// 路线视图：快速推演与真实运行档案都交出按执行实例分道的路线，时间轴即路线时间；
// 每次播放按“全局＝起点＋本地－本地起点”摆出所播 Timeline 的轨道，可直接编辑。
// 每个实例一层分组，组内是它所播的 Timeline。

import type { RouteInstance } from '../../api/previewQuickRecords';
import type { AuthorTimeline } from '../../api/types';
import type { TimelineSegment } from '../../state/timelineModel';

export interface RouteSource {
  instances: RouteInstance[];
  segments: { instance: number; timelineId: string | null; documentPath?: string | null;
    start: number; end: number; localStart: number; localEnd: number }[];
}

// 同一载体有多个实例（如连射的多颗子弹）时附实例编号区分。
export function routeCarrierLabel(instance: RouteInstance, instances: RouteInstance[]): string {
  const shared = instances.filter(item => item.carrier === instance.carrier).length > 1;
  return `${instance.carrier}${shared ? ` #${instance.id}` : ''}`;
}

export const routeEnd = (route: RouteSource) =>
  Math.max(0, ...route.instances.map(instance => instance.end), ...route.segments.map(segment => segment.end));

export interface RouteLane { instance: RouteInstance; label: string; segments: TimelineSegment[] }

// rootName：把实例的根身份换成可读名（运行档案只记根程序身份）。
export function routeLanes(route: RouteSource, timelines: AuthorTimeline[], inputTimeline: string | null,
  rootName: (root: string) => string = root => root): RouteLane[] {
  const byId = new Map(timelines.map(timeline => [timeline.documentId, timeline]));
  const lanes = new Map(route.instances.map(instance => [instance.id, {
    instance, label: `${routeCarrierLabel(instance, route.instances)} · ${rootName(instance.root)}`, segments: [] as TimelineSegment[],
  }]));
  route.segments.forEach((segment, index) => {
    const timeline = segment.timelineId === null ? undefined : byId.get(segment.timelineId);
    const lane = lanes.get(segment.instance);
    if (lane === undefined) throw new Error(`路线第 ${index} 段属于未声明的实例 ${segment.instance}`);
    if (timeline && timeline.documentId !== inputTimeline)
      lane.segments.push({ id: `route:${index}`, documentId: timeline.documentId, quickIndex: index,
        owner: segment.documentPath ?? undefined, origin: segment.start - segment.localStart,
        start: segment.start, end: segment.end, timeline, length: segment.localEnd - segment.localStart });
  });
  return [...lanes.values()];
}
