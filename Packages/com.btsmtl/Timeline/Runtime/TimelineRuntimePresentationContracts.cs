using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct TimelineRuntimeSample<TScalar> where TScalar : struct
    {
        public TimelineRuntimeSample(int clipIndex, int kindIndex, TScalar weight, TScalar previousSourceTime, TScalar sourceTime,
            TScalar normalizedTime, int cycle, TimelinePresentationSampleReason reason)
        {
            ClipIndex = clipIndex;
            KindIndex = kindIndex;
            Weight = weight;
            PreviousSourceTime = previousSourceTime;
            SourceTime = sourceTime;
            NormalizedTime = normalizedTime;
            Cycle = cycle;
            Reason = reason;
        }

        public int ClipIndex { get; }
        public int KindIndex { get; }
        public TScalar Weight { get; }
        public TScalar PreviousSourceTime { get; }
        public TScalar SourceTime { get; }
        public TScalar NormalizedTime { get; }
        public int Cycle { get; }
        public TimelinePresentationSampleReason Reason { get; }
    }

    public readonly struct TimelineRuntimeTreeClipRequest
    {
        public TimelineRuntimeTreeClipRequest(
            OrchestrationTimelineInvocationToken token,
            TimelineTreeExecutionPhase phase,
            TimelineClipExitSource exitSource,
            TimelineRuntimeTreeClipEventKind eventKind,
            OrchestrationTime time,
            OrchestrationTime previousTime,
            OrchestrationTime windowStartTime,
            float normalizedTime)
        {
            Token = token;
            Phase = phase;
            ExitSource = exitSource;
            EventKind = eventKind;
            Time = time;
            PreviousTime = previousTime;
            WindowStartTime = windowStartTime;
            NormalizedTime = Math.Clamp(normalizedTime, 0f, 1f);
        }

        public OrchestrationTimelineInvocationToken Token { get; }
        public TimelineTreeExecutionPhase Phase { get; }
        public TimelineClipExitSource ExitSource { get; }
        public TimelineRuntimeTreeClipEventKind EventKind { get; }
        public OrchestrationTime Time { get; }
        public OrchestrationTime PreviousTime { get; }
        public OrchestrationTime WindowStartTime { get; }
        public float NormalizedTime { get; }
    }

    public enum TimelineRuntimeTreeClipEventKind : byte
    {
        Enter = 1,
        Update = 2,
        Exit = 3,
        Destroy = 4
    }

    public readonly struct TimelineRuntimePresentationEvent
    {
        public TimelineRuntimePresentationEvent(
            in OrchestrationTimelineStart<OrchestrationTime> start,
            OrchestrationTimelineInvocationToken token,
            OrchestrationTime time)
        {
            Start = start;
            Token = token;
            Time = time;
        }

        public readonly OrchestrationTimelineStart<OrchestrationTime> Start;
        public OrchestrationTimelineInvocationToken Token { get; }
        public ulong TraversalIndex => checked((ulong)Token.Cycle + 1);
        public OrchestrationTime Time { get; }
    }

}
