#if UNITY_EDITOR || BTSMTL_AUTHORING
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Timeline
{
    public static class TimelineCurveJson
    {
        public static TimelineCurve Read(JToken source) => source.Type == JTokenType.Null ? null :
            new TimelineCurve((int)source["preWrapMode"], (int)source["postWrapMode"],
                source["keys"].Select(key => new TimelineCurveKey((float)key["time"], (float)key["value"],
                    Tangent(key["inTangent"]), Tangent(key["outTangent"]),
                    (float)key["inWeight"], (float)key["outWeight"], (int)key["weightedMode"], (int)key["tangentMode"])));

        public static JObject Write(TimelineCurve curve) => new JObject
        {
            ["keys"] = new JArray(curve.Keys.Select(key => new JObject
            {
                ["time"] = key.Time, ["value"] = key.Value,
                ["inTangent"] = Tangent(key.InTangent), ["outTangent"] = Tangent(key.OutTangent),
                ["inWeight"] = key.InWeight, ["outWeight"] = key.OutWeight,
                ["weightedMode"] = key.WeightedMode, ["tangentMode"] = key.TangentMode
            })),
            ["preWrapMode"] = curve.PreWrapMode,
            ["postWrapMode"] = curve.PostWrapMode
        };

        static float Tangent(JToken value) => value.Type == JTokenType.Null ? float.PositiveInfinity : (float)value;

        static JValue Tangent(float value) => float.IsInfinity(value) ? JValue.CreateNull() : new JValue(value);
    }
}
#endif
