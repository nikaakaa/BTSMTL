using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public interface IOrchestrationGraphContentEncoding<TValue> where TValue : unmanaged
    {
        string EncodingContract { get; }
        byte[] WriteIdentity(GraphExecutionIdentity identity);
        GraphExecutionIdentity ReadIdentity(byte[] bytes);
        byte[] Write(OrchestrationGraphContent<TValue> content);
        OrchestrationGraphContent<TValue> Read(byte[] bytes, GraphExecutionIdentity expected);
    }
}
