// 路线视图的动态轨道：输入方案时间即路线全局时间。快档按推导路线、真档按已读档案帧的实际播放记录，
// 拼成路线条（重叠的播放分到多条）；快档的每次播放再按“全局＝起点＋本地－本地起点”摆出所播 Timeline 的轨道，可直接编辑。

import type { PreviewQuickLocation, PreviewQuickPlan } from '../../api/previewQuickRecords';
import type { AuthorTimeline, RouteFrame } from '../../api/types';
import { callKey, callLabel } from '../../state/graphModel';
import type { TimelineSegment } from '../../state/timelineModel';
import { timeText, type TimelineObservation } from './timelineShared';

export interface RouteBar {
  key: string;
  start: number;
  end: number;
  label: string;
  tip: string;
  documentId: string | null;
  // 单击定位：快档为路线秒数，真档为该次播放首个已读录制帧。
  target: { kind: 'quick'; seconds: number } | { kind: 'record'; index: number };
  source: PreviewQuickLocation | null;
  // 双击进入所播 Timeline 时选中的推导播放或实际调用。
  quickIndex: number | null;
  observation: TimelineObservation | null;
  hint: boolean;
}

export interface RouteView {
  kind: 'quick' | 'real';
  title: string;
  meta: string;
  tip: string;
  notice: string | null;
  lanes: RouteBar[][];
  hints: RouteBar[];
  segments: TimelineSegment[];
}

function pack(bars: RouteBar[]): RouteBar[][] {
  const lanes: RouteBar[][] = [];
  for (const bar of [...bars].sort((left, right) => left.start - right.start || left.end - right.end)) {
    const lane = lanes.find(items => items[items.length - 1].end <= bar.start + 1e-9);
    if (lane) lane.push(bar);
    else lanes.push([bar]);
  }
  return lanes;
}

const range = (start: number, end: number) => `${timeText(start)}—${timeText(end)} s`;

export function quickRoute(plan: PreviewQuickPlan, timelines: AuthorTimeline[], inputTimeline: string, stale: boolean): RouteView {
  const byId = new Map(timelines.map(timeline => [timeline.documentId, timeline]));
  const segments: TimelineSegment[] = [];
  const bars = plan.segments.map((segment, index): RouteBar => {
    const timeline = segment.timelineId === null ? undefined : byId.get(segment.timelineId);
    const name = segment.timeline ?? timeline?.displayName ?? segment.timelineId?.slice(0, 8) ?? '无 Timeline';
    if (timeline && timeline.documentId !== inputTimeline)
      segments.push({ id: `route:${index}`, documentId: timeline.documentId, origin: segment.start - segment.localStart,
        start: segment.start, end: segment.end, timeline, length: segment.localEnd - segment.localStart });
    return {
      key: `quick:${index}`, start: segment.start, end: segment.end, label: segment.state === name ? name : `${segment.state} · ${name}`,
      tip: `${segment.state} · ${name}\n路线 ${range(segment.start, segment.end)}\n本地 ${range(segment.localStart, segment.localEnd)}\n` +
        `播放 ${segment.playback} · 循环 ${segment.cycle} · 出现 ${segment.occurrence}${segment.playbackCompleted ? ' · 播放完成' : ''}` +
        `${segment.callerClipId ? `\n调用方播放 ${segment.callerPlayback} · Clip ${segment.callerClipId}` : ''}\n单击定位 · 双击进入 Timeline`,
      documentId: timeline ? timeline.documentId : null, target: { kind: 'quick', seconds: segment.start },
      source: segment.stateSource, quickIndex: index, observation: null, hint: false,
    };
  });
  const hints = plan.suggestions.map((suggestion, index): RouteBar => ({
    key: `hint:${index}`, start: suggestion.start, end: Math.max(suggestion.end, suggestion.start), hint: true,
    label: `${suggestion.fromState} → ${suggestion.toState}`,
    tip: `${suggestion.transition}：${suggestion.fromState} → ${suggestion.toState}\n${range(suggestion.start, suggestion.end)}\n` +
      `缺少条件：${suggestion.missing.map(condition => condition.label).join('、') || '无'}\n单击定位并打开该转移`,
    documentId: null, target: { kind: 'quick', seconds: suggestion.start }, source: suggestion.transitionSource,
    quickIndex: null, observation: null,
  }));
  return {
    kind: 'quick', title: '推导路线', meta: `${plan.status} · ${timeText(plan.duration)} ${plan.clock.unit}`,
    tip: ['按正式内容与输入方案静态推导；实际数值与命中以真档为准', plan.stopReason, plan.stopDetail, ...plan.assumptions].filter(Boolean).join('\n'),
    notice: stale ? '内容或输入已变化，正在按当前内容重推；新路线采用前保留上一份' : bars.length === 0 ? '路线没有播放任何 Timeline' : null,
    lanes: pack(bars), hints, segments,
  };
}

interface InputSample { frame: string; index: number; seconds: number | null }

// 真档：已读帧按宿主帧换成输入时间，同一 Timeline、执行域与完整调用身份的连续帧合成一次播放；未读的帧不外推。
export function realRoute(frames: RouteFrame[], samples: InputSample[], timelines: AuthorTimeline[], step: number): RouteView {
  const times = new Map(samples.flatMap(sample => sample.seconds === null ? [] : [[sample.frame, sample] as const]));
  const names = new Map(timelines.map(timeline => [timeline.documentId, timeline.displayName]));
  const open = new Map<string, { bar: RouteBar; last: number; localStart: number; localEnd: number; status: string }>();
  const spans: { bar: RouteBar; localStart: number; localEnd: number; status: string }[] = [];
  let read = 0;
  for (const frame of frames) {
    const sample = times.get(frame.hostFrame);
    if (sample === undefined || sample.seconds === null) continue;
    read++;
    for (const playback of frame.playbacks) {
      if (playback.call === null || playback.domain === null) continue;
      const key = JSON.stringify([playback.documentId, playback.domain, callKey(playback.call)]);
      const current = open.get(key);
      if (current !== undefined && current.last === sample.index - 1) {
        current.bar.end = sample.seconds + step;
        current.last = sample.index;
        current.localEnd = playback.cursorSeconds;
        current.status = playback.status;
        continue;
      }
      const name = names.get(playback.documentId) ?? playback.documentId.slice(0, 8);
      const span = {
        bar: {
          key: `real:${key}:${sample.index}`, start: sample.seconds, end: sample.seconds + step, label: `${name} · ${playback.domain}`, tip: '',
          documentId: names.has(playback.documentId) ? playback.documentId : null, target: { kind: 'record' as const, index: sample.index },
          source: null, quickIndex: null, observation: { call: playback.call, domain: playback.domain }, hint: false,
        },
        last: sample.index, localStart: playback.cursorSeconds, localEnd: playback.cursorSeconds, status: playback.status,
      };
      open.set(key, span);
      spans.push(span);
    }
  }
  for (const span of spans)
    span.bar.tip = `${span.bar.label}\n输入时间 ${range(span.bar.start, span.bar.end)}\n本地游标 ${range(span.localStart, span.localEnd)} · ${span.status}\n` +
      `${callLabel(span.bar.observation!.call)}\n单击定位到首个已读帧 · 双击进入 Timeline 并选中这次调用`;
  return {
    kind: 'real', title: '实际路线', meta: `已读 ${read} 帧 · ${spans.length} 次播放`,
    tip: '按已读档案帧的实际播放记录拼出，随游标分页读取；未读的帧不外推',
    notice: read === 0 ? '尚未读到带输入时间的档案帧' : spans.length === 0 ? '已读帧内没有 Timeline 播放记录' : null,
    lanes: pack(spans.map(span => span.bar)), hints: [], segments: [],
  };
}
