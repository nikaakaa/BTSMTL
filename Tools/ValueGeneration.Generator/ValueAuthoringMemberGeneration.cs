using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        const string StoredMemberAttribute = "BTSMTL.Authoring.BtsmtlStoredMemberAttribute";

        sealed class StoredMember
        {
            public StoredMember(string name, ITypeSymbol type)
            {
                Name = name;
                Type = type;
            }

            public string Name { get; }
            public ITypeSymbol Type { get; }
        }

        static string[] AuthoringSerializedMemberPath(Compilation compilation, INamedTypeSymbol type,
            AuthoringField field)
        {
            var path = new List<string>();
            foreach (string name in field.Path)
            {
                ISymbol member = type.GetMembers(name).Single();
                StoredMember[] stored = AuthoringStoredMember(compilation, member);
                if (stored == null) return Array.Empty<string>();
                path.AddRange(stored.Select(value => value.Name));
                type = (member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type) as INamedTypeSymbol;
            }
            return path.ToArray();
        }

        static StoredMember[] AuthoringStoredMember(Compilation compilation, ISymbol member)
        {
            if (member is IFieldSymbol field) return new[] { new StoredMember(field.Name, field.Type) };
            var property = (IPropertySymbol)member;
            PropertyDeclarationSyntax declaration = AuthoringPropertyDeclaration(property);
            return declaration == null ? AuthoringDeclaredStoredMember(property)
                : AuthoringDerivedStoredMember(compilation, declaration);
        }

        static PropertyDeclarationSyntax AuthoringPropertyDeclaration(IPropertySymbol property) =>
            property.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
                .OfType<PropertyDeclarationSyntax>().SingleOrDefault();

        static StoredMember[] AuthoringDerivedStoredMember(Compilation compilation, PropertyDeclarationSyntax declaration)
        {
            ExpressionSyntax expression = declaration.ExpressionBody?.Expression;
            if (expression == null)
            {
                AccessorDeclarationSyntax getter = declaration.AccessorList?.Accessors
                    .SingleOrDefault(accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                expression = getter?.ExpressionBody?.Expression;
                if (expression == null && getter?.Body?.Statements.Count == 1 &&
                    getter.Body.Statements[0] is ReturnStatementSyntax returned)
                    expression = returned.Expression;
            }
            return expression == null ? null : AuthoringStoredExpression(compilation,
                compilation.GetSemanticModel(expression.SyntaxTree).GetOperation(expression));
        }

        static AttributeData AuthoringStoredMemberDeclaration(IPropertySymbol property) =>
            property.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == StoredMemberAttribute);

        // 声明路径末端的值即属性读取结果，因此末端类型取属性类型；中间成员可能是引用方不可见的私有字段。
        static StoredMember[] AuthoringDeclaredStoredMember(IPropertySymbol property)
        {
            AttributeData declaration = AuthoringStoredMemberDeclaration(property);
            if (declaration == null) return null;
            string[] names = AuthoringDeclaredStoredNames(declaration);
            return names.Length == 0 ? null : names.Select((name, index) =>
                new StoredMember(name, index == names.Length - 1 ? property.Type : null)).ToArray();
        }

        static string[] AuthoringDeclaredStoredNames(AttributeData declaration) =>
            declaration.ConstructorArguments[0].Values.Select(value => (string)value.Value).ToArray();

        static void ValidateStoredMemberDeclarations(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
            {
                AttributeData declaration = AuthoringStoredMemberDeclaration(property);
                if (declaration == null) continue;
                string[] declared = AuthoringDeclaredStoredNames(declaration);
                PropertyDeclarationSyntax syntax = AuthoringPropertyDeclaration(property);
                StoredMember[] derived = syntax == null ? null : AuthoringDerivedStoredMember(context.Compilation, syntax);
                if (declared.Length != 0 && derived != null && derived.Select(member => member.Name).SequenceEqual(declared))
                    continue;
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, property.Locations.FirstOrDefault(),
                    Identity(property), "存盘成员声明必须与 getter 实际读取的存盘路径一致：声明 [" + string.Join(", ", declared) +
                    "]，推导 [" + (derived == null ? "无" : string.Join(", ", derived.Select(member => member.Name))) + "]"));
            }
        }

        static StoredMember[] AuthoringStoredExpression(Compilation compilation, IOperation expression)
        {
            switch (expression)
            {
                case IInstanceReferenceOperation _:
                    return Array.Empty<StoredMember>();
                case IConversionOperation conversion:
                    return AuthoringStoredExpression(compilation, conversion.Operand);
                case IArgumentOperation argument:
                    return AuthoringStoredExpression(compilation, argument.Value);
                case IFieldReferenceOperation field when !field.Field.IsStatic:
                    return AuthoringStoredAccess(compilation, field.Instance,
                        new[] { new StoredMember(field.Field.Name, field.Field.Type) });
                case IPropertyReferenceOperation property when !property.Property.IsStatic:
                    return AuthoringStoredAccess(compilation, property.Instance,
                        AuthoringStoredMember(compilation, property.Property));
                default:
                    StoredMember[][] paths = expression.Children
                        .Select(child => AuthoringStoredExpression(compilation, child))
                        .Where(path => path != null && path.Length != 0)
                        .GroupBy(path => string.Join("\0", path.Select(member => member.Name)), StringComparer.Ordinal)
                        .Select(group => group.First()).Take(2).ToArray();
                    return paths.Length == 1 && SymbolEqualityComparer.Default.Equals(
                        paths[0][paths[0].Length - 1].Type, expression.Type) ? paths[0] : null;
            }
        }

        static StoredMember[] AuthoringStoredAccess(Compilation compilation, IOperation instance, StoredMember[] members)
        {
            if (members == null) return null;
            StoredMember[] owner = AuthoringStoredExpression(compilation, instance);
            return owner == null ? null : owner.Concat(members).ToArray();
        }
    }
}
