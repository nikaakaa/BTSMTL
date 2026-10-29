using BTSMTL.Timeline;
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
    }

    public sealed partial class MotionCurveTrack : SampledTrack
    {
        public override string ContractKind => MotionCurveTimelineContracts.TrackKind;
    }

    public sealed partial class MotionCurveClip : SampledClip
    {
        public override string ContractKind => MotionCurveTimelineContracts.ClipKind;
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
