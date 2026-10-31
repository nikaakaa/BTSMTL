namespace BTSMTL.Timeline
{
    public sealed partial class TimelineMarker
    {
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField, UnityEngine.HideInInspector]
#endif
        string m_AuthoringId;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        long m_TimeFPRawValue;
#if UNITY_5_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        bool m_ForceTriggerOnInitialSeek;
#if UNITY_EDITOR || BTSMTL_AUTHORING
#if UNITY_EDITOR
        [UnityEngine.SerializeField]
#endif
        string m_Graph = string.Empty;
#endif
    }
}
