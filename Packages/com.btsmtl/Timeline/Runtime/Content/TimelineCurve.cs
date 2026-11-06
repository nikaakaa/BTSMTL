using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    [Serializable]
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct TimelineCurveKey
    {
        public TimelineCurveKey(float time, float value, float inTangent, float outTangent,
            float inWeight, float outWeight, int weightedMode, int tangentMode)
        {
            Time = time;
            Value = value;
            InTangent = inTangent;
            OutTangent = outTangent;
            InWeight = inWeight;
            OutWeight = outWeight;
            WeightedMode = weightedMode;
            TangentMode = tangentMode;
        }

        public float Time { get; }
        public float Value { get; }
        public float InTangent { get; }
        public float OutTangent { get; }
        public float InWeight { get; }
        public float OutWeight { get; }
        public int WeightedMode { get; }
        public int TangentMode { get; }
    }

    [Serializable]
    [ValueStorage(UseConstructor = true)]
    public sealed partial class TimelineCurve
    {
        public TimelineCurve(int preWrapMode, int postWrapMode, IEnumerable<TimelineCurveKey> keys)
        {
            PreWrapMode = preWrapMode;
            PostWrapMode = postWrapMode;
            Keys = new ReadOnlyCollection<TimelineCurveKey>(new List<TimelineCurveKey>(keys));
        }

        public int PreWrapMode { get; }
        public int PostWrapMode { get; }
        public IReadOnlyList<TimelineCurveKey> Keys { get; }
    }
}
