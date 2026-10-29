using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace BTSMTL.Timeline
{
    public sealed partial class TimelineData
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField, HideInInspector]
#endif
        string m_AuthoringId;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        string m_Name = "Timeline";
#if UNITY_5_3_OR_NEWER
        [SerializeReference]
#endif
        List<Track> m_Tracks = new List<Track>();
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        List<TimelineExternalBindingDeclaration> m_ExternalBindings = new List<TimelineExternalBindingDeclaration>();
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        bool m_Loop;
    }

    public abstract partial class Track
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField, HideInInspector]
#endif
        string m_AuthoringId;
#if UNITY_EDITOR
        [BTSMTL.Authoring.Graph.GraphAuthoringField(nameof(Name), BTSMTL.Authoring.Graph.GraphAuthoringFieldValueKind.String,
            DocumentMember = BTSMTL.Authoring.Graph.GraphAuthoringDocumentMember.DisplayName)]
#endif
        public string Name;
        public virtual string ContractKind => string.Empty;
#if UNITY_5_3_OR_NEWER
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
#endif
        TimelineExecutionDomain m_ExecutionDomain;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        protected bool m_PersistentMuted;
#if UNITY_5_3_OR_NEWER
        [SerializeReference]
#endif
        protected List<Clip> m_Clips = new List<Clip>();
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        List<TimelineMarker> m_Markers = new List<TimelineMarker>();
    }

    public abstract partial class Clip
    {
#if UNITY_5_3_OR_NEWER
        [SerializeField, HideInInspector]
#endif
        string m_AuthoringId;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        long m_StartTimeFPRawValue;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        long m_EndTimeFPRawValue;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        long m_OtherEaseInTimeFPRawValue;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        long m_OtherEaseOutTimeFPRawValue;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        long m_SelfEaseInTimeFPRawValue;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        long m_SelfEaseOutTimeFPRawValue;
#if UNITY_5_3_OR_NEWER
        [SerializeField]
#endif
        long m_ClipInTimeFPRawValue;
        public virtual string ContractKind => string.Empty;
    }
}
