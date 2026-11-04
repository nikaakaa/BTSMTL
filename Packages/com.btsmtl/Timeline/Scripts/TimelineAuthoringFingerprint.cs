using BTSMTL.Diagnostics;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace BTSMTL.Timeline
{
    public static class TimelineAuthoringFingerprint
    {
        public static string Compute(TimelineData timeline)
        {
#if UNITY_5_3_OR_NEWER
            return timeline == null ? string.Empty : SourceContentHasher.Hash(JsonUtility.ToJson(timeline));
#else
            return timeline.ContentFingerprint;
#endif
        }
    }
}
