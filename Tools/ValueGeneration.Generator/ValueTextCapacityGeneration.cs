using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        static bool TryMaximumTextReferences(ITypeSymbol type, HashSet<string> path, out int maximum)
        {
            maximum = 0;
            if (HasAttribute(type, TextAttribute))
            {
                maximum = 1;
                return true;
            }
            if (NullableElement(type) is ITypeSymbol nullable)
                return TryMaximumTextReferences(nullable, path, out maximum);
            if (type.SpecialType != SpecialType.None && type.IsValueType || type.SpecialType == SpecialType.System_String ||
                type.TypeKind == TypeKind.Enum || type.TypeKind == TypeKind.Pointer)
                return true;
            if (type is ITypeParameterSymbol)
                return type.IsUnmanagedType;
            if (type.TypeKind == TypeKind.Error || type.SpecialType == SpecialType.System_Object ||
                type.SpecialType == SpecialType.System_Array)
                return false;
            if (!path.Add(Identity(type)))
                return false;
            if (SequenceElement(type) is ITypeSymbol element)
                return TryMaximumTextReferences(element, path, out int item) && item == 0;
            if (type is INamedTypeSymbol map && map.IsGenericType &&
                (Identity(map.OriginalDefinition) == "System.Collections.Generic.Dictionary<TKey, TValue>" || MapTypes(type) != null))
                return map.TypeArguments.All(argument =>
                    TryMaximumTextReferences(argument, new HashSet<string>(path), out int count) && count == 0);
            if (!(type is INamedTypeSymbol record) || type.TypeKind == TypeKind.Interface)
                return false;
            int branchMaximum = 0;
            foreach (Member member in ValueMembers(record).Where(member =>
                member.Symbol.Name != "Kind" && !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueStorageIgnoreAttribute")))
            {
                if (!TryMaximumTextReferences(member.Type, new HashSet<string>(path), out int count))
                    return false;
                if (member.IsCase)
                    branchMaximum = System.Math.Max(branchMaximum, count);
                else
                    maximum = checked(maximum + count);
            }
            maximum = checked(maximum + branchMaximum);
            return true;
        }
    }
}
