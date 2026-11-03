namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationBlackboardSlot
    {
        public OrchestrationBlackboardSlot(OrchestrationScopeKind scopeKind,
            OrchestrationBlackboardLifetime lifetime, int compiledOwnerIndex,
            int ownerOperationIndex, int executionFrameOwnerOperationIndex,
            OrchestrationValueKind valueKind, bool isGraphCallParameter)
        {
            m_ScopeKind = (ushort)scopeKind;
            m_Lifetime = (ushort)lifetime;
            CompiledOwnerIndex = compiledOwnerIndex;
            OwnerOperationIndex = ownerOperationIndex;
            ExecutionFrameOwnerOperationIndex = executionFrameOwnerOperationIndex;
            m_ValueKind = (ushort)valueKind;
            m_IsGraphCallParameter = isGraphCallParameter ? (ushort)1 : (ushort)0;
        }

        readonly ushort m_ScopeKind;
        readonly ushort m_Lifetime;
        public OrchestrationScopeKind ScopeKind => (OrchestrationScopeKind)m_ScopeKind;
        public OrchestrationBlackboardLifetime Lifetime => (OrchestrationBlackboardLifetime)m_Lifetime;
        public int CompiledOwnerIndex { get; }
        public int OwnerOperationIndex { get; }
        public int ExecutionFrameOwnerOperationIndex { get; }
        readonly ushort m_ValueKind;
        readonly ushort m_IsGraphCallParameter;
        public OrchestrationValueKind ValueKind => (OrchestrationValueKind)m_ValueKind;
        public bool IsGraphCallParameter => m_IsGraphCallParameter != 0;
    }
}
