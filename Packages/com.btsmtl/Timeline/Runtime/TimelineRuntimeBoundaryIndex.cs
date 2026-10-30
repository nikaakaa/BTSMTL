using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    // 准备期建好边界序与区间树；推进与编辑器查询都经 View。
    public sealed class TimelineRuntimeBoundaryIndex<TTime> where TTime : unmanaged
    {
        internal readonly TTime[] MinStarts;
        internal readonly TTime[] MaxEnds;
        internal readonly TTime[] MaxLifetimeEnds;
        internal readonly TTime[] StartMaxLifetimeEnds;

        TimelineRuntimeBoundaryIndex(ReadOnlyMemory<TimelineSchedulingClip<TTime>> clips,
            int[] starts, int[] ends, int[] stableOrder, int[] decisions)
        {
            Clips = clips;
            Starts = starts;
            Ends = ends;
            StableOrder = stableOrder;
            Decisions = decisions;
            MinStarts = new TTime[stableOrder.Length];
            MaxEnds = new TTime[stableOrder.Length];
            MaxLifetimeEnds = new TTime[stableOrder.Length];
            StartMaxLifetimeEnds = new TTime[starts.Length];
        }

        public ReadOnlyMemory<TimelineSchedulingClip<TTime>> Clips { get; }
        public ReadOnlyMemory<int> Starts { get; }
        public ReadOnlyMemory<int> Ends { get; }
        public ReadOnlyMemory<int> StableOrder { get; }
        public ReadOnlyMemory<int> Decisions { get; }

        public TimelineRuntimeBoundaryIndexView<TTime> View => new TimelineRuntimeBoundaryIndexView<TTime>(
            Clips.Span, Starts.Span, Ends.Span, StableOrder.Span, Decisions.Span,
            MinStarts, MaxEnds, MaxLifetimeEnds, StartMaxLifetimeEnds);

        public ReadOnlyMemory<OrchestrationTimelineTreeCycleWindow> PrepareCycleWindows(
            ReadOnlySpan<int> instances, out int wordCount)
        {
            var windows = new OrchestrationTimelineTreeCycleWindow[Clips.Length];
            int stateIndex = 0;
            wordCount = 0;
            foreach (int ordinal in Decisions.Span)
            {
                var window = new OrchestrationTimelineTreeCycleWindow(stateIndex++, wordCount, instances[ordinal]);
                windows[ordinal] = window;
                wordCount = checked(wordCount + window.WordCount);
            }
            return windows;
        }

        internal static TimelineRuntimeBoundaryIndex<TTime> Prepare<TClock>(
            ReadOnlyMemory<TimelineSchedulingClip<TTime>> clips, bool treeBoundaries)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            var starts = new List<int>();
            var ends = new List<int>();
            var decisions = new List<int>();
            for (int ordinal = 0; ordinal < clips.Length; ordinal++)
            {
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips.Span[ordinal];
                if (!clip.Enabled || treeBoundaries && !clip.Tree)
                    continue;
                starts.Add(ordinal);
                if (clip.TreeDecision)
                    decisions.Add(ordinal);
                else
                    ends.Add(ordinal);
            }
            var stable = starts.ToArray();
            if (treeBoundaries)
                Array.Sort(stable, (left, right) => clips.Span[left].SortOrdinal.CompareTo(clips.Span[right].SortOrdinal));
            starts.Sort((left, right) => Compare<TClock>(clips, left, right, true));
            ends.Sort((left, right) => Compare<TClock>(clips, left, right, false));
            decisions.Sort((left, right) => clips.Span[left].SortOrdinal.CompareTo(clips.Span[right].SortOrdinal));
            var result = new TimelineRuntimeBoundaryIndex<TTime>(clips, starts.ToArray(), ends.ToArray(),
                stable, decisions.ToArray());
            result.Build<TClock>(0, stable.Length);
            result.BuildStartEnds<TClock>(0, starts.Count);
            return result;
        }

        static int Compare<TClock>(ReadOnlyMemory<TimelineSchedulingClip<TTime>> clips,
            int left, int right, bool start)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            ref readonly TimelineSchedulingClip<TTime> a = ref clips.Span[left];
            ref readonly TimelineSchedulingClip<TTime> b = ref clips.Span[right];
            int comparison = default(TClock).Raw(start ? a.Start : a.End)
                .CompareTo(default(TClock).Raw(start ? b.Start : b.End));
            return comparison != 0 ? comparison : a.SortOrdinal.CompareTo(b.SortOrdinal);
        }

        void Build<TClock>(int lower, int upper)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (lower == upper)
                return;
            int middle = lower + (upper - lower) / 2;
            Build<TClock>(lower, middle);
            Build<TClock>(middle + 1, upper);
            TClock clock = default;
            ref readonly TimelineSchedulingClip<TTime> clip = ref Clips.Span[StableOrder.Span[middle]];
            TTime start = clip.Start;
            TTime end = clip.End;
            TTime lifetimeEnd = clip.TreeDecision ? clock.FromRaw(long.MaxValue) : end;
            if (lower < middle)
                Include<TClock>(lower + (middle - lower) / 2, ref start, ref end, ref lifetimeEnd);
            if (middle + 1 < upper)
                Include<TClock>(middle + 1 + (upper - middle - 1) / 2, ref start, ref end, ref lifetimeEnd);
            MinStarts[middle] = start;
            MaxEnds[middle] = end;
            MaxLifetimeEnds[middle] = lifetimeEnd;
        }

        void Include<TClock>(int middle, ref TTime start, ref TTime end, ref TTime lifetimeEnd)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            TClock clock = default;
            start = clock.Min(start, MinStarts[middle]);
            end = clock.Max(end, MaxEnds[middle]);
            lifetimeEnd = clock.Max(lifetimeEnd, MaxLifetimeEnds[middle]);
        }

        TTime BuildStartEnds<TClock>(int lower, int upper)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            TClock clock = default;
            if (lower == upper)
                return clock.FromRaw(long.MinValue);
            int middle = lower + (upper - lower) / 2;
            ref readonly TimelineSchedulingClip<TTime> clip = ref Clips.Span[Starts.Span[middle]];
            TTime end = clip.TreeDecision ? clock.FromRaw(long.MaxValue) : clip.End;
            end = clock.Max(end, BuildStartEnds<TClock>(lower, middle));
            end = clock.Max(end, BuildStartEnds<TClock>(middle + 1, upper));
            return StartMaxLifetimeEnds[middle] = end;
        }
    }

    public readonly ref struct TimelineRuntimeBoundaryIndexView<TTime> where TTime : unmanaged
    {
        readonly ReadOnlySpan<TTime> m_MinStarts;
        readonly ReadOnlySpan<TTime> m_MaxEnds;
        readonly ReadOnlySpan<TTime> m_MaxLifetimeEnds;
        readonly ReadOnlySpan<TTime> m_StartMaxLifetimeEnds;

        public TimelineRuntimeBoundaryIndexView(ReadOnlySpan<TimelineSchedulingClip<TTime>> clips,
            ReadOnlySpan<int> starts, ReadOnlySpan<int> ends, ReadOnlySpan<int> stableOrder,
            ReadOnlySpan<int> decisions, ReadOnlySpan<TTime> minStarts, ReadOnlySpan<TTime> maxEnds,
            ReadOnlySpan<TTime> maxLifetimeEnds, ReadOnlySpan<TTime> startMaxLifetimeEnds)
        {
            Clips = clips;
            Starts = starts;
            Ends = ends;
            StableOrder = stableOrder;
            Decisions = decisions;
            m_MinStarts = minStarts;
            m_MaxEnds = maxEnds;
            m_MaxLifetimeEnds = maxLifetimeEnds;
            m_StartMaxLifetimeEnds = startMaxLifetimeEnds;
        }

        public ReadOnlySpan<TimelineSchedulingClip<TTime>> Clips { get; }
        public ReadOnlySpan<int> Starts { get; }
        public ReadOnlySpan<int> Ends { get; }
        public ReadOnlySpan<int> StableOrder { get; }
        public ReadOnlySpan<int> Decisions { get; }

        internal int FindStart<TClock>(TTime time, bool includeEqual)
            where TClock : struct, IOrchestrationTimelineClock<TTime> => Find<TClock>(Starts, time, includeEqual, true);

        internal int FindEnd<TClock>(TTime time, bool includeEqual)
            where TClock : struct, IOrchestrationTimelineClock<TTime> => Find<TClock>(Ends, time, includeEqual, false);

        int Find<TClock>(ReadOnlySpan<int> order, TTime time, bool includeEqual, bool start)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            int lower = 0;
            int upper = order.Length;
            TClock clock = default;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                ref readonly TimelineSchedulingClip<TTime> clip = ref Clips[order[middle]];
                TTime position = start ? clip.Start : clip.End;
                if (clock.GreaterThan(time, position) || !includeEqual && clock.Equal(position, time))
                    lower = middle + 1;
                else
                    upper = middle;
            }
            return lower;
        }

        public int FindNext<TClock>(int after, TTime time, bool lifetime)
            where TClock : struct, IOrchestrationTimelineClock<TTime> =>
            FindNext<TClock>(0, StableOrder.Length, after, time, lifetime);

        int FindNext<TClock>(int lower, int upper, int after, TTime time, bool lifetime)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (lower == upper || upper <= after)
                return -1;
            int middle = lower + (upper - lower) / 2;
            TClock clock = default;
            if (clock.GreaterThan(m_MinStarts[middle], time) ||
                clock.LessThanOrEqual(lifetime ? m_MaxLifetimeEnds[middle] : m_MaxEnds[middle], time))
                return -1;
            int found = FindNext<TClock>(lower, middle, after, time, lifetime);
            if (found >= 0)
                return found;
            if (middle >= after)
            {
                ref readonly TimelineSchedulingClip<TTime> clip = ref Clips[StableOrder[middle]];
                if (clock.LessThanOrEqual(clip.Start, time) &&
                    (lifetime && clip.TreeDecision || clock.GreaterThan(clip.End, time)))
                    return middle;
            }
            return FindNext<TClock>(middle + 1, upper, after, time, lifetime);
        }

        internal int FindNextStart<TClock>(int after, TTime time)
            where TClock : struct, IOrchestrationTimelineClock<TTime> =>
            FindNextStart<TClock>(0, Starts.Length, after, time);

        int FindNextStart<TClock>(int lower, int upper, int after, TTime time)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (lower == upper || upper <= after)
                return -1;
            int middle = lower + (upper - lower) / 2;
            TClock clock = default;
            if (clock.LessThanOrEqual(m_StartMaxLifetimeEnds[middle], time))
                return -1;
            int found = FindNextStart<TClock>(lower, middle, after, time);
            if (found >= 0)
                return found;
            ref readonly TimelineSchedulingClip<TTime> clip = ref Clips[Starts[middle]];
            if (clock.GreaterThan(clip.Start, time))
                return -1;
            if (middle >= after && (clip.TreeDecision || clock.GreaterThan(clip.End, time)))
                return middle;
            return FindNextStart<TClock>(middle + 1, upper, after, time);
        }
    }
}
