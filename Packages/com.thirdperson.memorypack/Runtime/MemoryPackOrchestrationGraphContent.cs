using System.IO;
using BTSMTL.Orchestration;
using Float32Value = BTSMTL.Orchestration.OrchestrationValue<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32Quaternion>;
using FPValue = BTSMTL.Orchestration.OrchestrationValue<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3, Photon.Deterministic.FP, Photon.Deterministic.FPQuaternion>;

namespace ThirdPersonSimulation.Serialization
{
    public sealed partial class MemoryPackGraphContentPublisher :
        IOrchestrationGraphContentEncoding<Float32Value>, IOrchestrationGraphContentEncoding<FPValue>
    {
        public const string Float32OrchestrationContractIdentity = "btsmtl.float32-orchestration.memorypack/1";
        public const string FPOrchestrationContractIdentity = "btsmtl.fp-orchestration.memorypack/1";

        string IOrchestrationGraphContentEncoding<Float32Value>.EncodingContract => Float32OrchestrationContractIdentity;
        string IOrchestrationGraphContentEncoding<FPValue>.EncodingContract => FPOrchestrationContractIdentity;

        public byte[] Write(OrchestrationGraphContent<Float32Value> content) =>
            WriteOrchestrationPayload(MemoryPackContent.Serialize(content));

        public byte[] Write(OrchestrationGraphContent<FPValue> content) =>
            WriteOrchestrationPayload(MemoryPackContent.Serialize(content));

        OrchestrationGraphContent<Float32Value> IOrchestrationGraphContentEncoding<Float32Value>.Read(
            byte[] bytes, GraphExecutionIdentity expected)
        {
            byte[] payload = ReadOrchestrationPayload(bytes);
            OrchestrationGraphContent<Float32Value> content =
                MemoryPackContent.Deserialize<OrchestrationGraphContent<Float32Value>>(payload);
            RequireOrchestrationIdentity(content.Identity, expected, Float32OrchestrationContractIdentity, "Float32");
            return content;
        }

        OrchestrationGraphContent<FPValue> IOrchestrationGraphContentEncoding<FPValue>.Read(
            byte[] bytes, GraphExecutionIdentity expected)
        {
            byte[] payload = ReadOrchestrationPayload(bytes);
            OrchestrationGraphContent<FPValue> content =
                MemoryPackContent.Deserialize<OrchestrationGraphContent<FPValue>>(payload);
            RequireOrchestrationIdentity(content.Identity, expected, FPOrchestrationContractIdentity, "FP");
            return content;
        }

        static byte[] WriteOrchestrationPayload(byte[] payload) =>
            MemoryPackContent.Serialize((payload, StableHash.Compute(payload)));

        static byte[] ReadOrchestrationPayload(byte[] bytes)
        {
            var content = MemoryPackContent.Deserialize<(byte[] Payload, StableHash Hash)>(bytes);
            if (StableHash.Compute(content.Payload) != content.Hash)
                throw new InvalidDataException("图产物内容摘要不匹配，请重新编译。");
            return content.Payload;
        }

        static void RequireOrchestrationIdentity(GraphExecutionIdentity actual, GraphExecutionIdentity expected,
            string encodingContract, string numericTarget)
        {
            if (actual.EncodingContract != encodingContract || actual.NodeContract != GraphNodeSemanticContract.Identity ||
                actual.NumericTarget != numericTarget)
                throw new InvalidDataException("图产物的编码、节点语义或数值目标与当前编排核心不匹配。");
            actual.RequireMatches(expected);
        }
    }
}
