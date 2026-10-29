#if UNITY_EDITOR
using GraphAsset = UnityEngine.ScriptableObject;
#else
using GraphAsset = System.String;
#endif

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
        GraphAsset m_Graph;
#endif
    }
}
