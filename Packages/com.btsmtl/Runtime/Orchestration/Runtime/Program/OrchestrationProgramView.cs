using System;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationGraphCallView
    {
        internal OrchestrationGraphCallView(in OrchestrationGraphCallRecord record,
            ReadOnlySpan<OrchestrationGraphParameter> parameters)
        {
            OwnerOperationIndex = record.OwnerOperationIndex;
            EntryOperationIndex = record.EntryOperationIndex;
            Inputs = parameters.Slice(record.Inputs.Offset, record.Inputs.Count);
            Outputs = parameters.Slice(record.Outputs.Offset, record.Outputs.Count);
        }

        public int OwnerOperationIndex { get; }
        public int EntryOperationIndex { get; }
        public ReadOnlySpan<OrchestrationGraphParameter> Inputs { get; }
        public ReadOnlySpan<OrchestrationGraphParameter> Outputs { get; }
    }

    public readonly ref struct OrchestrationValueScheduleView
    {
        internal OrchestrationValueScheduleView(ReadOnlySpan<OrchestrationValueScheduleInstruction> instructions,
            ReadOnlySpan<int> argumentSlots, ReadOnlySpan<OrchestrationRange> argumentLists,
            ReadOnlySpan<OrchestrationValueScheduleEntry> valueEntries,
            ReadOnlySpan<OrchestrationValueScheduleEntry> inputEntries,
            ReadOnlySpan<OrchestrationValueScheduleEntry> transitionEntries,
            ReadOnlySpan<OrchestrationValueScheduleEntry> childSelectionEntries,
            ReadOnlySpan<OrchestrationValueScheduleEntry> selectorAbortEntries,
            ReadOnlySpan<OrchestrationValueScheduleEntry> booleanEntries,
            ReadOnlySpan<OrchestrationValueOperation> valueOperations, ReadOnlySpan<int> stateContexts,
            int valueSlotCount, int inputBindingCount, int scratchSlotCount, int predictionDepth)
        {
            Instructions = instructions;
            ArgumentSlots = argumentSlots;
            ArgumentLists = argumentLists;
            ValueEntries = valueEntries;
            InputEntries = inputEntries;
            TransitionEntries = transitionEntries;
            ChildSelectionEntries = childSelectionEntries;
            SelectorAbortEntries = selectorAbortEntries;
            BooleanEntries = booleanEntries;
            ValueOperations = valueOperations;
            StateContexts = stateContexts;
            ValueSlotCount = valueSlotCount;
            InputBindingCount = inputBindingCount;
            ScratchSlotCount = scratchSlotCount;
            PredictionDepth = predictionDepth;
        }

        public ReadOnlySpan<OrchestrationValueScheduleInstruction> Instructions { get; }
        public ReadOnlySpan<int> ArgumentSlots { get; }
        public ReadOnlySpan<OrchestrationRange> ArgumentLists { get; }
        public ReadOnlySpan<OrchestrationValueScheduleEntry> ValueEntries { get; }
        public ReadOnlySpan<OrchestrationValueScheduleEntry> InputEntries { get; }
        public ReadOnlySpan<OrchestrationValueScheduleEntry> TransitionEntries { get; }
        public ReadOnlySpan<OrchestrationValueScheduleEntry> ChildSelectionEntries { get; }
        public ReadOnlySpan<OrchestrationValueScheduleEntry> SelectorAbortEntries { get; }
        public ReadOnlySpan<OrchestrationValueScheduleEntry> BooleanEntries { get; }
        public ReadOnlySpan<OrchestrationValueOperation> ValueOperations { get; }
        public ReadOnlySpan<int> StateContexts { get; }
        public int ValueSlotCount { get; }
        public int InputBindingCount { get; }
        public int ScratchSlotCount { get; }
        public int PredictionDepth { get; }
        public int BooleanSlot => ValueSlotCount + InputBindingCount;
    }

    // 推进只经此视图读取程序；视图只持有镜像字节，访问时按段表头切片。
    public readonly ref struct OrchestrationProgramView<TValue> where TValue : unmanaged
    {
        readonly ReadOnlySpan<byte> m_Memory;

        public OrchestrationProgramView(ReadOnlySpan<byte> image) => m_Memory = image;

        ref readonly OrchestrationProgramImageHeader Header
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref OrchestrationMemoryImageWriter.Header<OrchestrationProgramImageHeader>(m_Memory);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ReadOnlySpan<T> Section<T>(in OrchestrationMemorySection section) where T : unmanaged =>
            OrchestrationMemoryImageWriter.Section<T>(m_Memory, section);

        public bool IsEmpty => m_Memory.IsEmpty;
        public ReadOnlySpan<byte> Image => m_Memory;
        public int RootOperationIndex => Header.RootOperationIndex;
        public int MaximumOperationEvaluations => Header.MaximumOperationEvaluations;
        public OrchestrationCorePorts Ports => Header.Ports;
        public int OperationCount => Header.Operations.Count;
        public int ValueSlotCount => Header.DefaultValues.Count;
        public int InputBindingCount => Header.ValueInputs.Count;
        public int BlackboardCount => Header.BlackboardSlots.Count;
        public int EdgeCount => Header.Edges.Count;
        public ReadOnlySpan<OrchestrationValueSlotType> ValueSlotTypes =>
            Section<OrchestrationValueSlotType>(in Header.ValueSlotTypes);
        public ReadOnlySpan<int> ValueKindSlotCounts => Section<int>(in Header.ValueKindSlotCounts);
        public ReadOnlySpan<OrchestrationOperation> Operations => Section<OrchestrationOperation>(in Header.Operations);
        public ReadOnlySpan<OrchestrationFlowEdge> Edges => Section<OrchestrationFlowEdge>(in Header.Edges);

        public int ValueEvaluationSlotCount =>
            Section<int>(in Header.ValueEvaluationOffsets)[Header.Operations.Count];
        public int InitialStateEntry(int machineOperationIndex) =>
            Section<int>(in Header.InitialStateEntries)[machineOperationIndex];
        public int AnyStateEntry(int machineOperationIndex) =>
            Section<int>(in Header.AnyStateEntries)[machineOperationIndex];
        public int StateOnEnterEdge(int stateOperationIndex) =>
            Section<int>(in Header.StateOnEnterEdges)[stateOperationIndex];
        public int StateRootEdge(int stateOperationIndex) =>
            Section<int>(in Header.StateRootEdges)[stateOperationIndex];
        public int StateOnExitEdge(int stateOperationIndex) =>
            Section<int>(in Header.StateOnExitEdges)[stateOperationIndex];
        public int StateOperationIndex(int stateIdentityId) =>
            Section<int>(in Header.StateOperationIndexes)[stateIdentityId];
        public int ValueEvaluationSlot(int operationIndex, int outputPortIndex) =>
            Section<int>(in Header.ValueEvaluationOffsets)[operationIndex] + outputPortIndex + 1;

        public ref readonly OrchestrationOperation Operation(int operationIndex) =>
            ref Section<OrchestrationOperation>(in Header.Operations)[operationIndex];
        public ref readonly OrchestrationOperationColdData ColdOperation(int operationIndex) =>
            ref Section<OrchestrationOperationColdData>(in Header.ColdOperations)[operationIndex];
        public T Payload<T>(int operationIndex) where T : unmanaged =>
            Payload<T>(in Operation(operationIndex));
        internal T Payload<T>(in OrchestrationOperation operation) where T : unmanaged =>
            SpanReinterpret.Read<T>(Section<byte>(in Header.Payloads).Slice(operation.ParameterOffset));
        public ref readonly OrchestrationValueOperation ValueOperation(int operationIndex, int outputPortIndex = -1) =>
            ref Section<OrchestrationValueOperation>(in Header.ScheduleValueOperations)[
                ValueEvaluationSlot(operationIndex, outputPortIndex)];
        public ref readonly OrchestrationFlowEdge Edge(int edgeIndex) =>
            ref Section<OrchestrationFlowEdge>(in Header.Edges)[edgeIndex];
        public ref readonly TValue Constant(int constantIndex) =>
            ref Section<TValue>(in Header.Constants)[constantIndex];
        public ref readonly TValue DefaultValue(int valueSlot) =>
            ref Section<TValue>(in Header.DefaultValues)[valueSlot];
        public ReadOnlySpan<TValue> DefaultValues => Section<TValue>(in Header.DefaultValues);
        public ref readonly OrchestrationBlackboardSlot Blackboard(int valueSlot) =>
            ref Section<OrchestrationBlackboardSlot>(in Header.BlackboardSlots)[valueSlot];

        public OrchestrationGraphCallView GraphCall(int graphCallIndex) =>
            new OrchestrationGraphCallView(
                in Section<OrchestrationGraphCallRecord>(in Header.GraphCalls)[graphCallIndex],
                Section<OrchestrationGraphParameter>(in Header.GraphParameters));

        public ReadOnlySpan<int> Outgoing(int operationIndex, OrchestrationFlowKind kind) =>
            OrchestrationProgramStructure.Outgoing(Section<int>(in Header.OutgoingOffsets),
                Section<int>(in Header.OutgoingEdgeIndexes), operationIndex, kind);

        public ReadOnlySpan<OrchestrationValueInputBinding> Inputs(int operationIndex)
        {
            OrchestrationIndexRange range = Operation(operationIndex).Inputs;
            return Section<OrchestrationValueInputBinding>(in Header.ValueInputs).Slice(range.Offset, range.Count);
        }

        public ReadOnlySpan<OrchestrationHookBinding> Hooks(int operationIndex)
        {
            OrchestrationIndexRange range = Operation(operationIndex).LifecycleHooks;
            return Section<OrchestrationHookBinding>(in Header.Hooks).Slice(range.Offset, range.Count);
        }

        public ReadOnlySpan<OrchestrationBlackboardOutputBinding> OutputAssignments(int operationIndex)
        {
            OrchestrationIndexRange range = Payload<OrchestrationExtensionPayload>(operationIndex).OutputAssignments;
            return Section<OrchestrationBlackboardOutputBinding>(in Header.BlackboardOutputs)
                .Slice(range.Offset, range.Count);
        }

        public int ScheduleValueSlotCount => Header.ScheduleValueSlotCount;
        public int ScheduleBooleanSlot => Header.ScheduleValueSlotCount + Header.ScheduleInputBindingCount;

        public OrchestrationValueScheduleView ValueSchedule
        {
            get
            {
                ref readonly OrchestrationProgramImageHeader header = ref Header;
                return new OrchestrationValueScheduleView(
                    Section<OrchestrationValueScheduleInstruction>(in header.ScheduleInstructions),
                    Section<int>(in header.ScheduleArgumentSlots),
                    Section<OrchestrationRange>(in header.ScheduleArgumentLists),
                    Section<OrchestrationValueScheduleEntry>(in header.ScheduleValueEntries),
                    Section<OrchestrationValueScheduleEntry>(in header.ScheduleInputEntries),
                    Section<OrchestrationValueScheduleEntry>(in header.ScheduleTransitionEntries),
                    Section<OrchestrationValueScheduleEntry>(in header.ScheduleChildSelectionEntries),
                    Section<OrchestrationValueScheduleEntry>(in header.ScheduleSelectorAbortEntries),
                    Section<OrchestrationValueScheduleEntry>(in header.ScheduleBooleanEntries),
                    Section<OrchestrationValueOperation>(in header.ScheduleValueOperations),
                    Section<int>(in header.ScheduleStateContexts),
                    header.ScheduleValueSlotCount, header.ScheduleInputBindingCount,
                    header.ScheduleScratchSlotCount, header.SchedulePredictionDepth);
            }
        }

        public ReadOnlySpan<OrchestrationRange> WorkspaceInputRows =>
            Section<OrchestrationRange>(in Header.WorkspaceInputRows);
        public ReadOnlySpan<OrchestrationRange> WorkspaceOutputRows =>
            Section<OrchestrationRange>(in Header.WorkspaceOutputRows);
        public int WorkspaceInputValueCount => Header.WorkspaceInputValueCount;
        public int WorkspaceOutputValueCount => Header.WorkspaceOutputValueCount;
        public int WorkspaceStateDepth => Header.WorkspaceStateDepth;
        public int WorkspaceGraphCallDepth => Header.WorkspaceGraphCallDepth;
        public int WorkspaceEntryDepth => Header.WorkspaceEntryDepth;
        public int WorkspaceStateAncestorCount => Header.WorkspaceStateAncestorCount;
        public int SchedulePredictionDepth => Header.SchedulePredictionDepth;
        public int ScheduleScratchSlotCount => Header.ScheduleScratchSlotCount;

        public OrchestrationProgramStorageView ExecutionStorage =>
            new OrchestrationProgramStorageView(Section<ushort>(in Header.StorageOperationIndices),
                Section<OrchestrationValueStorageSlot>(in Header.StorageValues));
    }
}
