using System.Collections.Generic;
using System.Globalization;
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
        static void EmitTextReferenceCapacity(GeneratorExecutionContext context, INamedTypeSymbol storage)
        {
            var valueType = (INamedTypeSymbol)storage.GetAttributes()
                .Single(attribute => Identity(attribute.AttributeClass) == TextCapacityAttribute).ConstructorArguments[0].Value;
            string reason = null;
            string expression = null;
            if (storage.DeclaringSyntaxReferences.Any(reference => !(reference.GetSyntax() is TypeDeclarationSyntax declaration) ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
                reason = "文本容量须在 partial 原存储类型生成";
            else if (storage.GetMembers("TextReferenceCapacity").Length != 0)
                reason = "原存储已声明 TextReferenceCapacity，不能重复维护文本容量";
            else
                TryTextCapacityExpression(valueType, storage, null, new HashSet<string>(), out expression, out reason);
            if (reason != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, storage.Locations.FirstOrDefault(),
                    Identity(storage), reason));
                return;
            }
            var code = new StringBuilder();
            code.Append("namespace ").Append(storage.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            var owners = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol owner = storage.ContainingType; owner != null; owner = owner.ContainingType)
                owners.Push(owner);
            foreach (INamedTypeSymbol owner in owners)
                EmitTypeDeclaration(code, owner);
            EmitTypeDeclaration(code, storage);
            code.Append("public int TextReferenceCapacity => checked(").Append(expression).AppendLine(");");
            foreach (INamedTypeSymbol owner in owners)
                code.AppendLine("}");
            code.AppendLine("} }");
            context.AddSource(Identity(storage).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') +
                ".TextCapacity.g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
        }

        static bool TryTextCapacityExpression(ITypeSymbol type, INamedTypeSymbol storage, string memberName,
            HashSet<string> path, out string expression, out string reason)
        {
            expression = null;
            reason = null;
            if (TryMaximumTextReferences(type, new HashSet<string>(), out int maximum))
            {
                expression = maximum.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            if (NullableElement(type) is ITypeSymbol nullable)
                return TryTextCapacityExpression(nullable, storage, memberName, path, out expression, out reason);
            if (SequenceElement(type) is ITypeSymbol element)
            {
                if (!TryMaximumTextReferences(element, new HashSet<string>(), out int itemMaximum))
                {
                    reason = memberName + ": 集合元素的文本引用数必须由原成员确定固定上界";
                    return false;
                }
                Member buffer = StorageMembers(storage).SingleOrDefault(member => member.Symbol.Name == memberName);
                ITypeSymbol expectedStorage = BorrowedStorage(type) ?? type;
                if (buffer == null || !SymbolEqualityComparer.Default.Equals(buffer.Type, expectedStorage))
                {
                    reason = memberName + ": 原存储必须提供同名、同型的准备缓冲，不能另列文本字段表";
                    return false;
                }
                string capacity = "this." + Escape(buffer.Symbol.Name);
                if (buffer.Type is IArrayTypeSymbol)
                    capacity += ".Length";
                else if (buffer.Type.GetMembers("Capacity").OfType<IPropertySymbol>().Any(property =>
                    !property.IsStatic && property.Type.SpecialType == SpecialType.System_Int32 && property.GetMethod != null))
                    capacity += ".Capacity";
                else
                {
                    reason = memberName + ": 原准备缓冲必须公开实际 Capacity，不能以当前 Count 代替";
                    return false;
                }
                expression = itemMaximum == 1 ? capacity : itemMaximum.ToString(CultureInfo.InvariantCulture) + " * " + capacity;
                return true;
            }
            if (!(type is INamedTypeSymbol record) || type.TypeKind == TypeKind.Interface || !path.Add(Identity(type)))
            {
                reason = Identity(type) + ": 文本容量无法由原成员和准备缓冲确定";
                return false;
            }
            var common = new List<string>();
            var branches = new List<string>();
            foreach (Member member in ValueMembers(record).Where(member => member.Symbol.Name != "Kind" &&
                !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueStorageIgnoreAttribute")))
            {
                if (!TryTextCapacityExpression(member.Type, storage, member.Symbol.Name, new HashSet<string>(path),
                    out string count, out reason))
                    return false;
                if (count == "0") continue;
                (member.IsCase ? branches : common).Add(count);
            }
            if (branches.Count != 0)
                common.Add(branches.Aggregate((left, right) => "global::System.Math.Max(" + left + ", " + right + ")"));
            expression = common.Count == 0 ? "0" : string.Join(" + ", common);
            return true;
        }

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
