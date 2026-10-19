using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExecutionStateCapacity
    {
        public OrchestrationExecutionStateCapacity(int frameCount,
            int operationsPerFrame, int valuesPerFrame, int savedOutputsPerFrame,
            ReadOnlyMemory<int> valueKindCounts)
        {
            FrameCount = frameCount;
            OperationsPerFrame = operationsPerFrame;
            ValuesPerFrame = valuesPerFrame;
            SavedOutputsPerFrame = savedOutputsPerFrame;
            ValueKindCounts = valueKindCounts;
        }

        public int FrameCount { get; }
        public int OperationsPerFrame { get; }
        public int ValuesPerFrame { get; }
        public int SavedOutputsPerFrame { get; }
        public ReadOnlyMemory<int> ValueKindCounts { get; }
        public int OperationCount => checked(FrameCount * OperationsPerFrame);
        public int ValueCount => checked(FrameCount * ValuesPerFrame);
        public int SavedOutputCount => checked(FrameCount * SavedOutputsPerFrame);
    }
}
