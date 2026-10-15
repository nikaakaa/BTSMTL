namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationOperation
    {
        public OrchestrationOperation(OrchestrationOperationCode code, bool isRunnable,
            int identityId, int integer0, int integer1, int constantIndex, int inputSlot,
            int requestIdentityId, int valueSlot, int timelineContentIdentityId,
            int stateIdentityId, int stateMachineOwnerOperationIndex,
            int extensionBindingIndex, int graphCallIndex, OrchestrationRange inputs,
            int outputCount, int outputSlotOffset, OrchestrationRange lifecycleHooks,
            int blackboardFactsBindingIndex, int parentStateOperationIndex,
            OrchestrationRange outputAssignments)
        {
            Code = code;
            IsRunnable = isRunnable;
            IdentityId = identityId;
            Integer0 = integer0;
            Integer1 = integer1;
            ConstantIndex = constantIndex;
            InputSlot = inputSlot;
            RequestIdentityId = requestIdentityId;
            ValueSlot = valueSlot;
            TimelineContentIdentityId = timelineContentIdentityId;
            StateIdentityId = stateIdentityId;
            StateMachineOwnerOperationIndex = stateMachineOwnerOperationIndex;
            ExtensionBindingIndex = extensionBindingIndex;
            GraphCallIndex = graphCallIndex;
            Inputs = inputs;
            OutputCount = outputCount;
            OutputSlotOffset = outputSlotOffset;
            LifecycleHooks = lifecycleHooks;
            BlackboardFactsBindingIndex = blackboardFactsBindingIndex;
            ParentStateOperationIndex = parentStateOperationIndex;
            OutputAssignments = outputAssignments;
        }

        public OrchestrationOperationCode Code { get; }
        public bool IsRunnable { get; }
        public int IdentityId { get; }
        public int Integer0 { get; }
        public int Integer1 { get; }
        public OrchestrationComparisonKind Comparison => (OrchestrationComparisonKind)Integer0;
        public OrchestrationCalculationKind Calculation => (OrchestrationCalculationKind)Integer0;
        public OrchestrationAssignmentKind Assignment => (OrchestrationAssignmentKind)Integer1;
        public int ConstantIndex { get; }
        public int InputSlot { get; }
        public int RequestIdentityId { get; }
        public int ValueSlot { get; }
        public int TimelineContentIdentityId { get; }
        public int StateIdentityId { get; }
        public int StateMachineOwnerOperationIndex { get; }
        public int ExtensionBindingIndex { get; }
        public int GraphCallIndex { get; }
        public OrchestrationRange Inputs { get; }
        public int OutputCount { get; }
        public int OutputSlotOffset { get; }
        public OrchestrationRange LifecycleHooks { get; }
        public int BlackboardFactsBindingIndex { get; }
        public int ParentStateOperationIndex { get; }
        public OrchestrationRange OutputAssignments { get; }
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
