using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationOperationColdData
    {
        public OrchestrationOperationColdData(int identityId, int valueVariant)
        {
            IdentityId = identityId;
            ValueVariant = valueVariant;
        }

        public readonly int IdentityId;
        public readonly int ValueVariant;
        public OrchestrationComparisonKind Comparison => (OrchestrationComparisonKind)ValueVariant;
        public OrchestrationCalculationKind Calculation => (OrchestrationCalculationKind)ValueVariant;
    }
}
