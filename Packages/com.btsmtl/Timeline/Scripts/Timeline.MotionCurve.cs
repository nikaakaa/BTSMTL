using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using BTSMTL.Diagnostics;
using ThirdPersonCharacter.Pipeline.Motion.RootMotion;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [TrackGroup("Base"), ScriptGuid("6f2a51d8c9b34d5f8a0e7b4c2d9f136a"), Ordered(1.5f), Color(126, 220, 146)]
    public sealed class MotionCurveTrack : Track
    {
        public override string ContractKind => TimelineContractKinds.MotionCurveTrack;

#if UNITY_EDITOR
        public override Type ClipType => typeof(MotionCurveClip);

        public override Clip AddClip(UnityEngine.Object referenceObject, FixedScalar time)
        {
            if (referenceObject is not RootMotionCurveAsset source)
                throw new ArgumentException("MotionCurveClip requires a RootMotionCurveAsset source.", nameof(referenceObject));
            MotionCurveClip clip = new MotionCurveClip(this, time, source);
            clip.RegenerateAuthoringIdentity();
            m_Clips.Add(clip);
            return clip;
        }

        public override Clip AddClip(FixedScalar time)
        {
            throw new InvalidOperationException("MotionCurveClip requires a RootMotionCurveAsset source.");
        }
#endif
    }

    [ScriptGuid("6f2a51d8c9b34d5f8a0e7b4c2d9f136a"), Color(126, 220, 146)]
#if UNITY_EDITOR
    [TimelineAuthoringProperty("curveId", TimelineAuthoringPropertyKind.Text, Trimmed = true)]
    [TimelineAuthoringProperty("sourceCurve", TimelineAuthoringPropertyKind.Object)]
    [TimelineAuthoringProperty("sourceStartTime", TimelineAuthoringPropertyKind.Float, HasMinimum = true, Minimum = 0, Finite = true)]
    [TimelineAuthoringProperty("sourceEndTime", TimelineAuthoringPropertyKind.Float, HasMinimum = true, Minimum = 0, Finite = true)]
    [TimelineAuthoringProperty("space", typeof(TimelineMotionContributionSpace))]
    [TimelineAuthoringProperty("channel", typeof(TimelineMotionChannel))]
    [TimelineAuthoringProperty("blendMode", typeof(TimelineMotionBlendMode))]
    [TimelineAuthoringProperty("priority", TimelineAuthoringPropertyKind.Integer)]
    [TimelineAuthoringProperty("consumeLowerChannels", TimelineAuthoringPropertyKind.Boolean)]
#endif
    public sealed partial class MotionCurveClip : Clip, ITimelineContentClosureSource
    {
        public override string ContractKind => TimelineContractKinds.MotionCurveClip;
        public override TimelineContentParameters CaptureContentParameters() => new TimelineContentMotion(
            $"timeline:{Track.Timeline.AuthoringId}/track:{Track.AuthoringId}/clip:{AuthoringId}",
            CurveId, Space, Channel, BlendMode, Priority, ConsumeLowerChannels,
            TimelineContentDiscovery.CaptureSourceCurve("motion.source-x", SourcePositionX),
            TimelineContentDiscovery.CaptureSourceCurve("motion.source-y", SourcePositionY),
            TimelineContentDiscovery.CaptureSourceCurve("motion.source-z", SourcePositionZ),
            TimelineContentDiscovery.CaptureSourceCurve("motion.source-yaw", SourceYaw));

        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public string CurveId = "MotionCurve";
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public TimelineMotionContributionSpace Space = TimelineMotionContributionSpace.Local;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public TimelineMotionChannel Channel = TimelineMotionChannel.Action;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public TimelineMotionBlendMode BlendMode = TimelineMotionBlendMode.Override;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public int Priority = 100;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public bool ConsumeLowerChannels = true;
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        RootMotionCurveAsset m_SourceCurve;
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        float m_SourceStartTime;
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        float m_SourceEndTime;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve WeightCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public RootMotionCurveAsset SourceCurve => m_SourceCurve;
        public float SourceStartTime => m_SourceStartTime;
        public float SourceEndTime => m_SourceEndTime;
        public float SourceDuration => Mathf.Max(0f, m_SourceEndTime - m_SourceStartTime);

        public void CollectContentClosure(TimelineContentClosureBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (!m_SourceCurve)
            {
                builder.AddError("timeline_motion_curve_missing", AuthoringId, "MotionCurveClip缺少RootMotionCurveAsset正式源引用。");
                return;
            }
            string contentHash = SourceContentHasher.Hash(JsonUtility.ToJson(m_SourceCurve));
            builder.AddDependency(
                $"motion-curve:{contentHash}",
                "timeline.motion-curve",
                m_SourceCurve.name,
                contentHash);
        }

        public FixedScalar CurveEndTime => StartTime + FixedScalar.FromDouble(SourceDuration);
        public AnimationCurve ProgramPositionX => ProgramCurve(SourcePositionX);
        public AnimationCurve ProgramPositionY => ProgramCurve(SourcePositionY);
        public AnimationCurve ProgramPositionZ => ProgramCurve(SourcePositionZ);
        public AnimationCurve ProgramYaw => ProgramCurve(RequireSource().LocalYaw);

        public AnimationCurve SourcePositionX => RequireSource().EvaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw
            ? ZeroCurve()
            : RequireSource().LocalPositionX;

        public AnimationCurve SourcePositionY => RequireSource().EvaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw
            ? ZeroCurve()
            : RequireSource().LocalPositionY;

        public AnimationCurve SourcePositionZ => RequireSource().EvaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw
            ? RequireSource().ForwardDistance
            : RequireSource().LocalPositionZ;

        public AnimationCurve SourceYaw => RequireSource().LocalYaw;

        RootMotionCurveAsset RequireSource()
        {
            if (!m_SourceCurve)
                throw new InvalidOperationException($"MotionCurveClip '{CurveId}' requires a RootMotionCurveAsset source.");
            return m_SourceCurve;
        }

#if UNITY_EDITOR
        internal void ApplySourceTimeRange(float sourceStartTime, float sourceEndTime)
        {
            m_SourceStartTime = sourceStartTime;
            m_SourceEndTime = sourceEndTime;
        }

        public void ConfigureSource(RootMotionCurveAsset source, float sourceStartTime, float sourceEndTime)
        {
            if (!source)
                throw new ArgumentNullException(nameof(source));
            if (!source.TryValidate(out string error))
                throw new InvalidOperationException(error);
            if (!float.IsFinite(sourceStartTime) || !float.IsFinite(sourceEndTime) ||
                sourceStartTime < 0f || sourceEndTime <= sourceStartTime || sourceEndTime > source.Duration)
                throw new ArgumentException("Motion curve source range is invalid.");
            m_SourceCurve = source;
            m_SourceStartTime = sourceStartTime;
            m_SourceEndTime = sourceEndTime;
            if (Track?.Timeline != null)
                RebindTimeline();
        }
#endif

        public Vector3 EvaluatePositionAtTimelineTime(FixedScalar timelineTime)
        {
            float sourceTime = Mathf.Clamp(
                m_SourceStartTime + FixedScalar.Max(FixedScalar.Zero, timelineTime - StartTime).ToSingle(),
                m_SourceStartTime,
                m_SourceEndTime);
            return RequireSource().EvaluatePosition(sourceTime);
        }

        public float EvaluateYawAtTimelineTime(FixedScalar timelineTime)
        {
            float sourceTime = Mathf.Clamp(
                m_SourceStartTime + FixedScalar.Max(FixedScalar.Zero, timelineTime - StartTime).ToSingle(),
                m_SourceStartTime,
                m_SourceEndTime);
            return RequireSource().EvaluateYaw(sourceTime);
        }

        public AnimationCurve CreateSourceDisplayCurve(AnimationCurve source, float timelineDuration)
        {
            return CreateSourceDisplayCurve(source, m_SourceStartTime, m_SourceEndTime, timelineDuration);
        }

        public AnimationCurve CreateSourceDisplayCurve(
            AnimationCurve source,
            float sourceStartTime,
            float sourceEndTime,
            float timelineDuration)
        {
            var result = new AnimationCurve();
            UpdateSourceDisplayCurve(result, source, sourceStartTime, sourceEndTime, timelineDuration);
            return result;
        }

        public void UpdateSourceDisplayCurve(AnimationCurve result, AnimationCurve source,
            float sourceStartTime, float sourceEndTime, float timelineDuration)
        {
            for (int i = result.length - 1; i >= 0; i--)
                result.RemoveKey(i);
            if (source == null)
                return;
            if (!float.IsFinite(sourceStartTime) || !float.IsFinite(sourceEndTime) ||
                sourceStartTime < 0f || sourceEndTime <= sourceStartTime ||
                sourceEndTime > RequireSource().Duration)
                throw new InvalidOperationException($"MotionCurveClip '{CurveId}' has an invalid source display range.");
            float sourceDuration = sourceEndTime - sourceStartTime;
            if (sourceDuration <= 0f)
                throw new InvalidOperationException($"MotionCurveClip '{CurveId}' has an invalid source duration.");

            bool hasStartKey = false;
            bool hasEndKey = false;
            int firstKeyAfterStart = -1;
            int lastKeyBeforeEnd = -1;
            for (int index = 0; index < source.length; index++)
            {
                Keyframe key = source[index];
                if (Mathf.Approximately(key.time, sourceStartTime))
                {
                    key.time = 0f;
                    result.AddKey(key);
                    hasStartKey = true;
                    continue;
                }
                if (Mathf.Approximately(key.time, sourceEndTime))
                {
                    key.time = sourceDuration;
                    result.AddKey(key);
                    hasEndKey = true;
                    continue;
                }
                if (key.time > sourceStartTime && firstKeyAfterStart < 0)
                    firstKeyAfterStart = index;
                if (key.time < sourceEndTime)
                    lastKeyBeforeEnd = index;
                if (key.time <= sourceStartTime || key.time >= sourceEndTime)
                    continue;
                key.time -= sourceStartTime;
                result.AddKey(key);
            }
            if (!hasStartKey)
            {
                float tangent = firstKeyAfterStart >= 0 ? source[firstKeyAfterStart].inTangent : 0f;
                result.AddKey(new Keyframe(0f, source.Evaluate(sourceStartTime), tangent, tangent));
            }
            if (!hasEndKey)
            {
                float tangent = lastKeyBeforeEnd >= 0 ? source[lastKeyBeforeEnd].outTangent : 0f;
                result.AddKey(new Keyframe(sourceDuration, source.Evaluate(sourceEndTime), tangent, tangent));
            }
            if (timelineDuration > sourceDuration)
                result.AddKey(new Keyframe(timelineDuration, source.Evaluate(sourceEndTime), 0f, 0f));
            result.preWrapMode = WrapMode.ClampForever;
            result.postWrapMode = WrapMode.ClampForever;
        }

        static AnimationCurve ZeroCurve() => AnimationCurve.Linear(0f, 0f, 1f, 0f);

        public override void Init(Track track)
        {
            base.Init(track);
            RootMotionCurveAsset source = RequireSource();
            if (!source.TryValidate(out string error))
                throw new InvalidOperationException(error);
            if (!float.IsFinite(m_SourceStartTime) || !float.IsFinite(m_SourceEndTime) ||
                m_SourceStartTime < 0f || m_SourceEndTime <= m_SourceStartTime ||
                m_SourceEndTime > source.Duration)
                throw new InvalidOperationException($"MotionCurveClip '{CurveId}' has an invalid source range.");
            if (CurveEndTime <= StartTime || CurveEndTime > EndTime)
                throw new InvalidOperationException($"MotionCurveClip '{CurveId}' requires StartTime < CurveEndTime <= EndTime.");
        }

        AnimationCurve ProgramCurve(AnimationCurve source)
        {
            AnimationCurve result = TimelineCurveAuthoring.CopyCurve(source);
            float duration = SourceDuration;
            if (duration <= 0f)
                throw new InvalidOperationException($"MotionCurveClip '{CurveId}' has an invalid source duration.");
            Keyframe[] keys = result.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                key.time = (key.time - m_SourceStartTime) / duration;
                key.inTangent *= duration;
                key.outTangent *= duration;
                keys[i] = key;
            }
            result.keys = keys;
            return result;
        }

#if UNITY_EDITOR
        public override ClipCapabilities Capabilities => ClipCapabilities.Resizable | ClipCapabilities.Mixable;

        public MotionCurveClip(Track track, FixedScalar time, RootMotionCurveAsset source) : base(track, time)
        {
            if (!source)
                throw new ArgumentNullException(nameof(source));
            if (!source.TryValidate(out string error))
                throw new InvalidOperationException(error);
            if (source.Duration <= 0f)
                throw new ArgumentException("MotionCurve source duration must be positive.", nameof(source));
            ConfigureTimeRange(time, time + FixedScalar.FromDouble(source.Duration));
            m_SourceCurve = source;
            m_SourceStartTime = 0f;
            m_SourceEndTime = source.Duration;
        }
#endif
    }

    public static class MotionCurveTimelineContracts
    {
        public static readonly ITimelineContractProvider Provider = new TimelineContractProvider(
            new[]
            {
                new TimelineTrackContract(
                    TimelineContractKinds.MotionCurveTrack,
                    TimelineTrackOverlapPolicy.Blend,
                    TimelineCapability.BodyMotion,
                    TimelineExecutionDomain.Logic,
                    TimelineOutputKind.GameplayFact,
                    TimelineContractKinds.MotionCurveClip)
            },
            new[]
            {
                new TimelineClipContract(
                    TimelineContractKinds.MotionCurveClip,
                    TimelineContractKinds.MotionCurveTrack,
                    TimelineClipExecutionPhase.Commit,
                    TimelineCapability.BodyMotion,
                    true,
                    true,
                    TimelineExecutionDomain.Logic,
                    TimelineOutputKind.GameplayFact)
            });
    }
}
