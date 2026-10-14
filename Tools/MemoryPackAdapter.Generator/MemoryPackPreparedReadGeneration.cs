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
        }

        static Dictionary<string, INamedTypeSymbol> PreparedStorages(Compilation compilation)
        {
            var result = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            INamedTypeSymbol content = compilation.GetTypeByMetadataName("ThirdPersonSimulation.Serialization.MemoryPackContent");
            if (content == null) return result;
            foreach (IMethodSymbol method in content.GetMembers("DeserializeInto").OfType<IMethodSymbol>().Where(method =>
                method.Arity == 0 && method.Parameters.Length == 3 && method.DeclaredAccessibility == Accessibility.Public))
                result.Add(Identity(method.ReturnType), (INamedTypeSymbol)method.Parameters[1].Type);
            return result;
        }

        static PreparedReadPlan CreatePreparedReadPlan(INamedTypeSymbol type, INamedTypeSymbol storage,
            Member[] members, Func<ISymbol, bool> isAccessible, out string reason)
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
            return new PreparedReadPlan { Storage = storage, Begin = begin[0], Complete = complete[0], Collections = collections };
        }

        static bool MatchesPreparedParameter(Member member, IParameterSymbol parameter) =>
            string.Equals(MemberKey(member.Name), MemberKey(parameter.Name), StringComparison.OrdinalIgnoreCase) &&
            CanPass(member.Type, parameter.Type);

        static bool DeclaredText(ISymbol symbol) => symbol.GetAttributes().Any(attribute =>
            Identity(attribute.AttributeClass) == "ThirdPersonSimulation.ValueTextRulesAttribute" &&
            attribute.NamedArguments.Any(argument => argument.Key == "Declared" && (bool)argument.Value.Value));

        static string ValidatePreparedMembers(INamedTypeSymbol root, PreparedReadPlan plan, ITypeSymbol text,
            Func<ITypeSymbol, ITypeSymbol, bool> implicitReference, HashSet<string> externalLeaves)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            return Visit(root);

            string Visit(ITypeSymbol type)
            {
                if (type is INamedTypeSymbol namedType) type = RecordType(namedType);
                if (!visited.Add(Identity(type)) || type.TypeKind == TypeKind.Enum) return null;
                if (NullableElement(type) is ITypeSymbol nullable) return Visit(nullable);
                if (HasAttribute(type, TextAttribute))
                    return SymbolEqualityComparer.Default.Equals(type, text) ? null :
                        "prepared 文本 " + Identity(type) + " 缺少现有接收 context 的原文本存储合同";
                if (type.SpecialType != SpecialType.None) return null;
                if (type.IsReferenceType && !SymbolEqualityComparer.Default.Equals(type, root))
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
                        if (!SymbolEqualityComparer.Default.Equals(type, root) || !plan.Collections.ContainsKey(member.Name))
                            return "prepared 原集合 " + Identity(named) + "." + member.Name +
                                " 缺少原 owner 的预备集合读取合同";
                        string reason = Visit(element);
                        if (reason != null) return reason;
                    }
                    else
                    {
                        string reason = Visit(member.Type);
                        if (reason != null) return reason;
                    }
                }
                return null;
            }
        }

        static void EmitPreparedRead(StringBuilder code, INamedTypeSymbol type, Member[] members,
            PreparedReadPlan plan, Func<ITypeSymbol, bool> hasPayload)
        {
            code.Append("static ").Append(TypeName(type)).Append(" ReadInto(ref global::MemoryPack.MemoryPackReader reader, ")
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
                    code.AppendLine("reader.ReadValue(ref item);");
                    code.Append("storage.").Append(Escape(add.Name)).AppendLine("(item); }");
                }
                else
                {
                    code.Append(TypeName(member.Type)).Append(" member_").Append(member.Name).AppendLine(" = default;");
                    EmitMemberRead(code, member, "member_" + member.Name, false, hasPayload);
                }
            }
            code.Append("return storage.").Append(Escape(plan.Complete.Name)).Append('(')
                .Append(string.Join(", ", plan.Complete.Parameters.Select(parameter =>
                    "member_" + members.Single(member => MatchesPreparedParameter(member, parameter)).Name)))
                .AppendLine("); }");
        }
    }
}
