using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using BTSMTL.Orchestration;

namespace BTSMTL.Authoring.Graph
{
    // 节点执行的扩展操作；作者端口 identity 即合同端口 identity。
    public readonly struct GraphOrchestrationExtensionContract
    {
        public GraphOrchestrationExtensionContract(OrchestrationExtensionDefinition definition) => Definition = definition;

        public OrchestrationExtensionDefinition Definition { get; }
    }

    // 编排输出节点：Member 用 nameof 引用 Owner 上类型为 OrchestrationOutputTemplate 的静态成员。
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class GraphNodeOutputAttribute : Attribute
    {
        public GraphNodeOutputAttribute(Type owner, string member)
        {
            Owner = owner;
            Member = member;
        }

        public Type Owner { get; }
        public string Member { get; }
    }

    // 宿主输入节点：Member 用 nameof 引用 Owner 上类型为宿主输入端口列表的静态成员。
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class GraphNodeHostInputsAttribute : Attribute
    {
        public GraphNodeHostInputsAttribute(Type owner, string member)
        {
            Owner = owner;
            Member = member;
        }

        public Type Owner { get; }
        public string Member { get; }
    }

    // 节点的编排声明：宿主输入、编排输出或扩展操作三者至多其一，由节点类型上的特性决定。
    public sealed class GraphOrchestrationNodeDeclaration
    {
        public static readonly GraphOrchestrationNodeDeclaration None = new(null, null, null);

        static readonly ConcurrentDictionary<Type, GraphOrchestrationNodeDeclaration> s_Declarations = new();

        GraphOrchestrationNodeDeclaration(IReadOnlyList<GraphOrchestrationHostInputPort> hostInputs,
            OrchestrationOutputTemplate? output, GraphOrchestrationExtensionContract? extension)
        {
            HostInputs = hostInputs;
            Output = output;
            Extension = extension;
        }

        public IReadOnlyList<GraphOrchestrationHostInputPort> HostInputs { get; }
        public OrchestrationOutputTemplate? Output { get; }
        public GraphOrchestrationExtensionContract? Extension { get; }

        // 扩展节点取扩展身份的 DomainId，编排输出节点取输出合同身份；图根按此准入节点，宿主按同一身份提供领域包。
        public string DomainId => Extension is { } extension ? extension.Definition.Identity.DomainId
            : Output is { } output ? output.ContractIdentity : string.Empty;

        // 未声明编排特性的节点类型（含原生数值与宏端点的 null）没有编排声明。
        public static GraphOrchestrationNodeDeclaration Of(Type nodeType) =>
            nodeType == null ? None : s_Declarations.GetOrAdd(nodeType, Create);

        static GraphOrchestrationNodeDeclaration Create(Type type)
        {
            var extension = type.GetCustomAttribute<GraphNodeExtensionAttribute>(true);
            var output = type.GetCustomAttribute<GraphNodeOutputAttribute>(true);
            var hostInputs = type.GetCustomAttribute<GraphNodeHostInputsAttribute>(true);
            if ((extension != null ? 1 : 0) + (output != null ? 1 : 0) + (hostInputs != null ? 1 : 0) > 1)
                throw new InvalidOperationException($"节点 '{type.FullName}' 只能声明宿主输入、编排输出或扩展操作之一。");
            if (extension != null)
                return new GraphOrchestrationNodeDeclaration(null, null,
                    new GraphOrchestrationExtensionContract(GraphNodeContracts.Definition(type).Declaration));
            if (output != null)
                return new GraphOrchestrationNodeDeclaration(null,
                    Static<OrchestrationOutputTemplate>(type, output.Owner, output.Member), null);
            if (hostInputs != null)
                return new GraphOrchestrationNodeDeclaration(
                    Static<IReadOnlyList<GraphOrchestrationHostInputPort>>(type, hostInputs.Owner, hostInputs.Member), null, null);
            return None;
        }

        static T Static<T>(Type nodeType, Type owner, string member)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
            object value = owner.GetProperty(member, flags)?.GetValue(null) ?? owner.GetField(member, flags)?.GetValue(null);
            return value is T typed ? typed : throw new InvalidOperationException(
                $"节点 '{nodeType.FullName}' 引用的 '{owner.FullName}.{member}' 不是 {typeof(T).Name}。");
        }
    }

    public static class GraphNodeDomains
    {
        public static bool TryGet(Type nodeType, out string domainId)
        {
            domainId = GraphOrchestrationNodeDeclaration.Of(nodeType).DomainId;
            return domainId.Length != 0;
        }
    }
}
