using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    internal static class OrchestrationPreparedScheduleTarget
    {
        internal const OrchestrationValueScheduleCode ReadValue = (OrchestrationValueScheduleCode)251;
        internal const OrchestrationValueScheduleCode ComputeNumber = (OrchestrationValueScheduleCode)252;
        internal const OrchestrationValueScheduleCode ReadReusableBlackboard = (OrchestrationValueScheduleCode)253;
        internal const OrchestrationValueScheduleCode GuardReusableValue = (OrchestrationValueScheduleCode)254;
        internal const OrchestrationValueScheduleCode MarkReusableValue = (OrchestrationValueScheduleCode)255;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsReturn(int target) => target < -1;

        // 装配后的负地址直接携带返回值和选择原因，保留 -1 表示没有目标。
        internal static int EncodeReturn(in OrchestrationValueScheduleInstruction instruction) =>
            int.MinValue | ((instruction.Argument + 1) << 1) | instruction.Operand;

        internal static int ReturnValue(int target) => ((target & int.MaxValue) >> 1) - 1;

        internal static OrchestrationValueSelectionReason ReturnReason(int target) =>
            (OrchestrationValueSelectionReason)(target & 1);
    }

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
        public OrchestrationMemorySection ScheduleLiterals;
        public OrchestrationMemorySection ScheduleBlackboardReuses;
        public OrchestrationMemorySection ScheduleBlackboardReuseSlots;
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
        public int ScheduleReuseOffset;
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
            AddSchedule(writer, schedule, parameters.Constants.Span, content.BlackboardSlots.Length,
                bindHostInputs, ref header);
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

        static void AddSchedule<TValue>(OrchestrationMemoryImageWriter writer, OrchestrationValueSchedule schedule,
            ReadOnlySpan<TValue> constants, int blackboardCount, bool bindHostInputs,
            ref OrchestrationProgramImageHeader header) where TValue : unmanaged
        {
            int reuseOffset = checked((schedule.ValueSlotCount + schedule.InputBindingCount + 1) *
                (schedule.PredictionDepth + 1));
            header.ScheduleReuseOffset = reuseOffset;
            AddBlackboardReuses(writer, schedule, blackboardCount, reuseOffset, ref header);
            OrchestrationValueScheduleInstruction[] instructions;
            ReadOnlySpan<int> argumentSlots = schedule.ArgumentSlots.Span;
            ReadOnlySpan<OrchestrationRange> argumentLists = schedule.ArgumentLists.Span;
            int[] productEntries = null;
            if (bindHostInputs)
            {
                productEntries = BindScheduleInputs(writer, schedule, constants, ref header, out instructions,
                    out int[] boundSlots, out OrchestrationRange[] boundLists);
                argumentSlots = boundSlots;
                argumentLists = boundLists;
            }
            else
                instructions = schedule.Instructions.ToArray();

            header.ScheduleArgumentSlots = writer.Add(argumentSlots);
            header.ScheduleArgumentLists = writer.Add(argumentLists);
            FuseReusableBlackboardReads(instructions, schedule.ValueOperations.Span, reuseOffset);
            int[] targets = BindScheduleTargets(instructions, reuseOffset, schedule.ScratchSlotCount - 1,
                out int instructionCount);
            Span<OrchestrationValueScheduleInstruction> prepared = instructions.AsSpan(0, instructionCount);
            // 跳转重定址完成后，求值指令的备用跳转字段可保存参数范围。
            BindValueComputations(prepared, schedule.ValueOperations.Span, argumentSlots, argumentLists);
            header.ScheduleInstructions = writer.Add<OrchestrationValueScheduleInstruction>(prepared);
            header.ScheduleValueEntries = AddEntries(schedule.ValueEntries.Span);
            header.ScheduleInputEntries = AddEntries(schedule.InputEntries.Span);
            header.ScheduleTransitionEntries = AddEntries(schedule.TransitionEntries.Span);
            header.ScheduleChildSelectionEntries = AddEntries(schedule.ChildSelectionEntries.Span);
            header.ScheduleSelectorAbortEntries = AddEntries(schedule.SelectorAbortEntries.Span);
            header.ScheduleBooleanEntries = AddEntries(schedule.BooleanEntries.Span);

            OrchestrationMemorySection AddEntries(ReadOnlySpan<OrchestrationValueScheduleEntry> entries)
            {
                var lowered = new OrchestrationValueScheduleEntry[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                {
                    int product = entries[i].Product;
                    if (product >= 0 && productEntries != null)
                        product = productEntries[product];
                    lowered[i] = new OrchestrationValueScheduleEntry(
                        BindScheduleTarget(targets, product), BindScheduleTarget(targets, entries[i].Workbench));
                }
                return writer.Add<OrchestrationValueScheduleEntry>(lowered);
            }
        }

        static void AddBlackboardReuses(OrchestrationMemoryImageWriter writer, OrchestrationValueSchedule schedule,
            int blackboardCount, int reuseOffset, ref OrchestrationProgramImageHeader header)
        {
            SortedSet<int>[] reuses = null;
            foreach (ref readonly var instruction in schedule.Instructions.Span)
            {
                if (instruction.Code != OrchestrationValueScheduleCode.BranchBoolean ||
                    instruction.Destination < reuseOffset || instruction.Operand < 0)
                    continue;
                reuses ??= new SortedSet<int>[blackboardCount];
                OrchestrationRange dependencies = schedule.ArgumentLists.Span[instruction.Operand];
                foreach (int slot in schedule.ArgumentSlots.Span.Slice(dependencies.Offset, dependencies.Count))
                    (reuses[slot] ??= new SortedSet<int>()).Add(instruction.Destination - reuseOffset);
            }
            if (reuses == null)
                return;

            var ranges = new OrchestrationRange[blackboardCount];
            var indices = new List<int>();
            for (int slot = 0; slot < reuses.Length; slot++)
            {
                if (reuses[slot] is not { } flags)
                    continue;
                ranges[slot] = new OrchestrationRange(indices.Count, flags.Count);
                indices.AddRange(flags);
            }
            header.ScheduleBlackboardReuses = writer.Add<OrchestrationRange>(ranges);
            header.ScheduleBlackboardReuseSlots = writer.Add<int>(indices.ToArray());
        }

        static void FuseReusableBlackboardReads(Span<OrchestrationValueScheduleInstruction> instructions,
            ReadOnlySpan<OrchestrationValueOperation> operations, int reuseOffset)
        {
            for (int pc = 0; pc < instructions.Length; pc++)
            {
                ref OrchestrationValueScheduleInstruction guard = ref instructions[pc];
                if (guard.Code != OrchestrationValueScheduleCode.BranchBoolean ||
                    guard.Destination < reuseOffset || guard.Operand < 0)
                    continue;
                ref readonly var read = ref instructions[guard.Alternative];
                if (read.Code != OrchestrationValueScheduleCode.Compute ||
                    operations[read.Operand].Code != OrchestrationValueOpcode.BlackboardGet)
                    continue;
                ref readonly var mark = ref instructions[read.Next];
                if (mark.Code != OrchestrationValueScheduleCode.CopyInput || mark.Destination != guard.Destination ||
                    mark.Operand != mark.Destination || mark.Next != guard.Next)
                    continue;
                guard = new OrchestrationValueScheduleInstruction(OrchestrationPreparedScheduleTarget.ReadReusableBlackboard,
                    read.Operation, operations[read.Operand].Argument, read.Destination,
                    mark.Destination - reuseOffset, mark.Next, -1);
            }
        }

        static void BindValueComputations(Span<OrchestrationValueScheduleInstruction> instructions,
            ReadOnlySpan<OrchestrationValueOperation> operations, ReadOnlySpan<int> argumentSlots,
            ReadOnlySpan<OrchestrationRange> argumentLists)
        {
            foreach (ref var instruction in instructions)
            {
                if (instruction.Code != OrchestrationValueScheduleCode.Compute)
                    continue;

                ref readonly OrchestrationValueOperation operation = ref operations[instruction.Operand];
                OrchestrationValueScheduleCode code = OrchestrationPreparedScheduleTarget.ReadValue;
                int argument = operation.Argument;
                int additional = -1;
                switch (operation.Code)
                {
                    case OrchestrationValueOpcode.Copy:
                        argument = argumentSlots[argumentLists[instruction.Argument].Offset + operation.Argument];
                        break;
                    case OrchestrationValueOpcode.Extension:
                        additional = instruction.Argument;
                        break;
                    case OrchestrationValueOpcode.Constant:
                    case OrchestrationValueOpcode.HostInput:
                    case OrchestrationValueOpcode.InputVector2Magnitude:
                    case OrchestrationValueOpcode.InputRequest:
                    case OrchestrationValueOpcode.BlackboardGet:
                    case OrchestrationValueOpcode.GraphCallOutput:
                    case OrchestrationValueOpcode.StateRootCompleted:
                    case OrchestrationValueOpcode.StateExitCause:
                    case OrchestrationValueOpcode.Cycle:
                    case OrchestrationValueOpcode.TimelineTime:
                        break;
                    default:
                        code = OrchestrationPreparedScheduleTarget.ComputeNumber;
                        OrchestrationRange range = argumentLists[instruction.Argument];
                        argument = range.Offset;
                        additional = range.Count;
                        break;
                }

                instruction = new OrchestrationValueScheduleInstruction(code, instruction.Operation,
                    (int)operation.Code, instruction.Destination, argument, instruction.Next, additional);
            }
        }

        static int[] BindScheduleTargets(Span<OrchestrationValueScheduleInstruction> instructions,
            int reuseOffset, int trueSlot, out int count)
        {
            var targets = new int[instructions.Length];
            count = 0;
            for (int i = 0; i < instructions.Length; i++)
            {
                ref readonly OrchestrationValueScheduleInstruction instruction = ref instructions[i];
                targets[i] = instruction.Code is OrchestrationValueScheduleCode.Return or OrchestrationValueScheduleCode.ReturnInputs
                    ? OrchestrationPreparedScheduleTarget.EncodeReturn(in instruction) : count++;
            }
            for (int i = 0; i < instructions.Length; i++)
            {
                int target = targets[i];
                if (OrchestrationPreparedScheduleTarget.IsReturn(target))
                    continue;
                OrchestrationValueScheduleInstruction instruction = instructions[i];
                OrchestrationValueScheduleCode code = instruction.Code;
                int destination = instruction.Destination;
                if (destination >= reuseOffset)
                {
                    if (code == OrchestrationValueScheduleCode.BranchBoolean)
                        code = OrchestrationPreparedScheduleTarget.GuardReusableValue;
                    else if (code == OrchestrationValueScheduleCode.CopyInput &&
                        (instruction.Operand == trueSlot || instruction.Operand == destination))
                        code = OrchestrationPreparedScheduleTarget.MarkReusableValue;
                    if (code is OrchestrationPreparedScheduleTarget.GuardReusableValue or
                        OrchestrationPreparedScheduleTarget.MarkReusableValue)
                        destination -= reuseOffset;
                }
                instructions[target] = new OrchestrationValueScheduleInstruction(code, instruction.Operation,
                    instruction.Operand, destination, instruction.Argument,
                    BindScheduleTarget(targets, instruction.Next), BindScheduleTarget(targets, instruction.Alternative));
            }
            return targets;
        }

        static int BindScheduleTarget(int[] targets, int target) => target >= 0 ? targets[target] : target;

        static int[] BindScheduleInputs<TValue>(OrchestrationMemoryImageWriter writer,
            OrchestrationValueSchedule schedule, ReadOnlySpan<TValue> constants, ref OrchestrationProgramImageHeader header,
            out OrchestrationValueScheduleInstruction[] boundInstructions, out int[] boundSlots,
            out OrchestrationRange[] boundLists)
            where TValue : unmanaged
        {
            const byte Product = 1;
            const byte Workbench = 2;
            const int Unwritten = -1;
            const int Variable = -2;
            ReadOnlySpan<OrchestrationValueScheduleInstruction> instructions = schedule.Instructions.Span;
            ReadOnlySpan<OrchestrationValueOperation> operations = schedule.ValueOperations.Span;
            var modes = new byte[instructions.Length];
            MarkEntries(schedule.ValueEntries.Span);
            MarkEntries(schedule.InputEntries.Span);
            MarkEntries(schedule.TransitionEntries.Span);
            MarkEntries(schedule.ChildSelectionEntries.Span);
            MarkEntries(schedule.SelectorAbortEntries.Span);
            MarkEntries(schedule.BooleanEntries.Span);
            var literalBySlot = new int[schedule.ScratchSlotCount];
            Array.Fill(literalBySlot, Unwritten);
            var hostBySlot = new Dictionary<int, int>();

            // 发布指令表的目标始终在来源之后，先标记两种入口可达的指令，再从返回端重接 Product。
            for (int i = 0; i < instructions.Length; i++)
            {
                ref readonly OrchestrationValueScheduleInstruction instruction = ref instructions[i];
                Mark(instruction.Next, modes[i]);
                Mark(instruction.Alternative, modes[i]);
                if ((modes[i] & Product) == 0)
                    continue;
                int slot = instruction.Destination;
                switch (instruction.Code)
                {
                    case OrchestrationValueScheduleCode.ReadInputConstant:
                        literalBySlot[slot] = literalBySlot[slot] == Unwritten || literalBySlot[slot] == instruction.Operand
                            ? instruction.Operand : Variable;
                        break;
                    case OrchestrationValueScheduleCode.Compute:
                        if (operations[instruction.Operand].Code == OrchestrationValueOpcode.HostInput)
                            hostBySlot[slot] = operations[instruction.Operand].Argument;
                        goto case OrchestrationValueScheduleCode.CopyInput;
                    case OrchestrationValueScheduleCode.CopyInput:
                    case OrchestrationValueScheduleCode.ConvertInput:
                    case OrchestrationValueScheduleCode.ReadInputBlackboard:
                    case OrchestrationValueScheduleCode.ReadActionOutput:
                    case OrchestrationValueScheduleCode.OverrideInput:
                    case OrchestrationValueScheduleCode.OverrideOutput:
                    case OrchestrationValueScheduleCode.WritePrediction:
                        literalBySlot[slot] = Variable;
                        break;
                }
            }

            var boundHostInputs = new OrchestrationBoundHostInput[hostBySlot.Count];
            int hostIndex = 0;
            foreach (var slot in hostBySlot)
                boundHostInputs[hostIndex++] = new OrchestrationBoundHostInput(slot.Value, slot.Key);
            Array.Sort(boundHostInputs, (left, right) => left.Input.CompareTo(right.Input));
            header.ScheduleBoundHostInputs = writer.Add<OrchestrationBoundHostInput>(boundHostInputs);
            var slots = new int[literalBySlot.Length];
            var literalSlots = new Dictionary<int, int>();
            var literals = new List<TValue>();
            for (int i = 0; i < slots.Length; i++)
            {
                int literal = literalBySlot[i];
                if (literal < 0)
                {
                    slots[i] = i;
                    continue;
                }
                if (!literalSlots.TryGetValue(literal, out int slot))
                {
                    slot = checked(schedule.ScratchSlotCount + literals.Count);
                    literalSlots.Add(literal, slot);
                    literals.Add(constants[literal]);
                }
                slots[i] = slot;
            }
            header.ScheduleLiterals = writer.Add<TValue>(literals.ToArray());
            var argumentSlots = new List<int>(schedule.ArgumentSlots.ToArray());
            var argumentLists = new List<OrchestrationRange>(schedule.ArgumentLists.ToArray());
            var productArguments = new int[argumentLists.Count];
            Array.Fill(productArguments, -1);

            var lowered = new List<OrchestrationValueScheduleInstruction>(schedule.Instructions.ToArray());
            var productEntries = new int[instructions.Length];
            for (int i = instructions.Length - 1; i >= 0; i--)
            {
                if ((modes[i] & Product) == 0)
                    continue;
                ref readonly OrchestrationValueScheduleInstruction instruction = ref instructions[i];
                OrchestrationValueScheduleCode code = instruction.Code;
                int next = instruction.Next < 0 ? -1 : productEntries[instruction.Next];
                bool boundInput = code switch
                {
                    OrchestrationValueScheduleCode.ReadInputConstant => literalBySlot[instruction.Destination] >= 0,
                    OrchestrationValueScheduleCode.Compute => operations[instruction.Operand].Code == OrchestrationValueOpcode.HostInput,
                    _ => false
                };
                if (boundInput)
                {
                    productEntries[i] = next;
                    continue;
                }
                int alternative = instruction.Alternative < 0 ? -1 : productEntries[instruction.Alternative];
                int operand = code == OrchestrationValueScheduleCode.CopyInput ? slots[instruction.Operand] : instruction.Operand;
                int destination = code is OrchestrationValueScheduleCode.BranchBoolean or OrchestrationValueScheduleCode.BranchCondition
                    ? slots[instruction.Destination] : instruction.Destination;
                int argument = code switch
                {
                    OrchestrationValueScheduleCode.ReturnInputs => BindArguments(instruction.Argument),
                    OrchestrationValueScheduleCode.Compute when
                        operations[instruction.Operand].Code != OrchestrationValueOpcode.GraphCallOutput =>
                        BindArguments(instruction.Argument),
                    OrchestrationValueScheduleCode.ConvertInput => slots[instruction.Argument],
                    _ => instruction.Argument
                };
                productEntries[i] = i;
                if (operand == instruction.Operand && destination == instruction.Destination && argument == instruction.Argument &&
                    next == instruction.Next && alternative == instruction.Alternative)
                    continue;
                var replacement = new OrchestrationValueScheduleInstruction(code, instruction.Operation, operand,
                    destination, argument, next, alternative);
                if ((modes[i] & Workbench) == 0)
                    lowered[i] = replacement;
                else
                {
                    productEntries[i] = lowered.Count;
                    lowered.Add(replacement);
                }
            }
            boundInstructions = lowered.ToArray();
            boundSlots = argumentSlots.ToArray();
            boundLists = argumentLists.ToArray();
            return productEntries;

            int BindArguments(int index)
            {
                if (productArguments[index] >= 0)
                    return productArguments[index];
                OrchestrationRange range = argumentLists[index];
                for (int i = range.Offset; i < range.Offset + range.Count; i++)
                {
                    if (slots[argumentSlots[i]] == argumentSlots[i])
                        continue;
                    int productIndex = argumentLists.Count;
                    argumentLists.Add(new OrchestrationRange(argumentSlots.Count, range.Count));
                    for (int source = range.Offset; source < range.Offset + range.Count; source++)
                        argumentSlots.Add(slots[argumentSlots[source]]);
                    return productArguments[index] = productIndex;
                }
                return productArguments[index] = index;
            }

            void Mark(int index, byte mode)
            {
                if (index >= 0)
                    modes[index] |= mode;
            }

            void MarkEntries(ReadOnlySpan<OrchestrationValueScheduleEntry> entries)
            {
                foreach (ref readonly OrchestrationValueScheduleEntry entry in entries)
                {
                    Mark(entry.Product, Product);
                    Mark(entry.Workbench, Workbench);
                }
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
