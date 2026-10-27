using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 作者值类型身份（bool、float、vector3 等）与编排值种类到编译值类型的唯一对应。
    // 全名是声明目录记录的平台类型名，零值为通用语义值。
    public static class CompileValueTypes
    {
        public static readonly CompileValueType Boolean = new(SemanticValueKind.Boolean, "System.Boolean", false);
        public static readonly CompileValueType Int32 = new(SemanticValueKind.Int32, "System.Int32", 0);
        public static readonly CompileValueType UInt32 = new(SemanticValueKind.UInt64, "System.UInt32", 0u);
        public static readonly CompileValueType UInt64 = new(SemanticValueKind.UInt64, "System.UInt64", 0UL);
        public static readonly CompileValueType Single = new(SemanticValueKind.Number, "System.Single", 0f);
        public static readonly CompileValueType Double = new(SemanticValueKind.Number, "System.Double", 0d);
        public static readonly CompileValueType String = new(SemanticValueKind.Identity, "System.String", string.Empty);
        public static readonly CompileValueType Vector2 = new(SemanticValueKind.Vector2, "UnityEngine.Vector2", new SemanticVector2Value(0d, 0d));
        public static readonly CompileValueType Vector3 = new(SemanticValueKind.Vector3, "UnityEngine.Vector3", new SemanticVector3Value(0d, 0d, 0d));
        public static readonly CompileValueType Quaternion = new(SemanticValueKind.Quaternion, "UnityEngine.Quaternion",
            new SemanticQuaternionValue(0d, 0d, 0d, 1d));

        public static bool TryFromId(string valueTypeId, out CompileValueType type)
        {
            type = valueTypeId switch
            {
                "bool" => Boolean,
                "int" => Int32,
                "uint" => UInt32,
                "ulong" => UInt64,
                "float" => Single,
                "double" => Double,
                "string" => String,
                "vector2" => Vector2,
                "vector3" => Vector3,
                "quaternion" => Quaternion,
                _ => null
            };
            return type != null;
        }

        public static CompileValueType FromId(string valueTypeId) =>
            TryFromId(valueTypeId, out CompileValueType type) ? type
                : throw new InvalidOperationException($"作者值类型 '{valueTypeId}' 没有编译合同。");

        // 枚举身份为 "Enum:" 加枚举类型的程序集限定名。
        public static CompileValueType FromKind(OrchestrationValueKind kind, string valueTypeIdentity) => kind switch
        {
            OrchestrationValueKind.Boolean => Boolean,
            OrchestrationValueKind.Int32 => Int32,
            OrchestrationValueKind.UInt64 => UInt64,
            OrchestrationValueKind.Scalar or OrchestrationValueKind.Yaw => Single,
            OrchestrationValueKind.Vector2 => Vector2,
            OrchestrationValueKind.Vector3 => Vector3,
            OrchestrationValueKind.Identity => String,
            OrchestrationValueKind.Quaternion => Quaternion,
            OrchestrationValueKind.Enum => Enum(valueTypeIdentity.Substring("Enum:".Length)),
            _ => throw new InvalidOperationException($"编排值种类 '{kind}' 没有作者值类型。")
        };

        public static CompileValueType Enum(string assemblyQualifiedName)
        {
            int separator = assemblyQualifiedName.IndexOf(',');
            string fullName = separator < 0 ? assemblyQualifiedName : assemblyQualifiedName.Substring(0, separator);
            return new CompileValueType(SemanticValueKind.Enum, fullName, 0, assemblyQualifiedName);
        }
    }
}
