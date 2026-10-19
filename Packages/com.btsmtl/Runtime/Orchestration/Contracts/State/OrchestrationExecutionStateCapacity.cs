namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExecutionStateCapacity
    {
        public OrchestrationExecutionStateCapacity(int frameCount,
            int operationsPerFrame, int valuesPerFrame, int transitionCachesPerFrame,
            int transitionSnapshotsPerFrame)
        {
            FrameCount = frameCount;
            OperationsPerFrame = operationsPerFrame;
            ValuesPerFrame = valuesPerFrame;
            TransitionCachesPerFrame = transitionCachesPerFrame;
            TransitionSnapshotsPerFrame = transitionSnapshotsPerFrame;
        }

        public int FrameCount { get; }
        public int OperationsPerFrame { get; }
        public int ValuesPerFrame { get; }
        public int TransitionCachesPerFrame { get; }
        public int TransitionSnapshotsPerFrame { get; }
        public int OperationCount => checked(FrameCount * OperationsPerFrame);
        public int ValueCount => checked(FrameCount * ValuesPerFrame);
        public int TransitionCacheCount => checked(FrameCount * TransitionCachesPerFrame);
        public int TransitionSnapshotCount => checked(FrameCount * TransitionSnapshotsPerFrame);
    }
}
