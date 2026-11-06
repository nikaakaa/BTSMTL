using System.Reflection;
using UnityEngine;

namespace BTSMTL.Timeline.Unity
{
    public static class TimelineCurveUnity
    {
        static readonly PropertyInfo s_TangentMode = typeof(Keyframe).GetProperty("tangentMode");

        public static TimelineCurve Capture(AnimationCurve curve)
        {
            if (curve == null)
                return null;
            Keyframe[] source = curve.keys;
            var keys = new TimelineCurveKey[source.Length];
            for (int index = 0; index < source.Length; index++)
                keys[index] = Capture(source[index]);
            return new TimelineCurve((int)curve.preWrapMode, (int)curve.postWrapMode, keys);
        }

        public static TimelineCurveKey Capture(Keyframe key) => new TimelineCurveKey(key.time, key.value,
            key.inTangent, key.outTangent, key.inWeight, key.outWeight, (int)key.weightedMode,
            (int)s_TangentMode.GetValue(key));

        public static AnimationCurve Create(TimelineCurve curve)
        {
            if (curve == null)
                return null;
            var keys = new Keyframe[curve.Keys.Count];
            for (int index = 0; index < keys.Length; index++)
                keys[index] = Create(curve.Keys[index]);
            return new AnimationCurve(keys)
            {
                preWrapMode = (WrapMode)curve.PreWrapMode,
                postWrapMode = (WrapMode)curve.PostWrapMode
            };
        }

        public static Keyframe Create(TimelineCurveKey key)
        {
            object result = new Keyframe(key.Time, key.Value, key.InTangent, key.OutTangent,
                key.InWeight, key.OutWeight) { weightedMode = (WeightedMode)key.WeightedMode };
            s_TangentMode.SetValue(result, key.TangentMode);
            return (Keyframe)result;
        }
    }
}
