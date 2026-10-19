namespace BTSMTL.Orchestration
{
    public struct OrchestrationBlackboardMetadata
    {
        public OrchestrationBlackboardOwnerToken Owner;
        public OrchestrationBlackboardLifetime Lifetime;
        public OrchestrationBlackboardWriteStamp WriteStamp;
        public ulong Revision;
    }
}
