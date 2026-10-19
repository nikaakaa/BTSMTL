using BTSMTL.Orchestration;
using Float32Value = BTSMTL.Orchestration.Float32OrchestrationValue;
using FPValue = ThirdPersonSimulation.QuantumNumerics.FPOrchestrationValue;

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
