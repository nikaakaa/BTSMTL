using System;

namespace ThirdPersonSimulation
{
    public sealed class CharacterGameplayEffectRuntimeBinding
    {
        public const int ContractSemanticVersion = 4;
        public const int CatalogFormatVersion = 4;
        public const string CodecIdentity = "character-gameplay-effect-runtime-binding/v4";

        GameplayAttributeDependencyLayout m_AttributeDependencies;

        public CharacterGameplayEffectRuntimeBinding(
            string sourceIdentity,
            StableHash contentRevision,
            int semanticVersion,
            GameplayEffectActorCapacity actorCapacity,
            GameplayEffectContentLayout contentLayout,
            GameplayEffectCatalogData content)
        {
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            if (semanticVersion != ContractSemanticVersion)
                throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.UnsupportedVersion,
                    GameplayEffectCapacityDimension.ContentVersion, SourceIdentity,
                    ContractSemanticVersion, semanticVersion));
            if (!actorCapacity.TryValidate(contentLayout, SourceIdentity, out GameplayEffectPreparationFailure failure))
                throw new GameplayEffectPreparationException(failure);
            ActorCapacity = actorCapacity;
            ContentLayout = contentLayout;
            SemanticVersion = semanticVersion;
            Content = content;
            ContentRevision = contentRevision;
            BindingHash = StableHash.Compute(
                CodecIdentity,
                SourceIdentity,
                ContentRevision.ToString(),
                SemanticVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public string SourceIdentity { get; }
        public StableHash ContentRevision { get; }
        public int SemanticVersion { get; }
        public StableHash BindingHash { get; }
        public GameplayEffectCatalogData Content { get; }
        public GameplayEffectActorCapacity ActorCapacity { get; }
        public GameplayEffectContentLayout ContentLayout { get; }
        public GameplayAttributeDependencyLayout AttributeDependencies => m_AttributeDependencies;

        public GameplayAttributeDependencyLayout PrepareAttributeDependencies(Func<GameplayAttributeDependencyLayout> prepare) =>
            m_AttributeDependencies ??= prepare();
    }

}
