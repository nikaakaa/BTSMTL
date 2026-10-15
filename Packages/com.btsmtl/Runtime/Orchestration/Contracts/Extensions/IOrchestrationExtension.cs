namespace BTSMTL.Orchestration
{
    public interface IOrchestrationExtension
    {
        OrchestrationExtensionIdentity Identity { get; }
        uint SemanticVersion { get; }
        OrchestrationExtensionContract Describe(int contentIndex);
    }

    public interface IReadOnlyOrchestrationExtension<TValue, TReadContext> : IOrchestrationExtension
        where TValue : struct
        where TReadContext : struct
    {
        OrchestrationExtensionResult Invoke(int contentIndex, in TReadContext context,
            in OrchestrationExtensionInvocation<TValue> invocation);
    }

    public interface ICandidateOrchestrationExtension<TValue, TCandidateContext> : IOrchestrationExtension
        where TValue : struct
        where TCandidateContext : struct
    {
        OrchestrationExtensionResult Invoke(int contentIndex, ref TCandidateContext context,
            in OrchestrationExtensionInvocation<TValue> invocation);
    }
}
