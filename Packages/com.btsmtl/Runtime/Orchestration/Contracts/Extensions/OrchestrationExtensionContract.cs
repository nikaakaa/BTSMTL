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

    public enum OrchestrationExtensionCompletion : byte
    {
        Immediate = 1,
        Resumable = 2
    }

    public enum OrchestrationExtensionPortDirection : byte
    {
        Input = 1,
        Output = 2
    }

    public readonly struct OrchestrationExtensionPort
    {
        public OrchestrationExtensionPort(string id, OrchestrationValueKind valueKind,
            OrchestrationExtensionPortDirection direction, int index)
        {
            Id = id;
            ValueKind = valueKind;
            Direction = direction;
            Index = index;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public OrchestrationExtensionPortDirection Direction { get; }
        public int Index { get; }
    }

    public interface IOrchestrationExtensionPort
    {
        OrchestrationExtensionPort Declaration { get; }
    }

    public readonly struct OrchestrationExtensionInputPort<TOperation> : IOrchestrationExtensionPort
        where TOperation : struct
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
        public OrchestrationExtensionPort Declaration => new OrchestrationExtensionPort(
            Id, ValueKind, OrchestrationExtensionPortDirection.Input, Index);
    }

    public readonly struct OrchestrationExtensionOutputPort<TOperation> : IOrchestrationExtensionPort
        where TOperation : struct
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
        public OrchestrationExtensionPort Declaration => new OrchestrationExtensionPort(
            Id, ValueKind, OrchestrationExtensionPortDirection.Output, Index);
    }

    public readonly struct OrchestrationExtensionContract
    {
        public OrchestrationExtensionContract(OrchestrationExtensionKind kind,
            OrchestrationExtensionAccess access,
            OrchestrationExtensionInvocationKind invocations,
            OrchestrationExtensionCompletion completion,
            ReadOnlyMemory<OrchestrationExtensionPort> ports,
            ReadOnlyMemory<OrchestrationExtensionContentField> fields)
        {
            Kind = kind;
            Access = access;
            Invocations = invocations;
            Completion = completion;
            Ports = ports;
            Fields = fields;
        }

        public OrchestrationExtensionKind Kind { get; }
        public OrchestrationExtensionAccess Access { get; }
        public OrchestrationExtensionInvocationKind Invocations { get; }
        public OrchestrationExtensionCompletion Completion { get; }
        public ReadOnlyMemory<OrchestrationExtensionPort> Ports { get; }
        public ReadOnlyMemory<OrchestrationExtensionContentField> Fields { get; }
    }
}
