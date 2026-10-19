using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ThirdPerson.ValueGeneration.Generator
{
    public sealed partial class ValueGenerator
    {
        const string HashAttribute = "ThirdPersonSimulation.ValueHashAttribute";

        static void EmitValueHashes(GeneratorExecutionContext context, IEnumerable<IMethodSymbol> methods)
        {
            var compilation = (CSharpCompilation)context.Compilation;
            compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
            foreach (IMethodSymbol original in methods.OrderBy(Identity, StringComparer.Ordinal))
            {
                var declaration = (MethodDeclarationSyntax)original.DeclaringSyntaxReferences[0].GetSyntax();
                var method = (IMethodSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration);
                string reason = null;
                if (!TryHashMethod(method, declaration, out reason))
                {
                    ReportHashError(context, original, reason);
                    continue;
                }
                string identity;
                using (SHA256 hash = SHA256.Create())
                    identity = string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(Identity(method)))
                        .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
                var emitter = new ValueHashEmitter(compilation, method.ContainingType, identity);
                foreach (IParameterSymbol parameter in method.Parameters)
                    if (!emitter.Prepare(parameter.Type, out reason))
                        break;
                if (reason != null)
                {
                    ReportHashError(context, original, reason);
                    continue;
                }
                var code = new StringBuilder();
                int depth = BeginStateLayoutType(code, method.ContainingType);
                code.Append(StateAccessibility(method.DeclaredAccessibility)).Append(" static partial int ")
                    .Append(Escape(method.Name)).Append('(')
                    .Append(string.Join(", ", method.Parameters.Select(parameter =>
                        TypeName(parameter.Type) + " " + Escape(parameter.Name))))
                    .AppendLine(") { unchecked {");
                code.AppendLine("int hash = 17;");
                foreach (IParameterSymbol parameter in method.Parameters)
                    emitter.AppendCall(code, parameter.Type, Escape(parameter.Name));
                code.AppendLine("return hash;");
                code.AppendLine("} }");
                emitter.Emit(code);
                EndStateLayoutType(code, depth);
                context.AddSource(StateLayoutHint(method.ContainingType, "Hash_" + method.Name + "_" + identity),
                    SourceText.From(code.ToString(), Encoding.UTF8));
            }
        }

        static bool TryHashMethod(IMethodSymbol method, MethodDeclarationSyntax declaration, out string reason)
        {
            reason = null;
            if (!method.IsStatic || method.Arity != 0 || method.ReturnsByRef || method.ReturnsByRefReadonly ||
                method.ReturnType.SpecialType != SpecialType.System_Int32 ||
                !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || declaration.Body != null || declaration.ExpressionBody != null)
                reason = "ValueHash 必须声明为非泛型 static partial int 方法，由生成器实现方法体";
            else if (method.Parameters.Length == 0 || method.Parameters.Any(parameter =>
                parameter.RefKind != RefKind.None || HashSpanElement(parameter.Type) == null || ContainsStateTypeParameter(parameter.Type)))
                reason = "ValueHash 参数必须是按值借用的实际闭合 ReadOnlySpan<T>，且至少有一个参数";
            else
                for (INamedTypeSymbol owner = method.ContainingType; owner != null; owner = owner.ContainingType)
                    if (!IsStatePartial(owner) || ContainsStateTypeParameter(owner) ||
                        owner.TypeKind != TypeKind.Class && owner.TypeKind != TypeKind.Struct)
                    {
                        reason = "ValueHash 及其外层类型必须是闭合 partial class 或 partial struct";
                        break;
                    }
            return reason == null;
        }

        static ITypeSymbol HashSpanElement(ITypeSymbol type) => type is INamedTypeSymbol named &&
            Identity(named.OriginalDefinition) == "System.ReadOnlySpan<T>" ? named.TypeArguments[0] : null;

        static void ReportHashError(GeneratorExecutionContext context, ISymbol symbol, string reason) =>
            context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, symbol.Locations.FirstOrDefault(), Identity(symbol), reason));

        sealed class ValueHashEmitter
        {
            readonly Compilation m_Compilation;
            readonly INamedTypeSymbol m_Owner;
            readonly string m_Prefix;
            readonly List<HashType> m_Types = new List<HashType>();

            public ValueHashEmitter(Compilation compilation, INamedTypeSymbol owner, string identity)
            {
                m_Compilation = compilation;
                m_Owner = owner;
                m_Prefix = "__ValueHash" + identity + "_";
            }

            public bool Prepare(ITypeSymbol type, out string reason)
            {
                reason = null;
                if (Index(type) >= 0)
                    return true;
                var plan = new HashType { Type = type, Element = HashSpanElement(type) };
                m_Types.Add(plan);
                if (plan.Element != null)
                    return Prepare(plan.Element, out reason);
                if (type.SpecialType == SpecialType.System_String || HashInteger(type.SpecialType))
                    return true;
                if (!(type is INamedTypeSymbol named) || ContainsStateTypeParameter(type))
                    reason = Identity(type) + ": 无法生成闭合原值的确定性哈希";
                else if (named.TypeKind == TypeKind.Enum)
                    return Prepare(named.EnumUnderlyingType, out reason);
                else if (named.TypeKind != TypeKind.Struct || named.SpecialType != SpecialType.None || named.IsRefLikeType)
                    reason = Identity(type) + ": ValueHash 只接受整数、布尔、UTF16 文本及由这些原值组成的 struct";
                else
                {
                    plan.Union = GetValueUnionShape(named);
                    plan.Members = StorageMembers(named);
                    if (plan.Union != null)
                    {
                        plan.Members = plan.Members.Where(member => member.Symbol.Name != "Kind")
                            .Concat(plan.Union.Members.Where(member => member.Generated &&
                                !plan.Members.Any(original => original.Symbol.Name == member.Symbol.Name)))
                            .OrderBy(member => Identity(member.Symbol), StringComparer.Ordinal).ToArray();
                        if (plan.Union.KindMember != null && !CanRead(plan.Union.KindMember))
                            reason = Identity(plan.Union.KindMember) + ": ValueHash 无法读取联合的真实 Kind 成员";
                    }
                    Dictionary<string, int> offsets = null;
                    if (plan.Members.Any(member => !member.Generated && !CanRead(member.Symbol)))
                        TryExplicitHashOffsets(named, out offsets);
                    var included = new List<Member>();
                    foreach (Member member in plan.Members)
                    {
                        if (reason != null)
                            break;
                        if (!member.Generated && !CanRead(member.Symbol))
                        {
                            if (IsCoveredAlias(member, plan.Members, offsets))
                                continue;
                            reason = Identity(member.Symbol) + ": ValueHash 无法读取真实存储成员，不能省略该成员";
                        }
                        else if (member.Symbol is IFieldSymbol field && field.IsFixedSizeBuffer)
                            reason = Identity(member.Symbol) + ": ValueHash 尚未声明 fixed buffer 的元素读取协议";
                        else if (!Prepare(member.Type, out reason))
                            reason = Identity(member.Symbol) + " -> " + reason;
                        included.Add(member);
                    }
                    plan.Members = included.ToArray();
                }
                return reason == null;
            }

            bool CanRead(ISymbol symbol) => symbol is IPropertySymbol property
                ? property.GetMethod != null && property.Parameters.Length == 0 &&
                    m_Compilation.IsSymbolAccessibleWithin(property.GetMethod, m_Owner)
                : m_Compilation.IsSymbolAccessibleWithin(symbol, m_Owner);

            bool IsCoveredAlias(Member member, Member[] members, Dictionary<string, int> offsets)
            {
                if (offsets == null || !(member.Symbol is IFieldSymbol field) ||
                    !offsets.TryGetValue(field.MetadataName, out int start) || start < 0 || HashFieldWidth(field.Type) == 0)
                    return false;
                int end = start + HashFieldWidth(field.Type);
                foreach (IFieldSymbol readable in members.Select(value => value.Symbol).OfType<IFieldSymbol>()
                    .Where(value => CanRead(value) && HashFieldWidth(value.Type) != 0 && offsets.ContainsKey(value.MetadataName))
                    .OrderBy(value => offsets[value.MetadataName]))
                {
                    int offset = offsets[readable.MetadataName];
                    if (offset > start)
                        break;
                    start = Math.Max(start, offset + HashFieldWidth(readable.Type));
                    if (start >= end)
                        return true;
                }
                return false;
            }

            bool TryExplicitHashOffsets(INamedTypeSymbol type, out Dictionary<string, int> offsets)
            {
                offsets = null;
                if (type.DeclaringSyntaxReferences.Length != 0)
                {
                    AttributeData layout = type.GetAttributes().SingleOrDefault(attribute => Identity(attribute.AttributeClass) == StructLayoutAttribute);
                    if (layout == null || Convert.ToInt32(layout.ConstructorArguments[0].Value, CultureInfo.InvariantCulture) != 2)
                        return false;
                    offsets = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>().Where(field => !field.IsStatic))
                    {
                        AttributeData offset = field.GetAttributes().SingleOrDefault(attribute =>
                            Identity(attribute.AttributeClass) == "System.Runtime.InteropServices.FieldOffsetAttribute");
                        if (offset == null)
                        {
                            offsets = null;
                            return false;
                        }
                        offsets.Add(field.MetadataName, (int)offset.ConstructorArguments[0].Value);
                    }
                    return true;
                }
                foreach (PortableExecutableReference reference in m_Compilation.References.OfType<PortableExecutableReference>()
                    .Where(reference => SymbolEqualityComparer.Default.Equals(m_Compilation.GetAssemblyOrModuleSymbol(reference), type.ContainingAssembly)))
                {
                    Metadata metadata = reference.GetMetadata();
                    IEnumerable<ModuleMetadata> modules = metadata is AssemblyMetadata assembly ? assembly.GetModules() : new[] { (ModuleMetadata)metadata };
                    foreach (ModuleMetadata module in modules)
                    {
                        MetadataReader reader = module.GetMetadataReader();
                        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
                        {
                            if (HashMetadataName(reader, handle) != HashMetadataName(type.OriginalDefinition))
                                continue;
                            TypeDefinition definition = reader.GetTypeDefinition(handle);
                            if ((definition.Attributes & TypeAttributes.LayoutMask) != TypeAttributes.ExplicitLayout)
                                return false;
                            offsets = definition.GetFields().Select(reader.GetFieldDefinition)
                                .Where(field => (field.Attributes & FieldAttributes.Static) == 0)
                                .ToDictionary(field => reader.GetString(field.Name), field => field.GetOffset(), StringComparer.Ordinal);
                            return true;
                        }
                    }
                }
                return false;
            }

            static string HashMetadataName(INamedTypeSymbol type) => type.ContainingType != null ?
                HashMetadataName(type.ContainingType) + "+" + type.MetadataName :
                (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;

            static string HashMetadataName(MetadataReader reader, TypeDefinitionHandle handle)
            {
                TypeDefinition type = reader.GetTypeDefinition(handle);
                TypeDefinitionHandle owner = type.GetDeclaringType();
                string name = reader.GetString(type.Name);
                if (!owner.IsNil)
                    return HashMetadataName(reader, owner) + "+" + name;
                string names = reader.GetString(type.Namespace);
                return names.Length == 0 ? name : names + "." + name;
            }

            static int HashFieldWidth(ITypeSymbol type)
            {
                if (type is INamedTypeSymbol named && named.TypeKind == TypeKind.Enum)
                    return HashFieldWidth(named.EnumUnderlyingType);
                switch (type.SpecialType)
                {
                    case SpecialType.System_Boolean:
                    case SpecialType.System_Byte:
                    case SpecialType.System_SByte: return 1;
                    case SpecialType.System_Int16:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_Char: return 2;
                    case SpecialType.System_Int32:
                    case SpecialType.System_UInt32: return 4;
                    case SpecialType.System_Int64:
                    case SpecialType.System_UInt64: return 8;
                    default: return 0;
                }
            }

            public void AppendCall(StringBuilder code, ITypeSymbol type, string value) =>
                code.Append(m_Prefix).Append(Index(type)).Append("(ref hash, ").Append(value).AppendLine(");");

            int Index(ITypeSymbol type) => m_Types.FindIndex(plan => SymbolEqualityComparer.Default.Equals(plan.Type, type));

            public void Emit(StringBuilder code)
            {
                foreach (HashType plan in m_Types)
                {
                    code.Append("static void ").Append(m_Prefix).Append(Index(plan.Type)).Append("(ref int hash, in ")
                        .Append(TypeName(plan.Type)).AppendLine(" value) { unchecked {");
                    if (plan.Element != null)
                    {
                        AppendMix(code, "value.Length");
                        code.AppendLine("for (int index = 0; index < value.Length; index++) {");
                        AppendCall(code, plan.Element, "in value[index]");
                        code.AppendLine("}");
                    }
                    else if (plan.Type.SpecialType == SpecialType.System_String)
                    {
                        code.AppendLine("if (value == null) {");
                        AppendMix(code, "-1");
                        code.AppendLine("return;");
                        code.AppendLine("}");
                        AppendMix(code, "value.Length");
                        code.AppendLine("for (int index = 0; index < value.Length; index++) {");
                        AppendMix(code, "value[index]");
                        code.AppendLine("}");
                    }
                    else if (HashInteger(plan.Type.SpecialType))
                        AppendInteger(code, plan.Type.SpecialType, "value");
                    else if (plan.Type.TypeKind == TypeKind.Enum)
                    {
                        var underlying = ((INamedTypeSymbol)plan.Type).EnumUnderlyingType;
                        AppendCall(code, underlying, "(" + TypeName(underlying) + ")value");
                    }
                    else
                        AppendMembers(code, plan);
                    code.AppendLine("} }");
                }
            }

            void AppendMembers(StringBuilder code, HashType plan)
            {
                if (plan.Union != null)
                {
                    var kind = plan.Union.KindOwner.TypeKind == TypeKind.Enum ? plan.Union.KindOwner.EnumUnderlyingType :
                        (plan.Union.KindMember is IPropertySymbol property ? property.Type :
                            (plan.Union.KindMember as IFieldSymbol)?.Type) as INamedTypeSymbol;
                    AppendInteger(code, kind != null && kind.TypeKind == TypeKind.Enum ? kind.EnumUnderlyingType.SpecialType :
                        kind?.SpecialType ?? SpecialType.System_Int32, "value.Kind");
                }
                foreach (Member member in plan.Members.Where(member => !member.IsCase))
                    AppendCall(code, member.Type, "value." + Escape(member.Symbol.Name));
                if (plan.Union == null)
                    return;
                code.AppendLine("switch (value.Kind) {");
                foreach (Member member in plan.Members.Where(member => member.IsCase))
                {
                    code.Append("case ").Append(plan.Union.KindType).Append('.').Append(Escape(member.Symbol.Name)).AppendLine(":");
                    AppendCall(code, member.Type, "value." + Escape(member.Symbol.Name));
                    code.AppendLine("break;");
                }
                code.AppendLine("}");
            }

            static void AppendInteger(StringBuilder code, SpecialType type, string value)
            {
                AppendMix(code, type == SpecialType.System_Boolean ? "(" + value + " ? 1 : 0)" : "(int)" + value);
                if (type == SpecialType.System_Int64 || type == SpecialType.System_UInt64)
                    AppendMix(code, "(int)((" + (type == SpecialType.System_UInt64 ? "ulong" : "long") + ")" + value + " >> 32)");
            }

            static void AppendMix(StringBuilder code, string value) =>
                code.Append("hash = hash * 397 ^ ").Append(value).AppendLine(";");

            static bool HashInteger(SpecialType type) => type == SpecialType.System_Boolean || type == SpecialType.System_Char ||
                type == SpecialType.System_Byte || type == SpecialType.System_SByte || type == SpecialType.System_Int16 ||
                type == SpecialType.System_UInt16 || type == SpecialType.System_Int32 || type == SpecialType.System_UInt32 ||
                type == SpecialType.System_Int64 || type == SpecialType.System_UInt64;

            sealed class HashType
            {
                public ITypeSymbol Type;
                public ITypeSymbol Element;
                public Member[] Members;
                public ValueUnionShape Union;
            }
        }
    }
}
