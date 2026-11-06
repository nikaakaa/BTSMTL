#if UNITY_EDITOR || BTSMTL_AUTHORING
using BTSMTL.Timeline;
using Newtonsoft.Json.Linq;
using ThirdPersonSimulation;

namespace ThirdPersonGameplay.Movement.Timeline
{
    public static class RootMotionCurveContentJson
    {
        public static RootMotionCurveContent Read(JObject source, string contentRevision)
        {
            JObject total = (JObject)source["totalLocalPosition"];
            return new RootMotionCurveContent((string)source["m_AuthoringId"], contentRevision,
                (string)source["m_Name"], (string)source["sourceAnimationIdentity"],
                (string)source["sourceAnimationRevision"], (bool)source["sourceLooping"],
                (float)source["duration"], (float)source["sampleRate"],
                (RootMotionCurveEvaluationMode)(int)source["evaluationMode"],
                TimelineCurveJson.Read(source["localPositionX"]), TimelineCurveJson.Read(source["localPositionY"]),
                TimelineCurveJson.Read(source["localPositionZ"]), TimelineCurveJson.Read(source["forwardDistance"]),
                TimelineCurveJson.Read(source["localYaw"]),
                new Float32Vector3(Float32Scalar.FromSingle((float)total["x"]),
                    Float32Scalar.FromSingle((float)total["y"]), Float32Scalar.FromSingle((float)total["z"])),
                (float)source["totalForwardDistance"], (float)source["totalYaw"], (string)source["authoredSource"]);
        }

        public static JObject Write(RootMotionCurveContent content, string sourceIdentity, string authoringScope) =>
            new JObject
            {
                ["m_AuthoringId"] = content.AuthoringId,
                ["m_SourceIdentity"] = sourceIdentity,
                ["m_ContentDomain"] = "motion-curve",
                ["m_AuthoringScope"] = authoringScope,
                ["m_Name"] = content.DisplayName,
                ["sourceAnimationIdentity"] = content.SourceAnimationIdentity,
                ["sourceAnimationRevision"] = content.SourceAnimationRevision,
                ["sourceLooping"] = content.SourceLooping,
                ["duration"] = content.Duration,
                ["sampleRate"] = content.SampleRate,
                ["evaluationMode"] = (int)content.EvaluationMode,
                ["localPositionX"] = TimelineCurveJson.Write(content.LocalPositionX),
                ["localPositionY"] = TimelineCurveJson.Write(content.LocalPositionY),
                ["localPositionZ"] = TimelineCurveJson.Write(content.LocalPositionZ),
                ["forwardDistance"] = TimelineCurveJson.Write(content.ForwardDistance),
                ["localYaw"] = TimelineCurveJson.Write(content.LocalYaw),
                ["totalLocalPosition"] = new JObject
                {
                    ["x"] = content.TotalLocalPosition.X.ToSingle(),
                    ["y"] = content.TotalLocalPosition.Y.ToSingle(),
                    ["z"] = content.TotalLocalPosition.Z.ToSingle()
                },
                ["totalForwardDistance"] = content.TotalForwardDistance,
                ["totalYaw"] = content.TotalYaw,
                ["authoredSource"] = content.AuthoredSource
            };
    }
}
#endif
