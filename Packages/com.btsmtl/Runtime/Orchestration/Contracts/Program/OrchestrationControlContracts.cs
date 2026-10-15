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

    public readonly struct OrchestrationStopContext
    {
        public OrchestrationStopContext(uint cause, int sourceOperationIndex,
            int replacementOperationIndex)
        {
            Cause = cause;
            SourceOperationIndex = sourceOperationIndex;
            ReplacementOperationIndex = replacementOperationIndex;
        }

        public uint Cause { get; }
        public int SourceOperationIndex { get; }
        public int ReplacementOperationIndex { get; }
        public bool IsValid => Cause != 0;

        public static OrchestrationStopContext SelfAbort(int sourceOperationIndex) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.SelfAbort,
                sourceOperationIndex, -1);

        public static OrchestrationStopContext LowerPriorityAbort(int sourceOperationIndex,
            int replacementOperationIndex) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.LowerPriorityAbort,
                sourceOperationIndex, replacementOperationIndex);

        public static OrchestrationStopContext StateTransition(int sourceOperationIndex,
            int replacementOperationIndex) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.StateTransition,
                sourceOperationIndex, replacementOperationIndex);

        public static OrchestrationStopContext ParentStop(int sourceOperationIndex) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.ParentStop,
                sourceOperationIndex, -1);

        public static OrchestrationStopContext Reset(int sourceOperationIndex) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.Reset,
                sourceOperationIndex, -1);

        public static OrchestrationStopContext Shutdown(int sourceOperationIndex) =>
            new OrchestrationStopContext((uint)OrchestrationStopCause.Shutdown,
                sourceOperationIndex, -1);
    }
}
