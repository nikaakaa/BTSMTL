namespace BTSMTL.Orchestration
{
    public struct OrchestrationBlackboardOwnerToken
    {
        public OrchestrationScopeKind ScopeKind;
        public int CompiledOwnerIndex;
        public ulong Generation;

        public readonly bool Equals(in OrchestrationBlackboardOwnerToken other) =>
            ScopeKind == other.ScopeKind && CompiledOwnerIndex == other.CompiledOwnerIndex &&
            Generation == other.Generation;
    }
}
