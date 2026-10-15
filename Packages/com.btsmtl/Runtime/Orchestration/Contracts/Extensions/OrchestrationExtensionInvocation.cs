using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExtensionInvocation<TValue> where TValue : struct
    {
        public OrchestrationExtensionInvocation(OrchestrationExtensionInvocationKind kind,
            int operationIndex, ulong operationGeneration, ulong tick, uint stopCause,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<TValue> inputs, Span<TValue> outputs)
        {
            Kind = kind;
            OperationIndex = operationIndex;
            OperationGeneration = operationGeneration;
            Tick = tick;
            StopCause = stopCause;
            StateAncestors = stateAncestors;
            Inputs = inputs;
            Outputs = outputs;
        }

        public OrchestrationExtensionInvocationKind Kind { get; }
        public int OperationIndex { get; }
        public ulong OperationGeneration { get; }
        public ulong Tick { get; }
        public uint StopCause { get; }
        public ReadOnlySpan<OrchestrationStateAncestor> StateAncestors { get; }
        public ReadOnlySpan<TValue> Inputs { get; }
        public Span<TValue> Outputs { get; }
    }
}
