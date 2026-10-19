using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs
{
    public static class GraphCompiledValues
    {
        public static TValue
            ToState<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TNumeric>(
                in GraphHostValue value, int enumTypeContentId, in OrchestrationIdentityValue identity)
            where TValue : unmanaged
            where TScalar : unmanaged
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TNumeric : struct, IGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion> =>
            GraphNumericValues<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TNumeric>
                .ToState(in value, enumTypeContentId, in identity);

        public static GraphHostValue
            FromState<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TNumeric>(
                in TValue value, Type enumType, string identity)
            where TValue : unmanaged
            where TScalar : unmanaged
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TNumeric : struct, IGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion> =>
            GraphNumericValues<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TNumeric>
                .FromState(in value, enumType, identity);

        public static string TypeIdentity(Type type)
        {
            GraphHostValueKinds.TryGet(type, out GraphHostValueKind kind);
            return kind switch
            {
                GraphHostValueKind.Bool => nameof(OrchestrationValueKind.Boolean),
                GraphHostValueKind.Int32 => nameof(OrchestrationValueKind.Int32),
                GraphHostValueKind.Float32 => nameof(OrchestrationValueKind.Scalar),
                GraphHostValueKind.Vector2 => nameof(OrchestrationValueKind.Vector2),
                GraphHostValueKind.Vector3 => nameof(OrchestrationValueKind.Vector3),
                GraphHostValueKind.Quaternion => nameof(OrchestrationValueKind.Quaternion),
                GraphHostValueKind.Text => nameof(OrchestrationValueKind.Identity),
                GraphHostValueKind.Enum => "Enum:" + type.AssemblyQualifiedName,
                _ => throw new InvalidOperationException($"事件图类型 '{type}' 不受支持。")
            };
        }

        public static Type ResolveTypeIdentity(string identity)
        {
            const string enumPrefix = "Enum:";
            if (identity is string enumIdentity && enumIdentity.StartsWith(enumPrefix, StringComparison.Ordinal))
            {
                Type enumType = Type.GetType(enumIdentity.Substring(enumPrefix.Length), true);
                return enumType.IsEnum ? enumType :
                    throw new InvalidOperationException($"事件图枚举类型身份 '{identity}' 没有指向枚举类型。");
            }
            return identity switch
            {
                nameof(OrchestrationValueKind.Boolean) => typeof(bool),
                nameof(OrchestrationValueKind.Int32) => typeof(int),
                nameof(OrchestrationValueKind.Scalar) => typeof(float),
                nameof(OrchestrationValueKind.Vector2) => typeof(Vector2),
                nameof(OrchestrationValueKind.Vector3) => typeof(Vector3),
                nameof(OrchestrationValueKind.Quaternion) => typeof(Quaternion),
                nameof(OrchestrationValueKind.Identity) => typeof(string),
                _ => throw new InvalidOperationException($"事件图类型身份 '{identity}' 不受支持。")
            };
        }

    }

    internal static class GraphNumericValues<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TNumeric>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TNumeric : struct, IGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion>
    {
        internal static Type ValueType(in TValue value, OrchestrationIdentityTable identities) => default(TValues).Kind(in value) switch
        {
            OrchestrationValueKind.Boolean => typeof(bool),
            OrchestrationValueKind.Int32 => typeof(int),
            OrchestrationValueKind.Scalar => typeof(float),
            OrchestrationValueKind.Vector2 => typeof(Vector2),
            OrchestrationValueKind.Vector3 => typeof(Vector3),
            OrchestrationValueKind.Quaternion => typeof(Quaternion),
            OrchestrationValueKind.Identity => typeof(string),
            OrchestrationValueKind.Enum => Type.GetType(identities.Resolve(default(TValues).Enum(in value).TypeContentId), true),
            _ => throw new InvalidOperationException($"事件图状态类型 '{default(TValues).Kind(in value)}' 不受支持。")
        };

        internal static TValue ToState(in GraphHostValue value, int enumTypeContentId,
            in OrchestrationIdentityValue identity) => value.Kind switch
        {
            GraphHostValueKind.Bool => default(TValues).FromBoolean(value.BoolValue),
            GraphHostValueKind.Int32 => default(TValues).FromInt32(value.Int32Value),
            GraphHostValueKind.Float32 => default(TValues).FromScalar(default(TNumeric).FromSingle(value.Float32Value)),
            GraphHostValueKind.Vector2 => default(TValues).FromVector2(default(TNumeric).FromUnityVector2(value.Vector2Value)),
            GraphHostValueKind.Vector3 => default(TValues).FromVector3(default(TNumeric).FromUnityVector3(value.Vector3Value)),
            GraphHostValueKind.Quaternion => default(TValues).FromQuaternion(default(TNumeric).FromUnityQuaternion(value.QuaternionValue)),
            GraphHostValueKind.Enum => default(TValues).FromEnum(new OrchestrationEnumValue(enumTypeContentId, value.EnumValue)),
            GraphHostValueKind.Text => default(TValues).FromIdentity(identity),
            _ => throw new InvalidOperationException($"事件图输入类型 '{value.Kind}' 不受支持。")
        };

        internal static GraphHostValue FromState(in TValue value, Type enumType,
            string identity) => default(TValues).Kind(in value) switch
        {
            OrchestrationValueKind.Boolean => GraphHostValue.FromBool(default(TValues).Boolean(in value)),
            OrchestrationValueKind.Int32 => GraphHostValue.FromInt32(default(TValues).Int32(in value)),
            OrchestrationValueKind.Scalar => GraphHostValue.FromFloat32(default(TNumeric).ToSingle(default(TValues).Scalar(in value))),
            OrchestrationValueKind.Vector2 => GraphHostValue.FromVector2(default(TNumeric).ToUnityVector2(default(TValues).Vector2(in value))),
            OrchestrationValueKind.Vector3 => GraphHostValue.FromVector3(default(TNumeric).ToUnityVector3(default(TValues).Vector3(in value))),
            OrchestrationValueKind.Quaternion => GraphHostValue.FromQuaternion(default(TNumeric).ToUnityQuaternion(default(TValues).Quaternion(in value))),
            OrchestrationValueKind.Enum => GraphHostValue.FromEnum(enumType, default(TValues).Enum(in value).Value),
            OrchestrationValueKind.Identity => GraphHostValue.FromText(identity),
            _ => throw new InvalidOperationException($"事件图输出类型 '{default(TValues).Kind(in value)}' 不受支持。")
        };
    }
}
