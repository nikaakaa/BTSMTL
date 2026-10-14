using System;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    public abstract class TimelineContentParameters
    {
    }

    [ValueVariant(1)]
    public sealed class TimelineContentAnimation : TimelineContentParameters
    {
        public TimelineContentAnimation(string resourceId, AnimationChannelId channelId, string slotId,
            string blendProfileId, ExtraPolationMode extrapolation)
        {
            ResourceId = resourceId;
            ChannelId = channelId;
            SlotId = slotId;
            BlendProfileId = blendProfileId;
            Extrapolation = extrapolation;
        }

        public string ResourceId { get; }
        public AnimationChannelId ChannelId { get; }
        public string SlotId { get; }
        public string BlendProfileId { get; }
        public ExtraPolationMode Extrapolation { get; }
    }

    [ValueVariant(2)]
    public sealed class TimelineContentMotion : TimelineContentParameters
    {
        public TimelineContentMotion(string resourceId, string curveId, TimelineMotionContributionSpace space,
            TimelineMotionChannel channel, TimelineMotionBlendMode blendMode, int priority, bool consumeLowerChannels,
            TimelineContentCurve x, TimelineContentCurve y, TimelineContentCurve z, TimelineContentCurve yaw)
        {
            ResourceId = resourceId;
            CurveId = curveId;
            Space = space;
            Channel = channel;
            BlendMode = blendMode;
            Priority = priority;
            ConsumeLowerChannels = consumeLowerChannels;
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public string ResourceId { get; }
        public string CurveId { get; }
        public TimelineMotionContributionSpace Space { get; }
        public TimelineMotionChannel Channel { get; }
        public TimelineMotionBlendMode BlendMode { get; }
        public int Priority { get; }
        public bool ConsumeLowerChannels { get; }
        public TimelineContentCurve X { get; }
        public TimelineContentCurve Y { get; }
        public TimelineContentCurve Z { get; }
        public TimelineContentCurve Yaw { get; }
    }

    [ValueVariant(3)]
    public sealed class TimelineContentTree : TimelineContentParameters
    {
        public TimelineContentTree(bool hasDeclaredEndTime) => HasDeclaredEndTime = hasDeclaredEndTime;
        public bool HasDeclaredEndTime { get; }
    }

    [ValueVariant(4)]
    public sealed class TimelineContentCameraState : TimelineContentParameters
    {
        public TimelineContentCameraState(TimelineCameraMode mode, string sequenceId, int priority,
            float blendInSeconds, float blendOutSeconds, string targetKey, TimelineCameraInterruptPolicy interruptPolicy)
        {
            Mode = mode;
            SequenceId = sequenceId;
            Priority = priority;
            BlendInSeconds = blendInSeconds;
            BlendOutSeconds = blendOutSeconds;
            TargetKey = targetKey;
            InterruptPolicy = interruptPolicy;
        }

        public TimelineCameraMode Mode { get; }
        public string SequenceId { get; }
        public int Priority { get; }
        public float BlendInSeconds { get; }
        public float BlendOutSeconds { get; }
        public string TargetKey { get; }
        public TimelineCameraInterruptPolicy InterruptPolicy { get; }
    }

    [ValueVariant(5)]
    public sealed class TimelineContentCameraResponse : TimelineContentParameters
    {
        public TimelineContentCameraResponse(TimelineCameraLookResponseMode lookResponse, float manualOrbitWeight,
            float pitchResponseWeight, float yawResponseWeight, int priority)
        {
            LookResponse = lookResponse;
            ManualOrbitWeight = manualOrbitWeight;
            PitchResponseWeight = pitchResponseWeight;
            YawResponseWeight = yawResponseWeight;
            Priority = priority;
        }

        public TimelineCameraLookResponseMode LookResponse { get; }
        public float ManualOrbitWeight { get; }
        public float PitchResponseWeight { get; }
        public float YawResponseWeight { get; }
        public int Priority { get; }
    }

    [ValueVariant(6)]
    public sealed class TimelineContentCameraEffect : TimelineContentParameters
    {
        public TimelineContentCameraEffect(TimelineCameraResourceKind kind, string resourceId, int priority,
            float sourceStartTime, float sourceEndTime, string resourceRevision)
        {
            Kind = kind;
            ResourceId = resourceId;
            Priority = priority;
            SourceStartTime = sourceStartTime;
            SourceEndTime = sourceEndTime;
            ResourceRevision = resourceRevision;
        }

        public TimelineCameraResourceKind Kind { get; }
        public string ResourceId { get; }
        public int Priority { get; }
        public float SourceStartTime { get; }
        public float SourceEndTime { get; }
        public string ResourceRevision { get; }
    }

    [ValueVariant(7)]
    public sealed class TimelineContentSceneParameter : TimelineContentParameters
    {
        public TimelineContentSceneParameter(string targetBindingId, string parameterBindingId,
            TimelineBindingValueKind valueKind)
        {
            TargetBindingId = targetBindingId;
            ParameterBindingId = parameterBindingId;
            ValueKind = valueKind;
        }

        public string TargetBindingId { get; }
        public string ParameterBindingId { get; }
        public TimelineBindingValueKind ValueKind { get; }
    }
}
