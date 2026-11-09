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

    // 合同端口声明。ObjectKind 是句柄端口的作者期与编译期种类约束，不进入 OrchestrationExtensionPort，
    // 因此不随编译内容序列化，也不参与运行时绑定比对。
    public interface IOrchestrationExtensionPort
    {
        OrchestrationExtensionPort Declaration { get; }
        OrchestrationObjectKind ObjectKind { get; }
    }

    static class OrchestrationExtensionPortKinds
    {
        public static OrchestrationObjectKind Require(string id, OrchestrationValueKind valueKind,
            OrchestrationObjectKind objectKind)
        {
            if ((valueKind == OrchestrationValueKind.ObjectHandle) != (objectKind != null))
                throw new ArgumentException(valueKind == OrchestrationValueKind.ObjectHandle
                    ? $"句柄端口 '{id}' 必须声明对象种类。"
                    : $"非句柄端口 '{id}' 不能声明对象种类。", nameof(objectKind));
            return objectKind;
        }
    }

    public sealed class OrchestrationExtensionInputPort<TOperation> : IOrchestrationExtensionPort
        where TOperation : struct
    {
        public OrchestrationExtensionInputPort(string id, OrchestrationValueKind valueKind, int index,
            OrchestrationObjectKind objectKind = null)
        {
            ObjectKind = OrchestrationExtensionPortKinds.Require(id, valueKind, objectKind);
            Declaration = new OrchestrationExtensionPort(id, valueKind, OrchestrationExtensionPortDirection.Input, index);
            Id = id;
            ValueKind = valueKind;
            Index = index;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public int Index { get; }
        public OrchestrationExtensionPort Declaration { get; }
        public OrchestrationObjectKind ObjectKind { get; }
    }

    public sealed class OrchestrationExtensionOutputPort<TOperation> : IOrchestrationExtensionPort
        where TOperation : struct
    {
        public OrchestrationExtensionOutputPort(string id, OrchestrationValueKind valueKind, int index,
            OrchestrationObjectKind objectKind = null)
        {
            ObjectKind = OrchestrationExtensionPortKinds.Require(id, valueKind, objectKind);
            Declaration = new OrchestrationExtensionPort(id, valueKind, OrchestrationExtensionPortDirection.Output, index);
            Id = id;
            ValueKind = valueKind;
            Index = index;
        }

        public string Id { get; }
        public OrchestrationValueKind ValueKind { get; }
        public int Index { get; }
        public OrchestrationExtensionPort Declaration { get; }
        public OrchestrationObjectKind ObjectKind { get; }
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
