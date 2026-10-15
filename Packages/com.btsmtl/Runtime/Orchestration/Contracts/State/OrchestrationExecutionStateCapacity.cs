namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExecutionStateCapacity
    {
        public OrchestrationExecutionStateCapacity(int frameCount,
            int operationsPerFrame, int valuesPerFrame)
        {
            FrameCount = frameCount;
            OperationsPerFrame = operationsPerFrame;
            ValuesPerFrame = valuesPerFrame;
        }

        public int FrameCount { get; }
        public int OperationsPerFrame { get; }
        public int ValuesPerFrame { get; }
        public int OperationCount => checked(FrameCount * OperationsPerFrame);
        public int ValueCount => checked(FrameCount * ValuesPerFrame);
    }
}
