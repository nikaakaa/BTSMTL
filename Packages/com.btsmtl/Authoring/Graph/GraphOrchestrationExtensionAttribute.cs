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

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class GraphOrchestrationExtensionAttribute : Attribute
    {
        public GraphOrchestrationExtensionAttribute(Type contractOwner, string memberName)
        {
            ContractOwner = contractOwner;
            MemberName = memberName;
        }

        public Type ContractOwner { get; }
        public string MemberName { get; }

        public OrchestrationExtensionDefinition Definition
        {
            get
            {
                Type operation = ContractOwner.GetNestedType(MemberName, BindingFlags.Public);
                FieldInfo field = operation?.GetField("Definition", BindingFlags.Public | BindingFlags.Static);
                if (field?.GetValue(null) is not IOrchestrationExtensionDefinition definition)
                    throw new InvalidOperationException($"扩展声明 '{ContractOwner.FullName}.{MemberName}' 不是正式编排合同项。");
                return definition.Declaration;
            }
        }
    }

    // 领域节点以 GraphOrchestrationExtension 标明执行它的扩展，以静态 RuntimeExtensionPorts 映射作者端口与扩展端口。
    public static class GraphOrchestrationExtensionNodes
    {
        public const string PortsPropertyName = "RuntimeExtensionPorts";

        public readonly struct Contract
        {
            internal Contract(OrchestrationExtensionDefinition definition, IReadOnlyList<GraphOrchestrationExtensionPortBinding> ports)
            {
                Definition = definition;
                Ports = ports;
            }

            public OrchestrationExtensionDefinition Definition { get; }
            public IReadOnlyList<GraphOrchestrationExtensionPortBinding> Ports { get; }
        }

        static readonly ConcurrentDictionary<Type, Contract?> s_Contracts = new();

        public static bool TryGet(Type nodeType, out Contract contract)
        {
            Contract? result = s_Contracts.GetOrAdd(nodeType, Read);
            contract = result.GetValueOrDefault();
            return result.HasValue;
        }

        static Contract? Read(Type type)
        {
            GraphOrchestrationExtensionAttribute attribute = type.GetCustomAttribute<GraphOrchestrationExtensionAttribute>();
            if (attribute == null)
                return null;
            PropertyInfo property = type.GetProperty(PortsPropertyName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (property == null)
                throw new InvalidOperationException($"作者能力 '{type.FullName}' 未声明正式扩展端口映射。");
            return new Contract(attribute.Definition, (IReadOnlyList<GraphOrchestrationExtensionPortBinding>)property.GetValue(null));
        }
    }
}
