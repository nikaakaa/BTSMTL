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
        static string[] AuthoringSerializedMemberPath(Compilation compilation, INamedTypeSymbol type,
            AuthoringField field)
        {
            var path = new List<string>();
            foreach (string name in field.Path)
            {
                ISymbol member = type.GetMembers(name).Single();
                IFieldSymbol[] stored = AuthoringStoredMember(compilation, member);
                if (stored == null) return Array.Empty<string>();
                path.AddRange(stored.Select(value => value.Name));
                type = (member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type) as INamedTypeSymbol;
            }
            return path.ToArray();
        }

        static IFieldSymbol[] AuthoringStoredMember(Compilation compilation, ISymbol member)
        {
            if (member is IFieldSymbol field) return new[] { field };
            var property = (IPropertySymbol)member;
            PropertyDeclarationSyntax declaration = property.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax()).OfType<PropertyDeclarationSyntax>().SingleOrDefault();
            if (declaration == null) return null;
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

        static IFieldSymbol[] AuthoringStoredExpression(Compilation compilation, IOperation expression)
        {
            switch (expression)
            {
                case IInstanceReferenceOperation _:
                    return Array.Empty<IFieldSymbol>();
                case IConversionOperation conversion:
                    return AuthoringStoredExpression(compilation, conversion.Operand);
                case IArgumentOperation argument:
                    return AuthoringStoredExpression(compilation, argument.Value);
                case IFieldReferenceOperation field when !field.Field.IsStatic:
                    return AuthoringStoredAccess(compilation, field.Instance, new[] { field.Field });
                case IPropertyReferenceOperation property when !property.Property.IsStatic:
                    return AuthoringStoredAccess(compilation, property.Instance,
                        AuthoringStoredMember(compilation, property.Property));
                default:
                    IFieldSymbol[][] paths = expression.Children
                        .Select(child => AuthoringStoredExpression(compilation, child))
                        .Where(path => path != null && path.Length != 0)
                        .GroupBy(path => string.Join("\0", path.Select(member => member.Name)), StringComparer.Ordinal)
                        .Select(group => group.First()).Take(2).ToArray();
                    return paths.Length == 1 && SymbolEqualityComparer.Default.Equals(
                        paths[0][paths[0].Length - 1].Type, expression.Type) ? paths[0] : null;
            }
        }

        static IFieldSymbol[] AuthoringStoredAccess(Compilation compilation, IOperation instance, IFieldSymbol[] members)
        {
            if (members == null) return null;
            IFieldSymbol[] owner = AuthoringStoredExpression(compilation, instance);
            return owner == null ? null : owner.Concat(members).ToArray();
        }
    }
}
