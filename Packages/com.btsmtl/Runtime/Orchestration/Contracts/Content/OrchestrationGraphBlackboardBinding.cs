using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationGraphBlackboardBinding
    {
        public OrchestrationGraphBlackboardBinding(int valueSlot, int instanceIdentityId, int declarationIdentityId)
        {
            ValueSlot = valueSlot;
            InstanceIdentityId = instanceIdentityId;
            DeclarationIdentityId = declarationIdentityId;
        }

        public int ValueSlot { get; }
        public int InstanceIdentityId { get; }
        public int DeclarationIdentityId { get; }
    }
}
