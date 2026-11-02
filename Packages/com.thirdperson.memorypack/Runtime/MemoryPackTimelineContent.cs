using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using BTSMTL.Timeline;
using MemoryPack;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackTimelineContent
    {
        const string Format = "btsmtl.timeline.memorypack/3";
        static readonly string s_FormatIdentity = Format + ":" + MemoryPackContent.GetSchemaHash<TimelineContentArtifact>().Value;
        public static TimelineContentParameterCodec<TimelineContentTree> TreeParameters { get; } =
            new TimelineContentParameterCodec<TimelineContentTree>(TimelineContractKinds.TreeClip,
                MemoryPackContent.GetSchemaHash<TimelineContentTree>(),
                value => MemoryPackContent.Serialize(value),
                bytes => MemoryPackContent.Deserialize<TimelineContentTree>(bytes));

        public static TimelineContentParameterCodec<TimelineContentSceneParameter> SceneParameters { get; } =
            new TimelineContentParameterCodec<TimelineContentSceneParameter>(TimelineContractKinds.ScenePresentationParameterCurveClip,
                MemoryPackContent.GetSchemaHash<TimelineContentSceneParameter>(),
                value => MemoryPackContent.Serialize(value),
                bytes => MemoryPackContent.Deserialize<TimelineContentSceneParameter>(bytes));

        static MemoryPackTimelineContent()
        {
            MemoryPackContent.Initialize();
        }

        public static TimelineContentArtifact CreateArtifact(IEnumerable<TimelineContentUnit> timelines,
            TimelineContentParameterCodecs codecs)
        {
            var artifact = new TimelineContentArtifact(timelines, default);
            return artifact.WithContentHash(ComputeContentHash(artifact.Timelines, codecs));
        }

        public static byte[] Write(TimelineContentArtifact artifact, TimelineContentParameterCodecs codecs)
        {
            var options = new MemoryPackSerializerOptions { ServiceProvider = codecs };
            using var state = MemoryPackWriterOptionalStatePool.Rent(options);
            var bytes = new ArrayBufferWriter<byte>();
            var writer = new MemoryPackWriter<ArrayBufferWriter<byte>>(ref bytes, state);
            writer.WriteObjectHeader(2);
            writer.WriteValue(s_FormatIdentity);
            writer.WriteValue(artifact);
            writer.Flush();
            return bytes.WrittenSpan.ToArray();
        }

        public static TimelineContentArtifact Read(ReadOnlySpan<byte> bytes, TimelineContentParameterCodecs codecs)
        {
            var options = new MemoryPackSerializerOptions { ServiceProvider = codecs };
            using var state = MemoryPackReaderOptionalStatePool.Rent(options);
            var reader = new MemoryPackReader(bytes, state);
            try
            {
                if (!reader.TryReadObjectHeader(out byte count) || count != 2)
                    throw new InvalidDataException("Timeline content record is unsupported.");
                if (reader.ReadValue<string>() != s_FormatIdentity)
                    throw new InvalidDataException("Timeline 内容格式或生成 schema 不匹配，请沿正式入口重新发布。");
                TimelineContentArtifact artifact = reader.ReadValue<TimelineContentArtifact>();
                if (reader.Remaining != 0)
                    throw new InvalidDataException("Timeline content contains trailing data.");
                if (ComputeContentHash(artifact.Timelines, codecs) != artifact.ContentHash)
                    throw new InvalidDataException("Timeline content hash does not match its declared content.");
                return artifact;
            }
            finally { reader.Dispose(); }
        }

        public static StableHash ComputeContentHash(IReadOnlyList<TimelineContentUnit> timelines,
            TimelineContentParameterCodecs codecs) =>
            StableHash.Compute(MemoryPackContent.Serialize((s_FormatIdentity, timelines), codecs));

    }
}
