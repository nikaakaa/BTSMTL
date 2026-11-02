using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationExecutionContract : byte
    {
        Complete = 1,
        Resumable = 2
    }

    public enum OrchestrationOperationStatus : byte
    {
        Dormant = 0,
        Running = 1,
        Success = 2,
        Failure = 3,
        Stopping = 4
    }

    public enum OrchestrationOperationResult : byte
    {
        Running = 1,
        Success = 2,
        Failure = 3
    }

    public enum OrchestrationStopCause : uint
    {
        None = 0,
        SelfAbort = 1,
        LowerPriorityAbort = 2,
        ParentStop = 3,
        StateTransition = 4,
        Reset = 5,
        Shutdown = 6
    }

    public enum OrchestrationStopStatus : byte
    {
        Running = 1,
        Completed = 2,
        Failed = 3
    }

    public enum OrchestrationStopMode : byte
    {
        Request = 1,
        Force = 2
    }

    public static class OrchestrationHostStopCauseRange
    {
        public const uint First = 0x10000;
        public const uint Last = 0x1FFFF;
    }

    public enum OrchestrationWaitReason : byte
    {
        ChildCompletion,
        ChildStop,
        SubgraphCompletion,
        PriorityReplacement,
        ParallelCompletion,
        ParallelStop,
        NextIteration,
        StateEnter,
        StateExit
    }

    public enum OrchestrationFlowKind : byte
    {
        Child = 1,
        Transition = 2,
        Enter = 3,
        Exit = 4,
        Interrupt = 5
    }

    public static class OrchestrationStopRoute
    {
        public static OrchestrationFlowKind ChildFlowKind(OrchestrationOperationCode code) =>
            code == OrchestrationOperationCode.SubGraph ? OrchestrationFlowKind.Enter : OrchestrationFlowKind.Child;

        // 状态、状态机走各自路线；Timeline 与声明 ContinueStop 的扩展由自身完成普通停止。
        // continuesStop 由调用方按自身依据给出：执行器读实际绑定能力，容量分析读引用保证的能力。
        public static bool OrdinaryStopUsesChildren(OrchestrationOperationCode code, bool continuesStop) =>
            code switch
            {
                OrchestrationOperationCode.State or OrchestrationOperationCode.StateMachine or
                    OrchestrationOperationCode.Timeline => false,
                OrchestrationOperationCode.Extension => !continuesStop,
                _ => true
            };
    }

    public enum OrchestrationAbortPolicy : byte
    {
        None = 0,
        Self = 1,
        LowerPriority = 2,
        Both = 3
    }

    public enum OrchestrationLifecycleHook : byte
    {
        OperationActivated = 1,
        StateEntered = 2,
        StateExited = 3,
        StateTransition = 4
    }

    public readonly struct OrchestrationExecutionEntry
    {
        public OrchestrationExecutionEntry(int operationIndex,
            OrchestrationExecutionContract contract, int inputPortId = 0)
        {
            OperationIndex = operationIndex;
            Contract = contract;
            InputPortId = inputPortId;
        }

        public int OperationIndex { get; }
        public OrchestrationExecutionContract Contract { get; }
        public int InputPortId { get; }
    }

    public struct OrchestrationStopContext
    {
        static readonly OrchestrationStopContext s_Empty;

        public static ref readonly OrchestrationStopContext Empty => ref s_Empty;

        public static ReadOnlySpan<OrchestrationStopContext> Borrow(in OrchestrationStopContext context) =>
            MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in context), 1);

        public OrchestrationStopContext(uint cause, OrchestrationStopMode mode, ulong tick,
            int initiatorOperationIndex, int initiatorExecutionFrameIndex,
            int replacementOperationIndex = -1, int replacementEdgeIndex = -1)
        {
            Cause = cause;
            Mode = mode;
            Tick = tick;
            OriginTick = tick;
            InitiatorOperationIndex = initiatorOperationIndex;
            InitiatorExecutionFrameIndex = initiatorExecutionFrameIndex;
            SourceOperationIndex = initiatorOperationIndex;
            SourceExecutionFrameIndex = initiatorExecutionFrameIndex;
            OriginSourceOperationIndex = -1;
            OriginSourceExecutionFrameIndex = -1;
            ParentOperationIndex = -1;
            ParentExecutionFrameIndex = -1;
            PropagationDepth = 0;
            ReplacementOperationIndex = replacementOperationIndex;
            ReplacementEdgeIndex = replacementEdgeIndex;
            StateOperationIndex = -1;
            StateExecutionFrameIndex = -1;
            StateTargetOperationIndex = -1;
            StateTransitionEdgeIndex = -1;
        }

        public uint Cause { get; private set; }
        public OrchestrationStopMode Mode { get; private set; }
        public ulong Tick { get; private set; }
        public ulong OriginTick { get; private set; }
        public int InitiatorOperationIndex { get; private set; }
        public int InitiatorExecutionFrameIndex { get; private set; }
        public int SourceOperationIndex { get; private set; }
        public int SourceExecutionFrameIndex { get; private set; }
        public int OriginSourceOperationIndex { get; private set; }
        public int OriginSourceExecutionFrameIndex { get; private set; }
        public int ParentOperationIndex { get; private set; }
        public int ParentExecutionFrameIndex { get; private set; }
        public int PropagationDepth { get; private set; }
        public int ReplacementOperationIndex { get; private set; }
        public int ReplacementEdgeIndex { get; private set; }
        internal int StateOperationIndex { get; private set; }
        internal int StateExecutionFrameIndex { get; private set; }
        internal int StateTargetOperationIndex { get; private set; }
        internal int StateTransitionEdgeIndex { get; private set; }
        public readonly bool IsValid => Mode != 0;

        public readonly OrchestrationStopContext AtOperation(int operationIndex, int executionFrameIndex, ulong tick)
        {
            OrchestrationStopContext context = this;
            context.Tick = tick;
            if (OriginSourceOperationIndex < 0)
            {
                context.OriginSourceOperationIndex = operationIndex;
                context.OriginSourceExecutionFrameIndex = executionFrameIndex;
            }
            if (SourceOperationIndex != operationIndex || SourceExecutionFrameIndex != executionFrameIndex)
            {
                context.ParentOperationIndex = SourceOperationIndex;
                context.ParentExecutionFrameIndex = SourceExecutionFrameIndex;
                context.SourceOperationIndex = operationIndex;
                context.SourceExecutionFrameIndex = executionFrameIndex;
                context.PropagationDepth++;
            }
            return context;
        }

        public readonly OrchestrationStopContext AtTick(ulong tick)
        {
            OrchestrationStopContext context = this;
            context.Tick = tick;
            return context;
        }

        public readonly OrchestrationStopContext WithMode(OrchestrationStopMode mode)
        {
            OrchestrationStopContext context = this;
            context.Mode = mode;
            return context;
        }

        public readonly OrchestrationStopContext WithReplacement(int operationIndex, int edgeIndex)
        {
            OrchestrationStopContext context = this;
            context.ReplacementOperationIndex = operationIndex;
            context.ReplacementEdgeIndex = edgeIndex;
            return context;
        }

        public readonly OrchestrationStopContext ForStateExit(int operationIndex, int executionFrameIndex,
            int targetOperationIndex = -1, int transitionEdgeIndex = -1)
        {
            OrchestrationStopContext context = this;
            context.StateOperationIndex = operationIndex;
            context.StateExecutionFrameIndex = executionFrameIndex;
            context.StateTargetOperationIndex = targetOperationIndex;
            context.StateTransitionEdgeIndex = transitionEdgeIndex;
            return context;
        }

        public readonly OrchestrationStopContext Resume(in OrchestrationStopContext update)
        {
            OrchestrationStopContext context = update;
            if (Cause == update.Cause && InitiatorOperationIndex == update.InitiatorOperationIndex &&
                InitiatorExecutionFrameIndex == update.InitiatorExecutionFrameIndex)
                context.OriginTick = OriginTick;
            return context;
        }

        public static OrchestrationStopContext SelfAbort(int initiator, int frame, ulong tick) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.SelfAbort,
                OrchestrationStopMode.Request, tick, initiator, frame);

        public static OrchestrationStopContext LowerPriorityAbort(int initiator, int frame, ulong tick,
            int replacement, int edge) => new OrchestrationStopContext((uint)OrchestrationStopCause.LowerPriorityAbort,
                OrchestrationStopMode.Request, tick, initiator, frame, replacement, edge);

        public static OrchestrationStopContext StateTransition(int initiator, int frame, ulong tick,
            int replacement, int edge) => new OrchestrationStopContext((uint)OrchestrationStopCause.StateTransition,
                OrchestrationStopMode.Request, tick, initiator, frame, replacement, edge);

        public static OrchestrationStopContext ParentStop(int initiator, int frame, ulong tick) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.ParentStop,
                OrchestrationStopMode.Request, tick, initiator, frame);

        public static OrchestrationStopContext Reset(int initiator, int frame, ulong tick) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.Reset,
                OrchestrationStopMode.Force, tick, initiator, frame);

        public static OrchestrationStopContext Shutdown(int initiator, int frame, ulong tick) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.Shutdown,
                OrchestrationStopMode.Force, tick, initiator, frame);
    }

    public readonly ref struct OrchestrationStateExitContext
    {
        readonly ReadOnlySpan<OrchestrationStopContext> m_Stop;

        public OrchestrationStateExitContext(in OrchestrationStopContext stop) =>
            m_Stop = OrchestrationStopContext.Borrow(in stop);

        ref readonly OrchestrationStopContext Stop => ref m_Stop[0];

        public bool IsValid => Stop.IsValid && Stop.StateOperationIndex >= 0;
        public uint Cause => Stop.Cause;
        public OrchestrationStopMode Mode => Stop.Mode;
        public ulong Tick => Stop.Tick;
        public int SourceStateOperationIndex => Stop.StateOperationIndex;
        public int SourceStateExecutionFrameIndex => Stop.StateExecutionFrameIndex;
        public int TargetStateOperationIndex => Stop.StateTargetOperationIndex;
        public int TransitionEdgeIndex => Stop.StateTransitionEdgeIndex;
        bool HasParentTree => Stop.Cause != (uint)OrchestrationStopCause.None &&
            Stop.Cause != (uint)OrchestrationStopCause.StateTransition;
        public int ParentTreeSourceOperationIndex => HasParentTree ? Stop.OriginSourceOperationIndex : -1;
        public int ParentTreeExecutionFrameIndex => HasParentTree ? Stop.OriginSourceExecutionFrameIndex : -1;
        public int ParentTreeReplacementOperationIndex => HasParentTree ? Stop.ReplacementOperationIndex : -1;
        public int ParentTreeReplacementEdgeIndex => HasParentTree ? Stop.ReplacementEdgeIndex : -1;
    }
}
