using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ThirdPerson.ValueGeneration.ValueModel;

namespace ThirdPerson.MemoryPackAdapters
{
    public sealed partial class MemoryPackAdapterGenerator
    {
        const string LogicalStorageAttribute = "ThirdPersonSimulation.ValueLogicalStorageAttribute";

        sealed class LogicalStoragePlan
        {
            public INamedTypeSymbol Type;
            public IMethodSymbol Enumeration;
            public ISymbol Count;
            public IMethodSymbol MoveNext;
            public IMethodSymbol Restore;
            public Member[] Entries;
            public Member Presence;
        }

        static bool HasLogicalStorage(INamedTypeSymbol type) => type.GetMembers().OfType<IMethodSymbol>()
            .Any(method => HasAttribute(method, LogicalStorageAttribute));

        static IEnumerable<INamedTypeSymbol> LogicalStorageTypes(Compilation compilation)
        {
            INamedTypeSymbol purpose = compilation.GetTypeByMetadataName(LogicalStorageAttribute);
            if (purpose == null) return Array.Empty<INamedTypeSymbol>();
            var result = new SortedDictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols.Concat(new[] { compilation.Assembly })
                .Where(assembly => SymbolEqualityComparer.Default.Equals(assembly, purpose.ContainingAssembly) ||
                    assembly.Modules.Any(module => module.ReferencedAssemblySymbols.Any(reference =>
                        SymbolEqualityComparer.Default.Equals(reference, purpose.ContainingAssembly)))))
                foreach (INamedTypeSymbol type in FormatterTypes(assembly.GlobalNamespace))
                    foreach (ISymbol member in type.GetMembers())
                        if (member is IFieldSymbol field) Visit(field.Type);
                        else if (member is IPropertySymbol property) Visit(property.Type);
                        else if (member is IMethodSymbol method)
                        {
                            Visit(method.ReturnType);
                            foreach (IParameterSymbol parameter in method.Parameters) Visit(parameter.Type);
                        }
            return result.Values;

            void Visit(ITypeSymbol type)
            {
                if (!visited.Add(Identity(type))) return;
                if (type is IArrayTypeSymbol array) { Visit(array.ElementType); return; }
                if (!(type is INamedTypeSymbol named)) return;
                if (!ContainsParameter(named) && HasLogicalStorage(named)) result.Add(Identity(named), named);
                foreach (ITypeSymbol argument in named.TypeArguments) Visit(argument);
            }

            bool ContainsParameter(ITypeSymbol type) => type is ITypeParameterSymbol ||
                type is IArrayTypeSymbol array && ContainsParameter(array.ElementType) ||
                type is INamedTypeSymbol named && (named.TypeArguments.Any(ContainsParameter) ||
                    named.ContainingType != null && ContainsParameter(named.ContainingType));
        }

        static LogicalStoragePlan CreateLogicalStoragePlan(INamedTypeSymbol type,
            Func<ISymbol, bool> isAccessible, out string reason)
        {
            reason = null;
            IMethodSymbol[] enumerations = type.GetMembers().OfType<IMethodSymbol>().Where(method =>
                HasAttribute(method, LogicalStorageAttribute)).ToArray();
            if (enumerations.Length != 1 || enumerations[0].IsStatic || enumerations[0].Parameters.Length != 0 ||
                !isAccessible(enumerations[0]) || !(enumerations[0].ReturnType is INamedTypeSymbol enumerator) ||
                !enumerator.IsValueType)
            {
                reason = "逻辑存储需要唯一、无参数的原值类型遍历入口";
                return null;
            }
            IMethodSymbol enumeration = enumerations[0];
            AttributeData purpose = enumeration.GetAttributes().Single(attribute => Identity(attribute.AttributeClass) == LogicalStorageAttribute);
            ISymbol[] counts = type.GetMembers((string)purpose.ConstructorArguments[0].Value).Where(member =>
                !member.IsStatic && isAccessible(member) &&
                (member is IPropertySymbol property && !property.IsIndexer && property.GetMethod != null && isAccessible(property.GetMethod) &&
                    property.Type.SpecialType == SpecialType.System_Int32 ||
                 member is IFieldSymbol field && field.Type.SpecialType == SpecialType.System_Int32)).ToArray();
            IMethodSymbol[] moves = enumerator.GetMembers("MoveNext").OfType<IMethodSymbol>().Where(method =>
                !method.IsStatic && isAccessible(method) && method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                method.Parameters.Length != 0 && method.Parameters.All(parameter => parameter.RefKind == RefKind.Out)).ToArray();
            if (counts.Length != 1 || moves.Length != 1 || moves[0].Parameters.Length > 249)
            {
                reason = "逻辑存储需要原 int 数量成员及唯一 MoveNext(out ...) 记录形状";
                return null;
            }
            Member[] entries = moves[0].Parameters.Select(parameter => new Member { Symbol = parameter, Type = parameter.Type }).ToArray();
            Member[] presences = entries.Where(member => HasAttribute(member.Symbol, PresenceAttribute)).ToArray();
            if (presences.Length > 1 || presences.Any(member => member.Type.SpecialType != SpecialType.System_Boolean) ||
                presences.Length == 1 && entries.Any(member => HasAttribute(member.Symbol, PayloadAttribute) &&
                    Array.IndexOf(entries, member) < Array.IndexOf(entries, presences[0])))
            {
                reason = "逻辑记录的 ValuePresence 必须是唯一 bool，并在可选 ValuePayload 之前提供";
                return null;
            }
            IMethodSymbol[] restores = type.GetMembers((string)purpose.ConstructorArguments[1].Value).OfType<IMethodSymbol>()
                .Where(method => !method.IsStatic && method.ReturnsVoid && isAccessible(method) &&
                    method.Parameters.Length == entries.Length - presences.Length &&
                    method.Parameters.All(parameter => parameter.RefKind != RefKind.Out && parameter.RefKind != RefKind.Ref &&
                        entries.Count(member => !presences.Contains(member) && MatchesPreparedParameter(member, parameter)) == 1)).ToArray();
            if (restores.Length != 1)
            {
                reason = "逻辑存储写回入口必须直接消费原 MoveNext 的非 Presence 输出";
                return null;
            }
            return new LogicalStoragePlan { Type = type, Enumeration = enumeration, Count = counts[0], MoveNext = moves[0],
                Restore = restores[0], Entries = entries, Presence = presences.SingleOrDefault() };
        }

        static void EmitLogicalStorageFormatter(StringBuilder code, LogicalStoragePlan plan, int ordinal,
            bool scopedReferences, Func<ITypeSymbol, bool> hasPayload, string schemaHash)
        {
            string name = TypeName(plan.Type);
            EmitMemoryPackFormatterHeader(code, "Formatter" + ordinal, name, schemaHash, name);
            EmitMemoryPackSerializeDeclaration(code, name, scopedReferences);
            EmitPartContext(code, "writer", true);
            code.Append("writer.WriteCollectionHeader(value.").Append(Escape(plan.Count.Name)).AppendLine(");");
            code.Append("var entries = value.").Append(Escape(plan.Enumeration.Name)).AppendLine("();");
            code.Append("while (entries.").Append(Escape(plan.MoveNext.Name)).Append('(')
                .Append(string.Join(", ", plan.Entries.Select(member => "out " + TypeName(member.Type) + " entry_" + member.Name)))
                .AppendLine(")) {");
            EmitObjectHeader(code, "writer.WriteObjectHeader", plan.Entries, true, hasPayload);
            foreach (Member member in plan.Entries)
            {
                if (plan.Presence != null && HasAttribute(member.Symbol, PayloadAttribute))
                    code.Append("if (entry_").Append(plan.Presence.Name).AppendLine(") {");
                EmitMemberWrite(code, member, "entry_" + member.Name, true, hasPayload);
                if (plan.Presence != null && HasAttribute(member.Symbol, PayloadAttribute)) code.AppendLine("}");
            }
            code.AppendLine("} }");
            EmitMemoryPackDeserializeDeclaration(code, name, scopedReferences);
            code.AppendLine("throw new global::MemoryPack.MemoryPackSerializationException(\"逻辑存储须通过原 owner 的准备页读取。\"); }");
            code.Append("public ").Append(name).Append(" ReadInto(ref global::MemoryPack.MemoryPackReader reader, ")
                .Append(name).AppendLine(" storage) {");
            code.Append("if (!reader.TryReadCollectionHeader(out int count) || count != storage.").Append(Escape(plan.Count.Name))
                .AppendLine(") throw new global::MemoryPack.MemoryPackSerializationException(\"逻辑存储数量与原准备页不符。\");");
            code.AppendLine("for (int index = 0; index < count; index++) {");
            code.Append("if (!reader.TryReadObjectHeader(out byte memberCount) || memberCount != ").Append(plan.Entries.Length)
                .AppendLine(") throw new global::MemoryPack.MemoryPackSerializationException(\"逻辑记录成员数与当前格式不符。\");");
            foreach (Member member in plan.Entries)
            {
                code.Append(TypeName(member.Type)).Append(" entry_").Append(member.Name).AppendLine(" = default;");
                if (plan.Presence != null && HasAttribute(member.Symbol, PayloadAttribute))
                    code.Append("if (entry_").Append(plan.Presence.Name).AppendLine(") {");
                EmitMemberRead(code, member, "entry_" + member.Name, false, hasPayload);
                if (plan.Presence != null && HasAttribute(member.Symbol, PayloadAttribute)) code.AppendLine("}");
            }
            if (plan.Presence != null) code.Append("if (entry_").Append(plan.Presence.Name).AppendLine(")");
            code.Append("storage.").Append(Escape(plan.Restore.Name)).Append('(')
                .Append(string.Join(", ", plan.Restore.Parameters.Select(parameter => "entry_" + plan.Entries.Single(member =>
                    MatchesPreparedParameter(member, parameter)).Name))).AppendLine(");");
            code.AppendLine("} return storage; } }");
        }
    }
}
