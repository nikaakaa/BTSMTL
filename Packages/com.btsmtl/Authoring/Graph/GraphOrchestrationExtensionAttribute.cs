using System;
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
}
