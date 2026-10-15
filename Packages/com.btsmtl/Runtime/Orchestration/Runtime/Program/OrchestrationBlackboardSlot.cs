namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationBlackboardSlot
    {
        public OrchestrationBlackboardSlot(OrchestrationScopeKind scopeKind,
            OrchestrationBlackboardLifetime lifetime, int compiledOwnerIndex,
            int ownerOperationIndex, OrchestrationValueKind valueKind, bool isGraphCallParameter)
        {
            ScopeKind = scopeKind;
            Lifetime = lifetime;
            CompiledOwnerIndex = compiledOwnerIndex;
            OwnerOperationIndex = ownerOperationIndex;
            ValueKind = valueKind;
            IsGraphCallParameter = isGraphCallParameter;
        }

        public OrchestrationScopeKind ScopeKind { get; }
        public OrchestrationBlackboardLifetime Lifetime { get; }
        public int CompiledOwnerIndex { get; }
        public int OwnerOperationIndex { get; }
        public OrchestrationValueKind ValueKind { get; }
        public bool IsGraphCallParameter { get; }
    }
}
