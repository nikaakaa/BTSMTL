namespace BTSMTL.Orchestration
{
    public interface IOrchestrationExtensionBindings<TValue, TReadContext, TCandidateContext>
        where TValue : struct
        where TReadContext : struct
        where TCandidateContext : struct
    {
        int Count { get; }
        ref readonly OrchestrationExtensionContract Contract(int bindingIndex);

        OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in TReadContext context,
            in OrchestrationExtensionInvocation<TValue> invocation);

        OrchestrationExtensionResult InvokeCandidate(int bindingIndex, ref TCandidateContext context,
            in OrchestrationExtensionInvocation<TValue> invocation);
    }
}
