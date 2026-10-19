using System;
using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationProgramContent<TValue> where TValue : unmanaged
    {
        public OrchestrationProgramContent(
            ReadOnlyMemory<OrchestrationOperation> operations,
            ReadOnlyMemory<byte> operationPayloads,
            ReadOnlyMemory<OrchestrationOperationColdData> coldOperations,
            ReadOnlyMemory<OrchestrationFlowEdge> edges,
            OrchestrationProgramStructure structure,
            OrchestrationValueSchedule valueSchedule,
            OrchestrationWorkspaceCapacity workspaceCapacity,
            OrchestrationProgramStorageLayout executionStorage,
            ReadOnlyMemory<OrchestrationValueInputBinding> valueInputs,
            ReadOnlyMemory<TValue> constants,
            ReadOnlyMemory<TValue> defaultValues,
            ReadOnlyMemory<OrchestrationValueSlotType> valueSlotTypes,
            ReadOnlyMemory<int> valueKindSlotCounts,
            ReadOnlyMemory<OrchestrationGraphCall> graphCalls,
            ReadOnlyMemory<OrchestrationHookBinding> hooks,
            ReadOnlyMemory<OrchestrationHookInput> hookInputs,
            ReadOnlyMemory<OrchestrationBlackboardSlot> blackboardSlots,
            ReadOnlyMemory<OrchestrationBlackboardOutputBinding> blackboardOutputs,
            ReadOnlyMemory<OrchestrationOutputTemplate> outputTemplates,
            OrchestrationCorePorts corePorts,
            int rootOperationIndex,
            int maximumOperationEvaluations)
        {
            Operations = operations;
            OperationPayloads = operationPayloads;
            ColdOperations = coldOperations;
            Edges = edges;
            Structure = structure;
            ValueSchedule = valueSchedule;
            WorkspaceCapacity = workspaceCapacity;
            ExecutionStorage = executionStorage;
            ValueInputs = valueInputs;
            Constants = constants;
            DefaultValues = defaultValues;
            ValueSlotTypes = valueSlotTypes;
            ValueKindSlotCounts = valueKindSlotCounts;
            GraphCalls = graphCalls;
            Hooks = hooks;
            HookInputs = hookInputs;
            BlackboardSlots = blackboardSlots;
            BlackboardOutputs = blackboardOutputs;
            OutputTemplates = outputTemplates;
            CorePorts = corePorts;
            RootOperationIndex = rootOperationIndex;
            MaximumOperationEvaluations = maximumOperationEvaluations;
        }

        public ReadOnlyMemory<OrchestrationOperation> Operations { get; }
        public ReadOnlyMemory<byte> OperationPayloads { get; }
        public ReadOnlyMemory<OrchestrationOperationColdData> ColdOperations { get; }
        public T ReadOperationPayload<T>(int operationIndex) where T : unmanaged =>
            MemoryMarshal.Read<T>(OperationPayloads.Span.Slice(Operations.Span[operationIndex].PayloadOffset));
        public ReadOnlyMemory<OrchestrationFlowEdge> Edges { get; }
        public OrchestrationProgramStructure Structure { get; }
        public OrchestrationValueSchedule ValueSchedule { get; }
        public OrchestrationWorkspaceCapacity WorkspaceCapacity { get; }
        public OrchestrationProgramStorageLayout ExecutionStorage { get; }
        public ReadOnlyMemory<OrchestrationValueInputBinding> ValueInputs { get; }
        public ReadOnlyMemory<TValue> Constants { get; }
        public ReadOnlyMemory<TValue> DefaultValues { get; }
        public ReadOnlyMemory<OrchestrationValueSlotType> ValueSlotTypes { get; }
        public ReadOnlyMemory<int> ValueKindSlotCounts { get; }
        public ReadOnlyMemory<OrchestrationGraphCall> GraphCalls { get; }
        public ReadOnlyMemory<OrchestrationHookBinding> Hooks { get; }
        public ReadOnlyMemory<OrchestrationHookInput> HookInputs { get; }
        public ReadOnlyMemory<OrchestrationBlackboardSlot> BlackboardSlots { get; }
        public ReadOnlyMemory<OrchestrationBlackboardOutputBinding> BlackboardOutputs { get; }
        public ReadOnlyMemory<OrchestrationOutputTemplate> OutputTemplates { get; }
        public OrchestrationCorePorts CorePorts { get; }
        public int RootOperationIndex { get; }
        public int MaximumOperationEvaluations { get; }
    }
}
