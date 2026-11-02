using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MemoryPack;
using ThirdPersonGameplay.Configuration;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackGameplayConfigurationContent
    {
        const string Format = "btsmtl.gameplay-config.memorypack/1";
        public static string FormatIdentity { get; } = Format + ":" + MemoryPackContent.GetSchemaHash<GameplayConfigMetadata>().Value;

        public static StableHash EncodingIdentity { get; } = StableHash.Compute(FormatIdentity,
            MemoryPackContent.GetSchemaHash<PublishedGameplayConfiguration>().Value,
            MemoryPackContent.GetSchemaHash<GameplayConfigurationSourceContent[]>().Value);

        public static byte[] PublishMetadata(GameplayConfigMetadata metadata) =>
            MemoryPackContent.Serialize((FormatIdentity, metadata));

        public static GameplayConfigMetadata ReadMetadata(ReadOnlySpan<byte> bytes)
        {
            var context = new MemoryPackContentContext(MemoryPackContentPart.Full);
            using var state = MemoryPackReaderOptionalStatePool.Rent(context.Options);
            var reader = new MemoryPackReader(bytes, state);
            try
            {
                if (!reader.TryReadObjectHeader(out byte count) || count != 2 || reader.ReadValue<string>() != FormatIdentity)
                    throw new InvalidDataException("Gameplay 配置元数据格式或生成 schema 不匹配，请沿正式入口重新发布。");
                GameplayConfigMetadata metadata = reader.ReadValue<GameplayConfigMetadata>();
                if (reader.Remaining != 0)
                    throw new InvalidDataException("Gameplay 配置元数据包含尾随数据。");
                return metadata;
            }
            finally { reader.Dispose(); }
        }

        public static StableHash ComputeIdentity(string sourceId, PublishedGameplayConfiguration configuration) =>
            StableHash.Compute(MemoryPackContent.Serialize(
                (Format, sourceId, GameplayConfigGeneratedContract.SchemaIdentity,
                    MemoryPackContent.GetSchemaHash<PublishedGameplayConfiguration>().Value, configuration)));

        public static GameplayConfigurationRuntimeBinding CreateBinding(IEnumerable<GameplayConfigurationSourceContent> sources)
        {
            GameplayConfigurationSourceContent[] ordered = sources.OrderBy(source => source.SourceId, StringComparer.Ordinal).ToArray();
            StableHash contentHash = StableHash.Compute(MemoryPackContent.Serialize(
                ("gameplay-configuration-binding.memorypack/1",
                    MemoryPackContent.GetSchemaHash<GameplayConfigurationSourceContent[]>().Value, ordered)));
            return new GameplayConfigurationRuntimeBinding(ordered, contentHash);
        }
    }
}
