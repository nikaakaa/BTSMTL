using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    internal readonly struct OrchestrationBoundHostInput
    {
        internal OrchestrationBoundHostInput(int input, int slot)
        {
            Input = input;
            Slot = slot;
        }

        internal readonly int Input;
        internal readonly int Slot;
    }

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
        public OrchestrationMemorySection ScheduleBoundHostInputs;
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
        public static byte[] Build<TValue>(OrchestrationProgramContent content,
            OrchestrationGraphParameters<TValue> parameters) where TValue : unmanaged =>
            Build(content, parameters, bindHostInputs: false);

        internal static byte[] Build<TValue>(OrchestrationProgramContent content,
            OrchestrationGraphParameters<TValue> parameters, bool bindHostInputs) where TValue : unmanaged
        {
            var writer = new OrchestrationMemoryImageWriter(
                OrchestrationMemoryImageWriter.SizeOf<OrchestrationProgramImageHeader>());
            var header = new OrchestrationProgramImageHeader();
            OrchestrationProgramStructure structure = content.Structure;
            OrchestrationValueSchedule schedule = content.ValueSchedule;
            OrchestrationProgramStorageLayout storage = content.ExecutionStorage;
            header.Operations = writer.Add(content.Operations.Span);
            header.Payloads = writer.Add(parameters.OperationPayloads.Span);
            header.ColdOperations = writer.Add(content.ColdOperations.Span);
            header.Edges = writer.Add(content.Edges.Span);
            header.ValueInputs = writer.Add(content.ValueInputs.Span);
            header.Constants = writer.Add(parameters.Constants.Span);
            header.DefaultValues = writer.Add(parameters.DefaultValues.Span);
            header.ValueSlotTypes = writer.Add(content.ValueSlotTypes.Span);
            header.ValueKindSlotCounts = writer.Add(content.ValueKindSlotCounts.Span);
            AddGraphCalls(writer, content.GraphCalls.Span, ref header);
            header.Hooks = writer.Add(content.Hooks.Span);
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
            AddSchedule(writer, schedule, bindHostInputs, ref header);
            header.ScheduleArgumentSlots = writer.Add(schedule.ArgumentSlots.Span);
            header.ScheduleArgumentLists = writer.Add(schedule.ArgumentLists.Span);
            header.ScheduleValueOperations = writer.Add(schedule.ValueOperations.Span);
            header.ScheduleStateContexts = writer.Add(schedule.StateContexts.Span);
            header.StorageOperationIndices = writer.Add(storage.OperationIndices.Span);
            header.StorageValues = writer.Add(storage.Values.Span);
            AddWorkspaceRows(writer, content, ref header);
            header.Ports = content.CorePorts;
            header.RootOperationIndex = content.RootOperationIndex;
            header.MaximumOperationEvaluations = content.MaximumOperationEvaluations;
            header.ScheduleValueSlotCount = schedule.ValueSlotCount;
            header.ScheduleInputBindingCount = schedule.InputBindingCount;
            header.ScheduleScratchSlotCount = schedule.ScratchSlotCount;
            header.SchedulePredictionDepth = schedule.PredictionDepth;
            return writer.Finish(in header);
        }

        static void AddSchedule(OrchestrationMemoryImageWriter writer, OrchestrationValueSchedule schedule,
            bool bindHostInputs, ref OrchestrationProgramImageHeader header)
        {
            ReadOnlySpan<OrchestrationValueScheduleInstruction> instructions = schedule.Instructions.Span;
            int[] next = null;
            if (bindHostInputs)
            {
                var slots = new Dictionary<int, int>();
                next = new int[instructions.Length];
                ReadOnlySpan<OrchestrationValueOperation> operations = schedule.ValueOperations.Span;
                for (int i = 0; i < instructions.Length; i++)
                {
                    ref readonly OrchestrationValueScheduleInstruction instruction = ref instructions[i];
                    next[i] = i;
                    // Workbench observes or overrides each output. Only Product can borrow the invocation's values.
                    if (instruction.Code != OrchestrationValueScheduleCode.Compute ||
                        operations[instruction.Operand].Code != OrchestrationValueOpcode.HostInput ||
                        instructions[instruction.Next].Code == OrchestrationValueScheduleCode.ObserveOutput)
                        continue;
                    slots[instruction.Destination] = operations[instruction.Operand].Argument;
                    next[i] = instruction.Next;
                }
                var bindings = new OrchestrationBoundHostInput[slots.Count];
                int index = 0;
                foreach (var slot in slots)
                    bindings[index++] = new OrchestrationBoundHostInput(slot.Value, slot.Key);
                Array.Sort(bindings, (left, right) => left.Input.CompareTo(right.Input));
                header.ScheduleBoundHostInputs = writer.Add<OrchestrationBoundHostInput>(bindings);
                if (bindings.Length != 0)
                {
                    var lowered = new OrchestrationValueScheduleInstruction[instructions.Length];
                    for (int i = 0; i < instructions.Length; i++)
                    {
                        ref readonly OrchestrationValueScheduleInstruction value = ref instructions[i];
                        lowered[i] = new OrchestrationValueScheduleInstruction(value.Code, value.Operation,
                            value.Operand, value.Destination, value.Argument, Resolve(value.Next), Resolve(value.Alternative));
                    }
                    instructions = lowered;
                }
                else
                    next = null;
            }
            header.ScheduleInstructions = writer.Add(instructions);
            header.ScheduleValueEntries = AddEntries(schedule.ValueEntries.Span);
            header.ScheduleInputEntries = AddEntries(schedule.InputEntries.Span);
            header.ScheduleTransitionEntries = AddEntries(schedule.TransitionEntries.Span);
            header.ScheduleChildSelectionEntries = AddEntries(schedule.ChildSelectionEntries.Span);
            header.ScheduleSelectorAbortEntries = AddEntries(schedule.SelectorAbortEntries.Span);
            header.ScheduleBooleanEntries = AddEntries(schedule.BooleanEntries.Span);

            int Resolve(int index)
            {
                while (index >= 0 && next[index] != index)
                    index = next[index];
                return index;
            }

            OrchestrationMemorySection AddEntries(ReadOnlySpan<OrchestrationValueScheduleEntry> entries)
            {
                if (next == null)
                    return writer.Add(entries);
                var lowered = new OrchestrationValueScheduleEntry[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                    lowered[i] = new OrchestrationValueScheduleEntry(Resolve(entries[i].Product), entries[i].Workbench);
                return writer.Add<OrchestrationValueScheduleEntry>(lowered);
            }
        }

        static void AddWorkspaceRows(OrchestrationMemoryImageWriter writer,
            OrchestrationProgramContent content, ref OrchestrationProgramImageHeader header)
        {
            OrchestrationWorkspaceCapacity workspace = content.WorkspaceCapacity;
            int maximumInputCount = 0;
            int maximumOutputCount = 0;
            foreach (ref readonly OrchestrationOperation operation in content.Operations.Span)
            {
                switch (operation.Code)
                {
                    case OrchestrationOperationCode.Extension:
                        maximumInputCount = Math.Max(maximumInputCount, operation.Inputs.Count);
                        maximumOutputCount = Math.Max(maximumOutputCount, operation.OutputCount);
                        break;
                    case OrchestrationOperationCode.EmitOutput:
                    case OrchestrationOperationCode.Timeline:
                        maximumInputCount = Math.Max(maximumInputCount, operation.Inputs.Count);
                        break;
                }
                if (operation.BlackboardFactsBindingIndex >= 0)
                {
                    maximumInputCount = Math.Max(maximumInputCount, OrchestrationBlackboardSourceContext.InputValueCount);
                    maximumOutputCount = Math.Max(maximumOutputCount, OrchestrationBlackboardSourceContext.OutputValueCount);
                }
            }
            ReadOnlySpan<int> inputs = workspace.InputCounts.Span;
            ReadOnlySpan<int> outputs = workspace.OutputCounts.Span;
            var inputRows = new OrchestrationRange[inputs.Length];
            var outputRows = new OrchestrationRange[outputs.Length];
            int inputOffset = 0;
            int outputOffset = 0;
            for (int i = 0; i < inputRows.Length; i++)
            {
                int inputCount = Math.Min(inputs[i], maximumInputCount);
                int outputCount = Math.Min(outputs[i], maximumOutputCount);
                inputRows[i] = new OrchestrationRange(inputOffset, inputCount);
                inputOffset = checked(inputOffset + inputCount);
                outputRows[i] = new OrchestrationRange(outputOffset, outputCount);
                outputOffset = checked(outputOffset + outputCount);
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
