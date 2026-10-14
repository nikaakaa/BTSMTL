using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        static void EmitValueChanges(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            AttributeData operations = type.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == ValueOperationsAttribute);
            var methods = operations.NamedArguments.Where(argument => argument.Key == "CopyMethod" || argument.Key == "ClearMethod")
                .ToDictionary(argument => argument.Key, argument => (string)argument.Value.Value, StringComparer.Ordinal);
            if (methods.Count == 0) return;
            Member[] members = StorageMembers(type);
            string reason = null;
            if (type.IsReadOnly || HasAttribute(type, UnionAttribute) || members.Any(member => member.IsCase))
                reason = "原值复制与清理只适用于可变正式数据记录，联合值保持不可变";
            else if (type.DeclaringSyntaxReferences.Any(reference => !(reference.GetSyntax() is TypeDeclarationSyntax declaration) ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
                reason = "原值操作必须在 partial 原类型生成";
            else if (members.Any(member => !ValueChangeCanAssign(member)))
                reason = "原值操作须能赋值原成员，readonly 仅用于预分配数组或集合";
            if (reason != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, type.Locations.FirstOrDefault(), Identity(type), reason));
                return;
            }
            bool resources = !operations.NamedArguments.Any(argument => argument.Key == "Resources" && argument.Value.Value is false);
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            var owners = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol owner = type.ContainingType; owner != null; owner = owner.ContainingType)
                owners.Push(owner);
            foreach (INamedTypeSymbol owner in owners)
                EmitTypeDeclaration(code, owner);
            EmitTypeDeclaration(code, type);
            foreach (var method in methods.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                bool copy = method.Key == "CopyMethod";
                IMethodSymbol declaration = type.GetMembers(method.Value).OfType<IMethodSymbol>().SingleOrDefault(candidate =>
                    !candidate.IsStatic && candidate.ReturnsVoid && candidate.Parameters.Length == (copy ? 1 : 0) &&
                    (!copy || SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, type)));
                if (declaration != null && !declaration.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax() is MethodDeclarationSyntax syntax && syntax.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                    syntax.Body == null && syntax.ExpressionBody == null))
                    continue;
                if (declaration == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, type.Locations.FirstOrDefault(),
                        Identity(type), method.Value + ": 必须在原类型声明匹配的 partial void 方法"));
                    return;
                }
                code.Append(declaration.DeclaredAccessibility.ToString().ToLowerInvariant()).Append(" partial void ")
                    .Append(Escape(method.Value)).Append('(');
                string source = copy ? Escape(declaration.Parameters[0].Name) : null;
                if (copy)
                    code.Append(declaration.Parameters[0].RefKind == RefKind.In ? "in " : "")
                        .Append(TypeName(type)).Append(' ').Append(source);
                code.AppendLine(") {");
                if (resources)
                {
                    if (copy)
                        foreach (Member member in members)
                            HoldValueMember(code, "Retain", member, source);
                    foreach (Member member in members)
                        HoldValueMember(code, "Release", member, "this");
                }
                foreach (Member member in members)
                    if (copy)
                        EmitValueMemberCopy(code, member, source + "." + Escape(member.Symbol.Name),
                            "this." + Escape(member.Symbol.Name), 0);
                    else
                        EmitValueMemberClear(code, member.Type, "this." + Escape(member.Symbol.Name), 0);
                code.AppendLine("}");
            }
            foreach (INamedTypeSymbol owner in owners)
                code.AppendLine("}");
            code.AppendLine("} }");
            context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') +
                ".Changes.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
        }

        static bool ValueChangeCanAssign(Member member) => IsPreparedCollection(member.Type) || member.Type is IArrayTypeSymbol ||
            !(member.Symbol is IFieldSymbol field && field.IsReadOnly ||
              member.Symbol is IPropertySymbol property && property.SetMethod == null);

        static void EmitValueMemberCopy(StringBuilder code, Member member, string source, string target, int depth)
        {
            ITypeSymbol type = member.Type;
            if (BufferElement(type) != null)
                code.Append(target).Append(".CopyFrom(").Append(source).AppendLine(".Span);");
            else if (type is IArrayTypeSymbol array)
            {
                SnapshotLoop(code, source + ".Length", depth);
                EmitValueMemberCopy(code, new Member { Type = array.ElementType }, source + "[__snapshotIndex" + depth + "]",
                    target + "[__snapshotIndex" + depth + "]", depth + 1);
                code.AppendLine("}");
            }
            else if (MapTypes(type) != null)
            {
                code.Append(target).AppendLine(".Clear();");
                SnapshotLoop(code, source + ".Count", depth);
                string index = "__snapshotIndex" + depth;
                code.Append(target).Append(".Add(").Append(source).Append(".Keys[").Append(index).Append("], ")
                    .Append(source).Append(".Values[").Append(index).AppendLine("]);");
                code.AppendLine("}");
            }
            else if (IsPreparedCollection(type))
            {
                code.Append(target).AppendLine(".Clear();");
                SnapshotLoop(code, source + ".Count", depth);
                string value = source + "[__snapshotIndex" + depth + "]";
                code.Append(target).Append(".Add(").Append(value).AppendLine(");");
                code.AppendLine("}");
            }
            else
            {
                code.Append(target).Append(" = ").Append(source).AppendLine(";");
            }
        }

        static void EmitValueMemberClear(StringBuilder code, ITypeSymbol type, string value, int depth)
        {
            if (type is IArrayTypeSymbol array)
            {
                if (array.ElementType is IArrayTypeSymbol || IsPreparedCollection(array.ElementType))
                {
                    SnapshotLoop(code, value + ".Length", depth);
                    EmitValueMemberClear(code, array.ElementType, value + "[__snapshotIndex" + depth + "]", depth + 1);
                    code.AppendLine("}");
                }
                else
                    code.Append("global::System.Array.Clear(").Append(value).Append(", 0, ").Append(value).AppendLine(".Length);");
            }
            else if (IsPreparedCollection(type))
                code.Append(value).AppendLine(".Clear();");
            else
                code.Append(value).Append(" = ").Append(ValueDefault(type)).AppendLine(";");
        }

        static string ValueDefault(ITypeSymbol type)
        {
            ISymbol member = type.GetMembers().SingleOrDefault(candidate => candidate.IsStatic &&
                HasAttribute(candidate, "ThirdPersonSimulation.ValueDefaultAttribute"));
            return member == null ? "default" : TypeName(type) + "." + Escape(member.Name);
        }
    }
}
