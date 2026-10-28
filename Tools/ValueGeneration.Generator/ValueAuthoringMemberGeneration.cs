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
                string[] stored = AuthoringStoredMember(compilation, member);
                if (stored == null) return Array.Empty<string>();
                path.AddRange(stored);
                type = (member is IPropertySymbol property ? property.Type : ((IFieldSymbol)member).Type) as INamedTypeSymbol;
            }
            return path.ToArray();
        }

        static string[] AuthoringStoredMember(Compilation compilation, ISymbol member)
        {
            if (member is IFieldSymbol field) return new[] { field.Name };
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

        static string[] AuthoringStoredExpression(Compilation compilation, IOperation expression)
        {
            switch (expression)
            {
                case IInstanceReferenceOperation _:
                    return Array.Empty<string>();
                case IConversionOperation conversion:
                    return AuthoringStoredExpression(compilation, conversion.Operand);
                case ICoalesceOperation coalesce when !AuthoringReadsInstance(coalesce.WhenNull):
                    return AuthoringStoredExpression(compilation, coalesce.Value);
                case IFieldReferenceOperation field when !field.Field.IsStatic:
                    return AuthoringStoredAccess(compilation, field.Instance, new[] { field.Field.Name });
                case IPropertyReferenceOperation property when !property.Property.IsStatic:
                    return AuthoringStoredAccess(compilation, property.Instance,
                        AuthoringStoredMember(compilation, property.Property));
                default:
                    return null;
            }
        }

        static string[] AuthoringStoredAccess(Compilation compilation, IOperation instance, string[] members)
        {
            if (members == null) return null;
            string[] owner = AuthoringStoredExpression(compilation, instance);
            return owner == null ? null : owner.Concat(members).ToArray();
        }

        static bool AuthoringReadsInstance(IOperation expression) => expression is IInstanceReferenceOperation ||
            expression.Children.Any(AuthoringReadsInstance);
    }
}
