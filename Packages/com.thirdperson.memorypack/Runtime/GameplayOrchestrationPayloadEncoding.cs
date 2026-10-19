using System;
using MemoryPack;
using ThirdPersonGameplay.Orchestration;
using Float32Value = BTSMTL.Orchestration.Float32OrchestrationValue;
using FPValue = ThirdPersonSimulation.QuantumNumerics.FPOrchestrationValue;

namespace ThirdPersonSimulation.Serialization
{
    public static class GameplayOrchestrationPayloadEncoding
    {
        public static byte[] Write(GameplayOrchestrationPayload<Float32Value, Float32Scalar> payload) =>
            MemoryPackSerializer.Serialize(payload, CreateOptions());

        public static byte[] Write(GameplayOrchestrationPayload<FPValue, Photon.Deterministic.FP> payload) =>
            MemoryPackSerializer.Serialize(payload, CreateOptions());

        static MemoryPackSerializerOptions CreateOptions()
        {
            MemoryPackContent.Initialize();
            return new MemoryPackSerializerOptions
            {
                ServiceProvider = new MemoryPackContentContext(MemoryPackContentPart.Full),
                StringEncoding = StringEncoding.Utf16
            };
        }

        public static GameplayOrchestrationPayload<TValue, TScalar> Read<TValue, TScalar>(byte[] payload)
            where TValue : unmanaged
            where TScalar : struct, IComparable<TScalar>, IEquatable<TScalar> =>
            MemoryPackContent.Deserialize<GameplayOrchestrationPayload<TValue, TScalar>>(payload);
    }
}
