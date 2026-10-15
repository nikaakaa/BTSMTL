using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationProgramContent<TValue> where TValue : unmanaged
    {
        public OrchestrationProgramContent(
            ReadOnlyMemory<OrchestrationOperation> operations,
            ReadOnlyMemory<OrchestrationFlowEdge> edges,
            ReadOnlyMemory<OrchestrationValueInputBinding> valueInputs,
            ReadOnlyMemory<TValue> constants,
            ReadOnlyMemory<TValue> defaultValues,
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
            Edges = edges;
            ValueInputs = valueInputs;
            Constants = constants;
            DefaultValues = defaultValues;
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
        public ReadOnlyMemory<OrchestrationFlowEdge> Edges { get; }
        public ReadOnlyMemory<OrchestrationValueInputBinding> ValueInputs { get; }
        public ReadOnlyMemory<TValue> Constants { get; }
        public ReadOnlyMemory<TValue> DefaultValues { get; }
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
