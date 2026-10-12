using System;

namespace ThirdPersonSimulation
{
    public sealed class CharacterGameplayEffectRuntimeBinding
    {
        public const int ContractSemanticVersion = 3;
        public const int CatalogFormatVersion = 3;
        public const string CodecIdentity = "character-gameplay-effect-runtime-binding/v3";

        readonly byte[] m_CatalogBytes;
        GameplayAttributeDependencyLayout m_AttributeDependencies;

        public CharacterGameplayEffectRuntimeBinding(
            string sourceIdentity,
            int semanticVersion,
            GameplayEffectActorCapacity actorCapacity,
            GameplayEffectContentLayout contentLayout,
            byte[] catalogBytes)
        {
            SourceIdentity = SimulationIdentity.Require(sourceIdentity, nameof(sourceIdentity));
            if (semanticVersion != ContractSemanticVersion)
                throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.UnsupportedVersion,
                    GameplayEffectCapacityDimension.ContentVersion, SourceIdentity,
                    ContractSemanticVersion, semanticVersion));
            if (catalogBytes == null || catalogBytes.Length == 0)
                throw new ArgumentException("Character Gameplay Effect catalog bytes are required.", nameof(catalogBytes));
            if (!actorCapacity.TryValidate(contentLayout, SourceIdentity, out GameplayEffectPreparationFailure failure))
                throw new GameplayEffectPreparationException(failure);
            ActorCapacity = actorCapacity;
            ContentLayout = contentLayout;
            SemanticVersion = semanticVersion;
            m_CatalogBytes = (byte[])catalogBytes.Clone();
            using var contentWriter = new CanonicalWriter();
            GameplayEffectActorCapacityCodec.Write(contentWriter, in actorCapacity);
            GameplayEffectContentLayoutCodec.Write(contentWriter, contentLayout);
            contentWriter.WriteBytes(m_CatalogBytes);
            ContentRevision = SimulationCanonicalPayloadHash.Compute(contentWriter.ToArray());
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
        public byte[] CatalogBytes => (byte[])m_CatalogBytes.Clone();
        public GameplayEffectActorCapacity ActorCapacity { get; }
        public GameplayEffectContentLayout ContentLayout { get; }
        public GameplayAttributeDependencyLayout AttributeDependencies => m_AttributeDependencies;

        public GameplayAttributeDependencyLayout PrepareAttributeDependencies(Func<GameplayAttributeDependencyLayout> prepare) =>
            m_AttributeDependencies ??= prepare();
    }

    public static class CharacterGameplayEffectRuntimeBindingCodec
    {
        public static byte[] Write(CharacterGameplayEffectRuntimeBinding binding)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            using var writer = new CanonicalWriter();
            writer.WriteText(CharacterGameplayEffectRuntimeBinding.CodecIdentity);
            writer.WriteText(binding.SourceIdentity);
            writer.WriteInt32(binding.SemanticVersion);
            GameplayEffectActorCapacity capacity = binding.ActorCapacity;
            GameplayEffectActorCapacityCodec.Write(writer, in capacity);
            GameplayEffectContentLayoutCodec.Write(writer, binding.ContentLayout);
            writer.WriteBytes(binding.CatalogBytes);
            return writer.ToArray();
        }

        public static CharacterGameplayEffectRuntimeBinding Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes ?? throw new ArgumentNullException(nameof(bytes)));
            if (!string.Equals(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), CharacterGameplayEffectRuntimeBinding.CodecIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("Character Gameplay Effect runtime binding codec identity is unsupported.");
            var binding = new CharacterGameplayEffectRuntimeBinding(
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                reader.ReadInt32(),
                GameplayEffectActorCapacityCodec.Read(reader),
                GameplayEffectContentLayoutCodec.Read(reader),
                reader.ReadBytes());
            reader.RequireComplete();
            return binding;
        }
    }
}
