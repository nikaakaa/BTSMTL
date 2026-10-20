namespace BTSMTL.Orchestration
{
    public interface IOrchestrationTimelineClipExitDispatcher<TContext> where TContext : struct
    {
        OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref TContext context);
    }
}
