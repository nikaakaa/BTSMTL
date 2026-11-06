using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ThirdPerson.ValueGeneration
{
    internal static class ValueTypeMembers
    {
        internal static T[] StorageConstructors<T>(IEnumerable<T> candidates, Func<T, IMethodSymbol> constructor)
        {
            T[] matches = candidates.ToArray();
            return matches.Any(candidate => constructor(candidate).Parameters.Length != 0)
                ? matches.Where(candidate => constructor(candidate).Parameters.Length != 0).ToArray() : matches;
        }

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
