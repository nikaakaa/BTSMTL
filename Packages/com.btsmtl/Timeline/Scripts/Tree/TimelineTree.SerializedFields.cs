#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif
#if UNITY_EDITOR
using GraphAsset = UnityEngine.ScriptableObject;
#else
using GraphAsset = System.String;
#endif

namespace BTSMTL.Timeline
{
    public partial class TreeTrack : Track
    {
        public override string ContractKind => TimelineContractKinds.TreeTrack;
    }

    public partial class TreeClip : Clip
    {
        public override string ContractKind => TimelineContractKinds.TreeClip;
#if UNITY_5_3_OR_NEWER
        [SerializeField, ShowInInspector, OnValueChanged("OnClipChanged", "RepaintInspector")]
#endif
        TimelineTreeExecutionPhase m_ExecutionPhase = TreeTimelineDefinition.DefaultExecutionPhase;
#if UNITY_5_3_OR_NEWER
        [SerializeField, ShowInInspector, OnValueChanged("OnClipChanged", "RepaintInspector")]
#endif
        TimelineClipExitSource m_ExitSource = TreeTimelineDefinition.DefaultExitSource;
#if UNITY_EDITOR || BTSMTL_AUTHORING
#if UNITY_EDITOR
        [SerializeField]
#endif
        GraphAsset m_AssetTree;
#endif
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        bool m_HasDeclaredEndTime;
    }
}
