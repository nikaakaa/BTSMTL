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
        // 区间采用预览全局时间，左闭右开；运行数据的默认成立由预览偏好提供者持有。
        // RuntimeData 收到原始读取：来源仍为 ClipState 表示当前 Timeline 没有 Clip 静态提供该状态。
        IReadOnlyList<TimelineStaticInterval> HostInput(string inputId);
        bool RuntimeData(in OrchestrationStaticRead read);
    }

    public enum TimelineStaticChainStopReason : byte
    {
        TimelineCompleted,
        StateMachineExited,
        NeedsSession,
        NoInitialState,
        ZeroTimeCycle,
        PreviewEnd
    }

    public sealed class TimelineStaticChainSpan
    {
        internal TimelineStaticChainSpan(OrchestrationStaticState state,
            OrchestrationTime start, OrchestrationTime end, OrchestrationTime localStart,
            OrchestrationTime localEnd, int cycle, bool playbackCompleted)
        {
            State = state;
            Start = start;
            End = end;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Cycle = cycle;
            PlaybackCompleted = playbackCompleted;
        }

        public OrchestrationStaticState State { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime LocalStart { get; }
        public OrchestrationTime LocalEnd { get; }
        public int Cycle { get; }
        // 本段播到 Timeline 终点（Once）；后续沿"完成"转移继续。
        public bool PlaybackCompleted { get; }
        // 被转移打断：本段没播到终点，[LocalEnd, Duration) 为未播部分。
        public bool Interrupted => !PlaybackCompleted && State.Timeline != null && LocalEnd < State.Timeline.Duration;
    }

    public sealed class TimelineStaticChainMarker
    {
        internal TimelineStaticChainMarker(OrchestrationStaticState source, OrchestrationStaticTransition transition,
            IReadOnlyList<OrchestrationStaticClip> providers, OrchestrationTime time,
            OrchestrationTime fromLocal, OrchestrationTime toLocal)
        {
            Source = source;
            Transition = transition;
            Providers = providers;
            Time = time;
            FromLocal = fromLocal;
            ToLocal = toLocal;
        }

        public OrchestrationStaticState Source { get; }
        public OrchestrationStaticTransition Transition { get; }
        // 转移时刻生效、提供转移所读状态的 Clip。
        public IReadOnlyList<OrchestrationStaticClip> Providers { get; }
        public OrchestrationTime Time { get; }
        public OrchestrationTime FromLocal { get; }
        public OrchestrationTime ToLocal { get; }
    }

    public sealed class TimelineStaticChain
    {
        readonly List<TimelineStaticChainSpan> m_Spans = new();
        readonly List<TimelineStaticChainMarker> m_Markers = new();

        public IReadOnlyList<TimelineStaticChainSpan> Spans => m_Spans;
        public IReadOnlyList<TimelineStaticChainMarker> Markers => m_Markers;
        public TimelineStaticChainStopReason StopReason { get; private set; }
        public string Reason { get; private set; }
        public OrchestrationStaticState LastState { get; private set; }

        public static TimelineStaticChain Build(OrchestrationStaticQuery query, int machineOperationIndex,
            OrchestrationTime previewEnd, ITimelineStaticReadIntervals inputs)
        {
            foreach (OrchestrationStaticTransition initial in query.InitialTransitions(machineOperationIndex))
                if (Earliest(initial.Condition, new ConditionTime(null, OrchestrationTime.Zero,
                    OrchestrationTime.Zero, OrchestrationTime.Zero, true, false), inputs,
                    out OrchestrationTime time, out _, out _) && time == OrchestrationTime.Zero)
                {
                    if (initial.Target == null)
                        return new TimelineStaticChain
                        {
                            StopReason = TimelineStaticChainStopReason.NoInitialState,
                            Reason = "入口选中的目标不是状态，需在会话中验证。"
                        };
                    return Build(initial.Target, previewEnd, inputs);
                }
            return new TimelineStaticChain
            {
                StopReason = TimelineStaticChainStopReason.NoInitialState,
                Reason = "入口状态的条件在预览起点没有成立。"
            };
        }

        // 只沿状态转移推导：出边按成立区间的最早时刻与正式比较器选择，无出边成立时播到终点并按"完成"转移继续。
        public static TimelineStaticChain Build(OrchestrationStaticState entryState,
            OrchestrationTime previewEnd, ITimelineStaticReadIntervals inputs)
        {
            var result = new TimelineStaticChain();
            OrchestrationStaticState state = entryState;
            OrchestrationTime time = OrchestrationTime.Zero;
            OrchestrationTime local = state.TimelineStartTime;
            bool startKnown = state.HasStaticTimelineStartTime;
            var visited = new HashSet<(int State, long Local, long Time)>();
            int cycle = 0;
            while (time < previewEnd)
            {
                result.LastState = state;
                if (state.Timeline == null)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession, "状态没有唯一绑定的 Timeline，需在会话中验证。");
                if (!state.TimelineDeterminesRootCompletion || !state.HasStaticTimelineClock)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession,
                        "当前静态链仅展开单个 Timeline 与不暂停的 1 倍常量时钟，其余 Root 或时钟需在会话中验证。");
                if (!startKnown)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession,
                        "Timeline 起播时刻来自运行数据，需在会话中验证。");
                if (state.PlaybackMode == OrchestrationTimelinePlaybackMode.HoldLastFrame)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession,
                        "HoldLastFrame 在终点前保持游标，需在会话中验证后续停留与转移。");
                if (!visited.Add((state.OperationIndex, local.RawValue, time.RawValue)))
                    return result.Stop(TimelineStaticChainStopReason.ZeroTimeCycle, "去向形成零时长循环，需在会话中验证逐 Tick 的结果。");
                OrchestrationStaticTimeline timeline = state.Timeline;
                OrchestrationTime endLocal = timeline.Duration;
                OrchestrationTime end = OrchestrationTime.Min(previewEnd, time + endLocal - local);
                bool completed = state.PlaybackMode == OrchestrationTimelinePlaybackMode.Once &&
                    end == time + endLocal - local;
                Candidate selected = Select(state, time, local, end, completed, inputs,
                    out string unavailableReason, out bool needsSession);
                if (needsSession)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession, unavailableReason);
                OrchestrationTime spanEnd = selected.Exists ? selected.Time : end;
                OrchestrationTime spanEndLocal = local + spanEnd - time;
                bool playbackCompleted = completed && spanEnd == end && selected.Transition?.FromAnyState != true;
                result.m_Spans.Add(new TimelineStaticChainSpan(state, time, spanEnd, local,
                    spanEndLocal, cycle, playbackCompleted));
                if (selected.Exists)
                {
                    result.m_Markers.Add(new TimelineStaticChainMarker(state, selected.Transition,
                        Providers(state, selected.Transition, spanEndLocal), spanEnd, spanEndLocal,
                        selected.Transition.Target?.TimelineStartTime ?? OrchestrationTime.Zero));
                    time = spanEnd;
                    state = selected.Transition.Target;
                    cycle = 0;
                    if (state == null)
                        return result.Stop(TimelineStaticChainStopReason.StateMachineExited, "已到达状态机出口。");
                    local = state.TimelineStartTime;
                    startKnown = state.HasStaticTimelineStartTime;
                    continue;
                }
                time = end;
                local = spanEndLocal;
                if (local == timeline.Duration)
                {
                    if (state.PlaybackMode != OrchestrationTimelinePlaybackMode.Loop)
                        return result.Stop(TimelineStaticChainStopReason.TimelineCompleted, unavailableReason);
                    local = OrchestrationTime.Zero;
                    cycle++;
                }
                if (time == previewEnd)
                    return result.Stop(TimelineStaticChainStopReason.PreviewEnd, "已到预览显示范围的终点。");
            }
            return result.Stop(TimelineStaticChainStopReason.PreviewEnd, "已到预览显示范围的终点。");
        }

        TimelineStaticChain Stop(TimelineStaticChainStopReason reason, string message)
        {
            StopReason = reason;
            Reason = message;
            return this;
        }

        readonly struct Candidate
        {
            internal Candidate(OrchestrationTime time, OrchestrationStaticTransition transition)
            {
                Exists = true;
                Time = time;
                Transition = transition;
            }

            internal bool Exists { get; }
            internal OrchestrationTime Time { get; }
            internal OrchestrationStaticTransition Transition { get; }
        }

        static Candidate Select(OrchestrationStaticState state,
            OrchestrationTime start, OrchestrationTime local, OrchestrationTime end, bool completed,
            ITimelineStaticReadIntervals inputs, out string unavailableReason, out bool needsSession)
        {
            Candidate selected = default;
            OrchestrationStaticRead? unavailable = null;
            needsSession = false;
            // 出边已按正式比较器排序（AnyState 优先，再按优先级与顺序）；同一时刻先成立的胜出。
            foreach (OrchestrationStaticTransition transition in state.Transitions)
            {
                var context = new ConditionTime(state, start, local, end, completed,
                    completed && !transition.FromAnyState);
                if (Earliest(transition.Condition, in context, inputs, out OrchestrationTime time,
                    out OrchestrationStaticRead? blocked, out bool unresolved))
                {
                    if (!selected.Exists || time < selected.Time)
                        selected = new Candidate(time, transition);
                }
                else if (!unavailable.HasValue)
                    unavailable = blocked;
                needsSession |= unresolved;
            }
            bool nextTickAny = false;
            if (completed && !selected.Exists)
                foreach (OrchestrationStaticTransition transition in state.Transitions)
                {
                    if (!transition.FromAnyState)
                        continue;
                    bool readsCompletion = false;
                    foreach (OrchestrationStaticRead read in transition.Reads)
                        readsCompletion |= read.Source == OrchestrationStaticReadSource.StateCompletion;
                    if (!readsCompletion)
                        continue;
                    var context = new ConditionTime(state, end, local + end - start, end, true, true);
                    if (Earliest(transition.Condition, in context, inputs, out _, out _, out _))
                        nextTickAny = true;
                }
            needsSession |= nextTickAny;
            unavailableReason = nextTickAny ? "AnyState 要在下一次 FSM Tick 才读取到主体完成，需在会话中验证具体时刻。" :
                needsSession ? "条件查询了当前静态遍历之外的 Timeline 播放实例，需在会话中验证。" : UnavailableReason(unavailable);
            return selected;
        }

        public static bool EvaluateConditionAt(OrchestrationStaticCondition condition,
            OrchestrationStaticState state, OrchestrationTime localTime, OrchestrationTime previewTime, bool rootCompleted,
            ITimelineStaticReadIntervals inputs, out OrchestrationStaticRead? unavailable, out bool needsSession) =>
            Earliest(condition, new ConditionTime(state, previewTime, localTime,
                previewTime, true, rootCompleted), inputs, out _, out unavailable, out needsSession);

        readonly struct ConditionTime
        {
            internal ConditionTime(OrchestrationStaticState state,
                OrchestrationTime start, OrchestrationTime local, OrchestrationTime end, bool includesEnd, bool completed)
            {
                State = state;
                Start = start;
                Local = local;
                End = end;
                IncludesEnd = includesEnd;
                Completed = completed;
            }

            internal OrchestrationStaticState State { get; }
            internal OrchestrationTime Start { get; }
            internal OrchestrationTime Local { get; }
            internal OrchestrationTime End { get; }
            internal bool IncludesEnd { get; }
            internal bool Completed { get; }
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

        static List<TimeRange> Universe(in ConditionTime context) => new()
        {
            new TimeRange(context.Start.RawValue, context.End.RawValue - (context.IncludesEnd ? 0 : 1))
        };

        static bool Earliest(OrchestrationStaticCondition condition, in ConditionTime context,
            ITimelineStaticReadIntervals inputs, out OrchestrationTime earliest,
            out OrchestrationStaticRead? unavailable, out bool needsSession)
        {
            unavailable = null;
            needsSession = false;
            List<TimeRange> intervals = Intersect(Universe(in context),
                Intervals(condition, in context, inputs, ref unavailable, ref needsSession));
            earliest = intervals.Count == 0 ? default : OrchestrationTime.FromRaw(intervals[0].First);
            return intervals.Count != 0;
        }

        static List<TimeRange> Intervals(OrchestrationStaticCondition condition, in ConditionTime context,
            ITimelineStaticReadIntervals inputs, ref OrchestrationStaticRead? unavailable, ref bool needsSession)
        {
            List<TimeRange> universe = Universe(in context);
            switch (condition.Kind)
            {
                case OrchestrationStaticConditionKind.Constant:
                    return condition.ConstantValue ? universe : new List<TimeRange>();
                case OrchestrationStaticConditionKind.All:
                {
                    List<TimeRange> result = universe;
                    foreach (OrchestrationStaticCondition child in condition.Children)
                    {
                        result = Intersect(result, Intervals(child, in context, inputs, ref unavailable, ref needsSession));
                        if (result.Count == 0)
                            break;
                    }
                    return result;
                }
                case OrchestrationStaticConditionKind.Any:
                {
                    var result = new List<TimeRange>();
                    foreach (OrchestrationStaticCondition child in condition.Children)
                        result.AddRange(Intervals(child, in context, inputs, ref unavailable, ref needsSession));
                    Normalize(result);
                    return result;
                }
                case OrchestrationStaticConditionKind.Not:
                    return Complement(Intersect(universe,
                        Intervals(condition.Children[0], in context, inputs, ref unavailable, ref needsSession)), in context);
                case OrchestrationStaticConditionKind.StateRootCompleted:
                    return context.Completed
                        ? new List<TimeRange> { new(context.End.RawValue, context.End.RawValue) } : new List<TimeRange>();
                case OrchestrationStaticConditionKind.TimelineTime:
                {
                    if (context.State?.Timeline == null)
                    {
                        needsSession = true;
                        return new List<TimeRange>();
                    }
                    long at = (context.Start + condition.Threshold - context.Local).RawValue;
                    long first = context.Start.RawValue;
                    long last = context.End.RawValue - (context.IncludesEnd ? 0 : 1);
                    var result = new List<TimeRange>();
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
                    Normalize(result);
                    return Intersect(universe, result);
                }
                default:
                {
                    OrchestrationStaticRead raw = condition.Read;
                    OrchestrationStaticRead read = OrchestrationStaticReads.EffectiveRead(context.State?.Timeline, in raw);
                    var result = new List<TimeRange>();
                    switch (read.Source)
                    {
                        case OrchestrationStaticReadSource.ClipState:
                            foreach (OrchestrationStaticClip clip in context.State.Timeline.Clips)
                                foreach (string key in clip.ProvidedStateKeys)
                                    if (key == read.Key)
                                        result.Add(new TimeRange((context.Start + clip.Start - context.Local).RawValue,
                                            (context.Start + clip.End - context.Local).RawValue - 1));
                            break;
                        case OrchestrationStaticReadSource.HostInput:
                            foreach (TimelineStaticInterval interval in inputs.HostInput(read.Key))
                                result.Add(new TimeRange(interval.Start.RawValue, interval.End.RawValue - 1));
                            break;
                        default:
                            if (inputs.RuntimeData(in raw))
                                return universe;
                            break;
                    }
                    Normalize(result);
                    if (Intersect(universe, result).Count == 0 && !unavailable.HasValue)
                        unavailable = read;
                    return result;
                }
            }
        }

        static void Normalize(List<TimeRange> ranges)
        {
            ranges.Sort((left, right) => left.First.CompareTo(right.First));
            int count = 0;
            for (int i = 0; i < ranges.Count; i++)
            {
                TimeRange range = ranges[i];
                if (range.First > range.Last)
                    continue;
                if (count != 0 && range.First - 1 <= ranges[count - 1].Last)
                    ranges[count - 1] = new TimeRange(ranges[count - 1].First, Math.Max(ranges[count - 1].Last, range.Last));
                else
                    ranges[count++] = range;
            }
            ranges.RemoveRange(count, ranges.Count - count);
        }

        static List<TimeRange> Intersect(List<TimeRange> left, List<TimeRange> right)
        {
            var result = new List<TimeRange>();
            int i = 0;
            int j = 0;
            while (i < left.Count && j < right.Count)
            {
                long first = Math.Max(left[i].First, right[j].First);
                long last = Math.Min(left[i].Last, right[j].Last);
                if (first <= last)
                    result.Add(new TimeRange(first, last));
                if (left[i].Last <= right[j].Last)
                    i++;
                else
                    j++;
            }
            return result;
        }

        static List<TimeRange> Complement(List<TimeRange> ranges, in ConditionTime context)
        {
            var result = new List<TimeRange>();
            long first = context.Start.RawValue;
            long last = context.End.RawValue - (context.IncludesEnd ? 0 : 1);
            foreach (TimeRange range in ranges)
            {
                if (first < range.First)
                    result.Add(new TimeRange(first, range.First - 1));
                first = range.Last + 1;
            }
            if (first <= last)
                result.Add(new TimeRange(first, last));
            return result;
        }

        static IReadOnlyList<OrchestrationStaticClip> Providers(OrchestrationStaticState state,
            OrchestrationStaticTransition transition, OrchestrationTime local)
        {
            var providers = new List<OrchestrationStaticClip>();
            foreach (OrchestrationStaticClip clip in state.Timeline.Clips)
                if (clip.Start <= local && local < clip.End)
                    foreach (OrchestrationStaticRead read in transition.Reads)
                        if (read.Source == OrchestrationStaticReadSource.ClipState && !providers.Contains(clip))
                            foreach (string key in clip.ProvidedStateKeys)
                                if (string.Equals(key, read.Key, StringComparison.Ordinal))
                                    providers.Add(clip);
            return providers;
        }

        static string UnavailableReason(OrchestrationStaticRead? unavailable)
        {
            if (unavailable.HasValue)
            {
                OrchestrationStaticRead read = unavailable.Value;
                switch (read.Source)
                {
                    case OrchestrationStaticReadSource.HostInput:
                        return $"输入不在所需区间内：{read.Label}。";
                    case OrchestrationStaticReadSource.ClipState:
                        return $"Clip 提供的状态没有共同成立区间：{read.Label}。";
                    default:
                        return $"运行数据假设未成立：{read.Label}。";
                }
            }
            return "没有成立的去向，链在此结束。";
        }
    }
}
