using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ThirdPerson.ValueGeneration
{
    internal static class ValueTypeMembers
    {
        internal static IEnumerable<ISymbol> InHierarchy(ITypeSymbol type)
        {
            var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            for (ITypeSymbol current = type; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            {
                if (!SymbolEqualityComparer.Default.Equals(current, type) &&
                    (current.SpecialType == SpecialType.System_Object || current.SpecialType == SpecialType.System_ValueType ||
                     current.SpecialType == SpecialType.System_Enum))
                    yield break;
                foreach (ISymbol member in current.GetMembers())
                {
                    if (overridden.Contains(member)) continue;
                    if (member is IPropertySymbol property)
                        for (IPropertySymbol original = property.OverriddenProperty; original != null; original = original.OverriddenProperty)
                            overridden.Add(original);
                    else if (member is IMethodSymbol method)
                        for (IMethodSymbol original = method.OverriddenMethod; original != null; original = original.OverriddenMethod)
                            overridden.Add(original);
                    yield return member;
                }
            }
        }
    }
}
