using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        static void EmitValueUnionAccess(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            AttributeData attribute = type.GetAttributes().Single(value => Identity(value.AttributeClass) == UnionAccessAttribute);
            var value = (INamedTypeSymbol)attribute.ConstructorArguments[0].Value;
            ValueUnionShape union = GetValueUnionShape(value);
            if (type.TypeKind != TypeKind.Struct || ContainsStateTypeParameter(type) || type.ContainingType != null ||
                union == null || ContainsStateTypeParameter(value) || union.Common.Length != 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, type.Locations.FirstOrDefault(),
                    Identity(type), "ValueUnionAccess 必须在闭合顶层 struct 上引用不带公共载荷字段的闭合联合"));
                return;
            }
            string name = TypeName(value);
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            EmitTypeDeclaration(code, type);
            code.Append("public ").Append(union.KindType).Append(" Kind(in ").Append(name).AppendLine(" value) => value.Kind;");
            code.Append("public ").Append(name).Append(" Default(").Append(union.KindType).Append(" kind) => ")
                .Append(name).AppendLine(".Default(kind);");
            foreach (Member member in union.Cases)
            {
                code.Append("public ").Append(TypeName(member.Type)).Append(' ').Append(Escape(member.Symbol.Name))
                    .Append("(in ").Append(name).Append(" value) => value.").Append(Escape(member.Symbol.Name)).AppendLine(";");
                code.Append("public ").Append(name).Append(" From").Append(member.Symbol.Name).Append('(')
                    .Append(TypeName(member.Type)).Append(" value) => ").Append(name).Append(".From")
                    .Append(member.Symbol.Name).AppendLine("(value);");
            }
            code.AppendLine("} }");
            context.AddSource(StateLayoutHint(type, "UnionAccess"), SourceText.From(code.ToString(), Encoding.UTF8));
        }
    }
}
