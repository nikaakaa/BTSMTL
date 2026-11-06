#if UNITY_EDITOR || BTSMTL_AUTHORING
using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Timeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using ThirdPersonSimulation;
using AuthoringVector3 = System.Numerics.Vector3;

namespace ThirdPersonGameplay.Movement.Timeline
{
    public sealed partial class RootMotionCurveAuthoring
    {
        [GraphAuthoringField("authoringId", GraphAuthoringFieldValueKind.String, DisplayName = "作者身份", ReadOnly = true)]
        public string m_AuthoringId = string.Empty;
        [GraphAuthoringField("sourceIdentity", GraphAuthoringFieldValueKind.String, DisplayName = "来源身份", ReadOnly = true)]
        public string m_SourceIdentity = string.Empty;
        [GraphAuthoringField("contentDomain", GraphAuthoringFieldValueKind.String, DisplayName = "内容领域", ReadOnly = true)]
        public string m_ContentDomain = "motion-curve";
        [GraphAuthoringField("authoringScope", GraphAuthoringFieldValueKind.String, DisplayName = "作者范围", ReadOnly = true)]
        public string m_AuthoringScope = string.Empty;
        [GraphAuthoringField("name", GraphAuthoringFieldValueKind.String, DisplayName = "名称", DocumentMember = GraphAuthoringDocumentMember.DisplayName)]
        public string m_Name = string.Empty;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.String, DisplayName = "源动画身份")]
        public string sourceAnimationIdentity = string.Empty;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.String, DisplayName = "源动画修订", ReadOnly = true)]
        public string sourceAnimationRevision = string.Empty;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Boolean, DisplayName = "源动画循环")]
        public bool sourceLooping;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Float, DisplayName = "源时长", Unit = "s")]
        public float duration;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Float, DisplayName = "采样率", Unit = "Hz")]
        public float sampleRate;
        [GraphAuthoringField(typeof(RootMotionCurveEvaluationMode), DisplayName = "求值模式")]
        public RootMotionCurveEvaluationMode evaluationMode;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Object, DisplayName = "局部位移 X")]
        public TimelineCurve localPositionX = NewZeroCurve();
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Object, DisplayName = "局部位移 Y")]
        public TimelineCurve localPositionY = NewZeroCurve();
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Object, DisplayName = "局部位移 Z")]
        public TimelineCurve localPositionZ = NewZeroCurve();
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Object, DisplayName = "前向距离")]
        public TimelineCurve forwardDistance = NewZeroCurve();
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Object, DisplayName = "局部转向")]
        public TimelineCurve localYaw = NewZeroCurve();
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Vector3, DisplayName = "总局部位移")]
        public AuthoringVector3 totalLocalPosition;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Float, DisplayName = "总前向距离")]
        public float totalForwardDistance;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.Float, DisplayName = "总转向", Unit = "deg")]
        public float totalYaw;
        [GraphAuthoringField(GraphAuthoringFieldValueKind.String, DisplayName = "作者来源")]
        public string authoredSource = string.Empty;

        public RootMotionCurveContent Compile(string contentRevision) => new RootMotionCurveContent(
            m_AuthoringId, contentRevision, m_Name, sourceAnimationIdentity, sourceAnimationRevision,
            sourceLooping, duration, sampleRate, evaluationMode, localPositionX, localPositionY, localPositionZ,
            forwardDistance, localYaw, new Float32Vector3(Float32Scalar.FromSingle(totalLocalPosition.X),
                Float32Scalar.FromSingle(totalLocalPosition.Y), Float32Scalar.FromSingle(totalLocalPosition.Z)),
            totalForwardDistance, totalYaw, authoredSource);

        public static RootMotionCurveAuthoring FromContent(RootMotionCurveContent content,
            string sourceIdentity, string authoringScope) => new RootMotionCurveAuthoring
            {
                m_AuthoringId = content.AuthoringId,
                m_SourceIdentity = sourceIdentity,
                m_AuthoringScope = authoringScope,
                m_Name = content.DisplayName,
                sourceAnimationIdentity = content.SourceAnimationIdentity,
                sourceAnimationRevision = content.SourceAnimationRevision,
                sourceLooping = content.SourceLooping,
                duration = content.Duration,
                sampleRate = content.SampleRate,
                evaluationMode = content.EvaluationMode,
                localPositionX = content.LocalPositionX,
                localPositionY = content.LocalPositionY,
                localPositionZ = content.LocalPositionZ,
                forwardDistance = content.ForwardDistance,
                localYaw = content.LocalYaw,
                totalLocalPosition = new AuthoringVector3(content.TotalLocalPosition.X.ToSingle(),
                    content.TotalLocalPosition.Y.ToSingle(), content.TotalLocalPosition.Z.ToSingle()),
                totalForwardDistance = content.TotalForwardDistance,
                totalYaw = content.TotalYaw,
                authoredSource = content.AuthoredSource
            };

        static TimelineCurve NewZeroCurve() => new TimelineCurve(8, 8,
            new[] { new TimelineCurveKey(0f, 0f, 0f, 0f, 0f, 0f, 0, 0) });
    }

    public static class RootMotionCurveContentJson
    {
        sealed class AuthoringContractResolver : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization) =>
                TimelineJson.StoredFields(type).Select(member =>
                {
                    JsonProperty property = base.CreateProperty(member.Value, MemberSerialization.Fields);
                    property.PropertyName = member.Key;
                    property.Required = Required.AllowNull;
                    property.Readable = true;
                    property.Writable = true;
                    return property;
                }).ToArray();
        }

        sealed class CurveConverter : JsonConverter<TimelineCurve>
        {
            public override TimelineCurve ReadJson(JsonReader reader, Type objectType, TimelineCurve existingValue,
                bool hasExistingValue, JsonSerializer serializer) => TimelineCurveJson.Read(JToken.Load(reader));

            public override void WriteJson(JsonWriter writer, TimelineCurve value, JsonSerializer serializer) =>
                TimelineCurveJson.Write(value).WriteTo(writer);
        }

        public static Type AuthoringType => typeof(RootMotionCurveAuthoring);

        public static RootMotionCurveAuthoring ReadAuthoring(JObject source) =>
            source.ToObject<RootMotionCurveAuthoring>(Serializer());

        public static RootMotionCurveContent Read(JObject source, string contentRevision) =>
            ReadAuthoring(source).Compile(contentRevision);

        public static JObject Write(RootMotionCurveAuthoring source) => JObject.FromObject(source, Serializer());

        public static JObject Write(RootMotionCurveContent content, string sourceIdentity, string authoringScope) =>
            Write(RootMotionCurveAuthoring.FromContent(content, sourceIdentity, authoringScope));

        static JsonSerializer Serializer() => JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new AuthoringContractResolver(),
                Converters = { new CurveConverter() }
            });
    }
}
#endif
