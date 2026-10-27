using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        static readonly IReadOnlyDictionary<Type, PropertyInfo[]> s_Properties = new Dictionary<Type, PropertyInfo[]>
        {
            [typeof(AnimationCurve)] = Properties(typeof(AnimationCurve)),
            [typeof(Keyframe)] = Properties(typeof(Keyframe))
        };

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
            if (value != null && s_Properties.TryGetValue(type, out PropertyInfo[] properties))
                return new AuthoringObjectValue(TypeIdentity(type), properties.Select(property =>
                    Member(property.Name, property.PropertyType, property.GetValue(value))).ToArray());
            return base.Capture(type, value);
        }

        public override AuthoringValueDefinition Describe(Type type)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return new AuthoringValueDefinition(TypeIdentity(type), AuthoringValueDefinitionKind.Reference,
                    TypeCode.Empty, true, Array.Empty<string>(), false, Array.Empty<AuthoringValueMemberDefinition>(), null);
            if (s_Properties.TryGetValue(type, out PropertyInfo[] properties))
                return new AuthoringValueDefinition(TypeIdentity(type), AuthoringValueDefinitionKind.Object,
                    TypeCode.Empty, !type.IsValueType, Array.Empty<string>(), false, properties.Select(property =>
                        new AuthoringValueMemberDefinition(property.Name, Describe(property.PropertyType))).ToArray(), null);
            return base.Describe(type);
        }

        protected override object RestoreContent(Type type, AuthoringValue value)
        {
            if (value is AuthoringReferenceValue reference)
                return m_Resolve(type, reference.Target);
            if (s_Properties.TryGetValue(type, out PropertyInfo[] properties) && value is AuthoringObjectValue composite)
            {
                var fields = composite.Members.ToArray().ToDictionary(member => member.FieldId.Value, member => member.Value);
                object instance = Activator.CreateInstance(type);
                foreach (PropertyInfo property in properties)
                    property.SetValue(instance, Restore(property.PropertyType, fields[property.Name]));
                return instance;
            }
            return base.RestoreContent(type, value);
        }

        static PropertyInfo[] Properties(Type type) => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod?.IsPublic == true && property.SetMethod?.IsPublic == true &&
                !property.IsDefined(typeof(ObsoleteAttribute), true)).OrderBy(property => property.MetadataToken).ToArray();
    }
}
