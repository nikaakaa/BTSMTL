using System;
using System.Collections.Generic;

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
        readonly int[][][] m_Outgoing;
        readonly int[] m_InitialEntries;
        readonly int[] m_AnyStateEntries;
        readonly int[] m_StateOnEnter;
        readonly int[] m_StateRoots;
        readonly int[] m_StateOnExit;
        readonly int[] m_ValueEvaluationOffsets;
        readonly int[] m_StateOperations;

        public OrchestrationProgram(OrchestrationProgramContent<TValue> content)
        {
            m_Content = content;
            int maximumStateIdentity = 0;
            for (int i = 0; i < m_Operations.Length; i++)
                if (m_Operations[i].Code == OrchestrationOperationCode.State ||
                    m_Operations[i].Code == OrchestrationOperationCode.StateExit)
                    maximumStateIdentity = Math.Max(maximumStateIdentity, m_Operations[i].StateIdentityId);
            m_StateOperations = new int[checked(maximumStateIdentity + 1)];
            Array.Fill(m_StateOperations, -1);
            Validate();
            m_Outgoing = BuildOutgoing();
            m_InitialEntries = EmptyOperationIndexes();
            m_AnyStateEntries = EmptyOperationIndexes();
            m_StateOnEnter = EmptyOperationIndexes();
            m_StateRoots = EmptyOperationIndexes();
            m_StateOnExit = EmptyOperationIndexes();
            m_ValueEvaluationOffsets = new int[m_Operations.Length + 1];
            BuildIndexes();
        }

        public OrchestrationProgramContent<TValue> Content => m_Content;
        public int RootOperationIndex => m_Content.RootOperationIndex;
        public int MaximumOperationEvaluations => m_Content.MaximumOperationEvaluations;
        public OrchestrationCorePorts Ports => m_Content.CorePorts;
        public int OperationCount => m_Operations.Length;
        public int ValueSlotCount => m_DefaultValues.Length;
        public int InputBindingCount => m_Inputs.Length;
        public int BlackboardCount => m_Blackboards.Length;
        public int ValueEvaluationSlotCount => m_ValueEvaluationOffsets[m_Operations.Length];
        public int InitialStateEntry(int machineOperationIndex) => m_InitialEntries[machineOperationIndex];
        public int AnyStateEntry(int machineOperationIndex) => m_AnyStateEntries[machineOperationIndex];
        public int StateOnEnterEdge(int stateOperationIndex) => m_StateOnEnter[stateOperationIndex];
        public int StateRootEdge(int stateOperationIndex) => m_StateRoots[stateOperationIndex];
        public int StateOnExitEdge(int stateOperationIndex) => m_StateOnExit[stateOperationIndex];
        public int StateOperationIndex(int stateIdentityId) =>
            m_StateOperations[stateIdentityId];
        public int ValueEvaluationSlot(int operationIndex, int outputPortIndex) =>
            m_ValueEvaluationOffsets[operationIndex] + outputPortIndex + 1;
        public ref readonly OrchestrationOperation Operation(int operationIndex) => ref m_Operations[operationIndex];
        public ref readonly OrchestrationFlowEdge Edge(int edgeIndex) => ref m_Edges[edgeIndex];
        public ref readonly TValue Constant(int constantIndex) => ref m_Constants[constantIndex];
        public ref readonly TValue DefaultValue(int valueSlot) => ref m_DefaultValues[valueSlot];
        public ref readonly OrchestrationBlackboardSlot Blackboard(int valueSlot) => ref m_Blackboards[valueSlot];
        public OrchestrationGraphCall GraphCall(int graphCallIndex) => m_GraphCalls[graphCallIndex];
        public ref readonly OrchestrationOutputTemplate OutputTemplate(int templateIndex) => ref m_OutputTemplates[templateIndex];

        public ReadOnlySpan<int> Outgoing(int operationIndex, OrchestrationFlowKind kind) =>
            m_Outgoing[operationIndex][(int)kind];

        public ReadOnlySpan<OrchestrationValueInputBinding> Inputs(int operationIndex)
        {
            OrchestrationRange range = m_Operations[operationIndex].Inputs;
            return m_Inputs.Slice(range.Offset, range.Count);
        }

        public ReadOnlySpan<OrchestrationHookBinding> Hooks(int operationIndex)
        {
            OrchestrationRange range = m_Operations[operationIndex].LifecycleHooks;
            return m_Hooks.Slice(range.Offset, range.Count);
        }

        public ReadOnlySpan<OrchestrationHookInput> HookInputs(in OrchestrationHookBinding hook) =>
            m_HookInputs.Slice(hook.Inputs.Offset, hook.Inputs.Count);

        public ReadOnlySpan<OrchestrationBlackboardOutputBinding> OutputAssignments(int operationIndex)
        {
            OrchestrationRange range = m_Operations[operationIndex].OutputAssignments;
            return m_BlackboardOutputs.Slice(range.Offset, range.Count);
        }

        void Validate()
        {
            RequireOperationIndex(RootOperationIndex);
            if (m_Operations[RootOperationIndex].Code != OrchestrationOperationCode.Root)
                throw new ArgumentException("The orchestration root is not a Root operation.");
            if (m_Operations[RootOperationIndex].ExecutionFrameOwnerOperationIndex != RootOperationIndex)
                throw new ArgumentException("The orchestration root must own its execution frame.");
            if (MaximumOperationEvaluations <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaximumOperationEvaluations));
            if (m_Blackboards.Length != m_DefaultValues.Length)
                throw new ArgumentException("Blackboard slot descriptors must use the value-slot address space.");
            for (int i = 0; i < m_Operations.Length; i++)
            {
                ref readonly OrchestrationOperation operation = ref m_Operations[i];
                if (!Enum.IsDefined(typeof(OrchestrationOperationCode), operation.Code))
                    throw new ArgumentException($"Operation '{i}' has an unsupported core code.");
                RequireExecutionFrameOwner(operation.ExecutionFrameOwnerOperationIndex);
                RequireRange(operation.Inputs, m_Inputs.Length);
                RequireRange(operation.LifecycleHooks, m_Hooks.Length);
                RequireRange(operation.OutputAssignments, m_BlackboardOutputs.Length);
                foreach (ref readonly OrchestrationBlackboardOutputBinding assignment in OutputAssignments(i))
                {
                    if (assignment.SourceOutputPortIndex < 0 || assignment.SourceOutputPortIndex >= operation.OutputCount ||
                        assignment.ValueSlot < 0 || assignment.ValueSlot >= m_Blackboards.Length ||
                        m_Blackboards[assignment.ValueSlot].ScopeKind == 0)
                        throw new ArgumentException($"Operation '{i}' has an invalid Blackboard output assignment.");
                }
                if (operation.OutputCount < 0)
                    throw new ArgumentException($"Operation '{i}' has a negative output count.");
                if (operation.OutputSlotOffset >= 0)
                    RequireRange(new OrchestrationRange(operation.OutputSlotOffset, operation.OutputCount), m_DefaultValues.Length);
                if (operation.ConstantIndex >= 0 && operation.ConstantIndex >= m_Constants.Length)
                    throw new ArgumentException($"Operation '{i}' references an unknown constant.");
                if (operation.ValueSlot >= 0 && operation.ValueSlot >= m_DefaultValues.Length)
                    throw new ArgumentException($"Operation '{i}' references an unknown value slot.");
                if (operation.Code == OrchestrationOperationCode.BlackboardGet ||
                    operation.Code == OrchestrationOperationCode.BlackboardSet)
                {
                    if (operation.ValueSlot < 0 || m_Blackboards[operation.ValueSlot].ScopeKind == 0)
                        throw new ArgumentException($"Blackboard operation '{i}' has no slot descriptor.");
                }
                if (operation.Code == OrchestrationOperationCode.EmitOutput)
                {
                    if ((uint)operation.Integer0 >= (uint)m_OutputTemplates.Length)
                        throw new ArgumentException($"Output operation '{i}' references an unknown output template.");
                    ReadOnlySpan<OrchestrationOutputPort> ports = m_OutputTemplates[operation.Integer0].Ports.Span;
                    ReadOnlySpan<OrchestrationValueInputBinding> inputs = Inputs(i);
                    if (inputs.Length != ports.Length)
                        throw new ArgumentException($"Output operation '{i}' does not supply every template port.");
                    for (int portIndex = 0; portIndex < ports.Length; portIndex++)
                        if (inputs[portIndex].TargetPortIndex != portIndex || inputs[portIndex].ValueKind != ports[portIndex].ValueKind)
                            throw new ArgumentException($"Output operation '{i}' has an invalid input for template port '{ports[portIndex].Id}'.");
                }
                if (operation.ParentStateOperationIndex >= 0)
                {
                    RequireOperationIndex(operation.ParentStateOperationIndex);
                    if (m_Operations[operation.ParentStateOperationIndex].Code != OrchestrationOperationCode.State)
                        throw new ArgumentException($"Operation '{i}' has an invalid parent State.");
                }
                if (operation.Code == OrchestrationOperationCode.State || operation.Code == OrchestrationOperationCode.StateExit)
                {
                    if (operation.StateIdentityId <= 0)
                        throw new ArgumentException($"State operation '{i}' has no published identity.");
                    if (m_StateOperations[operation.StateIdentityId] >= 0)
                        throw new ArgumentException($"State identity '{operation.StateIdentityId}' is declared twice.");
                    m_StateOperations[operation.StateIdentityId] = i;
                    if (operation.Code == OrchestrationOperationCode.State)
                    {
                        RequireOperationIndex(operation.StateMachineOwnerOperationIndex);
                        if (m_Operations[operation.StateMachineOwnerOperationIndex].Code != OrchestrationOperationCode.StateMachine)
                            throw new ArgumentException($"State operation '{i}' has no StateMachine owner.");
                        if (m_Operations[operation.StateMachineOwnerOperationIndex].ExecutionFrameOwnerOperationIndex !=
                            operation.ExecutionFrameOwnerOperationIndex)
                            throw new ArgumentException($"State operation '{i}' crosses execution-frame ownership of its StateMachine.");
                    }
                }
                if (operation.Code == OrchestrationOperationCode.SubGraph)
                {
                    if (operation.GraphCallIndex < 0 || operation.GraphCallIndex >= m_GraphCalls.Length)
                        throw new ArgumentException($"SubGraph operation '{i}' has no graph call.");
                    OrchestrationGraphCall call = m_GraphCalls[operation.GraphCallIndex];
                    RequireOperationIndex(call.EntryOperationIndex);
                    if (call.OwnerOperationIndex != i || m_Operations[call.EntryOperationIndex].Code != OrchestrationOperationCode.Root)
                        throw new ArgumentException($"SubGraph operation '{i}' has an invalid graph call entry.");
                    if (m_Operations[call.EntryOperationIndex].ExecutionFrameOwnerOperationIndex != operation.ExecutionFrameOwnerOperationIndex)
                        throw new ArgumentException($"SubGraph operation '{i}' crosses execution-frame ownership of its entry.");
                    ValidateParameters(call.Inputs.Span, operation.ExecutionFrameOwnerOperationIndex);
                    ValidateParameters(call.Outputs.Span, operation.ExecutionFrameOwnerOperationIndex);
                }
            }
            for (int i = 0; i < m_Edges.Length; i++)
            {
                ref readonly OrchestrationFlowEdge edge = ref m_Edges[i];
                RequireOperationIndex(edge.SourceOperationIndex);
                RequireOperationIndex(edge.TargetOperationIndex);
                if (m_Operations[edge.SourceOperationIndex].ExecutionFrameOwnerOperationIndex !=
                    m_Operations[edge.TargetOperationIndex].ExecutionFrameOwnerOperationIndex)
                    throw new ArgumentException($"Control edge '{edge.IdentityId}' crosses execution-frame ownership.");
                if (!Enum.IsDefined(typeof(OrchestrationFlowKind), edge.Kind))
                    throw new ArgumentException($"Edge '{edge.IdentityId}' has an unsupported control kind.");
                if (edge.HasCondition)
                    RequireOperationIndex(edge.ConditionOperationIndex);
            }
            for (int i = 0; i < m_Inputs.Length; i++)
            {
                ref readonly OrchestrationValueInputBinding input = ref m_Inputs[i];
                if (input.Source == OrchestrationValueInputSource.Operation)
                {
                    RequireOperationIndex(input.SourceIndex);
                    if (input.SourceOutputPortIndex < 0 || input.SourceOutputPortIndex >= m_Operations[input.SourceIndex].OutputCount)
                        throw new ArgumentException("A value edge references an unknown output port.");
                }
                else if (input.Source == OrchestrationValueInputSource.Constant)
                {
                    if (input.SourceIndex < 0 || input.SourceIndex >= m_Constants.Length)
                        throw new ArgumentException("A value input references an unknown constant.");
                }
                else if (input.Source == OrchestrationValueInputSource.Blackboard)
                {
                    if (input.SourceIndex < 0 || input.SourceIndex >= m_Blackboards.Length ||
                        m_Blackboards[input.SourceIndex].ScopeKind == 0)
                        throw new ArgumentException("A value input references an unknown Blackboard slot.");
                }
                else if (input.Source != OrchestrationValueInputSource.StateCompletionTransition)
                    throw new ArgumentException("A value input has an unsupported source kind.");
            }
            for (int i = 0; i < m_Hooks.Length; i++)
                RequireRange(m_Hooks[i].Inputs, m_HookInputs.Length);
            for (int i = 0; i < m_Blackboards.Length; i++)
            {
                ref readonly OrchestrationBlackboardSlot slot = ref m_Blackboards[i];
                if (slot.ScopeKind != 0)
                    RequireExecutionFrameOwner(slot.ExecutionFrameOwnerOperationIndex);
                if ((slot.ScopeKind == OrchestrationScopeKind.Graph &&
                     !(slot.Lifetime == OrchestrationBlackboardLifetime.GraphInstance && slot.OwnerOperationIndex < 0)) ||
                    slot.ScopeKind == OrchestrationScopeKind.State)
                    RequireOperationIndex(slot.OwnerOperationIndex);
            }
            for (int i = 0; i < m_Operations.Length; i++)
            {
                int parent = m_Operations[i].ParentStateOperationIndex;
                int count = 0;
                while (parent >= 0)
                {
                    if (++count > m_Operations.Length)
                        throw new ArgumentException($"Operation '{i}' has a cyclic parent State path.");
                    parent = m_Operations[parent].ParentStateOperationIndex;
                }
            }
        }

        void ValidateParameters(ReadOnlySpan<OrchestrationGraphParameter> parameters, int executionFrameOwnerOperationIndex)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ValueSlot < 0 || parameters[i].ValueSlot >= m_DefaultValues.Length)
                    throw new ArgumentException("A graph call parameter references an unknown value slot.");
                if (m_Blackboards[parameters[i].ValueSlot].ExecutionFrameOwnerOperationIndex != executionFrameOwnerOperationIndex)
                    throw new ArgumentException("A graph call parameter crosses execution-frame ownership.");
            }
        }

        void RequireExecutionFrameOwner(int ownerOperationIndex)
        {
            RequireOperationIndex(ownerOperationIndex);
            ref readonly OrchestrationOperation owner = ref m_Operations[ownerOperationIndex];
            if (owner.Code != OrchestrationOperationCode.Root || owner.ExecutionFrameOwnerOperationIndex != ownerOperationIndex)
                throw new ArgumentException($"Execution frame owner '{ownerOperationIndex}' is not a canonical Root operation.");
        }

        void RequireOperationIndex(int operationIndex)
        {
            if (operationIndex < 0 || operationIndex >= m_Operations.Length)
                throw new ArgumentOutOfRangeException(nameof(operationIndex));
        }

        static void RequireRange(OrchestrationRange range, int length)
        {
            if (range.Offset < 0 || range.Count < 0 || range.Offset > length - range.Count)
                throw new ArgumentException("A compiled range is outside its table.");
        }

        int[] EmptyOperationIndexes()
        {
            var result = new int[m_Operations.Length];
            Array.Fill(result, -1);
            return result;
        }

        int[][][] BuildOutgoing()
        {
            const int kindCount = (int)OrchestrationFlowKind.Interrupt + 1;
            var groups = new List<int>[m_Operations.Length][];
            for (int operationIndex = 0; operationIndex < groups.Length; operationIndex++)
            {
                groups[operationIndex] = new List<int>[kindCount];
                for (int kindIndex = 0; kindIndex < kindCount; kindIndex++)
                    groups[operationIndex][kindIndex] = new List<int>();
            }
            for (int edgeIndex = 0; edgeIndex < m_Edges.Length; edgeIndex++)
            {
                ref readonly OrchestrationFlowEdge edge = ref m_Edges[edgeIndex];
                groups[edge.SourceOperationIndex][(int)edge.Kind].Add(edgeIndex);
            }
            var result = new int[groups.Length][][];
            for (int operationIndex = 0; operationIndex < groups.Length; operationIndex++)
            {
                result[operationIndex] = new int[kindCount][];
                for (int kindIndex = 0; kindIndex < kindCount; kindIndex++)
                {
                    List<int> values = groups[operationIndex][kindIndex];
                    values.Sort((leftIndex, rightIndex) =>
                    {
                        ref readonly OrchestrationFlowEdge left = ref m_Edges[leftIndex];
                        ref readonly OrchestrationFlowEdge right = ref m_Edges[rightIndex];
                        int comparison = left.Kind == OrchestrationFlowKind.Transition
                            ? left.Priority.CompareTo(right.Priority) : 0;
                        if (comparison != 0)
                            return comparison;
                        comparison = left.Order.CompareTo(right.Order);
                        if (comparison != 0)
                            return comparison;
                        comparison = left.SourcePortId.CompareTo(right.SourcePortId);
                        return comparison != 0 ? comparison :
                            left.IdentityId.CompareTo(right.IdentityId);
                    });
                    result[operationIndex][kindIndex] = values.ToArray();
                }
            }
            return result;
        }

        void BuildIndexes()
        {
            for (int operationIndex = 0; operationIndex < m_Operations.Length; operationIndex++)
            {
                ref readonly OrchestrationOperation operation = ref m_Operations[operationIndex];
                m_ValueEvaluationOffsets[operationIndex + 1] = checked(m_ValueEvaluationOffsets[operationIndex] + operation.OutputCount + 1);
                if (operation.Code == OrchestrationOperationCode.Parallel &&
                    Outgoing(operationIndex, OrchestrationFlowKind.Child).Length > 31)
                    throw new ArgumentException($"Parallel operation '{operationIndex}' exceeds its 31-child completion mask.");
                if (operation.Code == OrchestrationOperationCode.StateMachine)
                {
                    foreach (int edgeIndex in Outgoing(operationIndex, OrchestrationFlowKind.Enter))
                    {
                        ref readonly OrchestrationFlowEdge edge = ref m_Edges[edgeIndex];
                        if (edge.SourcePortId == Ports.AnyState && m_AnyStateEntries[operationIndex] < 0)
                            m_AnyStateEntries[operationIndex] = edge.TargetOperationIndex;
                        else if (edge.SourcePortId != Ports.AnyState && m_InitialEntries[operationIndex] < 0)
                            m_InitialEntries[operationIndex] = edge.TargetOperationIndex;
                    }
                    ReadOnlySpan<int> entries = Outgoing(operationIndex, OrchestrationFlowKind.Enter);
                    if (m_InitialEntries[operationIndex] < 0 && entries.Length != 0)
                        m_InitialEntries[operationIndex] = m_Edges[entries[0]].TargetOperationIndex;
                }
                if (operation.Code != OrchestrationOperationCode.State)
                    continue;
                foreach (int edgeIndex in Outgoing(operationIndex, OrchestrationFlowKind.Enter))
                {
                    ref readonly OrchestrationFlowEdge edge = ref m_Edges[edgeIndex];
                    if (edge.SourcePortId == Ports.StateOnEnter)
                        AssignUnique(m_StateOnEnter, operationIndex, edgeIndex);
                    else if (edge.SourcePortId == Ports.StateRoot)
                        AssignUnique(m_StateRoots, operationIndex, edgeIndex);
                }
                foreach (int edgeIndex in Outgoing(operationIndex, OrchestrationFlowKind.Exit))
                    if (m_Edges[edgeIndex].SourcePortId == Ports.StateOnExit)
                        AssignUnique(m_StateOnExit, operationIndex, edgeIndex);
            }
        }

        static void AssignUnique(int[] indexes, int operationIndex, int value)
        {
            if (indexes[operationIndex] >= 0)
                throw new ArgumentException($"Operation '{operationIndex}' declares a core control entry twice.");
            indexes[operationIndex] = value;
        }
    }
}
