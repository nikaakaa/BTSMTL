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

    }

    public readonly ref struct OrchestrationExtensionArguments<TValue> where TValue : struct
    {
        public OrchestrationExtensionArguments(OrchestrationExtensionInvocationKind kind,
            int operationIndex, ulong operationGeneration, ulong tick, uint stopCause,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<OrchestrationExecutionFrameState> executionFrame,
            ReadOnlySpan<TValue> inputs, Span<TValue> outputs,
            in OrchestrationTimelineInvocationFacts timeline)
            : this(kind, operationIndex, operationGeneration, tick, stopCause, stateAncestors, executionFrame,
                new OrchestrationExtensionInputRows<TValue>(inputs, inputs.Length, 1, 1), outputs, in timeline)
        {
        }

        public OrchestrationExtensionArguments(OrchestrationExtensionInvocationKind kind,
            int operationIndex, ulong operationGeneration, ulong tick, uint stopCause,
            ReadOnlySpan<OrchestrationStateAncestor> stateAncestors,
            ReadOnlySpan<OrchestrationExecutionFrameState> executionFrame,
            OrchestrationExtensionInputRows<TValue> inputRows, Span<TValue> outputs,
            in OrchestrationTimelineInvocationFacts timeline)
        {
            Kind = kind;
            OperationIndex = operationIndex;
            OperationGeneration = operationGeneration;
            Tick = tick;
            StopCause = stopCause;
            StateAncestors = stateAncestors;
            ExecutionFrame = executionFrame;
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
        public ReadOnlySpan<OrchestrationExecutionFrameState> ExecutionFrame { get; }
        public OrchestrationExtensionInputRows<TValue> InputRows { get; }
        public ReadOnlySpan<TValue> Inputs => InputRows.Values;
        public Span<TValue> Outputs { get; }
        public readonly OrchestrationTimelineInvocationFacts Timeline;

    }

    public readonly ref struct OrchestrationExtensionInputRows<TValue, TOperation>
        where TValue : struct
        where TOperation : struct
    {
        readonly OrchestrationExtensionInputRows<TValue> m_Rows;

        internal OrchestrationExtensionInputRows(OrchestrationExtensionInputRows<TValue> rows)
        {
            m_Rows = rows;
        }

        public int RowWidth => m_Rows.RowWidth;
        public int Count => m_Rows.Count;
        public int OriginalCount => m_Rows.OriginalCount;

        public ref readonly TValue Read(in OrchestrationExtensionInputPort<TOperation> port, int rowIndex = 0) =>
            ref m_Rows.Values[rowIndex * m_Rows.RowWidth + port.Index];
    }

    public readonly ref struct OrchestrationExtensionInvocation<TValue, TOperation>
        where TValue : struct
        where TOperation : struct
    {
        readonly Span<TValue> m_Outputs;
        readonly ReadOnlySpan<OrchestrationExecutionFrameState> m_ExecutionFrame;

        internal OrchestrationExtensionInvocation(in OrchestrationExtensionArguments<TValue> arguments)
        {
            Kind = arguments.Kind;
            OperationIndex = arguments.OperationIndex;
            OperationGeneration = arguments.OperationGeneration;
            Tick = arguments.Tick;
            StopCause = arguments.StopCause;
            StateAncestors = arguments.StateAncestors;
            InputRows = new OrchestrationExtensionInputRows<TValue, TOperation>(arguments.InputRows);
            m_Outputs = arguments.Outputs;
            m_ExecutionFrame = arguments.ExecutionFrame;
            Timeline = arguments.Timeline;
        }

        public OrchestrationExtensionInvocationKind Kind { get; }
        public int OperationIndex { get; }
        public ulong OperationGeneration { get; }
        public ulong Tick { get; }
        public uint StopCause { get; }
        public ReadOnlySpan<OrchestrationStateAncestor> StateAncestors { get; }
        public OrchestrationExtensionInputRows<TValue, TOperation> InputRows { get; }
        public ref readonly OrchestrationExecutionFrameState ExecutionFrame => ref m_ExecutionFrame[0];
        public readonly OrchestrationTimelineInvocationFacts Timeline;

        public ref readonly TValue ReadInput(in OrchestrationExtensionInputPort<TOperation> port, int rowIndex = 0) =>
            ref InputRows.Read(in port, rowIndex);

        public void WriteOutput(in OrchestrationExtensionOutputPort<TOperation> port, in TValue value) =>
            m_Outputs[port.Index] = value;
    }
}
