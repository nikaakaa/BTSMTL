using System;
using BTSMTL.Orchestration;

namespace BTSMTL.Graphs
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BtsmtlGraphOperationAttribute : Attribute
    {
        public BtsmtlGraphOperationAttribute(OrchestrationOperationCode code) => Code = code;
        public OrchestrationOperationCode Code { get; }
    }

    public interface IBtsmtlGraphOperationSource
    {
        int OperationVariant { get; }
    }

    public interface IBtsmtlVariableFlowNode : BTSMTL.Authoring.Graph.IGraphNodeVariableAccess
    {
        Type ValueType { get; }
        bool Writes { get; }
        void ConfigureVariable(string ownerId, string variableId);
    }

    public interface IBtsmtlHostInputFlowNode
    {
        string InputId { get; }
        Type ValueType { get; }
        void ConfigureInput(string inputId);
    }
}
