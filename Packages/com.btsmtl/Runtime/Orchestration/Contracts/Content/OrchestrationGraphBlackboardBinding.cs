using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationGraphBlackboardBinding
    {
        public OrchestrationGraphBlackboardBinding(int valueSlot, int instanceIdentityId,
            int contractIdentityId, int field)
        {
            ValueSlot = valueSlot;
            InstanceIdentityId = instanceIdentityId;
            ContractIdentityId = contractIdentityId;
            Field = field;
        }

        public int ValueSlot { get; }
        public int InstanceIdentityId { get; }
        public int ContractIdentityId { get; }
        public int Field { get; }
    }
}
