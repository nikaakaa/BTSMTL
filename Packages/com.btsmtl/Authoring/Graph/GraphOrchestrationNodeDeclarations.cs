using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using BTSMTL.Orchestration;

namespace BTSMTL.Authoring.Graph
{
    public readonly struct GraphOrchestrationExtensionPortBinding
    {
        public GraphOrchestrationExtensionPortBinding(string authoringPortId, IOrchestrationExtensionPort port)
        {
            AuthoringPortId = authoringPortId;
            RuntimePort = port.Declaration;
        }

        public string AuthoringPortId { get; }
        public OrchestrationExtensionPort RuntimePort { get; }
    }

    // 扩展节点执行的扩展操作，以及作者端口到扩展端口的映射。
    public readonly struct GraphOrchestrationExtensionContract
    {
        public GraphOrchestrationExtensionContract(OrchestrationExtensionDefinition definition,
            IReadOnlyList<GraphOrchestrationExtensionPortBinding> ports)
        {
            Definition = definition;
            Ports = ports;
        }

        public OrchestrationExtensionDefinition Definition { get; }
        public IReadOnlyList<GraphOrchestrationExtensionPortBinding> Ports { get; }
    }

    // 宿主按节点类给出编排声明；声明数据本身由宿主按能力维护，独立编译不经节点类直接读取。
    public interface IGraphOrchestrationDeclarationSource
    {
        GraphOrchestrationNodeDeclaration Declare(Type nodeType);
    }

    // 节点类所在程序集以此标明编排声明来源；没有来源的程序集，其节点没有编排声明。
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class GraphOrchestrationDeclarationsAttribute : Attribute
    {
        public GraphOrchestrationDeclarationsAttribute(Type source) => Source = source;

        public Type Source { get; }
    }

    // 节点的编排声明：宿主输入、编排输出或扩展操作三者至多其一。
    public sealed class GraphOrchestrationNodeDeclaration
    {
        public static readonly GraphOrchestrationNodeDeclaration None = new(null, null, null);

        static readonly ConcurrentDictionary<Assembly, IGraphOrchestrationDeclarationSource> s_Sources = new();
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

        public static GraphOrchestrationNodeDeclaration ForHostInputs(IReadOnlyList<GraphOrchestrationHostInputPort> inputs) =>
            new(inputs ?? throw new ArgumentNullException(nameof(inputs)), null, null);

        public static GraphOrchestrationNodeDeclaration ForOutput(OrchestrationOutputTemplate output) => new(null, output, null);

        public static GraphOrchestrationNodeDeclaration ForExtension(IOrchestrationExtensionDefinition definition,
            params GraphOrchestrationExtensionPortBinding[] ports) =>
            new(null, null, new GraphOrchestrationExtensionContract(definition.Declaration, Array.AsReadOnly(ports)));

        public static GraphOrchestrationNodeDeclaration Of(Type nodeType) => s_Declarations.GetOrAdd(nodeType, type =>
        {
            IGraphOrchestrationDeclarationSource source = s_Sources.GetOrAdd(type.Assembly, assembly =>
                assembly.GetCustomAttribute<GraphOrchestrationDeclarationsAttribute>() is { } attribute
                    ? (IGraphOrchestrationDeclarationSource)Activator.CreateInstance(attribute.Source) : null);
            return source?.Declare(type) ?? None;
        });

        // 扩展端口按作者端口映射到运行端口；未映射的作者端口（常量输出）保持原身份，由编译合同核对。
        public string RuntimePort(string port, OrchestrationExtensionPortDirection direction)
        {
            if (Extension is not { } extension)
                return port;
            foreach (GraphOrchestrationExtensionPortBinding binding in extension.Ports)
                if (binding.AuthoringPortId == port && binding.RuntimePort.Direction == direction)
                    return binding.RuntimePort.Id;
            return port;
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
