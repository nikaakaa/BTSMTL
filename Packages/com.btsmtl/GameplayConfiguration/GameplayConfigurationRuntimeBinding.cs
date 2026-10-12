using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThirdPersonSimulation;

namespace ThirdPersonGameplay.Configuration
{
    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class GameplayConfigurationSourceContent
    {
        public GameplayConfigurationSourceContent(string sourceId, string configurationIdentity, PublishedGameplayConfiguration configuration)
        {
            SourceId = sourceId;
            ConfigurationIdentity = configurationIdentity;
            Configuration = configuration;
        }

        public string SourceId { get; }
        public string ConfigurationIdentity { get; }
        public PublishedGameplayConfiguration Configuration { get; }
    }

    public sealed class GameplayConfigurationRuntimeBinding
    {
        const string Identity = "gameplay-configuration-binding/1";
        readonly Dictionary<string, GameplayConfigurationSourceContent> m_Sources;
        readonly byte[] m_CanonicalBytes;

        public GameplayConfigurationRuntimeBinding(IEnumerable<GameplayConfigurationSourceContent> sources)
        {
            m_Sources = sources.ToDictionary(value => value.SourceId, StringComparer.Ordinal);
            using var writer = new CanonicalWriter();
            writer.WriteText(Identity);
            writer.WriteHash(GameplayConfigurationSourceContent.CanonicalSchemaHash);
            writer.WriteInt32(m_Sources.Count);
            foreach (GameplayConfigurationSourceContent source in m_Sources.Values.OrderBy(value => value.SourceId, StringComparer.Ordinal))
                GameplayConfigurationSourceContent.CanonicalWrite(writer, source);
            ContentHash = writer.ComputeHash();
            m_CanonicalBytes = writer.ToArray();
        }

        public StableHash ContentHash { get; }
        public GameplayConfigurationSourceContent RequireSource(string sourceId) => m_Sources[sourceId];
        public byte[] CopyCanonicalArtifact() => (byte[])m_CanonicalBytes.Clone();

        public static GameplayConfigurationRuntimeBinding Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()) != Identity)
                throw new InvalidDataException("Gameplay configuration binding format is unsupported.");
            GameplayConfigurationSourceContent.CanonicalRequireSchema(reader);
            var sources = new GameplayConfigurationSourceContent[reader.ReadInt32()];
            for (int index = 0; index < sources.Length; index++)
                sources[index] = GameplayConfigurationSourceContent.CanonicalReadMaterialized(reader);
            reader.RequireComplete();
            return new GameplayConfigurationRuntimeBinding(sources);
        }
    }
}
