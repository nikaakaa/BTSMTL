using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationTransitionSourceKind : byte
    {
        HostInput,
        Blackboard,
        InputRequest,
        StateRootCompleted,
        StateExitCause,
        Cycle,
        TimelineTime,
        TimelineSection,
        Extension,
        ActionOutput,
        GraphCallOutput,
        Prediction
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationTransitionSource
    {
        public OrchestrationTransitionSource(OrchestrationTransitionSourceKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public readonly OrchestrationTransitionSourceKind Kind;
        public readonly int Index;
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationTransitionCondition
    {
        public OrchestrationTransitionCondition(int edgeIndex, int machineOperationIndex,
            OrchestrationRange sources, int cacheIndex, int snapshotOffset)
        {
            EdgeIndex = edgeIndex;
            MachineOperationIndex = machineOperationIndex;
            Sources = sources;
            CacheIndex = cacheIndex;
            SnapshotOffset = snapshotOffset;
        }

        public readonly int EdgeIndex;
        public readonly int MachineOperationIndex;
        public readonly OrchestrationRange Sources;
        public readonly int CacheIndex;
        public readonly int SnapshotOffset;
    }
}
