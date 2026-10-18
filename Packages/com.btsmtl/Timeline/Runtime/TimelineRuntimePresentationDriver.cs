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

    public enum TimelineRuntimePresentationGraphSource : byte
    {
        Unexecuted,
        Executed
    }

    public readonly struct TimelineRuntimePresentationSource
    {
        TimelineRuntimePresentationSource(int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            in TimelineRuntimePresentationSample sample, TimelineRuntimePresentationGraphSource graphSource)
        {
            PlaybackSlot = playbackSlot;
            Start = start;
            Sample = sample;
            GraphSource = graphSource;
        }

        public int PlaybackSlot { get; }
        public readonly OrchestrationTimelineStart<OrchestrationTime> Start;
        public readonly TimelineRuntimePresentationSample Sample;
        public TimelineRuntimePresentationGraphSource GraphSource { get; }

        public static TimelineRuntimePresentationSource FromUnexecutedPlayback(int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            in TimelineRuntimePresentationSample sample) =>
            new(playbackSlot, in start, in sample, TimelineRuntimePresentationGraphSource.Unexecuted);

        public static TimelineRuntimePresentationSource FromExecutedPlayback(
            in OrchestrationTimelineCommittedView<OrchestrationTime> inventory, int playbackSlot,
            ulong logicTick, TimelinePresentationSampleReason reason, bool retainForCorrection) =>
            FromExecutedResult(playbackSlot, in inventory.Playback(playbackSlot), logicTick, reason, retainForCorrection);

        public static TimelineRuntimePresentationSource FromExecutedResult(int playbackSlot,
            in OrchestrationTimelinePlaybackState<OrchestrationTime> accepted, ulong logicTick,
            TimelinePresentationSampleReason reason, bool retainForCorrection)
        {
            var sample = new TimelineRuntimePresentationSample(accepted.Start.SourceGeneration, logicTick,
                accepted.Start.ContentRevisionId, accepted.Progress.CursorTime, accepted.Progress.Cycle,
                reason, retainForCorrection);
            return new TimelineRuntimePresentationSource(playbackSlot, in accepted.Start, in sample,
                TimelineRuntimePresentationGraphSource.Executed);
        }
    }

    public readonly struct TimelineRuntimePresentationPlayback
    {
        internal TimelineRuntimePresentationPlayback(in OrchestrationTimelineStart<OrchestrationTime> start,
            OrchestrationTime time, int cycle, bool finished, bool retainForCorrection,
            TimelineRuntimePresentationGraphSource graphSource)
        {
            Start = start;
            Time = time;
            Cycle = cycle;
            Finished = finished;
            RetainForCorrection = retainForCorrection;
            GraphSource = graphSource;
        }

        public OrchestrationTimelineStart<OrchestrationTime> Start { get; }
        public OrchestrationTime Time { get; }
        public int Cycle { get; }
        public bool Finished { get; }
        public bool RetainForCorrection { get; }
        public TimelineRuntimePresentationGraphSource GraphSource { get; }
    }

    public sealed class TimelineRuntimePresentationDriver
    {
        const int RecordedStateHeaderV5 = -5;
        readonly int m_PlaybackCapacity;
        readonly int m_RecordedPlaybackCapacity;
        readonly int m_RecordedStateHeaderBytes;
        int m_AllPlaybacksRecordedBytes;
        int m_AllPlaybacksRecordedTextReferences;
        int m_MaximumRecordedPlaybackBytes;
        int m_MaximumRecordedPlaybackTextReferences;
        readonly Dictionary<OrchestrationTimelineStart<OrchestrationTime>, PresentationPlaybackState> m_Playbacks;
        readonly Dictionary<(TimelineContentUnit Timeline, int SourceIdentityId, OrchestrationTimelinePlaybackMode Mode, TimelineRuntimePresentationGraphSource GraphSource), Stack<PresentationPlaybackState>> m_RecycledPlaybacks = new();
        readonly TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock> m_Scheduler = new();
        TimelineSchedulingBoundary<OrchestrationTime>[] m_Boundaries = Array.Empty<TimelineSchedulingBoundary<OrchestrationTime>>();
        TimelineScheduledInvocation<OrchestrationTime>[] m_Scheduled = Array.Empty<TimelineScheduledInvocation<OrchestrationTime>>();
        TimelineSchedulingBoundary<OrchestrationTime>[] m_ActiveTrees = Array.Empty<TimelineSchedulingBoundary<OrchestrationTime>>();
        bool[] m_PreviousActiveClips = Array.Empty<bool>();
        bool[] m_SelectedTrees = Array.Empty<bool>();
        PresentationPlaybackState m_FirstPendingFrame;
        PresentationPlaybackState m_LastPendingFrame;

        public PlaybackCollection Playbacks => new PlaybackCollection(this);
        public int MaximumRecordedStateBytes { get; private set; }
        public int MaximumRecordedTextReferences { get; private set; }

        public readonly struct PlaybackCollection
        {
            readonly TimelineRuntimePresentationDriver m_Owner;

            internal PlaybackCollection(TimelineRuntimePresentationDriver owner) => m_Owner = owner;

            public int Count => m_Owner.m_Playbacks.Count;
            public PlaybackEnumerator GetEnumerator() => new PlaybackEnumerator(m_Owner);
        }

        public struct PlaybackEnumerator : IDisposable
        {
            Dictionary<OrchestrationTimelineStart<OrchestrationTime>, PresentationPlaybackState>.Enumerator m_Enumerator;

            internal PlaybackEnumerator(TimelineRuntimePresentationDriver owner) =>
                m_Enumerator = owner.m_Playbacks.GetEnumerator();

            public TimelineRuntimePresentationPlayback Current
            {
                get
                {
                    KeyValuePair<OrchestrationTimelineStart<OrchestrationTime>, PresentationPlaybackState> item = m_Enumerator.Current;
                    var start = item.Key;
                    return new TimelineRuntimePresentationPlayback(in start,
                        item.Value.CursorTime, item.Value.Cycle, item.Value.Finished,
                        item.Value.CachedFrame.RetainForCorrection, item.Value.GraphSource);
                }
            }

            public bool MoveNext() => m_Enumerator.MoveNext();
            public void Dispose() => m_Enumerator.Dispose();
        }

        public TimelineRuntimePresentationDriver(int playbackCapacity, int recordedPlaybackCapacity)
        {
            m_PlaybackCapacity = playbackCapacity;
            m_RecordedPlaybackCapacity = recordedPlaybackCapacity;
            m_Playbacks = new Dictionary<OrchestrationTimelineStart<OrchestrationTime>, PresentationPlaybackState>(playbackCapacity);
            var writer = new RecordedRuntimeStateCountingWriter();
            WriteRecordedState(ref writer);
            m_RecordedStateHeaderBytes = writer.WrittenBytes;
            MaximumRecordedStateBytes = writer.WrittenBytes;
        }

        public void Prepare(TimelineRuntimePreparedContent content,
            OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent,
            int sourceIdentityId, TimelineRuntimePresentationGraphSource graphSource,
            Predicate<string> supportsSampleKind)
        {
            if (executionContent.Domain != TimelineExecutionDomain.Presentation ||
                executionContent.ContentIdentityId != content.ContentIdentityId ||
                executionContent.ContentRevisionId != content.ContentRevisionId)
                throw new ArgumentException("表现播放必须装配本次调用的同版本 Presentation 执行内容。", nameof(executionContent));
            var key = (content.Content, sourceIdentityId, executionContent.Mode, graphSource);
            if (m_RecycledPlaybacks.ContainsKey(key))
                return;
            for (int index = 0; index < content.Content.Clips.Count; index++)
            {
                TimelineContentClip clip = content.Content.Clips[index];
                if (clip.ContentKind == TimelineContentKind.Sampled &&
                    clip.ExecutionPolicy.Domain == TimelineExecutionDomain.Presentation &&
                    !supportsSampleKind(clip.ContractKind))
                    throw new InvalidOperationException($"Timeline '{content.Content.Identity}' 片段 '{clip.AuthoringId}' 的表现采样种类 '{clip.ContractKind}' 未装配消费者。");
            }
            if (graphSource == TimelineRuntimePresentationGraphSource.Unexecuted)
                PrepareGraphWorkspace(executionContent);
            var recycled = new Stack<PresentationPlaybackState>(m_PlaybackCapacity);
            for (int i = 0; i < m_PlaybackCapacity; i++)
                recycled.Push(new PresentationPlaybackState(content, executionContent, graphSource, recycled));
            var writer = new RecordedRuntimeStateCountingWriter();
            if (m_PlaybackCapacity != 0)
            {
                WritePlayback(ref writer, default, recycled.Peek(), true);
                writer.Write(default(OrchestrationTimelineStart<OrchestrationTime>));
            }
            m_AllPlaybacksRecordedBytes = checked(m_AllPlaybacksRecordedBytes +
                checked(writer.WrittenBytes * m_PlaybackCapacity));
            m_AllPlaybacksRecordedTextReferences = checked(m_AllPlaybacksRecordedTextReferences +
                checked(writer.TextReferenceCount * m_PlaybackCapacity));
            m_MaximumRecordedPlaybackBytes = Math.Max(m_MaximumRecordedPlaybackBytes, writer.WrittenBytes);
            m_MaximumRecordedPlaybackTextReferences = Math.Max(m_MaximumRecordedPlaybackTextReferences, writer.TextReferenceCount);
            MaximumRecordedStateBytes = checked(m_RecordedStateHeaderBytes + (int)Math.Min(
                m_AllPlaybacksRecordedBytes, (long)m_MaximumRecordedPlaybackBytes * m_RecordedPlaybackCapacity));
            MaximumRecordedTextReferences = (int)Math.Min(m_AllPlaybacksRecordedTextReferences,
                (long)m_MaximumRecordedPlaybackTextReferences * m_RecordedPlaybackCapacity);
            m_RecycledPlaybacks.Add(key, recycled);
            m_Playbacks.EnsureCapacity(checked(m_RecycledPlaybacks.Count * m_PlaybackCapacity));
        }

        public void ReleasePreparedContent(TimelineRuntimePreparedContent content, int sourceIdentityId,
            OrchestrationTimelinePlaybackMode mode, TimelineRuntimePresentationGraphSource graphSource)
        {
            var key = (content.Content, sourceIdentityId, mode, graphSource);
            Stack<PresentationPlaybackState> recycled = m_RecycledPlaybacks[key];
            if (recycled.Count != m_PlaybackCapacity)
                throw new InvalidOperationException("Timeline 时间版本仍有播放持有，不能释放其正式准备存储。");
            var writer = new RecordedRuntimeStateCountingWriter();
            if (m_PlaybackCapacity != 0)
            {
                WritePlayback(ref writer, default, recycled.Peek(), true);
                writer.Write(default(OrchestrationTimelineStart<OrchestrationTime>));
            }
            m_AllPlaybacksRecordedBytes -= checked(writer.WrittenBytes * m_PlaybackCapacity);
            m_AllPlaybacksRecordedTextReferences -= checked(writer.TextReferenceCount * m_PlaybackCapacity);
            m_RecycledPlaybacks.Remove(key);
            MaximumRecordedStateBytes = checked(m_RecordedStateHeaderBytes + (int)Math.Min(
                m_AllPlaybacksRecordedBytes, (long)m_MaximumRecordedPlaybackBytes * m_RecordedPlaybackCapacity));
            MaximumRecordedTextReferences = (int)Math.Min(m_AllPlaybacksRecordedTextReferences,
                (long)m_MaximumRecordedPlaybackTextReferences * m_RecordedPlaybackCapacity);
        }

        void PrepareGraphWorkspace(OrchestrationTimelinePreparedContent<OrchestrationTime> content)
        {
            int traversals = content.Mode == OrchestrationTimelinePlaybackMode.Loop
                ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
            int boundaries = checked(2 * content.Clips.Length * traversals);
            if (m_Boundaries.Length < boundaries)
                Array.Resize(ref m_Boundaries, boundaries);
            if (m_Scheduled.Length < content.TickCapacity.GraphInvocationsPerAdvance)
                Array.Resize(ref m_Scheduled, content.TickCapacity.GraphInvocationsPerAdvance);
            if (m_ActiveTrees.Length < content.RequiredActiveTrees)
                Array.Resize(ref m_ActiveTrees, content.RequiredActiveTrees);
            if (m_PreviousActiveClips.Length < content.Clips.Length)
                Array.Resize(ref m_PreviousActiveClips, content.Clips.Length);
            if (m_SelectedTrees.Length < content.Clips.Length)
                Array.Resize(ref m_SelectedTrees, content.Clips.Length);
        }

        public bool TryPresent(
            TimelineRuntimePreparedContent content,
            in TimelineRuntimePresentationSource source,
            ulong presentationFrame,
            float presentationDeltaSeconds,
            float interpolationAlpha,
            out TimelineRuntimePresentationFrame frame)
        {
            ref readonly OrchestrationTimelineStart<OrchestrationTime> start = ref source.Start;
            ref readonly TimelineRuntimePresentationSample sample = ref source.Sample;
            int playbackSlot = source.PlaybackSlot;
            if (!sample.IsValid || sample.SourceGeneration != start.SourceGeneration || sample.Time > content.Content.Duration ||
                sample.ContentRevisionId != content.ContentRevisionId ||
                start.ContentIdentityId != content.ContentIdentityId || start.ContentRevisionId != content.ContentRevisionId)
                throw new InvalidOperationException("Timeline presentation sample does not match the prepared playback.");
            PresentationPlaybackState state = GetPlaybackState(content, in start, source.GraphSource, start.StartTime, 0);

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

            if (state.Finished && (state.CachedFrame.Reason == TimelinePresentationSampleReason.Completed ||
                    state.CachedFrame.Reason == TimelinePresentationSampleReason.Stopped) && !sample.EndsPlayback)
                throw new InvalidOperationException("已确认结束的 Timeline 播放不能接受新的样本。");

            if (state.Finished && sample.EndsPlayback && sample.Time == state.CursorTime && sample.Cycle == state.Cycle &&
                state.CachedFrame.Reason == sample.Reason &&
                state.CachedFrame.RetainForCorrection == sample.RetainForCorrection)
            {
                frame = default;
                return false;
            }

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
            if (state.GraphSource == TimelineRuntimePresentationGraphSource.Unexecuted)
            {
                Array.Copy(state.Graphs.MarkerLastTraversal, state.Graphs.PendingMarkerLastTraversal, state.Graphs.MarkerLastTraversal.Length);
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
                    !state.Graphs.InitialBoundaryConsumed,
                    events);
                AppendTreeClips(content, playbackSlot, in start, presentationFrame, state, sample, allowTraversal, reason);
                state.Graphs.PendingInitialBoundaryConsumed = state.Graphs.InitialBoundaryConsumed || allowTraversal ||
                    reason == TimelinePresentationSampleReason.Correction;
            }
            var operations = new TimelineRuntimePresentationOperations(state.Candidate);
            state.PendingTime = currentTime;
            state.PendingCycle = currentCycle;
            state.PendingFinished = sample.EndsPlayback;
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
                currentCycle,
                sample.RetainForCorrection);
            state.PendingFrame = frame;
            state.PreviousPendingFrame = m_LastPendingFrame;
            if (m_LastPendingFrame == null)
                m_FirstPendingFrame = state;
            else
                m_LastPendingFrame.NextPendingFrame = state;
            m_LastPendingFrame = state;
            return true;
        }

        public TimelineRuntimePresentationFrame ReadPresentationFrame(
            in OrchestrationTimelineStart<OrchestrationTime> start)
        {
            PresentationPlaybackState state = m_Playbacks[start];
            return state.HasPendingFrame ? state.PendingFrame : state.CachedFrame;
        }

        PresentationPlaybackState GetPlaybackState(TimelineRuntimePreparedContent content,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            TimelineRuntimePresentationGraphSource graphSource, OrchestrationTime startTime, int cycle)
        {
            if (!m_Playbacks.TryGetValue(start, out PresentationPlaybackState state))
            {
                var key = (content.Content, start.SourceIdentityId, start.Mode, graphSource);
                Stack<PresentationPlaybackState> recycled = m_RecycledPlaybacks[key];
                state = recycled.Pop();
                state.Restart(startTime, cycle);
                m_Playbacks.Add(start, state);
            }
            return state;
        }

        public bool RequestTreeClipExit(in TimelineRuntimePresentationFrame frame, in TimelineRuntimeTreeClipRequest request)
        {
            if (!m_Playbacks.TryGetValue(frame.Start, out PresentationPlaybackState state) ||
                !state.HasPendingFrame || state.PendingFrame.PresentationFrame != frame.PresentationFrame ||
                request.Token.PlaybackIdentity != frame.Start.PlaybackIdentity || request.Token.Sequence != frame.PresentationFrame)
                throw new InvalidOperationException("Presentation TreeClip exit requires its current candidate frame.");
            if (state.Prepared.Content.Clips[request.Token.ClipOrdinal].ExitSource != TimelineClipExitSource.TreeDecision)
                throw new InvalidOperationException("A fixed-interval Presentation TreeClip cannot request a graph-controlled exit.");
            if (TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock>.Contains(
                state.Graphs.PendingTreeExits.AsSpan(0, state.Graphs.PendingTreeExitCount), request.Token.ClipOrdinal, request.Token.Cycle))
                return false;
            OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock>.AppendExit(
                state.Graphs.PendingTreeExits, ref state.Graphs.PendingTreeExitCount, new OrchestrationTimelineTreeExitRange
                {
                    ClipOrdinal = request.Token.ClipOrdinal,
                    FirstCycle = request.Token.Cycle,
                    LastCycle = request.Token.Cycle
                });
            for (int activeIndex = state.Candidate.ActiveTreeClips.Count - 1; activeIndex >= 0; activeIndex--)
                if (state.Candidate.ActiveTreeClips[activeIndex].Token.ClipOrdinal == request.Token.ClipOrdinal &&
                    state.Candidate.ActiveTreeClips[activeIndex].Token.Cycle == request.Token.Cycle)
                    state.Candidate.ActiveTreeClips.RemoveAt(activeIndex);
            if (request.EventKind == TimelineRuntimeTreeClipEventKind.Enter)
                for (int pendingIndex = state.Candidate.TreeClips.Count - 1; pendingIndex >= 0; pendingIndex--)
                {
                    TimelineRuntimeTreeClipRequest pending = state.Candidate.TreeClips[pendingIndex];
                    if (pending.Token.ClipOrdinal == request.Token.ClipOrdinal && pending.Token.Cycle == request.Token.Cycle &&
                        pending.EventKind == TimelineRuntimeTreeClipEventKind.Update)
                        state.Candidate.TreeClips.RemoveAt(pendingIndex);
                }
            state.Candidate.TreeClips.Add(TreeRequest(state.Prepared, request.Token.ClipOrdinal,
                TimelineRuntimeTreeClipEventKind.Exit, request.Time, request.Time, request.Token.Cycle,
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
                    if (state.GraphSource == TimelineRuntimePresentationGraphSource.Unexecuted)
                    {
                        Array.Copy(state.Graphs.PendingMarkerLastTraversal, state.Graphs.MarkerLastTraversal, state.Graphs.MarkerLastTraversal.Length);
                        state.Graphs.PendingTreeExits.AsSpan(0, state.Graphs.PendingTreeExitCount).CopyTo(state.Graphs.TreeExits);
                        state.Graphs.TreeExitCount = state.Graphs.PendingTreeExitCount;
                        state.Graphs.TreeInitialBoundaryState = state.Graphs.PendingTreeInitialBoundaryState;
                        state.Graphs.InitialBoundaryConsumed = state.Graphs.PendingInitialBoundaryConsumed;
                    }
                    state.CursorTime = state.PendingTime;
                    state.Cycle = state.PendingCycle;
                    state.Finished = state.PendingFinished;
                    state.SampleReason = state.PendingFrame.RetainForCorrection
                        ? TimelinePresentationSampleReason.Correction : TimelinePresentationSampleReason.Advance;
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

        public void ReleasePresentationPlayback(in OrchestrationTimelineStart<OrchestrationTime> start)
        {
            if (m_Playbacks.TryGetValue(start, out PresentationPlaybackState state))
            {
                if (state.HasPendingFrame)
                    ReleasePendingFrame(state);
                state.Recycle();
                m_Playbacks.Remove(start);
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
            m_AllPlaybacksRecordedBytes = 0;
            m_AllPlaybacksRecordedTextReferences = 0;
            m_MaximumRecordedPlaybackBytes = 0;
            m_MaximumRecordedPlaybackTextReferences = 0;
            var writer = new RecordedRuntimeStateCountingWriter();
            WriteRecordedState(ref writer);
            MaximumRecordedStateBytes = writer.WrittenBytes;
            MaximumRecordedTextReferences = writer.TextReferenceCount;
        }

        public void WriteRecordedState<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(RecordedStateHeaderV5);
            writer.Write(m_Playbacks.Count);
            foreach (KeyValuePair<OrchestrationTimelineStart<OrchestrationTime>, PresentationPlaybackState> playback in m_Playbacks)
            {
                WritePlayback(ref writer, playback.Key, playback.Value, false);
            }
            int pendingCount = 0;
            for (PresentationPlaybackState state = m_FirstPendingFrame; state != null; state = state.NextPendingFrame)
                pendingCount++;
            writer.Write(pendingCount);
            for (PresentationPlaybackState state = m_FirstPendingFrame; state != null; state = state.NextPendingFrame)
                writer.Write(state.PendingFrame.Start);
        }

        public void RestoreRecordedState(ref RecordedRuntimeStateReader reader,
            IReadOnlyList<TimelineRuntimePreparedContent> contents)
        {
            if (reader.Read<int>() != RecordedStateHeaderV5)
                throw new InvalidOperationException("Recorded Timeline presentation state version is not supported.");
            int count = reader.Read<int>();
            if (count < 0 || count > m_RecordedPlaybackCapacity)
                throw new InvalidOperationException("Recorded Timeline playback count exceeds the prepared capacity.");
            m_FirstPendingFrame = null;
            m_LastPendingFrame = null;
            foreach (KeyValuePair<OrchestrationTimelineStart<OrchestrationTime>, PresentationPlaybackState> playback in m_Playbacks)
            {
                PresentationPlaybackState state = playback.Value;
                state.PreviousPendingFrame = null;
                state.NextPendingFrame = null;
                state.PendingFrame = default;
                state.Recycle();
            }
            m_Playbacks.Clear();
            for (int index = 0; index < count; index++)
            {
                var start = reader.Read<OrchestrationTimelineStart<OrchestrationTime>>();
                var graphSource = reader.Read<TimelineRuntimePresentationGraphSource>();
                string contentIdentity = reader.ReadText();
                string contentRevision = reader.ReadText();
                TimelineRuntimePreparedContent prepared = RequireRecordedContent(contents, contentIdentity, contentRevision);
                if (!m_RecycledPlaybacks.TryGetValue((prepared.Content, start.SourceIdentityId, start.Mode, graphSource), out Stack<PresentationPlaybackState> recycled) ||
                    recycled.Count == 0)
                    throw new InvalidOperationException($"Recorded Timeline '{contentIdentity}' revision '{contentRevision}' has no prepared playback capacity for '{start.Mode}'.");
                PresentationPlaybackState state = recycled.Pop();
                state.RestoreRecordedState(ref reader, in start);
                m_Playbacks.Add(start, state);
            }
            int pendingCount = reader.Read<int>();
            if (pendingCount < 0 || pendingCount > count)
                throw new InvalidOperationException("Recorded Timeline pending frame count exceeds its playback count.");
            for (int index = 0; index < pendingCount; index++)
            {
                PresentationPlaybackState state = m_Playbacks[reader.Read<OrchestrationTimelineStart<OrchestrationTime>>()];
                state.PreviousPendingFrame = m_LastPendingFrame;
                if (m_LastPendingFrame == null)
                    m_FirstPendingFrame = state;
                else
                    m_LastPendingFrame.NextPendingFrame = state;
                m_LastPendingFrame = state;
            }
        }

        static TimelineRuntimePreparedContent RequireRecordedContent(IReadOnlyList<TimelineRuntimePreparedContent> contents,
            string identity, string revision)
        {
            for (int index = 0; index < contents.Count; index++)
            {
                TimelineRuntimePreparedContent prepared = contents[index];
                if (prepared.Content.Identity == identity && prepared.Content.Revision == revision)
                    return prepared;
            }
            throw new InvalidOperationException($"Recorded Timeline '{identity}' revision '{revision}' is not in the prepared content table.");
        }

        static void WritePlayback<TWriter>(ref TWriter writer, OrchestrationTimelineStart<OrchestrationTime> start,
            PresentationPlaybackState state, bool maximum) where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(start);
            writer.Write(state.GraphSource);
            writer.WriteText(state.Prepared.Content.Identity);
            writer.WriteText(state.Prepared.Content.Revision);
            state.WriteRecordedState(ref writer, maximum);
        }

        static void WriteFrame<TWriter>(ref TWriter writer, in TimelineRuntimePresentationFrame frame)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(frame.LogicTick);
            writer.Write(frame.PresentationFrame);
            writer.Write(frame.PresentationDeltaSeconds);
            writer.Write(frame.InterpolationAlpha);
            writer.Write(frame.Reason);
            writer.Write(frame.Time);
            writer.Write(frame.Cycle);
            writer.Write(frame.RetainForCorrection);
        }

        static TimelineRuntimePresentationFrame ReadFrame(ref RecordedRuntimeStateReader reader,
            TimelineRuntimePresentationBuffer buffer, in OrchestrationTimelineStart<OrchestrationTime> start)
        {
            ulong logicTick = reader.Read<ulong>();
            ulong presentationFrame = reader.Read<ulong>();
            float deltaSeconds = reader.Read<float>();
            float alpha = reader.Read<float>();
            TimelinePresentationSampleReason reason = reader.Read<TimelinePresentationSampleReason>();
            OrchestrationTime time = reader.Read<OrchestrationTime>();
            int cycle = reader.Read<int>();
            bool retainForCorrection = reader.Read<bool>();
            return new TimelineRuntimePresentationFrame(in start, logicTick, presentationFrame, deltaSeconds, alpha,
                new TimelineRuntimePresentationOperations(buffer), buffer.Events.View, reason, time, cycle, retainForCorrection);
        }

        static void WriteBuffer<TWriter>(ref TWriter writer, TimelineRuntimePresentationBuffer buffer, bool maximum)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            for (int index = 0; index < buffer.Samples.Length; index++)
                WriteSamples(ref writer, buffer.Samples[index], maximum);
            WriteSamples(ref writer, buffer.Events, maximum);
            WriteSamples(ref writer, buffer.TreeClips, maximum);
            WriteSamples(ref writer, buffer.ActiveTreeClips, maximum);
            int count = maximum ? buffer.SamplingClips.Capacity : buffer.SamplingClips.Count;
            writer.Write(count);
            for (int index = 0; index < count; index++)
                writer.Write(maximum ? default : buffer.SamplingClips[index]);
        }

        static void ReadBuffer(ref RecordedRuntimeStateReader reader, TimelineRuntimePresentationBuffer buffer)
        {
            for (int index = 0; index < buffer.Samples.Length; index++)
                ReadSamples(ref reader, buffer.Samples[index]);
            ReadSamples(ref reader, buffer.Events);
            ReadSamples(ref reader, buffer.TreeClips);
            ReadSamples(ref reader, buffer.ActiveTreeClips);
            int count = reader.Read<int>();
            if (count < 0 || count > buffer.SamplingClips.Capacity)
                throw new InvalidOperationException("Recorded Timeline sampling workspace exceeds the prepared content capacity.");
            buffer.SamplingClips.Clear();
            for (int index = 0; index < count; index++)
                buffer.SamplingClips.Add(reader.Read<int>());
        }

        static void WriteSamples<TWriter, T>(ref TWriter writer, TimelineRuntimeSampleBuffer<T> buffer, bool maximum)
            where TWriter : struct, IRecordedRuntimeStateWriter where T : unmanaged
        {
            writer.Write(buffer.Version);
            int count = maximum ? buffer.Capacity : buffer.Count;
            writer.Write(count);
            for (int index = 0; index < count; index++)
                writer.Write(maximum ? default : buffer[index]);
        }

        static void ReadSamples<T>(ref RecordedRuntimeStateReader reader, TimelineRuntimeSampleBuffer<T> buffer)
            where T : unmanaged
        {
            ulong version = reader.Read<ulong>();
            int count = reader.Read<int>();
            buffer.RestoreRecordedState(reader.ReadSpan<T>(count), version);
        }

        void AppendMarkerEvents(
            TimelineRuntimePreparedContent content,
            int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            ulong presentationFrame,
            PresentationPlaybackState state,
            OrchestrationTime previousTime,
            int previousCycle,
            OrchestrationTime currentTime,
            int currentCycle,
            bool includeStartBoundary,
            TimelineRuntimeSampleBuffer<TimelineRuntimePresentationEvent> events)
        {
            int count = m_Scheduler.Markers(state.ExecutionContent.Markers.Span,
                previousTime, previousCycle, currentTime, currentCycle, content.Content.Duration,
                includeStartBoundary, m_Scheduled);
            for (int index = 0; index < count; index++)
            {
                ref readonly TimelineScheduledInvocation<OrchestrationTime> marker = ref m_Scheduled[index];
                ulong traversalIndex = checked((ulong)marker.Cycle + 1);
                if (state.Graphs.PendingMarkerLastTraversal[marker.Ordinal] >= traversalIndex)
                    continue;
                state.Graphs.PendingMarkerLastTraversal[marker.Ordinal] = traversalIndex;
                events.Add(new TimelineRuntimePresentationEvent(in start,
                    new OrchestrationTimelineInvocationToken(playbackSlot, presentationFrame,
                        start.PlaybackIdentity, marker.Ordinal, marker.Cycle, true), marker.Time));
            }
        }

        static TimelineRuntimeTreeClipRequest TreeRequest(TimelineRuntimePreparedContent content,
            int ordinal, TimelineRuntimeTreeClipEventKind kind, OrchestrationTime time, OrchestrationTime previousTime,
            int cycle, int playbackSlot, ulong presentationFrame,
            in OrchestrationTimelineStart<OrchestrationTime> start)
        {
            TimelineContentClip clip = content.Content.Clips[ordinal];
            return new TimelineRuntimeTreeClipRequest(
                new OrchestrationTimelineInvocationToken(playbackSlot, presentationFrame,
                    start.PlaybackIdentity, ordinal, cycle, false),
                TimelineTreeExecutionPhase.Commit, clip.ExitSource, kind, time, previousTime,
                clip.StartTime, ((time - clip.StartTime) / (clip.EndTime - clip.StartTime)).AsSingle);
        }

        void AppendTreeClips(TimelineRuntimePreparedContent content, int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start, ulong presentationFrame, PresentationPlaybackState state,
            in TimelineRuntimePresentationSample sample, bool traverse, TimelinePresentationSampleReason reason)
        {
            bool stopped = reason == TimelinePresentationSampleReason.Stopped || reason == TimelinePresentationSampleReason.Withdrawn;
            bool correction = state.SampleReason == TimelinePresentationSampleReason.Correction ||
                reason == TimelinePresentationSampleReason.Correction ||
                reason == TimelinePresentationSampleReason.Advance && !traverse;
            var native = state.ExecutionContent;
            bool align = native.Duration > OrchestrationTime.Zero &&
                (correction || state.Graphs.TreeInitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending &&
                    (!traverse || sample.Cycle == state.Cycle && sample.Time == state.CursorTime));
            var playback = new OrchestrationTimelinePlaybackState<OrchestrationTime>
            {
                Start = start,
                Progress = new()
                {
                    CursorTime = state.CursorTime,
                    Cycle = state.Cycle,
                    InitialBoundaryState = state.Graphs.TreeInitialBoundaryState,
                    State = state.Finished ? OrchestrationTimelinePlaybackStatus.Completed : OrchestrationTimelinePlaybackStatus.Running
                }
            };
            ReadOnlySpan<TimelineSchedulingClip<OrchestrationTime>> clips = native.Clips.Span;
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exits = state.Graphs.TreeExits.AsSpan(0, state.Graphs.TreeExitCount);
            int activeCount = OrchestrationTimelineStateQueries<OrchestrationTime>.CopyActiveTrees<OrchestrationTimeClock>(
                native, in playback, exits, m_ActiveTrees);
            ReadOnlySpan<TimelineSchedulingBoundary<OrchestrationTime>> active = m_ActiveTrees.AsSpan(0, activeCount);
            exits.CopyTo(state.Graphs.PendingTreeExits);
            state.Graphs.PendingTreeExitCount = state.Graphs.TreeExitCount;
            state.Graphs.PendingTreeInitialBoundaryState = state.Graphs.TreeInitialBoundaryState;
            var output = state.Candidate.TreeClips;
            if (stopped || correction)
            {
                foreach (ref readonly TimelineSchedulingBoundary<OrchestrationTime> occurrence in active)
                {
                    OrchestrationTime time = state.CursorTime + native.Duration *
                        OrchestrationTime.FromInt32(state.Cycle - occurrence.Cycle);
                    output.Add(TreeRequest(content, occurrence.ClipOrdinal, TimelineRuntimeTreeClipEventKind.Destroy,
                        time, time, occurrence.Cycle, playbackSlot, presentationFrame, in start));
                }
                if (stopped)
                    return;
            }
            if (align)
            {
                state.Graphs.PendingTreeExitCount = 0;
                if (sample.Cycle > 0)
                    for (int ordinal = 0; ordinal < clips.Length; ordinal++)
                        if (clips[ordinal].Enabled && clips[ordinal].Tree && clips[ordinal].TreeDecision)
                            OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock>.AppendExit(
                                state.Graphs.PendingTreeExits, ref state.Graphs.PendingTreeExitCount, new OrchestrationTimelineTreeExitRange
                                {
                                    ClipOrdinal = ordinal,
                                    FirstCycle = 0,
                                    LastCycle = sample.Cycle - 1
                                });
            }
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> pendingExits =
                state.Graphs.PendingTreeExits.AsSpan(0, state.Graphs.PendingTreeExitCount);
            int scheduledCount = 0;
            if (align)
            {
                int boundaryCount = 0;
                Array.Clear(m_PreviousActiveClips, 0, clips.Length);
                for (int ordinal = 0; ordinal < clips.Length; ordinal++)
                    if (clips[ordinal].Tree &&
                        TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock>.Active(
                            in clips[ordinal], ordinal, sample.Time, sample.Cycle, pendingExits))
                        m_Boundaries[boundaryCount++] = new TimelineSchedulingBoundary<OrchestrationTime>(
                            ordinal, clips[ordinal].SortOrdinal, sample.Time, sample.Cycle, true);
                scheduledCount = m_Scheduler.Trees(clips, native.TreeOrdinals.Span,
                    m_Boundaries.AsSpan(0, boundaryCount), m_PreviousActiveClips,
                    sample.Time, sample.Cycle, sample.Time, sample.Cycle, native.Duration,
                    false, pendingExits, m_SelectedTrees.AsSpan(0, clips.Length), m_Scheduled, 0);
            }
            else if (traverse)
            {
                for (int ordinal = 0; ordinal < clips.Length; ordinal++)
                    m_PreviousActiveClips[ordinal] =
                        TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock>.Active(
                            in clips[ordinal], ordinal, state.CursorTime, state.Cycle, exits);
                int boundaryCount = m_Scheduler.Boundaries(clips, state.CursorTime, state.Cycle,
                    sample.Time, sample.Cycle, native.Duration, start.Mode == OrchestrationTimelinePlaybackMode.Loop,
                    state.Graphs.TreeInitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending, exits,
                    ReadOnlySpan<OrchestrationTimelineClipOccurrence>.Empty, m_Boundaries, 0);
                scheduledCount = m_Scheduler.Trees(clips, native.TreeOrdinals.Span,
                    m_Boundaries.AsSpan(0, boundaryCount), m_PreviousActiveClips,
                    state.CursorTime, state.Cycle, sample.Time, sample.Cycle, native.Duration,
                    start.Mode == OrchestrationTimelinePlaybackMode.Loop, exits,
                    m_SelectedTrees.AsSpan(0, clips.Length), m_Scheduled, 0);
                scheduledCount += m_Scheduler.ContinueTrees(clips, active,
                    state.CursorTime, state.Cycle, sample.Time, sample.Cycle, native.Duration,
                    exits, m_Scheduled.AsSpan(scheduledCount));
            }
            m_Scheduler.SortTrees(m_Scheduled, 0, scheduledCount);
            ReadOnlySpan<TimelineScheduledInvocation<OrchestrationTime>> scheduled = m_Scheduled.AsSpan(0, scheduledCount);
            foreach (ref readonly TimelineScheduledInvocation<OrchestrationTime> request in scheduled)
            {
                if (request.Hook == OrchestrationTimelineHook.Root &&
                    (reason == TimelinePresentationSampleReason.Paused ||
                     !TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock>.IsActive(
                        request.Ordinal, request.Cycle, request.Time, request.Hook, scheduled, active)))
                    continue;
                TimelineRuntimeTreeClipEventKind kind = request.Hook switch
                {
                    OrchestrationTimelineHook.Enable => TimelineRuntimeTreeClipEventKind.Enter,
                    OrchestrationTimelineHook.Root => TimelineRuntimeTreeClipEventKind.Update,
                    OrchestrationTimelineHook.Disable => TimelineRuntimeTreeClipEventKind.Exit,
                    _ => throw new ArgumentOutOfRangeException(nameof(request.Hook))
                };
                output.Add(TreeRequest(content, request.Ordinal, kind, request.Time, request.PreviousTime,
                    request.Cycle, playbackSlot, presentationFrame, in start));
            }
            state.Graphs.PendingTreeInitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Applied;
            playback.Progress.CursorTime = sample.Time;
            playback.Progress.Cycle = sample.Cycle;
            playback.Progress.InitialBoundaryState = state.Graphs.PendingTreeInitialBoundaryState;
            playback.Progress.State = OrchestrationTimelinePlaybackStatus.Running;
            activeCount = OrchestrationTimelineStateQueries<OrchestrationTime>.CopyActiveTrees<OrchestrationTimeClock>(
                native, in playback, pendingExits, m_ActiveTrees);
            for (int index = 0; index < activeCount; index++)
            {
                TimelineSchedulingBoundary<OrchestrationTime> occurrence = m_ActiveTrees[index];
                ref readonly TimelineSchedulingClip<OrchestrationTime> clip = ref clips[occurrence.ClipOrdinal];
                OrchestrationTime time = sample.Time + native.Duration *
                    OrchestrationTime.FromInt32(sample.Cycle - occurrence.Cycle);
                if (clip.TreeDecision && clip.DeclaredEnd)
                    time = OrchestrationTime.Min(time, clip.End);
                if (sample.EndsPlayback)
                {
                    output.Add(TreeRequest(content, occurrence.ClipOrdinal, TimelineRuntimeTreeClipEventKind.Exit,
                        time, time, occurrence.Cycle, playbackSlot, presentationFrame, in start));
                    if (clip.TreeDecision)
                        OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock>.AppendExit(
                            state.Graphs.PendingTreeExits, ref state.Graphs.PendingTreeExitCount, new OrchestrationTimelineTreeExitRange
                            {
                                ClipOrdinal = occurrence.ClipOrdinal,
                                FirstCycle = occurrence.Cycle,
                                LastCycle = occurrence.Cycle
                            });
                }
                else
                    state.Candidate.ActiveTreeClips.Add(TreeRequest(content, occurrence.ClipOrdinal,
                        TimelineRuntimeTreeClipEventKind.Update, time, time, occurrence.Cycle,
                        playbackSlot, presentationFrame, in start));
            }
        }

        sealed class PresentationGraphState
        {
            public PresentationGraphState(TimelineRuntimePreparedContent prepared,
                OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent)
            {
                int markerCount = prepared.Content.Markers.Count;
                MarkerLastTraversal = new ulong[markerCount];
                PendingMarkerLastTraversal = new ulong[markerCount];
                TreeExits = new OrchestrationTimelineTreeExitRange[executionContent.RequiredTreeExits];
                PendingTreeExits = new OrchestrationTimelineTreeExitRange[executionContent.RequiredTreeExits];
            }

            public readonly OrchestrationTimelineTreeExitRange[] TreeExits;
            public readonly OrchestrationTimelineTreeExitRange[] PendingTreeExits;
            public int TreeExitCount;
            public int PendingTreeExitCount;
            public OrchestrationTimelineInitialBoundaryState TreeInitialBoundaryState;
            public OrchestrationTimelineInitialBoundaryState PendingTreeInitialBoundaryState;
            public readonly ulong[] MarkerLastTraversal;
            public readonly ulong[] PendingMarkerLastTraversal;
            public bool InitialBoundaryConsumed;
            public bool PendingInitialBoundaryConsumed;

            public void WriteRecordedState<TWriter>(ref TWriter writer)
                where TWriter : struct, IRecordedRuntimeStateWriter
            {
                writer.Write(InitialBoundaryConsumed);
                writer.Write(PendingInitialBoundaryConsumed);
                writer.WriteSpan<ulong>(MarkerLastTraversal);
                writer.WriteSpan<ulong>(PendingMarkerLastTraversal);
                writer.Write(TreeExitCount);
                writer.Write(PendingTreeExitCount);
                writer.WriteSpan<OrchestrationTimelineTreeExitRange>(TreeExits);
                writer.WriteSpan<OrchestrationTimelineTreeExitRange>(PendingTreeExits);
                writer.Write(TreeInitialBoundaryState);
                writer.Write(PendingTreeInitialBoundaryState);
            }

            public void RestoreRecordedState(ref RecordedRuntimeStateReader reader)
            {
                InitialBoundaryConsumed = reader.Read<bool>();
                PendingInitialBoundaryConsumed = reader.Read<bool>();
                reader.ReadSpan<ulong>(MarkerLastTraversal.Length).CopyTo(MarkerLastTraversal);
                reader.ReadSpan<ulong>(PendingMarkerLastTraversal.Length).CopyTo(PendingMarkerLastTraversal);
                TreeExitCount = reader.Read<int>();
                PendingTreeExitCount = reader.Read<int>();
                reader.ReadSpan<OrchestrationTimelineTreeExitRange>(TreeExits.Length).CopyTo(TreeExits);
                reader.ReadSpan<OrchestrationTimelineTreeExitRange>(PendingTreeExits.Length).CopyTo(PendingTreeExits);
                TreeInitialBoundaryState = reader.Read<OrchestrationTimelineInitialBoundaryState>();
                PendingTreeInitialBoundaryState = reader.Read<OrchestrationTimelineInitialBoundaryState>();
            }

            public void Restart()
            {
                InitialBoundaryConsumed = false;
                PendingInitialBoundaryConsumed = false;
                Array.Clear(MarkerLastTraversal, 0, MarkerLastTraversal.Length);
                Array.Clear(PendingMarkerLastTraversal, 0, PendingMarkerLastTraversal.Length);
            }

            public void Clear()
            {
                TreeExitCount = 0;
                PendingTreeExitCount = 0;
                TreeInitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Pending;
                PendingTreeInitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Pending;
            }
        }

        sealed class PresentationPlaybackState
        {
            readonly Stack<PresentationPlaybackState> m_Recycled;

            public PresentationPlaybackState(
                TimelineRuntimePreparedContent prepared,
                OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent,
                TimelineRuntimePresentationGraphSource graphSource, Stack<PresentationPlaybackState> recycled)
            {
                m_Recycled = recycled;
                Prepared = prepared;
                ExecutionContent = executionContent;
                GraphSource = graphSource;
                Candidate = new TimelineRuntimePresentationBuffer(prepared, executionContent, graphSource);
                Accepted = new TimelineRuntimePresentationBuffer(prepared, executionContent, graphSource);
                if (graphSource == TimelineRuntimePresentationGraphSource.Unexecuted)
                    Graphs = new PresentationGraphState(prepared, executionContent);
            }

            public readonly TimelineRuntimePreparedContent Prepared;
            public readonly OrchestrationTimelinePreparedContent<OrchestrationTime> ExecutionContent;
            public readonly TimelineRuntimePresentationGraphSource GraphSource;
            public readonly PresentationGraphState Graphs;
            public TimelineRuntimePresentationBuffer Candidate;
            public TimelineRuntimePresentationBuffer Accepted;
            public OrchestrationTime PendingTime;
            public int PendingCycle;
            public bool PendingFinished;
            public TimelineRuntimePresentationFrame PendingFrame;
            public bool HasPendingFrame => PendingFrame.PresentationFrame != 0;
            public PresentationPlaybackState PreviousPendingFrame;
            public PresentationPlaybackState NextPendingFrame;
            public OrchestrationTime CursorTime;
            public int Cycle;
            public TimelinePresentationSampleReason SampleReason;
            public bool Finished;
            public TimelineRuntimePresentationFrame CachedFrame;
            public ulong LastPresentationFrame;
            public bool HasCachedFrame;

            public void WriteRecordedState<TWriter>(ref TWriter writer, bool maximum) where TWriter : struct, IRecordedRuntimeStateWriter
            {
                writer.Write(CursorTime);
                writer.Write(Cycle);
                writer.Write(SampleReason);
                writer.Write(Finished);
                writer.Write(PendingTime);
                writer.Write(PendingCycle);
                writer.Write(PendingFinished);
                writer.Write(LastPresentationFrame);
                if (GraphSource == TimelineRuntimePresentationGraphSource.Unexecuted)
                    Graphs.WriteRecordedState(ref writer);
                WriteBuffer(ref writer, Candidate, maximum);
                WriteBuffer(ref writer, Accepted, maximum);
                writer.Write(HasPendingFrame);
                if (maximum || HasPendingFrame)
                    WriteFrame(ref writer, in PendingFrame);
                writer.Write(HasCachedFrame);
                if (maximum || HasCachedFrame)
                    WriteFrame(ref writer, in CachedFrame);
            }

            public void RestoreRecordedState(ref RecordedRuntimeStateReader reader,
                in OrchestrationTimelineStart<OrchestrationTime> start)
            {
                CursorTime = reader.Read<OrchestrationTime>();
                Cycle = reader.Read<int>();
                SampleReason = reader.Read<TimelinePresentationSampleReason>();
                Finished = reader.Read<bool>();
                PendingTime = reader.Read<OrchestrationTime>();
                PendingCycle = reader.Read<int>();
                PendingFinished = reader.Read<bool>();
                LastPresentationFrame = reader.Read<ulong>();
                if (GraphSource == TimelineRuntimePresentationGraphSource.Unexecuted)
                    Graphs.RestoreRecordedState(ref reader);
                ReadBuffer(ref reader, Candidate);
                ReadBuffer(ref reader, Accepted);
                PendingFrame = reader.Read<bool>() ? ReadFrame(ref reader, Candidate, in start) : default;
                HasCachedFrame = reader.Read<bool>();
                CachedFrame = HasCachedFrame ? ReadFrame(ref reader, Accepted, in start) : default;
            }

            public void Restart(OrchestrationTime startTime, int cycle)
            {
                if (GraphSource == TimelineRuntimePresentationGraphSource.Unexecuted)
                    Graphs.Restart();
                PendingTime = startTime;
                PendingCycle = cycle;
                PendingFinished = false;
                PendingFrame = default;
                CursorTime = startTime;
                Cycle = cycle;
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
                if (GraphSource == TimelineRuntimePresentationGraphSource.Unexecuted)
                    Graphs.Clear();
            }

        }


    }

}
