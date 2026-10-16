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
            TreeOrdinals = treeOrdinals;
            Markers = markers;
            Sections = sections;
        }

        public TimelineExecutionDomain Domain { get; }
        public ReadOnlyMemory<TimelineSchedulingClip<OrchestrationTime>> Clips { get; }
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
        static readonly Comparison<TimelineSchedulingBoundary<TTime>> m_BoundaryComparison;
        readonly Comparison<TimelineScheduledInvocation<TTime>> m_InvocationComparison = CompareInvocations;

        static TimelineRuntimeScheduling() => m_BoundaryComparison = CompareBoundaries;

        public static TTime Target(TTime cursor, int cycle, int carry, TTime duration,
            int tickRate, int tickCount, TTime rate, bool paused, out int nextCarry)
        {
            TClock clock = default;
            long numerator = checked(carry + (long)tickCount * (paused ? 0L : clock.Raw(rate)));
            long parityOffset = (cycle & 1) * (clock.Raw(duration) & 1);
            nextCarry = checked((int)(numerator % tickRate));
            long targetRaw = checked(clock.Raw(cursor) + (numerator - nextCarry) / tickRate);
            long doubledRemainder = Math.Abs((long)nextCarry) * 2;
            if (doubledRemainder > tickRate ||
                doubledRemainder == tickRate && ((targetRaw ^ parityOffset) & 1) != 0)
            {
                int direction = Math.Sign(nextCarry);
                targetRaw = checked(targetRaw + direction);
                nextCarry -= direction * tickRate;
            }
            return clock.FromRaw(targetRaw);
        }

        public int Boundaries(ReadOnlySpan<TimelineSchedulingClip<TTime>> clips,
            TTime previousTime, int previousCycle, TTime nextTime, int nextCycle,
            TTime duration, bool loop, bool initialBoundary,
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exited,
            ReadOnlySpan<OrchestrationTimelineClipOccurrence> pending,
            TimelineSchedulingBoundary<TTime>[] output, int offset)
        {
            TClock clock = default;
            if (!clock.GreaterThan(duration, clock.Zero))
                return 0;
            if (loop && nextCycle - previousCycle > OrchestrationTimelineLimits.MaximumCycleAdvance)
                throw new InvalidOperationException("Timeline playback crossed more than 20 cycles in one Advance.");
            Span<TimelineSchedulingBoundary<TTime>> result = output.AsSpan(offset);
            int count = 0;
            for (int i = 0; i < pending.Length; i++)
            {
                OrchestrationTimelineClipOccurrence exit = pending[i];
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[exit.ClipOrdinal];
                if (clip.Enabled && clip.TreeDecision)
                    result[count++] = new TimelineSchedulingBoundary<TTime>(exit.ClipOrdinal,
                        clip.SortOrdinal, clock.Add(nextTime, clock.Multiply(duration,
                            clock.FromInt32(nextCycle - exit.Cycle))), exit.Cycle, false);
            }
            TTime previousAbsolute = clock.Add(clock.Multiply(duration, clock.FromInt32(previousCycle)), previousTime);
            TTime nextAbsolute = clock.Add(clock.Multiply(duration, clock.FromInt32(nextCycle)), nextTime);
            if (clock.GreaterThan(nextAbsolute, previousAbsolute))
            {
                if (initialBoundary)
                    for (int i = 0; i < clips.Length; i++)
                    {
                        ref readonly TimelineSchedulingClip<TTime> clip = ref clips[i];
                        if (clip.Enabled && clock.GreaterThan(previousTime, clip.Start) &&
                            (clip.TreeDecision || clock.GreaterThan(clip.End, previousTime)))
                            result[count++] = new TimelineSchedulingBoundary<TTime>(i,
                                clip.SortOrdinal, previousTime, previousCycle, true);
                    }
                int firstCycle = loop ? previousCycle : 0;
                int lastCycle = loop ? nextCycle : 0;
                for (int cycle = firstCycle; cycle <= lastCycle; cycle++)
                {
                    TTime from = cycle == previousCycle ? previousTime : clock.Zero;
                    TTime to = cycle == nextCycle ? nextTime : duration;
                    bool includeStart = initialBoundary || cycle > previousCycle;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        ref readonly TimelineSchedulingClip<TTime> clip = ref clips[i];
                        if (!clip.Enabled)
                            continue;
                        if ((clock.GreaterThan(clip.Start, from) || includeStart && clock.Equal(clip.Start, from)) &&
                            clock.LessThanOrEqual(clip.Start, to) &&
                            (!clip.TreeDecision || !Contains(exited, i, cycle) && !Contains(pending, i, cycle)))
                            result[count++] = new TimelineSchedulingBoundary<TTime>(i,
                                clip.SortOrdinal, clip.Start, cycle, true);
                        if (!clip.TreeDecision &&
                            (clock.GreaterThan(clip.End, from) || cycle > previousCycle && clock.Equal(clip.End, from)) &&
                            clock.LessThanOrEqual(clip.End, to))
                            result[count++] = new TimelineSchedulingBoundary<TTime>(i,
                                clip.SortOrdinal, clip.End, cycle, false);
                    }
                }
            }
            SortBoundaries(output.AsSpan(offset, count));
            return count;
        }

        public static void SortBoundaries(Span<TimelineSchedulingBoundary<TTime>> values) =>
            SimulationArraySort.Sort(values, m_BoundaryComparison);

        public int Markers(ReadOnlySpan<TimelineSchedulingMarker<TTime>> markers,
            TTime previousTime, int previousCycle, TTime nextTime, int nextCycle,
            TTime duration, bool includeStart,
            Span<TimelineScheduledInvocation<TTime>> output)
        {
            TClock clock = default;
            if (!clock.GreaterThan(duration, clock.Zero))
                return 0;
            int count = 0;
            for (int cycle = previousCycle; cycle <= nextCycle; cycle++)
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
                        marker.Time, cycle);
                }
            }
            return count;
        }

        public int Trees(ReadOnlySpan<TimelineSchedulingClip<TTime>> clips,
            ReadOnlySpan<int> treeOrdinals,
            ReadOnlySpan<TimelineSchedulingBoundary<TTime>> boundaries,
            ReadOnlySpan<bool> previousActiveClips,
            TTime previousTime, int previousCycle, TTime nextTime, int nextCycle,
            TTime duration, bool loop,
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exited,
            Span<bool> selected, TimelineScheduledInvocation<TTime>[] output, int offset)
        {
            TClock clock = default;
            int count = 0;
            selected.Clear();
            for (int i = 0; i < clips.Length; i++)
                selected[i] = clips[i].Tree && previousActiveClips[i];
            Span<TimelineScheduledInvocation<TTime>> result = output.AsSpan(offset);
            for (int i = 0; i < boundaries.Length; i++)
            {
                ref readonly TimelineSchedulingBoundary<TTime> boundary = ref boundaries[i];
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[boundary.ClipOrdinal];
                if (!clip.Tree)
                    continue;
                if (boundary.Enter)
                    selected[boundary.ClipOrdinal] = true;
                OrchestrationTimelineHook hook = boundary.Enter
                    ? OrchestrationTimelineHook.Enable : OrchestrationTimelineHook.Disable;
                result[count++] = Invocation(boundary.ClipOrdinal, in clip, hook,
                    boundary.Time, boundary.Time, boundary.Cycle);
            }
            int segments = loop && clock.GreaterThan(duration, clock.Zero)
                ? nextCycle - previousCycle + 1 : 1;
            for (int treeIndex = 0; treeIndex < treeOrdinals.Length; treeIndex++)
            {
                int clipIndex = treeOrdinals[treeIndex];
                if (!selected[clipIndex])
                    continue;
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[clipIndex];
                for (int segment = 0; segment < segments; segment++)
                {
                    int cycle = segments == 1 ? nextCycle : previousCycle + segment;
                    TTime from = segment == 0 ? previousTime : clock.Zero;
                    TTime to = segment == segments - 1 ? nextTime : duration;
                    if (clip.TreeDecision)
                        to = clock.Add(nextTime, clock.Multiply(duration, clock.FromInt32(nextCycle - cycle)));
                    if (Contains(exited, clipIndex, cycle) || clock.LessThanOrEqual(to, clip.Start))
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

        internal int ContinueTrees(ReadOnlySpan<TimelineSchedulingClip<TTime>> clips,
            ReadOnlySpan<TimelineSchedulingBoundary<TTime>> previousActive,
            TTime previousTime, int previousCycle, TTime nextTime, int nextCycle, TTime duration,
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exited,
            Span<TimelineScheduledInvocation<TTime>> output)
        {
            TClock clock = default;
            int count = 0;
            foreach (ref readonly TimelineSchedulingBoundary<TTime> occurrence in previousActive)
            {
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[occurrence.ClipOrdinal];
                if (!clip.TreeDecision || occurrence.Cycle >= previousCycle ||
                    Contains(exited, occurrence.ClipOrdinal, occurrence.Cycle))
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

        public static bool Contains(ReadOnlySpan<OrchestrationTimelineClipOccurrence> values,
            int clipOrdinal, int cycle)
        {
            for (int i = 0; i < values.Length; i++)
                if (values[i].ClipOrdinal == clipOrdinal && values[i].Cycle == cycle)
                    return true;
            return false;
        }

        public static bool Contains(ReadOnlySpan<OrchestrationTimelineTreeExitRange> values,
            int clipOrdinal, int cycle)
        {
            int lower = 0;
            int upper = values.Length;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                ref readonly OrchestrationTimelineTreeExitRange value = ref values[middle];
                if (value.ClipOrdinal < clipOrdinal || value.ClipOrdinal == clipOrdinal && value.LastCycle < cycle)
                    lower = middle + 1;
                else if (value.ClipOrdinal > clipOrdinal || value.FirstCycle > cycle)
                    upper = middle;
                else
                    return true;
            }
            return false;
        }

        public static bool Active(in TimelineSchedulingClip<TTime> clip, int ordinal,
            TTime time, int cycle, ReadOnlySpan<OrchestrationTimelineTreeExitRange> exited)
        {
            TClock clock = default;
            return clip.Enabled && clock.LessThanOrEqual(clip.Start, time) &&
                (clip.TreeDecision ? !Contains(exited, ordinal, cycle) : clock.GreaterThan(clip.End, time));
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
