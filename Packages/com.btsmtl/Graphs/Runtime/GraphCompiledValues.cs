using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs
{
    public static class GraphCompiledValues
    {
        public static OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>
            ToState<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>(
                in GraphHostValue value, int enumTypeContentId, in OrchestrationIdentityValue identity)
            where TScalar : unmanaged
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TNumeric : struct, IGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion> =>
            GraphNumericValues<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>
                .ToState(in value, enumTypeContentId, in identity);

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

    }

    internal static class GraphNumericValues<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TNumeric : struct, IGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion>
    {
        internal static Type ValueType(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value, OrchestrationIdentityTable identities) => value.Kind switch
        {
            OrchestrationValueKind.Boolean => typeof(bool),
            OrchestrationValueKind.Int32 => typeof(int),
            OrchestrationValueKind.Scalar => typeof(float),
            OrchestrationValueKind.Vector2 => typeof(Vector2),
            OrchestrationValueKind.Vector3 => typeof(Vector3),
            OrchestrationValueKind.Quaternion => typeof(Quaternion),
            OrchestrationValueKind.Identity => typeof(string),
            OrchestrationValueKind.Enum => Type.GetType(identities.Resolve(value.Enum.TypeContentId), true),
            _ => throw new InvalidOperationException($"事件图状态类型 '{value.Kind}' 不受支持。")
        };

        internal static OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> ToState(in GraphHostValue value, int enumTypeContentId,
            in OrchestrationIdentityValue identity) => value.Kind switch
        {
            GraphHostValueKind.Bool => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromBoolean(value.BoolValue),
            GraphHostValueKind.Int32 => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromInt32(value.Int32Value),
            GraphHostValueKind.Float32 => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromScalar(default(TNumeric).FromSingle(value.Float32Value)),
            GraphHostValueKind.Vector2 => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromVector2(default(TNumeric).FromUnityVector2(value.Vector2Value)),
            GraphHostValueKind.Vector3 => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromVector3(default(TNumeric).FromUnityVector3(value.Vector3Value)),
            GraphHostValueKind.Quaternion => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromQuaternion(default(TNumeric).FromUnityQuaternion(value.QuaternionValue)),
            GraphHostValueKind.Enum => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromEnum(new OrchestrationEnumValue(enumTypeContentId, value.EnumValue)),
            GraphHostValueKind.Text => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromIdentity(identity),
            _ => throw new InvalidOperationException($"事件图输入类型 '{value.Kind}' 不受支持。")
        };

        internal static GraphHostValue FromState(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value, Type enumType,
            string identity) => value.Kind switch
        {
            OrchestrationValueKind.Boolean => GraphHostValue.FromBool(value.Boolean),
            OrchestrationValueKind.Int32 => GraphHostValue.FromInt32(value.Int32),
            OrchestrationValueKind.Scalar => GraphHostValue.FromFloat32(default(TNumeric).ToSingle(value.Scalar)),
            OrchestrationValueKind.Vector2 => GraphHostValue.FromVector2(default(TNumeric).ToUnityVector2(value.Vector2)),
            OrchestrationValueKind.Vector3 => GraphHostValue.FromVector3(default(TNumeric).ToUnityVector3(value.Vector3)),
            OrchestrationValueKind.Quaternion => GraphHostValue.FromQuaternion(default(TNumeric).ToUnityQuaternion(value.Quaternion)),
            OrchestrationValueKind.Enum => GraphHostValue.FromEnum(enumType, value.Enum.Value),
            OrchestrationValueKind.Identity => GraphHostValue.FromText(identity),
            _ => throw new InvalidOperationException($"事件图输出类型 '{value.Kind}' 不受支持。")
        };
    }
}
