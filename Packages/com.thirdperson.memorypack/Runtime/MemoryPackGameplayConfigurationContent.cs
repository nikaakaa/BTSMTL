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

        public static byte[] PublishMetadata(GameplayConfigMetadata metadata) =>
            MemoryPackContent.Serialize((Format, metadata));

        public static GameplayConfigMetadata ReadMetadata(ReadOnlySpan<byte> bytes)
        {
            var content = MemoryPackContent.Deserialize<(string Format, GameplayConfigMetadata Metadata)>(bytes);
            if (content.Format != Format)
                throw new InvalidDataException("Gameplay 配置元数据格式不受支持。");
            return content.Metadata;
        }

        public static StableHash ComputeIdentity(string sourceId, PublishedGameplayConfiguration configuration) =>
            SimulationCanonicalPayloadHash.Compute(MemoryPackContent.Serialize(
                (Format, sourceId, GameplayConfigGeneratedContract.SchemaIdentity, configuration)));

        public static GameplayConfigurationRuntimeBinding CreateBinding(IEnumerable<GameplayConfigurationSourceContent> sources)
        {
            GameplayConfigurationSourceContent[] ordered = sources.OrderBy(source => source.SourceId, StringComparer.Ordinal).ToArray();
            StableHash contentHash = SimulationCanonicalPayloadHash.Compute(MemoryPackContent.Serialize(
                ("gameplay-configuration-binding.memorypack/1", ordered)));
            return new GameplayConfigurationRuntimeBinding(ordered, contentHash);
        }
    }
}
