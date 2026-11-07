using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation.Analysis
{
    public readonly struct TimelineStaticInterval
    {
        public TimelineStaticInterval(OrchestrationTime start, OrchestrationTime end)
        {
            Start = start;
            End = end;
        }

        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
    }

    public interface ITimelineStaticReadIntervals
    {
        // 区间采用预览全局时间，左闭右开；运行数据的成立区间（含默认值）由预览提供者持有。
        // RuntimeData 收到原始读取：来源仍为 ClipState 表示当前 Timeline 没有 Clip 静态提供该状态。
        IReadOnlyList<TimelineStaticInterval> HostInput(string inputId);
        IReadOnlyList<TimelineStaticInterval> RuntimeData(in OrchestrationStaticRead read);
    }

    public interface ITimelineStaticClipState
    {
        OrchestrationTime Start { get; }
        OrchestrationTime End { get; }
        IReadOnlyList<string> ProvidedStateKeys { get; }
    }

    public static class TimelineStaticConditionEvaluator
    {
        // null 表示没有当前状态的 Timeline，空列表表示有 Timeline 但无 Clip 提供状态。
        public static bool EvaluateConditionAt(OrchestrationStaticCondition condition,
            IReadOnlyList<ITimelineStaticClipState> clips, OrchestrationTime localTime, OrchestrationTime previewTime,
            bool rootCompleted, ITimelineStaticReadIntervals inputs,
            out OrchestrationStaticRead? unavailable, out bool needsSession) =>
            Earliest(condition, new ConditionTime(clips, previewTime, localTime, previewTime, true, rootCompleted),
                inputs, out _, out unavailable, out needsSession);

        internal readonly struct ConditionTime
        {
            internal ConditionTime(IReadOnlyList<ITimelineStaticClipState> clips,
                OrchestrationTime start, OrchestrationTime local, OrchestrationTime end, bool includesEnd, bool completed)
            {
                Clips = clips;
                Start = start;
                Local = local;
                End = end;
                IncludesEnd = includesEnd;
                Completed = completed;
            }

            internal IReadOnlyList<ITimelineStaticClipState> Clips { get; }
            internal OrchestrationTime Start { get; }
            internal OrchestrationTime Local { get; }
            internal OrchestrationTime End { get; }
            internal bool IncludesEnd { get; }
            internal bool Completed { get; }
            internal long Last => End.RawValue - (IncludesEnd ? 0 : 1);
        }

        // 时间采用正式 Raw 整数格；闭合格点集合能精确表示完成事件，不给它制造持续区间。
        readonly struct TimeRange
        {
            internal TimeRange(long first, long last)
            {
                First = first;
                Last = last;
            }

            internal long First { get; }
            internal long Last { get; }
        }

        struct TimeRanges
        {
            readonly List<TimeRange> m_Ranges;
            readonly long m_Point;
            bool m_HasPoint;

            internal TimeRanges(in ConditionTime context)
            {
                m_Ranges = context.IncludesEnd && context.Start == context.End ? null : new List<TimeRange>();
                m_Point = context.Start.RawValue;
                m_HasPoint = false;
            }

            internal readonly int Count => m_Ranges?.Count ?? (m_HasPoint ? 1 : 0);
            internal readonly TimeRange this[int index] => m_Ranges == null ? new TimeRange(m_Point, m_Point) : m_Ranges[index];

            internal void Add(TimeRange range)
            {
                if (m_Ranges == null)
                    m_HasPoint |= range.First <= m_Point && m_Point <= range.Last;
                else
                    m_Ranges.Add(range);
            }

            internal void AddRange(in TimeRanges ranges)
            {
                if (m_Ranges == null)
                    m_HasPoint |= ranges.m_HasPoint;
                else
                    m_Ranges.AddRange(ranges.m_Ranges);
            }

            internal void Normalize()
            {
                if (m_Ranges == null)
                    return;
                m_Ranges.Sort((left, right) => left.First.CompareTo(right.First));
                int count = 0;
                for (int i = 0; i < m_Ranges.Count; i++)
                {
                    TimeRange range = m_Ranges[i];
                    if (range.First > range.Last)
                        continue;
                    if (count != 0 && range.First - 1 <= m_Ranges[count - 1].Last)
                        m_Ranges[count - 1] = new TimeRange(m_Ranges[count - 1].First, Math.Max(m_Ranges[count - 1].Last, range.Last));
                    else
                        m_Ranges[count++] = range;
                }
                m_Ranges.RemoveRange(count, m_Ranges.Count - count);
            }
        }

        static TimeRanges Universe(in ConditionTime context)
        {
            var result = new TimeRanges(in context);
            result.Add(new TimeRange(context.Start.RawValue, context.Last));
            return result;
        }

        internal static bool Earliest(OrchestrationStaticCondition condition, in ConditionTime context,
            ITimelineStaticReadIntervals inputs, out OrchestrationTime earliest,
            out OrchestrationStaticRead? unavailable, out bool needsSession)
        {
            unavailable = null;
            needsSession = false;
            TimeRanges intervals = Intersect(Universe(in context),
                Intervals(condition, in context, inputs, ref unavailable, ref needsSession), in context);
            earliest = intervals.Count == 0 ? default : OrchestrationTime.FromRaw(intervals[0].First);
            return intervals.Count != 0;
        }

        static TimeRanges Intervals(OrchestrationStaticCondition condition, in ConditionTime context,
            ITimelineStaticReadIntervals inputs, ref OrchestrationStaticRead? unavailable, ref bool needsSession)
        {
            switch (condition.Kind)
            {
                case OrchestrationStaticConditionKind.Constant:
                    return condition.ConstantValue ? Universe(in context) : new TimeRanges(in context);
                case OrchestrationStaticConditionKind.All:
                {
                    TimeRanges result = Universe(in context);
                    for (int i = 0; i < condition.Children.Count; i++)
                    {
                        result = Intersect(result, Intervals(condition.Children[i], in context, inputs,
                            ref unavailable, ref needsSession), in context);
                        if (result.Count == 0)
                            break;
                    }
                    return result;
                }
                case OrchestrationStaticConditionKind.Any:
                {
                    var result = new TimeRanges(in context);
                    for (int i = 0; i < condition.Children.Count; i++)
                        result.AddRange(Intervals(condition.Children[i], in context, inputs, ref unavailable, ref needsSession));
                    result.Normalize();
                    return result;
                }
                case OrchestrationStaticConditionKind.Not:
                    return Complement(Intersect(Universe(in context),
                        Intervals(condition.Children[0], in context, inputs, ref unavailable, ref needsSession), in context), in context);
                case OrchestrationStaticConditionKind.StateRootCompleted:
                {
                    var result = new TimeRanges(in context);
                    if (context.Completed)
                        result.Add(new TimeRange(context.End.RawValue, context.End.RawValue));
                    return result;
                }
                case OrchestrationStaticConditionKind.TimelineTime:
                {
                    var result = new TimeRanges(in context);
                    if (context.Clips == null)
                    {
                        needsSession = true;
                        return result;
                    }
                    long at = (context.Start + condition.Threshold - context.Local).RawValue;
                    long first = context.Start.RawValue;
                    long last = context.Last;
                    switch (condition.Comparison)
                    {
                        case OrchestrationComparisonKind.Equal: result.Add(new TimeRange(at, at)); break;
                        case OrchestrationComparisonKind.NotEqual:
                            result.Add(new TimeRange(first, at - 1));
                            result.Add(new TimeRange(at + 1, last));
                            break;
                        case OrchestrationComparisonKind.LessThan: result.Add(new TimeRange(first, at - 1)); break;
                        case OrchestrationComparisonKind.LessThanOrEqual: result.Add(new TimeRange(first, at)); break;
                        case OrchestrationComparisonKind.GreaterThanOrEqual: result.Add(new TimeRange(at, last)); break;
                        default: result.Add(new TimeRange(at + 1, last)); break;
                    }
                    result.Normalize();
                    return Intersect(Universe(in context), result, in context);
                }
                default:
                {
                    OrchestrationStaticRead raw = condition.Read;
                    OrchestrationStaticRead read = EffectiveRead(context.Clips, in raw);
                    var result = new TimeRanges(in context);
                    switch (read.Source)
                    {
                        case OrchestrationStaticReadSource.ClipState:
                            for (int i = 0; i < context.Clips.Count; i++)
                            {
                                ITimelineStaticClipState clip = context.Clips[i];
                                if (ProvidesState(clip, read.Key))
                                    result.Add(new TimeRange((context.Start + clip.Start - context.Local).RawValue,
                                        (context.Start + clip.End - context.Local).RawValue - 1));
                            }
                            break;
                        case OrchestrationStaticReadSource.HostInput:
                        {
                            IReadOnlyList<TimelineStaticInterval> intervals = inputs.HostInput(read.Key);
                            for (int i = 0; i < intervals.Count; i++)
                            {
                                TimelineStaticInterval interval = intervals[i];
                                result.Add(new TimeRange(interval.Start.RawValue, interval.End.RawValue - 1));
                            }
                            break;
                        }
                        default:
                        {
                            IReadOnlyList<TimelineStaticInterval> intervals = inputs.RuntimeData(in raw);
                            for (int i = 0; i < intervals.Count; i++)
                            {
                                TimelineStaticInterval interval = intervals[i];
                                result.Add(new TimeRange(interval.Start.RawValue,
                                    interval.End == OrchestrationTime.MaxValue ? long.MaxValue : interval.End.RawValue - 1));
                            }
                            break;
                        }
                    }
                    result.Normalize();
                    if (!unavailable.HasValue && Intersect(Universe(in context), result, in context).Count == 0)
                        unavailable = read;
                    return result;
                }
            }
        }

        static TimeRanges Intersect(in TimeRanges left, in TimeRanges right, in ConditionTime context)
        {
            var result = new TimeRanges(in context);
            int i = 0;
            int j = 0;
            while (i < left.Count && j < right.Count)
            {
                TimeRange leftRange = left[i];
                TimeRange rightRange = right[j];
                long first = Math.Max(leftRange.First, rightRange.First);
                long last = Math.Min(leftRange.Last, rightRange.Last);
                if (first <= last)
                    result.Add(new TimeRange(first, last));
                if (leftRange.Last <= rightRange.Last)
                    i++;
                else
                    j++;
            }
            return result;
        }

        static TimeRanges Complement(in TimeRanges ranges, in ConditionTime context)
        {
            var result = new TimeRanges(in context);
            long first = context.Start.RawValue;
            long last = context.Last;
            for (int i = 0; i < ranges.Count; i++)
            {
                TimeRange range = ranges[i];
                if (first < range.First)
                    result.Add(new TimeRange(first, range.First - 1));
                first = range.Last + 1;
            }
            if (first <= last)
                result.Add(new TimeRange(first, last));
            return result;
        }

        internal static OrchestrationStaticReadSource EffectiveSource(IReadOnlyList<ITimelineStaticClipState> clips,
            in OrchestrationStaticRead read)
        {
            if (read.Source != OrchestrationStaticReadSource.ClipState)
                return read.Source;
            if (clips != null)
                for (int i = 0; i < clips.Count; i++)
                    if (ProvidesState(clips[i], read.Key))
                        return OrchestrationStaticReadSource.ClipState;
            return OrchestrationStaticReadSource.RuntimeData;
        }

        internal static OrchestrationStaticRead EffectiveRead(IReadOnlyList<ITimelineStaticClipState> clips,
            in OrchestrationStaticRead read)
        {
            OrchestrationStaticReadSource source = EffectiveSource(clips, in read);
            return source == read.Source ? read : new OrchestrationStaticRead(read.OperationIndex,
                source, read.Key, true, read.Label);
        }

        internal static bool ProvidesState(ITimelineStaticClipState clip, string key)
        {
            IReadOnlyList<string> keys = clip.ProvidedStateKeys;
            for (int i = 0; i < keys.Count; i++)
                if (string.Equals(keys[i], key, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
