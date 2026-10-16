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
        public OrchestrationExtensionPort(OrchestrationExtensionInputPort input)
        {
            Id = input.Id;
            ValueKind = input.ValueKind;
            Direction = OrchestrationExtensionPortDirection.Input;
            Index = input.Index;
        }

        public OrchestrationExtensionPort(OrchestrationExtensionOutputPort output)
        {
            Id = output.Id;
            ValueKind = output.ValueKind;
            Direction = OrchestrationExtensionPortDirection.Output;
            Index = output.Index;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public OrchestrationExtensionPortDirection Direction { get; }
        public int Index { get; }
    }

    public readonly struct OrchestrationExtensionInputPort
    {
        public OrchestrationExtensionInputPort(string id, OrchestrationValueKind valueKind, int index)
        {
            Id = id;
            ValueKind = valueKind;
            Index = index;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public int Index { get; }
        public OrchestrationExtensionPort Declaration => new OrchestrationExtensionPort(this);
    }

    public readonly struct OrchestrationExtensionOutputPort
    {
        public OrchestrationExtensionOutputPort(string id, OrchestrationValueKind valueKind, int index)
        {
            Id = id;
            ValueKind = valueKind;
            Index = index;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public int Index { get; }
        public OrchestrationExtensionPort Declaration => new OrchestrationExtensionPort(this);
    }

    public readonly struct OrchestrationExtensionContract
    {
        public OrchestrationExtensionContract(OrchestrationExtensionKind kind,
            OrchestrationExtensionAccess access,
            OrchestrationExtensionInvocationKind invocations,
            ReadOnlyMemory<OrchestrationExtensionPort> ports,
            ReadOnlyMemory<OrchestrationExtensionContentField> fields)
        {
            Kind = kind;
            Access = access;
            Invocations = invocations;
            Ports = ports;
            Fields = fields;
        }

        public OrchestrationExtensionKind Kind { get; }
        public OrchestrationExtensionAccess Access { get; }
        public OrchestrationExtensionInvocationKind Invocations { get; }
        public ReadOnlyMemory<OrchestrationExtensionPort> Ports { get; }
        public ReadOnlyMemory<OrchestrationExtensionContentField> Fields { get; }
    }
}
