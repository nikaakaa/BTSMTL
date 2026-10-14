using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        sealed class SnapshotEquality
        {
            readonly Dictionary<string, int> m_Indices = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly List<ITypeSymbol> m_Types = new List<ITypeSymbol>();
            readonly List<bool> m_Resources = new List<bool>();

            int Index(ITypeSymbol type, bool resource)
            {
                string identity = Identity(type) + (resource ? "#resource" : "");
                if (!m_Indices.TryGetValue(identity, out int index))
                {
                    index = m_Types.Count;
                    m_Indices.Add(identity, index);
                    m_Types.Add(type);
                    m_Resources.Add(resource);
                }
                return index;
            }

            public string Equals(ITypeSymbol type, string left, string right, bool resource = false) =>
                "__SnapshotEquals" + Index(type, resource) + "(" + left + ", " + right + ")";

            public string Hash(ITypeSymbol type, string value, bool resource = false) =>
                "__SnapshotHash" + Index(type, resource) + "(" + value + ")";

            public void Emit(StringBuilder code)
            {
                for (int index = 0; index < m_Types.Count; index++)
                {
                    ITypeSymbol type = m_Types[index];
                    code.Append("static bool __SnapshotEquals").Append(index).Append("(in ").Append(TypeName(type))
                        .Append(" left, in ").Append(TypeName(type)).AppendLine(" right) {");
                    EmitValueEquals(code, type, m_Resources[index]);
                    code.AppendLine("}");
                    code.Append("static int __SnapshotHash").Append(index).Append("(in ").Append(TypeName(type))
                        .AppendLine(" value) { unchecked {");
                    EmitValueHash(code, type, m_Resources[index]);
                    code.AppendLine("} }");
                }
            }

            void EmitValueEquals(StringBuilder code, ITypeSymbol type, bool resource)
            {
                if (type.SpecialType == SpecialType.System_String)
                {
                    code.AppendLine("return global::System.String.Equals(left, right, global::System.StringComparison.Ordinal);");
                    return;
                }
                if (resource && type.IsReferenceType)
                {
                    code.AppendLine("return global::System.Object.Equals(left, right);");
                    return;
                }
                if (type.TypeKind == TypeKind.Enum)
                {
                    code.AppendLine("return left == right;");
                    return;
                }
                if (NullableElement(type) is ITypeSymbol nullable)
                {
                    code.AppendLine("if (left.HasValue != right.HasValue) return false;");
                    code.Append("return !left.HasValue || ").Append(Equals(nullable, "left.Value", "right.Value")).AppendLine(";");
                    return;
                }
                if (SequenceElement(type) is ITypeSymbol element)
                {
                    code.Append("if (").Append(SequenceCount(type, "left")).Append(" != ")
                        .Append(SequenceCount(type, "right")).AppendLine(") return false;");
                    SnapshotLoop(code, SequenceCount(type, "left"), 0);
                    code.Append("if (!").Append(Equals(element, SequenceValue(type, "left", "__snapshotIndex0"),
                        SequenceValue(type, "right", "__snapshotIndex0"))).AppendLine(") return false; }");
                    code.AppendLine("return true;");
                    return;
                }
                if (type.SpecialType != SpecialType.None || HasTypedEquals(type))
                {
                    code.AppendLine("return left.Equals(right);");
                    return;
                }
                var named = (INamedTypeSymbol)type;
                Member[] members = EqualityMembers(named);
                string kindType = ValueKindType(named);
                if (kindType != null) code.AppendLine("if (left.Kind != right.Kind) return false;");
                foreach (Member member in members.Where(member => !member.IsCase))
                    code.Append("if (!").Append(Equals(member.Type, "left." + Escape(member.Symbol.Name),
                        "right." + Escape(member.Symbol.Name), HasAttribute(member.Symbol, ValueResourceAttribute))).AppendLine(") return false;");
                if (kindType == null)
                    code.AppendLine("return true;");
                else
                {
                    code.AppendLine("switch (left.Kind) {");
                    foreach (Member member in Cases(members))
                        code.Append("case ").Append(kindType).Append('.').Append(Escape(member.Symbol.Name))
                            .Append(": return ").Append(Equals(member.Type, "left." + Escape(member.Symbol.Name),
                                "right." + Escape(member.Symbol.Name), HasAttribute(member.Symbol, ValueResourceAttribute))).AppendLine(";");
                    code.Append("case (").Append(kindType).AppendLine(")0: return true;");
                    code.AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(left.Kind)); }");
                }
            }

            void EmitValueHash(StringBuilder code, ITypeSymbol type, bool resource)
            {
                if (type.SpecialType == SpecialType.System_String)
                {
                    code.AppendLine("return value == null ? 0 : global::System.StringComparer.Ordinal.GetHashCode(value);");
                    return;
                }
                if (resource && type.IsReferenceType)
                {
                    code.AppendLine("return value == null ? 0 : value.GetHashCode();");
                    return;
                }
                if (NullableElement(type) is ITypeSymbol nullable)
                {
                    code.Append("return value.HasValue ? ").Append(Hash(nullable, "value.Value")).AppendLine(" : 0;");
                    return;
                }
                if (SequenceElement(type) is ITypeSymbol element)
                {
                    code.AppendLine("int hash = 17;");
                    SnapshotLoop(code, SequenceCount(type, "value"), 0);
                    code.Append("hash = hash * 397 ^ ").Append(Hash(element,
                        SequenceValue(type, "value", "__snapshotIndex0"))).AppendLine("; }");
                    code.AppendLine("return hash;");
                    return;
                }
                if (type.TypeKind == TypeKind.Enum)
                {
                    code.Append("return ((").Append(TypeName(((INamedTypeSymbol)type).EnumUnderlyingType))
                        .AppendLine(")value).GetHashCode();");
                    return;
                }
                if (type.SpecialType != SpecialType.None ||
                    type.GetMembers("GetHashCode").OfType<IMethodSymbol>().Any(method => method.IsOverride && method.Parameters.Length == 0))
                {
                    code.AppendLine("return value.GetHashCode();");
                    return;
                }
                var named = (INamedTypeSymbol)type;
                Member[] members = EqualityMembers(named);
                string kindType = ValueKindType(named);
                code.AppendLine("int hash = 17;");
                if (kindType != null) code.AppendLine("hash = hash * 397 ^ (int)value.Kind;");
                foreach (Member member in members.Where(member => !member.IsCase))
                    code.Append("hash = hash * 397 ^ ").Append(Hash(member.Type, "value." + Escape(member.Symbol.Name),
                        HasAttribute(member.Symbol, ValueResourceAttribute))).AppendLine(";");
                if (kindType != null)
                {
                    code.AppendLine("switch (value.Kind) {");
                    foreach (Member member in Cases(members))
                        code.Append("case ").Append(kindType).Append('.').Append(Escape(member.Symbol.Name))
                            .Append(": hash = hash * 397 ^ ").Append(Hash(member.Type, "value." + Escape(member.Symbol.Name),
                                HasAttribute(member.Symbol, ValueResourceAttribute)))
                            .AppendLine("; break;");
                    code.AppendLine("}");
                }
                code.AppendLine("return hash;");
            }

            static bool HasTypedEquals(ITypeSymbol type) => type.GetMembers("Equals").OfType<IMethodSymbol>()
                .Any(method => !method.IsStatic && method.Parameters.Length == 1 &&
                    method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                    SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, type));

            static Member[] EqualityMembers(INamedTypeSymbol type)
            {
                bool explicitEquality = type.GetAttributes().Where(attribute => Identity(attribute.AttributeClass) == ValueOperationsAttribute)
                    .SelectMany(attribute => attribute.NamedArguments).Any(argument => argument.Key == "ExplicitEquality" && argument.Value.Value is true);
                return ValueMembers(type).Concat(type.GetMembers().OfType<IPropertySymbol>().Where(property =>
                        !IsAutoProperty(property) && HasAttribute(property, "ThirdPersonSimulation.ValueEqualityMemberAttribute"))
                    .Select(property => new Member { Symbol = property, Type = property.Type }))
                    .Where(member => member.IsCase || !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueEqualityIgnoreAttribute") &&
                        (!explicitEquality || HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueEqualityMemberAttribute"))).ToArray();
            }

            static string ValueKindType(INamedTypeSymbol type)
            {
                INamedTypeSymbol definition = (INamedTypeSymbol)type.GetAttributes()
                    .SingleOrDefault(attribute => Identity(attribute.AttributeClass) == UnionAttribute)?.ConstructorArguments[0].Value;
                if (definition == null && Cases(ValueMembers(type)).Length == 0) return null;
                INamedTypeSymbol owner = definition ?? type;
                return owner.TypeKind == TypeKind.Enum ? TypeName(owner) :
                    "global::" + owner.ContainingNamespace.ToDisplayString() + "." + Escape(owner.Name) + "Kind";
            }
        }

        static void EmitSnapshotEquals(StringBuilder code, SnapshotShape shape, string left, string right,
            SnapshotEquality equality, int depth)
        {
            switch (shape.Kind)
            {
                case SnapshotKind.Value:
                    code.Append("if (!").Append(equality.Equals(shape.Type, left, right, shape.Resource != null)).AppendLine(") return false;");
                    break;
                case SnapshotKind.Source:
                    code.Append("if (!").Append(left).Append(".Equals(").Append(right).AppendLine(")) return false;");
                    break;
                case SnapshotKind.Array:
                case SnapshotKind.Sequence:
                    string length = shape.Kind == SnapshotKind.Array || shape.Element.Kind == SnapshotKind.Source ? ".Length" : ".Count";
                    code.Append("if (").Append(left).Append(length).Append(" != ").Append(right).Append(length).AppendLine(") return false;");
                    SnapshotLoop(code, left + length, depth);
                    EmitSnapshotEquals(code, shape.Element, left + "[__snapshotIndex" + depth + "]",
                        right + "[__snapshotIndex" + depth + "]", equality, depth + 1);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.Dictionary:
                    code.Append("if (").Append(left).Append(".Count != ").Append(right).AppendLine(".Count) return false;");
                    string pair = "__snapshotPair" + depth;
                    string value = "__snapshotValue" + depth;
                    code.Append("foreach (var ").Append(pair).Append(" in ").Append(left).AppendLine(") {");
                    code.Append("if (!").Append(right).Append(".TryGetValue(").Append(pair).Append(".Key, out var ")
                        .Append(value).AppendLine(")) return false;");
                    EmitSnapshotEquals(code, shape.Element, pair + ".Value", value, equality, depth + 1);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.SortedList:
                case SnapshotKind.KeyedBuffer:
                    code.Append("if (").Append(left).Append(".Count != ").Append(right).AppendLine(".Count) return false;");
                    SnapshotLoop(code, left + ".Count", depth);
                    string index = "__snapshotIndex" + depth;
                    code.Append("if (!").Append(equality.Equals(shape.Key, SnapshotKey(shape, left, index),
                        SnapshotKey(shape, right, index))).AppendLine(") return false;");
                    EmitSnapshotEquals(code, shape.Element, SnapshotValue(shape, left, index),
                        SnapshotValue(shape, right, index), equality, depth + 1);
                    code.AppendLine("}");
                    break;
            }
        }

        static void EmitSnapshotHash(StringBuilder code, SnapshotShape shape, string value, SnapshotEquality equality,
            int depth, string accumulator = "hash")
        {
            switch (shape.Kind)
            {
                case SnapshotKind.Value:
                    code.Append(accumulator).Append(" = ").Append(accumulator).Append(" * 397 ^ ")
                        .Append(equality.Hash(shape.Type, value, shape.Resource != null)).AppendLine(";");
                    break;
                case SnapshotKind.Source:
                    code.Append(accumulator).Append(" = ").Append(accumulator).Append(" * 397 ^ ").Append(value).AppendLine(".GetHashCode();");
                    break;
                case SnapshotKind.Array:
                case SnapshotKind.Sequence:
                    SnapshotLoop(code, value + (shape.Kind == SnapshotKind.Array || shape.Element.Kind == SnapshotKind.Source ? ".Length" : ".Count"), depth);
                    EmitSnapshotHash(code, shape.Element, value + "[__snapshotIndex" + depth + "]", equality, depth + 1, accumulator);
                    code.AppendLine("}");
                    break;
                case SnapshotKind.Dictionary:
                    string pair = "__snapshotPair" + depth;
                    string hash = "__snapshotHash" + depth;
                    string valueHash = "__snapshotValueHash" + depth;
                    code.AppendLine("{");
                    code.Append("int ").Append(hash).AppendLine(" = 0;");
                    code.Append("foreach (var ").Append(pair).Append(" in ").Append(value).AppendLine(") {");
                    code.Append("int ").Append(valueHash).AppendLine(" = 17;");
                    EmitSnapshotHash(code, shape.Element, pair + ".Value", equality, depth + 1, valueHash);
                    code.Append(hash).Append(" ^= ").Append(value).Append(".Comparer.GetHashCode(").Append(pair)
                        .Append(".Key) * 397 ^ ").Append(valueHash).AppendLine("; }");
                    code.Append(accumulator).Append(" = ").Append(accumulator).Append(" * 397 ^ ").Append(hash).AppendLine(";");
                    code.AppendLine("}");
                    break;
                case SnapshotKind.SortedList:
                case SnapshotKind.KeyedBuffer:
                    SnapshotLoop(code, value + ".Count", depth);
                    string index = "__snapshotIndex" + depth;
                    code.Append(accumulator).Append(" = ").Append(accumulator).Append(" * 397 ^ ")
                        .Append(equality.Hash(shape.Key, SnapshotKey(shape, value, index))).AppendLine(";");
                    EmitSnapshotHash(code, shape.Element, SnapshotValue(shape, value, index), equality, depth + 1, accumulator);
                    code.AppendLine("}");
                    break;
            }
        }
    }
}
