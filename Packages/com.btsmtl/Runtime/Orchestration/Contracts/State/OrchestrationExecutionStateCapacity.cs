using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExecutionStateCapacity
    {
        public OrchestrationExecutionStateCapacity(int frameCount,
            int operationsPerFrame, int valuesPerFrame, int savedOutputsPerFrame,
            int transitionCachesPerFrame, int transitionSnapshotsPerFrame,
            ReadOnlyMemory<int> valueKindCounts)
        {
            FrameCount = frameCount;
            OperationsPerFrame = operationsPerFrame;
            ValuesPerFrame = valuesPerFrame;
            SavedOutputsPerFrame = savedOutputsPerFrame;
            TransitionCachesPerFrame = transitionCachesPerFrame;
            TransitionSnapshotsPerFrame = transitionSnapshotsPerFrame;
            ValueKindCounts = valueKindCounts;
        }

        public int FrameCount { get; }
        public int OperationsPerFrame { get; }
        public int ValuesPerFrame { get; }
        public int SavedOutputsPerFrame { get; }
        public ReadOnlyMemory<int> ValueKindCounts { get; }
        public int TransitionCachesPerFrame { get; }
        public int TransitionSnapshotsPerFrame { get; }
        public int OperationCount => checked(FrameCount * OperationsPerFrame);
        public int ValueCount => checked(FrameCount * ValuesPerFrame);
        public int SavedOutputCount => checked(FrameCount * SavedOutputsPerFrame);
        public int TransitionCacheCount => checked(FrameCount * TransitionCachesPerFrame);
        public int TransitionSnapshotCount => checked(FrameCount * TransitionSnapshotsPerFrame);
    }
}
