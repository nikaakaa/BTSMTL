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
        // 场景方案不属于技能或独立 Timeline（Owner、Timeline 为空），由场景包与其中的目标角色确定运行对象。
        [GraphAuthoringField(nameof(Scene), GraphAuthoringFieldValueKind.String)]
        public string Scene;
        [GraphAuthoringField(nameof(Actor), GraphAuthoringFieldValueKind.String)]
        public string Actor;
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
        // 打开时播放器在开局前把目标角色放到起始位置与朝向；关闭时沿用场景原出生位姿。
        [GraphAuthoringField(nameof(HasStart), GraphAuthoringFieldValueKind.Boolean)]
        public bool HasStart;
        [GraphAuthoringField(nameof(StartPosition), GraphAuthoringFieldValueKind.Vector3)]
        public Vector3 StartPosition;
        [GraphAuthoringField(nameof(StartYaw), GraphAuthoringFieldValueKind.Float)]
        public float StartYaw;
        [GraphAuthoringField(nameof(RangeEnd), GraphAuthoringFieldValueKind.Float)]
        public float RangeEnd = 10f;
    }
}
