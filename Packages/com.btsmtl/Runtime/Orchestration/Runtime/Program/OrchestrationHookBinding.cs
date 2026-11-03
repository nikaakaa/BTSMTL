namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationHookBinding
    {
        public OrchestrationHookBinding(OrchestrationLifecycleHook hook, int extensionBindingIndex)
        {
            m_Hook = (int)hook;
            ExtensionBindingIndex = extensionBindingIndex;
        }

        readonly int m_Hook;
        public OrchestrationLifecycleHook Hook => (OrchestrationLifecycleHook)m_Hook;
        public int ExtensionBindingIndex { get; }
    }
}
