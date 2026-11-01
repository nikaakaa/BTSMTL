using System;
using System.IO;
using System.Text;
using BTSMTL.Orchestration;
using Float32Value = BTSMTL.Orchestration.Float32OrchestrationValue;
using FPValue = ThirdPersonSimulation.QuantumNumerics.FPOrchestrationValue;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackBlackboardDefaultsEncoding
    {
        public const string ContractIdentity = "btsmtl.blackboard-defaults.memorypack/2";

        public static byte[] Write(OrchestrationBlackboardDefaults<Float32Value> defaults) =>
            Encode<Float32Value>(MemoryPackContent.Serialize(defaults));

        public static byte[] Write(OrchestrationBlackboardDefaults<FPValue> defaults) =>
            Encode<FPValue>(MemoryPackContent.Serialize(defaults));

        public static OrchestrationBlackboardDefaults<TValue> Read<TValue>(byte[] bytes)
            where TValue : unmanaged
        {
            byte[] header = Header<TValue>();
            if (!bytes.AsSpan().StartsWith(header))
                throw new InvalidDataException("黑板默认值的编码或值布局与当前编排核心不匹配，请沿正式入口重新发布。");
            return MemoryPackContent.Deserialize<OrchestrationBlackboardDefaults<TValue>>(bytes.AsSpan(header.Length));
        }

        static byte[] Encode<TValue>(byte[] content) where TValue : unmanaged
        {
            byte[] header = Header<TValue>();
            var bytes = new byte[header.Length + content.Length];
            header.CopyTo(bytes, 0);
            content.CopyTo(bytes, header.Length);
            return bytes;
        }

        static byte[] Header<TValue>() where TValue : unmanaged => Encoding.UTF8.GetBytes(
            ContractIdentity + "|" + MemoryPackContent.GetSchemaHash<OrchestrationBlackboardDefaults<TValue>>().Value + "\n");
    }
}
