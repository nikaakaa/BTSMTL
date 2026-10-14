using System;
using System.Collections.Generic;
using System.IO;
using BTSMTL.Timeline;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackTimelineContent
    {
        const string Format = "btsmtl.timeline.memorypack/2";

        public static TimelineContentArtifact CreateArtifact(IEnumerable<TimelineContentUnit> timelines)
        {
            var artifact = new TimelineContentArtifact(timelines, default);
            return artifact.WithContentHash(ComputeContentHash(artifact.Timelines));
        }

        public static byte[] Write(TimelineContentArtifact artifact) => MemoryPackContent.Serialize((Format, artifact));

        public static TimelineContentArtifact Read(ReadOnlySpan<byte> bytes)
        {
            var content = MemoryPackContent.Deserialize<(string Format, TimelineContentArtifact Artifact)>(bytes);
            if (content.Format != Format)
                throw new InvalidDataException("Timeline content format is unsupported.");
            if (ComputeContentHash(content.Artifact.Timelines) != content.Artifact.ContentHash)
                throw new InvalidDataException("Timeline content hash does not match its declared content.");
            return content.Artifact;
        }

        public static StableHash ComputeContentHash(IReadOnlyList<TimelineContentUnit> timelines) =>
            SimulationCanonicalPayloadHash.Compute(MemoryPackContent.Serialize(timelines));

        public static StableHash ComputeRevision(string baselineContentHash, ReadOnlyMemory<TimelineClipTimeRange> ranges) =>
            SimulationCanonicalPayloadHash.Compute(MemoryPackContent.Serialize((baselineContentHash, ranges)));
    }
}
