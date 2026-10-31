#if UNITY_5_3_OR_NEWER
using UnityEngine;
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
        public override ClipCapabilities Capabilities =>
            m_ExitSource == TimelineClipExitSource.TreeDecision
                ? ClipCapabilities.TickQuantized
                : ClipCapabilities.Resizable | ClipCapabilities.TickQuantized;
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
        string m_AssetTree = string.Empty;
#endif
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        bool m_HasDeclaredEndTime;
    }
}
