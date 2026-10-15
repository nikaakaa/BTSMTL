using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        static void EmitImplementationIdentity(GeneratorExecutionContext context, INamedTypeSymbol type)
        {
            string[] sources = type.DeclaringSyntaxReferences.Select(reference => reference.SyntaxTree.GetRoot()
                .WithoutTrivia().NormalizeWhitespace(eol: "\n").ToFullString()).Distinct()
                .OrderBy(source => source, StringComparer.Ordinal).ToArray();
            string identity;
            using (SHA256 hash = SHA256.Create())
                identity = string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(Identity(type) + "\n" +
                    string.Join("\n", sources))).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            var code = new StringBuilder();
            code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            var owners = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol owner = type.ContainingType; owner != null; owner = owner.ContainingType)
                owners.Push(owner);
            foreach (INamedTypeSymbol owner in owners) EmitTypeDeclaration(code, owner);
            EmitTypeDeclaration(code, type);
            code.Append("public const string ImplementationIdentity = \"").Append(identity).AppendLine("\";");
            foreach (INamedTypeSymbol owner in owners) code.AppendLine("}");
            code.AppendLine("} }");
            context.AddSource(Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') +
                ".ImplementationIdentity.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
        }
    }
}
