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
            OrchestrationExtensionPortDirection direction, int index, OrchestrationObjectKind objectKind = null)
        {
            if ((valueKind == OrchestrationValueKind.ObjectHandle) != (objectKind != null))
                throw new ArgumentException(valueKind == OrchestrationValueKind.ObjectHandle
                    ? $"句柄端口 '{id}' 必须声明对象种类。"
                    : $"非句柄端口 '{id}' 不能声明对象种类。", nameof(objectKind));
            Id = id;
            ValueKind = valueKind;
            Direction = direction;
            Index = index;
            ObjectKind = objectKind;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public OrchestrationExtensionPortDirection Direction { get; }
        public int Index { get; }
        // 句柄端口的对象种类；非句柄端口为 null。
        public OrchestrationObjectKind ObjectKind { get; }
    }

    public interface IOrchestrationExtensionPort
    {
        OrchestrationExtensionPort Declaration { get; }
    }

    public sealed class OrchestrationExtensionInputPort<TOperation> : IOrchestrationExtensionPort
        where TOperation : struct
    {
        public OrchestrationExtensionInputPort(string id, OrchestrationValueKind valueKind, int index,
            OrchestrationObjectKind objectKind = null)
        {
            Declaration = new OrchestrationExtensionPort(id, valueKind, OrchestrationExtensionPortDirection.Input,
                index, objectKind);
            Id = id;
            ValueKind = valueKind;
            Index = index;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public int Index { get; }
        public OrchestrationExtensionPort Declaration { get; }
    }

    public sealed class OrchestrationExtensionOutputPort<TOperation> : IOrchestrationExtensionPort
        where TOperation : struct
    {
        public OrchestrationExtensionOutputPort(string id, OrchestrationValueKind valueKind, int index,
            OrchestrationObjectKind objectKind = null)
        {
            Declaration = new OrchestrationExtensionPort(id, valueKind, OrchestrationExtensionPortDirection.Output,
                index, objectKind);
            Id = id;
            ValueKind = valueKind;
            Index = index;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public int Index { get; }
        public OrchestrationExtensionPort Declaration { get; }
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

        public OrchestrationExtensionExecutionContract Execution =>
            new OrchestrationExtensionExecutionContract(Kind, Access, Invocations, Completion);
    }

    // 推进只读取的扩展执行约定，纯值，可进入 Burst。
    public readonly struct OrchestrationExtensionExecutionContract
    {
        public OrchestrationExtensionExecutionContract(OrchestrationExtensionKind kind,
            OrchestrationExtensionAccess access, OrchestrationExtensionInvocationKind invocations,
            OrchestrationExtensionCompletion completion)
        {
            Kind = kind;
            Access = access;
            Invocations = invocations;
            Completion = completion;
        }

        public OrchestrationExtensionKind Kind { get; }
        public OrchestrationExtensionAccess Access { get; }
        public OrchestrationExtensionInvocationKind Invocations { get; }
        public OrchestrationExtensionCompletion Completion { get; }
    }
}
