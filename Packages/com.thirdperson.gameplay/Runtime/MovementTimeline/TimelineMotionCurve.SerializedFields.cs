using System;
using BTSMTL.Diagnostics;
using BTSMTL.Timeline;
using ThirdPersonGameplay.Orchestration;
using ThirdPersonSimulation;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
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
            TimelineContentCurve x = TimelineCurveContent.CaptureSourceCurve("motion.source-x", SourcePositionX);
            TimelineContentCurve y = TimelineCurveContent.CaptureSourceCurve("motion.source-y", SourcePositionY);
            TimelineContentCurve z = TimelineCurveContent.CaptureSourceCurve("motion.source-z", SourcePositionZ);
            TimelineContentCurve yaw = TimelineCurveContent.CaptureSourceCurve("motion.source-yaw", SourceYaw);
            return new TimelineContentMotion(
                $"timeline:{Track.Timeline.AuthoringId}/track:{Track.AuthoringId}/clip:{AuthoringId}",
                CurveId, Space, Channel, BlendMode, Priority, ConsumeLowerChannels, x, y, z, yaw);
        }

        public void CollectContentClosure(TimelineContentClosureBuilder builder)
        {
            builder.AddDependency($"motion-curve:{SourceContent.ContentRevision}",
                GameplayMotionExtensionContracts.RootMotionDependencyKind, m_SourcePath, SourceContent.ContentRevision);
        }

        public RootMotionCurveContent SourceContent => m_SourceContent;

        public void BindSourceContent(RootMotionCurveContent source, string sourcePath)
        {
            m_SourceContent = source;
            m_SourcePath = sourcePath;
        }

#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringConfigure]
        public void Configure(string curveId, string sourceCurve, float sourceStartTime,
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

        public void ConfigureSource(string source, float sourceStartTime, float sourceEndTime)
        {
            m_SourceCurve = source;
            ConfigureSourceRange(sourceStartTime, sourceEndTime);
        }

        [BTSMTL.Authoring.Graph.GraphAuthoringField("sourceCurve", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.String)]
        [BTSMTL.Authoring.Graph.GraphAuthoringResource("motion-curve", typeof(RootMotionCurveAuthoring),
            Selection = "asset", Profile = "Motion", Source = BTSMTL.Authoring.Graph.GraphAuthoringReferenceSource.AuthoringDocument)]
        [TimelineAuthoringProperty(IsSourceAsset = true, SourceAssetKind = "motion-curve")]
#endif
        public string SourceCurve => m_SourceCurve;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringField("curveId", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.String)]
        [TimelineAuthoringProperty(Trimmed = true)]
#endif
        public string CurveId = "MotionCurve";
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringField("space", typeof(SimulationMotionContributionSpace))]
#endif
        public SimulationMotionContributionSpace Space = SimulationMotionContributionSpace.ActorLocal;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringField("channel", typeof(SimulationMotionChannel))]
#endif
        public SimulationMotionChannel Channel = SimulationMotionChannel.Action;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringField("blendMode", typeof(SimulationMotionBlendMode))]
#endif
        public SimulationMotionBlendMode BlendMode = SimulationMotionBlendMode.Override;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringField("priority", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Integer)]
#endif
        public int Priority = 100;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringField("consumeLowerChannels", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Boolean)]
#endif
        public bool ConsumeLowerChannels = true;
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringField("sourceStartTime", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Float, HasMinimum = true, Minimum = 0, Finite = true)]
        [TimelineAuthoringProperty(SourceBoundary = TimelineAuthoringSourceBoundary.Start)]
#endif
        public new float SourceStartTime => base.SourceStartTime;
#if UNITY_EDITOR || BTSMTL_AUTHORING
        [BTSMTL.Authoring.Graph.GraphAuthoringField("sourceEndTime", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Float, HasMinimum = true, Minimum = 0, Finite = true)]
        [TimelineAuthoringProperty(SourceBoundary = TimelineAuthoringSourceBoundary.End)]
#endif
        public new float SourceEndTime => base.SourceEndTime;
#if UNITY_5_3_OR_NEWER
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
        string m_SourceCurve;
        [NonSerialized] RootMotionCurveContent m_SourceContent;
        [NonSerialized] string m_SourcePath;
    }
}
