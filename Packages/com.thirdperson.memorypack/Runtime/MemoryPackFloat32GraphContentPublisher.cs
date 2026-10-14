using System.Collections.Generic;

namespace ThirdPersonSimulation.Serialization
{
    public sealed partial class MemoryPackGraphContentPublisher
    {
        public const string Float32ContractIdentity = "btsmtl.float32-graph.memorypack/1";

        public byte[] WriteGraph(Float32GraphExecutionData data, GraphExecutionIdentity identity) =>
            MemoryPackContent.Serialize((identity, data.Tables, data.GraphSources.Diagnostics,
                data.GraphSourceRevisions.Sources, data.Chunks.PayloadHash));

        public Float32GraphExecutionData ReadGraph(byte[] bytes, GraphExecutionIdentity expected)
        {
            var content = MemoryPackContent.Deserialize<(GraphExecutionIdentity Identity, Float32GraphExecutionTables Tables,
                IReadOnlyList<ProgramSourceMapEntry> Diagnostics, IReadOnlyList<ProgramGraphSourceRevision> Revisions,
                StableHash PayloadHash)>(bytes);
            if (content.Identity.EncodingContract != Float32ContractIdentity ||
                content.Identity.NodeContract != GraphNodeSemanticContract.Identity || content.Identity.NumericTarget != "Float32")
                throw new System.IO.InvalidDataException("图产物的编码、节点语义或数值目标与当前 Float32 核心不匹配。");
            content.Identity.RequireMatches(expected);
            var graph = new Float32GraphExecutionData(content.Tables,
                ReadSources(content.Tables.Structure.ExecutionSources, content.Diagnostics, content.Revisions));
            if (graph.Chunks.PayloadHash != content.PayloadHash)
                throw new System.IO.InvalidDataException("图产物内容摘要不匹配，请重新编译。");
            return graph;
        }
    }
}
