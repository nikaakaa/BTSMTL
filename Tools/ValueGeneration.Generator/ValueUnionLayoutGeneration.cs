using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        const int ValueUnionPayloadOffset = 8;

        sealed class ValueUnionLayout
        {
            internal int PayloadOffset => ValueUnionPayloadOffset;
        }

        static LayoutKind RequestedValueUnionLayout(INamedTypeSymbol type) => type.GetAttributes()
            .Where(attribute => Identity(attribute.AttributeClass) == UnionAttribute)
            .SelectMany(attribute => attribute.NamedArguments).Where(argument => argument.Key == "Layout")
            .Select(argument => (LayoutKind)Convert.ToInt32(argument.Value.Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(LayoutKind.Auto).Single();

        static bool TryExplicitValueUnionLayout(CSharpCompilation compilation, INamedTypeSymbol type,
            out ValueUnionLayout layout, out string reason)
        {
            layout = null;
            reason = null;
            LayoutKind kind = RequestedValueUnionLayout(type);
            if (kind == LayoutKind.Auto)
                return true;
            if (kind != LayoutKind.Explicit)
            {
                reason = "ValueUnion 的生成布局只接受 Auto 或 Explicit";
                return false;
            }
            compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
            SyntaxNode declaration = type.DeclaringSyntaxReferences[0].GetSyntax();
            type = (INamedTypeSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration);
            ValueUnionShape union = GetValueUnionShape(type);
            if (type.TypeKind != TypeKind.Struct || ContainsStateTypeParameter(type) || type.Arity != 0 ||
                type.ContainingType != null || union == null || union.Definition == null || union.KindMember != null ||
                union.Common.Length != 0 || union.Cases.Any(member => !member.Generated) ||
                type.GetMembers().OfType<IFieldSymbol>().Any(field => !field.IsStatic) || HasAttribute(type, StructLayoutAttribute))
            {
                reason = "Explicit ValueUnion 必须在空的非泛型顶层 struct 上引用闭合联合声明，Kind、实例字段及布局由生成器提供";
                return false;
            }
            if (!new StateLayoutSchema(compilation).Append(new StringBuilder(), type, out reason))
                return false;
            layout = new ValueUnionLayout();
            return true;
        }

        static void EmitValueUnionPayloadAccess(StringBuilder code, INamedTypeSymbol type, string kindType)
        {
            string fullType = TypeName(type);
            code.Append("private ").Append(Escape(type.Name)).Append('(').Append(kindType)
                .AppendLine(" kind) { this = default; Kind = kind; }");
            code.Append("public static unsafe void ReadPayload(").Append(kindType)
                .Append(" kind, global::System.ReadOnlySpan<byte> payload, ref ").Append(fullType)
                .AppendLine(" value) {");
            code.Append("value = new ").Append(fullType).AppendLine("(kind);");
            code.Append("fixed (").Append(fullType).AppendLine("* destination = &value)");
            code.Append("payload.CopyTo(new global::System.Span<byte>((byte*)destination + ")
                .Append(ValueUnionPayloadOffset).AppendLine(", payload.Length)); }");
            code.Append("public static unsafe void WritePayload(in ").Append(fullType)
                .AppendLine(" value, global::System.Span<byte> payload) {");
            code.Append("fixed (").Append(fullType).AppendLine("* source = &value)");
            code.Append("new global::System.ReadOnlySpan<byte>((byte*)source + ")
                .Append(ValueUnionPayloadOffset).AppendLine(", payload.Length).CopyTo(payload); }");
        }
    }
}
