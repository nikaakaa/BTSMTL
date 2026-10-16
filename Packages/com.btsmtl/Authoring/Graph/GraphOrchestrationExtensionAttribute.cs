using System;
using System.Reflection;
using BTSMTL.Orchestration;

namespace BTSMTL.Authoring.Graph
{
    public readonly struct GraphOrchestrationExtensionPortBinding
    {
        public GraphOrchestrationExtensionPortBinding(string authoringPortId, OrchestrationExtensionInputPort input)
        {
            AuthoringPortId = authoringPortId;
            RuntimePort = input.Declaration;
        }

        public GraphOrchestrationExtensionPortBinding(string authoringPortId, OrchestrationExtensionOutputPort output)
        {
            AuthoringPortId = authoringPortId;
            RuntimePort = output.Declaration;
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
                FieldInfo field = ContractOwner.GetField(MemberName, BindingFlags.Public | BindingFlags.Static);
                if (field == null || field.FieldType != typeof(OrchestrationExtensionDefinition))
                    throw new InvalidOperationException($"扩展声明 '{ContractOwner.FullName}.{MemberName}' 不是正式编排合同项。");
                return (OrchestrationExtensionDefinition)field.GetValue(null);
            }
        }
    }
}
