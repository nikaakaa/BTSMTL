namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationOperation
    {
        public OrchestrationOperation(OrchestrationOperationCode code, bool isRunnable,
            ushort payloadOffset, OrchestrationIndexRange inputs, ushort outputCount, ushort outputSlot,
            OrchestrationIndexRange lifecycleHooks, ushort blackboardFactsBinding,
            ushort parentState, ushort executionFrameOwnerOperationIndex)
        {
            Code = code;
            IsRunnable = isRunnable;
            PayloadOffset = payloadOffset;
            Inputs = inputs;
            OutputCount = outputCount;
            OutputSlot = outputSlot;
            LifecycleHooks = lifecycleHooks;
            BlackboardFactsBinding = blackboardFactsBinding;
            ParentState = parentState;
            ExecutionFrameOwnerOperationIndex = executionFrameOwnerOperationIndex;
        }

        public OrchestrationOperationCode Code { get; }
        public bool IsRunnable { get; }
        public ushort PayloadOffset { get; }
        public OrchestrationIndexRange Inputs { get; }
        public ushort OutputCount { get; }
        public ushort OutputSlot { get; }
        public OrchestrationIndexRange LifecycleHooks { get; }
        public ushort BlackboardFactsBinding { get; }
        public ushort ParentState { get; }
        public ushort ExecutionFrameOwnerOperationIndex { get; }
        public int OutputSlotOffset => OutputSlot - 1;
        public int BlackboardFactsBindingIndex => BlackboardFactsBinding - 1;
        public int ParentStateOperationIndex => ParentState - 1;
    }

    public readonly struct OrchestrationCorePorts
    {
        public OrchestrationCorePorts(int anyState, int stateOnEnter,
            int stateRoot, int stateOnExit, int branchTrue, int branchFalse,
            int branchThen, int cycleReset)
        {
            AnyState = anyState;
            StateOnEnter = stateOnEnter;
            StateRoot = stateRoot;
            StateOnExit = stateOnExit;
            BranchTrue = branchTrue;
            BranchFalse = branchFalse;
            BranchThen = branchThen;
            CycleReset = cycleReset;
        }

        public int AnyState { get; }
        public int StateOnEnter { get; }
        public int StateRoot { get; }
        public int StateOnExit { get; }
        public int BranchTrue { get; }
        public int BranchFalse { get; }
        public int BranchThen { get; }
        public int CycleReset { get; }
    }
}
