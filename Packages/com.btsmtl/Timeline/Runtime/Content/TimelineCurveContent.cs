using System.Collections.Generic;

namespace BTSMTL.Timeline
{
    public static class TimelineCurveContent
    {
        public static TimelineContentCurve CaptureSourceCurve(string channelId, TimelineCurve curve) =>
            Capture(channelId, channelId, TimelineCurveTimeDomain.SourceSeconds,
                TimelineCurveValueDomain.Unbounded(0f, string.Empty), curve);

        public static TimelineContentCurve Capture(string channelId, string displayName,
            TimelineCurveTimeDomain timeDomain, TimelineCurveValueDomain valueDomain, TimelineCurve curve)
        {
            IReadOnlyList<TimelineCurveKey> source = curve.Keys;
            var keys = new TimelineContentCurveKey[source.Count];
            for (int i = 0; i < keys.Length; i++)
            {
                TimelineCurveKey key = source[i];
                keys[i] = new TimelineContentCurveKey(key.Time, key.Value, key.InTangent, key.OutTangent,
                    key.InWeight, key.OutWeight, key.WeightedMode);
            }
            return new TimelineContentCurve(channelId, displayName, timeDomain, valueDomain,
                curve.PreWrapMode, curve.PostWrapMode, keys);
        }
    }
}
