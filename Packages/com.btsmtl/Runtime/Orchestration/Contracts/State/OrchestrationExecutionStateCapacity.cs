using System;
using System.Globalization;
using System.Text;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExecutionFramePoolCapacity
    {
        public OrchestrationExecutionFramePoolCapacity(int frameCount,
            int operationsPerFrame, int valuesPerFrame, int savedOutputsPerFrame,
            ReadOnlyMemory<int> valueKindCounts, OrchestrationOperationStateStorage operationStates)
        {
            FrameCount = frameCount;
            OperationsPerFrame = operationsPerFrame;
            ValuesPerFrame = valuesPerFrame;
            SavedOutputsPerFrame = savedOutputsPerFrame;
            ValueKindCounts = valueKindCounts;
            OperationStates = operationStates;
        }

        public int FrameCount { get; }
        public int OperationsPerFrame { get; }
        public int ValuesPerFrame { get; }
        public int SavedOutputsPerFrame { get; }
        public ReadOnlyMemory<int> ValueKindCounts { get; }
        public OrchestrationOperationStateStorage OperationStates { get; }

        internal int PayloadElementCount(in OrchestrationStateSegment segment)
        {
            switch (segment.Role)
            {
                case OrchestrationStateSegmentRole.Operations: return OperationsPerFrame;
                case OrchestrationStateSegmentRole.OperationStops: return OperationStates.StopContexts;
                case OrchestrationStateSegmentRole.StateMachines: return OperationStates.StateMachines;
                case OrchestrationStateSegmentRole.TimelineHandles: return OperationStates.Timelines;
                case OrchestrationStateSegmentRole.SavedOutputs: return SavedOutputsPerFrame;
                case OrchestrationStateSegmentRole.PrivateMetadata: return ValuesPerFrame;
                case OrchestrationStateSegmentRole.PrivateValues:
                    if (segment.ValueKind == 0)
                        return ValuesPerFrame;
                    int count = ValueKindCounts.Span[(int)segment.ValueKind];
                    return segment.ValueKind == OrchestrationValueKind.Boolean ? (count + 63) / 64 : count;
                default: throw new InvalidOperationException("区段不是执行帧载荷。");
            }
        }
    }

    public readonly struct OrchestrationExecutionStateCapacity
    {
        public OrchestrationExecutionStateCapacity(ReadOnlyMemory<OrchestrationExecutionFramePoolCapacity> framePools,
            ReadOnlyMemory<ReadOnlyMemory<int>> ownerPools)
        {
            this = default;
            FramePools = framePools;
            OwnerPools = ownerPools;
            var kinds = new int[(int)OrchestrationValueKind.SpatialIdentity + 1];
            foreach (ref readonly OrchestrationExecutionFramePoolCapacity pool in framePools.Span)
            {
                FrameCount = checked(FrameCount + pool.FrameCount);
                OperationCount = checked(OperationCount + pool.FrameCount * pool.OperationsPerFrame);
                OperationStopCount = checked(OperationStopCount + pool.FrameCount * pool.OperationStates.StopContexts);
                StateMachineCount = checked(StateMachineCount + pool.FrameCount * pool.OperationStates.StateMachines);
                TimelineHandleCount = checked(TimelineHandleCount + pool.FrameCount * pool.OperationStates.Timelines);
                ValueCount = checked(ValueCount + pool.FrameCount * pool.ValuesPerFrame);
                SavedOutputCount = checked(SavedOutputCount + pool.FrameCount * pool.SavedOutputsPerFrame);
                for (int kind = 0; kind < kinds.Length; kind++)
                {
                    int count = pool.ValueKindCounts.Span[kind];
                    if (kind == (int)OrchestrationValueKind.Boolean)
                        count = checked((count + 63) / 64 * 64);
                    kinds[kind] = checked(kinds[kind] + pool.FrameCount * count);
                }
            }
            ValueKindCounts = kinds;
        }

        public ReadOnlyMemory<OrchestrationExecutionFramePoolCapacity> FramePools { get; }
        public ReadOnlyMemory<ReadOnlyMemory<int>> OwnerPools { get; }
        public int FrameCount { get; }
        public int OperationCount { get; }
        public int OperationStopCount { get; }
        public int StateMachineCount { get; }
        public int TimelineHandleCount { get; }
        public int ValueCount { get; }
        public int SavedOutputCount { get; }
        public ReadOnlyMemory<int> ValueKindCounts { get; }

        internal OrchestrationExecutionStateCapacity PrepareSource(int frameCount)
        {
            int operations = 0, values = 0, outputs = 0;
            int stops = 0, machines = 0, timelines = 0;
            var kinds = new int[(int)OrchestrationValueKind.SpatialIdentity + 1];
            foreach (ref readonly OrchestrationExecutionFramePoolCapacity pool in FramePools.Span)
            {
                operations = Math.Max(operations, pool.OperationsPerFrame);
                stops = Math.Max(stops, pool.OperationStates.StopContexts);
                machines = Math.Max(machines, pool.OperationStates.StateMachines);
                timelines = Math.Max(timelines, pool.OperationStates.Timelines);
                values = Math.Max(values, pool.ValuesPerFrame);
                outputs = Math.Max(outputs, pool.SavedOutputsPerFrame);
                for (int kind = 0; kind < kinds.Length; kind++)
                    kinds[kind] = Math.Max(kinds[kind], pool.ValueKindCounts.Span[kind]);
            }
            var ownerPools = new ReadOnlyMemory<int>[OwnerPools.Length];
            for (int program = 0; program < ownerPools.Length; program++)
                ownerPools[program] = new int[OwnerPools.Span[program].Length];
            return new OrchestrationExecutionStateCapacity(new[]
            {
                new OrchestrationExecutionFramePoolCapacity(frameCount, operations, values, outputs, kinds,
                    new OrchestrationOperationStateStorage(stops, machines, timelines))
            }, ownerPools);
        }

        public StableHash BindIdentity(StableHash identity)
        {
            var text = new StringBuilder();
            foreach (ref readonly OrchestrationExecutionFramePoolCapacity pool in FramePools.Span)
            {
                text.Append(pool.FrameCount.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(pool.OperationsPerFrame.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(pool.ValuesPerFrame.ToString(CultureInfo.InvariantCulture)).Append(':')
                    .Append(pool.SavedOutputsPerFrame.ToString(CultureInfo.InvariantCulture));
                text.Append(':').Append(pool.OperationStates.StopContexts.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(pool.OperationStates.StateMachines.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(pool.OperationStates.Timelines.ToString(CultureInfo.InvariantCulture));
                foreach (int count in pool.ValueKindCounts.Span)
                    text.Append(':').Append(count.ToString(CultureInfo.InvariantCulture));
                text.Append(';');
            }
            foreach (ReadOnlyMemory<int> owners in OwnerPools.Span)
            {
                foreach (int pool in owners.Span)
                    text.Append(pool.ToString(CultureInfo.InvariantCulture)).Append(',');
                text.Append(';');
            }
            return StableHash.Compute(identity.ToString(), "execution-frame-pools/3", text.ToString());
        }
    }
}
