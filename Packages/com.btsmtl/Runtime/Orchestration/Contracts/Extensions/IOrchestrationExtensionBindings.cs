namespace BTSMTL.Orchestration
{
    public interface IOrchestrationExtensionBindings<TValue>
        where TValue : struct
    {
        int Count { get; }
        ref readonly OrchestrationExtensionContract Contract(int bindingIndex);

        OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in OrchestrationReadContextView context,
            in OrchestrationExtensionInvocation<TValue> invocation);

        OrchestrationExtensionResult InvokeCandidate(int bindingIndex, in OrchestrationCandidateContextView context,
            in OrchestrationExtensionInvocation<TValue> invocation);
    }
}
