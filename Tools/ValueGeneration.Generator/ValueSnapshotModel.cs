using System.Linq;
using Microsoft.CodeAnalysis;

namespace ThirdPerson.ValueGeneration
{
    internal static class ValueSnapshotModel
    {
        internal static string SnapshotTypeName(INamedTypeSymbol owner) =>
            (owner.ContainingNamespace.IsGlobalNamespace ? "global::" :
                "global::" + owner.ContainingNamespace.ToDisplayString() + ".") +
            owner.Name + "ValueSnapshot" + (owner.Arity == 0 ? "" :
                "<" + string.Join(", ", owner.TypeArguments.Select(type =>
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ">");

        internal static bool IsSnapshotSource(ITypeSymbol type) =>
            type.GetAttributes().Any(attribute => Name(attribute.AttributeClass) == "ThirdPersonSimulation.ValueSnapshotAttribute") ||
            type is INamedTypeSymbol named &&
                (Name(named) == "ThirdPersonSimulation.IValueSnapshotSource" || named.AllInterfaces.Any(contract =>
                    Name(contract) == "ThirdPersonSimulation.IValueSnapshotSource"));

        internal static ITypeSymbol PreparedSnapshotElement(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named)) return null;
            INamedTypeSymbol sequence = named.AllInterfaces.FirstOrDefault(contract =>
                Name(contract.OriginalDefinition) == "System.Collections.Generic.IReadOnlyList<T>");
            if (sequence == null || !named.GetMembers("Capacity").OfType<IPropertySymbol>()
                    .Any(property => property.Type.SpecialType == SpecialType.System_Int32 && property.GetMethod != null) ||
                !named.GetMembers("Add").OfType<IMethodSymbol>().Any(method => method.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, sequence.TypeArguments[0])) ||
                !named.GetMembers("Clear").OfType<IMethodSymbol>().Any(method => method.Parameters.Length == 0))
                return null;
            return sequence.TypeArguments[0];
        }

        static string Name(ISymbol symbol) => symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }
}
