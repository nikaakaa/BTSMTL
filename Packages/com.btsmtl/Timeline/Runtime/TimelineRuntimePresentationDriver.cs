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

    public readonly struct TimelineRuntimePresentationPlayback
    {
        internal TimelineRuntimePresentationPlayback(in OrchestrationTimelineStart<OrchestrationTime> start,
            OrchestrationTime time, int cycle, bool finished, bool retainForCorrection)
        {
            Start = start;
            Time = time;
            Cycle = cycle;
            Finished = finished;
            RetainForCorrection = retainForCorrection;
        }

        public OrchestrationTimelineStart<OrchestrationTime> Start { get; }
        public OrchestrationTime Time { get; }
        public int Cycle { get; }
        public bool Finished { get; }
        public bool RetainForCorrection { get; }
    }

    public sealed class TimelineRuntimePresentationDriver
    {
        const int RecordedStateHeaderV4 = -4;
        readonly int m_PlaybackCapacity;
        readonly Dictionary<OrchestrationTimelineStart<OrchestrationTime>, PresentationPlaybackState> m_Playbacks;
        readonly Dictionary<(TimelineContentUnit Timeline, int SourceIdentityId, OrchestrationTimelinePlaybackMode Mode), Stack<PresentationPlaybackState>> m_RecycledPlaybacks = new();
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
                        item.Value.CachedFrame.RetainForCorrection);
                }
            }

            public bool MoveNext() => m_Enumerator.MoveNext();
            public void Dispose() => m_Enumerator.Dispose();
        }

        public TimelineRuntimePresentationDriver(int playbackCapacity)
        {
            m_PlaybackCapacity = playbackCapacity;
            m_Playbacks = new Dictionary<OrchestrationTimelineStart<OrchestrationTime>, PresentationPlaybackState>(playbackCapacity);
            var writer = new RecordedRuntimeStateCountingWriter();
            WriteRecordedState(ref writer);
            MaximumRecordedStateBytes = writer.WrittenBytes;
        }

        public void Prepare(TimelineRuntimePreparedContent content,
            OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent,
            int sourceIdentityId, Predicate<string> supportsSampleKind)
        {
            if (executionContent.Domain != TimelineExecutionDomain.Presentation ||
                executionContent.ContentIdentityId != content.ContentIdentityId ||
                executionContent.ContentRevisionId != content.ContentRevisionId)
                throw new ArgumentException("表现播放必须装配本次调用的同版本 Presentation 执行内容。", nameof(executionContent));
            var key = (content.Content, sourceIdentityId, executionContent.Mode);
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
            var recycled = new Stack<PresentationPlaybackState>(m_PlaybackCapacity);
            for (int i = 0; i < m_PlaybackCapacity; i++)
                recycled.Push(new PresentationPlaybackState(content, executionContent, recycled));
            var writer = new RecordedRuntimeStateCountingWriter();
            if (m_PlaybackCapacity != 0)
            {
                WritePlayback(ref writer, default, recycled.Peek(), true);
                writer.Write(default(OrchestrationTimelineStart<OrchestrationTime>));
            }
            int recordedBytes = checked(writer.WrittenBytes * m_PlaybackCapacity);
            int recordedTexts = checked(writer.TextReferenceCount * m_PlaybackCapacity);
            m_RecycledPlaybacks.Add(key, recycled);
            MaximumRecordedStateBytes = checked(MaximumRecordedStateBytes + recordedBytes);
            MaximumRecordedTextReferences = checked(MaximumRecordedTextReferences + recordedTexts);
            m_Playbacks.EnsureCapacity(checked(m_RecycledPlaybacks.Count * m_PlaybackCapacity));
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

        PresentationPlaybackState GetPlaybackState(TimelineRuntimePreparedContent content,
            in OrchestrationTimelineStart<OrchestrationTime> start, OrchestrationTime startTime, int cycle)
        {
            if (!m_Playbacks.TryGetValue(start, out PresentationPlaybackState state))
            {
                var key = (content.Content, start.SourceIdentityId, start.Mode);
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
                state.PendingTreeExits.AsSpan(0, state.PendingTreeExitCount), request.Token.ClipOrdinal, request.Token.Cycle))
                return false;
            OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock>.AppendExit(
                state.PendingTreeExits, ref state.PendingTreeExitCount, new OrchestrationTimelineTreeExitRange
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
                    Array.Copy(state.PendingMarkerLastTraversal, state.MarkerLastTraversal, state.MarkerLastTraversal.Length);
                    state.PendingTreeExits.AsSpan(0, state.PendingTreeExitCount).CopyTo(state.TreeExits);
                    state.TreeExitCount = state.PendingTreeExitCount;
                    state.TreeInitialBoundaryState = state.PendingTreeInitialBoundaryState;
                    state.CursorTime = state.PendingTime;
                    state.Cycle = state.PendingCycle;
                    state.Finished = state.PendingFinished;
                    state.InitialBoundaryConsumed = state.PendingInitialBoundaryConsumed;
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
            var writer = new RecordedRuntimeStateCountingWriter();
            WriteRecordedState(ref writer);
            MaximumRecordedStateBytes = writer.WrittenBytes;
            MaximumRecordedTextReferences = writer.TextReferenceCount;
        }

        public void WriteRecordedState<TWriter>(ref TWriter writer) where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(RecordedStateHeaderV4);
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
            if (reader.Read<int>() != RecordedStateHeaderV4)
                throw new InvalidOperationException("Recorded Timeline presentation state version is not supported.");
            int count = reader.Read<int>();
            if (count < 0 || count > checked(m_RecycledPlaybacks.Count * m_PlaybackCapacity))
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
                string contentIdentity = reader.ReadText();
                string contentRevision = reader.ReadText();
                TimelineRuntimePreparedContent prepared = RequireRecordedContent(contents, contentIdentity, contentRevision);
                if (!m_RecycledPlaybacks.TryGetValue((prepared.Content, start.SourceIdentityId, start.Mode), out Stack<PresentationPlaybackState> recycled) ||
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
            bool includeStartBoundary,
            TimelineRuntimeSampleBuffer<TimelineRuntimePresentationEvent> events)
        {
            int count = state.Scheduler.Markers(state.ExecutionContent.Markers.Span,
                previousTime, previousCycle, currentTime, currentCycle, content.Content.Duration,
                includeStartBoundary, state.Scheduled);
            for (int index = 0; index < count; index++)
            {
                ref readonly TimelineScheduledInvocation<OrchestrationTime> marker = ref state.Scheduled[index];
                ulong traversalIndex = checked((ulong)marker.Cycle + 1);
                if (state.PendingMarkerLastTraversal[marker.Ordinal] >= traversalIndex)
                    continue;
                state.PendingMarkerLastTraversal[marker.Ordinal] = traversalIndex;
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

        static void AppendTreeClips(TimelineRuntimePreparedContent content, int playbackSlot,
            in OrchestrationTimelineStart<OrchestrationTime> start, ulong presentationFrame, PresentationPlaybackState state,
            in TimelineRuntimePresentationSample sample, bool traverse, TimelinePresentationSampleReason reason)
        {
            bool stopped = reason == TimelinePresentationSampleReason.Stopped || reason == TimelinePresentationSampleReason.Withdrawn;
            bool correction = state.SampleReason == TimelinePresentationSampleReason.Correction ||
                reason == TimelinePresentationSampleReason.Correction ||
                reason == TimelinePresentationSampleReason.Advance && !traverse;
            var native = state.ExecutionContent;
            bool align = native.Duration > OrchestrationTime.Zero &&
                (correction || state.TreeInitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending &&
                    (!traverse || sample.Cycle == state.Cycle && sample.Time == state.CursorTime));
            var playback = new OrchestrationTimelinePlaybackState<OrchestrationTime>
            {
                Start = start,
                Progress = new()
                {
                    CursorTime = state.CursorTime,
                    Cycle = state.Cycle,
                    InitialBoundaryState = state.TreeInitialBoundaryState,
                    State = state.Finished ? OrchestrationTimelinePlaybackStatus.Completed : OrchestrationTimelinePlaybackStatus.Running
                }
            };
            ReadOnlySpan<TimelineSchedulingClip<OrchestrationTime>> clips = native.Clips.Span;
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> exits = state.TreeExits.AsSpan(0, state.TreeExitCount);
            int activeCount = OrchestrationTimelineStateQueries<OrchestrationTime>.CopyActiveTrees<OrchestrationTimeClock>(
                native, in playback, exits, state.ActiveTrees);
            ReadOnlySpan<TimelineSchedulingBoundary<OrchestrationTime>> active = state.ActiveTrees.AsSpan(0, activeCount);
            exits.CopyTo(state.PendingTreeExits);
            state.PendingTreeExitCount = state.TreeExitCount;
            state.PendingTreeInitialBoundaryState = state.TreeInitialBoundaryState;
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
                state.PendingTreeExitCount = 0;
                if (sample.Cycle > 0)
                    for (int ordinal = 0; ordinal < clips.Length; ordinal++)
                        if (clips[ordinal].Enabled && clips[ordinal].Tree && clips[ordinal].TreeDecision)
                            OrchestrationTimelineRuntime<OrchestrationTime, OrchestrationTimeClock>.AppendExit(
                                state.PendingTreeExits, ref state.PendingTreeExitCount, new OrchestrationTimelineTreeExitRange
                                {
                                    ClipOrdinal = ordinal,
                                    FirstCycle = 0,
                                    LastCycle = sample.Cycle - 1
                                });
            }
            ReadOnlySpan<OrchestrationTimelineTreeExitRange> pendingExits =
                state.PendingTreeExits.AsSpan(0, state.PendingTreeExitCount);
            int scheduledCount = 0;
            if (align)
            {
                int boundaryCount = 0;
                Array.Clear(state.PreviousActiveClips, 0, state.PreviousActiveClips.Length);
                for (int ordinal = 0; ordinal < clips.Length; ordinal++)
                    if (clips[ordinal].Tree &&
                        TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock>.Active(
                            in clips[ordinal], ordinal, sample.Time, sample.Cycle, pendingExits))
                        state.Boundaries[boundaryCount++] = new TimelineSchedulingBoundary<OrchestrationTime>(
                            ordinal, clips[ordinal].SortOrdinal, sample.Time, sample.Cycle, true);
                scheduledCount = state.Scheduler.Trees(clips, native.TreeOrdinals.Span,
                    state.Boundaries.AsSpan(0, boundaryCount), state.PreviousActiveClips,
                    sample.Time, sample.Cycle, sample.Time, sample.Cycle, native.Duration,
                    false, pendingExits, state.SelectedTrees, state.Scheduled, 0);
            }
            else if (traverse)
            {
                for (int ordinal = 0; ordinal < clips.Length; ordinal++)
                    state.PreviousActiveClips[ordinal] =
                        TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock>.Active(
                            in clips[ordinal], ordinal, state.CursorTime, state.Cycle, exits);
                int boundaryCount = state.Scheduler.Boundaries(clips, state.CursorTime, state.Cycle,
                    sample.Time, sample.Cycle, native.Duration, start.Mode == OrchestrationTimelinePlaybackMode.Loop,
                    state.TreeInitialBoundaryState == OrchestrationTimelineInitialBoundaryState.Pending, exits,
                    ReadOnlySpan<OrchestrationTimelineClipOccurrence>.Empty, state.Boundaries, 0);
                scheduledCount = state.Scheduler.Trees(clips, native.TreeOrdinals.Span,
                    state.Boundaries.AsSpan(0, boundaryCount), state.PreviousActiveClips,
                    state.CursorTime, state.Cycle, sample.Time, sample.Cycle, native.Duration,
                    start.Mode == OrchestrationTimelinePlaybackMode.Loop, exits,
                    state.SelectedTrees, state.Scheduled, 0);
                scheduledCount += state.Scheduler.ContinueTrees(clips, active,
                    state.CursorTime, state.Cycle, sample.Time, sample.Cycle, native.Duration,
                    exits, state.Scheduled.AsSpan(scheduledCount));
            }
            state.Scheduler.SortTrees(state.Scheduled, 0, scheduledCount);
            ReadOnlySpan<TimelineScheduledInvocation<OrchestrationTime>> scheduled = state.Scheduled.AsSpan(0, scheduledCount);
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
            state.PendingTreeInitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Applied;
            playback.Progress.CursorTime = sample.Time;
            playback.Progress.Cycle = sample.Cycle;
            playback.Progress.InitialBoundaryState = state.PendingTreeInitialBoundaryState;
            playback.Progress.State = OrchestrationTimelinePlaybackStatus.Running;
            activeCount = OrchestrationTimelineStateQueries<OrchestrationTime>.CopyActiveTrees<OrchestrationTimeClock>(
                native, in playback, pendingExits, state.ActiveTrees);
            for (int index = 0; index < activeCount; index++)
            {
                TimelineSchedulingBoundary<OrchestrationTime> occurrence = state.ActiveTrees[index];
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
                            state.PendingTreeExits, ref state.PendingTreeExitCount, new OrchestrationTimelineTreeExitRange
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

        sealed class PresentationPlaybackState
        {
            readonly Stack<PresentationPlaybackState> m_Recycled;

            public PresentationPlaybackState(
                TimelineRuntimePreparedContent prepared,
                OrchestrationTimelinePreparedContent<OrchestrationTime> executionContent,
                Stack<PresentationPlaybackState> recycled)
            {
                m_Recycled = recycled;
                Prepared = prepared;
                ExecutionContent = executionContent;
                int markerCount = prepared.Content.Markers.Count;
                Candidate = new TimelineRuntimePresentationBuffer(prepared, executionContent);
                Accepted = new TimelineRuntimePresentationBuffer(prepared, executionContent);
                MarkerLastTraversal = new ulong[markerCount];
                PendingMarkerLastTraversal = new ulong[markerCount];
                TreeExits = new OrchestrationTimelineTreeExitRange[executionContent.RequiredTreeExits];
                PendingTreeExits = new OrchestrationTimelineTreeExitRange[executionContent.RequiredTreeExits];
                int traversals = executionContent.Mode == OrchestrationTimelinePlaybackMode.Loop
                    ? OrchestrationTimelineLimits.MaximumCycleAdvance + 1 : 1;
                Boundaries = new TimelineSchedulingBoundary<OrchestrationTime>[checked(2 * executionContent.Clips.Length * traversals)];
                Scheduled = new TimelineScheduledInvocation<OrchestrationTime>[executionContent.TickCapacity.GraphInvocationsPerAdvance];
                ActiveTrees = new TimelineSchedulingBoundary<OrchestrationTime>[executionContent.RequiredActiveTrees];
                PreviousActiveClips = new bool[executionContent.Clips.Length];
                SelectedTrees = new bool[executionContent.Clips.Length];
            }

            public readonly TimelineRuntimePreparedContent Prepared;
            public readonly OrchestrationTimelinePreparedContent<OrchestrationTime> ExecutionContent;
            public readonly TimelineRuntimeScheduling<OrchestrationTime, OrchestrationTimeClock> Scheduler = new();
            public readonly TimelineSchedulingBoundary<OrchestrationTime>[] Boundaries;
            public readonly TimelineScheduledInvocation<OrchestrationTime>[] Scheduled;
            public readonly TimelineSchedulingBoundary<OrchestrationTime>[] ActiveTrees;
            public readonly bool[] PreviousActiveClips;
            public readonly bool[] SelectedTrees;
            public readonly OrchestrationTimelineTreeExitRange[] TreeExits;
            public readonly OrchestrationTimelineTreeExitRange[] PendingTreeExits;
            public int TreeExitCount;
            public int PendingTreeExitCount;
            public OrchestrationTimelineInitialBoundaryState TreeInitialBoundaryState;
            public OrchestrationTimelineInitialBoundaryState PendingTreeInitialBoundaryState;
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
            public OrchestrationTime CursorTime;
            public int Cycle;
            public bool InitialBoundaryConsumed;
            public TimelinePresentationSampleReason SampleReason;
            public bool Finished;
            public TimelineRuntimePresentationFrame CachedFrame;
            public ulong LastPresentationFrame;
            public bool HasCachedFrame;

            public void WriteRecordedState<TWriter>(ref TWriter writer, bool maximum) where TWriter : struct, IRecordedRuntimeStateWriter
            {
                writer.Write(CursorTime);
                writer.Write(Cycle);
                writer.Write(InitialBoundaryConsumed);
                writer.Write(SampleReason);
                writer.Write(Finished);
                writer.Write(PendingTime);
                writer.Write(PendingCycle);
                writer.Write(PendingFinished);
                writer.Write(PendingInitialBoundaryConsumed);
                writer.Write(LastPresentationFrame);
                writer.WriteSpan<ulong>(MarkerLastTraversal);
                writer.WriteSpan<ulong>(PendingMarkerLastTraversal);
                writer.Write(TreeExitCount);
                writer.Write(PendingTreeExitCount);
                writer.WriteSpan<OrchestrationTimelineTreeExitRange>(TreeExits);
                writer.WriteSpan<OrchestrationTimelineTreeExitRange>(PendingTreeExits);
                writer.Write(TreeInitialBoundaryState);
                writer.Write(PendingTreeInitialBoundaryState);
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
                InitialBoundaryConsumed = reader.Read<bool>();
                SampleReason = reader.Read<TimelinePresentationSampleReason>();
                Finished = reader.Read<bool>();
                PendingTime = reader.Read<OrchestrationTime>();
                PendingCycle = reader.Read<int>();
                PendingFinished = reader.Read<bool>();
                PendingInitialBoundaryConsumed = reader.Read<bool>();
                LastPresentationFrame = reader.Read<ulong>();
                reader.ReadSpan<ulong>(MarkerLastTraversal.Length).CopyTo(MarkerLastTraversal);
                reader.ReadSpan<ulong>(PendingMarkerLastTraversal.Length).CopyTo(PendingMarkerLastTraversal);
                TreeExitCount = reader.Read<int>();
                PendingTreeExitCount = reader.Read<int>();
                reader.ReadSpan<OrchestrationTimelineTreeExitRange>(TreeExits.Length).CopyTo(TreeExits);
                reader.ReadSpan<OrchestrationTimelineTreeExitRange>(PendingTreeExits.Length).CopyTo(PendingTreeExits);
                TreeInitialBoundaryState = reader.Read<OrchestrationTimelineInitialBoundaryState>();
                PendingTreeInitialBoundaryState = reader.Read<OrchestrationTimelineInitialBoundaryState>();
                ReadBuffer(ref reader, Candidate);
                ReadBuffer(ref reader, Accepted);
                PendingFrame = reader.Read<bool>() ? ReadFrame(ref reader, Candidate, in start) : default;
                HasCachedFrame = reader.Read<bool>();
                CachedFrame = HasCachedFrame ? ReadFrame(ref reader, Accepted, in start) : default;
            }

            public void Restart(OrchestrationTime startTime, int cycle)
            {
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
                TreeExitCount = 0;
                PendingTreeExitCount = 0;
                TreeInitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Pending;
                PendingTreeInitialBoundaryState = OrchestrationTimelineInitialBoundaryState.Pending;
            }

        }


    }

}
