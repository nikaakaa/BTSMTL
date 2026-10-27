using System;
using System.Linq;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using UnityEngine;

namespace BTSMTL.Graphs.Editor
{
    // Unity 作者值与共同文档值的转换：Unity 对象写为引用身份，AnimationCurve 写为键序列与前后循环方式。
    public sealed class BtsmtlUnityDocumentValues : AuthoringValueCodec
    {
        readonly Func<UnityEngine.Object, AuthoringContentReference> m_Identify;
        readonly Func<Type, AuthoringContentReference, UnityEngine.Object> m_Resolve;

        public BtsmtlUnityDocumentValues(Func<UnityEngine.Object, AuthoringContentReference> identify,
            Func<Type, AuthoringContentReference, UnityEngine.Object> resolve)
        {
            m_Identify = identify;
            m_Resolve = resolve;
        }

        public override AuthoringValue Capture(Type type, object value)
        {
            if (value is UnityEngine.Object asset)
                return new AuthoringReferenceValue(TypeIdentity(type), m_Identify(asset));
            if (value is AnimationCurve curve)
            {
                AuthoringValue[] keys = curve.keys.Select(key => (AuthoringValue)new AuthoringObjectValue(
                    TypeIdentity(typeof(Keyframe)), new[]
                    {
                        Member("time", typeof(float), key.time), Member("value", typeof(float), key.value),
                        Member("inTangent", typeof(float), key.inTangent), Member("outTangent", typeof(float), key.outTangent),
                        Member("inWeight", typeof(float), key.inWeight), Member("outWeight", typeof(float), key.outWeight),
                        Member("weightedMode", typeof(WeightedMode), key.weightedMode)
                    })).ToArray();
                return new AuthoringObjectValue(TypeIdentity(type), new[]
                {
                    new AuthoringNamedValue(new GraphAuthoringFieldId("keys"),
                        new AuthoringSequenceValue(TypeIdentity(typeof(Keyframe[])), keys)),
                    Member("preWrapMode", typeof(WrapMode), curve.preWrapMode),
                    Member("postWrapMode", typeof(WrapMode), curve.postWrapMode)
                });
            }
            return base.Capture(type, value);
        }

        protected override object RestoreContent(Type type, AuthoringValue value)
        {
            if (value is AuthoringReferenceValue reference)
                return m_Resolve(type, reference.Target);
            if (type == typeof(AnimationCurve) && value is AuthoringObjectValue composite)
            {
                var fields = composite.Members.ToArray().ToDictionary(member => member.FieldId.Value, member => member.Value);
                var keys = (AuthoringSequenceValue)fields["keys"];
                Keyframe[] keyframes = keys.Items.ToArray().Cast<AuthoringObjectValue>().Select(key =>
                {
                    var parts = key.Members.ToArray().ToDictionary(member => member.FieldId.Value, member => member.Value);
                    return new Keyframe(
                        (float)Restore(typeof(float), parts["time"]), (float)Restore(typeof(float), parts["value"]),
                        (float)Restore(typeof(float), parts["inTangent"]), (float)Restore(typeof(float), parts["outTangent"]),
                        (float)Restore(typeof(float), parts["inWeight"]), (float)Restore(typeof(float), parts["outWeight"]))
                    { weightedMode = (WeightedMode)Restore(typeof(WeightedMode), parts["weightedMode"]) };
                }).ToArray();
                return new AnimationCurve(keyframes)
                {
                    preWrapMode = (WrapMode)Restore(typeof(WrapMode), fields["preWrapMode"]),
                    postWrapMode = (WrapMode)Restore(typeof(WrapMode), fields["postWrapMode"])
                };
            }
            return base.RestoreContent(type, value);
        }
    }
}
