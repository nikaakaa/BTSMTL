using System;

namespace ThirdPersonSimulation
{
    public interface IGraphContentPublisher
    {
        GraphExecutionChunk Publish(ProgramExecutionSourceLayout layout);
        GraphExecutionChunk Publish(ProgramGraphSources sources);
        StableHash ComputeStateLayoutHash(SemanticStateLayout layout);
        GraphExecutionChunks Publish(GameplayAbilitySemanticIr graph, GraphExecutionChunks previous, GraphCompilationWork work);
        GraphExecutionChunks Publish<TTables>(TTables tables, ProgramGraphSources sources,
            GraphExecutionChunks previous, GraphCompilationWork work);
        StableHash ComputeContentHash(GraphExecutionChunks chunks, SemanticHash semanticHash,
            SimulationNumericProfile profile, int tickRate, string installationIdentity);
    }

    public enum GraphCompilationWork
    {
        Unchanged,
        Sources,
        Parameters,
        Structure
    }

    [ValueStorage]
    public sealed partial class GraphExecutionChunk
    {
        readonly byte[] m_Bytes;

        public GraphExecutionChunk(byte[] bytes)
        {
            m_Bytes = bytes;
            Hash = SimulationCanonicalPayloadHash.Compute(bytes);
        }

        public ReadOnlyMemory<byte> Bytes => m_Bytes;
        public StableHash Hash { get; }
    }

    public sealed class GraphExecutionChunks
    {
        public GraphExecutionChunks(GraphExecutionChunk structure, GraphExecutionChunk parameters,
            GraphExecutionChunk sources, StableHash executionHash, StableHash payloadHash)
        {
            Structure = structure;
            Parameters = parameters;
            Sources = sources;
            ExecutionHash = executionHash;
            PayloadHash = payloadHash;
        }

        public GraphExecutionChunk Structure { get; }
        public GraphExecutionChunk Parameters { get; }
        public GraphExecutionChunk Sources { get; }
        public StableHash ExecutionHash { get; }
        public StableHash PayloadHash { get; }
    }
}
