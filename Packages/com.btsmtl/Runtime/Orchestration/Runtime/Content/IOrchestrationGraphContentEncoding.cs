using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public interface IOrchestrationGraphContentEncoding<TValue> where TValue : unmanaged
    {
        string EncodingContract { get; }
        StableHash EncodingIdentity { get; }
        byte[] WriteIdentity(GraphExecutionIdentity identity);
        GraphExecutionIdentity ReadIdentity(byte[] bytes);
        OrchestrationGraphContent<TValue> Create(GraphCompilationIdentity source,
            OrchestrationGraphStructure structure, OrchestrationGraphParameters<TValue> parameters,
            OrchestrationGraphSourceMap sources, OrchestrationGraphContent<TValue> previous);
        OrchestrationGraphContentBlocks WriteBlocks(OrchestrationGraphContent<TValue> content);
        OrchestrationGraphContent<TValue> ReadBlocks(OrchestrationGraphContentBlocks blocks,
            GraphExecutionIdentity expected);
        byte[] Write(OrchestrationGraphContent<TValue> content);
        OrchestrationGraphContent<TValue> Read(byte[] bytes, GraphExecutionIdentity expected);
    }
}
