using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExtensionInvocation<TValue> where TValue : struct
    {
        public OrchestrationExtensionInvocation(OrchestrationExtensionInvocationKind kind,
            int operationIndex, ulong tick, uint stopCause,
            ReadOnlySpan<TValue> inputs, Span<TValue> outputs)
        {
            Kind = kind;
            OperationIndex = operationIndex;
            Tick = tick;
            StopCause = stopCause;
            Inputs = inputs;
            Outputs = outputs;
        }

        public OrchestrationExtensionInvocationKind Kind { get; }
        public int OperationIndex { get; }
        public ulong Tick { get; }
        public uint StopCause { get; }
        public ReadOnlySpan<TValue> Inputs { get; }
        public Span<TValue> Outputs { get; }
    }
}
