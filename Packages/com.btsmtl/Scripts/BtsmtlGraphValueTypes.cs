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
            if (type == typeof(string)) return SemanticValueKind.Identity;
            if (type.IsEnum) return SemanticValueKind.Enum;
            throw new InvalidOperationException($"图值类型 '{type.FullName}' 没有编译合同。");
        }

        public static object ToSemanticLiteral(Type type, object value) => type.IsEnum
            ? new SemanticEnumValue(type.AssemblyQualifiedName, Convert.ToInt32(value, CultureInfo.InvariantCulture))
            : value;
    }
}
