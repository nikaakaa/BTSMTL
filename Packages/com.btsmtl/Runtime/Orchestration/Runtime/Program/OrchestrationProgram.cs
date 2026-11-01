using System;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationProgram<TValue> where TValue : unmanaged
    {
        readonly OrchestrationProgramContent m_Content;
        readonly OrchestrationGraphParameters<TValue> m_Parameters;
        ReadOnlySpan<OrchestrationOperation> m_Operations => m_Content.Operations.Span;
        ReadOnlySpan<OrchestrationFlowEdge> m_Edges => m_Content.Edges.Span;
        ReadOnlySpan<OrchestrationValueInputBinding> m_Inputs => m_Content.ValueInputs.Span;
        ReadOnlySpan<OrchestrationHookBinding> m_Hooks => m_Content.Hooks.Span;
        ReadOnlySpan<OrchestrationBlackboardSlot> m_Blackboards => m_Content.BlackboardSlots.Span;
        ReadOnlySpan<OrchestrationBlackboardOutputBinding> m_BlackboardOutputs => m_Content.BlackboardOutputs.Span;
        ReadOnlySpan<OrchestrationOutputTemplate> m_OutputTemplates => m_Content.OutputTemplates.Span;
        OrchestrationProgramStructure Structure => m_Content.Structure;

        readonly byte[] m_Image;

        public OrchestrationProgram(OrchestrationProgramContent content, OrchestrationGraphParameters<TValue> parameters)
        {
            m_Content = content;
            m_Parameters = parameters;
            m_Image = OrchestrationProgramImage.Build(content, parameters);
        }

        public OrchestrationProgramContent Content => m_Content;
        public OrchestrationGraphParameters<TValue> Parameters => m_Parameters;
        public ReadOnlyMemory<byte> Image => m_Image;
        public OrchestrationProgramView<TValue> View => new OrchestrationProgramView<TValue>(m_Image);
        public OrchestrationProgramStorageLayout ExecutionStorage => m_Content.ExecutionStorage;
        public int RootOperationIndex => m_Content.RootOperationIndex;
        public OrchestrationCorePorts Ports => m_Content.CorePorts;
        public int OperationCount => m_Operations.Length;
        public int ValueSlotCount => m_Parameters.DefaultValues.Length;
        public int InputBindingCount => m_Inputs.Length;
        public int BlackboardCount => m_Blackboards.Length;
        public int EdgeCount => m_Edges.Length;
        public int ValueEvaluationSlotCount => Structure.ValueEvaluationOffsets.Span[m_Operations.Length];
        public int InitialStateEntry(int machineOperationIndex) => Structure.InitialStateEntries.Span[machineOperationIndex];
        public int AnyStateEntry(int machineOperationIndex) => Structure.AnyStateEntries.Span[machineOperationIndex];
        public int StateOnEnterEdge(int stateOperationIndex) => Structure.StateOnEnterEdges.Span[stateOperationIndex];
        public int StateRootEdge(int stateOperationIndex) => Structure.StateRootEdges.Span[stateOperationIndex];
        public int StateOnExitEdge(int stateOperationIndex) => Structure.StateOnExitEdges.Span[stateOperationIndex];
        public int ValueEvaluationSlot(int operationIndex, int outputPortIndex) =>
            Structure.ValueEvaluationOffsets.Span[operationIndex] + outputPortIndex + 1;
        public ref readonly OrchestrationOperation Operation(int operationIndex) => ref m_Operations[operationIndex];
        public ref readonly OrchestrationOperationColdData ColdOperation(int operationIndex) =>
            ref m_Content.ColdOperations.Span[operationIndex];
        public T Payload<T>(int operationIndex) where T : unmanaged =>
            m_Parameters.ReadOperationPayload<T>(m_Content, operationIndex);
        public ref readonly OrchestrationValueOperation ValueOperation(int operationIndex, int outputPortIndex = -1) =>
            ref m_Content.ValueSchedule.ValueOperations.Span[ValueEvaluationSlot(operationIndex, outputPortIndex)];
        public ref readonly OrchestrationFlowEdge Edge(int edgeIndex) => ref m_Edges[edgeIndex];
        public ref readonly TValue Constant(int constantIndex) => ref m_Parameters.Constants.Span[constantIndex];
        public ref readonly TValue DefaultValue(int valueSlot) => ref m_Parameters.DefaultValues.Span[valueSlot];
        public ref readonly OrchestrationBlackboardSlot Blackboard(int valueSlot) => ref m_Blackboards[valueSlot];
        public ref readonly OrchestrationOutputTemplate OutputTemplate(int templateIndex) => ref m_OutputTemplates[templateIndex];

        public ReadOnlySpan<int> Outgoing(int operationIndex, OrchestrationFlowKind kind) =>
            Structure.Outgoing(operationIndex, kind);

        public ReadOnlySpan<OrchestrationValueInputBinding> Inputs(int operationIndex)
        {
            OrchestrationIndexRange range = m_Operations[operationIndex].Inputs;
            return m_Inputs.Slice(range.Offset, range.Count);
        }

        public ReadOnlySpan<OrchestrationHookBinding> Hooks(int operationIndex)
        {
            OrchestrationIndexRange range = m_Operations[operationIndex].LifecycleHooks;
            return m_Hooks.Slice(range.Offset, range.Count);
        }

        public ReadOnlySpan<OrchestrationBlackboardOutputBinding> OutputAssignments(int operationIndex)
        {
            OrchestrationIndexRange range = Payload<OrchestrationExtensionPayload>(operationIndex).OutputAssignments;
            return m_BlackboardOutputs.Slice(range.Offset, range.Count);
        }
    }
}
