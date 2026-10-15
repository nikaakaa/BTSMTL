using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationActorProgramSource<TValue> where TValue : unmanaged
    {
        public OrchestrationActorProgramSource(OrchestrationGraphContent<TValue> content,
            StableHash contentIdentity)
        {
            Content = content;
            ContentIdentity = contentIdentity;
        }

        public OrchestrationGraphContent<TValue> Content { get; }
        public StableHash ContentIdentity { get; }
    }
}
