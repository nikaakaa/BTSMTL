using System.Collections.Generic;

namespace ThirdPersonSimulation.Serialization
{
    public sealed partial class MemoryPackGraphContentPublisher : IGraphContentPublisher
    {
        public static MemoryPackGraphContentPublisher Instance { get; } = new MemoryPackGraphContentPublisher();

        public GraphExecutionChunk Publish(ProgramExecutionSourceLayout layout) =>
            new GraphExecutionChunk(MemoryPackContent.Serialize(layout.Entries));

        public GraphExecutionChunk Publish(ProgramGraphSources sources) =>
            new GraphExecutionChunk(MemoryPackContent.Serialize(sources.Diagnostics));

        public StableHash ComputeStateLayoutHash(SemanticStateLayout layout) =>
            SimulationCanonicalPayloadHash.Compute(MemoryPackContent.Serialize(layout));

        public GraphExecutionChunks Publish(GameplayAbilitySemanticIr graph, GraphExecutionChunks previous, GraphCompilationWork work)
        {
            if (work == GraphCompilationWork.Unchanged) return previous;
            var content = (graph.Manifest.Contract, graph.Tables);
            GraphExecutionChunk structure = work == GraphCompilationWork.Structure
                ? new GraphExecutionChunk(MemoryPackContent.Serialize(content, MemoryPackContentPart.Layout))
                : previous.Structure;
            GraphExecutionChunk parameters = work == GraphCompilationWork.Structure || work == GraphCompilationWork.Parameters
                ? new GraphExecutionChunk(MemoryPackContent.Serialize(content, MemoryPackContentPart.Payload))
                : previous.Parameters;
            var sources = new GraphExecutionChunk(MemoryPackContent.Serialize((graph.Manifest.SourceRevision,
                graph.GraphSourceRevisions.Sources, graph.GraphSources.DiagnosticChunk.Bytes)));
            StableHash executionHash = StableHash.Compute(structure.Hash.Value, parameters.Hash.Value);
            return new GraphExecutionChunks(structure, parameters, sources, executionHash,
                StableHash.Compute(executionHash.Value, sources.Hash.Value));
        }

        public IReadOnlyList<ProgramExecutionSource> ReadExecutionSources(GraphExecutionChunk chunk) =>
            MemoryPackContent.Deserialize<IReadOnlyList<ProgramExecutionSource>>(chunk.Bytes.Span);

        public byte[] WriteIdentity(GraphExecutionIdentity identity) => MemoryPackContent.Serialize(identity);

        public GraphExecutionIdentity ReadIdentity(byte[] bytes) => MemoryPackContent.Deserialize<GraphExecutionIdentity>(bytes);

        public ProgramGraphSources ReadSources(GraphExecutionChunk layout,
            IReadOnlyList<ProgramSourceMapEntry> diagnostics, IReadOnlyList<ProgramGraphSourceRevision> revisions) =>
            new ProgramGraphSources(new ProgramExecutionSourceLayout(ReadExecutionSources(layout), this),
                diagnostics, new ProgramGraphSourceRevisionTable(revisions));

        public (GameplayAbilitySemanticIrContract Contract, SemanticGraphTables Tables) ReadTables(GraphExecutionChunks chunks) =>
            MemoryPackContent.DeserializeParts<(GameplayAbilitySemanticIrContract, SemanticGraphTables)>(
                chunks.Structure.Bytes.Span, chunks.Parameters.Bytes.Span);

        public GraphExecutionChunks Publish<TTables>(TTables tables, ProgramGraphSources sources,
            GraphExecutionChunks previous, GraphCompilationWork work)
        {
            GraphExecutionChunk structure = work == GraphCompilationWork.Structure
                ? new GraphExecutionChunk(MemoryPackContent.Serialize(tables, MemoryPackContentPart.Layout))
                : previous.Structure;
            GraphExecutionChunk parameters = work == GraphCompilationWork.Structure || work == GraphCompilationWork.Parameters
                ? new GraphExecutionChunk(MemoryPackContent.Serialize(tables, MemoryPackContentPart.Payload))
                : previous.Parameters;
            GraphExecutionChunk diagnostics = sources.DiagnosticChunk;
            StableHash executionHash = StableHash.Compute(structure.Hash.Value, parameters.Hash.Value);
            return new GraphExecutionChunks(structure, parameters, diagnostics, executionHash,
                StableHash.Compute(executionHash.Value, diagnostics.Hash.Value));
        }

        public StableHash ComputeContentHash(GraphExecutionChunks chunks, SemanticHash semanticHash,
            SimulationNumericProfile profile, int tickRate, string installationIdentity) =>
            SimulationCanonicalPayloadHash.Compute(MemoryPackContent.Serialize(
                (semanticHash, chunks.ExecutionHash, profile, tickRate, installationIdentity)));
    }
}
