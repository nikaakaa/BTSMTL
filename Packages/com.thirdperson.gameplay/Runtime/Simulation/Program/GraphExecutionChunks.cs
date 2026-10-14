using System;
using System.IO;

namespace ThirdPersonSimulation
{
    public enum GraphCompilationWork
    {
        Unchanged,
        Sources,
        Parameters,
        Structure
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class GraphExecutionChunk
    {
        readonly byte[] m_Bytes;

        internal GraphExecutionChunk(byte[] bytes)
        {
            m_Bytes = bytes;
            Hash = SimulationCanonicalPayloadHash.Compute(bytes);
        }

        public ReadOnlyMemory<byte> Bytes => m_Bytes;
        public StableHash Hash { get; }

        [CanonicalValidation]
        void RequireContentHash()
        {
            if (Hash != SimulationCanonicalPayloadHash.Compute(m_Bytes))
                throw new InvalidDataException("Graph chunk content hash does not match its manifest.");
        }
        public CanonicalReader CreateReader()
        {
            var reader = new CanonicalReader(m_Bytes);
            reader.SetTextResolver(CanonicalInitializationTextResolver.Instance);
            return reader;
        }

        public static GraphExecutionChunk Encode(Action<CanonicalWriter> write)
        {
            using var writer = new CanonicalWriter();
            write(writer);
            return new GraphExecutionChunk(writer.ToArray());
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class GraphExecutionChunkSet
    {
        internal GraphExecutionChunkSet(GraphExecutionChunk structure, GraphExecutionChunk parameters,
            GraphExecutionChunk sources)
        {
            Structure = structure;
            Parameters = parameters;
            Sources = sources;
        }

        public GraphExecutionChunk Structure { get; }
        public GraphExecutionChunk Parameters { get; }
        public GraphExecutionChunk Sources { get; }
    }

    public sealed class GraphExecutionChunks
    {
        public GraphExecutionChunks(GraphExecutionChunk structure, GraphExecutionChunk parameters,
            GraphExecutionChunk sources)
            : this(new GraphExecutionChunkSet(structure, parameters, sources))
        {
        }

        internal GraphExecutionChunks(GraphExecutionChunkSet data)
        {
            Data = data;
            using var writer = new CanonicalWriter();
            writer.WriteText("graph-execution-chunks/1");
            writer.WriteHash(data.Structure.Hash);
            writer.WriteHash(data.Parameters.Hash);
            ExecutionHash = writer.ComputeHash();
            writer.WriteHash(data.Sources.Hash);
            PayloadHash = writer.ComputeHash();
        }

        internal GraphExecutionChunkSet Data { get; }
        public GraphExecutionChunk Structure => Data.Structure;
        public GraphExecutionChunk Parameters => Data.Parameters;
        public GraphExecutionChunk Sources => Data.Sources;
        public StableHash ExecutionHash { get; }
        public StableHash PayloadHash { get; }

        public StableHash ComputeContentHash(SemanticHash canonicalExecution, SimulationNumericProfile profile,
            int tickRate, string installationIdentity)
        {
            using var writer = new CanonicalWriter();
            writer.WriteText("graph-execution-content/2");
            writer.WriteHash(canonicalExecution);
            writer.WriteHash(ExecutionHash);
            SimulationNumericProfile.CanonicalWrite(writer, profile);
            writer.WriteInt32(tickRate);
            writer.WriteText(installationIdentity);
            return writer.ComputeHash();
        }
    }

    public static class GraphExecutionChunksCodec
    {
        public static void Write(CanonicalWriter writer, GraphExecutionChunks chunks) =>
            GraphExecutionChunkSet.CanonicalWrite(writer, chunks.Data);

        public static GraphExecutionChunks Read(CanonicalReader reader) =>
            new GraphExecutionChunks(GraphExecutionChunkSet.CanonicalReadMaterialized(reader));
    }
}
