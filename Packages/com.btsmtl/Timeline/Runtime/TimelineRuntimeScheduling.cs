using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct TimelineSchedulingClip<TTime> where TTime : unmanaged
    {
        public TimelineSchedulingClip(int sortOrdinal, TTime start, TTime end,
            bool enabled, bool tree, bool treeDecision, bool declaredEnd)
        {
            SortOrdinal = sortOrdinal;
            Start = start;
            End = end;
            Enabled = enabled;
            Tree = tree;
            TreeDecision = treeDecision;
            DeclaredEnd = declaredEnd;
        }

        public int SortOrdinal { get; }
        public TTime Start { get; }
        public TTime End { get; }
        public bool Enabled { get; }
        public bool Tree { get; }
        public bool TreeDecision { get; }
        public bool DeclaredEnd { get; }
    }

    public readonly struct TimelineSchedulingSection<TTime> where TTime : unmanaged
    {
        public TimelineSchedulingSection(int sectionIndex, TTime time)
        {
            SectionIndex = sectionIndex;
            Time = time;
        }

        public int SectionIndex { get; }
        public TTime Time { get; }
    }

    public readonly struct TimelineSchedulingMarker<TTime> where TTime : unmanaged
    {
        public TimelineSchedulingMarker(int ordinal, TTime time)
        {
            Ordinal = ordinal;
            Time = time;
        }

        public int Ordinal { get; }
        public TTime Time { get; }
    }

    public sealed class TimelineRuntimeSchedulingContent
    {
        internal TimelineRuntimeSchedulingContent(TimelineExecutionDomain domain,
            ReadOnlyMemory<TimelineSchedulingClip<OrchestrationTime>> clips,
            ReadOnlyMemory<int> treeOrdinals,
            ReadOnlyMemory<TimelineSchedulingMarker<OrchestrationTime>> markers,
            ReadOnlyMemory<TimelineSchedulingSection<OrchestrationTime>> sections)
        {
            Domain = domain;
            Clips = clips;
            Boundaries = TimelineRuntimeBoundaryIndex<OrchestrationTime>.Prepare<OrchestrationTimeClock>(clips, true);
            Lifetimes = TimelineRuntimeBoundaryIndex<OrchestrationTime>.Prepare<OrchestrationTimeClock>(clips, false);
            TreeOrdinals = treeOrdinals;
            Markers = markers;
            Sections = sections;
        }

        public TimelineExecutionDomain Domain { get; }
        public ReadOnlyMemory<TimelineSchedulingClip<OrchestrationTime>> Clips { get; }
        public TimelineRuntimeBoundaryIndex<OrchestrationTime> Boundaries { get; }
        public TimelineRuntimeBoundaryIndex<OrchestrationTime> Lifetimes { get; }
        public ReadOnlyMemory<int> TreeOrdinals { get; }
        public ReadOnlyMemory<TimelineSchedulingMarker<OrchestrationTime>> Markers { get; }
        public ReadOnlyMemory<TimelineSchedulingSection<OrchestrationTime>> Sections { get; }
    }

    public readonly struct TimelineSchedulingBoundary<TTime> where TTime : unmanaged
    {
        public TimelineSchedulingBoundary(int clipOrdinal, int sortOrdinal,
            TTime time, int cycle, bool enter)
        {
            ClipOrdinal = clipOrdinal;
            SortOrdinal = sortOrdinal;
            Time = time;
            Cycle = cycle;
            Enter = enter;
        }

        public int ClipOrdinal { get; }
        public int SortOrdinal { get; }
        public TTime Time { get; }
        public int Cycle { get; }
        public bool Enter { get; }
    }

    public readonly struct TimelineScheduledInvocation<TTime> where TTime : unmanaged
    {
        public TimelineScheduledInvocation(int ordinal, int sortOrdinal, bool marker,
            OrchestrationTimelineHook hook, TTime time, TTime previousTime,
            TTime windowStartTime, int cycle)
        {
            Ordinal = ordinal;
            SortOrdinal = sortOrdinal;
            Marker = marker;
            Hook = hook;
            Time = time;
            PreviousTime = previousTime;
            WindowStartTime = windowStartTime;
            Cycle = cycle;
        }

        public int Ordinal { get; }
        public int SortOrdinal { get; }
        public bool Marker { get; }
        public OrchestrationTimelineHook Hook { get; }
        public TTime Time { get; }
        public TTime PreviousTime { get; }
        public TTime WindowStartTime { get; }
        public int Cycle { get; }
    }

    public sealed class TimelineRuntimeScheduling<TTime, TClock>
        where TTime : unmanaged
        where TClock : struct, IOrchestrationTimelineClock<TTime>
    {
        readonly Comparison<TimelineScheduledInvocation<TTime>> m_InvocationComparison = CompareInvocations;

        public static TTime Target(TTime cursor, int cycle, int carry, TTime duration,
            int tickRate, int tickCount, TTime rate, bool paused, out int nextCarry)
        {
            TClock clock = default;
            long numerator = checked(carry + (long)tickCount * (paused ? 0L : clock.Raw(rate)));
            return Target(cursor, cycle, duration, numerator, tickRate, out nextCarry);
        }

        public static TTime TargetElapsed(TTime cursor, int cycle, int carry, TTime duration,
            TTime elapsed, TTime rate, bool paused, out int nextCarry)
        {
            TClock clock = default;
            long numerator = checked(carry + clock.Raw(elapsed) * (paused ? 0L : clock.Raw(rate)));
            return Target(cursor, cycle, duration, numerator, checked((int)clock.Raw(clock.One)), out nextCarry);
        }

        static TTime Target(TTime cursor, int cycle, TTime duration, long numerator,
            int denominator, out int nextCarry)
        {
            TClock clock = default;
            long parityOffset = (cycle & 1) * (clock.Raw(duration) & 1);
            nextCarry = checked((int)(numerator % denominator));
            long targetRaw = checked(clock.Raw(cursor) + (numerator - nextCarry) / denominator);
            long doubledRemainder = Math.Abs((long)nextCarry) * 2;
            if (doubledRemainder > denominator ||
                doubledRemainder == denominator && ((targetRaw ^ parityOffset) & 1) != 0)
            {
                int direction = Math.Sign(nextCarry);
                targetRaw = checked(targetRaw + direction);
                nextCarry -= direction * denominator;
            }
            return clock.FromRaw(targetRaw);
        }

        public int Boundaries(TimelineRuntimeBoundaryIndex<TTime> index,
            TTime previousTime, int previousCycle, TTime nextTime, int nextCycle,
            TTime duration, bool loop, bool initialBoundary,
            TimelineTreeExitView exited,
            ReadOnlySpan<OrchestrationTimelineClipOccurrence> pending,
            Span<TimelineSchedulingBoundary<TTime>> output,
            Span<TimelineSchedulingBoundary<TTime>> synthetic)
        {
            TClock clock = default;
            if (!clock.GreaterThan(duration, clock.Zero))
                return 0;
            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = index.Clips.Span;
            TTime previousAbsolute = clock.Add(clock.Multiply(duration, clock.FromInt32(previousCycle)), previousTime);
            TTime nextAbsolute = clock.Add(clock.Multiply(duration, clock.FromInt32(nextCycle)), nextTime);
            bool advances = clock.GreaterThan(nextAbsolute, previousAbsolute);
            int initialCount = 0;
            if (advances && initialBoundary)
            {
                int position = index.FindNext<TClock>(0, previousTime, true);
                while (position >= 0)
                {
                    int ordinal = index.StableOrder.Span[position];
                    ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                    if (clock.GreaterThan(previousTime, clip.Start))
                        synthetic[initialCount++] = new TimelineSchedulingBoundary<TTime>(ordinal,
                            clip.SortOrdinal, previousTime, previousCycle, true);
                    position = index.FindNext<TClock>(position + 1, previousTime, true);
                }
            }
            var ordinary = new BoundaryCursor(index, previousTime, previousCycle, nextTime, nextCycle,
                duration, loop, initialBoundary, advances, exited);
            bool hasOrdinary = ordinary.MoveNext();
            int pendingIndex = 0;
            int initialIndex = 0;
            int count = 0;
            while (pendingIndex < pending.Length || initialIndex < initialCount || hasOrdinary)
            {
                int stream = -1;
                TimelineSchedulingBoundary<TTime> next = default;
                if (pendingIndex < pending.Length)
                {
                    OrchestrationTimelineClipOccurrence occurrence = pending[pendingIndex];
                    next = new TimelineSchedulingBoundary<TTime>(occurrence.ClipOrdinal,
                        clips[occurrence.ClipOrdinal].SortOrdinal, clock.Add(nextTime, clock.Multiply(duration,
                            clock.FromInt32(nextCycle - occurrence.Cycle))), occurrence.Cycle, false);
                    stream = 0;
                }
                if (initialIndex < initialCount)
                    SelectNext(in synthetic[initialIndex], 1, ref next, ref stream);
                if (hasOrdinary)
                    SelectNext(in ordinary.Current, 2, ref next, ref stream);
                output[count++] = next;
                switch (stream)
                {
                    case 0: pendingIndex++; break;
                    case 1: initialIndex++; break;
                    case 2: hasOrdinary = ordinary.MoveNext(); break;
                }
            }
            return count;
        }

        static void SelectNext(in TimelineSchedulingBoundary<TTime> candidate, int candidateStream,
            ref TimelineSchedulingBoundary<TTime> selected, ref int stream)
        {
            if (stream < 0 || CompareBoundaries(candidate, selected) < 0)
            {
                selected = candidate;
                stream = candidateStream;
            }
        }

        ref struct BoundaryCursor
        {
            readonly TimelineRuntimeBoundaryIndex<TTime> m_Index;
            readonly TTime m_Previous;
            readonly TTime m_Next;
            readonly TTime m_Duration;
            readonly int m_PreviousCycle;
            readonly int m_NextCycle;
            readonly int m_LastCycle;
            readonly bool m_Initial;
            readonly TimelineTreeExitView m_Exits;
            long m_Cycle;
            int m_Start;
            int m_End;
            public TimelineSchedulingBoundary<TTime> Current;

            internal BoundaryCursor(TimelineRuntimeBoundaryIndex<TTime> index,
                TTime previous, int previousCycle, TTime next, int nextCycle, TTime duration,
                bool loop, bool initial, bool advances, TimelineTreeExitView exits)
            {
                m_Index = index;
                m_Previous = previous;
                m_Next = next;
                m_Duration = duration;
                m_PreviousCycle = previousCycle;
                m_NextCycle = nextCycle;
                m_LastCycle = advances ? loop ? nextCycle : 0 : -1;
                m_Initial = initial;
                m_Exits = exits;
                m_Cycle = loop ? previousCycle : 0;
                m_Start = 0;
                m_End = 0;
                Current = default;
                Locate();
            }

            void Locate()
            {
                TTime from = m_Cycle == m_PreviousCycle ? m_Previous : default(TClock).Zero;
                m_Start = m_Index.FindStart<TClock>(from, m_Initial || m_Cycle > m_PreviousCycle);
                m_End = m_Index.FindEnd<TClock>(from, m_Cycle > m_PreviousCycle);
            }

            internal bool MoveNext()
            {
                TClock clock = default;
                ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = m_Index.Clips.Span;
                while (m_Cycle <= m_LastCycle)
                {
                    TTime to = m_Cycle == m_NextCycle ? m_Next : m_Duration;
                    while (m_Start < m_Index.Starts.Length)
                    {
                        int ordinal = m_Index.Starts.Span[m_Start];
                        ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                        if (clock.GreaterThan(clip.Start, to))
                            break;
                        if (!clip.TreeDecision || !m_Exits.Requested(ordinal, m_Cycle))
                            break;
                        m_Start++;
                    }
                    bool start = m_Start < m_Index.Starts.Length &&
                        clock.LessThanOrEqual(clips[m_Index.Starts.Span[m_Start]].Start, to);
                    bool end = m_End < m_Index.Ends.Length &&
                        clock.LessThanOrEqual(clips[m_Index.Ends.Span[m_End]].End, to);
                    if (end && (!start || clock.LessThanOrEqual(clips[m_Index.Ends.Span[m_End]].End,
                            clips[m_Index.Starts.Span[m_Start]].Start)))
                    {
                        int ordinal = m_Index.Ends.Span[m_End++];
                        ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                        Current = new TimelineSchedulingBoundary<TTime>(ordinal, clip.SortOrdinal, clip.End, (int)m_Cycle, false);
                        return true;
                    }
                    if (start)
                    {
                        int ordinal = m_Index.Starts.Span[m_Start++];
                        ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                        Current = new TimelineSchedulingBoundary<TTime>(ordinal, clip.SortOrdinal, clip.Start, (int)m_Cycle, true);
                        return true;
                    }
                    m_Cycle++;
                    if (m_Cycle <= m_LastCycle)
                        Locate();
                }
                return false;
            }
        }

        public static int SectionAt(ReadOnlySpan<TimelineSchedulingSection<TTime>> sections, TTime time)
        {
            TClock clock = default;
            int lower = 0;
            int upper = sections.Length;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                if (clock.LessThanOrEqual(sections[middle].Time, time))
                    lower = middle + 1;
                else
                    upper = middle;
            }
            return lower - 1;
        }

        public int Markers(ReadOnlySpan<TimelineSchedulingMarker<TTime>> markers,
            TTime previousTime, int previousCycle, TTime nextTime, int nextCycle,
            TTime duration, bool includeStart,
            Span<TimelineScheduledInvocation<TTime>> output)
        {
            TClock clock = default;
            if (!clock.GreaterThan(duration, clock.Zero))
                return 0;
            int count = 0;
            for (long cycle = previousCycle; cycle <= nextCycle; cycle++)
            {
                TTime from = cycle == previousCycle ? previousTime : clock.Zero;
                TTime to = cycle == nextCycle ? nextTime : duration;
                bool include = includeStart || cycle > previousCycle;
                for (int i = 0; i < markers.Length; i++)
                {
                    ref readonly TimelineSchedulingMarker<TTime> marker = ref markers[i];
                    if ((!clock.GreaterThan(marker.Time, from) && !(include && clock.Equal(marker.Time, from))) ||
                        clock.GreaterThan(marker.Time, to))
                        continue;
                    output[count++] = new TimelineScheduledInvocation<TTime>(marker.Ordinal, i,
                        true, OrchestrationTimelineHook.Enable, marker.Time, marker.Time,
                        marker.Time, (int)cycle);
                }
            }
            return count;
        }

        public int Trees(ReadOnlySpan<TimelineSchedulingClip<TTime>> clips,
            ReadOnlySpan<TimelineSchedulingBoundary<TTime>> boundaries,
            ReadOnlySpan<TimelineSchedulingBoundary<TTime>> previousActive,
            TTime previousTime, int previousCycle, TTime nextTime, int nextCycle,
            TTime duration, bool loop,
            TimelineTreeExitView exited,
            Span<bool> selected, Span<int> selectedOrdinals, ref int selectedCount,
            TimelineScheduledInvocation<TTime>[] output, int offset)
        {
            TClock clock = default;
            int count = 0;
            for (int i = 0; i < selectedCount; i++)
                selected[selectedOrdinals[i]] = false;
            selectedCount = 0;
            foreach (ref readonly TimelineSchedulingBoundary<TTime> occurrence in previousActive)
                if (occurrence.Cycle == previousCycle)
                    SelectTree(occurrence.ClipOrdinal, selected, selectedOrdinals, ref selectedCount);
            Span<TimelineScheduledInvocation<TTime>> result = output.AsSpan(offset);
            for (int i = 0; i < boundaries.Length; i++)
            {
                ref readonly TimelineSchedulingBoundary<TTime> boundary = ref boundaries[i];
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[boundary.ClipOrdinal];
                if (boundary.Enter)
                    SelectTree(boundary.ClipOrdinal, selected, selectedOrdinals, ref selectedCount);
                OrchestrationTimelineHook hook = boundary.Enter
                    ? OrchestrationTimelineHook.Enable : OrchestrationTimelineHook.Disable;
                result[count++] = Invocation(boundary.ClipOrdinal, in clip, hook,
                    boundary.Time, boundary.Time, boundary.Cycle);
            }
            int segments = loop && clock.GreaterThan(duration, clock.Zero)
                ? nextCycle - previousCycle + 1 : 1;
            for (int treeIndex = 0; treeIndex < selectedCount; treeIndex++)
            {
                int clipIndex = selectedOrdinals[treeIndex];
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[clipIndex];
                for (int segment = 0; segment < segments; segment++)
                {
                    int cycle = segments == 1 ? nextCycle : previousCycle + segment;
                    TTime from = segment == 0 ? previousTime : clock.Zero;
                    TTime to = segment == segments - 1 ? nextTime : duration;
                    if (clip.TreeDecision)
                        to = clock.Add(nextTime, clock.Multiply(duration, clock.FromInt32(nextCycle - cycle)));
                    if (clip.TreeDecision && exited.Exited(clipIndex, cycle) || clock.LessThanOrEqual(to, clip.Start))
                        continue;
                    bool finite = clip.TreeDecision && clip.DeclaredEnd;
                    if (finite && clock.LessThanOrEqual(clip.End, from) ||
                        !clip.TreeDecision && clock.LessThanOrEqual(clip.End, to))
                        continue;
                    TTime sample = finite ? clock.Min(to, clip.End) : to;
                    TTime samplePrevious = clock.Max(from, clip.Start);
                    result[count++] = Invocation(clipIndex, in clip, OrchestrationTimelineHook.Root,
                        sample, samplePrevious, cycle);
                }
            }
            return count;
        }

        static void SelectTree(int ordinal, Span<bool> selected, Span<int> ordinals, ref int count)
        {
            if (!selected[ordinal])
            {
                selected[ordinal] = true;
                ordinals[count++] = ordinal;
            }
        }

        internal int ContinueTrees(ReadOnlySpan<TimelineSchedulingClip<TTime>> clips,
            ReadOnlySpan<TimelineSchedulingBoundary<TTime>> previousActive,
            TTime previousTime, int previousCycle, TTime nextTime, int nextCycle, TTime duration,
            TimelineTreeExitView exited,
            Span<TimelineScheduledInvocation<TTime>> output)
        {
            TClock clock = default;
            int count = 0;
            foreach (ref readonly TimelineSchedulingBoundary<TTime> occurrence in previousActive)
            {
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[occurrence.ClipOrdinal];
                if (!clip.TreeDecision || occurrence.Cycle >= previousCycle ||
                    exited.Exited(occurrence.ClipOrdinal, occurrence.Cycle))
                    continue;
                TTime from = clock.Add(previousTime, clock.Multiply(duration,
                    clock.FromInt32(previousCycle - occurrence.Cycle)));
                TTime to = clock.Add(nextTime, clock.Multiply(duration,
                    clock.FromInt32(nextCycle - occurrence.Cycle)));
                if (clip.DeclaredEnd && clock.LessThanOrEqual(clip.End, from))
                    continue;
                TTime sample = clip.DeclaredEnd ? clock.Min(to, clip.End) : to;
                output[count++] = Invocation(occurrence.ClipOrdinal, in clip, OrchestrationTimelineHook.Root,
                    sample, clock.Max(from, clip.Start), occurrence.Cycle);
            }
            return count;
        }

        public void SortTrees(TimelineScheduledInvocation<TTime>[] output, int offset, int count) =>
            SimulationArraySort.Sort(output.AsSpan(offset, count), m_InvocationComparison);

        public static bool IsActive(int ordinal, int cycle, TTime time, OrchestrationTimelineHook hook,
            ReadOnlySpan<TimelineScheduledInvocation<TTime>> requests,
            ReadOnlySpan<TimelineSchedulingBoundary<TTime>> previousActiveTrees)
        {
            TClock clock = default;
            for (int i = requests.Length - 1; i >= 0; i--)
            {
                ref readonly TimelineScheduledInvocation<TTime> candidate = ref requests[i];
                if (clock.GreaterThan(candidate.Time, time) ||
                    clock.Equal(candidate.Time, time) && Order(candidate.Hook) > Order(hook))
                    continue;
                if (candidate.Hook != OrchestrationTimelineHook.Root &&
                    candidate.Ordinal == ordinal && candidate.Cycle == cycle)
                    return candidate.Hook == OrchestrationTimelineHook.Enable;
            }
            foreach (ref readonly TimelineSchedulingBoundary<TTime> active in previousActiveTrees)
                if (active.ClipOrdinal == ordinal && active.Cycle == cycle)
                    return true;
            return false;
        }

        public static void InsertOccurrence(ReadOnlySpan<TimelineSchedulingClip<TTime>> clips,
            Span<OrchestrationTimelineClipOccurrence> values, ref int count,
            in OrchestrationTimelineClipOccurrence occurrence)
        {
            int lower = 0;
            int upper = count;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                ref readonly OrchestrationTimelineClipOccurrence value = ref values[middle];
                if (value.Cycle < occurrence.Cycle || value.Cycle == occurrence.Cycle &&
                    clips[value.ClipOrdinal].SortOrdinal < clips[occurrence.ClipOrdinal].SortOrdinal)
                    lower = middle + 1;
                else
                    upper = middle;
            }
            values.Slice(lower, count - lower).CopyTo(values.Slice(lower + 1));
            values[lower] = occurrence;
            count++;
        }

        public static bool Active(in TimelineSchedulingClip<TTime> clip, int ordinal,
            TTime time, int cycle, TimelineTreeExitView exited)
        {
            TClock clock = default;
            return clip.Enabled && clock.LessThanOrEqual(clip.Start, time) &&
                (clip.TreeDecision ? !exited.Exited(ordinal, cycle) : clock.GreaterThan(clip.End, time));
        }

        static TimelineScheduledInvocation<TTime> Invocation(int ordinal,
            in TimelineSchedulingClip<TTime> clip, OrchestrationTimelineHook hook,
            TTime time, TTime previous, int cycle)
        {
            return new TimelineScheduledInvocation<TTime>(ordinal, clip.SortOrdinal, false,
                hook, time, previous, clip.Start, cycle);
        }

        public static TTime Fraction(TTime previous, TTime advance, int previousCycle,
            TTime duration, TTime time, int cycle)
        {
            TClock clock = default;
            return clock.IsZero(advance) ? clock.Zero : clock.Divide(
                clock.Subtract(clock.Add(time, clock.Multiply(duration,
                    clock.FromInt32(cycle - previousCycle))), previous), advance);
        }

        static int Order(OrchestrationTimelineHook hook) => hook == OrchestrationTimelineHook.Enable
            ? 1 : hook == OrchestrationTimelineHook.Root ? 2 : 3;

        static int CompareBoundaries(TimelineSchedulingBoundary<TTime> left, TimelineSchedulingBoundary<TTime> right)
        {
            int result = left.Cycle.CompareTo(right.Cycle);
            if (result == 0)
                result = default(TClock).Raw(left.Time).CompareTo(default(TClock).Raw(right.Time));
            if (result == 0)
                result = left.Enter.CompareTo(right.Enter);
            return result == 0 ? left.SortOrdinal.CompareTo(right.SortOrdinal) : result;
        }

        static int CompareInvocations(TimelineScheduledInvocation<TTime> left, TimelineScheduledInvocation<TTime> right)
        {
            int result = left.Cycle.CompareTo(right.Cycle);
            if (result == 0)
                result = default(TClock).Raw(left.Time).CompareTo(default(TClock).Raw(right.Time));
            if (result == 0)
                result = Order(left.Hook).CompareTo(Order(right.Hook));
            return result == 0 ? left.SortOrdinal.CompareTo(right.SortOrdinal) : result;
        }
    }
}
