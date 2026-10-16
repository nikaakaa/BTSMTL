using BTSMTL.Orchestration;
using Float32Value = BTSMTL.Orchestration.OrchestrationValue<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32Quaternion>;
using FPValue = BTSMTL.Orchestration.OrchestrationValue<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, Photon.Deterministic.FPQuaternion>;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackBlackboardDefaultsEncoding
    {
        public static byte[] Write(OrchestrationBlackboardDefaults<Float32Value> defaults) =>
            MemoryPackContent.Serialize(defaults);

        public static byte[] Write(OrchestrationBlackboardDefaults<FPValue> defaults) =>
            MemoryPackContent.Serialize(defaults);

        public static OrchestrationBlackboardDefaults<TValue> Read<TValue>(byte[] bytes)
            where TValue : unmanaged =>
            MemoryPackContent.Deserialize<OrchestrationBlackboardDefaults<TValue>>(bytes);
    }
}
