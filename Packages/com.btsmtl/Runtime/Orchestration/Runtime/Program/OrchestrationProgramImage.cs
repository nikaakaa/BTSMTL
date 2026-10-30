using System;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationGraphCallRecord
    {
        public OrchestrationGraphCallRecord(int ownerOperationIndex, int entryOperationIndex,
            OrchestrationRange inputs, OrchestrationRange outputs)
        {
            OwnerOperationIndex = ownerOperationIndex;
            EntryOperationIndex = entryOperationIndex;
            Inputs = inputs;
            Outputs = outputs;
        }

        public int OwnerOperationIndex { get; }
        public int EntryOperationIndex { get; }
        public OrchestrationRange Inputs { get; }
        public OrchestrationRange Outputs { get; }
    }

    public struct OrchestrationProgramImageHeader
    {
        public OrchestrationMemorySection Operations;
        public OrchestrationMemorySection Payloads;
        public OrchestrationMemorySection ColdOperations;
        public OrchestrationMemorySection Edges;
        public OrchestrationMemorySection ValueInputs;
        public OrchestrationMemorySection Constants;
        public OrchestrationMemorySection DefaultValues;
        public OrchestrationMemorySection ValueSlotTypes;
        public OrchestrationMemorySection ValueKindSlotCounts;
        public OrchestrationMemorySection GraphCalls;
        public OrchestrationMemorySection GraphParameters;
        public OrchestrationMemorySection Hooks;
        public OrchestrationMemorySection HookInputs;
        public OrchestrationMemorySection BlackboardSlots;
        public OrchestrationMemorySection BlackboardOutputs;
        public OrchestrationMemorySection OutgoingEdgeIndexes;
        public OrchestrationMemorySection OutgoingOffsets;
        public OrchestrationMemorySection InitialStateEntries;
        public OrchestrationMemorySection AnyStateEntries;
        public OrchestrationMemorySection StateOnEnterEdges;
        public OrchestrationMemorySection StateRootEdges;
        public OrchestrationMemorySection StateOnExitEdges;
        public OrchestrationMemorySection ValueEvaluationOffsets;
        public OrchestrationMemorySection StateOperationIndexes;
        public OrchestrationMemorySection ScheduleInstructions;
        public OrchestrationMemorySection ScheduleArgumentSlots;
        public OrchestrationMemorySection ScheduleArgumentLists;
        public OrchestrationMemorySection ScheduleValueEntries;
        public OrchestrationMemorySection ScheduleInputEntries;
        public OrchestrationMemorySection ScheduleTransitionEntries;
        public OrchestrationMemorySection ScheduleChildSelectionEntries;
        public OrchestrationMemorySection ScheduleSelectorAbortEntries;
        public OrchestrationMemorySection ScheduleBooleanEntries;
        public OrchestrationMemorySection ScheduleValueOperations;
        public OrchestrationMemorySection ScheduleStateContexts;
        public OrchestrationMemorySection StorageOperationIndices;
        public OrchestrationMemorySection StorageValues;
        public OrchestrationMemorySection WorkspaceInputRows;
        public OrchestrationMemorySection WorkspaceOutputRows;
        public int WorkspaceInputValueCount;
        public int WorkspaceOutputValueCount;
        public int WorkspaceStateDepth;
        public int WorkspaceGraphCallDepth;
        public int WorkspaceEntryDepth;
        public int WorkspaceStateAncestorCount;
        public OrchestrationCorePorts Ports;
        public int RootOperationIndex;
        public int MaximumOperationEvaluations;
        public int ScheduleValueSlotCount;
        public int ScheduleInputBindingCount;
        public int ScheduleScratchSlotCount;
        public int SchedulePredictionDepth;
    }

    // 准备期把程序推进所需的全部表排进一块 16 字节对齐的连续内存，段表头位于偏移 0。
    // 托管宿主直接持有该数组；Burst 宿主把同一份字节放进原生内存，两者共用同一布局。
    public static class OrchestrationProgramImage
    {
        public static byte[] Build<TValue>(OrchestrationProgramContent<TValue> content) where TValue : unmanaged
        {
            var writer = new OrchestrationMemoryImageWriter(
                OrchestrationMemoryImageWriter.SizeOf<OrchestrationProgramImageHeader>());
            var header = new OrchestrationProgramImageHeader();
            OrchestrationProgramStructure structure = content.Structure;
            OrchestrationValueSchedule schedule = content.ValueSchedule;
            OrchestrationProgramStorageLayout storage = content.ExecutionStorage;
            header.Operations = writer.Add(content.Operations.Span);
            header.Payloads = writer.Add(content.OperationPayloads.Span);
            header.ColdOperations = writer.Add(content.ColdOperations.Span);
            header.Edges = writer.Add(content.Edges.Span);
            header.ValueInputs = writer.Add(content.ValueInputs.Span);
            header.Constants = writer.Add(content.Constants.Span);
            header.DefaultValues = writer.Add(content.DefaultValues.Span);
            header.ValueSlotTypes = writer.Add(content.ValueSlotTypes.Span);
            header.ValueKindSlotCounts = writer.Add(content.ValueKindSlotCounts.Span);
            AddGraphCalls(writer, content.GraphCalls.Span, ref header);
            header.Hooks = writer.Add(content.Hooks.Span);
            header.HookInputs = writer.Add(content.HookInputs.Span);
            header.BlackboardSlots = writer.Add(content.BlackboardSlots.Span);
            header.BlackboardOutputs = writer.Add(content.BlackboardOutputs.Span);
            header.OutgoingEdgeIndexes = writer.Add(structure.OutgoingEdgeIndexes.Span);
            header.OutgoingOffsets = writer.Add(structure.OutgoingOffsets.Span);
            header.InitialStateEntries = writer.Add(structure.InitialStateEntries.Span);
            header.AnyStateEntries = writer.Add(structure.AnyStateEntries.Span);
            header.StateOnEnterEdges = writer.Add(structure.StateOnEnterEdges.Span);
            header.StateRootEdges = writer.Add(structure.StateRootEdges.Span);
            header.StateOnExitEdges = writer.Add(structure.StateOnExitEdges.Span);
            header.ValueEvaluationOffsets = writer.Add(structure.ValueEvaluationOffsets.Span);
            header.StateOperationIndexes = writer.Add(structure.StateOperationIndexes.Span);
            header.ScheduleInstructions = writer.Add(schedule.Instructions.Span);
            header.ScheduleArgumentSlots = writer.Add(schedule.ArgumentSlots.Span);
            header.ScheduleArgumentLists = writer.Add(schedule.ArgumentLists.Span);
            header.ScheduleValueEntries = writer.Add(schedule.ValueEntries.Span);
            header.ScheduleInputEntries = writer.Add(schedule.InputEntries.Span);
            header.ScheduleTransitionEntries = writer.Add(schedule.TransitionEntries.Span);
            header.ScheduleChildSelectionEntries = writer.Add(schedule.ChildSelectionEntries.Span);
            header.ScheduleSelectorAbortEntries = writer.Add(schedule.SelectorAbortEntries.Span);
            header.ScheduleBooleanEntries = writer.Add(schedule.BooleanEntries.Span);
            header.ScheduleValueOperations = writer.Add(schedule.ValueOperations.Span);
            header.ScheduleStateContexts = writer.Add(schedule.StateContexts.Span);
            header.StorageOperationIndices = writer.Add(storage.OperationIndices.Span);
            header.StorageValues = writer.Add(storage.Values.Span);
            AddWorkspaceRows(writer, content.WorkspaceCapacity, ref header);
            header.Ports = content.CorePorts;
            header.RootOperationIndex = content.RootOperationIndex;
            header.MaximumOperationEvaluations = content.MaximumOperationEvaluations;
            header.ScheduleValueSlotCount = schedule.ValueSlotCount;
            header.ScheduleInputBindingCount = schedule.InputBindingCount;
            header.ScheduleScratchSlotCount = schedule.ScratchSlotCount;
            header.SchedulePredictionDepth = schedule.PredictionDepth;
            return writer.Finish(in header);
        }

        static void AddWorkspaceRows(OrchestrationMemoryImageWriter writer,
            OrchestrationWorkspaceCapacity workspace, ref OrchestrationProgramImageHeader header)
        {
            ReadOnlySpan<int> inputs = workspace.InputCounts.Span;
            ReadOnlySpan<int> outputs = workspace.OutputCounts.Span;
            var inputRows = new OrchestrationRange[inputs.Length];
            var outputRows = new OrchestrationRange[outputs.Length];
            int inputOffset = 0;
            int outputOffset = 0;
            for (int i = 0; i < inputRows.Length; i++)
            {
                inputRows[i] = new OrchestrationRange(inputOffset, inputs[i]);
                inputOffset = checked(inputOffset + inputs[i]);
                outputRows[i] = new OrchestrationRange(outputOffset, outputs[i]);
                outputOffset = checked(outputOffset + outputs[i]);
            }
            header.WorkspaceInputRows = writer.Add<OrchestrationRange>(inputRows);
            header.WorkspaceOutputRows = writer.Add<OrchestrationRange>(outputRows);
            header.WorkspaceInputValueCount = inputOffset;
            header.WorkspaceOutputValueCount = outputOffset;
            header.WorkspaceStateDepth = workspace.StateDepth;
            header.WorkspaceGraphCallDepth = workspace.GraphCallDepth;
            header.WorkspaceEntryDepth = workspace.EntryDepth;
            header.WorkspaceStateAncestorCount = workspace.StateAncestorCount;
        }

        static void AddGraphCalls(OrchestrationMemoryImageWriter writer,ReadOnlySpan<OrchestrationGraphCall> calls,
            ref OrchestrationProgramImageHeader header)
        {
            var records = new OrchestrationGraphCallRecord[calls.Length];
            int parameterCount = 0;
            foreach (OrchestrationGraphCall call in calls)
                parameterCount += call.Inputs.Length + call.Outputs.Length;
            var parameters = new OrchestrationGraphParameter[parameterCount];
            int offset = 0;
            for (int i = 0; i < calls.Length; i++)
            {
                OrchestrationGraphCall call = calls[i];
                var inputs = new OrchestrationRange(offset, call.Inputs.Length);
                call.Inputs.Span.CopyTo(parameters.AsSpan(offset));
                offset += call.Inputs.Length;
                var outputs = new OrchestrationRange(offset, call.Outputs.Length);
                call.Outputs.Span.CopyTo(parameters.AsSpan(offset));
                offset += call.Outputs.Length;
                records[i] = new OrchestrationGraphCallRecord(call.OwnerOperationIndex,
                    call.EntryOperationIndex, inputs, outputs);
            }
            header.GraphCalls = writer.Add<OrchestrationGraphCallRecord>(records);
            header.GraphParameters = writer.Add<OrchestrationGraphParameter>(parameters);
        }
    }
}
