using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationActorProgramSource<TValue> where TValue : unmanaged
    {
        public OrchestrationActorProgramSource(OrchestrationGraphContent<TValue> content,
            StableHash contentIdentity, int identityTableIndex)
        {
            Content = content;
            ContentIdentity = contentIdentity;
            IdentityTableIndex = identityTableIndex;
        }

        public OrchestrationGraphContent<TValue> Content { get; }
        public StableHash ContentIdentity { get; }
        public int IdentityTableIndex { get; }
    }
}
