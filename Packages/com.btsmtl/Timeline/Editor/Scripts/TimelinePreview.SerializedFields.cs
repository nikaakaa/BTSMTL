using System.Collections.Generic;
using BTSMTL.Authoring.Graph;
#if UNITY_EDITOR
using Vector3 = UnityEngine.Vector3;
using InputTimelineAsset = BTSMTL.Timeline.TimelineAsset;
#else
using Vector3 = System.Numerics.Vector3;
using InputTimelineAsset = System.String;
#endif

namespace BTSMTL.Timeline.Editor
{
    public sealed partial class TimelinePreviewInputSchemes
    {
        [GraphAuthoringField(nameof(Owner), GraphAuthoringFieldValueKind.String)]
        public string Owner;
        [GraphAuthoringField(nameof(Timeline), GraphAuthoringFieldValueKind.String)]
        public string Timeline;
        [GraphAuthoringField(nameof(Schemes), GraphAuthoringFieldValueKind.Object)]
        public List<TimelineAssetPreviewPreferences> Schemes = new();
        [GraphAuthoringField(nameof(Selected), GraphAuthoringFieldValueKind.Integer, HasMinimum = true, Minimum = 0)]
        public int Selected;
    }

    public sealed partial class TimelineAssetPreviewPreferences
    {
        [GraphAuthoringField(nameof(Name), GraphAuthoringFieldValueKind.String)]
        public string Name = "默认";
        [GraphAuthoringField(nameof(InputTimeline), GraphAuthoringFieldValueKind.AssetReference, Optional = true)]
        public InputTimelineAsset InputTimeline;
        [GraphAuthoringField(nameof(HasTarget), GraphAuthoringFieldValueKind.Boolean)]
        public bool HasTarget;
        [GraphAuthoringField(nameof(TargetPosition), GraphAuthoringFieldValueKind.Vector3)]
        public Vector3 TargetPosition;
        [GraphAuthoringField(nameof(TargetEuler), GraphAuthoringFieldValueKind.Vector3)]
        public Vector3 TargetEuler;
        [GraphAuthoringField(nameof(StartPosition), GraphAuthoringFieldValueKind.Vector3)]
        public Vector3 StartPosition;
        [GraphAuthoringField(nameof(StartYaw), GraphAuthoringFieldValueKind.Float)]
        public float StartYaw;
        [GraphAuthoringField(nameof(RangeEnd), GraphAuthoringFieldValueKind.Float)]
        public float RangeEnd = 10f;
    }
}
