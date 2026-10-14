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
        sealed class PreparedReadPlan
        {
            public INamedTypeSymbol Storage;
            public IMethodSymbol Begin;
            public IMethodSymbol Complete;
            public Dictionary<string, IMethodSymbol> Collections;
            public Dictionary<string, IMethodSymbol> Nested;
        }

        static Dictionary<string, INamedTypeSymbol> PreparedStorages(Compilation compilation, Receiver receiver)
        {
            var result = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (INamedTypeSymbol owner in FormatterTypes(compilation.Assembly.GlobalNamespace).Concat(
                compilation.GetTypeByMetadataName("ThirdPersonSimulation.Serialization.MemoryPackContent") is INamedTypeSymbol content
                    ? new[] { content } : Array.Empty<INamedTypeSymbol>()))
                foreach (IMethodSymbol method in owner.GetMembers("DeserializeInto").OfType<IMethodSymbol>().Where(method =>
                    method.Arity == 0 && method.IsStatic && method.Parameters.Length == 3 &&
                    method.DeclaredAccessibility == Accessibility.Public &&
                    Identity(method.Parameters[0].Type) == "System.ReadOnlySpan<byte>" &&
                    Identity(method.Parameters[2].Type) == "ThirdPersonSimulation.Serialization.MemoryPackContentContext"))
                    result[Identity(method.ReturnType)] = (INamedTypeSymbol)method.Parameters[1].Type;
            foreach (var call in receiver.Calls)
            {
                var method = compilation.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol as IMethodSymbol;
                if (method != null && method.Name == "DeserializeInto" && method.TypeArguments.Length == 2 &&
                    Identity(method.ContainingType) == "ThirdPersonSimulation.Serialization.MemoryPackContent" &&
                    method.TypeArguments[0] is INamedTypeSymbol value && method.TypeArguments[1] is INamedTypeSymbol storage)
                    result[Identity(value)] = storage;
            }
            var pending = new Queue<INamedTypeSymbol>(result.Values);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count != 0)
            {
                INamedTypeSymbol storage = pending.Dequeue();
                if (!visited.Add(Identity(storage))) continue;
                foreach (IMethodSymbol prepare in storage.GetMembers().OfType<IMethodSymbol>().Where(method =>
                    !method.IsStatic && method.Parameters.Length == 0 &&
                    method.Name.StartsWith("PrepareRead", StringComparison.Ordinal) &&
                    method.ReturnType is INamedTypeSymbol))
                {
                    var nested = (INamedTypeSymbol)prepare.ReturnType;
                    foreach (IMethodSymbol complete in nested.GetMembers("CompleteRead").OfType<IMethodSymbol>().Where(method =>
                        !method.IsStatic && !method.ReturnsVoid && method.ReturnType is INamedTypeSymbol))
                        result[Identity(complete.ReturnType)] = nested;
                    pending.Enqueue(nested);
                }
            }
            return result;
        }

        static PreparedReadPlan CreatePreparedReadPlan(INamedTypeSymbol type, INamedTypeSymbol storage,
            Member[] members, Func<ISymbol, bool> isAccessible,
            Dictionary<string, INamedTypeSymbol> preparedStorages, out string reason)
        {
            reason = null;
            IMethodSymbol[] begin = storage.GetMembers("Begin").OfType<IMethodSymbol>().Where(method =>
                !method.IsStatic && method.Parameters.Length == 0 && method.ReturnsVoid && isAccessible(method)).ToArray();
            Member[] scalars = members.Where(member => SequenceElement(member.Type) == null).ToArray();
            IMethodSymbol[] complete = storage.GetMembers("CompleteRead").OfType<IMethodSymbol>().Where(method =>
                !method.IsStatic && isAccessible(method) && SymbolEqualityComparer.Default.Equals(method.ReturnType, type) &&
                method.Parameters.Length == scalars.Length && method.Parameters.All(parameter =>
                    scalars.Count(member => MatchesPreparedParameter(member, parameter)) == 1)).ToArray();
            if (begin.Length != 1 || complete.Length != 1)
            {
                reason = "prepared 原 owner 需要唯一 Begin() 与按原标量成员完成的 CompleteRead，不能退回原构造分配";
                return null;
            }
            var collections = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
            var nested = new Dictionary<string, IMethodSymbol>(StringComparer.Ordinal);
            foreach (Member member in members.Where(member => SequenceElement(member.Type) != null))
            {
                ITypeSymbol element = SequenceElement(member.Type);
                IMethodSymbol[] add = storage.GetMembers().OfType<IMethodSymbol>().Where(method =>
                    !method.IsStatic && method.ReturnsVoid && method.Name.StartsWith("Add", StringComparison.Ordinal) &&
                    isAccessible(method) && method.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, element)).ToArray();
                if (add.Length != 1)
                {
                    reason = "prepared 原集合 " + member.Name + " 没有按原元素类型唯一匹配的 Add 入口";
                    return null;
                }
                collections.Add(member.Name, add[0]);
            }
            foreach (Member member in members)
            {
                ITypeSymbol value = SequenceElement(member.Type) ?? member.Type;
                if (!preparedStorages.TryGetValue(Identity(value), out INamedTypeSymbol nestedStorage)) continue;
                IMethodSymbol[] prepare = storage.GetMembers().OfType<IMethodSymbol>().Where(method =>
                    !method.IsStatic && method.Parameters.Length == 0 &&
                    method.Name.StartsWith("PrepareRead", StringComparison.Ordinal) && isAccessible(method) &&
                    SymbolEqualityComparer.Default.Equals(method.ReturnType, nestedStorage)).ToArray();
                IMethodSymbol[] named = prepare.Where(method => string.Equals(method.Name,
                    "PrepareRead" + MemberKey(member.Name), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (named.Length == 1) prepare = named;
                if (prepare.Length != 1)
                {
                    reason = "prepared 原成员 " + member.Name + " 需要唯一返回所属存储的 PrepareRead 入口";
                    return null;
                }
                nested.Add(member.Name, prepare[0]);
            }
            return new PreparedReadPlan { Storage = storage, Begin = begin[0], Complete = complete[0], Collections = collections, Nested = nested };
        }

        static bool MatchesPreparedParameter(Member member, IParameterSymbol parameter) =>
            string.Equals(MemberKey(member.Name), MemberKey(parameter.Name), StringComparison.OrdinalIgnoreCase) &&
            CanPass(member.Type, parameter.Type);

        static bool DeclaredText(ISymbol symbol) => symbol.GetAttributes().Any(attribute =>
            Identity(attribute.AttributeClass) == "ThirdPersonSimulation.ValueTextRulesAttribute" &&
            attribute.NamedArguments.Any(argument => argument.Key == "Declared" && (bool)argument.Value.Value));

        static string ValidatePreparedMembers(INamedTypeSymbol root, PreparedReadPlan plan, ITypeSymbol text,
            Func<ITypeSymbol, ITypeSymbol, bool> implicitReference, HashSet<string> externalLeaves,
            Dictionary<string, PreparedReadPlan> preparedPlans)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            return Visit(root, plan);

            string Visit(ITypeSymbol type, PreparedReadPlan current)
            {
                if (type is INamedTypeSymbol namedType) type = RecordType(namedType);
                if (!visited.Add(Identity(type)) || type.TypeKind == TypeKind.Enum) return null;
                if (NullableElement(type) is ITypeSymbol nullable) return Visit(nullable, current);
                if (HasAttribute(type, TextAttribute))
                    return SymbolEqualityComparer.Default.Equals(type, text) ? null :
                        "prepared 文本 " + Identity(type) + " 缺少现有接收 context 的原文本存储合同";
                if (type.SpecialType != SpecialType.None) return null;
                if (preparedPlans.TryGetValue(Identity(type), out PreparedReadPlan nestedPlan)) current = nestedPlan;
                else if (type.IsReferenceType && !SymbolEqualityComparer.Default.Equals(type, root))
                    return "prepared 原成员 " + Identity(type) + " 需要自己的预备存储，不能在接收时构造对象";
                if (externalLeaves.Contains(Identity(type))) return null;
                if (!(type is INamedTypeSymbol named)) return null;
                foreach (Member member in ValueMembers(named, implicitReference))
                {
                    if (member.Type.SpecialType == SpecialType.System_String)
                    {
                        if (!DeclaredText(member.Symbol))
                            return "prepared 原 string " + Identity(named) + "." + member.Name +
                                " 未声明 ValueTextRules.Declared；接收不能新建 string";
                    }
                    else if (SequenceElement(member.Type) is ITypeSymbol element)
                    {
                        if (current == null || !SymbolEqualityComparer.Default.Equals(type, current.Complete.ReturnType) ||
                            !current.Collections.ContainsKey(member.Name))
                            return "prepared 原集合 " + Identity(named) + "." + member.Name +
                                " 缺少原 owner 的预备集合读取合同";
                        string reason = Visit(element, null);
                        if (reason != null) return reason;
                    }
                    else
                    {
                        string reason = Visit(member.Type, null);
                        if (reason != null) return reason;
                    }
                }
                return null;
            }
        }

        static void EmitPreparedRead(StringBuilder code, INamedTypeSymbol type, Member[] members,
            PreparedReadPlan plan, Func<ITypeSymbol, bool> hasPayload)
        {
            code.Append("public ").Append(TypeName(type)).Append(" ReadInto(ref global::MemoryPack.MemoryPackReader reader, ")
                .Append(TypeName(plan.Storage)).AppendLine(" storage) {");
            code.Append("storage.").Append(Escape(plan.Begin.Name)).AppendLine("();");
            code.AppendLine("if (!reader.TryReadObjectHeader(out byte count)) return default;");
            code.Append("if (count != ").Append(members.Length)
                .AppendLine(") throw new global::MemoryPack.MemoryPackSerializationException(\"记录成员数与当前格式不符。\");");
            foreach (Member member in members)
            {
                if (plan.Collections.TryGetValue(member.Name, out IMethodSymbol add))
                {
                    code.Append("if (!reader.TryReadCollectionHeader(out int count_").Append(member.Name)
                        .AppendLine(")) throw new global::MemoryPack.MemoryPackSerializationException(\"prepared 原输入集合不可为 null。\");");
                    code.Append("for (int index = 0; index < count_").Append(member.Name).AppendLine("; index++) {");
                    code.Append(TypeName(SequenceElement(member.Type))).AppendLine(" item = default;");
                    EmitPreparedValueRead(code, member, plan, "item", true, hasPayload);
                    code.Append("storage.").Append(Escape(add.Name)).AppendLine("(item); }");
                }
                else
                {
                    code.Append(TypeName(member.Type)).Append(" member_").Append(member.Name).AppendLine(" = default;");
                    EmitPreparedValueRead(code, member, plan, "member_" + member.Name, false, hasPayload);
                }
            }
            code.Append("return storage.").Append(Escape(plan.Complete.Name)).Append('(')
                .Append(string.Join(", ", plan.Complete.Parameters.Select(parameter =>
                    "member_" + members.Single(member => MatchesPreparedParameter(member, parameter)).Name)))
                .AppendLine("); }");
        }

        static void EmitPreparedValueRead(StringBuilder code, Member member, PreparedReadPlan plan, string target,
            bool element, Func<ITypeSymbol, bool> hasPayload)
        {
            if (plan.Nested.TryGetValue(member.Name, out IMethodSymbol prepare))
            {
                string valueType = TypeName(element ? SequenceElement(member.Type) : member.Type);
                code.Append(target).Append(" = ((global::ThirdPersonSimulation.Serialization.IMemoryPackPreparedFormatter<")
                    .Append(valueType).Append(", ").Append(TypeName(prepare.ReturnType)).Append(">)reader.GetFormatter<")
                    .Append(valueType).Append(">()).ReadInto(ref reader, storage.").Append(Escape(prepare.Name)).AppendLine("());");
            }
            else if (element)
                code.Append("reader.ReadValue(ref ").Append(target).AppendLine(");");
            else
                EmitMemberRead(code, member, target, false, hasPayload);
        }
    }
}
