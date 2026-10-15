namespace BTSMTL.Orchestration
{
    public interface ICandidateOrchestrationExtensionExecutionGate<TCandidateContext>
        where TCandidateContext : struct
    {
        bool ShouldExecute(int contentIndex, in TCandidateContext context);
    }
}
