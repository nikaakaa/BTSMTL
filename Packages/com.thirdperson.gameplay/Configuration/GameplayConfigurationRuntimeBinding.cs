using System;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;

namespace ThirdPersonGameplay.Configuration
{
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
        readonly Dictionary<string, GameplayConfigurationSourceContent> m_Sources;

        public GameplayConfigurationRuntimeBinding(IEnumerable<GameplayConfigurationSourceContent> sources, StableHash contentHash)
        {
            m_Sources = sources.ToDictionary(value => value.SourceId, StringComparer.Ordinal);
            ContentHash = contentHash;
        }

        public StableHash ContentHash { get; }
        public GameplayConfigurationSourceContent RequireSource(string sourceId) => m_Sources[sourceId];
    }
}
