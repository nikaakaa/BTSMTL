using BTSMTL.Orchestration;
using RuntimeValue = BTSMTL.Orchestration.OrchestrationValue<
    ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2,
    ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw,
    ThirdPersonSimulation.Float32Quaternion>;

namespace ThirdPersonSimulation
{
    public static partial class Float32OrchestrationStateLayouts
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
