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
                RequireCapacity(content.ContentIdentityId, "TreeExitFirstCycles",
                    content.RequiredTreeExitWindows, capacity.TreeExitWindowsPerPlayback);
                RequireCapacity(content.ContentIdentityId, "TreeExitBits",
                    content.RequiredTreeExitWords, capacity.TreeExitWordsPerPlayback);
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
            OrchestrationTimelinePreparedContent<TTime> content = RequireContent(in playback.Start);
            return OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(content, in playback,
                TreeExits(candidate, slot, content).View, destination);
        }

        public void Start(OrchestrationStateView candidate, int slot,
            in OrchestrationTimelineStart<TTime> start)
        {
            OrchestrationTimelinePreparedContent<TTime> content = RequireContent(in start);
            ref OrchestrationTimelinePlaybackState<TTime> playback = ref Playback(candidate, slot);
            playback = default;
            playback.Start = start;
            playback.Section = TimelineRuntimeScheduling<TTime, TClock>.SectionAt(content.Sections.Span, start.StartTime) + 1;
            playback.LastConsumedTick = start.StartTick == 0 ? 0 : start.StartTick - 1;
            playback.Progress.CursorTime = start.StartTime;
            playback.Progress.Control.Rate = default(TClock).One;
            playback.Progress.InitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Pending;
            playback.Progress.State = OrchestrationTimelinePlaybackStatus.Running;
            TimelineTreeExitState exits = TreeExits(candidate, slot, content);
            exits.Clear();
            foreach (int ordinal in content.Boundaries.Decisions.Span)
                if (!TimelineRuntimeScheduling<TTime, TClock>.InitialClipAvailable(
                    in content.Clips.Span[ordinal], content.Sections.Span, start.StartTime))
                    exits.SetFirstCycle(ordinal, 1);
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
            work.Proposed.LastConsumedTick = step.Tick;
            work.Proposed.LastConsumedClockTime = step.ClockTime;
            work.Proposed.InvocationSequence = checked(playback.InvocationSequence + 1);
            work.BatchSequence = workspace.NextBatchSequence();
            work.Tick = step.Tick;
            work.SourceIdentityId = step.SourceIdentityId;
            work.Paused = step.Paused || clock.IsZero(step.Rate);
            ReadOnlySpan<TimelineSchedulingSection<TTime>> sections = content.Sections.Span;
            TTime previous = playback.Progress.CursorTime;
            int previousCycle = playback.Progress.Cycle;
            TTime jumpFrom = previous;
            bool jumped = !work.Paused && playback.PendingSectionJump != 0;
            if (jumped)
            {
                // 跳转像一次回绕：本次推进从目标分段起点开始，被跳过的区间不遍历、不积分。
                previous = sections[playback.PendingSectionJump - 1].Time;
                work.Proposed.PendingSectionJump = 0;
                work.Proposed.SectionJumpCount = checked(playback.SectionJumpCount + 1);
                work.Proposed.SectionJumpTick = step.Tick;
                work.Proposed.SectionJumpFrom = jumpFrom;
                work.Proposed.SectionJumpTo = previous;
            }
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
            bool sectionEnd = false;
            // 可继续分段只能经跳转离开，所以沿用记录的当前分段；其余分段按游标重新定位。
            int section = jumped ? playback.PendingSectionJump - 1 : playback.Section - 1;
            if (section < 0 || !sections[section].Continuable)
                section = TimelineRuntimeScheduling<TTime, TClock>.SectionAt(sections, previous);
            if (!loop && !work.Paused && section >= 0 && sections[section].Continuable)
            {
                // 可继续分段未跳转时在分段终点收招：Once 完成，HoldLastFrame 停在终点前一刻。
                TTime end = content.Mode == OrchestrationTimelinePlaybackMode.HoldLastFrame
                    ? clock.FromRaw(clock.Raw(sections[section].End) - 1L) : sections[section].End;
                if (clock.LessThanOrEqual(end, time))
                {
                    time = end;
                    carry = 0;
                    sectionEnd = content.Mode == OrchestrationTimelinePlaybackMode.Once;
                }
            }
            TTime sectionTime = !loop && content.Mode == OrchestrationTimelinePlaybackMode.Once &&
                clock.Equal(time, content.Duration) && clock.GreaterThan(time, clock.Zero)
                ? clock.FromRaw(clock.Raw(time) - 1L) : time;
            work.Proposed.Section = (section >= 0 && sections[section].Continuable
                ? section : TimelineRuntimeScheduling<TTime, TClock>.SectionAt(sections, sectionTime)) + 1;
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
            if (jumped)
                work.Proposed.Progress.InitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Applied;

            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = content.Clips.Span;
            TimelineTreeExitView exits = TreeExits(candidate, slot, content).View;
            ReadOnlySpan<OrchestrationTimelineClipOccurrence> pending = TreeExitRequests(candidate, slot).Slice(0, playback.TreeExitRequestCount);
            CopyExitState(candidate, slot, workspace);
            TimelineTreeExitState evaluationExits = EvaluationExits(workspace, slot, content);
            if (cycle > 0 && previousCycle == 0 &&
                playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Skipped)
                foreach (int ordinal in content.Boundaries.Decisions.Span)
                    if (clock.LessThanOrEqual(clips[ordinal].Start, playback.Start.StartTime))
                        evaluationExits.SetFirstCycle(ordinal, 1);
            if (!work.Paused)
                work.InjectedExitCount = pending.Length;
            int boundaryOffset = checked(slot * workspace.BoundaryCapacity);
            Span<TimelineSchedulingBoundary<TTime>> active = workspace.ActiveTrees.AsSpan(
                checked(slot * m_Capacity.ActiveTreesPerPlayback), m_Capacity.ActiveTreesPerPlayback);
            int activeCount = OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(content, in playback, exits, active);

            var traversal = new TimelineSchedulingJump<TTime>(jumped, jumpFrom, sectionEnd);
            int boundaryCount = work.Paused ? 0 : m_Scheduler.Boundaries(content.Boundaries, sections, playback.Start.StartTime,
                previous, previousCycle, time, cycle, content.Duration, loop,
                playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending, exits, pending,
                workspace.Boundaries.AsSpan(boundaryOffset, workspace.BoundaryCapacity),
                workspace.SyntheticBoundaries.AsSpan(checked(slot * workspace.SyntheticBoundaryCapacity), workspace.SyntheticBoundaryCapacity),
                in traversal);
            foreach (ref readonly TimelineSchedulingBoundary<TTime> boundary in
                workspace.Boundaries.AsSpan(boundaryOffset, boundaryCount))
                if (!boundary.Enter && clips[boundary.ClipOrdinal].TreeDecision)
                    evaluationExits.SetExited(boundary.ClipOrdinal, boundary.Cycle);
            TimelineTreeExitView consumed = evaluationExits.View;
            int invocationOffset = checked(slot * workspace.InvocationCapacity);
            TTime requestedAdvance = clock.Subtract(target, previous);
            work.RequestedAdvance = requestedAdvance;
            work.MarkerCount = m_Scheduler.Markers(content.Markers.Span, previous, previousCycle,
                time, cycle, content.Duration,
                !work.Paused && (jumped ||
                    playback.Progress.InitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending),
                workspace.Scheduled.AsSpan(invocationOffset, workspace.InvocationCapacity), sectionEnd);
            work.ScheduledTreeOffset = checked(invocationOffset + work.MarkerCount);
            work.ScheduledTreeCount = m_Scheduler.Trees(clips,
                workspace.Boundaries.AsSpan(boundaryOffset, boundaryCount), active.Slice(0, activeCount),
                previous, previousCycle, time, cycle, content.Duration,
                loop && !work.Paused, jumped, consumed,
                workspace.SelectedTrees.AsSpan(checked(slot * workspace.ClipCapacity), workspace.ClipCapacity),
                workspace.SelectedTreeOrdinals.AsSpan(checked(slot * workspace.ClipCapacity), workspace.ClipCapacity),
                ref workspace.SelectedTreeCounts[slot],
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
            if (!work.Paused && content.Mode == OrchestrationTimelinePlaybackMode.Once &&
                (sectionEnd || clock.LessThanOrEqual(content.Duration, time)))
            {
                bool hasUnexitedDecision = false;
                int position = content.Boundaries.FindNext<TClock>(0, time, true);
                while (position >= 0)
                {
                    int ordinal = content.Boundaries.StableOrder.Span[position];
                    if (clips[ordinal].TreeDecision && TimelineRuntimeScheduling<TTime, TClock>.Active(
                        in clips[ordinal], ordinal, time, cycle, consumed, sections, playback.Start.StartTime))
                    {
                        hasUnexitedDecision = true;
                        break;
                    }
                    position = content.Boundaries.FindNext<TClock>(position + 1, time, true);
                }
                if (!hasUnexitedDecision)
                    work.Proposed.Progress.State = OrchestrationTimelinePlaybackStatus.Completed;
            }
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
            CopyExitState(candidate, slot, workspace);
            if (context.Cause != (uint)OrchestrationStopCause.Shutdown)
            {
                Span<TimelineSchedulingBoundary<TTime>> active = workspace.ActiveTrees.AsSpan(
                    checked(slot * m_Capacity.ActiveTreesPerPlayback), m_Capacity.ActiveTreesPerPlayback);
                int activeCount = OrchestrationTimelineStateQueries<TTime>.CopyActiveTrees<TClock>(content, in playback,
                    TreeExits(candidate, slot, content).View, active);
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
            TimelineTreeExitState exits = EvaluationExits(workspace, token.PlaybackSlot, content);
            ref OrchestrationTimelineWorkspace<TTime>.Operation work = ref workspace.Operations[token.PlaybackSlot];
            Span<OrchestrationTimelineClipOccurrence> produced = workspace.ProducedExits.AsSpan(
                checked(token.PlaybackSlot * m_Capacity.TreeExitRequestsPerPlayback), m_Capacity.TreeExitRequestsPerPlayback);
            if (exits.View.Requested(exit.ClipOrdinal, exit.Cycle))
                return false;
            exits.SetPending(exit.ClipOrdinal, exit.Cycle);
            TimelineRuntimeScheduling<TTime, TClock>.InsertOccurrence(content.Clips.Span,
                produced, ref work.ProducedExitCount, in exit);
            return true;
        }

        // 门 Clip 在生效期间请求进入所在分段的下一分段；准备阶段已保证节点只在可继续分段内的门 Clip 入口与 Root 中。
        public void SectionContinue(OrchestrationTimelineWorkspace<TTime> workspace)
        {
            ref readonly OrchestrationTimelineInvocation<TTime> invocation = ref workspace.CurrentInvocation;
            OrchestrationTimelineInvocationToken token = invocation.Token;
            ref OrchestrationTimelineWorkspace<TTime>.Operation work = ref workspace.Operations[token.PlaybackSlot];
            OrchestrationTimelinePreparedContent<TTime> content = RequireContent(in work.Proposed.Start);
            if (token.Marker || work.Stop ||
                invocation.Hook != OrchestrationTimelineHook.Enable && invocation.Hook != OrchestrationTimelineHook.Root)
                throw new InvalidOperationException("进入下一分段只能由生效中的门 Clip 请求。");
            TClock clock = default;
            ref readonly TimelineSchedulingClip<TTime> clip = ref content.Clips.Span[token.ClipOrdinal];
            ReadOnlySpan<TimelineSchedulingSection<TTime>> sections = content.Sections.Span;
            int section = TimelineRuntimeScheduling<TTime, TClock>.SectionAt(sections, clip.Start);
            if (section < 0 || !sections[section].Continuable || clock.GreaterThan(clip.Start, invocation.Time) ||
                clock.GreaterThan(invocation.Time, clip.End))
                throw new InvalidOperationException($"Timeline '{content.ContentIdentityId}' 片段 #{token.ClipOrdinal} 不在可继续分段的生效区间内，不能请求进入下一分段。");
            work.Proposed.PendingSectionJump = sections[section].NextSectionIndex + 1;
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
                pending.Slice(work.InjectedExitCount, work.Proposed.TreeExitRequestCount - work.InjectedExitCount).CopyTo(pending);
                work.Proposed.TreeExitRequestCount -= work.InjectedExitCount;
            }
            ReadOnlySpan<OrchestrationTimelineClipOccurrence> produced = workspace.ProducedExits.AsSpan(
                checked(slot * m_Capacity.TreeExitRequestsPerPlayback), work.ProducedExitCount);
            OrchestrationTimelinePreparedContent<TTime> content = RequireContent(in playback.Start);
            foreach (ref readonly OrchestrationTimelineClipOccurrence occurrence in produced)
                TimelineRuntimeScheduling<TTime, TClock>.InsertOccurrence(content.Clips.Span,
                    pending, ref work.Proposed.TreeExitRequestCount, in occurrence);
            workspace.EvaluationFirstCycles.AsSpan(checked(slot * m_Capacity.TreeExitWindowsPerPlayback),
                m_Capacity.TreeExitWindowsPerPlayback).CopyTo(TreeExitFirstCycles(candidate, slot));
            workspace.EvaluationExitBits.AsSpan(checked(slot * m_Capacity.TreeExitWordsPerPlayback),
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

        TimelineTreeExitState TreeExits(OrchestrationStateView candidate, int slot,
            OrchestrationTimelinePreparedContent<TTime> content) =>
            new(content.TreeCycleWindows.Span, TreeExitFirstCycles(candidate, slot), TreeExitBits(candidate, slot));

        TimelineTreeExitState EvaluationExits(OrchestrationTimelineWorkspace<TTime> workspace,
            int slot, OrchestrationTimelinePreparedContent<TTime> content) =>
            new(content.TreeCycleWindows.Span,
                workspace.EvaluationFirstCycles.AsSpan(checked(slot * m_Capacity.TreeExitWindowsPerPlayback), m_Capacity.TreeExitWindowsPerPlayback),
                workspace.EvaluationExitBits.AsSpan(checked(slot * m_Capacity.TreeExitWordsPerPlayback), m_Capacity.TreeExitWordsPerPlayback));

        void CopyExitState(OrchestrationStateView candidate, int slot, OrchestrationTimelineWorkspace<TTime> workspace)
        {
            TreeExitFirstCycles(candidate, slot).CopyTo(workspace.EvaluationFirstCycles.AsSpan(
                checked(slot * m_Capacity.TreeExitWindowsPerPlayback), m_Capacity.TreeExitWindowsPerPlayback));
            TreeExitBits(candidate, slot).CopyTo(workspace.EvaluationExitBits.AsSpan(
                checked(slot * m_Capacity.TreeExitWordsPerPlayback), m_Capacity.TreeExitWordsPerPlayback));
        }

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
            TimelineTreeExitView exits, int ordinal)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (!IsActive(in playback))
                return false;
            ref readonly TimelineSchedulingClip<TTime> clip = ref content.Clips.Span[ordinal];
            return TimelineRuntimeScheduling<TTime, TClock>.Active(in clip, ordinal,
                playback.Progress.CursorTime, playback.Progress.Cycle, exits,
                content.Sections.Span, playback.Start.StartTime);
        }

        internal static int CopyActiveTrees<TClock>(OrchestrationTimelinePreparedContent<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback,
            TimelineTreeExitView exits,
            Span<TimelineSchedulingBoundary<TTime>> destination)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            if (!HasActiveTrees<TClock>(content, in playback))
                return 0;
            int count = 0;
            ReadOnlySpan<TimelineSchedulingClip<TTime>> clips = content.Clips.Span;
            long firstCycle = playback.Progress.Cycle;
            foreach (int ordinal in content.Boundaries.Decisions.Span)
            {
                ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                if (TryTreeCycles<TClock>(content, in clip, ordinal, in playback, exits,
                        out long first, out _))
                    firstCycle = Math.Min(firstCycle, Math.Max(first, exits.FirstCycle(ordinal)));
            }
            TClock clock = default;
            for (long cycle = firstCycle; cycle <= playback.Progress.Cycle; cycle++)
            {
                TTime time = cycle == playback.Progress.Cycle ? playback.Progress.CursorTime : content.Duration;
                if (cycle == 0)
                {
                    int position = content.Boundaries.FindNext<TClock>(0, time, true);
                    while (position >= 0)
                    {
                        int ordinal = content.Boundaries.StableOrder.Span[position];
                        if (clock.LessThanOrEqual(clips[ordinal].Start, playback.Start.StartTime))
                            AppendActiveTree<TClock>(content, in playback, exits, ordinal, 0,
                                playback.Start.StartTime, destination, ref count);
                        position = content.Boundaries.FindNext<TClock>(position + 1, time, true);
                    }
                }
                int start = cycle == 0 ? content.Boundaries.FindStart<TClock>(playback.Start.StartTime, false) : 0;
                int index = content.Boundaries.FindNextStart<TClock>(start, time);
                while (index >= 0)
                {
                    int ordinal = content.Boundaries.Starts.Span[index];
                    ref readonly TimelineSchedulingClip<TTime> clip = ref clips[ordinal];
                    AppendActiveTree<TClock>(content, in playback, exits, ordinal, (int)cycle,
                        clip.Start, destination, ref count);
                    index = content.Boundaries.FindNextStart<TClock>(index + 1, time);
                }
            }
            return count;
        }

        static void AppendActiveTree<TClock>(OrchestrationTimelinePreparedContent<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback, TimelineTreeExitView exits,
            int ordinal, int cycle, TTime start, Span<TimelineSchedulingBoundary<TTime>> destination, ref int count)
            where TClock : struct, IOrchestrationTimelineClock<TTime>
        {
            ref readonly TimelineSchedulingClip<TTime> clip = ref content.Clips.Span[ordinal];
            if (TryTreeCycles<TClock>(content, in clip, ordinal, in playback, exits,
                    out long first, out long last) && first <= cycle && cycle <= last &&
                (!clip.TreeDecision || !exits.Exited(ordinal, cycle)))
                destination[count++] = new TimelineSchedulingBoundary<TTime>(ordinal, clip.SortOrdinal, start, cycle, true);
        }

        static bool HasActiveTrees<TClock>(OrchestrationTimelinePreparedContent<TTime> content,
            in OrchestrationTimelinePlaybackState<TTime> playback)
            where TClock : struct, IOrchestrationTimelineClock<TTime> =>
            IsActive(in playback) &&
            playback.Progress.InitialBoundaryState != OrchestrationTimelineInitialBoundaryState.Pending &&
            default(TClock).GreaterThan(content.Duration, default(TClock).Zero);

        static bool TryTreeCycles<TClock>(OrchestrationTimelinePreparedContent<TTime> content,
            in TimelineSchedulingClip<TTime> clip, int ordinal,
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
                        playback.Progress.CursorTime, playback.Progress.Cycle, exits,
                        content.Sections.Span, playback.Start.StartTime);
            firstCycle = skippedInitial || !TimelineRuntimeScheduling<TTime, TClock>.InitialClipAvailable(
                in clip, content.Sections.Span, playback.Start.StartTime) ? 1 : 0;
            if (!clock.LessThanOrEqual(clip.Start, playback.Progress.CursorTime))
                lastCycle--;
            return firstCycle <= lastCycle;
        }

    }
}
