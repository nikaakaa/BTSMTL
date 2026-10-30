using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    // Timeline 逻辑推进：只读内容镜像、候选状态与草稿视图，托管宿主与 Burst 宿主共用。
    public readonly ref struct OrchestrationTimelineRuntime<TTime, TClock>
        where TTime : unmanaged, IEquatable<TTime>
        where TClock : struct, IOrchestrationTimelineClock<TTime>
    {
        readonly OrchestrationTimelineContentView<TTime> m_Content;
        readonly OrchestrationTimelineStateRegions<OrchestrationTimelinePlaybackState<TTime>> m_Layout;
        readonly OrchestrationTimelinePreparedCapacity m_Capacity;
        readonly OrchestrationTimelineWorkspaceView<TTime> m_Workspace;

        public OrchestrationTimelineRuntime(OrchestrationTimelineContentView<TTime> content,
            OrchestrationTimelineStateRegions<OrchestrationTimelinePlaybackState<TTime>> layout,
            in OrchestrationTimelinePreparedCapacity capacity, OrchestrationTimelineWorkspaceView<TTime> workspace)
        {
            m_Content = content;
            m_Layout = layout;
            m_Capacity = capacity;
            m_Workspace = workspace;
        }

        public OrchestrationTimelineContentView<TTime> Content => m_Content;
        public OrchestrationTimelineWorkspaceView<TTime> Workspace => m_Workspace;

        public int FindPlayback(OrchestrationStateView candidate, ulong playbackIdentity)
        {
            Span<OrchestrationTimelinePlaybackState<TTime>> playbacks = candidate.Get(m_Layout.Playbacks);
            for (int i = 0; i < playbacks.Length; i++)
                if (playbacks[i].Start.PlaybackIdentity == playbackIdentity)
                    return i;
            throw new InvalidOperationException("Timeline playback is not in the candidate.");
        }

        public int FindFreePlayback(OrchestrationStateView candidate)
        {
            Span<OrchestrationTimelinePlaybackState<TTime>> playbacks = candidate.Get(m_Layout.Playbacks);
            for (int i = 0; i < playbacks.Length; i++)
                if (playbacks[i].Progress.State == OrchestrationTimelinePlaybackStatus.Empty)
                    return i;
            throw new InvalidOperationException("Timeline playbacks exceed the prepared candidate capacity.");
        }

        public ulong AllocatePlaybackIdentity(OrchestrationStateView candidate) =>
            checked(++candidate.Get(m_Layout.Sequences)[0].NextPlaybackIdentity);

        public ref OrchestrationTimelinePlaybackState<TTime> Playback(OrchestrationStateView candidate, int slot) =>
            ref candidate.Get(m_Layout.Playbacks)[slot];

        public void ReleasePlayback(OrchestrationStateView candidate, ulong playbackIdentity)
        {
            int slot = FindPlayback(candidate, playbackIdentity);
            TreeExitRequests(candidate, slot).Clear();
            TreeExitFirstCycles(candidate, slot).Clear();
            TreeExitBits(candidate, slot).Clear();
            Playback(candidate, slot) = default;
        }

        public int CopyActiveTrees(OrchestrationStateView candidate, ulong playbackIdentity,
            Span<TimelineSchedulingBoundary<TTime>> destination)
        {
            int slot = FindPlayback(candidate, playbackIdentity);
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            RequireContent(in playback.Start);
            return OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(m_Content, in playback,
                TreeExits(candidate, slot).View, destination);
        }

        public void Start(OrchestrationStateView candidate, int slot,
            in OrchestrationTimelineStart<TTime> start)
        {
            RequireContent(in start);
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            playback = default;
            playback.Start = start;
            playback.LastConsumedTick = start.StartTick == 0 ? 0 : start.StartTick - 1;
            playback.Progress.CursorTime = start.StartTime;
            playback.Progress.Control.Rate = default(TClock).One;
            playback.Progress.InitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Pending;
            playback.Progress.State = OrchestrationTimelinePlaybackStatus.Running;
            TreeExits(candidate, slot).Clear();
        }

        public OrchestrationTimelineBatch<TTime> BeginAdvance(OrchestrationStateView candidate,
            int slot, OrchestrationTimelineStep<TTime> step)
        {
            TClock clock = default;
            OrchestrationTimelineContentView<TTime> content = m_Content;
            OrchestrationTimelineWorkspaceView<TTime> workspace = m_Workspace;
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            RequireContent(in playback.Start);
            ref OrchestrationTimelineOperationWork<TTime> work = ref workspace.Operations[slot];
            work = default;
            work.Proposed = playback;
            work.Proposed.LastConsumedTick = step.Tick;
            work.Proposed.LastConsumedClockTime = step.ClockTime;
            work.Proposed.InvocationSequence = checked(playback.InvocationSequence + 1);
            work.BatchSequence = workspace.NextBatchSequence();
            work.Tick = step.Tick;
            work.SourceIdentityId = step.SourceIdentityId;
            work.Paused = step.Paused || clock.IsZero(step.Rate);
            TTime previous = playback.Progress.CursorTime;
            int previousCycle = playback.Progress.Cycle;
            work.PreviousTime = previous;
            work.PreviousCycle = previousCycle;
            int carry;
            TTime target;
            switch (step.ClockSource)
            {
                case OrchestrationTimelineClockSource.FixedTicks:
                    int tickCount = checked((int)checked(step.Tick - playback.LastConsumedTick));
                    target = TimelineRuntimeScheduling<TTime, TClock>.Target(previous, previousCycle,
                        playback.Progress.TimeCarry, content.Duration, content.TickRate, tickCount,
                        step.Rate, work.Paused, out carry);
                    break;
                case OrchestrationTimelineClockSource.ElapsedTime:
                    TTime previousClockTime = playback.InvocationSequence == 0
                        ? step.PreviousClockTime : playback.LastConsumedClockTime;
                    target = TimelineRuntimeScheduling<TTime, TClock>.TargetElapsed(previous, previousCycle,
                        playback.Progress.TimeCarry, content.Duration, clock.Subtract(step.ClockTime, previousClockTime),
                        step.Rate, work.Paused, out carry);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(step.ClockSource));
            }
            TTime time;
            int cycle = previousCycle;
            bool loop = content.Mode == OrchestrationTimelinePlaybackMode.Loop;
            if (work.Paused)
            {
                time = previous;
                carry = playback.Progress.TimeCarry;
            }
            else if (loop && clock.GreaterThan(content.Duration, clock.Zero))
            {
                long duration = clock.Raw(content.Duration);
                long raw = clock.Raw(target);
                cycle = checked(cycle + (int)(raw / duration));
                time = clock.FromRaw(raw % duration);
            }
            else if (content.Mode == OrchestrationTimelinePlaybackMode.HoldLastFrame)
            {
                TTime last = clock.FromRaw(Math.Max(0L, clock.Raw(content.Duration) - 1L));
                time = clock.Min(target, last);
                if (clock.Equal(time, last))
                    carry = 0;
            }
            else
                time = clock.Min(target, content.Duration);
            if (cycle < previousCycle || cycle - previousCycle > OrchestrationTimelineLimits.MaximumCycleAdvance ||
                cycle == previousCycle && clock.GreaterThan(previous, time))
                throw new InvalidOperationException("Timeline interval has an invalid traversal range.");
            work.Proposed.Progress.CursorTime = time;
            work.Proposed.Progress.Cycle = cycle;
            work.Proposed.Progress.TimeCarry = carry;
            work.Proposed.Progress.Control.Rate = step.Rate;
            work.Proposed.Progress.Control.Paused = step.Paused;
            if (!work.Paused && playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending)
                work.Proposed.Progress.InitialBoundaryState =
                    cycle > previousCycle || clock.GreaterThan(time, previous)
                        ? OrchestrationTimelineInitialBoundaryState.Applied
                        : OrchestrationTimelineInitialBoundaryState.Skipped;

            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = content.Clips;
            TimelineRuntimeBoundaryIndexView<TTime> boundaries = content.Boundaries;
            TimelineTreeExitView exits = TreeExits(candidate, slot).View;
            ReadOnlySpan<OrchestrationTimelineClipOccurrence> pending = TreeExitRequests(candidate, slot).Slice(0, playback.TreeExitRequestCount);
            CopyExitState(candidate, slot);
            TimelineTreeExitState evaluationExits = EvaluationExits(slot);
            if (cycle > 0 && previousCycle == 0 &&
                playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Skipped)
                foreach (int ordinal in boundaries.Decisions)
                    if (clock.LessThanOrEqual(clips[ordinal].Start, playback.Start.StartTime))
                        evaluationExits.SetFirstCycle(ordinal, 1);
            if (!work.Paused)
                work.InjectedExitCount = pending.Length;
            int boundaryOffset = checked(slot * workspace.BoundaryCapacity);
            Span<TimelineSchedulingBoundary<TTime>> active = workspace.ActiveTrees.Slice(
                checked(slot * m_Capacity.ActiveTreesPerPlayback), m_Capacity.ActiveTreesPerPlayback);
            int activeCount = OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(content, in playback, exits, active);

            Span<TimelineSchedulingBoundary<TTime>> boundaryOutput = workspace.Boundaries.Slice(boundaryOffset, workspace.BoundaryCapacity);
            int boundaryCount = work.Paused ? 0 : TimelineRuntimeScheduling<TTime, TClock>.Boundaries(boundaries,
                previous, previousCycle, time, cycle, content.Duration, loop,
                playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending, exits, pending,
                boundaryOutput,
                workspace.SyntheticBoundaries.Slice(checked(slot * workspace.SyntheticBoundaryCapacity), workspace.SyntheticBoundaryCapacity));
            foreach (ref readonly TimelineSchedulingBoundary<TTime> boundary in boundaryOutput.Slice(0, boundaryCount))
                if (!boundary.Enter && clips[boundary.ClipOrdinal].TreeDecision)
                    evaluationExits.SetExited(boundary.ClipOrdinal, boundary.Cycle);
            TimelineTreeExitView consumed = evaluationExits.View;
            int invocationOffset = checked(slot * workspace.InvocationCapacity);
            Span<TimelineScheduledInvocation<TTime>> scheduled = workspace.Scheduled;
            TTime requestedAdvance = clock.Subtract(target, previous);
            work.RequestedAdvance = requestedAdvance;
            work.MarkerCount = TimelineRuntimeScheduling<TTime, TClock>.Markers(content.Markers, previous, previousCycle,
                time, cycle, content.Duration,
                !work.Paused && playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending,
                scheduled.Slice(invocationOffset, workspace.InvocationCapacity));
            work.ScheduledTreeOffset = checked(invocationOffset + work.MarkerCount);
            int clipOffset = checked(slot * workspace.ClipCapacity);
            work.ScheduledTreeCount = TimelineRuntimeScheduling<TTime, TClock>.Trees(clips,
                boundaryOutput.Slice(0, boundaryCount), active.Slice(0, activeCount),
                previous, previousCycle, time, cycle, content.Duration,
                loop && !work.Paused, consumed,
                workspace.SelectedTrees.Slice(clipOffset, workspace.ClipCapacity),
                workspace.SelectedTreeOrdinals.Slice(clipOffset, workspace.ClipCapacity),
                ref workspace.SelectedTreeCounts[slot],
                scheduled.Slice(work.ScheduledTreeOffset));
            work.ScheduledTreeCount += TimelineRuntimeScheduling<TTime, TClock>.ContinueTrees(clips, active.Slice(0, activeCount),
                previous, previousCycle, time, cycle, content.Duration, consumed,
                scheduled.Slice(work.ScheduledTreeOffset + work.ScheduledTreeCount,
                    workspace.InvocationCapacity - work.MarkerCount - work.ScheduledTreeCount));
            TimelineRuntimeScheduling<TTime, TClock>.SortTrees(scheduled.Slice(work.ScheduledTreeOffset, work.ScheduledTreeCount));
            ReadOnlySpan<TimelineScheduledInvocation<TTime>> treeRequests = scheduled.Slice(
                work.ScheduledTreeOffset, work.ScheduledTreeCount);
            for (int i = 0; i < work.MarkerCount; i++)
                AppendInvocation(slot, in work.Proposed.Start, in scheduled[invocationOffset + i], ref work.InvocationCount);
            for (int i = 0; i < treeRequests.Length; i++)
            {
                ref readonly TimelineScheduledInvocation<TTime> request = ref treeRequests[i];
                if (request.Hook != OrchestrationTimelineHook.Root ||
                    TimelineRuntimeScheduling<TTime, TClock>.IsActive(request.Ordinal, request.Cycle,
                        request.Time, request.Hook, treeRequests, active.Slice(0, activeCount)))
                    AppendInvocation(slot, in work.Proposed.Start, in request, ref work.InvocationCount);
            }
            if (!work.Paused && content.Mode == OrchestrationTimelinePlaybackMode.Once &&
                clock.LessThanOrEqual(content.Duration, time))
            {
                bool hasUnexitedDecision = false;
                int position = boundaries.FindNext<TClock>(0, time, true);
                while (position >= 0)
                {
                    int ordinal = boundaries.StableOrder[position];
                    if (clips[ordinal].TreeDecision && TimelineRuntimeScheduling<TTime, TClock>.Active(
                        in clips[ordinal], ordinal, time, cycle, consumed))
                    {
                        hasUnexitedDecision = true;
                        break;
                    }
                    position = boundaries.FindNext<TClock>(position + 1, time, true);
                }
                if (!hasUnexitedDecision)
                    work.Proposed.Progress.State = OrchestrationTimelinePlaybackStatus.Completed;
            }
            return Batch(slot);
        }

        public OrchestrationTimelineBatch<TTime> BeginStop(OrchestrationStateView candidate,
            int slot, OrchestrationTimelineStopContext context, bool force)
        {
            TClock clock = default;
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            RequireContent(in playback.Start);
            ref OrchestrationTimelineOperationWork<TTime> work = ref m_Workspace.Operations[slot];
            work = default;
            work.Proposed = playback;
            work.Proposed.InvocationSequence = checked(playback.InvocationSequence + 1);
            work.BatchSequence = m_Workspace.NextBatchSequence();
            work.Tick = context.Tick;
            work.SourceIdentityId = context.SourceIdentityId;
            work.PreviousTime = playback.Progress.CursorTime;
            work.PreviousCycle = playback.Progress.Cycle;
            work.Stop = true;
            work.Proposed.Progress.StopContext = context;
            work.Proposed.Progress.HasStopContext = true;
            work.Proposed.Progress.State = OrchestrationTimelinePlaybackStatus.Stopped;
            CopyExitState(candidate, slot);
            if (context.Cause != (uint)OrchestrationStopCause.Shutdown)
            {
                Span<TimelineSchedulingBoundary<TTime>> active = m_Workspace.ActiveTrees.Slice(
                    checked(slot * m_Capacity.ActiveTreesPerPlayback), m_Capacity.ActiveTreesPerPlayback);
                int activeCount = OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(m_Content, in playback,
                    TreeExits(candidate, slot).View, active);
                ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = m_Content.Clips;
                for (int i = 0; i < activeCount; i++)
                {
                    TimelineSchedulingBoundary<TTime> branch = active[i];
                    var request = new TimelineScheduledInvocation<TTime>(branch.ClipOrdinal,
                        clips[branch.ClipOrdinal].SortOrdinal, false,
                        OrchestrationTimelineHook.Destroy, clock.Zero, clock.Zero, clock.Zero,
                        branch.Cycle);
                    AppendInvocation(slot, in playback.Start, in request, ref work.InvocationCount);
                }
            }
            return Batch(slot);
        }

        public bool TreeClipExit(OrchestrationStateView candidate, in OrchestrationTimelineInvocationToken token)
        {
            OrchestrationTimelineInvocationToken current = m_Workspace.CurrentInvocation.Token;
            if (token.PlaybackSlot != current.PlaybackSlot || token.Sequence != current.Sequence ||
                token.PlaybackIdentity != current.PlaybackIdentity || token.ClipOrdinal != current.ClipOrdinal ||
                token.Cycle != current.Cycle || token.Marker != current.Marker)
                throw new InvalidOperationException("Timeline exit must use the current invocation token.");
            if (token.Marker)
                return false;
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, token.PlaybackSlot);
            RequireContent(in playback.Start);
            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = m_Content.Clips;
            if (!clips[token.ClipOrdinal].TreeDecision)
                return false;
            var exit = new OrchestrationTimelineClipOccurrence { ClipOrdinal = token.ClipOrdinal, Cycle = token.Cycle };
            TimelineTreeExitState exits = EvaluationExits(token.PlaybackSlot);
            ref OrchestrationTimelineOperationWork<TTime> work = ref m_Workspace.Operations[token.PlaybackSlot];
            Span<OrchestrationTimelineClipOccurrence> produced = m_Workspace.ProducedExits.Slice(
                checked(token.PlaybackSlot * m_Capacity.TreeExitRequestsPerPlayback), m_Capacity.TreeExitRequestsPerPlayback);
            if (exits.View.Requested(exit.ClipOrdinal, exit.Cycle))
                return false;
            exits.SetPending(exit.ClipOrdinal, exit.Cycle);
            TimelineRuntimeScheduling<TTime, TClock>.InsertOccurrence(clips,
                produced, ref work.ProducedExitCount, in exit);
            return true;
        }

        public void Complete(OrchestrationStateView candidate, int slot, in OrchestrationTimelineBatch<TTime> batch)
        {
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            ref OrchestrationTimelineOperationWork<TTime> work = ref m_Workspace.Operations[slot];
            if (work.BatchSequence != batch.Sequence || slot != batch.PlaybackSlot)
                throw new InvalidOperationException("Timeline completion must use its current batch.");
            Span<OrchestrationTimelineClipOccurrence> pending = TreeExitRequests(candidate, slot);
            if (!work.Stop)
            {
                pending.Slice(work.InjectedExitCount, work.Proposed.TreeExitRequestCount - work.InjectedExitCount).CopyTo(pending);
                work.Proposed.TreeExitRequestCount -= work.InjectedExitCount;
            }
            ReadOnlySpan<OrchestrationTimelineClipOccurrence> produced = m_Workspace.ProducedExits.Slice(
                checked(slot * m_Capacity.TreeExitRequestsPerPlayback), work.ProducedExitCount);
            RequireContent(in playback.Start);
            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = m_Content.Clips;
            foreach (ref readonly OrchestrationTimelineClipOccurrence occurrence in produced)
                TimelineRuntimeScheduling<TTime, TClock>.InsertOccurrence(clips,
                    pending, ref work.Proposed.TreeExitRequestCount, in occurrence);
            m_Workspace.EvaluationFirstCycles.Slice(checked(slot * m_Capacity.TreeExitWindowsPerPlayback),
                m_Capacity.TreeExitWindowsPerPlayback).CopyTo(TreeExitFirstCycles(candidate, slot));
            m_Workspace.EvaluationExitBits.Slice(checked(slot * m_Capacity.TreeExitWordsPerPlayback),
                m_Capacity.TreeExitWordsPerPlayback).CopyTo(TreeExitBits(candidate, slot));
            playback = work.Proposed;
        }

        public static TTime ReadTime(in OrchestrationTimelineInvocation<TTime> invocation) => invocation.Time;

        Span<OrchestrationTimelineClipOccurrence> TreeExitRequests(OrchestrationStateView candidate, int slot) =>
            candidate.Get(m_Layout.TreeExitRequests).Slice(checked(slot * m_Capacity.TreeExitRequestsPerPlayback), m_Capacity.TreeExitRequestsPerPlayback);

        Span<long> TreeExitFirstCycles(OrchestrationStateView candidate, int slot) =>
            candidate.Get(m_Layout.TreeExitFirstCycles).Slice(checked(slot * m_Capacity.TreeExitWindowsPerPlayback), m_Capacity.TreeExitWindowsPerPlayback);

        Span<ulong> TreeExitBits(OrchestrationStateView candidate, int slot) =>
            candidate.Get(m_Layout.TreeExitBits).Slice(checked(slot * m_Capacity.TreeExitWordsPerPlayback), m_Capacity.TreeExitWordsPerPlayback);

        TimelineTreeExitState TreeExits(OrchestrationStateView candidate, int slot) =>
            new(m_Content.TreeCycleWindows, TreeExitFirstCycles(candidate, slot), TreeExitBits(candidate, slot));

        TimelineTreeExitState EvaluationExits(int slot) =>
            new(m_Content.TreeCycleWindows,
                m_Workspace.EvaluationFirstCycles.Slice(checked(slot * m_Capacity.TreeExitWindowsPerPlayback), m_Capacity.TreeExitWindowsPerPlayback),
                m_Workspace.EvaluationExitBits.Slice(checked(slot * m_Capacity.TreeExitWordsPerPlayback), m_Capacity.TreeExitWordsPerPlayback));

        void CopyExitState(OrchestrationStateView candidate, int slot)
        {
            TreeExitFirstCycles(candidate, slot).CopyTo(m_Workspace.EvaluationFirstCycles.Slice(
                checked(slot * m_Capacity.TreeExitWindowsPerPlayback), m_Capacity.TreeExitWindowsPerPlayback));
            TreeExitBits(candidate, slot).CopyTo(m_Workspace.EvaluationExitBits.Slice(
                checked(slot * m_Capacity.TreeExitWordsPerPlayback), m_Capacity.TreeExitWordsPerPlayback));
        }

        void RequireContent(in OrchestrationTimelineStart<TTime> start)
        {
            if (!m_Content.Matches(in start))
                throw new InvalidOperationException("Timeline content is not prepared for this playback.");
        }

        OrchestrationTimelineBatch<TTime> Batch(int slot)
        {
            ref OrchestrationTimelineOperationWork<TTime> work = ref m_Workspace.Operations[slot];
            return new OrchestrationTimelineBatch<TTime>(slot, work.BatchSequence, work.Proposed.Progress.State,
                m_Workspace.Invocations.Slice(checked(slot * m_Workspace.InvocationCapacity), work.InvocationCount));
        }

        void AppendInvocation(int slot, in OrchestrationTimelineStart<TTime> start,
            in TimelineScheduledInvocation<TTime> request, ref int count)
        {
            OrchestrationExecutionEntry entry = request.Marker
                ? m_Content.MarkerEntries[request.Ordinal]
                : m_Content.ClipEntries[request.Ordinal].Entry(request.Hook);
            ref OrchestrationTimelineOperationWork<TTime> work = ref m_Workspace.Operations[slot];
            var token = new OrchestrationTimelineInvocationToken(slot, work.Proposed.InvocationSequence,
                start.PlaybackIdentity, request.Ordinal, request.Cycle, request.Marker);
            TClock clock = default;
            TTime duration = m_Content.Duration;
            TTime startFraction = work.Stop ? clock.Zero : TimelineRuntimeScheduling<TTime, TClock>.Fraction(
                work.PreviousTime, work.RequestedAdvance, work.PreviousCycle,
                duration, request.PreviousTime, request.Cycle);
            TTime endFraction = work.Stop ? clock.Zero :
                clock.IsZero(work.RequestedAdvance) && request.Hook == OrchestrationTimelineHook.Root
                    ? clock.One : TimelineRuntimeScheduling<TTime, TClock>.Fraction(work.PreviousTime,
                        work.RequestedAdvance, work.PreviousCycle, duration, request.Time, request.Cycle);
            m_Workspace.Invocations[checked(slot * m_Workspace.InvocationCapacity + count++)] =
                new OrchestrationTimelineInvocation<TTime>(token, entry, request.Hook,
                    work.Tick, work.SourceIdentityId,
                    request.Time, request.PreviousTime, request.WindowStartTime,
                    startFraction, endFraction);
        }
    }

    internal static class OrchestrationTimelineStateQueries<TTime>
        where TTime : unmanaged, IEquatable<TTime>
    {
        internal static bool IsActive(in OrchestrationTimelinePlaybackState<TTime> playback) =>
            playback.Progress.State == OrchestrationTimelinePlaybackStatus.Running ||
            playback.Progress.State == OrchestrationTimelinePlaybackStatus.Stopping;

        internal static OrchestrationTimelinePreparedContent<TTime> RequireContent(
            ReadOnlySpan<OrchestrationTimelinePreparedContent<TTime>> contents,
            in OrchestrationTimelineStart<TTime> start)
        {
            foreach (OrchestrationTimelinePreparedContent<TTime> content in contents)
                if (content.ContentIdentityId == start.ContentIdentityId &&
                    content.ContentRevisionId == start.ContentRevisionId && content.Mode == start.Mode)
                    return content;
            throw new InvalidOperationException($"Timeline content '{start.ContentIdentityId}/{start.ContentRevisionId}/{start.Mode}' is not prepared.");
        }

        internal static bool IsActiveClip<TClock>(OrchestrationTimelineContentView<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback,
            TimelineTreeExitView exits, int ordinal)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (!IsActive(in playback))
                return false;
            ref readonly TimelineSchedulingClip<TTime> clip = ref content.Clips[ordinal];
            return TimelineRuntimeScheduling<TTime, TClock>.Active(in clip, ordinal,
                playback.Progress.CursorTime, playback.Progress.Cycle, exits);
        }

        internal static int CopyActiveTrees<TClock>(OrchestrationTimelineContentView<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback,
            TimelineTreeExitView exits,
            Span<TimelineSchedulingBoundary<TTime>> destination)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (!HasActiveTrees<TClock>(content, in playback))
                return 0;
            int count = 0;
            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = content.Clips;
            TimelineRuntimeBoundaryIndexView<TTime> boundaries = content.Boundaries;
            long firstCycle = playback.Progress.Cycle;
            foreach (int ordinal in boundaries.Decisions)
            {
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                if (TryTreeCycles<TClock>(in clip, ordinal, in playback, exits,
                        out long first, out _))
                    firstCycle = Math.Min(firstCycle, Math.Max(first, exits.FirstCycle(ordinal)));
            }
            TClock clock = default;
            for (long cycle = firstCycle; cycle <= playback.Progress.Cycle; cycle++)
            {
                TTime time = cycle == playback.Progress.Cycle ? playback.Progress.CursorTime : content.Duration;
                if (cycle == 0)
                {
                    int position = boundaries.FindNext<TClock>(0, time, true);
                    while (position >= 0)
                    {
                        int ordinal = boundaries.StableOrder[position];
                        if (clock.LessThanOrEqual(clips[ordinal].Start, playback.Start.StartTime))
                            AppendActiveTree<TClock>(clips, in playback, exits, ordinal, 0,
                                playback.Start.StartTime, destination, ref count);
                        position = boundaries.FindNext<TClock>(position + 1, time, true);
                    }
                }
                int start = cycle == 0 ? boundaries.FindStart<TClock>(playback.Start.StartTime, false) : 0;
                int index = boundaries.FindNextStart<TClock>(start, time);
                while (index >= 0)
                {
                    int ordinal = boundaries.Starts[index];
                    ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                    AppendActiveTree<TClock>(clips, in playback, exits, ordinal, (int)cycle,
                        clip.Start, destination, ref count);
                    index = boundaries.FindNextStart<TClock>(index + 1, time);
                }
            }
            return count;
        }

        static void AppendActiveTree<TClock>(ReadOnlySpan<TimelineSchedulingClip<TTime>> clips,
            in OrchestrationTimelinePlaybackState<TTime> playback, TimelineTreeExitView exits,
            int ordinal, int cycle, TTime start, Span<TimelineSchedulingBoundary<TTime>> destination, ref int count)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
            if (TryTreeCycles<TClock>(in clip, ordinal, in playback, exits,
                    out long first, out long last) && first <= cycle && cycle <= last &&
                (!clip.TreeDecision || !exits.Exited(ordinal, cycle)))
                destination[count++] = new TimelineSchedulingBoundary<TTime>(ordinal, clip.SortOrdinal, start, cycle, true);
        }

        static bool HasActiveTrees<TClock>(OrchestrationTimelineContentView<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback)
            where TClock : struct, IOrchestrationTimelineClock<TTime> =>
            IsActive(in playback) &&
            playback.Progress.InitialBoundaryState != OrchestrationTimelineInitialBoundaryState.Pending &&
            default(TClock).GreaterThan(content.Duration, default(TClock).Zero);

        static bool TryTreeCycles<TClock>(in TimelineSchedulingClip<TTime> clip, int ordinal,
            in OrchestrationTimelinePlaybackState<TTime> playback,
            TimelineTreeExitView exits, out long firstCycle, out long lastCycle)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            firstCycle = playback.Progress.Cycle;
            lastCycle = firstCycle;
            TClock clock = default;
            bool skippedInitial = playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Skipped &&
                clock.LessThanOrEqual(clip.Start, playback.Start.StartTime);
            if (!clip.TreeDecision)
                return (!skippedInitial || playback.Progress.Cycle > 0) &&
                    TimelineRuntimeScheduling<TTime, TClock>.Active(in clip, ordinal,
                        playback.Progress.CursorTime, playback.Progress.Cycle, exits);
            firstCycle = skippedInitial ? 1 : 0;
            if (!clock.LessThanOrEqual(clip.Start, playback.Progress.CursorTime))
                lastCycle--;
            return firstCycle <= lastCycle;
        }
    }
}
