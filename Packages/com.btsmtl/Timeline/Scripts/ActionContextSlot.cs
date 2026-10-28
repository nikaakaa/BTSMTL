#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace BTSMTL.Timeline
{
#if UNITY_5_3_OR_NEWER
    [CreateAssetMenu(fileName = "ActionContext", menuName = "BTSMTL/Timeline/Action Context")]
#endif
    public sealed class ActionContextSlot
#if UNITY_5_3_OR_NEWER
        : ScriptableObject
#endif
    {
    }
}
