using System;

namespace ThirdPersonSimulation
{
    public enum OperationWaitReason
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

    public interface ISimulationControlTraceInterest
    {
        bool IsControlCaptureRequested(ActorId actorId);
    }

    public enum OperationRunnableStatus : byte
    {
        Dormant = 0,
        Running = 1,
        Success = 2,
        Failure = 3,
        Stopping = 4
    }

    public enum OperationExecutionResult : byte
    {
        None = 0,
        Running = 1,
        Success = 2,
        Failure = 3
    }

    public enum OperationStopCause : byte
    {
        None = 0,
        SelfAbort = 1,
        LowerPriorityAbort = 2,
        ParentStop = 3,
        StateTransition = 4,
        Reset = 5,
        Shutdown = 6,
        ActionContextEnded = 7
    }

    public enum OperationStopStatus : byte
    {
        Running = 1,
        Completed = 2,
        Failed = 3
    }

    public enum OperationControlTraceSeverity : byte
    {
        Detail = 1,
        Error = 2
    }

    internal static class OperationTraceText
    {
        public static string Wait(OperationWaitReason reason) => reason switch
        {
            OperationWaitReason.ChildCompletion => "ChildCompletion",
            OperationWaitReason.ChildStop => "ChildStop",
            OperationWaitReason.SubgraphCompletion => "SubgraphCompletion",
            OperationWaitReason.PriorityReplacement => "PriorityReplacement",
            OperationWaitReason.ParallelCompletion => "ParallelCompletion",
            OperationWaitReason.ParallelStop => "ParallelStop",
            OperationWaitReason.NextIteration => "NextIteration",
            OperationWaitReason.StateEnter => "StateEnter",
            OperationWaitReason.StateExit => "StateExit",
            _ => throw new ArgumentOutOfRangeException(nameof(reason))
        };

        public static string Result(OperationExecutionResult result) => result switch
        {
            OperationExecutionResult.Success => "Success",
            OperationExecutionResult.Failure => "Failure",
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };

        public static string StopCause(OperationStopCause cause) => cause switch
        {
            OperationStopCause.None => "None",
            OperationStopCause.SelfAbort => "SelfAbort",
            OperationStopCause.LowerPriorityAbort => "LowerPriorityAbort",
            OperationStopCause.ParentStop => "ParentStop",
            OperationStopCause.StateTransition => "StateTransition",
            OperationStopCause.Reset => "Reset",
            OperationStopCause.Shutdown => "Shutdown",
            OperationStopCause.ActionContextEnded => "ActionContextEnded",
            _ => throw new ArgumentOutOfRangeException(nameof(cause))
        };

        public static string LifecyclePhase(OperationStateLifecyclePhase phase) => phase switch
        {
            OperationStateLifecyclePhase.Entered => "Entered",
            OperationStateLifecyclePhase.Exited => "Exited",
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
    }

    public enum OperationStateLifecyclePhase : byte
    {
        Entered = 1,
        Exited = 2
    }

    public readonly struct OperationStopContext
    {
        public OperationStopContext(OperationStopCause cause, OperationHandle source, OperationHandle replacement)
        {
            Cause = cause;
            Source = source;
            Replacement = replacement;
        }

        public OperationStopCause Cause { get; }
        public OperationHandle Source { get; }
        public OperationHandle Replacement { get; }
        public bool IsValid => Cause != OperationStopCause.None;

        public static OperationStopContext SelfAbort(OperationHandle source) =>
            new OperationStopContext(OperationStopCause.SelfAbort, source, OperationHandle.Invalid);

        public static OperationStopContext LowerPriorityAbort(OperationHandle source, OperationHandle replacement) =>
            new OperationStopContext(OperationStopCause.LowerPriorityAbort, source, replacement);

        public static OperationStopContext StateTransition(OperationHandle source, OperationHandle replacement) =>
            new OperationStopContext(OperationStopCause.StateTransition, source, replacement);

        public static OperationStopContext ParentStop(OperationHandle source) =>
            new OperationStopContext(OperationStopCause.ParentStop, source, OperationHandle.Invalid);

        public static OperationStopContext Reset(OperationHandle source) =>
            new OperationStopContext(OperationStopCause.Reset, source, OperationHandle.Invalid);

        public static OperationStopContext Shutdown(OperationHandle source) =>
            new OperationStopContext(OperationStopCause.Shutdown, source, OperationHandle.Invalid);

        public static OperationStopContext ActionContextEnded(OperationHandle source) =>
            new OperationStopContext(OperationStopCause.ActionContextEnded, source, OperationHandle.Invalid);
    }

    public interface IOperationControlTarget<TTarget>
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        bool DiagnosticsEnabled { get; }
        bool ControlTraceEnabled { get; }
        void TraceEdge(ProgramControlFlowEdge edge, bool selected, bool passed);
        int ReadInt32(int slotIndex);
        void WriteInt32(int slotIndex, int value);
        ulong ReadUInt64(int slotIndex);
        void WriteUInt64(int slotIndex, ulong value);
        CanonicalTextValue ReadIdentity(int slotIndex);
        void WriteIdentity(int slotIndex, CanonicalTextValue value);
        bool EvaluateCondition(OperationControlCursor<TTarget> cursor, ProgramControlFlowEdge edge);
        OperationExecutionResult ExecuteLeaf(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation);
        void PrepareActivation(OperationExecutionDescriptor operation);
        void PrepareSubGraph(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation);
        void ActivateScopes(OperationControlCursor<TTarget> cursor, OperationExecutionDescriptor operation, ulong generation);
        void CompleteScopes(OperationExecutionDescriptor operation);
        void ClearStateScope(OperationExecutionDescriptor state);
        void ResetOperationState(OperationExecutionDescriptor operation);
        OperationStopStatus ContinueLeafStop(
            OperationControlCursor<TTarget> cursor,
            OperationExecutionDescriptor operation,
            OperationStopContext context);
        void ForceStopLeaf(
            OperationControlCursor<TTarget> cursor,
            OperationExecutionDescriptor operation,
            OperationStopContext context);
        void EmitTrace(
            OperationExecutionDescriptor operation,
            string code,
            OperationControlTraceSeverity severity,
            string detail);
        void NotifyStateLifecycle(
            OperationExecutionDescriptor machine,
            OperationHandle state,
            OperationStateLifecyclePhase phase);
        void NotifyStateTransition(
            OperationExecutionDescriptor machine,
            OperationHandle exitingState,
            OperationHandle targetState);
    }

    public readonly struct OperationControlCursor<TTarget>
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        readonly OperationControlRuntime<TTarget> m_Runtime;

        internal OperationControlCursor(OperationControlRuntime<TTarget> runtime)
        {
            m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public OperationExecutionResult Tick(OperationHandle operation) => m_Runtime.Tick(operation);
        public bool IsPredictiveEvaluation => m_Runtime.IsPredictiveEvaluation;
        public OperationExecutionResult TickPersistent(OperationHandle operation) => m_Runtime.TickPersistent(operation);
        public void RequireExecution(OperationHandle operation) => m_Runtime.RequireExecution(operation);
        public OperationStopStatus RequestStop(OperationHandle operation, OperationStopContext context) => m_Runtime.RequestStop(operation, context);
        public OperationStopStatus ContinueStop(OperationHandle operation) => m_Runtime.ContinueStop(operation);
        public void ForceStop(OperationHandle operation, OperationStopContext context) => m_Runtime.ForceStop(operation, context);
        public bool IsActive(OperationHandle operation) => m_Runtime.IsActive(operation);
        public OperationRunnableStatus ReadStatus(OperationHandle operation) => m_Runtime.ReadStatus(operation);
        public bool IsRunning(OperationHandle operation) => m_Runtime.IsRunning(operation);
        public bool IsStopping(OperationHandle operation) => m_Runtime.IsStopping(operation);
        public ulong ReadGeneration(OperationHandle operation) => m_Runtime.ReadGeneration(operation);
        public bool CurrentStateRootCompleted() => m_Runtime.CurrentStateRootCompleted();
        public ProgramControlFlowEdge PredictCurrentStateRootCompletionTransition() =>
            m_Runtime.PredictCurrentStateRootCompletionTransition();
        public int CurrentStateExitCause() => m_Runtime.CurrentStateExitCause();
        public CanonicalTextValue FindStateExecutionPath(OperationHandle state) => m_Runtime.FindStateExecutionPath(state);
        public bool TryGetCurrentStateExecutionPath(OperationHandle state, out CanonicalTextValue path) =>
            m_Runtime.TryGetCurrentStateExecutionPath(state, out path);
        public bool IsCurrentStateExecution(OperationHandle state) => m_Runtime.IsCurrentStateExecution(state);
        public OperationControlRuntime<TTarget>.StateExecutionScope PushStateExecution(OperationHandle state, int exitCause) => m_Runtime.PushStateExecution(state, exitCause);
    }
}
