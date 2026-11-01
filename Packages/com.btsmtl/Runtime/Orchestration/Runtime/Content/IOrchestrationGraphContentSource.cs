namespace BTSMTL.Orchestration
{
    public interface IOrchestrationGraphContentSource
    {
        GraphExecutionIdentity Identity { get; }
        OrchestrationGraphContent<TValue> Load<TValue>(IOrchestrationGraphContentEncoding<TValue> encoding)
            where TValue : unmanaged;
    }
}
