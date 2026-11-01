namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationHookBinding
    {
        public OrchestrationHookBinding(OrchestrationLifecycleHook hook, int extensionBindingIndex)
        {
            Hook = hook;
            ExtensionBindingIndex = extensionBindingIndex;
        }

        public OrchestrationLifecycleHook Hook { get; }
        public int ExtensionBindingIndex { get; }
    }
}
