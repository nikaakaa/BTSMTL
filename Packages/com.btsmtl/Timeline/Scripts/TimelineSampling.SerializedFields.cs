using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using Curve = UnityEngine.AnimationCurve;
#else
using Curve = ThirdPersonSimulation.GameplayAbilityCurve<double>;
#endif

namespace BTSMTL.Timeline
{
    [Serializable]
    public abstract class SampledTrack : Track { }

    public abstract partial class SampledClip : Clip
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        float m_SourceStartTime;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        float m_SourceEndTime;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("weightCurve", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Object,
            ObjectType = typeof(Curve))]
#endif
        public Curve WeightCurve
#if UNITY_5_3_OR_NEWER
            = Curve.Linear(0f, 1f, 1f, 1f)
#endif
            ;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("easeInCurve", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Object,
            ObjectType = typeof(Curve))]
#endif
        public Curve EaseInCurve
#if UNITY_5_3_OR_NEWER
            = Curve.Linear(0f, 0f, 1f, 1f)
#endif
            ;
#if UNITY_5_3_OR_NEWER
        [ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField("easeOutCurve", BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.Object,
            ObjectType = typeof(Curve))]
#endif
        public Curve EaseOutCurve
#if UNITY_5_3_OR_NEWER
            = Curve.Linear(0f, 0f, 1f, 1f)
#endif
            ;
    }
}
