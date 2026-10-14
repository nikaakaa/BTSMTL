using System;
using ThirdPersonSimulation;
using Photon.Deterministic;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineAnimationContribution
    {
        public TimelineAnimationContribution(
            int trackIndex,
            int clipIndex,
            string timelineAuthoringId,
            string trackAuthoringId,
            string clipAuthoringId,
            string sourceId,
            string sourceName,
            string trackName,
            string resourceId,
            AnimationChannelId animationChannelId,
            string animationSlotId,
            string blendProfileId,
            float clipTime,
            float normalizedTime,
            float weight,
            bool isLooping,
            float clipLoopStartTime,
            float clipLoopDuration,
            int cycleIndex)
        {
            TrackIndex = trackIndex;
            ClipIndex = clipIndex;
            TimelineAuthoringId = timelineAuthoringId ?? string.Empty;
            TrackAuthoringId = trackAuthoringId ?? string.Empty;
            ClipAuthoringId = clipAuthoringId ?? string.Empty;
            SourceId = sourceId;
            SourceName = sourceName;
            TrackName = trackName;
            ResourceId = resourceId;
            AnimationChannelId = animationChannelId.IsValid
                ? animationChannelId
                : throw new ArgumentException("Animation Channel identity is invalid.", nameof(animationChannelId));
            AnimationSlotId = animationSlotId?.Trim() ?? string.Empty;
            BlendProfileId = blendProfileId?.Trim() ?? string.Empty;
            ClipTime = clipTime;
            NormalizedTime = normalizedTime;
            Weight = weight;
            IsLooping = isLooping && clipLoopDuration > 0f;
            ClipLoopStartTime = clipLoopStartTime;
            ClipLoopDuration = Math.Max(0f, clipLoopDuration);
            Cycle = Math.Max(0, cycleIndex);
            ContinuousClipTime = IsLooping ? clipTime + Cycle * ClipLoopDuration : clipTime;
        }

        public int TrackIndex { get; }
        public int ClipIndex { get; }
        public string TimelineAuthoringId { get; }
        public string TrackAuthoringId { get; }
        public string ClipAuthoringId { get; }
        public string SourceId { get; }
        public string SourceName { get; }
        public string TrackName { get; }
        public string ResourceId { get; }
        public AnimationChannelId AnimationChannelId { get; }
        public string AnimationSlotId { get; }
        public string BlendProfileId { get; }
        public float ClipTime { get; }
        public float NormalizedTime { get; }
        public float Weight { get; }
        public bool IsLooping { get; }
        public float ClipLoopStartTime { get; }
        public float ClipLoopDuration { get; }
        public int Cycle { get; }
        public float ContinuousClipTime { get; }
    }

    public enum TimelineMotionContributionSpace
    {
        Local,
        World
    }

    public enum TimelineMotionChannel
    {
        Locomotion,
        Action,
        GameplayResult
    }

    public enum TimelineMotionBlendMode
    {
        Additive,
        WeightedBlend,
        Override
    }

    public readonly struct TimelineMotionCurveContribution
    {
        public TimelineMotionCurveContribution(
            string sourceId,
            string sourceName,
            string trackName,
            string curveId,
            string clipAuthoringId,
            TimelineMotionContributionSpace space,
            TimelineMotionChannel channel,
            TimelineMotionBlendMode blendMode,
            FP displacementX,
            FP displacementY,
            FP displacementZ,
            FP yawDegrees,
            int priority,
            FP weight,
            bool consumeLowerChannels,
            FP normalizedTime)
        {
            SourceId = sourceId ?? string.Empty;
            SourceName = sourceName ?? string.Empty;
            TrackName = trackName ?? string.Empty;
            CurveId = curveId ?? string.Empty;
            ClipAuthoringId = clipAuthoringId;
            Space = space;
            Channel = channel;
            BlendMode = blendMode;
            DisplacementX = displacementX;
            DisplacementY = displacementY;
            DisplacementZ = displacementZ;
            YawDegrees = yawDegrees;
            Priority = priority;
            Weight = FPMath.Clamp(weight, FP._0, FP._1);
            ConsumeLowerChannels = consumeLowerChannels;
            NormalizedTime = FPMath.Clamp(normalizedTime, FP._0, FP._1);
        }

        public string SourceId { get; }
        public string SourceName { get; }
        public string TrackName { get; }
        public string CurveId { get; }
        public string ClipAuthoringId { get; }
        public TimelineMotionContributionSpace Space { get; }
        public TimelineMotionChannel Channel { get; }
        public TimelineMotionBlendMode BlendMode { get; }
        public FP DisplacementX { get; }
        public FP DisplacementY { get; }
        public FP DisplacementZ { get; }
        public FP YawDegrees { get; }
        public int Priority { get; }
        public FP Weight { get; }
        public bool ConsumeLowerChannels { get; }
        public FP NormalizedTime { get; }
        public bool HasDelta => Weight > FP._0 && (DisplacementX != FP._0 || DisplacementY != FP._0 || DisplacementZ != FP._0 || YawDegrees != FP._0);
        public bool ClaimsLowerChannels => Weight > FP._0 && BlendMode == TimelineMotionBlendMode.Override && ConsumeLowerChannels;
        public bool CanResolve => HasDelta || ClaimsLowerChannels;
    }

    public enum TimelineCameraMode
    {
        FreeLook,
        Aim,
        LockOn,
        ActionFocus,
        SkillCloseup
    }

    public enum TimelineCameraLookResponseMode
    {
        Full,
        Suppressed,
        Weighted
    }

    public enum TimelineCameraInterruptPolicy
    {
        BlendOut,
        Cut,
        HoldUntilSourceEnds
    }

    public readonly struct TimelineCameraStateSample
    {
        public TimelineCameraStateSample(
            string sourceId,
            string sourceName,
            string trackName,
            string trackAuthoringId,
            string clipAuthoringId,
            string sequenceId,
            TimelineCameraMode mode,
            int priority,
            float weight,
            float blendInSeconds,
            float blendOutSeconds,
            string targetKey,
            TimelineCameraInterruptPolicy interruptPolicy)
        {
            SourceId = sourceId ?? string.Empty;
            SourceName = sourceName ?? string.Empty;
            TrackName = trackName ?? string.Empty;
            TrackAuthoringId = trackAuthoringId;
            ClipAuthoringId = clipAuthoringId;
            SequenceId = sequenceId ?? string.Empty;
            Mode = mode;
            Priority = priority;
            Weight = Math.Clamp(weight, 0f, 1f);
            BlendInSeconds = Math.Max(0f, blendInSeconds);
            BlendOutSeconds = Math.Max(0f, blendOutSeconds);
            TargetKey = targetKey ?? string.Empty;
            InterruptPolicy = interruptPolicy;
        }

        public string SourceId { get; }
        public string SourceName { get; }
        public string TrackName { get; }
        public string TrackAuthoringId { get; }
        public string ClipAuthoringId { get; }
        public string SequenceId { get; }
        public TimelineCameraMode Mode { get; }
        public int Priority { get; }
        public float Weight { get; }
        public float BlendInSeconds { get; }
        public float BlendOutSeconds { get; }
        public string TargetKey { get; }
        public TimelineCameraInterruptPolicy InterruptPolicy { get; }
    }

    public readonly struct TimelineCameraResponseSample
    {
        public TimelineCameraResponseSample(
            string sourceId,
            string sourceName,
            string trackName,
            string trackAuthoringId,
            string clipAuthoringId,
            TimelineCameraLookResponseMode lookResponse,
            float manualOrbitWeight,
            float pitchResponseWeight,
            float yawResponseWeight,
            int priority,
            float weight)
        {
            SourceId = sourceId ?? string.Empty;
            SourceName = sourceName ?? string.Empty;
            TrackName = trackName ?? string.Empty;
            TrackAuthoringId = trackAuthoringId;
            ClipAuthoringId = clipAuthoringId;
            LookResponse = lookResponse;
            ManualOrbitWeight = Math.Clamp(manualOrbitWeight, 0f, 1f);
            PitchResponseWeight = Math.Clamp(pitchResponseWeight, 0f, 1f);
            YawResponseWeight = Math.Clamp(yawResponseWeight, 0f, 1f);
            Priority = priority;
            Weight = Math.Clamp(weight, 0f, 1f);
        }

        public string SourceId { get; }
        public string SourceName { get; }
        public string TrackName { get; }
        public string TrackAuthoringId { get; }
        public string ClipAuthoringId { get; }
        public TimelineCameraLookResponseMode LookResponse { get; }
        public float ManualOrbitWeight { get; }
        public float PitchResponseWeight { get; }
        public float YawResponseWeight { get; }
        public int Priority { get; }
        public float Weight { get; }
    }

    public enum TimelineCameraResourceKind : byte
    {
        Override = 1,
        Zoom = 2,
        Stretch = 3,
        Shot = 4
    }

    public readonly struct TimelineCameraResourceSample
    {
        public TimelineCameraResourceSample(
            string sourceId,
            string sourceName,
            string trackAuthoringId,
            string clipAuthoringId,
            TimelineCameraResourceKind kind,
            string resourceId,
            int priority,
            float weight,
            float normalizedTime,
            float sourceTime,
            string resourceRevision,
            int cycle,
            float sourceStartTime,
            float sourceEndTime,
            bool endsWindow)
        {
            SourceId = sourceId ?? string.Empty;
            SourceName = sourceName ?? string.Empty;
            TrackAuthoringId = trackAuthoringId ?? string.Empty;
            ClipAuthoringId = clipAuthoringId ?? string.Empty;
            Kind = kind;
            ResourceId = resourceId ?? string.Empty;
            Priority = priority;
            Weight = Math.Clamp(weight, 0f, 1f);
            NormalizedTime = Math.Clamp(normalizedTime, 0f, 1f);
            SourceTime = sourceTime;
            ResourceRevision = resourceRevision;
            Cycle = cycle;
            SourceStartTime = sourceStartTime;
            SourceEndTime = sourceEndTime;
            EndsWindow = endsWindow;
        }

        public string SourceId { get; }
        public string SourceName { get; }
        public string TrackAuthoringId { get; }
        public string ClipAuthoringId { get; }
        public TimelineCameraResourceKind Kind { get; }
        public string ResourceId { get; }
        public int Priority { get; }
        public float Weight { get; }
        public float NormalizedTime { get; }
        public float SourceTime { get; }
        public string ResourceRevision { get; }
        public int Cycle { get; }
        public float SourceStartTime { get; }
        public float SourceEndTime { get; }
        public bool EndsWindow { get; }
    }

    public enum ExtraPolationMode
    {
        None = 0,
        Hold = 1,
    }

    [Flags]
    public enum ClipCapabilities
    {
        None = 0,
        Resizable = 0x1,
        Mixable = 0x2,
        ClipInable = 0x4,
        TickQuantized = 0x8,
    }

    public delegate void Evaluate(float deltaTime);

}
