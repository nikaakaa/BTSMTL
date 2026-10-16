using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        const string StateLayoutAttribute = "ThirdPersonSimulation.ValueStateLayoutAttribute";
        const string StateLayoutFactoryAttribute = "ThirdPersonSimulation.ValueStateLayoutFactoryAttribute";
        const string StateRegionType = "BTSMTL.Orchestration.OrchestrationStateRegion<T>";
        const string Orchestration = "global::BTSMTL.Orchestration.";

        static void EmitStateLayout(GeneratorExecutionContext context, INamedTypeSymbol layout)
        {
            if (!TryStateMembers(layout, true, out Member[] members, out string reason))
            {
                ReportStateLayoutError(context, layout, reason);
                return;
            }
            var code = new StringBuilder();
            var owners = BeginStateLayoutType(code, layout);
            code.Append("[").Append(Runtime).AppendLine("ValueStorageIgnore]");
            code.Append("public ").Append(Orchestration).AppendLine("OrchestrationStateLayout Layout { get; }");
            foreach (Member member in members.Where(value => !IsStateRegion(value)))
            {
                code.Append("[").Append(Runtime).AppendLine("ValueStorageIgnore]");
                code.Append("readonly ").Append(Orchestration).Append("OrchestrationStateRegion<byte> ")
                    .Append(Escape(StateChildRegionName(member))).AppendLine(";");
            }
            code.Append("public ").Append(Escape(layout.Name)).Append("(string generatedDeclarationIdentity, ")
                .Append(Runtime).Append("StableHash contentIdentity, ").Append(Runtime)
                .Append("NumericProfileId numericProfile, ").Append(Runtime).Append("TargetAbiVersion targetAbi");
            foreach (Member member in members)
                code.Append(", ").Append(IsStateRegion(member) ? "int" : TypeName(member.Type)).Append(' ')
                    .Append(Escape(StateParameterName(member)));
            code.AppendLine(") {");
            code.AppendLine("this = default;");
            code.AppendLine("int offset = 0;");
            code.AppendLine("int alignment = 1;");
            foreach (Member member in members)
            {
                bool leaf = IsStateRegion(member);
                string value = "this." + Escape(member.Symbol.Name);
                string regionType = leaf ? TypeName(member.Type) : Orchestration + "OrchestrationStateRegion<byte>";
                string region = leaf ? value : "this." + Escape(StateChildRegionName(member));
                string elementAlignment = leaf ? regionType + ".ElementAlignment" : value + ".Layout.Alignment";
                if (!leaf)
                    code.Append(value).Append(" = ").Append(Escape(StateParameterName(member))).AppendLine(";");
                code.Append("offset = checked((offset + ").Append(elementAlignment).Append(" - 1) / ")
                    .Append(elementAlignment).Append(" * ").Append(elementAlignment).AppendLine(");");
                code.Append(region).Append(" = new ").Append(regionType).Append("(offset, ")
                    .Append(leaf ? Escape(StateParameterName(member)) : value + ".Layout.SizeBytes").AppendLine(");");
                code.Append("offset = checked(offset + ").Append(region).AppendLine(".SizeBytes);");
                code.Append("alignment = global::System.Math.Max(alignment, ").Append(elementAlignment).AppendLine(");");
            }
            code.AppendLine("int sizeBytes = checked((offset + alignment - 1) / alignment * alignment);");
            code.Append("Layout = new ").Append(Orchestration).Append("OrchestrationStateLayout(sizeBytes, alignment, ")
                .Append(Runtime).AppendLine("StableHash.Compute(");
            code.AppendLine("generatedDeclarationIdentity, contentIdentity.ToString(), numericProfile.Value,");
            code.AppendLine("targetAbi.Value.ToString(global::System.Globalization.CultureInfo.InvariantCulture),");
            code.AppendLine("sizeBytes.ToString(global::System.Globalization.CultureInfo.InvariantCulture),");
            code.Append("alignment.ToString(global::System.Globalization.CultureInfo.InvariantCulture)");
            foreach (Member member in members)
            {
                bool leaf = IsStateRegion(member);
                string value = "this." + Escape(member.Symbol.Name);
                string[] identityParts = leaf ? new[] { value + ".Length", value + ".OffsetBytes", value + ".SizeBytes",
                    TypeName(member.Type) + ".ElementSizeBytes", TypeName(member.Type) + ".ElementAlignment" } :
                    new[] { "this." + Escape(StateChildRegionName(member)) + ".OffsetBytes",
                        value + ".Layout.SizeBytes", value + ".Layout.Alignment" };
                foreach (string part in identityParts)
                    code.AppendLine(",").Append(part)
                        .Append(".ToString(global::System.Globalization.CultureInfo.InvariantCulture)");
                if (!leaf)
                    code.AppendLine(",").Append(value).Append(".Layout.Identity.ToString()");
            }
            code.AppendLine("));");
            code.AppendLine("}");
            foreach (Member member in members)
            {
                bool leaf = IsStateRegion(member);
                string method = Escape("Get" + member.Symbol.Name);
                string value = "this." + Escape(member.Symbol.Name);
                string region = leaf ? value : "this." + Escape(StateChildRegionName(member));
                string element = leaf ? TypeName(((INamedTypeSymbol)member.Type).TypeArguments[0]) : null;
                code.Append("public ").Append(leaf ? "global::System.Span<" + element + ">" : Orchestration + "OrchestrationStateView")
                    .Append(' ').Append(method).Append("(").Append(Orchestration).Append("OrchestrationStateView state) => ")
                    .Append(leaf ? "" : value + ".Layout.BindCandidate(").Append("state.Get(").Append(region)
                    .AppendLine(leaf ? ");" : "));");
                code.Append("public ").Append(leaf ? "global::System.ReadOnlySpan<" + element + ">" : Orchestration + "ReadOnlyOrchestrationStateView")
                    .Append(' ').Append(method).Append("(").Append(Orchestration).Append("ReadOnlyOrchestrationStateView state) => ")
                    .Append(leaf ? "" : value + ".Layout.BindCommitted(").Append("state.Get(").Append(region)
                    .AppendLine(leaf ? ");" : "));");
            }
            EndStateLayoutType(code, owners);
            context.AddSource(StateLayoutHint(layout, "StateLayout"), SourceText.From(code.ToString(), Encoding.UTF8));
        }

        static void EmitStateLayoutFactories(GeneratorExecutionContext context, IEnumerable<IMethodSymbol> factories)
        {
            var compilation = (CSharpCompilation)context.Compilation;
            compilation = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
            var schema = new StateLayoutSchema(compilation);
            foreach (IMethodSymbol original in factories.OrderBy(Identity, StringComparer.Ordinal))
            {
                var declaration = (MethodDeclarationSyntax)original.DeclaringSyntaxReferences[0].GetSyntax();
                var factory = (IMethodSymbol)compilation.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration);
                if (!TryStateFactory(factory, out INamedTypeSymbol layout, out Member[] members, out string reason))
                {
                    ReportStateLayoutError(context, original, reason);
                    continue;
                }
                var identity = new StringBuilder();
                StateSchemaPart(identity, Identity(layout));
                if (!AppendStateMembersSchema(schema, identity, members, out reason))
                {
                    ReportStateLayoutError(context, original, reason);
                    continue;
                }
                string hash;
                using (SHA256 algorithm = SHA256.Create())
                    hash = string.Concat(algorithm.ComputeHash(Encoding.UTF8.GetBytes(identity.ToString()))
                        .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
                var code = new StringBuilder();
                var owners = BeginStateLayoutType(code, factory.ContainingType);
                code.Append(StateAccessibility(factory.DeclaredAccessibility)).Append(" static partial ")
                    .Append(TypeName(layout)).Append(' ').Append(Escape(factory.Name)).Append('(');
                code.Append(string.Join(", ", factory.Parameters.Select(parameter =>
                    TypeName(parameter.Type) + " " + Escape(parameter.Name))));
                code.Append(") => new ").Append(TypeName(layout)).Append("(\"").Append(hash).Append('"');
                foreach (IParameterSymbol parameter in factory.Parameters)
                    code.Append(", ").Append(Escape(parameter.Name));
                code.AppendLine(");");
                EndStateLayoutType(code, owners);
                context.AddSource(StateLayoutHint(factory.ContainingType, "StateLayoutFactory_" + factory.Name + "_" + hash),
                    SourceText.From(code.ToString(), Encoding.UTF8));
            }
        }

        static bool AppendStateMembersSchema(StateLayoutSchema schema, StringBuilder identity, Member[] members, out string reason)
        {
            reason = null;
            foreach (Member member in members)
            {
                StateSchemaPart(identity, member.Symbol.Name);
                var type = (INamedTypeSymbol)member.Type;
                if (IsStateRegion(member))
                {
                    if (!schema.Append(identity, type.TypeArguments[0], out reason))
                        return false;
                }
                else
                {
                    StateSchemaPart(identity, "layout");
                    StateSchemaPart(identity, Identity(type));
                    if (!TryStateMembers(type, false, out Member[] children, out reason) ||
                        !AppendStateMembersSchema(schema, identity, children, out reason))
                        return false;
                    StateSchemaPart(identity, "end-layout");
                }
            }
            return true;
        }

        static bool TryStateMembers(INamedTypeSymbol layout, bool declaration, out Member[] members, out string reason)
        {
            members = StorageMembers(layout).Where(member =>
                !HasAttribute(member.Symbol, "ThirdPersonSimulation.ValueStorageIgnoreAttribute")).ToArray();
            reason = null;
            if (!HasAttribute(layout, StateLayoutAttribute) || layout.TypeKind != TypeKind.Struct ||
                !layout.IsReadOnly || (declaration || layout.DeclaringSyntaxReferences.Length != 0) && !IsStatePartial(layout))
                reason = "区段布局必须在原 readonly partial struct 上声明 ValueStateLayout";
            else if (members.Length == 0)
                reason = "区段布局必须由原 OrchestrationStateRegion<T> 或 ValueStateLayout 成员声明子区";
            else if (members.Select(StateParameterName).Distinct(StringComparer.Ordinal).Count() != members.Length)
                reason = "原子区成员产生了重复的准备参数名称";
            else if (declaration && layout.GetMembers("Layout").Length != 0)
                reason = "Layout 由原子区生成，不能重复声明";
            else
                foreach (Member member in members)
                {
                    bool leaf = IsStateRegion(member);
                    if (!(member.Type is INamedTypeSymbol type) ||
                        (leaf ? !IsStateLayoutUnmanaged(type.TypeArguments[0]) : !HasAttribute(type, StateLayoutAttribute)))
                    {
                        reason = member.Symbol.Name + ": 子区必须使用 unmanaged 元素的 OrchestrationStateRegion<T> 或 ValueStateLayout";
                        break;
                    }
                    if (declaration && layout.GetMembers("Get" + member.Symbol.Name).Length != 0)
                    {
                        reason = member.Symbol.Name + ": typed 子区访问由原成员生成，不能重复声明";
                        break;
                    }
                    if (declaration && !leaf && layout.GetMembers(StateChildRegionName(member)).Length != 0)
                    {
                        reason = member.Symbol.Name + ": 子布局字节区由原成员生成，不能重复声明";
                        break;
                    }
                }
            return reason == null;
        }

        static bool TryStateFactory(IMethodSymbol factory, out INamedTypeSymbol layout,
            out Member[] members, out string reason)
        {
            layout = factory.ReturnType as INamedTypeSymbol;
            members = null;
            reason = null;
            if (!factory.IsStatic || factory.Arity != 0 || !factory.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax() is MethodDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                    declaration.Body == null && declaration.ExpressionBody == null) ||
                ContainsStateTypeParameter(factory.ContainingType) || !IsStatePartial(factory.ContainingType))
                reason = "区段准备入口必须是在原闭合类型上声明的非泛型 static partial 方法";
            else if (layout == null || !TryStateMembers(layout, false, out members, out reason))
                reason = reason ?? "区段准备入口必须返回 ValueStateLayout 原布局类型";
            else if (ContainsStateTypeParameter(layout))
                reason = "区段准备入口必须返回已闭合的实际布局类型，不能以泛型名称代替成员布局";
            else
            {
                Member[] factoryMembers = members;
                string[] types = { "ThirdPersonSimulation.StableHash", "ThirdPersonSimulation.NumericProfileId",
                    "ThirdPersonSimulation.TargetAbiVersion" };
                if (factory.Parameters.Length != types.Length + members.Length ||
                    factory.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
                    factory.Parameters.Take(types.Length).Where((parameter, index) => Identity(parameter.Type) != types[index]).Any() ||
                    factory.Parameters.Skip(types.Length).Where((parameter, index) =>
                        (IsStateRegion(factoryMembers[index]) ? parameter.Type.SpecialType != SpecialType.System_Int32 :
                            !SymbolEqualityComparer.Default.Equals(parameter.Type, factoryMembers[index].Type)) ||
                        parameter.Name != StateParameterName(factoryMembers[index])).Any())
                    reason = "准备参数依次为 contentIdentity、numericProfile、targetAbi，再按原成员名称顺序传入 xxxCapacity Int32 容量或 camelCase 子布局";
            }
            return reason == null;
        }

        static bool ContainsStateTypeParameter(ITypeSymbol type) => type is ITypeParameterSymbol ||
            type is INamedTypeSymbol named && (named.TypeArguments.Any(ContainsStateTypeParameter) ||
                named.ContainingType != null && ContainsStateTypeParameter(named.ContainingType));

        static bool IsStatePartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences.Length != 0 &&
            type.DeclaringSyntaxReferences.All(reference => reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        static bool IsStateRegion(Member member) => member.Type is INamedTypeSymbol type &&
            Identity(type.OriginalDefinition) == StateRegionType;

        static string StateParameterName(Member member) => char.ToLowerInvariant(member.Symbol.Name[0]) +
            member.Symbol.Name.Substring(1) + (IsStateRegion(member) ? "Capacity" : "");

        static string StateChildRegionName(Member member) => "m_" + member.Symbol.Name + "Region";

        static int BeginStateLayoutType(StringBuilder code, INamedTypeSymbol type)
        {
            if (!type.ContainingNamespace.IsGlobalNamespace)
                code.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(" {");
            var owners = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol owner = type.ContainingType; owner != null; owner = owner.ContainingType)
                owners.Push(owner);
            foreach (INamedTypeSymbol owner in owners)
                EmitStateTypeDeclaration(code, owner);
            EmitStateTypeDeclaration(code, type);
            return owners.Count + 1 + (type.ContainingNamespace.IsGlobalNamespace ? 0 : 1);
        }

        static void EndStateLayoutType(StringBuilder code, int depth)
        {
            for (int index = 0; index < depth; index++)
                code.AppendLine("}");
        }

        static string StateLayoutHint(INamedTypeSymbol type, string suffix) =>
            Identity(type).Replace('.', '_').Replace('<', '_').Replace('>', '_').Replace(',', '_') + "." + suffix + ".g.cs";

        static void EmitStateTypeDeclaration(StringBuilder code, INamedTypeSymbol type)
        {
            int start = code.Length;
            EmitTypeDeclaration(code, type);
            string accessibility = StateAccessibility(type.DeclaredAccessibility);
            code.Remove(start, type.DeclaredAccessibility == Accessibility.Public ? 6 : 8).Insert(start, accessibility);
            if (type.IsStatic)
                code.Insert(start + accessibility.Length, " static");
            if (type.IsRefLikeType)
                code.Insert(start + accessibility.Length, " ref");
        }

        static string StateAccessibility(Accessibility accessibility) => accessibility == Accessibility.Public ? "public" :
            accessibility == Accessibility.Private ? "private" : accessibility == Accessibility.Protected ? "protected" :
            accessibility == Accessibility.ProtectedAndInternal ? "private protected" :
            accessibility == Accessibility.ProtectedOrInternal ? "protected internal" : "internal";

        static void StateSchemaPart(StringBuilder schema, string part) =>
            schema.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part);

        static void ReportStateLayoutError(GeneratorExecutionContext context, ISymbol symbol, string reason) =>
            context.ReportDiagnostic(Diagnostic.Create(InvalidValueDeclaration, symbol.Locations.FirstOrDefault(), Identity(symbol), reason));
    }
}
