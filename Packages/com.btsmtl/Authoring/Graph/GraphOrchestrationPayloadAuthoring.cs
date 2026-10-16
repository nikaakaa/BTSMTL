using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Authoring.Graph
{
    public interface IGraphOrchestrationPayloadAuthoring
    {
        IReadOnlyList<KeyValuePair<string, object>> DescribeOrchestrationPayload();
    }

    public interface IGraphOrchestrationOutputAuthoring : IGraphOrchestrationPayloadAuthoring
    {
        OrchestrationOutputTemplate OutputTemplate { get; }
    }

    public interface IGraphOrchestrationHostInputAuthoring
    {
        IReadOnlyList<GraphOrchestrationHostInputPort> HostInputs { get; }
    }

    public readonly struct GraphOrchestrationBlackboardPort
    {
        public GraphOrchestrationBlackboardPort(string portId, string ownerId, string declarationId,
            OrchestrationValueKind valueKind, string valueTypeIdentity)
        {
            PortId = portId;
            OwnerId = ownerId;
            DeclarationId = declarationId;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string PortId { get; }
        public string OwnerId { get; }
        public string DeclarationId { get; }
        public OrchestrationValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    public interface IGraphOrchestrationBlackboardAuthoring
    {
        IReadOnlyList<GraphOrchestrationBlackboardPort> ReadBindings { get; }
        IReadOnlyList<GraphOrchestrationBlackboardPort> WriteBindings { get; }
    }

    public readonly struct GraphOrchestrationConstantPort
    {
        public GraphOrchestrationConstantPort(string portId, string payloadFieldName,
            OrchestrationValueKind valueKind, string valueTypeIdentity)
        {
            PortId = portId;
            PayloadFieldName = payloadFieldName;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string PortId { get; }
        public string PayloadFieldName { get; }
        public OrchestrationValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    public interface IGraphOrchestrationConstantPortAuthoring
    {
        IReadOnlyList<GraphOrchestrationConstantPort> ConstantInputs { get; }
        IReadOnlyList<GraphOrchestrationConstantPort> ConstantOutputs { get; }
    }

    public enum GraphOrchestrationDerivedInputSource : byte
    {
        SelfOutput = 1,
        StateCompletionTransition = 2
    }

    public readonly struct GraphOrchestrationDerivedInput
    {
        public GraphOrchestrationDerivedInput(string portId, GraphOrchestrationDerivedInputSource source,
            int sourceOutputPortIndex, OrchestrationValueKind valueKind, string valueTypeIdentity)
        {
            PortId = portId;
            Source = source;
            SourceOutputPortIndex = sourceOutputPortIndex;
            ValueKind = valueKind;
            ValueTypeIdentity = valueTypeIdentity;
        }

        public string PortId { get; }
        public GraphOrchestrationDerivedInputSource Source { get; }
        public int SourceOutputPortIndex { get; }
        public OrchestrationValueKind ValueKind { get; }
        public string ValueTypeIdentity { get; }
    }

    public interface IGraphOrchestrationDerivedInputAuthoring
    {
        IReadOnlyList<GraphOrchestrationDerivedInput> DerivedInputs { get; }
    }

    public interface IGraphOrchestrationRequiredInputAuthoring
    {
        IReadOnlyList<string> RequiredInputConnections { get; }
    }
}
