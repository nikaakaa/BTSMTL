using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationProgramStructure
    {
        public const int FlowKindCount = (int)OrchestrationFlowKind.Interrupt;

        public OrchestrationProgramStructure(
            ReadOnlyMemory<int> outgoingEdgeIndexes,
            ReadOnlyMemory<int> outgoingOffsets,
            ReadOnlyMemory<int> initialStateEntries,
            ReadOnlyMemory<int> anyStateEntries,
            ReadOnlyMemory<int> stateOnEnterEdges,
            ReadOnlyMemory<int> stateRootEdges,
            ReadOnlyMemory<int> stateOnExitEdges,
            ReadOnlyMemory<int> valueEvaluationOffsets,
            ReadOnlyMemory<int> stateOperationIndexes)
        {
            OutgoingEdgeIndexes = outgoingEdgeIndexes;
            OutgoingOffsets = outgoingOffsets;
            InitialStateEntries = initialStateEntries;
            AnyStateEntries = anyStateEntries;
            StateOnEnterEdges = stateOnEnterEdges;
            StateRootEdges = stateRootEdges;
            StateOnExitEdges = stateOnExitEdges;
            ValueEvaluationOffsets = valueEvaluationOffsets;
            StateOperationIndexes = stateOperationIndexes;
        }

        public ReadOnlyMemory<int> OutgoingEdgeIndexes { get; }
        public ReadOnlyMemory<int> OutgoingOffsets { get; }
        public ReadOnlyMemory<int> InitialStateEntries { get; }
        public ReadOnlyMemory<int> AnyStateEntries { get; }
        public ReadOnlyMemory<int> StateOnEnterEdges { get; }
        public ReadOnlyMemory<int> StateRootEdges { get; }
        public ReadOnlyMemory<int> StateOnExitEdges { get; }
        public ReadOnlyMemory<int> ValueEvaluationOffsets { get; }
        public ReadOnlyMemory<int> StateOperationIndexes { get; }
    }
}
