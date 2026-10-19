namespace BTSMTL.Orchestration
{
    public interface IOrchestrationTimelineClipExitDispatcher<TContext> where TContext : struct
    {
        OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref TContext context);

        // 当前门 Clip 请求进入其所在分段的下一分段，由下一次推进执行跳转。
        OrchestrationOperationResult RequestSectionContinue(OrchestrationStateView candidate,
            int operationIndex, ulong tick, ref TContext context);
    }
}
