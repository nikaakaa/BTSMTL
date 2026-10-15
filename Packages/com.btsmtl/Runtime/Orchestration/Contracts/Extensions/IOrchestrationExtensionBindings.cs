namespace BTSMTL.Orchestration
{
    public interface IOrchestrationExtensionBindings<TValue, TContext>
        where TValue : struct
        where TContext : struct
    {
        int Count { get; }
        ref readonly OrchestrationExtensionContract Contract(int bindingIndex);

        bool ShouldExecute(int bindingIndex, in TContext context);

        OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in TContext context,
            in OrchestrationExtensionInvocation<TValue> invocation);

        OrchestrationExtensionResult InvokeCandidate(int bindingIndex, ref TContext context,
            in OrchestrationExtensionInvocation<TValue> invocation);
    }
}
