namespace BTSMTL.Orchestration
{
    public enum OrchestrationHookInputSource : byte
    {
        Constant = 1,
        OperationIndex = 2,
        OperationGeneration = 3,
        MachineOperationIndex = 4,
        StateOperationIndex = 5,
        ExitingStateOperationIndex = 6,
        TargetStateOperationIndex = 7,
        Tick = 8,
        StateGeneration = 9,
        ExitingStateGeneration = 10,
        TargetStateGeneration = 11
    }

    public readonly struct OrchestrationHookInput
    {
        public OrchestrationHookInput(OrchestrationHookInputSource source, int constantIndex)
        {
            Source = source;
            ConstantIndex = constantIndex;
        }

        public OrchestrationHookInputSource Source { get; }
        public int ConstantIndex { get; }
    }

    public readonly struct OrchestrationHookBinding
    {
        public OrchestrationHookBinding(OrchestrationLifecycleHook hook,
            int extensionBindingIndex, OrchestrationRange inputs)
        {
            Hook = hook;
            ExtensionBindingIndex = extensionBindingIndex;
            Inputs = inputs;
        }

        public OrchestrationLifecycleHook Hook { get; }
        public int ExtensionBindingIndex { get; }
        public OrchestrationRange Inputs { get; }
    }
}
