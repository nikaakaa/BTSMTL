using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationProgramContent
    {
        public OrchestrationProgramContent(
            ReadOnlyMemory<OrchestrationOperation> operations,
            ReadOnlyMemory<OrchestrationOperationColdData> coldOperations,
            ReadOnlyMemory<OrchestrationFlowEdge> edges,
            OrchestrationProgramStructure structure,
            OrchestrationValueSchedule valueSchedule,
            OrchestrationWorkspaceCapacity workspaceCapacity,
            OrchestrationProgramStorageLayout executionStorage,
            ReadOnlyMemory<OrchestrationValueInputBinding> valueInputs,
            ReadOnlyMemory<OrchestrationValueSlotType> valueSlotTypes,
            ReadOnlyMemory<int> valueKindSlotCounts,
            ReadOnlyMemory<OrchestrationGraphCall> graphCalls,
            ReadOnlyMemory<OrchestrationHookBinding> hooks,
            ReadOnlyMemory<OrchestrationBlackboardSlot> blackboardSlots,
            ReadOnlyMemory<OrchestrationBlackboardOutputBinding> blackboardOutputs,
            ReadOnlyMemory<OrchestrationOutputTemplate> outputTemplates,
            OrchestrationCorePorts corePorts,
            int rootOperationIndex,
            int maximumOperationEvaluations)
        {
            Operations = operations;
            ColdOperations = coldOperations;
            Edges = edges;
            Structure = structure;
            ValueSchedule = valueSchedule;
            WorkspaceCapacity = workspaceCapacity;
            ExecutionStorage = executionStorage;
            ValueInputs = valueInputs;
            ValueSlotTypes = valueSlotTypes;
            ValueKindSlotCounts = valueKindSlotCounts;
            GraphCalls = graphCalls;
            Hooks = hooks;
            BlackboardSlots = blackboardSlots;
            BlackboardOutputs = blackboardOutputs;
            OutputTemplates = outputTemplates;
            CorePorts = corePorts;
            RootOperationIndex = rootOperationIndex;
            MaximumOperationEvaluations = maximumOperationEvaluations;
        }

        public ReadOnlyMemory<OrchestrationOperation> Operations { get; }
        public ReadOnlyMemory<OrchestrationOperationColdData> ColdOperations { get; }
        public ReadOnlyMemory<OrchestrationFlowEdge> Edges { get; }
        public OrchestrationProgramStructure Structure { get; }
        public OrchestrationValueSchedule ValueSchedule { get; }
        public OrchestrationWorkspaceCapacity WorkspaceCapacity { get; }
        public OrchestrationProgramStorageLayout ExecutionStorage { get; }
        public ReadOnlyMemory<OrchestrationValueInputBinding> ValueInputs { get; }
        public ReadOnlyMemory<OrchestrationValueSlotType> ValueSlotTypes { get; }
        public ReadOnlyMemory<int> ValueKindSlotCounts { get; }
        public ReadOnlyMemory<OrchestrationGraphCall> GraphCalls { get; }
        public ReadOnlyMemory<OrchestrationHookBinding> Hooks { get; }
        public ReadOnlyMemory<OrchestrationBlackboardSlot> BlackboardSlots { get; }
        public ReadOnlyMemory<OrchestrationBlackboardOutputBinding> BlackboardOutputs { get; }
        public ReadOnlyMemory<OrchestrationOutputTemplate> OutputTemplates { get; }
        public OrchestrationCorePorts CorePorts { get; }
        public int RootOperationIndex { get; }
        public int MaximumOperationEvaluations { get; }
    }
}
