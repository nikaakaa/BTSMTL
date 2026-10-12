using System;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.EventGraphs
{
    public static class EventGraphCompiledValues
    {
        internal static Type ValueType(AbilityStateValue value) => value.Kind switch
        {
            ProgramStateValueKind.Boolean => typeof(bool),
            ProgramStateValueKind.Int32 => typeof(int),
            ProgramStateValueKind.Scalar => typeof(float),
            ProgramStateValueKind.Vector2 => typeof(Vector2),
            ProgramStateValueKind.Vector3 => typeof(Vector3),
            ProgramStateValueKind.Quaternion => typeof(Quaternion),
            ProgramStateValueKind.Identity => typeof(CanonicalTextValue),
            ProgramStateValueKind.Enum => Type.GetType(value.Enum.TypeIdentity.ToString(), true),
            _ => throw new InvalidOperationException($"事件图状态类型 '{value.Kind}' 不受支持。")
        };

        public static AbilityStateValue ToState(EventGraphValue value, CanonicalTextValue enumIdentity) => value.Kind switch
        {
            EventGraphValueKind.Bool => AbilityStateValue.FromBoolean(value.BoolValue),
            EventGraphValueKind.Int32 => AbilityStateValue.FromInt32(value.Int32Value),
            EventGraphValueKind.Float32 => AbilityStateValue.FromScalar(Float32Scalar.FromSingle(value.Float32Value)),
            EventGraphValueKind.Vector2 => AbilityStateValue.FromVector2(new Float32Vector2(Float32Scalar.FromSingle(value.Vector2Value.x), Float32Scalar.FromSingle(value.Vector2Value.y))),
            EventGraphValueKind.Vector3 => AbilityStateValue.FromVector3(new Float32Vector3(Float32Scalar.FromSingle(value.Vector3Value.x), Float32Scalar.FromSingle(value.Vector3Value.y), Float32Scalar.FromSingle(value.Vector3Value.z))),
            EventGraphValueKind.Quaternion => AbilityStateValue.FromQuaternion(new Float32Quaternion(
                Float32Scalar.FromSingle(value.QuaternionValue.x), Float32Scalar.FromSingle(value.QuaternionValue.y), Float32Scalar.FromSingle(value.QuaternionValue.z), Float32Scalar.FromSingle(value.QuaternionValue.w))),
            EventGraphValueKind.Enum => AbilityStateValue.FromEnum(enumIdentity, value.EnumValue),
            EventGraphValueKind.Text => AbilityStateValue.FromIdentity(value.TextValue),
            _ => throw new InvalidOperationException($"事件图输入类型 '{value.Kind}' 不受支持。")
        };

        internal static EventGraphValue FromState(AbilityStateValue value, Type enumType) => value.Kind switch
        {
            ProgramStateValueKind.Boolean => EventGraphValue.FromBool(value.Boolean),
            ProgramStateValueKind.Int32 => EventGraphValue.FromInt32(value.Int32),
            ProgramStateValueKind.Scalar => EventGraphValue.FromFloat32(value.Scalar.ToSingle()),
            ProgramStateValueKind.Vector2 => EventGraphValue.FromVector2(new Vector2(value.Vector2.X.ToSingle(), value.Vector2.Y.ToSingle())),
            ProgramStateValueKind.Vector3 => EventGraphValue.FromVector3(new Vector3(value.Vector3.X.ToSingle(), value.Vector3.Y.ToSingle(), value.Vector3.Z.ToSingle())),
            ProgramStateValueKind.Quaternion => EventGraphValue.FromQuaternion(new Quaternion(
                value.Quaternion.X.ToSingle(), value.Quaternion.Y.ToSingle(), value.Quaternion.Z.ToSingle(), value.Quaternion.W.ToSingle())),
            ProgramStateValueKind.Enum => EventGraphValue.FromEnum(enumType, value.Enum.Value),
            ProgramStateValueKind.Identity => EventGraphValue.FromText(value.Identity),
            _ => throw new InvalidOperationException($"事件图输出类型 '{value.Kind}' 不受支持。")
        };
    }
}
