using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public enum TimelineRuntimeStepDecision : byte
    {
        Commit = 1,
        Discard = 2
    }

    public readonly struct TimelineRuntimeStepContext
    {
        internal TimelineRuntimeStepContext(
            TimelineRuntimePlayback playback,
            TimelineRuntimeAdvanceRequest request,
            TimelineRuntimeAdvanceResult advance)
        {
            Playback = playback;
            Request = request;
            Advance = advance;
        }

        public TimelineRuntimePlayback Playback { get; }
        public TimelineRuntimeAdvanceRequest Request { get; }
        public readonly TimelineRuntimeAdvanceResult Advance;
    }

    public interface ITimelineRuntimeStepConsumer
    {
        TimelineRuntimeStepDecision Consume(TimelineRuntimeStepContext context);
    }

    public interface ITimelineRuntimeEvaluationSink
    {
        bool Consume(TimelineRuntimeStepContext context);
        void Commit(TimelineRuntimeStepContext context);
        void Discard(TimelineRuntimeStepContext context);
        bool ConsumeStop(TimelineRuntimeStopRequest request);
        void CommitStop(TimelineRuntimeStopRequest request);
        void DiscardStop(TimelineRuntimeStopRequest request);
    }

    public interface ITimelineRuntimeTreeClipService
    {
        bool Consume(TimelineRuntimeTreeClipRequest request, TimelineRuntimeStepContext context);
        void Discard(TimelineRuntimeTreeClipRequest request, TimelineRuntimeStepContext context);
        void Commit(TimelineRuntimeStepContext context);
        void DiscardStep(TimelineRuntimeStepContext context);
        bool ConsumeStop(TimelineRuntimeStopRequest request);
        void CommitStop(TimelineRuntimeStopRequest request);
        void DiscardStop(TimelineRuntimeStopRequest request);
    }

    public interface ITimelineRuntimeMarkerService
    {
        bool Consume(TimelineRuntimeMarkerRequest request, TimelineRuntimeStepContext context);
        void Discard(TimelineRuntimeMarkerRequest request, TimelineRuntimeStepContext context);
        void Commit(TimelineRuntimeStepContext context);
        void DiscardStep(TimelineRuntimeStepContext context);
        bool ConsumeStop(TimelineRuntimeStopRequest request);
        void CommitStop(TimelineRuntimeStopRequest request);
        void DiscardStop(TimelineRuntimeStopRequest request);
    }

    public interface ITimelineRuntimeStopConsumer
    {
        bool ConsumeStop(TimelineRuntimeStopRequest request);
    }

    public interface ITimelineRuntimeStepCommitConsumer
    {
        void Commit(TimelineRuntimeStepContext context);
        void Discard(TimelineRuntimeStepContext context);
    }

    public interface ITimelineRuntimeStopCommitConsumer
    {
        void CommitStop(TimelineRuntimeStopRequest request);
        void DiscardStop(TimelineRuntimeStopRequest request);
    }

    public readonly struct TimelineRuntimeStopRequest
    {
        readonly ulong m_Sequence;

        internal TimelineRuntimeStopRequest(
            TimelineRuntimePlayback playback,
            TimelinePlaybackStopContext reason)
        {
            Playback = playback ?? throw new ArgumentNullException(nameof(playback));
            Reason = reason;
            m_Sequence = playback.PendingStopSequence;
        }

        public TimelineRuntimePlayback Playback { get; }
        public TimelinePlaybackStopContext Reason { get; }
        public TimelineRuntimePlaybackHandle Handle => Playback.Handle;
        public TimelineExecutionIdentity ExecutionIdentity => Playback.ExecutionIdentity;
        public ulong Generation => Playback.Generation;
        public ulong Sequence => m_Sequence;
        internal bool IsPendingFor(TimelineRuntimePlayback playback) =>
            ReferenceEquals(Playback, playback) && m_Sequence != 0 &&
            m_Sequence == playback.PendingStopSequence;
    }

}
