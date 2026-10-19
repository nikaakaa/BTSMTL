using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        const string StructLayoutAttribute = "System.Runtime.InteropServices.StructLayoutAttribute";

        static string ValueUnionKindName(INamedTypeSymbol type)
        {
            ISymbol member = type.GetMembers("Kind").SingleOrDefault(value => !value.IsStatic &&
                (value is IFieldSymbol || value is IPropertySymbol));
            return (member is IPropertySymbol property ? property.Type : (member as IFieldSymbol)?.Type)?.Name ?? type.Name + "Kind";
        }

        static string UnionPayloadFieldName(Member member) => "m_Value_" + member.Symbol.Name;

        static string UnionPayloadField(Member member) => Escape(UnionPayloadFieldName(member));

        sealed class ValueUnionShape
        {
            internal INamedTypeSymbol Definition;
            internal INamedTypeSymbol KindOwner;
            internal ISymbol KindMember;
            internal Member[] Members;
            internal Member[] Cases;
            internal Member[] Common;
            internal string KindIdentity;
            internal string KindType;
        }

        static ValueUnionShape GetValueUnionShape(INamedTypeSymbol type)
        {
            if (HasAttribute(type, UnionTemplateAttribute))
                return null;
            AttributeData attribute = type.GetAttributes().SingleOrDefault(value => Identity(value.AttributeClass) == UnionAttribute);
            INamedTypeSymbol definition = attribute == null ? null : (INamedTypeSymbol)attribute.ConstructorArguments[0].Value;
            Member[] members = StorageMembers(type).Where(member => member.Symbol.Name != "Kind").ToArray();
            if (definition != null)
            {
                Member[] generated = UnionMembers(definition);
                foreach (Member member in generated)
                    member.Generated = true;
                members = members.Concat(generated).OrderBy(member => member.Symbol.Name, StringComparer.Ordinal).ToArray();
            }
            Member[] cases = Cases(members);
            if (cases.Length == 0)
                return null;
            INamedTypeSymbol kindOwner = definition ?? type;
            string kindIdentity = kindOwner.TypeKind == TypeKind.Enum ? Identity(kindOwner) :
                (kindOwner.ContainingNamespace.IsGlobalNamespace ? "" : kindOwner.ContainingNamespace.ToDisplayString() + ".") + ValueUnionKindName(kindOwner);
            return new ValueUnionShape
            {
                Definition = definition,
                KindOwner = kindOwner,
                KindMember = type.GetMembers("Kind").SingleOrDefault(member => !member.IsStatic &&
                    (member is IFieldSymbol || member is IPropertySymbol)),
                Members = members,
                Cases = cases,
                Common = members.Where(member => !member.IsCase).ToArray(),
                KindIdentity = kindIdentity,
                KindType = kindOwner.TypeKind == TypeKind.Enum ? TypeName(kindOwner) :
                    "global::" + (kindOwner.ContainingNamespace.IsGlobalNamespace ? "" : kindOwner.ContainingNamespace.ToDisplayString() + ".") +
                    Escape(ValueUnionKindName(kindOwner))
            };
        }

        static bool IsStateLayoutUnmanaged(ITypeSymbol type) => IsStateLayoutUnmanaged(type, new HashSet<string>(StringComparer.Ordinal));

        static bool IsStateLayoutUnmanaged(ITypeSymbol type, HashSet<string> path)
        {
            if (!(type is INamedTypeSymbol named) || named.TypeKind != TypeKind.Struct || named.DeclaringSyntaxReferences.Length == 0)
                return type.IsUnmanagedType;
            ValueUnionShape union = GetValueUnionShape(named);
            if (!path.Add(Identity(type)))
                return false;
            return named.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).All(field =>
                    union != null && IsUnionKindField(field, union) || IsStateLayoutUnmanaged(field.Type, new HashSet<string>(path))) &&
                (union == null || union.Members.All(member => IsStateLayoutUnmanaged(member.Type, new HashSet<string>(path))));
        }

        static bool IsUnionKindField(IFieldSymbol field, ValueUnionShape union) =>
            SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol ?? field, union.KindMember) &&
            (Identity(field.Type) == union.KindIdentity || field.Type.TypeKind == TypeKind.Error &&
                field.Type.Name == ValueUnionKindName(union.KindOwner) &&
                (field.Type.ContainingNamespace.IsGlobalNamespace ||
                    SymbolEqualityComparer.Default.Equals(field.Type.ContainingNamespace, union.KindOwner.ContainingNamespace)));

        static bool ValidateUnionLayout(INamedTypeSymbol type, ValueUnionShape union, out string reason)
        {
            reason = null;
            AttributeData layout = type.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == StructLayoutAttribute);
            if (layout == null || Convert.ToInt32(layout.ConstructorArguments[0].Value) != 0)
                return true;
            IFieldSymbol[] fields = type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).ToArray();
            int parts = fields.SelectMany(field => (field.AssociatedSymbol ?? field).DeclaringSyntaxReferences)
                .Select(reference => reference.GetSyntax().AncestorsAndSelf().OfType<TypeDeclarationSyntax>().First())
                .Select(declaration => Tuple.Create(declaration.SyntaxTree, declaration.SpanStart)).Distinct().Count();
            if (parts > 1 || fields.Length != 0 && (union.KindMember == null || union.Members.Any(member => member.Generated)))
            {
                reason = "Sequential 联合的载荷与 Kind 必须位于同一原声明，不能把实例字段分布到多个 partial";
                return false;
            }
            return true;
        }

        static void EmitUnionValueStorage(StringBuilder code, INamedTypeSymbol type, ValueUnionShape union)
        {
            Member[] values = union.Common.Concat(union.Cases).ToArray();
            code.Append("public void ReadValueStorage(out ")
                .Append(union.KindType).Append(" Kind");
            foreach (Member member in values)
                code.Append(", ").Append(member.Payload ? "[global::ThirdPersonSimulation.ValuePayload] " : "")
                    .Append("out ").Append(TypeName(member.Type)).Append(' ').Append(Escape(member.Symbol.Name));
            code.AppendLine(") {");
            code.AppendLine("Kind = this.Kind;");
            foreach (Member member in union.Common)
                code.Append(Escape(member.Symbol.Name)).Append(" = this.").Append(Escape(member.Symbol.Name)).AppendLine(";");
            foreach (Member member in union.Cases)
                code.Append(Escape(member.Symbol.Name)).AppendLine(" = default;");
            code.AppendLine("switch (Kind) {");
            foreach (Member member in union.Cases)
                code.Append("case ").Append(union.KindType).Append('.').Append(Escape(member.Symbol.Name)).Append(": ")
                    .Append(Escape(member.Symbol.Name)).Append(" = this.").Append(Escape(member.Symbol.Name)).AppendLine("; break;");
            code.AppendLine("}");
            code.AppendLine("}");
            code.Append("public static ").Append(TypeName(type)).Append(" FromValueStorage(in ").Append(union.KindType).Append(" Kind");
            foreach (Member member in values)
                code.Append(", in ").Append(TypeName(member.Type)).Append(' ').Append(Escape(member.Symbol.Name));
            code.AppendLine(") {");
            code.AppendLine("switch (Kind) {");
            foreach (Member branch in union.Cases)
            {
                code.Append("case ").Append(union.KindType).Append('.').Append(Escape(branch.Symbol.Name))
                    .Append(": return From").Append(branch.Symbol.Name).Append('(');
                foreach (Member member in union.Common)
                    code.Append(Escape(member.Symbol.Name)).Append(", ");
                code.Append(Escape(branch.Symbol.Name)).AppendLine(");");
            }
            if (union.KindOwner.TypeKind != TypeKind.Enum || !union.KindOwner.GetMembers().OfType<IFieldSymbol>()
                .Any(field => field.HasConstantValue && Convert.ToDecimal(field.ConstantValue) == 0))
                code.Append("case (").Append(union.KindType).AppendLine(")0: return default;");
            code.AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(Kind));");
            code.AppendLine("} }");
        }
    }
}
