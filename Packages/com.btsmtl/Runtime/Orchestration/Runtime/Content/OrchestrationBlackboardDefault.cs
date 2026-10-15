using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationBlackboardDefault<TValue> where TValue : unmanaged
    {
        public OrchestrationBlackboardDefault(int contractIdentityId, int field, TValue value)
        {
            ContractIdentityId = contractIdentityId;
            Field = field;
            Value = value;
        }

        public int ContractIdentityId { get; }
        public int Field { get; }
        public TValue Value { get; }
    }
}
