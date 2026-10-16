using System;
using BTSMTL;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using BTSMTL.Diagnostics;
using BTSMTL.Timeline;
using ThirdPersonGameplay.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace ThirdPersonGameplay.Movement.Timeline
{
    [TrackGroup("Base"), ScriptGuid("6f2a51d8c9b34d5f8a0e7b4c2d9f136a"), Ordered(1.5f), Color(126, 220, 146)]
    public sealed class MotionCurveTrack : SampledTrack
    {
        public override string ContractKind => MotionCurveTimelineContracts.TrackKind;

#if UNITY_EDITOR
        public override Type ClipType => typeof(MotionCurveClip);

        public override Clip AddClip(UnityEngine.Object referenceObject, OrchestrationTime time)
        {
            if (referenceObject is not RootMotionCurveAsset source)
                throw new ArgumentException("MotionCurveClip requires a RootMotionCurveAsset source.", nameof(referenceObject));
            MotionCurveClip clip = new MotionCurveClip(this, time, source);
            clip.RegenerateAuthoringIdentity();
            m_Clips.Add(clip);
            return clip;
        }

        public override Clip AddClip(OrchestrationTime time)
        {
            throw new InvalidOperationException("MotionCurveClip requires a RootMotionCurveAsset source.");
        }
#endif
    }

    [ScriptGuid("6f2a51d8c9b34d5f8a0e7b4c2d9f136a"), Color(126, 220, 146)]
#if UNITY_EDITOR
    [TimelineAuthoringProperty("curveId", TimelineAuthoringPropertyKind.Text, MemberName = nameof(MotionCurveClip.CurveId), Trimmed = true)]
    [TimelineAuthoringProperty("sourceCurve", TimelineAuthoringPropertyKind.Object, MemberName = nameof(MotionCurveClip.SourceCurve), IsSourceAsset = true)]
    [TimelineAuthoringProperty("sourceStartTime", TimelineAuthoringPropertyKind.Float, MemberName = nameof(MotionCurveClip.SourceStartTime), HasMinimum = true, Minimum = 0, Finite = true)]
    [TimelineAuthoringProperty("sourceEndTime", TimelineAuthoringPropertyKind.Float, MemberName = nameof(MotionCurveClip.SourceEndTime), HasMinimum = true, Minimum = 0, Finite = true)]
    [TimelineAuthoringProperty("space", typeof(SimulationMotionContributionSpace), MemberName = nameof(MotionCurveClip.Space))]
    [TimelineAuthoringProperty("channel", typeof(SimulationMotionChannel), MemberName = nameof(MotionCurveClip.Channel))]
    [TimelineAuthoringProperty("blendMode", typeof(SimulationMotionBlendMode), MemberName = nameof(MotionCurveClip.BlendMode))]
    [TimelineAuthoringProperty("priority", TimelineAuthoringPropertyKind.Integer, MemberName = nameof(MotionCurveClip.Priority))]
    [TimelineAuthoringProperty("consumeLowerChannels", TimelineAuthoringPropertyKind.Boolean, MemberName = nameof(MotionCurveClip.ConsumeLowerChannels))]
#endif
    public sealed partial class MotionCurveClip : SampledClip, ITimelineContentClosureSource,
        ITimelineClipTimeRangeValidator
#if UNITY_EDITOR
        , ITimelineAuthoringClipSource, ITimelineAuthoringReferenceCurveSource,
        ITimelineAuthoringClipSplitSource, ITimelineAuthoringSourceTimeEditing
#endif
    {
        public override string ContractKind => MotionCurveTimelineContracts.ClipKind;
        public override string CurveChannelPrefix => "motion";
        public override TimelineContentSampling CaptureSampling(int weightCurveIndex, int easeInCurveIndex,
            int easeOutCurveIndex) => new TimelineContentSampling(weightCurveIndex, easeInCurveIndex,
                easeOutCurveIndex, TimelineSampleSourceMapping.ClipSecondsClampedToSourceRange,
                ExtraPolationMode.None, TimelineSampleWindow.TraversalWindow, false, 0f);

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
        public SimulationMotionContributionSpace Space = SimulationMotionContributionSpace.ActorLocal;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public SimulationMotionChannel Channel = SimulationMotionChannel.Action;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public SimulationMotionBlendMode BlendMode = SimulationMotionBlendMode.Override;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public int Priority = 100;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public bool ConsumeLowerChannels = true;
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        RootMotionCurveAsset m_SourceCurve;

        public RootMotionCurveAsset SourceCurve => m_SourceCurve;
        public float SourceDuration => Mathf.Max(0f, SourceEndTime - SourceStartTime);

        public bool TryValidateTimeRange(in TimelineClipTimeRange range, out string error)
        {
            if (!float.IsFinite(range.SourceStart) || !float.IsFinite(range.SourceEnd) ||
                range.SourceStart < 0f || range.SourceEnd <= range.SourceStart ||
                range.SourceEnd > SourceCurve.Duration ||
                OrchestrationTime.FromSeconds(range.SourceEnd - range.SourceStart) > range.End - range.Start)
            {
                error = $"Motion Clip '{Name}' 的源区间必须位于素材内，并且不能长于时间条。";
                return false;
            }
            error = string.Empty;
            return true;
        }

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
                GameplayMotionExtensionContracts.RootMotionDependencyKind,
                m_SourceCurve.name,
                contentHash);
        }

        public OrchestrationTime CurveEndTime => StartTime + OrchestrationTime.FromSeconds(SourceDuration);
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
        public UnityEngine.Object SourceAsset => SourceCurve;
        public Type SourceAssetType => typeof(RootMotionCurveAsset);
        public string SourceMissingErrorCode => "timeline_motion_curve_missing";
        public string SourceMissingError => "MotionCurveClip缺少RootMotionCurveAsset正式源引用。";
        public float GetSourceDuration(UnityEngine.Object source) => ((RootMotionCurveAsset)source).Duration;
        public bool CanSplit => true;

        public IReadOnlyList<TimelineReferenceCurveDescriptor> ReferenceCurves => new[]
        {
            ReferenceCurve("source.position-x", "Position X", SourcePositionX),
            ReferenceCurve("source.position-y", "Position Y", SourcePositionY),
            ReferenceCurve("source.position-z", "Position Z", SourcePositionZ),
            ReferenceCurve("source.yaw", "Yaw", SourceYaw)
        };

        TimelineReferenceCurveDescriptor ReferenceCurve(string channelId, string displayName, AnimationCurve source) =>
            new TimelineReferenceCurveDescriptor(channelId, displayName, SourceCurve.name,
                (target, sourceStart, sourceEnd, duration) =>
                    UpdateSourceDisplayCurve(target, source, sourceStart, sourceEnd, duration));

        public override void ApplyAuthoringConfiguration(TimelineAuthoringClipConfiguration configuration)
        {
            string curveId = configuration.Get<string>("curveId");
            RootMotionCurveAsset source = configuration.Get<RootMotionCurveAsset>("sourceCurve");
            float sourceStart = configuration.Get<float>("sourceStartTime");
            float sourceEnd = configuration.Get<float>("sourceEndTime");
            SimulationMotionContributionSpace space = configuration.Get<SimulationMotionContributionSpace>("space");
            SimulationMotionChannel channel = configuration.Get<SimulationMotionChannel>("channel");
            SimulationMotionBlendMode blend = configuration.Get<SimulationMotionBlendMode>("blendMode");
            int priority = configuration.Get<int>("priority");
            bool consumeLower = configuration.Get<bool>("consumeLowerChannels");
            ConfigureSource(source, sourceStart, sourceEnd);
            CurveId = curveId;
            Space = space;
            Channel = channel;
            BlendMode = blend;
            Priority = priority;
            ConsumeLowerChannels = consumeLower;
        }

        public void SplitSource(Clip copy, OrchestrationTime splitTime)
        {
            var motionCopy = (MotionCurveClip)copy;
            float splitSourceTime = SourceStartTime + (splitTime - StartTime).AsSingle;
            float originalSourceEnd = SourceEndTime;
            ConfigureSource(SourceCurve, SourceStartTime, splitSourceTime);
            motionCopy.ConfigureSource(motionCopy.SourceCurve, splitSourceTime, originalSourceEnd);
        }

        public void AdjustSourceRange(in TimelineClipTimeRange original, double originalStart,
            double currentStart, double currentEnd, bool trimStart,
            out float sourceStart, out float sourceEnd)
        {
            sourceStart = original.SourceStart;
            double duration = Math.Max(0d, currentEnd - currentStart);
            if (trimStart)
                sourceStart = (float)Math.Max(0d,
                    Math.Min(original.SourceEnd, original.SourceStart + currentStart - originalStart));
            sourceEnd = (float)Math.Min(SourceCurve.Duration, sourceStart + duration);
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
            ConfigureSourceRange(sourceStartTime, sourceEndTime);
            if (Track?.Timeline != null)
                RebindTimeline();
        }
#endif

        public Vector3 EvaluatePositionAtTimelineTime(OrchestrationTime timelineTime)
        {
            float sourceTime = Mathf.Clamp(
                SourceStartTime + OrchestrationTime.Max(OrchestrationTime.Zero, timelineTime - StartTime).AsSingle,
                SourceStartTime,
                SourceEndTime);
            return RequireSource().EvaluatePosition(sourceTime);
        }

        public float EvaluateYawAtTimelineTime(OrchestrationTime timelineTime)
        {
            float sourceTime = Mathf.Clamp(
                SourceStartTime + OrchestrationTime.Max(OrchestrationTime.Zero, timelineTime - StartTime).AsSingle,
                SourceStartTime,
                SourceEndTime);
            return RequireSource().EvaluateYaw(sourceTime);
        }

        public AnimationCurve CreateSourceDisplayCurve(AnimationCurve source, float timelineDuration)
        {
            return CreateSourceDisplayCurve(source, SourceStartTime, SourceEndTime, timelineDuration);
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
            if (!float.IsFinite(SourceStartTime) || !float.IsFinite(SourceEndTime) ||
                SourceStartTime < 0f || SourceEndTime <= SourceStartTime ||
                SourceEndTime > source.Duration)
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
                key.time = (key.time - SourceStartTime) / duration;
                key.inTangent *= duration;
                key.outTangent *= duration;
                keys[i] = key;
            }
            result.keys = keys;
            return result;
        }

#if UNITY_EDITOR
        public override ClipCapabilities Capabilities => ClipCapabilities.Resizable | ClipCapabilities.Mixable;

        public MotionCurveClip(Track track, OrchestrationTime time) : base(track, time)
        {
        }

        public MotionCurveClip(Track track, OrchestrationTime time, RootMotionCurveAsset source) : this(track, time)
        {
            if (!source)
                throw new ArgumentNullException(nameof(source));
            if (!source.TryValidate(out string error))
                throw new InvalidOperationException(error);
            if (source.Duration <= 0f)
                throw new ArgumentException("MotionCurve source duration must be positive.", nameof(source));
            ConfigureTimeRange(time, time + OrchestrationTime.FromSeconds(source.Duration));
            m_SourceCurve = source;
            ConfigureSourceRange(0f, source.Duration);
        }
#endif
    }

    public static class MotionCurveTimelineContracts
    {
        public const string TrackKind = "motion-curve.track";
        public const string ClipKind = TimelineContentMotion.Kind;

        public static readonly ITimelineContractProvider Provider = new TimelineContractProvider(
            new[]
            {
                new TimelineTrackContract(
                    TrackKind,
                    TimelineTrackOverlapPolicy.Blend,
                    TimelineContentKind.Sampled,
                    TimelineExecutionDomain.Logic,
                    TimelineOutputKind.LogicFact,
                    new TimelineLogicSampleExtension(TimelineContentMotion.MotionCurveSample.Definition.Identity,
                        TimelineContentMotion.MotionCurveSample.Definition.SemanticVersion),
                    ClipKind)
            },
            new[]
            {
                new TimelineClipContract(
                    ClipKind,
                    TrackKind,
                    TimelineClipExecutionPhase.Commit,
                    TimelineContentKind.Sampled,
                    true,
                    true,
                    TimelineExecutionDomain.Logic,
                    TimelineOutputKind.LogicFact)
            });
    }
}
