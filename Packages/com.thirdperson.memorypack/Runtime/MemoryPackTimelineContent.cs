using System;
using BTSMTL.Timeline;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackTimelineContent
    {
        public static StableHash ComputeRevision(string baselineContentHash, ReadOnlyMemory<TimelineClipTimeRange> ranges) =>
            SimulationCanonicalPayloadHash.Compute(MemoryPackContent.Serialize((baselineContentHash, ranges)));
    }
}
