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
            OrchestrationTime localEnd, int cycle, bool recovery)
        {
            State = state;
            Section = section;
            Start = start;
            End = end;
            LocalStart = localStart;
            LocalEnd = localEnd;
            Cycle = cycle;
            Recovery = recovery;
        }

        public OrchestrationStaticState State { get; }
        public OrchestrationStaticSection Section { get; }
        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime LocalStart { get; }
        public OrchestrationTime LocalEnd { get; }
        public int Cycle { get; }
        public bool Recovery { get; }
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
                if (Earliest(initial.Reads, null, OrchestrationTime.Zero, OrchestrationTime.Zero,
                    OrchestrationTime.FromRaw(1), inputs, out OrchestrationTime time, out _) && time == OrchestrationTime.Zero)
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
                ? state.Timeline.Section(startingSectionId).Start : OrchestrationTime.Zero;
            var visited = new HashSet<(int State, long Local, long Time)>();
            int cycle = 0;
            while (time < previewEnd)
            {
                result.LastState = state;
                if (state.Timeline == null)
                    return result.Stop(TimelineStaticChainStopReason.NeedsSession, "状态没有唯一绑定的 Timeline，需在会话中验证。");
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
                Candidate selected = Select(state, section, time, local, end, inputs, out string unavailableReason);
                OrchestrationTime spanEnd = selected.Exists ? selected.Time : end;
                OrchestrationTime spanEndLocal = local + spanEnd - time;
                bool recovery = !selected.Exists && section != null && section.Continuable && end == time + section.End - local;
                result.m_Spans.Add(new TimelineStaticChainSpan(state, section, time, spanEnd, local,
                    spanEndLocal, cycle, recovery));
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
            OrchestrationTime start, OrchestrationTime local, OrchestrationTime end, ITimelineStaticReadIntervals inputs,
            out string unavailableReason)
        {
            Candidate selected = default;
            OrchestrationStaticRead? unavailable = null;
            OrchestrationStaticTimeline timeline = state.Timeline;
            foreach (OrchestrationStaticTransition transition in state.Transitions)
            {
                if (Earliest(transition.Reads, timeline, start, local, end, inputs,
                    out OrchestrationTime time, out OrchestrationStaticRead? blocked))
                    Choose(ref selected, new Candidate(time, transition, null, null, null));
                else if (!unavailable.HasValue)
                    unavailable = blocked;
            }
            if (section != null && section.Continuable)
                foreach (OrchestrationStaticClip clip in timeline.Clips)
                {
                    if (timeline.SectionAt(clip.Start) != section)
                        continue;
                    OrchestrationTime gateStart = OrchestrationTime.Max(start, start + clip.Start - local);
                    OrchestrationTime gateEnd = OrchestrationTime.Min(end, start + clip.End - local);
                    foreach (OrchestrationStaticContinueRequest request in clip.ContinueRequests)
                    {
                        OrchestrationTime requestEnd = request.Hook == OrchestrationTimelineHook.Enable
                            ? OrchestrationTime.Min(gateEnd, gateStart + OrchestrationTime.FromRaw(1)) : gateEnd;
                        if (Earliest(request.Reads, timeline, gateStart, local + gateStart - start,
                            requestEnd, inputs, out OrchestrationTime time, out OrchestrationStaticRead? blocked))
                            Choose(ref selected, new Candidate(time, null, clip, request, timeline.Section(section.NextSectionId)));
                        else if (!unavailable.HasValue)
                            unavailable = blocked;
                    }
                }
            unavailableReason = UnavailableReason(unavailable);
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

        static bool Earliest(IReadOnlyList<OrchestrationStaticRead> reads, OrchestrationStaticTimeline timeline,
            OrchestrationTime start, OrchestrationTime local, OrchestrationTime end,
            ITimelineStaticReadIntervals inputs, out OrchestrationTime earliest, out OrchestrationStaticRead? unavailable)
        {
            unavailable = null;
            var intersection = new List<TimelineStaticInterval>();
            if (start < end)
                intersection.Add(new TimelineStaticInterval(start, end));
            foreach (OrchestrationStaticRead raw in reads)
            {
                OrchestrationStaticRead read = TimelineDoorReads.EffectiveRead(timeline, in raw);
                List<TimelineStaticInterval> intervals;
                switch (read.Source)
                {
                    case OrchestrationStaticReadSource.ClipState:
                        intervals = new List<TimelineStaticInterval>();
                        foreach (OrchestrationStaticClip clip in timeline.Clips)
                            foreach (string key in clip.ProvidedStateKeys)
                                if (string.Equals(key, read.Key, StringComparison.Ordinal))
                                    intervals.Add(new TimelineStaticInterval(start + clip.Start - local, start + clip.End - local));
                        break;
                    case OrchestrationStaticReadSource.HostInput:
                        intervals = new List<TimelineStaticInterval>(inputs.HostInput(read.Key));
                        break;
                    default:
                        if (inputs.RuntimeData(in read))
                            continue;
                        unavailable = read;
                        earliest = default;
                        return false;
                }
                Normalize(intervals);
                intersection = Intersect(intersection, intervals);
                if (intersection.Count == 0)
                {
                    unavailable = read;
                    break;
                }
            }
            earliest = intersection.Count == 0 ? default : intersection[0].Start;
            return intersection.Count != 0;
        }

        static void Normalize(List<TimelineStaticInterval> intervals)
        {
            intervals.Sort((left, right) => left.Start.CompareTo(right.Start));
            int count = 0;
            for (int i = 0; i < intervals.Count; i++)
            {
                TimelineStaticInterval current = intervals[i];
                if (current.Start >= current.End)
                    continue;
                if (count != 0 && current.Start <= intervals[count - 1].End)
                {
                    TimelineStaticInterval previous = intervals[count - 1];
                    intervals[count - 1] = new TimelineStaticInterval(previous.Start,
                        OrchestrationTime.Max(previous.End, current.End));
                }
                else
                    intervals[count++] = current;
            }
            intervals.RemoveRange(count, intervals.Count - count);
        }

        static List<TimelineStaticInterval> Intersect(List<TimelineStaticInterval> left, List<TimelineStaticInterval> right)
        {
            var result = new List<TimelineStaticInterval>();
            int i = 0;
            int j = 0;
            while (i < left.Count && j < right.Count)
            {
                OrchestrationTime start = OrchestrationTime.Max(left[i].Start, right[j].Start);
                OrchestrationTime end = OrchestrationTime.Min(left[i].End, right[j].End);
                if (start < end)
                    result.Add(new TimelineStaticInterval(start, end));
                if (left[i].End <= right[j].End)
                    i++;
                else
                    j++;
            }
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
