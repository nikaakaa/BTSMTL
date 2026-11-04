using System;
using BTSMTL.Diagnostics;
using BTSMTL.Timeline;
using ThirdPersonGameplay.Orchestration;
using ThirdPersonSimulation;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using SourceCurveAsset = ThirdPersonGameplay.Movement.Timeline.RootMotionCurveAsset;
#else
using SourceCurveAsset = System.String;
#endif

namespace ThirdPersonGameplay.Movement.Timeline
{
    public static partial class MotionCurveTimelineContracts
    {
        public const string TrackKind = "motion-curve.track";
        public const string ClipKind = TimelineContentMotion.Kind;

        public static TimelineTrackContract Track { get; } = new(
            TrackKind,
            TimelineTrackOverlapPolicy.Blend,
            TimelineContentKind.Sampled,
            TimelineExecutionDomain.Logic,
            TimelineOutputKind.LogicFact,
            new TimelineLogicSampleExtension(TimelineContentMotion.MotionCurveSample.Definition.Identity,
                TimelineContentMotion.MotionCurveSample.Definition.SemanticVersion),
            ClipKind);

        public static TimelineClipDefinition Clip { get; } = new(
            ClipKind,
            TrackKind,
            TimelineClipExecutionPhase.Commit,
            TimelineContentKind.Sampled,
            true,
            true,
            TimelineExecutionDomain.Logic,
            TimelineOutputKind.LogicFact);

        public static ITimelineContractProvider CreateProvider(
            TimelineContentParameterCodec<TimelineContentMotion> codec) => new TimelineContractProvider(
            new[] { Track },
            new[] { new TimelineClipContract(Clip, null) },
            new ITimelineContentParameterCodec[] { codec });
    }

    public sealed partial class MotionCurveTrack : SampledTrack
    {
        public override string ContractKind => MotionCurveTimelineContracts.TrackKind;
        public override Type ClipType => typeof(MotionCurveClip);
    }

    public sealed partial class MotionCurveClip : SampledClip, ITimelineContentClosureSource
    {
        public override string ContractKind => MotionCurveTimelineContracts.ClipKind;
        public override ClipCapabilities Capabilities => ClipCapabilities.Resizable | ClipCapabilities.Mixable;
        public override string CurveChannelPrefix => "motion";
        public override TimelineContentSampling CaptureSampling(int weightCurveIndex, int easeInCurveIndex,
            int easeOutCurveIndex) => new TimelineContentSampling(weightCurveIndex, easeInCurveIndex,
                easeOutCurveIndex, TimelineSampleSourceMapping.ClipSecondsClampedToSourceRange,
                ExtraPolationMode.None, TimelineSampleWindow.TraversalWindow, false, 0f);

        public override TimelineContentParameters CaptureContentParameters()
        {
#if UNITY_5_3_OR_NEWER
            TimelineContentCurve x = TimelineContentDiscovery.CaptureSourceCurve("motion.source-x", SourcePositionX);
            TimelineContentCurve y = TimelineContentDiscovery.CaptureSourceCurve("motion.source-y", SourcePositionY);
            TimelineContentCurve z = TimelineContentDiscovery.CaptureSourceCurve("motion.source-z", SourcePositionZ);
            TimelineContentCurve yaw = TimelineContentDiscovery.CaptureSourceCurve("motion.source-yaw", SourceYaw);
#else
            var source = (TimelineContentMotion)ContentResourceParameters;
            TimelineContentCurve x = source.X;
            TimelineContentCurve y = source.Y;
            TimelineContentCurve z = source.Z;
            TimelineContentCurve yaw = source.Yaw;
#endif
            return new TimelineContentMotion(
                $"timeline:{Track.Timeline.AuthoringId}/track:{Track.AuthoringId}/clip:{AuthoringId}",
                CurveId, Space, Channel, BlendMode, Priority, ConsumeLowerChannels, x, y, z, yaw);
        }

        public void CollectContentClosure(TimelineContentClosureBuilder builder)
        {
#if UNITY_5_3_OR_NEWER
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            if (!m_SourceCurve)
            {
                builder.AddError("timeline_motion_curve_missing", AuthoringId, "MotionCurveClip缺少RootMotionCurveAsset正式源引用。");
                return;
            }
            string contentHash = SourceContentHasher.Hash(JsonUtility.ToJson(m_SourceCurve));
            builder.AddDependency($"motion-curve:{contentHash}", GameplayMotionExtensionContracts.RootMotionDependencyKind,
                m_SourceCurve.name, contentHash);
#else
            builder.AddDependency(ContentResourceDependency.Identity, ContentResourceDependency.Kind,
                ContentResourceDependency.SourcePath, ContentResourceDependency.ContentHash);
#endif
        }

#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringConfigure]
        public void Configure(string curveId, SourceCurveAsset sourceCurve, float sourceStartTime,
            float sourceEndTime, SimulationMotionContributionSpace space, SimulationMotionChannel channel,
            SimulationMotionBlendMode blendMode, int priority, bool consumeLowerChannels)
        {
            ConfigureSource(sourceCurve, sourceStartTime, sourceEndTime);
            CurveId = curveId;
            Space = space;
            Channel = channel;
            BlendMode = blendMode;
            Priority = priority;
            ConsumeLowerChannels = consumeLowerChannels;
        }

        public void ConfigureSource(SourceCurveAsset source, float sourceStartTime, float sourceEndTime)
        {
#if UNITY_EDITOR
            if (!source)
                throw new ArgumentNullException(nameof(source));
            if (!source.TryValidate(out string error))
                throw new InvalidOperationException(error);
#endif
            if (!float.IsFinite(sourceStartTime) || !float.IsFinite(sourceEndTime) ||
                sourceStartTime < 0f || sourceEndTime <= sourceStartTime
#if UNITY_EDITOR
                || sourceEndTime > source.Duration
#endif
                )
                throw new ArgumentException("Motion curve source range is invalid.");
            m_SourceCurve = source;
            ConfigureSourceRange(sourceStartTime, sourceEndTime);
#if UNITY_EDITOR
            if (Track?.Timeline != null)
                RebindTimeline();
#endif
        }

        [BTSMTL.Authoring.Graph.GraphAuthoringField("sourceCurve", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Object)]
        [TimelineAuthoringProperty(IsSourceAsset = true)]
#endif
        public SourceCurveAsset SourceCurve => m_SourceCurve;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("curveId", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.String)]
        [TimelineAuthoringProperty(Trimmed = true)]
#endif
        public string CurveId = "MotionCurve";
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("space", typeof(SimulationMotionContributionSpace))]
#endif
        public SimulationMotionContributionSpace Space = SimulationMotionContributionSpace.ActorLocal;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("channel", typeof(SimulationMotionChannel))]
#endif
        public SimulationMotionChannel Channel = SimulationMotionChannel.Action;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("blendMode", typeof(SimulationMotionBlendMode))]
#endif
        public SimulationMotionBlendMode BlendMode = SimulationMotionBlendMode.Override;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("priority", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Integer)]
#endif
        public int Priority = 100;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("consumeLowerChannels", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Boolean)]
#endif
        public bool ConsumeLowerChannels = true;
#if UNITY_5_3_OR_NEWER
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
        SourceCurveAsset m_SourceCurve;
    }
}
