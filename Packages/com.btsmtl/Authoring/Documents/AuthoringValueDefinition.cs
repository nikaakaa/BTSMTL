using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public enum AuthoringValueDefinitionKind { Scalar, Enumeration, Reference, Object, Sequence }

    public sealed class AuthoringValueDefinition
    {
        public AuthoringValueDefinition(string typeId, AuthoringValueDefinitionKind kind, TypeCode scalarType,
            bool allowsNull, IReadOnlyList<string> enumValues, bool flags,
            IReadOnlyList<AuthoringValueMemberDefinition> members, AuthoringValueDefinition element)
        {
            TypeId = typeId; Kind = kind; ScalarType = scalarType; AllowsNull = allowsNull;
            EnumValues = enumValues; Flags = flags; Members = members; Element = element;
        }

        public string TypeId { get; }
        public AuthoringValueDefinitionKind Kind { get; }
        public TypeCode ScalarType { get; }
        public bool AllowsNull { get; }
        public IReadOnlyList<string> EnumValues { get; }
        public bool Flags { get; }
        public IReadOnlyList<AuthoringValueMemberDefinition> Members { get; }
        public AuthoringValueDefinition Element { get; }

        public void Receive(AuthoringValue value, string path)
        {
            if (value.TypeId != TypeId) throw Error(path, $"类型 {value.TypeId} 与正式成员 {TypeId} 不一致。");
            if (value is AuthoringNullValue)
            {
                if (!AllowsNull) throw Error(path, "此值类型不能为 null。");
                return;
            }
            switch (Kind)
            {
                case AuthoringValueDefinitionKind.Scalar when value is AuthoringScalarValue scalar:
                    GraphAuthoringFieldValueKind scalarKind = ScalarType switch
                    {
                        TypeCode.Boolean => GraphAuthoringFieldValueKind.Boolean,
                        TypeCode.String => GraphAuthoringFieldValueKind.String,
                        TypeCode.Single or TypeCode.Double or TypeCode.Decimal => GraphAuthoringFieldValueKind.Float,
                        _ => GraphAuthoringFieldValueKind.Integer
                    };
                    if (scalar.Kind != scalarKind) throw Error(path, "标量值种类与正式类型不一致。");
                    try { Convert.ChangeType(scalar.Value, ScalarType, CultureInfo.InvariantCulture); }
                    catch (Exception error) when (error is FormatException || error is OverflowException)
                    { throw Error(path, $"值不属于正式 {ScalarType} 类型：{error.Message}"); }
                    break;
                case AuthoringValueDefinitionKind.Enumeration when value is AuthoringScalarValue enumeration:
                    if (enumeration.Kind != GraphAuthoringFieldValueKind.Enum) throw Error(path, "枚举必须使用正式枚举值种类。");
                    string[] values = Flags ? enumeration.Value.Split(',').Select(text => text.Trim()).ToArray() : new[] { enumeration.Value };
                    if (values.Any(text => !EnumValues.Contains(text, StringComparer.Ordinal)))
                        throw Error(path, "枚举值不属于正式声明。");
                    break;
                case AuthoringValueDefinitionKind.Reference when value is AuthoringReferenceValue:
                    break;
                case AuthoringValueDefinitionKind.Sequence when value is AuthoringSequenceValue sequence:
                    for (int index = 0; index < sequence.Items.Length; index++)
                        Element.Receive(sequence.Items.Span[index], path + "/" + index);
                    break;
                case AuthoringValueDefinitionKind.Object when value is AuthoringObjectValue composite:
                    var fields = composite.Members.ToArray().ToDictionary(member => member.FieldId.Value, member => member.Value,
                        StringComparer.Ordinal);
                    if (!Members.Select(member => member.Id).ToHashSet(StringComparer.Ordinal).SetEquals(fields.Keys))
                        throw Error(path, "复合值成员与原公开构造及作者成员不一致。");
                    foreach (AuthoringValueMemberDefinition member in Members) member.Definition.Receive(fields[member.Id], path + "/" + member.Id);
                    break;
                default: throw Error(path, $"值结构不属于正式 {Kind} 合同。");
            }
        }

        static InvalidOperationException Error(string path, string reason) => new(path + ": " + reason);
    }

    public sealed class AuthoringValueMemberDefinition
    {
        public AuthoringValueMemberDefinition(string id, AuthoringValueDefinition definition)
        { Id = id; Definition = definition; }
        public string Id { get; }
        public AuthoringValueDefinition Definition { get; }
    }
}
