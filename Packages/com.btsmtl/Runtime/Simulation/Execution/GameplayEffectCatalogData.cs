using System.Collections.Generic;
using ThirdPersonGameplay.Configuration;

namespace ThirdPersonSimulation
{
    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct GameplayEffectTagContent
    {
        public GameplayEffectTagContent(string id, string parent, bool initiallyOwned)
        {
            Id = id;
            Parent = parent;
            InitiallyOwned = initiallyOwned;
        }

        public string Id { get; }
        public string Parent { get; }
        public bool InitiallyOwned { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class GameplayEffectCatalogData
    {
        public GameplayEffectCatalogData(int version, string sourceId, string configurationIdentity,
            PublishedGameplayConfiguration configuration, IReadOnlyList<GameplayEffectTagContent> tags)
        {
            Version = version;
            SourceId = sourceId;
            ConfigurationIdentity = configurationIdentity;
            Configuration = configuration;
            Tags = tags;
        }

        public int Version { get; }
        public string SourceId { get; }
        public string ConfigurationIdentity { get; }
        public PublishedGameplayConfiguration Configuration { get; }
        public IReadOnlyList<GameplayEffectTagContent> Tags { get; }
    }
}
