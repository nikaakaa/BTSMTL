using System;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationExtensionKind : byte
    {
        Action = 1,
        Condition = 2,
        Value = 3
    }

    public enum OrchestrationExtensionAccess : byte
    {
        ReadOnly = 1,
        CandidateWrite = 2
    }

    [Flags]
    public enum OrchestrationExtensionInvocationKind : byte
    {
        Evaluate = 1,
        Execute = 2,
        RequestStop = 4,
        ContinueStop = 8,
        ForceStop = 16
    }

    public enum OrchestrationExtensionResult : byte
    {
        Running = 1,
        Success = 2,
        Failure = 3
    }

    public enum OrchestrationExtensionPortDirection : byte
    {
        Input = 1,
        Output = 2
    }

    public readonly struct OrchestrationExtensionPort
    {
        public OrchestrationExtensionPort(string id, string valueTypeId,
            OrchestrationExtensionPortDirection direction)
        {
            Id = id;
            ValueTypeId = valueTypeId;
            Direction = direction;
        }

        public string Id { get; }
        public string ValueTypeId { get; }
        public OrchestrationExtensionPortDirection Direction { get; }
    }

    public readonly struct OrchestrationExtensionContract
    {
        public OrchestrationExtensionContract(OrchestrationExtensionKind kind,
            OrchestrationExtensionAccess access,
            OrchestrationExtensionInvocationKind invocations,
            ReadOnlyMemory<OrchestrationExtensionPort> ports)
        {
            Kind = kind;
            Access = access;
            Invocations = invocations;
            Ports = ports;
        }

        public OrchestrationExtensionKind Kind { get; }
        public OrchestrationExtensionAccess Access { get; }
        public OrchestrationExtensionInvocationKind Invocations { get; }
        public ReadOnlyMemory<OrchestrationExtensionPort> Ports { get; }
    }
}
