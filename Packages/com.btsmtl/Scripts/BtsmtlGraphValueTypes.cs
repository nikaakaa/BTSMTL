using System;
using System.Globalization;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL
{
    public static class BtsmtlGraphValueTypes
    {
        public static SemanticValueKind Kind(Type type)
        {
            if (type == typeof(bool)) return SemanticValueKind.Boolean;
            if (type == typeof(int)) return SemanticValueKind.Int32;
            if (type == typeof(uint) || type == typeof(ulong)) return SemanticValueKind.UInt64;
            if (type == typeof(float) || type == typeof(double)) return SemanticValueKind.Number;
            if (type == typeof(Vector2)) return SemanticValueKind.Vector2;
            if (type == typeof(Vector3)) return SemanticValueKind.Vector3;
            if (type == typeof(Quaternion)) return SemanticValueKind.Quaternion;
            if (type == typeof(string) || type == typeof(CanonicalTextValue)) return SemanticValueKind.Identity;
            if (type.IsEnum) return SemanticValueKind.Enum;
            throw new InvalidOperationException($"图值类型 '{type.FullName}' 没有编译合同。");
        }

        public static ProgramStateValueKind StateKind(Type type) => Kind(type) switch
        {
            SemanticValueKind.Boolean => ProgramStateValueKind.Boolean,
            SemanticValueKind.Int32 => ProgramStateValueKind.Int32,
            SemanticValueKind.UInt64 => ProgramStateValueKind.UInt64,
            SemanticValueKind.Number => ProgramStateValueKind.Scalar,
            SemanticValueKind.Vector2 => ProgramStateValueKind.Vector2,
            SemanticValueKind.Vector3 => ProgramStateValueKind.Vector3,
            SemanticValueKind.Quaternion => ProgramStateValueKind.Quaternion,
            SemanticValueKind.Identity => ProgramStateValueKind.Identity,
            SemanticValueKind.Enum => ProgramStateValueKind.Enum,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        public static object AuthoringValue(Type type, object value) => type.IsEnum
            ? new SemanticEnumValue(type.AssemblyQualifiedName, Convert.ToInt32(value, CultureInfo.InvariantCulture))
            : value;
    }
}
