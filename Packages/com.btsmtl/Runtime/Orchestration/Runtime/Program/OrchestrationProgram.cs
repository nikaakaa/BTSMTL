using System;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationProgram<TValue> where TValue : unmanaged
    {
        readonly OrchestrationProgramContent<TValue> m_Content;
        ReadOnlySpan<OrchestrationOperation> m_Operations => m_Content.Operations.Span;
        ReadOnlySpan<OrchestrationFlowEdge> m_Edges => m_Content.Edges.Span;
        ReadOnlySpan<OrchestrationValueInputBinding> m_Inputs => m_Content.ValueInputs.Span;
        ReadOnlySpan<TValue> m_Constants => m_Content.Constants.Span;
        ReadOnlySpan<TValue> m_DefaultValues => m_Content.DefaultValues.Span;
        ReadOnlySpan<OrchestrationGraphCall> m_GraphCalls => m_Content.GraphCalls.Span;
        ReadOnlySpan<OrchestrationHookBinding> m_Hooks => m_Content.Hooks.Span;
        ReadOnlySpan<OrchestrationHookInput> m_HookInputs => m_Content.HookInputs.Span;
        ReadOnlySpan<OrchestrationBlackboardSlot> m_Blackboards => m_Content.BlackboardSlots.Span;
        ReadOnlySpan<OrchestrationBlackboardOutputBinding> m_BlackboardOutputs => m_Content.BlackboardOutputs.Span;
        ReadOnlySpan<OrchestrationOutputTemplate> m_OutputTemplates => m_Content.OutputTemplates.Span;
        OrchestrationProgramStructure Structure => m_Content.Structure;

        public OrchestrationProgram(OrchestrationProgramContent<TValue> content)
        {
            m_Content = content;
        }

        public OrchestrationProgramContent<TValue> Content => m_Content;
        public OrchestrationValueSchedule ValueSchedule => m_Content.ValueSchedule;
        public OrchestrationProgramStorageLayout ExecutionStorage => m_Content.ExecutionStorage;
        public int RootOperationIndex => m_Content.RootOperationIndex;
        public int MaximumOperationEvaluations => m_Content.MaximumOperationEvaluations;
        public OrchestrationCorePorts Ports => m_Content.CorePorts;
        public int OperationCount => m_Operations.Length;
        public int ValueSlotCount => m_DefaultValues.Length;
        public ReadOnlySpan<OrchestrationValueSlotType> ValueSlotTypes => m_Content.ValueSlotTypes.Span;
        public ReadOnlySpan<int> ValueKindSlotCounts => m_Content.ValueKindSlotCounts.Span;
        public int InputBindingCount => m_Inputs.Length;
        public int BlackboardCount => m_Blackboards.Length;
        public int EdgeCount => m_Edges.Length;
        public int ValueEvaluationSlotCount => Structure.ValueEvaluationOffsets.Span[m_Operations.Length];
        public int InitialStateEntry(int machineOperationIndex) => Structure.InitialStateEntries.Span[machineOperationIndex];
        public int AnyStateEntry(int machineOperationIndex) => Structure.AnyStateEntries.Span[machineOperationIndex];
        public int StateOnEnterEdge(int stateOperationIndex) => Structure.StateOnEnterEdges.Span[stateOperationIndex];
        public int StateRootEdge(int stateOperationIndex) => Structure.StateRootEdges.Span[stateOperationIndex];
        public int StateOnExitEdge(int stateOperationIndex) => Structure.StateOnExitEdges.Span[stateOperationIndex];
        public int StateOperationIndex(int stateIdentityId) =>
            Structure.StateOperationIndexes.Span[stateIdentityId];
        public int ValueEvaluationSlot(int operationIndex, int outputPortIndex) =>
            Structure.ValueEvaluationOffsets.Span[operationIndex] + outputPortIndex + 1;
        public ref readonly OrchestrationOperation Operation(int operationIndex) => ref m_Operations[operationIndex];
        public ref readonly OrchestrationOperationColdData ColdOperation(int operationIndex) =>
            ref m_Content.ColdOperations.Span[operationIndex];
        public T Payload<T>(int operationIndex) where T : unmanaged =>
            m_Content.ReadOperationPayload<T>(operationIndex);
        public ref readonly OrchestrationValueOperation ValueOperation(int operationIndex, int outputPortIndex = -1) =>
            ref ValueSchedule.ValueOperations.Span[ValueEvaluationSlot(operationIndex, outputPortIndex)];
        public ref readonly OrchestrationFlowEdge Edge(int edgeIndex) => ref m_Edges[edgeIndex];
        public ref readonly TValue Constant(int constantIndex) => ref m_Constants[constantIndex];
        public ref readonly TValue DefaultValue(int valueSlot) => ref m_DefaultValues[valueSlot];
        public ref readonly OrchestrationBlackboardSlot Blackboard(int valueSlot) => ref m_Blackboards[valueSlot];
        public OrchestrationGraphCall GraphCall(int graphCallIndex) => m_GraphCalls[graphCallIndex];
        public ref readonly OrchestrationOutputTemplate OutputTemplate(int templateIndex) => ref m_OutputTemplates[templateIndex];

        public ReadOnlySpan<int> Outgoing(int operationIndex, OrchestrationFlowKind kind)
        {
            int segment = operationIndex * OrchestrationProgramStructure.FlowKindCount + (int)kind - 1;
            ReadOnlySpan<int> offsets = Structure.OutgoingOffsets.Span;
            int start = offsets[segment];
            return Structure.OutgoingEdgeIndexes.Span.Slice(start, offsets[segment + 1] - start);
        }

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

        public ReadOnlySpan<OrchestrationHookInput> HookInputs(in OrchestrationHookBinding hook) =>
            m_HookInputs.Slice(hook.Inputs.Offset, hook.Inputs.Count);

        public ReadOnlySpan<OrchestrationBlackboardOutputBinding> OutputAssignments(int operationIndex)
        {
            OrchestrationIndexRange range = Payload<OrchestrationExtensionPayload>(operationIndex).OutputAssignments;
            return m_BlackboardOutputs.Slice(range.Offset, range.Count);
        }
    }
}
