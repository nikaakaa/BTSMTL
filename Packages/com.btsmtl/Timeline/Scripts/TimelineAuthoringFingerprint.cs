using BTSMTL.Diagnostics;

namespace BTSMTL.Timeline
{
    public static class TimelineAuthoringFingerprint
    {
        public static string Compute(TimelineData timeline)
        {
            return timeline.ContentFingerprint;
        }
    }
}
