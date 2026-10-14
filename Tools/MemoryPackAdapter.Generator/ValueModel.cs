using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ThirdPerson.ValueGeneration
{
    internal static class ValueModel
    {
        internal const string UnionCaseAttribute = "ThirdPersonSimulation.ValueUnionCaseAttribute";


        internal const string UnionAttribute = "ThirdPersonSimulation.ValueUnionAttribute";

        internal const string OmittedUnionValueAttribute = "ThirdPersonSimulation.ValueUnionOmittedAttribute";

        internal const string StorageAttribute = "ThirdPersonSimulation.ValueStorageAttribute";

        internal static bool ReadOnlyStorage(ITypeSymbol type) => type.GetAttributes().Any(attribute =>
            Identity(attribute.AttributeClass) == StorageAttribute && attribute.NamedArguments.Any(argument =>
                argument.Key == "ReadOnly" && argument.Value.Value is true));

        internal static bool IsUnmanagedValue(ITypeSymbol type) => IsUnmanagedValue(type, new HashSet<string>());

        static bool IsUnmanagedValue(ITypeSymbol type, HashSet<string> path)
        {
            if (!type.IsUnmanagedType) return false;
            if (NullableElement(type) is ITypeSymbol nullable) return IsUnmanagedValue(nullable, path);
            if (type.SpecialType != SpecialType.None || type.TypeKind == TypeKind.Enum || !(type is INamedTypeSymbol record))
                return true;
            if (!path.Add(Identity(type))) return false;
            return record.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic).All(field =>
                IsUnmanagedValue(field.Type, new HashSet<string>(path))) &&
                (!HasAttribute(type, UnionAttribute) || ValueMembers(record).Where(member => member.IsCase).All(member =>
                    IsUnmanagedValue(member.Type, new HashSet<string>(path))));
        }

        internal static Member[] ValueMembers(INamedTypeSymbol type, Func<ITypeSymbol, ITypeSymbol, bool> implicitReference = null)
        {
            AttributeData union = type.GetAttributes().SingleOrDefault(a => Identity(a.AttributeClass) == UnionAttribute);
            Member[] members = StorageMembers(type, implicitReference);
            if (union == null) return members.Any(member => member.IsCase)
                ? members.Where(member => member.Symbol.Name != "Kind").ToArray() : members;
            Member[] generated = UnionMembers((INamedTypeSymbol)union.ConstructorArguments[0].Value)
                .Where(m => !HasAttribute(m.Type, OmittedUnionValueAttribute)).ToArray();
            return members.Where(m => m.Symbol.Name != "Kind" && !generated.Any(g => g.Symbol.Name == m.Symbol.Name))
                .Concat(generated).ToArray();
        }

        internal static ITypeSymbol NullableElement(ITypeSymbol type) => type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? named.TypeArguments[0] : null;

        internal static ITypeSymbol SequenceElement(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array && array.Rank == 1)
                return array.ElementType;
            if (type is INamedTypeSymbol named)
            {
                switch (Identity(named.OriginalDefinition))
                {
                    case "System.ReadOnlyMemory<T>":
                    case "System.Collections.Generic.List<T>":
                    case "System.Collections.Generic.IReadOnlyList<T>":
                    case "System.Collections.Generic.IEnumerable<T>":
                    case "System.Collections.ObjectModel.ReadOnlyCollection<T>":
                        return named.TypeArguments[0];
                }
            }
            return null;
        }

        internal static string Identity(ISymbol symbol) => (symbol is INamedTypeSymbol named ? RecordType(named) : symbol)
            .ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        internal static string TypeName(ITypeSymbol type) => (type is INamedTypeSymbol named ? RecordType(named) : type)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        internal static INamedTypeSymbol RecordType(INamedTypeSymbol type) => type.IsTupleType ?
            type.TupleUnderlyingType ?? type : type;

        internal static string Escape(string name) => "@" + name;

        internal static bool HasAttribute(ISymbol symbol, string attribute) => symbol.GetAttributes().Any(a => Identity(a.AttributeClass) == attribute);

        internal sealed class Member
        {
            public ISymbol Symbol;
            public ITypeSymbol Type;
            public bool IsCase;
            public string TupleName;
            public string Name => TupleName ?? Symbol.Name;
        }

        internal static Member[] StorageMembers(INamedTypeSymbol type, Func<ITypeSymbol, ITypeSymbol, bool> implicitReference = null)
        {
            if (type.IsTupleType)
                return type.TupleElements.Select((field, index) => new Member
                {
                    Symbol = field,
                    Type = field.Type,
                    TupleName = "Item" + (index + 1)
                }).ToArray();
            Member[] storage = StorageReadMembers(type, implicitReference);
            if (storage != null) return storage.OrderBy(member => member.Name, StringComparer.Ordinal).ToArray();
            Member[] stored = DeclaredStorageMembers(type);
            Member[] readable = PublicReadableMembers(type);
            IMethodSymbol[] constructors = type.InstanceConstructors.Where(constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public && !constructor.IsImplicitlyDeclared &&
                constructor.Parameters.All(parameter => readable.Count(member => Matches(member, parameter)) == 1) &&
                stored.Where(member => member.Symbol.DeclaredAccessibility == Accessibility.Public)
                    .All(member => constructor.Parameters.Any(parameter => Matches(member, parameter)))).ToArray();
            return constructors.Length == 1 ? constructors[0].Parameters.Select(parameter =>
                readable.Single(member => Matches(member, parameter))).OrderBy(member => member.Name, StringComparer.Ordinal).ToArray() : stored;
        }

        internal static Member[] StorageReadMembers(INamedTypeSymbol type, Func<ITypeSymbol, ITypeSymbol, bool> implicitReference = null)
        {
            IMethodSymbol storage = type.GetMembers("ReadValueStorage").OfType<IMethodSymbol>().SingleOrDefault();
            if (storage != null)
                return storage.Parameters.Select(parameter => new Member
                {
                    Symbol = type.GetMembers(parameter.Name).SingleOrDefault() ?? (ISymbol)parameter,
                    Type = parameter.Type
                }).ToArray();
            AttributeData purpose = type.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == StorageAttribute);
            if (purpose == null) return null;
            Member[] stored = DeclaredStorageMembers(type);
            if (!purpose.NamedArguments.Any(argument => argument.Key == "UseConstructor" && (bool)argument.Value.Value))
                return stored;
            Member[] readable = type.GetMembers().OfType<IPropertySymbol>().Where(property =>
                !property.IsStatic && !property.IsIndexer && property.GetMethod != null)
                .Select(property => new Member { Symbol = property, Type = property.Type }).ToArray();
            IMethodSymbol[] constructors = type.InstanceConstructors.Where(constructor =>
                !constructor.IsImplicitlyDeclared && constructor.Parameters.All(parameter => Match(parameter) != null)).ToArray();
            return constructors.Length == 1 ? constructors[0].Parameters.Select(parameter =>
            {
                Member member = Match(parameter);
                return new Member { Symbol = member.Symbol, Type = parameter.Type, IsCase = member.IsCase };
            }).ToArray() : null;

            Member Match(IParameterSymbol parameter)
            {
                Member[] matches = stored.Where(member => MatchesStorage(member, parameter, implicitReference)).ToArray();
                if (matches.Length != 0) return matches.Length == 1 ? matches[0] : null;
                matches = readable.Where(member => MatchesStorage(member, parameter, implicitReference)).ToArray();
                return matches.Length == 1 ? matches[0] : null;
            }
        }

        static Member[] DeclaredStorageMembers(INamedTypeSymbol type)
        {
            ISymbol[] members = type.GetMembers().Where(symbol => !symbol.IsStatic && !symbol.IsImplicitlyDeclared &&
                !HasAttribute(symbol, "ThirdPersonSimulation.ValueStorageIgnoreAttribute")).ToArray();
            bool explicitStorage = HasAttribute(type, StorageAttribute) && members.Any(member =>
                HasAttribute(member, "ThirdPersonSimulation.ValueStorageMemberAttribute"));
            return members.Where(symbol => explicitStorage ?
                HasAttribute(symbol, "ThirdPersonSimulation.ValueStorageMemberAttribute") :
                symbol is IFieldSymbol field && !HasAttribute(field, "System.Runtime.CompilerServices.CompilerGeneratedAttribute") ||
                symbol is IPropertySymbol property && IsAutoProperty(property))
                .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
                .Select(symbol => new Member { Symbol = symbol,
                    Type = symbol is IFieldSymbol field ? field.Type : ((IPropertySymbol)symbol).Type,
                    IsCase = HasAttribute(symbol, UnionCaseAttribute) }).ToArray();
        }

        static Member[] PublicReadableMembers(INamedTypeSymbol type) =>
            type.GetMembers().Where(symbol => !symbol.IsStatic &&
                symbol.DeclaredAccessibility == Accessibility.Public &&
                (symbol is IFieldSymbol field && !field.IsImplicitlyDeclared ||
                 symbol is IPropertySymbol property && !property.IsIndexer &&
                 property.GetMethod?.DeclaredAccessibility == Accessibility.Public))
                .Select(symbol => new Member { Symbol = symbol,
                    Type = symbol is IFieldSymbol field ? field.Type : ((IPropertySymbol)symbol).Type,
                    IsCase = HasAttribute(symbol, UnionCaseAttribute) }).ToArray();

        static bool MatchesStorage(Member member, IParameterSymbol parameter, Func<ITypeSymbol, ITypeSymbol, bool> implicitReference) =>
            string.Equals(MemberKey(member.Name), MemberKey(parameter.Name), StringComparison.OrdinalIgnoreCase) &&
            (SymbolEqualityComparer.Default.Equals(member.Type, parameter.Type) ||
             implicitReference?.Invoke(member.Type, parameter.Type) == true || parameter.Type is IArrayTypeSymbol array &&
             member.Type.AllInterfaces.Concat(new[] { member.Type }).Any(contract => contract is INamedTypeSymbol named &&
                 Identity(named.OriginalDefinition) == "System.Collections.Generic.IEnumerable<T>" &&
                 SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], array.ElementType)));

        internal static string MemberKey(string name) => name.StartsWith("m_", StringComparison.Ordinal) ? name.Substring(2) :
            name.StartsWith("value_", StringComparison.Ordinal) ? name.Substring(6) : name;

        internal static bool CanPass(ITypeSymbol value, ITypeSymbol parameter) =>
            SymbolEqualityComparer.Default.Equals(value, parameter) ||
            value.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract, parameter));

        static bool Matches(Member member, IParameterSymbol parameter) =>
            string.Equals(MemberKey(member.Name), MemberKey(parameter.Name), StringComparison.OrdinalIgnoreCase) &&
            CanPass(member.Type, parameter.Type);

        internal static Member[] UnionMembers(INamedTypeSymbol type) => type.TypeKind == TypeKind.Enum ?
            type.GetMembers().OfType<IFieldSymbol>().Where(field => field.HasConstantValue && HasAttribute(field, UnionCaseAttribute))
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .Select(field => new Member
                {
                    Symbol = field,
                    Type = (ITypeSymbol)field.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == UnionCaseAttribute)
                        .ConstructorArguments.Single().Value,
                    IsCase = true
                }).ToArray() : StorageMembers(type);

        internal static bool IsAutoProperty(IPropertySymbol property) => property.DeclaringSyntaxReferences.Length == 0
            ? property.GetMethod != null && HasAttribute(property.GetMethod, "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
            : property.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is PropertyDeclarationSyntax declaration && declaration.AccessorList != null &&
                declaration.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null));
    }
}

