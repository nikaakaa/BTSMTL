using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public enum TimelinePresentationSampleReason : byte
    {
        Advance = 0,
        Correction = 1,
        Completed = 2,
        Stopped = 3,
        Withdrawn = 4,
        Paused = 5
    }

    public readonly struct TimelineRuntimePresentationSample
    {
        public TimelineRuntimePresentationSample(ulong sourceGeneration, ulong logicTick, int contentRevisionId,
            OrchestrationTime time, int cycle, TimelinePresentationSampleReason reason, bool retainForCorrection)
        {
            if (sourceGeneration == 0 || logicTick == 0 || time < OrchestrationTime.Zero || cycle < 0 ||
                retainForCorrection && reason != TimelinePresentationSampleReason.Withdrawn)
                throw new ArgumentException("Timeline presentation sample is invalid.");
            SourceGeneration = sourceGeneration;
            LogicTick = logicTick;
            ContentRevisionId = contentRevisionId;
            Time = time;
            Cycle = cycle;
            Reason = reason;
            RetainForCorrection = retainForCorrection;
        }

        public ulong SourceGeneration { get; }
        public ulong LogicTick { get; }
        public OrchestrationTime Time { get; }
        public int Cycle { get; }
        public int ContentRevisionId { get; }
        public TimelinePresentationSampleReason Reason { get; }
        public bool RetainForCorrection { get; }
        public bool AllowTraversal => Reason == TimelinePresentationSampleReason.Advance || Reason == TimelinePresentationSampleReason.Completed;
        public bool EndsPlayback => Reason == TimelinePresentationSampleReason.Completed || Reason == TimelinePresentationSampleReason.Stopped || Reason == TimelinePresentationSampleReason.Withdrawn;
        public bool IsValid => SourceGeneration != 0;
    }

    public sealed class TimelineRuntimePresentationDriver
    {
        readonly int m_PlaybackCapacity;
        readonly Dictionary<ulong, PresentationPlaybackState> m_Playbacks;
        readonly Dictionary<(TimelineContentUnit Timeline, OrchestrationTimelinePlaybackMode Mode), Stack<PresentationPlaybackState>> m_RecycledPlaybacks = new();
        PresentationPlaybackState m_FirstPendingFrame;
        PresentationPlaybackState m_LastPendingFrame;

        public TimelineRuntimePresentationDriver(int playbackCapacity)
        {
            m_PlaybackCapacity = playbackCapacity;
            m_Playbacks = new Dictionary<ulong, PresentationPlaybackState>(playbackCapacity);
        }

        public void RetireContent(TimelineContentUnit timeline, OrchestrationTimelinePlaybackMode mode) =>
            m_RecycledPlaybacks.Remove((timeline, mode));

        public void Prepare(TimelineRuntimePreparedContent content, OrchestrationTimelinePlaybackMode mode)
        {
            var key = (content.Content, mode);
            if (m_RecycledPlaybacks.ContainsKey(key))
                return;
            var recycled = new Stack<PresentationPlaybackState>(m_PlaybackCapacity);
            for (int i = 0; i < m_PlaybackCapacity; i++)
                recycled.Push(new PresentationPlaybackState(content, mode, recycled));
            m_RecycledPlaybacks.Add(key, recycled);
        }

        public bool TryPresent(
            TimelineRuntimePreparedContent content,
            int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            in TimelineRuntimePresentationSample sample,
            ulong presentationFrame,
            float presentationDeltaSeconds,
            float interpolationAlpha,
            out TimelineRuntimePresentationFrame frame)
        {
            if (!sample.IsValid || sample.SourceGeneration != start.SourceGeneration || sample.Time > content.Content.Duration ||
                sample.ContentRevisionId != content.ContentRevisionId ||
                start.ContentIdentityId != content.ContentIdentityId || start.ContentRevisionId != content.ContentRevisionId)
                throw new InvalidOperationException("Timeline presentation sample does not match the prepared playback.");
            PresentationPlaybackState state = GetPlaybackState(content, in start, start.StartTime, 0);

            if (state.HasPendingFrame)
            {
                if (state.PendingFrame.PresentationFrame != presentationFrame)
                    throw new InvalidOperationException("Timeline presentation candidate has not been accepted or discarded.");
                frame = state.PendingFrame;
                return false;
            }
            if (state.HasCachedFrame)
            {
                if (presentationFrame == state.LastPresentationFrame)
                {
                    frame = state.CachedFrame;
                    return false;
                }
                if (presentationFrame < state.LastPresentationFrame)
                    throw new InvalidOperationException("Timeline presentation frame moved backward without a generation reset.");
            }

            if (state.Finished && sample.EndsPlayback && sample.Time == state.CursorTime && sample.Cycle == state.Cycle)
            {
                frame = default;
                return false;
            }

            bool loop = start.Mode == OrchestrationTimelinePlaybackMode.Loop;
            OrchestrationTime previousTime = state.CursorTime;
            int previousCycle = state.Cycle;
            OrchestrationTime currentTime = sample.Time;
            int currentCycle = sample.Cycle;
            TimelinePresentationSampleReason reason = state.SampleReason == TimelinePresentationSampleReason.Correction && !sample.EndsPlayback
                ? TimelinePresentationSampleReason.Correction : sample.Reason;
            bool allowTraversal = state.SampleReason != TimelinePresentationSampleReason.Correction && sample.AllowTraversal &&
                (currentCycle > previousCycle || currentCycle == previousCycle && currentTime >= previousTime);
            if (allowTraversal && currentCycle - previousCycle > OrchestrationTimelineLimits.MaximumCycleAdvance)
                throw new InvalidOperationException("Timeline presentation exceeded its prepared cycle capacity.");
            if (!allowTraversal)
            {
                previousTime = currentTime;
                previousCycle = currentCycle;
            }
            Array.Copy(state.MarkerLastTraversal, state.PendingMarkerLastTraversal, state.MarkerLastTraversal.Length);
            state.Candidate.Clear();
            var events = state.Candidate.Events;
            bool stopped = reason == TimelinePresentationSampleReason.Stopped || reason == TimelinePresentationSampleReason.Withdrawn;
            TimelinePresentationSampleReason sampleReason = !allowTraversal && reason == TimelinePresentationSampleReason.Advance
                ? TimelinePresentationSampleReason.Correction : reason;
            if (!stopped)
                TimelineRuntimePresentationEvaluator.Evaluate(
                content,
                previousTime,
                previousCycle,
                currentTime,
                currentCycle,
                sampleReason,
                state.Candidate);
            if (allowTraversal)
                AppendMarkerEvents(
                content,
                playbackSlot,
                in start,
                presentationFrame,
                state,
                previousTime,
                previousCycle,
                currentTime,
                currentCycle,
                loop,
                !state.InitialBoundaryConsumed,
                events);
            AppendTreeClips(content, playbackSlot, in start, presentationFrame, state, sample, allowTraversal, reason);
            var operations = new TimelineRuntimePresentationOperations(state.Candidate);
            state.PendingTime = currentTime;
            state.PendingCycle = currentCycle;
            state.PendingFinished = sample.EndsPlayback;
            state.PendingInitialBoundaryConsumed = state.InitialBoundaryConsumed || allowTraversal ||
                reason == TimelinePresentationSampleReason.Correction;
            frame = new TimelineRuntimePresentationFrame(
                in start,
                sample.LogicTick,
                presentationFrame,
                presentationDeltaSeconds,
                interpolationAlpha,
                operations,
                events.View,
                sampleReason,
                currentTime,
                currentCycle);
            state.PendingFrame = frame;
            state.PreviousPendingFrame = m_LastPendingFrame;
            if (m_LastPendingFrame == null)
                m_FirstPendingFrame = state;
            else
                m_LastPendingFrame.NextPendingFrame = state;
            m_LastPendingFrame = state;
            return true;
        }

        PresentationPlaybackState GetPlaybackState(TimelineRuntimePreparedContent content,
            in OrchestrationTimelineStart<OrchestrationTime> start, OrchestrationTime startTime, int cycle)
        {
            bool hasState = m_Playbacks.TryGetValue(start.PlaybackIdentity, out PresentationPlaybackState state);
            TimelinePresentationSampleReason reason = hasState ? state.SampleReason : TimelinePresentationSampleReason.Advance;
            if (hasState && state.SourceGeneration != start.SourceGeneration)
            {
                if (state.HasPendingFrame)
                    ReleasePendingFrame(state);
                state.Recycle();
                m_Playbacks.Remove(start.PlaybackIdentity);
                hasState = false;
            }
            if (!hasState)
            {
                var key = (content.Content, start.Mode);
                Stack<PresentationPlaybackState> recycled = m_RecycledPlaybacks[key];
                state = recycled.Pop();
                state.Restart(start.SourceGeneration, startTime, cycle);
                state.SampleReason = reason;
                m_Playbacks.Add(start.PlaybackIdentity, state);
            }
            return state;
        }

        public bool RequestTreeClipExit(in TimelineRuntimePresentationFrame frame, in TimelineRuntimeTreeClipRequest request)
        {
            if (!m_Playbacks.TryGetValue(frame.Start.PlaybackIdentity, out PresentationPlaybackState state) ||
                state.SourceGeneration != frame.Start.SourceGeneration || !state.HasPendingFrame || state.PendingFrame.PresentationFrame != frame.PresentationFrame ||
                request.Token.PlaybackIdentity != frame.Start.PlaybackIdentity || request.Token.Sequence != frame.PresentationFrame)
                throw new InvalidOperationException("Presentation TreeClip exit requires its current candidate frame.");
            int index = state.TreePositions[request.Token.ClipOrdinal];
            PresentationTree tree = state.Trees[index];
            if (tree.Clip.ExitSource != TimelineClipExitSource.TreeDecision)
                throw new InvalidOperationException("A fixed-interval Presentation TreeClip cannot request a graph-controlled exit.");
            if (state.PendingTreeCycles[index] != request.Token.Cycle)
                return false;
            state.PendingTreeCycles[index] = -1;
            state.PendingTreeExitCycles[index] = request.Token.Cycle;
            for (int activeIndex = state.Candidate.ActiveTreeClips.Count - 1; activeIndex >= 0; activeIndex--)
                if (state.Candidate.ActiveTreeClips[activeIndex].Token.ClipOrdinal == request.Token.ClipOrdinal)
                    state.Candidate.ActiveTreeClips.RemoveAt(activeIndex);
            if (request.EventKind == TimelineRuntimeTreeClipEventKind.Enter)
                for (int pendingIndex = state.Candidate.TreeClips.Count - 1; pendingIndex >= 0; pendingIndex--)
                {
                    TimelineRuntimeTreeClipRequest pending = state.Candidate.TreeClips[pendingIndex];
                    if (pending.Token.ClipOrdinal == request.Token.ClipOrdinal && pending.Token.Cycle == request.Token.Cycle &&
                        pending.EventKind == TimelineRuntimeTreeClipEventKind.Update)
                        state.Candidate.TreeClips.RemoveAt(pendingIndex);
                }
            state.Candidate.TreeClips.Add(tree.Request(TimelineRuntimeTreeClipEventKind.Exit, frame.Time, request.Token.Cycle,
                request.Token.PlaybackSlot, frame.PresentationFrame, in frame.Start));
            return true;
        }

        public void CommitPresentationFrame(ulong presentationFrame)
        {
            PresentationPlaybackState state = m_FirstPendingFrame;
            while (state != null)
            {
                PresentationPlaybackState next = state.NextPendingFrame;
                if (state.PendingFrame.PresentationFrame == presentationFrame)
                {
                    Array.Copy(state.PendingMarkerLastTraversal, state.MarkerLastTraversal, state.MarkerLastTraversal.Length);
                    Array.Copy(state.PendingTreeCycles, state.TreeCycles, state.TreeCycles.Length);
                    Array.Copy(state.PendingTreeExitCycles, state.TreeExitCycles, state.TreeExitCycles.Length);
                    state.CursorTime = state.PendingTime;
                    state.Cycle = state.PendingCycle;
                    state.Finished = state.PendingFinished;
                    state.InitialBoundaryConsumed = state.PendingInitialBoundaryConsumed;
                    state.SampleReason = TimelinePresentationSampleReason.Advance;
                    state.Cache(state.PendingFrame);
                    ReleasePendingFrame(state);
                }
                state = next;
            }
        }

        public void DiscardPresentationFrame(ulong presentationFrame)
        {
            PresentationPlaybackState state = m_FirstPendingFrame;
            while (state != null)
            {
                PresentationPlaybackState next = state.NextPendingFrame;
                if (state.PendingFrame.PresentationFrame == presentationFrame)
                {
                    state.Candidate.Clear();
                    ReleasePendingFrame(state);
                }
                state = next;
            }
        }

        void ReleasePendingFrame(PresentationPlaybackState state)
        {
            if (state.PreviousPendingFrame == null)
                m_FirstPendingFrame = state.NextPendingFrame;
            else
                state.PreviousPendingFrame.NextPendingFrame = state.NextPendingFrame;
            if (state.NextPendingFrame == null)
                m_LastPendingFrame = state.PreviousPendingFrame;
            else
                state.NextPendingFrame.PreviousPendingFrame = state.PreviousPendingFrame;
            state.PreviousPendingFrame = null;
            state.NextPendingFrame = null;
            state.PendingFrame = default;
        }

        public void SuspendPresentationPlayback(ulong playbackIdentity, ulong sourceGeneration)
        {
            if (!m_Playbacks.TryGetValue(playbackIdentity, out PresentationPlaybackState state) || state.SourceGeneration != sourceGeneration)
                return;
            if (state.HasPendingFrame)
                ReleasePendingFrame(state);
            state.Clear();
            state.CachedFrame = default;
            state.HasCachedFrame = false;
            state.Finished = false;
        }

        public void ReleasePresentationPlayback(ulong playbackIdentity, ulong sourceGeneration)
        {
            if (m_Playbacks.TryGetValue(playbackIdentity, out PresentationPlaybackState state) && state.SourceGeneration == sourceGeneration)
            {
                if (state.HasPendingFrame)
                    ReleasePendingFrame(state);
                state.Recycle();
                m_Playbacks.Remove(playbackIdentity);
            }
        }

        public void Clear()
        {
            while (m_FirstPendingFrame != null)
                ReleasePendingFrame(m_FirstPendingFrame);
            foreach (PresentationPlaybackState state in m_Playbacks.Values)
                state.Clear();
            m_Playbacks.Clear();
            m_RecycledPlaybacks.Clear();
        }

        static void AppendMarkerEvents(
            TimelineRuntimePreparedContent content,
            int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            ulong presentationFrame,
            PresentationPlaybackState state,
            OrchestrationTime previousTime,
            int previousCycle,
            OrchestrationTime currentTime,
            int currentCycle,
            bool loop,
            bool includeStartBoundary,
            TimelineRuntimeSampleBuffer<TimelineRuntimePresentationEvent> events)
        {
            OrchestrationTime duration = content.Content.Duration;
            if (duration <= OrchestrationTime.Zero)
                return;
            if (currentCycle < previousCycle || currentCycle == previousCycle && currentTime < previousTime)
                throw new InvalidOperationException("Timeline presentation cursor moved backward without a generation reset.");
            int firstCycle = loop ? previousCycle : 0;
            int lastCycle = loop ? currentCycle : 0;
            TimelineRuntimeMarkerIndex markers = content.Index.Presentation.Markers;
            List<int> selected = state.Candidate.SamplingClips;
            for (int cycle = firstCycle; cycle <= lastCycle; cycle++)
            {
                OrchestrationTime from = cycle == previousCycle ? previousTime : OrchestrationTime.Zero;
                OrchestrationTime to = cycle == currentCycle ? currentTime : duration;
                markers.Select(from, to, includeStartBoundary || cycle > previousCycle,
                    includeStartBoundary && cycle == previousCycle, selected);
                for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
                {
                    int position = selected[selectedIndex];
                    TimelineContentMarker marker = markers[position];
                    int markerIndex = markers.ContentIndexAt(position);
                    ulong traversalIndex = checked((ulong)cycle + 1);
                    if (state.PendingMarkerLastTraversal[markerIndex] >= traversalIndex)
                        continue;
                    state.PendingMarkerLastTraversal[markerIndex] = traversalIndex;
                    events.Add(new TimelineRuntimePresentationEvent(
                        in start,
                        new OrchestrationTimelineInvocationToken(playbackSlot, presentationFrame,
                            start.PlaybackIdentity, markerIndex, cycle, true),
                        marker.Time));
                }
            }
        }

        readonly struct PresentationTree
        {
            public PresentationTree(TimelineRuntimeIndexedClip clip)
            {
                m_Indexed = clip;
            }
            readonly TimelineRuntimeIndexedClip m_Indexed;
            public TimelineContentClip Clip => m_Indexed.Clip;
            public TimelineRuntimeTreeClipRequest Request(TimelineRuntimeTreeClipEventKind kind, OrchestrationTime time, int cycle,
                int playbackSlot, ulong presentationFrame, in OrchestrationTimelineStart<OrchestrationTime> start) =>
                new(new OrchestrationTimelineInvocationToken(playbackSlot, presentationFrame,
                        start.PlaybackIdentity, m_Indexed.ContentIndex, cycle, false),
                    TimelineTreeExecutionPhase.Commit,
                    Clip.ExitSource, kind, time, time, Clip.StartTime,
                    ((time - Clip.StartTime) / (Clip.EndTime - Clip.StartTime)).AsSingle);
        }

        static void AppendTreeClips(TimelineRuntimePreparedContent content, int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start, ulong presentationFrame, PresentationPlaybackState state,
            in TimelineRuntimePresentationSample sample, bool traverse, TimelinePresentationSampleReason reason)
        {
            bool stopped = reason == TimelinePresentationSampleReason.Stopped || reason == TimelinePresentationSampleReason.Withdrawn;
            bool correction = state.SampleReason == TimelinePresentationSampleReason.Correction ||
                reason == TimelinePresentationSampleReason.Correction ||
                reason == TimelinePresentationSampleReason.Advance && !traverse;
            var output = state.Candidate.TreeClips;
            Array.Copy(state.TreeCycles, state.PendingTreeCycles, state.TreeCycles.Length);
            Array.Copy(state.TreeExitCycles, state.PendingTreeExitCycles, state.TreeExitCycles.Length);
            if (correction)
                Array.Fill(state.PendingTreeExitCycles, -1);
            List<int> selected = state.Candidate.SamplingClips;
            selected.Clear();
            for (int index = 0; index < state.Accepted.ActiveTreeClips.Count; index++)
                TimelineRuntimeClipIndex.AddSelected(selected,
                    state.TreePositions[state.Accepted.ActiveTreeClips[index].Token.ClipOrdinal]);
            if (!stopped)
            {
                TimelineRuntimeClipIndex trees = content.Index.Presentation.TreeLifetimes;
                if (!traverse)
                    trees.Select(sample.Time, sample.Time, true, selected);
                else
                {
                    if (!state.InitialBoundaryConsumed)
                        trees.Select(state.CursorTime, state.CursorTime, false, selected);
                    for (int cycle = state.Cycle; cycle <= sample.Cycle; cycle++)
                    {
                        OrchestrationTime from = cycle == state.Cycle ? state.CursorTime : OrchestrationTime.Zero;
                        OrchestrationTime to = cycle == sample.Cycle ? sample.Time : content.Content.Duration;
                        int first = trees.FindStart(from, !state.InitialBoundaryConsumed || cycle > state.Cycle);
                        for (int index = first; index < trees.Count; index++)
                        {
                            if (trees.StartAt(index).Clip.StartTime > to)
                                break;
                            TimelineRuntimeClipIndex.AddSelected(selected, trees.StartIndexAt(index));
                        }
                    }
                }
            }
            for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
            {
                int index = selected[selectedIndex];
                PresentationTree tree = state.Trees[index];
                int activeCycle = state.TreeCycles[index];
                int exitCycle = correction ? -1 : state.TreeExitCycles[index];
                bool dynamic = tree.Clip.ExitSource == TimelineClipExitSource.TreeDecision;
                if ((stopped || correction) && activeCycle >= 0)
                {
                    output.Add(tree.Request(TimelineRuntimeTreeClipEventKind.Destroy, state.CursorTime, activeCycle, playbackSlot, presentationFrame, in start));
                    activeCycle = -1;
                }
                if (!stopped)
                {
                    int firstCycle = traverse ? state.Cycle : sample.Cycle;
                    for (int cycle = firstCycle; cycle <= sample.Cycle; cycle++)
                    {
                        OrchestrationTime from = cycle == state.Cycle && traverse ? state.CursorTime : OrchestrationTime.Zero;
                        OrchestrationTime to = cycle == sample.Cycle ? sample.Time : content.Content.Duration;
                        bool crossesStart = tree.Clip.StartTime > from ||
                            tree.Clip.StartTime == from && (!state.InitialBoundaryConsumed || cycle > state.Cycle);
                        bool entersAtStart = traverse && !state.InitialBoundaryConsumed && cycle == state.Cycle &&
                            tree.Clip.StartTime < from && (dynamic || from < tree.Clip.EndTime);
                        bool align = !traverse && cycle == sample.Cycle &&
                            to >= tree.Clip.StartTime && (dynamic || to < tree.Clip.EndTime);
                        if (activeCycle < 0 && cycle > exitCycle && to >= tree.Clip.StartTime && (crossesStart && traverse || entersAtStart || align))
                        {
                            activeCycle = cycle;
                            OrchestrationTime enterTime = entersAtStart ? from : align ? to : tree.Clip.StartTime;
                            output.Add(tree.Request(TimelineRuntimeTreeClipEventKind.Enter, enterTime, cycle, playbackSlot, presentationFrame, in start));
                        }
                        if (activeCycle >= 0 && (activeCycle < cycle || !dynamic && to >= tree.Clip.EndTime ||
                            cycle < sample.Cycle || sample.EndsPlayback && cycle == sample.Cycle))
                        {
                            OrchestrationTime exitTime = dynamic ? to : OrchestrationTime.Min(to, tree.Clip.EndTime);
                            output.Add(tree.Request(TimelineRuntimeTreeClipEventKind.Exit, exitTime, activeCycle, playbackSlot, presentationFrame, in start));
                            exitCycle = activeCycle;
                            activeCycle = -1;
                        }
                    }
                    if (activeCycle >= 0)
                    {
                        var update = tree.Request(TimelineRuntimeTreeClipEventKind.Update, sample.Time, activeCycle, playbackSlot, presentationFrame, in start);
                        if (reason != TimelinePresentationSampleReason.Paused)
                            output.Add(update);
                        state.Candidate.ActiveTreeClips.Add(update);
                    }
                }
                state.PendingTreeCycles[index] = activeCycle;
                state.PendingTreeExitCycles[index] = exitCycle;
            }
        }

        sealed class PresentationPlaybackState
        {
            readonly Stack<PresentationPlaybackState> m_Recycled;

            public PresentationPlaybackState(
                TimelineRuntimePreparedContent prepared,
                OrchestrationTimelinePlaybackMode mode,
                Stack<PresentationPlaybackState> recycled)
            {
                m_Recycled = recycled;
                TimelineContentUnit content = prepared.Content;
                int markerCount = content.Markers.Count;
                Candidate = new TimelineRuntimePresentationBuffer(prepared, mode);
                Accepted = new TimelineRuntimePresentationBuffer(prepared, mode);
                MarkerLastTraversal = new ulong[markerCount];
                PendingMarkerLastTraversal = new ulong[markerCount];
                TimelineRuntimeClipIndex trees = prepared.Index.Presentation.TreeLifetimes;
                Trees = new PresentationTree[trees.Count];
                TreePositions = new int[content.Clips.Count];
                for (int index = 0; index < trees.Count; index++)
                {
                    TimelineRuntimeIndexedClip clip = trees[index];
                    Trees[index] = new PresentationTree(clip);
                    TreePositions[clip.ContentIndex] = index;
                }
                TreeCycles = new int[Trees.Length];
                PendingTreeCycles = new int[Trees.Length];
                TreeExitCycles = new int[Trees.Length];
                PendingTreeExitCycles = new int[Trees.Length];
                Array.Fill(TreeCycles, -1);
                Array.Fill(PendingTreeCycles, -1);
                Array.Fill(TreeExitCycles, -1);
                Array.Fill(PendingTreeExitCycles, -1);
            }

            public readonly PresentationTree[] Trees;
            public readonly int[] TreePositions;
            public readonly int[] TreeCycles;
            public readonly int[] PendingTreeCycles;
            public readonly int[] TreeExitCycles;
            public readonly int[] PendingTreeExitCycles;
            public TimelineRuntimePresentationBuffer Candidate;
            public TimelineRuntimePresentationBuffer Accepted;
            public readonly ulong[] MarkerLastTraversal;
            public readonly ulong[] PendingMarkerLastTraversal;
            public OrchestrationTime PendingTime;
            public int PendingCycle;
            public bool PendingFinished;
            public bool PendingInitialBoundaryConsumed;
            public TimelineRuntimePresentationFrame PendingFrame;
            public bool HasPendingFrame => PendingFrame.PresentationFrame != 0;
            public PresentationPlaybackState PreviousPendingFrame;
            public PresentationPlaybackState NextPendingFrame;
            public ulong SourceGeneration { get; private set; }
            public OrchestrationTime CursorTime;
            public int Cycle;
            public bool InitialBoundaryConsumed;
            public TimelinePresentationSampleReason SampleReason;
            public bool Finished;
            public TimelineRuntimePresentationFrame CachedFrame;
            public ulong LastPresentationFrame;
            public bool HasCachedFrame;

            public void Restart(ulong sourceGeneration, OrchestrationTime startTime, int cycle)
            {
                SourceGeneration = sourceGeneration;
                Array.Clear(MarkerLastTraversal, 0, MarkerLastTraversal.Length);
                Array.Clear(PendingMarkerLastTraversal, 0, PendingMarkerLastTraversal.Length);
                PendingTime = startTime;
                PendingCycle = cycle;
                PendingFinished = false;
                PendingInitialBoundaryConsumed = false;
                PendingFrame = default;
                CursorTime = startTime;
                Cycle = cycle;
                InitialBoundaryConsumed = false;
                SampleReason = TimelinePresentationSampleReason.Advance;
                Finished = false;
                CachedFrame = default;
                LastPresentationFrame = 0;
                HasCachedFrame = false;
            }

            public void Recycle()
            {
                Clear();
                m_Recycled.Push(this);
            }

            public void Cache(TimelineRuntimePresentationFrame frame)
            {
                TimelineRuntimePresentationBuffer recycled = Accepted;
                Accepted = Candidate;
                Candidate = recycled;
                Candidate.Clear();
                CachedFrame = frame;
                LastPresentationFrame = frame.PresentationFrame;
                HasCachedFrame = true;
            }

            public void Clear()
            {
                Candidate.Clear();
                Accepted.Clear();
                Array.Fill(TreeCycles, -1);
                Array.Fill(PendingTreeCycles, -1);
                Array.Fill(TreeExitCycles, -1);
                Array.Fill(PendingTreeExitCycles, -1);
            }

        }


    }

}
