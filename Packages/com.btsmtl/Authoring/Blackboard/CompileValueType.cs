using System;
using System.Globalization;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public sealed class CompileValueType
    {
        public CompileValueType(SemanticValueKind kind, string fullName, object defaultValue, string enumIdentity = null,
            OrchestrationObjectKind objectKind = null)
        {
            if (kind == SemanticValueKind.Enum && string.IsNullOrEmpty(enumIdentity))
                throw new ArgumentException("枚举值类型需要枚举身份。", nameof(enumIdentity));
            if (kind != SemanticValueKind.ObjectHandle && objectKind != null)
                throw new ArgumentException("只有句柄值类型能声明对象种类。", nameof(objectKind));
            Kind = kind;
            FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
            DefaultValue = defaultValue;
            EnumIdentity = enumIdentity;
            ObjectKind = kind == SemanticValueKind.ObjectHandle ? objectKind ?? OrchestrationObjectKind.Any : null;
        }

        public SemanticValueKind Kind { get; }
        public string FullName { get; }
        public object DefaultValue { get; }
        public string EnumIdentity { get; }
        // 句柄的对象种类，只参与作者期与编译期检查；非句柄为 null。
        public OrchestrationObjectKind ObjectKind { get; }
        public bool IsEnum => Kind == SemanticValueKind.Enum;

        public CompileValueType WithObjectKind(OrchestrationObjectKind objectKind) =>
            Kind != SemanticValueKind.ObjectHandle || objectKind == null || ReferenceEquals(objectKind, ObjectKind) ? this
                : new CompileValueType(Kind, FullName, DefaultValue, EnumIdentity, objectKind);

        public object ToSemanticLiteral(object value) => IsEnum
            ? new SemanticEnumValue(EnumIdentity, Convert.ToInt32(value, CultureInfo.InvariantCulture))
            : value;

        // 值表示相同；句柄不区分种类。
        public bool SameType(CompileValueType other) => other != null &&
            Kind == other.Kind && string.Equals(FullName, other.FullName, StringComparison.Ordinal);

        // 连线兼容：值表示相同，句柄还要求本种类等于目标种类或是其后代。
        public bool IsAssignableTo(CompileValueType target) => SameType(target) &&
            (Kind != SemanticValueKind.ObjectHandle || ObjectKind.IsAssignableTo(target.ObjectKind));

        public string DisplayName => Kind == SemanticValueKind.ObjectHandle ? $"{FullName}（{ObjectKind.DisplayName}）" : FullName;
    }
}
