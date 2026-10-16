namespace ThirdPersonSimulation.Serialization
{
    public sealed class MemoryPackGraphCompilationPublisher : IGraphContentPublisher
    {
        static MemoryPackGraphCompilationPublisher()
        {
            MemoryPackContent.Initialize();
            MemoryPackGeneratedFormatters.Register();
        }

        public static MemoryPackGraphCompilationPublisher Instance { get; } = new MemoryPackGraphCompilationPublisher();

        public GraphExecutionChunk Publish(ProgramExecutionSourceLayout layout) =>
            new GraphExecutionChunk(MemoryPackContent.Serialize(layout.Entries));

        public GraphExecutionChunk Publish(ProgramGraphSources sources) =>
            new GraphExecutionChunk(MemoryPackContent.Serialize(sources.Diagnostics));

        public StableHash ComputeStateLayoutHash(SemanticStateLayout layout) =>
            StableHash.Compute(MemoryPackContent.Serialize(layout));

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
    }
}
