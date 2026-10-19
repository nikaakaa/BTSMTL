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
        internal TimelineStaticChainSpan(OrchestrationStaticState state, OrchestrationStaticSection section,
            OrchestrationTime start, OrchestrationTime end, OrchestrationTime localStart,
            OrchestrationTime localEnd, int cycle, bool recovery, bool playbackCompleted)
        {
            State = state;
            Section = section;
            Start = start;
            End = end;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Cycle = cycle;
            Recovery = recovery;
            PlaybackCompleted = playbackCompleted;
        }

        public OrchestrationStaticState State { get; }
        public OrchestrationStaticSection Section { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime LocalStart { get; }
        public OrchestrationTime LocalEnd { get; }
        public int Cycle { get; }
        public bool Recovery { get; }
        public bool PlaybackCompleted { get; }
    }

    public sealed class TimelineStaticChainMarker
    {
        internal TimelineStaticChainMarker(OrchestrationStaticState source, OrchestrationStaticTransition transition,
            OrchestrationStaticContinueRequest request, OrchestrationStaticSection nextSection,
            IReadOnlyList<OrchestrationStaticClip> doors, OrchestrationTime time,
            OrchestrationTime fromLocal, OrchestrationTime toLocal)
        {
            Source = source;
            Transition = transition;
            Request = request;
            NextSection = nextSection;
            Doors = doors;
            Time = time;
            FromLocal = fromLocal;
            ToLocal = toLocal;
        }

        public OrchestrationStaticState Source { get; }
        public OrchestrationStaticTransition Transition { get; }
        public OrchestrationStaticContinueRequest Request { get; }
        public OrchestrationStaticSection NextSection { get; }
        public IReadOnlyList<OrchestrationStaticClip> Doors { get; }
        public OrchestrationTime Time { get; }
        public OrchestrationTime FromLocal { get; }
        public OrchestrationTime ToLocal { get; }
        public bool IsSectionJump => Request != null;
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
            string startingSectionId, OrchestrationTime previewEnd, ITimelineStaticReadIntervals inputs)
        {
            foreach (OrchestrationStaticTransition initial in query.InitialTransitions(machineOperationIndex))
                if (Earliest(initial.Condition, new ConditionTime(null, null, OrchestrationTime.Zero,
                    OrchestrationTime.Zero, OrchestrationTime.Zero, true, false), inputs,
                    out OrchestrationTime time, out _, out _) && time == OrchestrationTime.Zero)
                {
                    if (initial.Target == null)
                        return new TimelineStaticChain
                        {
                            StopReason = TimelineStaticChainStopReason.NoInitialState,
                            Reason = "入口选中的目标不是状态，需在会话中验证。"
                        };
                    return Build(initial.Target, startingSectionId, previewEnd, inputs);
                }
            return new TimelineStaticChain
            {
                StopReason = TimelineStaticChainStopReason.NoInitialState,
                Reason = "入口状态的条件在预览起点没有成立。"
            };
        }

        public static TimelineStaticChain Build(OrchestrationStaticState entryState, string startingSectionId,
            OrchestrationTime previewEnd, ITimelineStaticReadIntervals inputs)
        {
            var result = new TimelineStaticChain();
            OrchestrationStaticState state = entryState;
            OrchestrationTime time = OrchestrationTime.Zero;
            OrchestrationTime local = state.Timeline != null && startingSectionId.Length != 0
                ? state.Timeline.Section(startingSectionId).Start : state.TimelineStartTime;
            bool startKnown = startingSectionId.Length != 0 || state.HasStaticTimelineStartTime;
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
                        "Timeline 默认起播时刻来自运行数据；预览入口可显式选择起始分段，后续状态需在会话中验证。");
                if (state.PlaybackMode == OrchestrationTimelinePlaybackMode.HoldLastFrame)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession,
                        "HoldLastFrame 在终点前保持游标，需在会话中验证后续停留与转移。");
                if (!visited.Add((state.OperationIndex, local.RawValue, time.RawValue)))
                    return result.Stop(TimelineStaticChainStopReason.ZeroTimeCycle, "去向形成零时长循环，需在会话中验证逐 Tick 的结果。");
                OrchestrationStaticTimeline timeline = state.Timeline;
                OrchestrationStaticSection section = timeline.SectionAt(local);
                OrchestrationTime endLocal = section != null ? section.End : timeline.Duration;
                if (section == null)
                    foreach (OrchestrationStaticSection later in timeline.Sections)
                        if (later.Start > local)
                        {
                            endLocal = later.Start;
                            break;
                        }
                OrchestrationTime end = OrchestrationTime.Min(previewEnd, time + endLocal - local);
                bool completed = state.PlaybackMode == OrchestrationTimelinePlaybackMode.Once &&
                    end == time + endLocal - local && (section?.Continuable == true || endLocal == timeline.Duration);
                Candidate selected = Select(state, section, time, local, end, completed, inputs,
                    out string unavailableReason, out bool needsSession);
                if (needsSession)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession, unavailableReason);
                OrchestrationTime spanEnd = selected.Exists ? selected.Time : end;
                OrchestrationTime spanEndLocal = local + spanEnd - time;
                bool playbackCompleted = completed && spanEnd == end && selected.Transition?.FromAnyState != true;
                bool recovery = playbackCompleted && section?.Continuable == true;
                result.m_Spans.Add(new TimelineStaticChainSpan(state, section, time, spanEnd, local,
                    spanEndLocal, cycle, recovery, playbackCompleted));
                if (selected.Exists)
                {
                    OrchestrationTime nextLocal = selected.NextSection != null
                        ? selected.NextSection.Start : OrchestrationTime.Zero;
                    result.m_Markers.Add(new TimelineStaticChainMarker(state, selected.Transition,
                        selected.Request, selected.NextSection, OpenDoors(state, selected, spanEndLocal),
                        spanEnd, spanEndLocal, nextLocal));
                    time = spanEnd;
                    local = nextLocal;
                    if (selected.Transition != null)
                    {
                        state = selected.Transition.Target;
                        cycle = 0;
                        if (state == null)
                            return result.Stop(TimelineStaticChainStopReason.StateMachineExited, "已到达状态机出口。");
                        local = state.TimelineStartTime;
                        startKnown = state.HasStaticTimelineStartTime;
                    }
                    continue;
                }
                time = end;
                local = spanEndLocal;
                if (recovery)
                    return result.Stop(TimelineStaticChainStopReason.TimelineCompleted,
                        "分段没有成立的继续门，播放到分段终点收招。" + unavailableReason);
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
            internal Candidate(OrchestrationTime time, OrchestrationStaticTransition transition,
                OrchestrationStaticClip clip, OrchestrationStaticContinueRequest request,
                OrchestrationStaticSection nextSection)
            {
                Exists = true;
                Time = time;
                Transition = transition;
                Clip = clip;
                Request = request;
                NextSection = nextSection;
            }

            internal bool Exists { get; }
            internal OrchestrationTime Time { get; }
            internal OrchestrationStaticTransition Transition { get; }
            internal OrchestrationStaticClip Clip { get; }
            internal OrchestrationStaticContinueRequest Request { get; }
            internal OrchestrationStaticSection NextSection { get; }
            internal int Phase => Transition?.FromAnyState == true ? 0 : Request != null ? 1 : 2;
        }

        static Candidate Select(OrchestrationStaticState state, OrchestrationStaticSection section,
            OrchestrationTime start, OrchestrationTime local, OrchestrationTime end, bool completed,
            ITimelineStaticReadIntervals inputs, out string unavailableReason, out bool needsSession)
        {
            Candidate selected = default;
            OrchestrationStaticRead? unavailable = null;
            OrchestrationStaticTimeline timeline = state.Timeline;
            needsSession = false;
            foreach (OrchestrationStaticTransition transition in state.Transitions)
            {
                var context = new ConditionTime(state, section, start, local, end, completed,
                    completed && !transition.FromAnyState);
                if (Earliest(transition.Condition, in context, inputs, out OrchestrationTime time,
                    out OrchestrationStaticRead? blocked, out bool unresolved))
                    Choose(ref selected, new Candidate(time, transition, null, null, null));
                else if (!unavailable.HasValue)
                    unavailable = blocked;
                needsSession |= unresolved;
            }
            if (section != null && section.Continuable)
                foreach (OrchestrationStaticClip clip in timeline.Clips)
                {
                    if (state.PlaybackMode == OrchestrationTimelinePlaybackMode.Loop || timeline.SectionAt(clip.Start) != section)
                        continue;
                    OrchestrationTime gateStart = OrchestrationTime.Max(start, start + clip.Start - local);
                    OrchestrationTime gateEnd = OrchestrationTime.Min(end, start + clip.End - local);
                    foreach (OrchestrationStaticContinueRequest request in clip.ContinueRequests)
                    {
                        if (request.TimelineOperationIndex != state.TimelineOperationIndex)
                            continue;
                        bool enable = request.Hook == OrchestrationTimelineHook.Enable;
                        if (gateStart >= gateEnd || enable && gateStart != start + clip.Start - local)
                            continue;
                        var context = new ConditionTime(state, section, gateStart, local + gateStart - start,
                            enable ? gateStart : gateEnd, enable, false);
                        if (Earliest(request.Condition, in context, inputs, out OrchestrationTime time,
                            out OrchestrationStaticRead? blocked, out bool unresolved))
                            Choose(ref selected, new Candidate(time, null, clip, request, timeline.Section(section.NextSectionId)));
                        else if (!unavailable.HasValue)
                            unavailable = blocked;
                        needsSession |= unresolved;
                    }
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
                    var context = new ConditionTime(state, section, end, local + end - start, end, true, true);
                    if (Earliest(transition.Condition, in context, inputs, out _, out _, out _))
                        nextTickAny = true;
                }
            needsSession |= nextTickAny;
            unavailableReason = nextTickAny ? "AnyState 要在下一次 FSM Tick 才读取到主体完成，需在会话中验证具体时刻。" :
                needsSession ? "条件查询了当前静态遍历之外的 Timeline 播放实例，需在会话中验证。" : UnavailableReason(unavailable);
            return selected;
        }

        static void Choose(ref Candidate selected, Candidate candidate)
        {
            if (!selected.Exists || candidate.Time < selected.Time ||
                candidate.Time == selected.Time && (candidate.Phase < selected.Phase ||
                    candidate.Phase == selected.Phase && candidate.Request != null &&
                    string.CompareOrdinal(candidate.Clip.ClipId, selected.Clip.ClipId) < 0))
                selected = candidate;
        }

        public static bool EvaluateConditionAt(OrchestrationStaticCondition condition,
            OrchestrationStaticState state, OrchestrationStaticSection sourceSection,
            OrchestrationTime localTime, OrchestrationTime previewTime, bool rootCompleted,
            ITimelineStaticReadIntervals inputs, out OrchestrationStaticRead? unavailable, out bool needsSession) =>
            Earliest(condition, new ConditionTime(state, sourceSection, previewTime, localTime,
                previewTime, true, rootCompleted), inputs, out _, out unavailable, out needsSession);

        readonly struct ConditionTime
        {
            internal ConditionTime(OrchestrationStaticState state, OrchestrationStaticSection section,
                OrchestrationTime start, OrchestrationTime local, OrchestrationTime end, bool includesEnd, bool completed)
            {
                State = state;
                Section = section;
                Start = start;
                Local = local;
                End = end;
                IncludesEnd = includesEnd;
                Completed = completed;
            }

            internal OrchestrationStaticState State { get; }
            internal OrchestrationStaticSection Section { get; }
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
                case OrchestrationStaticConditionKind.TimelineSection:
                {
                    OrchestrationStaticState state = context.State;
                    if (state == null || state.TimelineIdentity != condition.TimelineIdentity || state.FrameIdentity != condition.FrameIdentity)
                    {
                        needsSession = true;
                        return new List<TimeRange>();
                    }
                    var result = new List<TimeRange>();
                    foreach (OrchestrationStaticSection section in state.Timeline.Sections)
                        if (section.AuthoringId == condition.SectionId)
                            result.Add(new TimeRange((context.Start + section.Start - context.Local).RawValue,
                                (context.Start + section.End - context.Local).RawValue - 1));
                    if (condition.SectionId.Length == 0)
                    {
                        foreach (OrchestrationStaticSection section in state.Timeline.Sections)
                            result.Add(new TimeRange((context.Start + section.Start - context.Local).RawValue,
                                (context.Start + section.End - context.Local).RawValue - 1));
                        Normalize(result);
                        result = Complement(Intersect(universe, result), in context);
                    }
                    if (context.IncludesEnd && context.Section != null)
                    {
                        result = Intersect(result, new List<TimeRange>
                            { new(context.Start.RawValue, context.End.RawValue - 1) });
                        if (context.Section.AuthoringId == condition.SectionId)
                            result.Add(new TimeRange(context.End.RawValue, context.End.RawValue));
                    }
                    Normalize(result);
                    return result;
                }
                default:
                {
                    OrchestrationStaticRead raw = condition.Read;
                    OrchestrationStaticRead read = TimelineDoorReads.EffectiveRead(context.State?.Timeline, in raw);
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
                            if (inputs.RuntimeData(in read))
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

        static IReadOnlyList<OrchestrationStaticClip> OpenDoors(OrchestrationStaticState state,
            in Candidate candidate, OrchestrationTime local)
        {
            var doors = new List<OrchestrationStaticClip>();
            if (candidate.Clip != null)
                doors.Add(candidate.Clip);
            if (candidate.Transition != null)
                foreach (OrchestrationStaticClip clip in state.Timeline.Clips)
                    if (clip.Start <= local && local < clip.End)
                        foreach (OrchestrationStaticRead read in candidate.Transition.Reads)
                            if (read.Source == OrchestrationStaticReadSource.ClipState && !doors.Contains(clip))
                                foreach (string key in clip.ProvidedStateKeys)
                                    if (string.Equals(key, read.Key, StringComparison.Ordinal))
                                        doors.Add(clip);
            return doors;
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
