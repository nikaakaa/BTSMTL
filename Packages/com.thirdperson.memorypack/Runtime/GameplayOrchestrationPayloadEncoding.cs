using System;
using MemoryPack;
using ThirdPersonGameplay.Orchestration;
using Float32Value = BTSMTL.Orchestration.OrchestrationValue<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32Quaternion>;
using FPValue = BTSMTL.Orchestration.OrchestrationValue<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, Photon.Deterministic.FPQuaternion>;

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
