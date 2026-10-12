using System;
using System.Collections.Generic;
using System.IO;
using static ThirdPersonSimulation.GameplayAbilitySemanticsCodec;

namespace ThirdPersonSimulation
{
    public static class Float32GraphExecutionDataCodec
    {
        const int Version = 8;
        public const string ContractIdentity = "btsmtl.float32-graph/8";

        public static byte[] Write(Float32GraphExecutionData data, GraphExecutionIdentity identity)
        {
            using var writer = new CanonicalWriter();
            writer.WriteInt32(Version);
            GraphExecutionIdentity.CanonicalWrite(writer, identity);
            WriteTables(writer, data);
            return writer.ToArray();
        }

        public static Float32GraphExecutionData Read(byte[] bytes, GraphExecutionIdentity expected)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadInt32() != Version)
                throw new InvalidDataException("图执行数据版本不匹配，请重新编译。");
            GraphExecutionIdentity identity = GraphExecutionIdentity.CanonicalReadMaterialized(reader);
            if (identity.EncodingContract != ContractIdentity || identity.NodeContract != GraphNodeSemanticContract.Identity ||
                identity.NumericTarget != "Float32")
                throw new InvalidDataException("图产物的编码、节点语义或数值目标与当前 Float32 核心不匹配。");
            identity.RequireMatches(expected);
            return ReadTables(reader);
        }

        public static GraphExecutionIdentity ReadIdentity(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadInt32() != Version)
                throw new InvalidDataException("图执行数据版本不匹配，请重新编译。");
            return GraphExecutionIdentity.CanonicalReadMaterialized(reader);
        }

        internal static GraphExecutionChunks CreateChunks(Float32GraphExecutionData data)
        {
            var structure = GraphExecutionChunk.Encode(writer => WriteStructure(writer, data));
            var parameters = GraphExecutionChunk.Encode(writer => WriteParameters(writer, data, structure.Hash));
            return new GraphExecutionChunks(structure, parameters, data.GraphSources.DiagnosticChunk);
        }

        static void WriteStructure(CanonicalWriter writer, Float32GraphExecutionData data)
        {
            writer.WriteText(ContractIdentity);
            writer.WriteText(GraphNodeSemanticContract.Identity);
            writer.WriteHash(Float32GraphExecutionTables.CanonicalSchemaHash);
            Float32GraphExecutionTables.CanonicalWriteLayout(writer, data.Tables);
        }

        internal static void WriteParameters(CanonicalWriter writer, Float32GraphExecutionData data,
            StableHash structureHash)
        {
            writer.WriteHash(structureHash);
            Float32GraphExecutionTables.CanonicalWritePayload(writer, data.Tables);
        }

        internal static void WriteTables(CanonicalWriter writer, Float32GraphExecutionData data)
        {
            WriteGraphSourceRevisions(writer, data.GraphSourceRevisions);
            GraphExecutionChunksCodec.Write(writer, data.Chunks);
        }

        internal static Float32GraphExecutionData ReadTables(CanonicalReader envelope)
        {
            IReadOnlyList<ProgramGraphSourceRevision> graphSourceRevisions = ReadGraphSourceRevisions(envelope);
            GraphExecutionChunks chunks = GraphExecutionChunksCodec.Read(envelope);
            envelope.RequireComplete();
            var reader = chunks.Structure.CreateReader();
            var parameterReader = chunks.Parameters.CreateReader();
            if (CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()) != ContractIdentity ||
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()) != GraphNodeSemanticContract.Identity)
                throw new InvalidDataException("Float32 graph structure contract is unsupported.");
            if (parameterReader.ReadHash() != chunks.Structure.Hash)
                throw new InvalidDataException("Float32 graph parameters or diagnostic sources use a different structure.");
            Float32GraphExecutionTables.CanonicalRequireSchema(reader);
            Float32GraphExecutionTables tables = Float32GraphExecutionTables.CanonicalReadMaterialized(reader, parameterReader);
            reader.RequireComplete();
            parameterReader.RequireComplete();
            ProgramExecutionSourceLayout executionSources = ProgramExecutionSourceLayout.FromChunk(tables.Structure.ExecutionSources);
            ProgramGraphSources graphSources = ProgramGraphSources.Read(executionSources, chunks.Sources, graphSourceRevisions);
            var data = new Float32GraphExecutionData(tables, graphSources);
            data.m_Chunks = chunks;
            return data;
        }

    }
}
