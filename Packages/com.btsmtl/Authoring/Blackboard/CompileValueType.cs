using System;
using System.Globalization;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public sealed class CompileValueType
    {
        public CompileValueType(SemanticValueKind kind, string fullName, object defaultValue, string enumIdentity = null)
        {
            if (kind == SemanticValueKind.Enum && string.IsNullOrEmpty(enumIdentity))
                throw new ArgumentException("枚举值类型需要枚举身份。", nameof(enumIdentity));
            Kind = kind;
            FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
            DefaultValue = defaultValue;
            EnumIdentity = enumIdentity;
        }

        public SemanticValueKind Kind { get; }
        public string FullName { get; }
        public object DefaultValue { get; }
        public string EnumIdentity { get; }
        public bool IsEnum => Kind == SemanticValueKind.Enum;

        public object ToSemanticLiteral(object value) => IsEnum
            ? new SemanticEnumValue(EnumIdentity, Convert.ToInt32(value, CultureInfo.InvariantCulture))
            : value;

        public bool SameType(CompileValueType other) => other != null &&
            Kind == other.Kind && string.Equals(FullName, other.FullName, StringComparison.Ordinal);
    }
}
