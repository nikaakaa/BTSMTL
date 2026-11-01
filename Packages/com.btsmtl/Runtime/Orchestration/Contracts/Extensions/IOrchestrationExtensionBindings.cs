namespace BTSMTL.Orchestration
{
    public interface IOrchestrationExtensionBindings<TValue, TContext>
        where TValue : struct
        where TContext : struct
    {
        int Count { get; }
        OrchestrationExtensionExecutionContract ExecutionContract(int bindingIndex);

        OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in TContext context,
            in OrchestrationExtensionArguments<TValue> invocation);

        OrchestrationExtensionResult InvokeCandidate(int bindingIndex, ref TContext context,
            in OrchestrationExtensionArguments<TValue> invocation);
    }
}
