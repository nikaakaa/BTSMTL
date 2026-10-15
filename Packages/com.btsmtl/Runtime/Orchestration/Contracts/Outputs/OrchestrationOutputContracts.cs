using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationOutputPort
    {
        public OrchestrationOutputPort(string id, OrchestrationValueKind valueKind)
        {
            Id = id;
            ValueKind = valueKind;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
    }

    public readonly struct OrchestrationOutputTemplate
    {
        public OrchestrationOutputTemplate(string contractIdentity, uint semanticVersion,
            ReadOnlyMemory<OrchestrationOutputPort> ports)
        {
            ContractIdentity = contractIdentity;
            SemanticVersion = semanticVersion;
            Ports = ports;
        }

        public string ContractIdentity { get; }
        public uint SemanticVersion { get; }
        public ReadOnlyMemory<OrchestrationOutputPort> Ports { get; }
    }

    public readonly struct OrchestrationOutputRequest
    {
        public OrchestrationOutputRequest(int operationIndex, ulong operationGeneration,
            ulong instanceGeneration, int templateIndex, OrchestrationRange values,
            bool hasTimelineInvocation, OrchestrationTimelineInvocationToken timelineInvocation,
            OrchestrationTimelineHook timelineHook)
        {
            OperationIndex = operationIndex;
            OperationGeneration = operationGeneration;
            InstanceGeneration = instanceGeneration;
            TemplateIndex = templateIndex;
            Values = values;
            HasTimelineInvocation = hasTimelineInvocation;
            TimelineInvocation = timelineInvocation;
            TimelineHook = timelineHook;
        }

        public int OperationIndex { get; }
        public ulong OperationGeneration { get; }
        public ulong InstanceGeneration { get; }
        public int TemplateIndex { get; }
        public OrchestrationRange Values { get; }
        public bool HasTimelineInvocation { get; }
        public OrchestrationTimelineInvocationToken TimelineInvocation { get; }
        public OrchestrationTimelineHook TimelineHook { get; }
    }
}
