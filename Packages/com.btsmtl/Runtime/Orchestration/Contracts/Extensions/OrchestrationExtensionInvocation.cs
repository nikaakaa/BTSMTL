using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExtensionInputRows<TValue> where TValue : struct
    {
        public OrchestrationExtensionInputRows(ReadOnlySpan<TValue> values, int rowWidth,
            int count, int originalCount)
        {
            Values = values;
            RowWidth = rowWidth;
            Count = count;
            OriginalCount = originalCount;
        }

        public ReadOnlySpan<TValue> Values { get; }
        public int RowWidth { get; }
        public int Count { get; }
        public int OriginalCount { get; }

        public ReadOnlySpan<TValue> Row(int index) => Values.Slice(index * RowWidth, RowWidth);

        public ref readonly TValue Read(in OrchestrationExtensionInputPort port, int rowIndex = 0) =>
            ref Values[rowIndex * RowWidth + port.Index];
    }

    public readonly ref struct OrchestrationExtensionInvocation<TValue> where TValue : struct
    {
        public OrchestrationExtensionInvocation(OrchestrationExtensionInvocationKind kind,
            int operationIndex, ulong operationGeneration, ulong tick, uint stopCause,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<TValue> inputs, Span<TValue> outputs,
            in OrchestrationTimelineInvocationFacts timeline)
            : this(kind, operationIndex, operationGeneration, tick, stopCause, stateAncestors,
                new OrchestrationExtensionInputRows<TValue>(inputs, inputs.Length, 1, 1), outputs, in timeline)
        {
        }

        public OrchestrationExtensionInvocation(OrchestrationExtensionInvocationKind kind,
            int operationIndex, ulong operationGeneration, ulong tick, uint stopCause,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            OrchestrationExtensionInputRows<TValue> inputRows, Span<TValue> outputs,
            in OrchestrationTimelineInvocationFacts timeline)
        {
            Kind = kind;
            OperationIndex = operationIndex;
            OperationGeneration = operationGeneration;
            Tick = tick;
            StopCause = stopCause;
            StateAncestors = stateAncestors;
            InputRows = inputRows;
            Outputs = outputs;
            Timeline = timeline;
        }

        public OrchestrationExtensionInvocationKind Kind { get; }
        public int OperationIndex { get; }
        public ulong OperationGeneration { get; }
        public ulong Tick { get; }
        public uint StopCause { get; }
        public ReadOnlySpan<OrchestrationStateAncestor> StateAncestors { get; }
        public OrchestrationExtensionInputRows<TValue> InputRows { get; }
        public ReadOnlySpan<TValue> Inputs => InputRows.Values;
        public Span<TValue> Outputs { get; }
        public readonly OrchestrationTimelineInvocationFacts Timeline;

        public ref readonly TValue ReadInput(in OrchestrationExtensionInputPort port, int rowIndex = 0) =>
            ref InputRows.Read(in port, rowIndex);

        public void WriteOutput(in OrchestrationExtensionOutputPort port, in TValue value) =>
            Outputs[port.Index] = value;
    }
}
