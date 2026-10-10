#if UNITY_EDITOR
using System;
using ThirdPersonCamera;
using ThirdPersonCharacter.Pipeline.Motion.RootMotion;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public enum TimelineAuthoringPropertyKind : byte
    {
        Text,
        Boolean,
        Integer,
        Float,
        Enum,
        Vector2,
        Object
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public sealed class TimelineAuthoringPropertyAttribute : Attribute
    {
        public TimelineAuthoringPropertyAttribute(
            string propertyId,
            TimelineAuthoringPropertyKind kind)
        {
            PropertyId = propertyId ?? string.Empty;
            Kind = kind;
        }

        public TimelineAuthoringPropertyAttribute(string propertyId, Type enumType)
            : this(propertyId, TimelineAuthoringPropertyKind.Enum)
        {
            EnumType = enumType;
        }

        public string PropertyId { get; }
        public TimelineAuthoringPropertyKind Kind { get; }
        public Type EnumType { get; }
        public bool Optional { get; set; }
        public bool Trimmed { get; set; }
        public bool HasMinimum { get; set; }
        public double Minimum { get; set; }
        public bool HasMaximum { get; set; }
        public double Maximum { get; set; }
        public bool Finite { get; set; }
    }


    public sealed class TimelineAuthoringClipConfiguration
    {
        public ExtraPolationMode Extrapolation { get; set; }
        public string BlendProfileId { get; set; }
        public string CurveId { get; set; }
        public RootMotionCurveAsset SourceCurve { get; set; }
        public float SourceStartTime { get; set; }
        public float SourceEndTime { get; set; }
        public TimelineMotionContributionSpace Space { get; set; }
        public TimelineMotionChannel Channel { get; set; }
        public TimelineMotionBlendMode BlendMode { get; set; }
        public int Priority { get; set; }
        public bool ConsumeLowerChannels { get; set; }
        public TimelineCameraMode CameraMode { get; set; }
        public float CameraBlendInSeconds { get; set; }
        public float CameraBlendOutSeconds { get; set; }
        public string CameraTargetKey { get; set; }
        public TimelineCameraInterruptPolicy CameraInterruptPolicy { get; set; }
        public string CameraSequenceId { get; set; }
        public string CameraResourceId { get; set; }
        public CameraEffectAsset CameraEffect { get; set; }
        public AnimationCurve CameraWeightCurve { get; set; }
        public AnimationCurve CameraEaseInCurve { get; set; }
        public AnimationCurve CameraEaseOutCurve { get; set; }
        public TimelineCameraLookResponseMode CameraLookResponse { get; set; }
        public float ManualOrbitWeight { get; set; }
        public float PitchResponseWeight { get; set; }
        public float YawResponseWeight { get; set; }
        public string TargetBindingId { get; set; }
        public string ParameterBindingId { get; set; }
        public AnimationCurve ValueCurve { get; set; }
    }

    public static class TimelineAuthoringClipBinding
    {
        public static UnityEngine.Object SourceAsset(Clip clip)
        {
            if (clip is MotionCurveClip motion)
                return motion.SourceCurve;
            if (clip is AnimationClip animation)
                return animation.Clip;
            return null;
        }

        public static TimelineAuthoringClipConfiguration Read(Clip clip)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            var result = new TimelineAuthoringClipConfiguration();
            if (clip is AnimationClip animation)
            {
                result.Extrapolation = animation.ExtraPolationMode;
                result.BlendProfileId = animation.BlendProfileId;
            }
            if (clip is MotionCurveClip motion)
            {
                result.CurveId = motion.CurveId;
                result.SourceCurve = motion.SourceCurve;
                result.SourceStartTime = motion.SourceStartTime;
                result.SourceEndTime = motion.SourceEndTime;
                result.Space = motion.Space;
                result.Channel = motion.Channel;
                result.BlendMode = motion.BlendMode;
                result.Priority = motion.Priority;
                result.ConsumeLowerChannels = motion.ConsumeLowerChannels;
            }
            if (clip is CameraStateClip cameraState)
            {
                result.CameraMode = cameraState.Mode;
                result.CameraSequenceId = cameraState.SequenceId;
                result.Priority = cameraState.Priority;
                result.CameraBlendInSeconds = cameraState.BlendInSeconds;
                result.CameraBlendOutSeconds = cameraState.BlendOutSeconds;
                result.CameraTargetKey = cameraState.TargetKey;
                result.CameraInterruptPolicy = cameraState.InterruptPolicy;
            }
            if (clip is CameraEffectClip cameraResource)
            {
                result.CameraWeightCurve = cameraResource.WeightCurve;
                result.CameraEaseInCurve = cameraResource.EaseInCurve;
                result.CameraEaseOutCurve = cameraResource.EaseOutCurve;
                result.CameraResourceId = cameraResource.Effect ? cameraResource.Effect.EffectId : null;
                result.CameraEffect = cameraResource.Effect;
            }
            if (clip is CameraResponseClip cameraResponse)
            {
                result.CameraLookResponse = cameraResponse.LookResponse;
                result.ManualOrbitWeight = cameraResponse.ManualOrbitWeight;
                result.PitchResponseWeight = cameraResponse.PitchResponseWeight;
                result.YawResponseWeight = cameraResponse.YawResponseWeight;
                result.Priority = cameraResponse.Priority;
            }
            if (clip is ScenePresentationParameterCurveClip sceneParameter)
            {
                result.TargetBindingId = sceneParameter.TargetBindingId;
                result.ParameterBindingId = sceneParameter.ParameterBindingId;
                result.ValueCurve = sceneParameter.ValueCurve;
            }
            return result;
        }

        public static void Configure(
            TimelineData timeline,
            Clip clip,
            TimelineAuthoringClipConfiguration configuration)
        {
            if (timeline == null)
                throw new ArgumentNullException(nameof(timeline));
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            if (clip is AnimationClip animation)
            {
                animation.ExtraPolationMode = configuration.Extrapolation;
                animation.BlendProfileId = configuration.BlendProfileId;
            }
            if (clip is MotionCurveClip motion)
            {
                motion.CurveId = configuration.CurveId;
                motion.ConfigureSource(
                    configuration.SourceCurve,
                    configuration.SourceStartTime,
                    configuration.SourceEndTime);
                motion.Space = configuration.Space;
                motion.Channel = configuration.Channel;
                motion.BlendMode = configuration.BlendMode;
                motion.Priority = configuration.Priority;
                motion.ConsumeLowerChannels = configuration.ConsumeLowerChannels;
            }
            if (clip is CameraStateClip cameraState)
            {
                cameraState.Mode = configuration.CameraMode;
                cameraState.Priority = configuration.Priority;
                cameraState.BlendInSeconds = configuration.CameraBlendInSeconds;
                cameraState.BlendOutSeconds = configuration.CameraBlendOutSeconds;
                cameraState.TargetKey = configuration.CameraTargetKey;
                cameraState.InterruptPolicy = configuration.CameraInterruptPolicy;
                cameraState.SequenceId = configuration.CameraSequenceId;
            }
            if (clip is CameraResponseClip cameraResponse)
            {
                cameraResponse.LookResponse = configuration.CameraLookResponse;
                cameraResponse.ManualOrbitWeight = configuration.ManualOrbitWeight;
                cameraResponse.PitchResponseWeight = configuration.PitchResponseWeight;
                cameraResponse.YawResponseWeight = configuration.YawResponseWeight;
                cameraResponse.Priority = configuration.Priority;
                cameraResponse.WeightCurve = configuration.CameraWeightCurve;
                cameraResponse.EaseInCurve = configuration.CameraEaseInCurve;
                cameraResponse.EaseOutCurve = configuration.CameraEaseOutCurve;
            }
            if (clip is CameraEffectClip cameraEffect)
            {
                cameraEffect.Effect = configuration.CameraEffect;
                cameraEffect.WeightCurve = configuration.CameraWeightCurve;
                cameraEffect.EaseInCurve = configuration.CameraEaseInCurve;
                cameraEffect.EaseOutCurve = configuration.CameraEaseOutCurve;
            }
            if (clip is ScenePresentationParameterCurveClip sceneParameter)
                sceneParameter.ConfigureBindings(
                    configuration.TargetBindingId,
                    configuration.ParameterBindingId,
                    configuration.ValueCurve);
        }
    }
}
#endif
