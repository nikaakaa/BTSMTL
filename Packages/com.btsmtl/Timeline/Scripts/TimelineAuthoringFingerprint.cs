using BTSMTL.Diagnostics;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public static class TimelineAuthoringFingerprint
    {
        public static string Compute(TimelineData timeline)
        {
            return timeline == null ? string.Empty : SourceContentHasher.Hash(JsonUtility.ToJson(timeline));
        }
    }
}
