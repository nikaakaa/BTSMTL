using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationValueScheduleCode : byte
    {
        Return,
        ReturnInputs,
        ReadInputConstant,
        ReadInputBlackboard,
        CopyInput,
        ConvertInput,
        Compute,
        ReadActionOutput,
        OverrideInput,
        OverrideOutput,
        ObserveInput,
        ObserveOutput,
        BranchBoolean,
        BranchExcludedTarget,
        BranchCondition,
        EnterPrediction,
        LeavePrediction,
        WritePrediction,
        ReadTransitionCache,
        WriteTransitionCache
    }

    public enum OrchestrationValueSelectionReason : byte
    {
        None,
        SelfAbort
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationValueScheduleInstruction
    {
        public OrchestrationValueScheduleInstruction(OrchestrationValueScheduleCode code,
            int operation, int operand, int destination, int argument, int next, int alternative)
        {
            Code = code;
            Operation = operation;
            Operand = operand;
            Destination = destination;
            Argument = argument;
            Next = next;
            Alternative = alternative;
        }

        public readonly OrchestrationValueScheduleCode Code;
        public readonly int Operation;
        public readonly int Operand;
        public readonly int Destination;
        public readonly int Argument;
        public readonly int Next;
        public readonly int Alternative;
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationValueScheduleEntry
    {
        public OrchestrationValueScheduleEntry(int product, int workbench)
        {
            Product = product;
            Workbench = workbench;
        }

        public readonly int Product;
        public readonly int Workbench;
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationValueSchedule
    {
        public OrchestrationValueSchedule(ReadOnlyMemory<OrchestrationValueScheduleInstruction> instructions,
            ReadOnlyMemory<int> argumentSlots, ReadOnlyMemory<OrchestrationRange> argumentLists,
            ReadOnlyMemory<OrchestrationValueScheduleEntry> valueEntries,
            ReadOnlyMemory<OrchestrationValueScheduleEntry> inputEntries,
            ReadOnlyMemory<OrchestrationValueScheduleEntry> transitionEntries,
            ReadOnlyMemory<OrchestrationValueScheduleEntry> childSelectionEntries,
            ReadOnlyMemory<OrchestrationValueScheduleEntry> selectorAbortEntries,
            ReadOnlyMemory<OrchestrationValueScheduleEntry> booleanEntries,
            ReadOnlyMemory<OrchestrationValueOperation> valueOperations,
            ReadOnlyMemory<int> stateContexts, int valueSlotCount, int inputBindingCount,
            int scratchSlotCount, int predictionDepth,
            ReadOnlyMemory<OrchestrationTransitionCondition> transitionConditions,
            ReadOnlyMemory<OrchestrationTransitionSource> transitionSources,
            int transitionCacheCount, int transitionSnapshotCount)
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
            TransitionConditions = transitionConditions;
            TransitionSources = transitionSources;
            TransitionCacheCount = transitionCacheCount;
            TransitionSnapshotCount = transitionSnapshotCount;
        }

        public ReadOnlyMemory<OrchestrationValueScheduleInstruction> Instructions { get; }
        public ReadOnlyMemory<int> ArgumentSlots { get; }
        public ReadOnlyMemory<OrchestrationRange> ArgumentLists { get; }
        public ReadOnlyMemory<OrchestrationValueScheduleEntry> ValueEntries { get; }
        public ReadOnlyMemory<OrchestrationValueScheduleEntry> InputEntries { get; }
        public ReadOnlyMemory<OrchestrationValueScheduleEntry> TransitionEntries { get; }
        public ReadOnlyMemory<OrchestrationValueScheduleEntry> ChildSelectionEntries { get; }
        public ReadOnlyMemory<OrchestrationValueScheduleEntry> SelectorAbortEntries { get; }
        public ReadOnlyMemory<OrchestrationValueScheduleEntry> BooleanEntries { get; }
        public ReadOnlyMemory<OrchestrationValueOperation> ValueOperations { get; }
        public ReadOnlyMemory<int> StateContexts { get; }
        public int ValueSlotCount { get; }
        public int InputBindingCount { get; }
        public int ScratchSlotCount { get; }
        public int PredictionDepth { get; }
        public int BooleanSlot => ValueSlotCount + InputBindingCount;
        public ReadOnlyMemory<OrchestrationTransitionCondition> TransitionConditions { get; }
        public ReadOnlyMemory<OrchestrationTransitionSource> TransitionSources { get; }
        public int TransitionCacheCount { get; }
        public int TransitionSnapshotCount { get; }
    }
}
