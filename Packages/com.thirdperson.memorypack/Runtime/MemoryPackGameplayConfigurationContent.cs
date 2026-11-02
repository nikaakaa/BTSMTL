using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThirdPersonGameplay.Configuration;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackGameplayConfigurationContent
    {
        const string Format = "btsmtl.gameplay-config.memorypack/1";
        static readonly string s_MetadataFormat = Format + ":" + MemoryPackContent.GetSchemaHash<GameplayConfigMetadata>().Value;

        public static byte[] PublishMetadata(GameplayConfigMetadata metadata) =>
            MemoryPackContent.Serialize((s_MetadataFormat, metadata));

        public static GameplayConfigMetadata ReadMetadata(ReadOnlySpan<byte> bytes)
        {
            var content = MemoryPackContent.Deserialize<(string Format, GameplayConfigMetadata Metadata)>(bytes);
            if (content.Format != s_MetadataFormat)
                throw new InvalidDataException("Gameplay 配置元数据格式或生成 schema 不匹配，请沿正式入口重新发布。");
            return content.Metadata;
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
