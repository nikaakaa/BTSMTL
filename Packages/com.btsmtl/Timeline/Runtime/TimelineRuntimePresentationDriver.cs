using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;

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
        public TimelineRuntimePresentationSample(ulong generation, ulong logicTick, CanonicalTextValue contentRevision, FixedScalar time, int cycle, TimelinePresentationSampleReason reason, bool retainForCorrection)
        {
            if (generation == 0 || logicTick == 0 || time < FixedScalar.Zero || cycle < 0 ||
                retainForCorrection && reason != TimelinePresentationSampleReason.Withdrawn)
                throw new ArgumentException("Timeline presentation sample is invalid.");
            Generation = generation;
            LogicTick = logicTick;
            ContentRevision = contentRevision;
            Time = time;
            Cycle = cycle;
            Reason = reason;
            RetainForCorrection = retainForCorrection;
        }

        public ulong Generation { get; }
        public ulong LogicTick { get; }
        public FixedScalar Time { get; }
        public int Cycle { get; }
        public CanonicalTextValue ContentRevision { get; }
        public TimelinePresentationSampleReason Reason { get; }
        public bool RetainForCorrection { get; }
        public bool AllowTraversal => Reason == TimelinePresentationSampleReason.Advance || Reason == TimelinePresentationSampleReason.Completed;
        public bool EndsPlayback => Reason == TimelinePresentationSampleReason.Completed || Reason == TimelinePresentationSampleReason.Stopped || Reason == TimelinePresentationSampleReason.Withdrawn;
        public bool IsValid => Generation != 0;
        public void Retain() => ContentRevision.Retain();
        public void Release() => ContentRevision.Release();
    }

    public sealed class TimelineRuntimePresentationDriver : ITimelineRuntimeEvaluationSink
    {
        readonly Dictionary<ulong, PresentationPlaybackState> m_Playbacks =
            new Dictionary<ulong, PresentationPlaybackState>();
        readonly Dictionary<(TimelineContentUnit Timeline, TimelinePlaybackMode Mode), Stack<PresentationPlaybackState>> m_RecycledPlaybacks = new();
        PresentationPlaybackState m_FirstPendingFrame;
        PresentationPlaybackState m_LastPendingFrame;

        public void RetireContent(TimelineContentUnit timeline, TimelinePlaybackMode mode) =>
            m_RecycledPlaybacks.Remove((timeline, mode));

        public void Prepare(TimelineRuntimePreparedContent content, TimelinePlaybackMode mode)
        {
            var key = (content.Content, mode);
            if (m_RecycledPlaybacks.ContainsKey(key))
                return;
            var recycled = new Stack<PresentationPlaybackState>(1);
            recycled.Push(new PresentationPlaybackState(content.Content, content.Index, mode, recycled));
            m_RecycledPlaybacks.Add(key, recycled);
        }

        public bool TryPresent(
            TimelineRuntimePlayback playback,
            in TimelineRuntimePresentationSample sample,
            ulong presentationFrame,
            float presentationDeltaSeconds,
            float interpolationAlpha,
            out TimelineRuntimePresentationFrame frame)
        {
            if (!sample.IsValid || sample.Generation != playback.Generation || sample.Time > playback.Content.Duration ||
                !sample.ContentRevision.Equals(playback.ContentRevision))
                throw new InvalidOperationException("Timeline presentation sample does not match the prepared playback.");
            PresentationPlaybackState state = GetPlaybackState(playback, playback.Preparation.StartTime, 0);

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

            bool loop = playback.PlaybackMode == TimelinePlaybackMode.Loop;
            FixedScalar previousTime = state.CursorTime;
            int previousCycle = state.Cycle;
            FixedScalar currentTime = sample.Time;
            int currentCycle = sample.Cycle;
            TimelinePresentationSampleReason reason = state.SampleReason == TimelinePresentationSampleReason.Correction && !sample.EndsPlayback
                ? TimelinePresentationSampleReason.Correction : sample.Reason;
            bool allowTraversal = state.SampleReason != TimelinePresentationSampleReason.Correction && sample.AllowTraversal &&
                (currentCycle > previousCycle || currentCycle == previousCycle && currentTime >= previousTime);
            if (allowTraversal && currentCycle - previousCycle > TimelineRuntimeEvaluationSegments.MaximumCycleAdvance)
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
            if (!stopped)
                TimelineRuntimePresentationEvaluator.Evaluate(
                playback,
                currentTime,
                currentCycle,
                loop,
                state.Candidate);
            if (allowTraversal)
                AppendMarkerEvents(
                playback,
                state,
                previousTime,
                previousCycle,
                currentTime,
                currentCycle,
                loop,
                !state.InitialBoundaryConsumed,
                events);
            AppendTreeClips(playback, state, sample, allowTraversal, reason);
            var operations = new TimelineRuntimePresentationOperations(state.Candidate);
            state.PendingTime = currentTime;
            state.PendingCycle = currentCycle;
            state.PendingFinished = sample.EndsPlayback;
            state.PendingInitialBoundaryConsumed = state.InitialBoundaryConsumed || allowTraversal ||
                reason == TimelinePresentationSampleReason.Correction;
            frame = new TimelineRuntimePresentationFrame(
                playback,
                sample.LogicTick,
                presentationFrame,
                presentationDeltaSeconds,
                interpolationAlpha,
                operations,
                events.View,
                !allowTraversal && reason == TimelinePresentationSampleReason.Advance
                    ? TimelinePresentationSampleReason.Correction : reason,
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

        PresentationPlaybackState GetPlaybackState(TimelineRuntimePlayback playback, FixedScalar startTime, int cycle)
        {
            bool hasState = m_Playbacks.TryGetValue(playback.Handle.Value, out PresentationPlaybackState state);
            TimelinePresentationSampleReason reason = hasState ? state.SampleReason : TimelinePresentationSampleReason.Advance;
            if (hasState && state.Generation != playback.Generation)
            {
                if (state.HasPendingFrame)
                    ReleasePendingFrame(state);
                state.Recycle();
                m_Playbacks.Remove(playback.Handle.Value);
                hasState = false;
            }
            if (!hasState)
            {
                var key = (playback.Content, playback.PlaybackMode);
                Stack<PresentationPlaybackState> recycled = m_RecycledPlaybacks[key];
                if (recycled.Count == 0)
                    state = new PresentationPlaybackState(playback.Content, playback.Index, playback.PlaybackMode, recycled);
                else
                    state = recycled.Pop();
                state.Restart(playback.Generation, startTime, cycle);
                state.SampleReason = reason;
                m_Playbacks.Add(playback.Handle.Value, state);
            }
            return state;
        }

        internal void RestorePlayback(TimelineRuntimePlayback playback)
        {
            if (!m_Playbacks.TryGetValue(playback.Handle.Value, out PresentationPlaybackState state))
                state = GetPlaybackState(playback, playback.CursorTime, playback.Cycle);
            state.SampleReason = TimelinePresentationSampleReason.Correction;
        }

        public bool RequestTreeClipExit(in TimelineRuntimePresentationFrame frame, in TimelineRuntimeTreeClipRequest request)
        {
            if (!m_Playbacks.TryGetValue(frame.Handle.Value, out PresentationPlaybackState state) ||
                state.Generation != frame.Generation || !state.HasPendingFrame || state.PendingFrame.PresentationFrame != frame.PresentationFrame)
                throw new InvalidOperationException("Presentation TreeClip exit requires its current candidate frame.");
            if (!state.TreePositions.TryGetValue(request.ClipAuthoringId, out int index))
                throw new InvalidOperationException("Presentation TreeClip exit has no prepared clip.");
            PresentationTree tree = state.Trees[index];
            if (tree.Clip.ExitSource != TimelineClipExitSource.TreeDecision)
                throw new InvalidOperationException("A fixed-interval Presentation TreeClip cannot request a graph-controlled exit.");
            if (state.PendingTreeCycles[index] != request.Cycle)
                return false;
            state.PendingTreeCycles[index] = -1;
            state.PendingTreeExitCycles[index] = request.Cycle;
            for (int activeIndex = state.Candidate.ActiveTreeClips.Count - 1; activeIndex >= 0; activeIndex--)
                if (state.Candidate.ActiveTreeClips[activeIndex].ClipAuthoringId == request.ClipAuthoringId)
                    state.Candidate.ActiveTreeClips.RemoveAt(activeIndex);
            if (request.EventKind == TimelineRuntimeTreeClipEventKind.Enter)
                for (int pendingIndex = state.Candidate.TreeClips.Count - 1; pendingIndex >= 0; pendingIndex--)
                {
                    TimelineRuntimeTreeClipRequest pending = state.Candidate.TreeClips[pendingIndex];
                    if (pending.ClipAuthoringId == request.ClipAuthoringId && pending.Cycle == request.Cycle &&
                        pending.EventKind == TimelineRuntimeTreeClipEventKind.Update)
                        state.Candidate.TreeClips.RemoveAt(pendingIndex);
                }
            state.Candidate.TreeClips.Add(tree.Request(TimelineRuntimeTreeClipEventKind.Exit, frame.Time, request.Cycle, frame.Generation));
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

        public void SuspendPresentationPlayback(TimelineRuntimePlaybackHandle handle, ulong generation)
        {
            if (!m_Playbacks.TryGetValue(handle.Value, out PresentationPlaybackState state) || state.Generation != generation)
                return;
            if (state.HasPendingFrame)
                ReleasePendingFrame(state);
            state.Clear();
            state.CachedFrame = default;
            state.HasCachedFrame = false;
            state.Finished = false;
        }

        public void ReleasePresentationPlayback(TimelineRuntimePlaybackHandle handle, ulong generation)
        {
            if (m_Playbacks.TryGetValue(handle.Value, out PresentationPlaybackState state) && state.Generation == generation)
            {
                if (state.HasPendingFrame)
                    ReleasePendingFrame(state);
                state.Recycle();
                m_Playbacks.Remove(handle.Value);
            }
        }

        public bool Consume(TimelineRuntimeStepContext context) => true;

        public void Commit(TimelineRuntimeStepContext context)
        {
        }

        public void Discard(TimelineRuntimeStepContext context)
        {
        }

        public bool ConsumeStop(TimelineRuntimeStopRequest request) => true;

        public void CommitStop(TimelineRuntimeStopRequest request)
        {
        }

        public void DiscardStop(TimelineRuntimeStopRequest request)
        {
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
            TimelineRuntimePlayback playback,
            PresentationPlaybackState state,
            FixedScalar previousTime,
            int previousCycle,
            FixedScalar currentTime,
            int currentCycle,
            bool loop,
            bool includeStartBoundary,
            TimelineRuntimeSampleBuffer<TimelineRuntimePresentationEvent> events)
        {
            FixedScalar duration = playback.Content.Duration;
            if (duration <= FixedScalar.Zero)
                return;
            if (currentCycle < previousCycle || currentCycle == previousCycle && currentTime < previousTime)
                throw new InvalidOperationException("Timeline presentation cursor moved backward without a generation reset.");
            int firstCycle = loop ? previousCycle : 0;
            int lastCycle = loop ? currentCycle : 0;
            TimelineRuntimeMarkerIndex markers = playback.Index.Presentation.Markers;
            List<int> selected = state.Candidate.SamplingClips;
            for (int cycle = firstCycle; cycle <= lastCycle; cycle++)
            {
                FixedScalar from = cycle == previousCycle ? previousTime : FixedScalar.Zero;
                FixedScalar to = cycle == currentCycle ? currentTime : duration;
                markers.Select(from, to, includeStartBoundary || cycle > previousCycle,
                    includeStartBoundary && cycle == previousCycle, selected);
                for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
                {
                    int position = selected[selectedIndex];
                    TimelineContentMarker marker = markers[position];
                    int markerIndex = markers.ContentIndexAt(position);
                    string markerId = marker.MarkerId;
                    ulong traversalIndex = checked((ulong)cycle + 1);
                    if (state.PendingMarkerLastTraversal[markerIndex] >= traversalIndex)
                        continue;
                    state.PendingMarkerLastTraversal[markerIndex] = traversalIndex;
                    events.Add(new TimelineRuntimePresentationEvent(
                        playback.Handle,
                        playback.ExecutionIdentity,
                        playback.Generation,
                        markerId,
                        traversalIndex,
                        marker.GraphId,
                        marker.GraphRevision,
                        marker.Time,
                        cycle));
                }
            }
        }

        readonly struct PresentationTree
        {
            public PresentationTree(TimelineRuntimeIndexedClip clip, string graphId, string revision)
            {
                m_Indexed = clip;
                GraphId = graphId;
                Revision = revision;
            }
            readonly TimelineRuntimeIndexedClip m_Indexed;
            public TimelineContentClip Clip => m_Indexed.Clip;
            public readonly string GraphId;
            public readonly string Revision;
            public TimelineRuntimeTreeClipRequest Request(TimelineRuntimeTreeClipEventKind kind, FixedScalar time, int cycle, ulong generation) =>
                new(Clip.AuthoringId, Clip.TrackAuthoringId, GraphId, Revision, TimelineTreeExecutionPhase.Commit,
                    Clip.ExitSource, kind, time, time, Clip.StartTime, cycle,
                    ((time - Clip.StartTime) / (Clip.EndTime - Clip.StartTime)).ToSingle(), generation,
                    TimelineRuntimeTreeClipRequest.ComposeBranchRevision(cycle));
        }

        static void AppendTreeClips(TimelineRuntimePlayback playback, PresentationPlaybackState state,
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
                    state.TreePositions[state.Accepted.ActiveTreeClips[index].ClipAuthoringId]);
            if (!stopped)
            {
                TimelineRuntimeClipIndex trees = playback.Index.Presentation.TreeLifetimes;
                if (!traverse)
                    trees.Select(sample.Time, sample.Time, true, selected);
                else
                {
                    if (!state.InitialBoundaryConsumed)
                        trees.Select(state.CursorTime, state.CursorTime, false, selected);
                    for (int cycle = state.Cycle; cycle <= sample.Cycle; cycle++)
                    {
                        FixedScalar from = cycle == state.Cycle ? state.CursorTime : FixedScalar.Zero;
                        FixedScalar to = cycle == sample.Cycle ? sample.Time : playback.Content.Duration;
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
                    output.Add(tree.Request(TimelineRuntimeTreeClipEventKind.Destroy, state.CursorTime, activeCycle, playback.Generation));
                    activeCycle = -1;
                }
                if (!stopped)
                {
                    int firstCycle = traverse ? state.Cycle : sample.Cycle;
                    for (int cycle = firstCycle; cycle <= sample.Cycle; cycle++)
                    {
                        FixedScalar from = cycle == state.Cycle && traverse ? state.CursorTime : FixedScalar.Zero;
                        FixedScalar to = cycle == sample.Cycle ? sample.Time : playback.Content.Duration;
                        bool crossesStart = tree.Clip.StartTime > from ||
                            tree.Clip.StartTime == from && (!state.InitialBoundaryConsumed || cycle > state.Cycle);
                        bool entersAtStart = traverse && !state.InitialBoundaryConsumed && cycle == state.Cycle &&
                            tree.Clip.StartTime < from && (dynamic || from < tree.Clip.EndTime);
                        bool align = !traverse && cycle == sample.Cycle &&
                            to >= tree.Clip.StartTime && (dynamic || to < tree.Clip.EndTime);
                        if (activeCycle < 0 && cycle > exitCycle && to >= tree.Clip.StartTime && (crossesStart && traverse || entersAtStart || align))
                        {
                            activeCycle = cycle;
                            FixedScalar enterTime = entersAtStart ? from : align ? to : tree.Clip.StartTime;
                            output.Add(tree.Request(TimelineRuntimeTreeClipEventKind.Enter, enterTime, cycle, playback.Generation));
                        }
                        if (activeCycle >= 0 && (activeCycle < cycle || !dynamic && to >= tree.Clip.EndTime ||
                            cycle < sample.Cycle || sample.EndsPlayback && cycle == sample.Cycle))
                        {
                            FixedScalar exitTime = dynamic ? to : FixedScalar.Min(to, tree.Clip.EndTime);
                            output.Add(tree.Request(TimelineRuntimeTreeClipEventKind.Exit, exitTime, activeCycle, playback.Generation));
                            exitCycle = activeCycle;
                            activeCycle = -1;
                        }
                    }
                    if (activeCycle >= 0)
                    {
                        var update = tree.Request(TimelineRuntimeTreeClipEventKind.Update, sample.Time, activeCycle, playback.Generation);
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
                TimelineContentUnit content,
                TimelineRuntimeContentIndex preparedIndex,
                TimelinePlaybackMode mode,
                Stack<PresentationPlaybackState> recycled)
            {
                m_Recycled = recycled;
                int markerCount = content.Markers.Count;
                Candidate = new TimelineRuntimePresentationBuffer(content, mode);
                Accepted = new TimelineRuntimePresentationBuffer(content, mode);
                MarkerLastTraversal = new ulong[markerCount];
                PendingMarkerLastTraversal = new ulong[markerCount];
                TimelineRuntimeClipIndex trees = preparedIndex.Presentation.TreeLifetimes;
                Trees = new PresentationTree[trees.Count];
                TreePositions = new Dictionary<string, int>(trees.Count, StringComparer.Ordinal);
                for (int index = 0; index < trees.Count; index++)
                {
                    TimelineRuntimeIndexedClip clip = trees[index];
                    Trees[index] = new PresentationTree(clip, clip.GraphId, clip.GraphRevision);
                    TreePositions.Add(clip.Clip.AuthoringId, index);
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
            public readonly Dictionary<string, int> TreePositions;
            public readonly int[] TreeCycles;
            public readonly int[] PendingTreeCycles;
            public readonly int[] TreeExitCycles;
            public readonly int[] PendingTreeExitCycles;
            public TimelineRuntimePresentationBuffer Candidate;
            public TimelineRuntimePresentationBuffer Accepted;
            public readonly ulong[] MarkerLastTraversal;
            public readonly ulong[] PendingMarkerLastTraversal;
            public FixedScalar PendingTime;
            public int PendingCycle;
            public bool PendingFinished;
            public bool PendingInitialBoundaryConsumed;
            public TimelineRuntimePresentationFrame PendingFrame;
            public bool HasPendingFrame => PendingFrame.PresentationFrame != 0;
            public PresentationPlaybackState PreviousPendingFrame;
            public PresentationPlaybackState NextPendingFrame;
            public ulong Generation { get; private set; }
            public FixedScalar CursorTime;
            public int Cycle;
            public bool InitialBoundaryConsumed;
            public TimelinePresentationSampleReason SampleReason;
            public bool Finished;
            public TimelineRuntimePresentationFrame CachedFrame;
            public ulong LastPresentationFrame;
            public bool HasCachedFrame;

            public void Restart(ulong generation, FixedScalar startTime, int cycle)
            {
                Generation = generation;
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
