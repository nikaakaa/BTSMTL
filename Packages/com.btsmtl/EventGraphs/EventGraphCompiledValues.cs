using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.EventGraphs
{
    public static class EventGraphCompiledValues
    {
        public static string TypeIdentity(Type type)
        {
            EventGraphValueKinds.TryGet(type, out EventGraphValueKind kind);
            return kind switch
            {
                EventGraphValueKind.Bool => nameof(OrchestrationValueKind.Boolean),
                EventGraphValueKind.Int32 => nameof(OrchestrationValueKind.Int32),
                EventGraphValueKind.Float32 => nameof(OrchestrationValueKind.Scalar),
                EventGraphValueKind.Vector2 => nameof(OrchestrationValueKind.Vector2),
                EventGraphValueKind.Vector3 => nameof(OrchestrationValueKind.Vector3),
                EventGraphValueKind.Quaternion => nameof(OrchestrationValueKind.Quaternion),
                EventGraphValueKind.Text => nameof(OrchestrationValueKind.Identity),
                EventGraphValueKind.Enum => "Enum:" + type.AssemblyQualifiedName,
                _ => throw new InvalidOperationException($"事件图类型 '{type}' 不受支持。")
            };
        }

    }

    internal static class EventGraphNumericValues<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TNumeric : struct, IEventGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion>
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

        internal static OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> ToState(in EventGraphValue value, int enumTypeContentId,
            in OrchestrationIdentityValue identity) => value.Kind switch
        {
            EventGraphValueKind.Bool => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromBoolean(value.BoolValue),
            EventGraphValueKind.Int32 => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromInt32(value.Int32Value),
            EventGraphValueKind.Float32 => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromScalar(default(TNumeric).FromSingle(value.Float32Value)),
            EventGraphValueKind.Vector2 => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromVector2(default(TNumeric).FromUnityVector2(value.Vector2Value)),
            EventGraphValueKind.Vector3 => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromVector3(default(TNumeric).FromUnityVector3(value.Vector3Value)),
            EventGraphValueKind.Quaternion => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromQuaternion(default(TNumeric).FromUnityQuaternion(value.QuaternionValue)),
            EventGraphValueKind.Enum => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromEnum(new OrchestrationEnumValue(enumTypeContentId, value.EnumValue)),
            EventGraphValueKind.Text => OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromIdentity(identity),
            _ => throw new InvalidOperationException($"事件图输入类型 '{value.Kind}' 不受支持。")
        };

        internal static EventGraphValue FromState(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value, Type enumType,
            string identity) => value.Kind switch
        {
            OrchestrationValueKind.Boolean => EventGraphValue.FromBool(value.Boolean),
            OrchestrationValueKind.Int32 => EventGraphValue.FromInt32(value.Int32),
            OrchestrationValueKind.Scalar => EventGraphValue.FromFloat32(default(TNumeric).ToSingle(value.Scalar)),
            OrchestrationValueKind.Vector2 => EventGraphValue.FromVector2(default(TNumeric).ToUnityVector2(value.Vector2)),
            OrchestrationValueKind.Vector3 => EventGraphValue.FromVector3(default(TNumeric).ToUnityVector3(value.Vector3)),
            OrchestrationValueKind.Quaternion => EventGraphValue.FromQuaternion(default(TNumeric).ToUnityQuaternion(value.Quaternion)),
            OrchestrationValueKind.Enum => EventGraphValue.FromEnum(enumType, value.Enum.Value),
            OrchestrationValueKind.Identity => EventGraphValue.FromText(identity),
            _ => throw new InvalidOperationException($"事件图输出类型 '{value.Kind}' 不受支持。")
        };
    }
}
