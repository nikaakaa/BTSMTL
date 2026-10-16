using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public sealed class OrchestrationTimelineRuntime<TTime, TClock>
        where TTime : unmanaged, IEquatable<TTime>
        where TClock : struct, IOrchestrationTimelineClock<TTime>
    {
        readonly ReadOnlyMemory<OrchestrationTimelinePreparedContent<TTime>> m_Contents;
        readonly OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<TTime>> m_Layout;
        readonly OrchestrationTimelinePreparedCapacity m_Capacity;
        readonly TimelineRuntimeScheduling<TTime, TClock> m_Scheduler = new TimelineRuntimeScheduling<TTime, TClock>();

        public OrchestrationTimelineRuntime(ReadOnlyMemory<OrchestrationTimelinePreparedContent<TTime>> contents,
            in OrchestrationTimelineStateLayout<OrchestrationTimelinePlaybackState<TTime>> layout,
            in OrchestrationTimelinePreparedCapacity capacity)
        {
            foreach (OrchestrationTimelinePreparedContent<TTime> content in contents.Span)
            {
                RequireCapacity(content.ContentIdentityId, "ActiveTrees scratch",
                    content.RequiredActiveTrees, capacity.ActiveTreesPerPlayback);
                RequireCapacity(content.ContentIdentityId, "TreeExitRequests",
                    content.RequiredTreeExitRequests, capacity.TreeExitRequestsPerPlayback);
                RequireCapacity(content.ContentIdentityId, "TreeExits",
                    content.RequiredTreeExits, capacity.TreeExitsPerPlayback);
            }
            m_Contents = contents;
            m_Layout = layout;
            m_Capacity = capacity;
        }

        public OrchestrationTimelineCommittedView<TTime> Observe(ReadOnlyOrchestrationStateView committed) =>
            new OrchestrationTimelineCommittedView<TTime>(committed, m_Contents, in m_Layout, in m_Capacity);

        public int FindPlayback(OrchestrationStateView candidate, ulong playbackIdentity)
        {
            Span<OrchestrationTimelinePlaybackState<TTime>> playbacks = candidate.Get(m_Layout.Playbacks);
            for (int i = 0; i < playbacks.Length; i++)
                if (playbacks[i].Start.PlaybackIdentity == playbackIdentity)
                    return i;
            throw new InvalidOperationException($"Timeline playback '{playbackIdentity}' is not in the candidate.");
        }

        public int FindFreePlayback(OrchestrationStateView candidate)
        {
            Span<OrchestrationTimelinePlaybackState<TTime>> playbacks = candidate.Get(m_Layout.Playbacks);
            for (int i = 0; i < playbacks.Length; i++)
                if (playbacks[i].Progress.State == OrchestrationTimelinePlaybackStatus.Empty)
                    return i;
            throw new InvalidOperationException("Timeline playbacks exceed the prepared candidate capacity.");
        }

        public ref OrchestrationTimelinePlaybackState<TTime> Playback(OrchestrationStateView candidate, int slot) =>
            ref candidate.Get(m_Layout.Playbacks)[slot];

        public void ReleasePlayback(OrchestrationStateView candidate, ulong playbackIdentity)
        {
            int slot = FindPlayback(candidate, playbackIdentity);
            TreeExitRequests(candidate, slot).Clear();
            TreeExits(candidate, slot).Clear();
            Playback(candidate, slot) = default;
        }

        public int CopyActiveTrees(OrchestrationStateView candidate, ulong playbackIdentity,
            Span<TimelineSchedulingBoundary<TTime>> destination)
        {
            int slot = FindPlayback(candidate, playbackIdentity);
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            return OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(RequireContent(in playback.Start), in playback,
                TreeExits(candidate, slot).Slice(0, playback.TreeExitCount), destination);
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
        }

        public OrchestrationTimelineBatch<TTime> BeginAdvance(OrchestrationStateView candidate,
            int slot, OrchestrationTimelineStep<TTime> step, OrchestrationTimelineWorkspace<TTime> workspace)
        {
            TClock clock = default;
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            OrchestrationTimelinePreparedContent<TTime> content = RequireContent(in playback.Start);
            ref OrchestrationTimelineWorkspace<TTime>.Operation work = ref workspace.Operations[slot];
            work = default;
            work.Proposed = playback;
            int tickCount = checked((int)checked(step.Tick - playback.LastConsumedTick));
            work.Proposed.LastConsumedTick = step.Tick;
            work.Proposed.InvocationSequence = checked(playback.InvocationSequence + 1);
            work.BatchSequence = workspace.NextBatchSequence();
            work.Tick = step.Tick;
            work.SourceIdentityId = step.SourceIdentityId;
            work.Paused = step.Paused || clock.IsZero(step.Rate);
            TTime previous = playback.Progress.CursorTime;
            int previousCycle = playback.Progress.Cycle;
            work.PreviousTime = previous;
            work.PreviousCycle = previousCycle;
            TTime target = TimelineRuntimeScheduling<TTime, TClock>.Target(previous, previousCycle,
                playback.Progress.TimeCarry, content.Duration, content.TickRate, tickCount,
                step.Rate, work.Paused, out int carry);
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

            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = content.Clips.Span;
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exits = TreeExits(candidate, slot).Slice(0, playback.TreeExitCount);
            ReadOnlySpan<OrchestrationTimelineClipOccurrence> pending = TreeExitRequests(candidate, slot).Slice(0, playback.TreeExitRequestCount);
            Span<OrchestrationTimelineTreeExitRange> evaluationExits = workspace.EvaluationExits.AsSpan(
                checked(slot * workspace.EvaluationExitCapacity), workspace.EvaluationExitCapacity);
            exits.CopyTo(evaluationExits);
            work.EvaluationExitCount = exits.Length;
            if (!work.Paused)
            {
                work.InjectedExitCount = pending.Length;
                for (int i = 0; i < pending.Length; i++)
                    AppendExit(evaluationExits, ref work.EvaluationExitCount, pending[i]);
            }
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> consumed = evaluationExits.Slice(0, work.EvaluationExitCount);
            Span<bool> activeClips = workspace.PreviousActiveClips.AsSpan(checked(slot * workspace.ClipCapacity), clips.Length);
            for (int i = 0; i < clips.Length; i++)
                activeClips[i] = TimelineRuntimeScheduling<TTime, TClock>.Active(in clips[i], i,
                    previous, previousCycle, exits);
            Span<TimelineSchedulingBoundary<TTime>> active = workspace.ActiveTrees.AsSpan(
                checked(slot * m_Capacity.ActiveTreesPerPlayback), m_Capacity.ActiveTreesPerPlayback);
            int activeCount = OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(content, in playback, exits, active);

            int boundaryOffset = checked(slot * workspace.BoundaryCapacity);
            int boundaryCount = work.Paused ? 0 : m_Scheduler.Boundaries(clips,
                previous, previousCycle, time, cycle, content.Duration, loop,
                playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending, exits, pending,
                workspace.Boundaries, boundaryOffset);
            int invocationOffset = checked(slot * workspace.InvocationCapacity);
            TTime requestedAdvance = clock.Subtract(target, previous);
            work.RequestedAdvance = requestedAdvance;
            work.MarkerCount = m_Scheduler.Markers(content.Markers.Span, previous, previousCycle,
                time, cycle, content.Duration,
                !work.Paused && playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending,
                workspace.Scheduled.AsSpan(invocationOffset, workspace.InvocationCapacity));
            work.ScheduledTreeOffset = checked(invocationOffset + work.MarkerCount);
            work.ScheduledTreeCount = m_Scheduler.Trees(clips, content.TreeOrdinals.Span,
                workspace.Boundaries.AsSpan(boundaryOffset, boundaryCount), activeClips,
                previous, previousCycle, time, cycle, content.Duration,
                loop && !work.Paused, consumed,
                workspace.SelectedTrees.AsSpan(checked(slot * workspace.ClipCapacity), clips.Length),
                workspace.Scheduled, work.ScheduledTreeOffset);
            work.ScheduledTreeCount += m_Scheduler.ContinueTrees(clips, active.Slice(0, activeCount),
                previous, previousCycle, time, cycle, content.Duration, consumed,
                workspace.Scheduled.AsSpan(work.ScheduledTreeOffset + work.ScheduledTreeCount,
                    workspace.InvocationCapacity - work.MarkerCount - work.ScheduledTreeCount));
            m_Scheduler.SortTrees(workspace.Scheduled, work.ScheduledTreeOffset, work.ScheduledTreeCount);
            ReadOnlySpan<TimelineScheduledInvocation<TTime>> treeRequests = workspace.Scheduled.AsSpan(
                work.ScheduledTreeOffset, work.ScheduledTreeCount);
            for (int i = 0; i < work.MarkerCount; i++)
                AppendInvocation(content, slot, in work.Proposed.Start,
                    in workspace.Scheduled[invocationOffset + i], workspace, ref work.InvocationCount);
            for (int i = 0; i < treeRequests.Length; i++)
            {
                ref readonly TimelineScheduledInvocation<TTime> request = ref treeRequests[i];
                if (request.Hook != OrchestrationTimelineHook.Root ||
                    TimelineRuntimeScheduling<TTime, TClock>.IsActive(request.Ordinal, request.Cycle,
                        request.Time, request.Hook, treeRequests, active.Slice(0, activeCount)))
                    AppendInvocation(content, slot, in work.Proposed.Start,
                        in request, workspace, ref work.InvocationCount);
            }
            bool hasUnexitedDecision = false;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i].TreeDecision && TimelineRuntimeScheduling<TTime, TClock>.Active(
                        in clips[i], i, time, cycle, consumed))
                {
                    hasUnexitedDecision = true;
                    break;
                }
            if (!work.Paused && content.Mode == OrchestrationTimelinePlaybackMode.Once &&
                clock.LessThanOrEqual(content.Duration, time) && !hasUnexitedDecision)
                work.Proposed.Progress.State = OrchestrationTimelinePlaybackStatus.Completed;
            return Batch(slot, workspace);
        }

        public OrchestrationTimelineBatch<TTime> BeginStop(OrchestrationStateView candidate,
            int slot, OrchestrationTimelineStopContext context, bool force,
            OrchestrationTimelineWorkspace<TTime> workspace)
        {
            TClock clock = default;
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            OrchestrationTimelinePreparedContent<TTime> content = RequireContent(in playback.Start);
            ref OrchestrationTimelineWorkspace<TTime>.Operation work = ref workspace.Operations[slot];
            work = default;
            work.Proposed = playback;
            work.Proposed.InvocationSequence = checked(playback.InvocationSequence + 1);
            work.BatchSequence = workspace.NextBatchSequence();
            work.Tick = context.Tick;
            work.SourceIdentityId = context.SourceIdentityId;
            work.PreviousTime = playback.Progress.CursorTime;
            work.PreviousCycle = playback.Progress.Cycle;
            work.Stop = true;
            work.Proposed.Progress.StopContext = context;
            work.Proposed.Progress.HasStopContext = true;
            work.Proposed.Progress.State = OrchestrationTimelinePlaybackStatus.Stopped;
            if (context.Cause != (uint)OrchestrationStopCause.Shutdown)
            {
                Span<TimelineSchedulingBoundary<TTime>> active = workspace.ActiveTrees.AsSpan(
                    checked(slot * m_Capacity.ActiveTreesPerPlayback), m_Capacity.ActiveTreesPerPlayback);
                int activeCount = OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(content, in playback,
                    TreeExits(candidate, slot).Slice(0, playback.TreeExitCount), active);
                for (int i = 0; i < activeCount; i++)
                {
                    TimelineSchedulingBoundary<TTime> branch = active[i];
                    var request = new TimelineScheduledInvocation<TTime>(branch.ClipOrdinal,
                        content.Clips.Span[branch.ClipOrdinal].SortOrdinal, false,
                        OrchestrationTimelineHook.Destroy, clock.Zero, clock.Zero, clock.Zero,
                        branch.Cycle);
                    AppendInvocation(content, slot, in playback.Start,
                        in request, workspace, ref work.InvocationCount);
                }
            }
            return Batch(slot, workspace);
        }

        public bool TreeClipExit(OrchestrationStateView candidate,
            OrchestrationTimelineWorkspace<TTime> workspace, in OrchestrationTimelineInvocationToken token)
        {
            OrchestrationTimelineInvocationToken current = workspace.CurrentInvocation.Token;
            if (token.PlaybackSlot != current.PlaybackSlot || token.Sequence != current.Sequence ||
                token.PlaybackIdentity != current.PlaybackIdentity || token.ClipOrdinal != current.ClipOrdinal ||
                token.Cycle != current.Cycle || token.Marker != current.Marker)
                throw new InvalidOperationException("Timeline exit must use the current invocation token.");
            if (token.Marker)
                return false;
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, token.PlaybackSlot);
            OrchestrationTimelinePreparedContent<TTime> content = RequireContent(in playback.Start);
            if (!content.Clips.Span[token.ClipOrdinal].TreeDecision)
                return false;
            var exit = new OrchestrationTimelineClipOccurrence { ClipOrdinal = token.ClipOrdinal, Cycle = token.Cycle };
            Span<OrchestrationTimelineClipOccurrence> pending = TreeExitRequests(candidate, token.PlaybackSlot);
            Span<OrchestrationTimelineTreeExitRange> exits = TreeExits(candidate, token.PlaybackSlot);
            ref OrchestrationTimelineWorkspace<TTime>.Operation work = ref workspace.Operations[token.PlaybackSlot];
            Span<OrchestrationTimelineClipOccurrence> produced = workspace.ProducedExits.AsSpan(
                checked(token.PlaybackSlot * m_Capacity.TreeExitRequestsPerPlayback), m_Capacity.TreeExitRequestsPerPlayback);
            if (TimelineRuntimeScheduling<TTime, TClock>.Contains(pending.Slice(0, playback.TreeExitRequestCount), exit.ClipOrdinal, exit.Cycle) ||
                TimelineRuntimeScheduling<TTime, TClock>.Contains(exits.Slice(0, playback.TreeExitCount), exit.ClipOrdinal, exit.Cycle) ||
                TimelineRuntimeScheduling<TTime, TClock>.Contains(produced.Slice(0, work.ProducedExitCount), exit.ClipOrdinal, exit.Cycle))
                return false;
            produced[work.ProducedExitCount++] = exit;
            return true;
        }

        public void Complete(OrchestrationStateView candidate, int slot,
            in OrchestrationTimelineBatch<TTime> batch, OrchestrationTimelineWorkspace<TTime> workspace)
        {
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            ref OrchestrationTimelineWorkspace<TTime>.Operation work = ref workspace.Operations[slot];
            if (work.BatchSequence != batch.Sequence || slot != batch.PlaybackSlot)
                throw new InvalidOperationException("Timeline completion must use its current batch.");
            Span<OrchestrationTimelineClipOccurrence> pending = TreeExitRequests(candidate, slot);
            if (!work.Stop)
            {
                workspace.EvaluationExits.AsSpan(checked(slot * workspace.EvaluationExitCapacity),
                    work.EvaluationExitCount).CopyTo(TreeExits(candidate, slot));
                work.Proposed.TreeExitCount = work.EvaluationExitCount;
                pending.Slice(work.InjectedExitCount, work.Proposed.TreeExitRequestCount - work.InjectedExitCount).CopyTo(pending);
                work.Proposed.TreeExitRequestCount -= work.InjectedExitCount;
            }
            ReadOnlySpan<OrchestrationTimelineClipOccurrence> produced = workspace.ProducedExits.AsSpan(
                checked(slot * m_Capacity.TreeExitRequestsPerPlayback), work.ProducedExitCount);
            produced.CopyTo(pending.Slice(work.Proposed.TreeExitRequestCount));
            work.Proposed.TreeExitRequestCount += produced.Length;
            playback = work.Proposed;
        }

        public static TTime ReadTime(in OrchestrationTimelineInvocation<TTime> invocation) => invocation.Time;

        Span<OrchestrationTimelineClipOccurrence> TreeExitRequests(OrchestrationStateView candidate, int slot) =>
            candidate.Get(m_Layout.TreeExitRequests).Slice(checked(slot * m_Capacity.TreeExitRequestsPerPlayback), m_Capacity.TreeExitRequestsPerPlayback);

        Span<OrchestrationTimelineTreeExitRange> TreeExits(OrchestrationStateView candidate, int slot) =>
            candidate.Get(m_Layout.TreeExits).Slice(checked(slot * m_Capacity.TreeExitsPerPlayback), m_Capacity.TreeExitsPerPlayback);

        OrchestrationTimelinePreparedContent<TTime> RequireContent(in OrchestrationTimelineStart<TTime> start)
            => OrchestrationTimelineStateQueries<TTime>.RequireContent(m_Contents.Span, in start);

        static OrchestrationTimelineBatch<TTime> Batch(int slot, OrchestrationTimelineWorkspace<TTime> workspace)
        {
            ref OrchestrationTimelineWorkspace<TTime>.Operation work = ref workspace.Operations[slot];
            return new OrchestrationTimelineBatch<TTime>(slot, work.BatchSequence, work.Proposed.Progress.State,
                workspace.Invocations.AsSpan(checked(slot * workspace.InvocationCapacity), work.InvocationCount));
        }

        static void AppendInvocation(OrchestrationTimelinePreparedContent<TTime> content,
            int slot, in OrchestrationTimelineStart<TTime> start,
            in TimelineScheduledInvocation<TTime> request, OrchestrationTimelineWorkspace<TTime> workspace, ref int count)
        {
            OrchestrationExecutionEntry entry = request.Marker
                ? content.MarkerEntries.Span[request.Ordinal]
                : content.ClipEntries.Span[request.Ordinal].Entry(request.Hook);
            ref OrchestrationTimelineWorkspace<TTime>.Operation work = ref workspace.Operations[slot];
            var token = new OrchestrationTimelineInvocationToken(slot, work.Proposed.InvocationSequence,
                start.PlaybackIdentity, request.Ordinal, request.Cycle, request.Marker);
            TClock clock = default;
            TTime startFraction = work.Stop ? clock.Zero : TimelineRuntimeScheduling<TTime, TClock>.Fraction(
                work.PreviousTime, work.RequestedAdvance, work.PreviousCycle,
                content.Duration, request.PreviousTime, request.Cycle);
            TTime endFraction = work.Stop ? clock.Zero :
                clock.IsZero(work.RequestedAdvance) && request.Hook == OrchestrationTimelineHook.Root
                    ? clock.One : TimelineRuntimeScheduling<TTime, TClock>.Fraction(work.PreviousTime,
                        work.RequestedAdvance, work.PreviousCycle, content.Duration, request.Time, request.Cycle);
            workspace.Invocations[checked(slot * workspace.InvocationCapacity + count++)] =
                new OrchestrationTimelineInvocation<TTime>(token, entry, request.Hook,
                    work.Tick, work.SourceIdentityId,
                    request.Time, request.PreviousTime, request.WindowStartTime,
                    startFraction, endFraction);
        }

        static void AppendExit(Span<OrchestrationTimelineTreeExitRange> values,
            ref int count, OrchestrationTimelineClipOccurrence exit)
        {
            int index = 0;
            while (index < count && (values[index].ClipOrdinal < exit.ClipOrdinal ||
                values[index].ClipOrdinal == exit.ClipOrdinal && (long)values[index].LastCycle + 1 < exit.Cycle))
                index++;
            if (index < count && values[index].ClipOrdinal == exit.ClipOrdinal &&
                values[index].FirstCycle <= (long)exit.Cycle + 1)
            {
                int first = Math.Min(values[index].FirstCycle, exit.Cycle);
                int last = Math.Max(values[index].LastCycle, exit.Cycle);
                int end = index + 1;
                while (end < count && values[end].ClipOrdinal == exit.ClipOrdinal &&
                    values[end].FirstCycle <= (long)last + 1)
                {
                    last = Math.Max(last, values[end].LastCycle);
                    end++;
                }
                values[index] = new OrchestrationTimelineTreeExitRange
                {
                    ClipOrdinal = exit.ClipOrdinal,
                    FirstCycle = first,
                    LastCycle = last
                };
                values.Slice(end, count - end).CopyTo(values.Slice(index + 1));
                count -= end - index - 1;
            }
            else
            {
                values.Slice(index, count - index).CopyTo(values.Slice(index + 1));
                values[index] = new OrchestrationTimelineTreeExitRange
                {
                    ClipOrdinal = exit.ClipOrdinal,
                    FirstCycle = exit.Cycle,
                    LastCycle = exit.Cycle
                };
                count++;
            }
        }

        static void RequireCapacity(int content, string region, int required, int available)
        {
            if (required > available)
                throw new InvalidOperationException($"Timeline '{content}' region '{region}' requires '{required}' elements; prepared capacity is '{available}'.");
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

        internal static bool IsActiveClip<TClock>(OrchestrationTimelinePreparedContent<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback,
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exits, int ordinal)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (!IsActive(in playback))
                return false;
            ref readonly TimelineSchedulingClip<TTime> clip = ref content.Clips.Span[ordinal];
            return TimelineRuntimeScheduling<TTime, TClock>.Active(in clip, ordinal,
                playback.Progress.CursorTime, playback.Progress.Cycle, exits);
        }

        internal static int CopyActiveTrees<TClock>(OrchestrationTimelinePreparedContent<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback,
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exits,
            Span<TimelineSchedulingBoundary<TTime>> destination)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (!HasActiveTrees<TClock>(content, in playback))
                return 0;
            int count = 0;
            int rangeIndex = 0;
            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = content.Clips.Span;
            for (int ordinal = 0; ordinal < clips.Length; ordinal++)
            {
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                if (!TryTreeCycles<TClock>(in clip, ordinal, in playback, exits, out long nextCycle, out long lastCycle))
                    continue;
                if (!clip.TreeDecision)
                {
                    AppendActiveCycles<TClock>(destination, ref count, ordinal, in clip,
                        playback.Start.StartTime, nextCycle, lastCycle);
                    continue;
                }
                while (TryActiveRange(exits, ordinal, ref rangeIndex, ref nextCycle, lastCycle,
                    out long firstActive, out long lastActive))
                    AppendActiveCycles<TClock>(destination, ref count, ordinal, in clip,
                        playback.Start.StartTime, firstActive, lastActive);
            }
            TimelineRuntimeScheduling<TTime, TClock>.SortBoundaries(destination.Slice(0, count));
            return count;
        }

        static bool HasActiveTrees<TClock>(OrchestrationTimelinePreparedContent<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback)
            where TClock : struct, IOrchestrationTimelineClock<TTime> =>
            IsActive(in playback) &&
            playback.Progress.InitialBoundaryState != OrchestrationTimelineInitialBoundaryState.Pending &&
            default(TClock).GreaterThan(content.Duration, default(TClock).Zero);

        static bool TryTreeCycles<TClock>(in TimelineSchedulingClip<TTime> clip, int ordinal,
            in OrchestrationTimelinePlaybackState<TTime> playback,
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exits, out long firstCycle, out long lastCycle)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            firstCycle = playback.Progress.Cycle;
            lastCycle = firstCycle;
            if (!clip.Enabled || !clip.Tree)
                return false;
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

        static bool TryActiveRange(ReadOnlySpan<OrchestrationTimelineTreeExitRange> exits, int ordinal,
            ref int rangeIndex, ref long nextCycle, long lastCycle, out long firstActive, out long lastActive)
        {
            while (rangeIndex < exits.Length && exits[rangeIndex].ClipOrdinal < ordinal)
                rangeIndex++;
            while (nextCycle <= lastCycle && rangeIndex < exits.Length && exits[rangeIndex].ClipOrdinal == ordinal)
            {
                ref readonly OrchestrationTimelineTreeExitRange range = ref exits[rangeIndex++];
                if (range.LastCycle < nextCycle)
                    continue;
                firstActive = nextCycle;
                lastActive = Math.Min(lastCycle, (long)range.FirstCycle - 1);
                nextCycle = Math.Max(nextCycle, (long)range.LastCycle + 1);
                if (firstActive <= lastActive)
                    return true;
            }
            firstActive = nextCycle;
            lastActive = lastCycle;
            nextCycle = lastCycle + 1;
            return firstActive <= lastActive;
        }

        static void AppendActiveCycles<TClock>(Span<TimelineSchedulingBoundary<TTime>> destination,
            ref int count, int ordinal, in TimelineSchedulingClip<TTime> clip,
            TTime startTime, long firstCycle, long lastCycle)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            TClock clock = default;
            for (long cycle = firstCycle; cycle <= lastCycle; cycle++)
                destination[count++] = new TimelineSchedulingBoundary<TTime>(ordinal, clip.SortOrdinal,
                    cycle == 0 ? clock.Max(startTime, clip.Start) : clip.Start, (int)cycle, true);
        }
    }
}
