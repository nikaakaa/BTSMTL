using BTSMTL.Orchestration;
using RuntimeValue = BTSMTL.Orchestration.OrchestrationValue<
    Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3,
    Photon.Deterministic.FP, Photon.Deterministic.FPQuaternion>;

namespace ThirdPersonSimulation.FPGameplay
{
    public static partial class FPOrchestrationStateLayouts
    {
        [ValueStateLayoutFactory]
        public static partial OrchestrationExecutionStateLayout<RuntimeValue> PrepareExecution(
            StableHash contentIdentity, NumericProfileId numericProfile, TargetAbiVersion targetAbi,
            int blackboardMetadataCapacity, int framesCapacity, int operationsCapacity,
            int rosterCapacity, int valuesCapacity);

        [ValueStateLayoutFactory]
        public static partial OrchestrationCharacterBlackboardStateLayout<RuntimeValue> PrepareCharacterBlackboard(
            StableHash contentIdentity, NumericProfileId numericProfile, TargetAbiVersion targetAbi,
            int metadataCapacity, int valuesCapacity);
    }
}
