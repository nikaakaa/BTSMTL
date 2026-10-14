using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public static class GameplayEffectRuntimeIdentity
    {
        const string AttributePrefix = "attribute:";

        public static string Tag(string value) => Normalize(value, "tag:", "GameplayTag");
        public static string Attribute(string value) => Normalize(value, AttributePrefix, "GameplayAttribute");
        public static string Effect(string value) => Normalize(value, "effect:", "GameplayEffect");

        internal static CanonicalTextValue AttributeCaptureKey(CanonicalTextValue trimmedIdentity) =>
            trimmedIdentity.StartsWith(AttributePrefix, StringComparison.Ordinal)
                ? trimmedIdentity.Substring(AttributePrefix.Length)
                : trimmedIdentity;

        static string Normalize(string value, string prefix, string parameterName)
        {
            string identity = SimulationIdentity.Require(value, parameterName);
            return identity.StartsWith(prefix, StringComparison.Ordinal) ? identity : prefix + identity;
        }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class GameplayEffectAdditionalLayout
    {
        readonly int[] m_SourceCaptureIndices;

        public GameplayEffectAdditionalLayout(int trigger, int componentIndex, int declarationIndex,
            int effectIndex, int[] sourceCaptureIndices)
        {
            Trigger = trigger;
            ComponentIndex = componentIndex;
            DeclarationIndex = declarationIndex;
            EffectIndex = effectIndex;
            m_SourceCaptureIndices = (int[])sourceCaptureIndices.Clone();
        }

        public int Trigger { get; }
        public int ComponentIndex { get; }
        public int DeclarationIndex { get; }
        public int EffectIndex { get; }
        public ReadOnlySpan<int> SourceCaptureIndices => m_SourceCaptureIndices;
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class GameplayEffectDefinitionLayout
    {
        readonly ReadOnlyCollection<string> m_ParameterIds;
        readonly ReadOnlyCollection<string> m_OwnSourceAttributes;
        readonly ReadOnlyCollection<string> m_SourceAttributes;
        readonly CanonicalTextValue[] m_SourceAttributeKeys;
        readonly ReadOnlyCollection<string> m_TargetAttributes;
        readonly int[] m_SourceAttributeIndices;
        readonly int[] m_TargetAttributeIndices;
        readonly ReadOnlyCollection<GameplayEffectAdditionalLayout> m_Additional;
        readonly int[][] m_AdditionalDeclarationOrder;
        readonly int[] m_AdditionalApplicationCounts;
        readonly Dictionary<(int Component, int Declaration), int> m_AdditionalIndices;

        public GameplayEffectDefinitionLayout(string effectId, uint definitionRevision, string[] parameterIds,
            string[] ownSourceAttributes, string[] sourceAttributes, string[] targetAttributes,
            int[] sourceAttributeIndices, int[] targetAttributeIndices,
            int persistentModifiers, int attributeMutations, int grantedTags, int cues,
            bool createsActiveEffect, bool hasPeriod, GameplayEffectAdditionalLayout[] additional,
            int[][] additionalDeclarationOrder, int[] additionalApplicationCounts, int maximumApplicationAdditionalCount)
        {
            EffectId = effectId;
            DefinitionRevision = definitionRevision;
            m_ParameterIds = Array.AsReadOnly((string[])parameterIds.Clone());
            m_OwnSourceAttributes = Array.AsReadOnly((string[])ownSourceAttributes.Clone());
            m_SourceAttributes = Array.AsReadOnly((string[])sourceAttributes.Clone());
            m_SourceAttributeKeys = new CanonicalTextValue[m_SourceAttributes.Count];
            for (int index = 0; index < m_SourceAttributeKeys.Length; index++)
                m_SourceAttributeKeys[index] = GameplayEffectRuntimeIdentity.AttributeCaptureKey(m_SourceAttributes[index]);
            m_TargetAttributes = Array.AsReadOnly((string[])targetAttributes.Clone());
            m_SourceAttributeIndices = (int[])sourceAttributeIndices.Clone();
            m_TargetAttributeIndices = (int[])targetAttributeIndices.Clone();
            PersistentModifiers = persistentModifiers;
            AttributeMutations = attributeMutations;
            GrantedTags = grantedTags;
            Cues = cues;
            CreatesActiveEffect = createsActiveEffect;
            HasPeriod = hasPeriod;
            m_Additional = Array.AsReadOnly((GameplayEffectAdditionalLayout[])additional.Clone());
            m_AdditionalIndices = new Dictionary<(int, int), int>(additional.Length);
            for (int index = 0; index < additional.Length; index++)
                m_AdditionalIndices.Add((additional[index].ComponentIndex, additional[index].DeclarationIndex), index);
            m_AdditionalDeclarationOrder = new int[additionalDeclarationOrder.Length][];
            for (int index = 0; index < additionalDeclarationOrder.Length; index++)
                m_AdditionalDeclarationOrder[index] = (int[])additionalDeclarationOrder[index].Clone();
            m_AdditionalApplicationCounts = (int[])additionalApplicationCounts.Clone();
            MaximumApplicationAdditionalCount = maximumApplicationAdditionalCount;
        }

        public string EffectId { get; }
        public uint DefinitionRevision { get; }
        public IReadOnlyList<string> ParameterIds => m_ParameterIds;
        public IReadOnlyList<string> OwnSourceAttributes => m_OwnSourceAttributes;
        public IReadOnlyList<string> SourceAttributes => m_SourceAttributes;
        public CanonicalTextValue SourceAttributeKey(int index) => m_SourceAttributeKeys[index];
        public IReadOnlyList<string> TargetAttributes => m_TargetAttributes;
        public ReadOnlySpan<int> SourceAttributeIndices => m_SourceAttributeIndices;
        public ReadOnlySpan<int> TargetAttributeIndices => m_TargetAttributeIndices;
        public int PersistentModifiers { get; }
        public int AttributeMutations { get; }
        public int GrantedTags { get; }
        public int Cues { get; }
        public bool CreatesActiveEffect { get; }
        public bool HasPeriod { get; }
        public IReadOnlyList<GameplayEffectAdditionalLayout> Additional => m_Additional;
        public int MaximumApplicationAdditionalCount { get; }
        public ReadOnlySpan<int> AdditionalDeclarationOrder(int trigger) => m_AdditionalDeclarationOrder[trigger];
        public int AdditionalApplicationCount(int trigger) => m_AdditionalApplicationCounts[trigger];
        public GameplayEffectAdditionalLayout AdditionalDeclaration(int componentIndex, int declarationIndex) =>
            m_Additional[m_AdditionalIndices[(componentIndex, declarationIndex)]];
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class GameplayEffectContentLayout
    {
        public const int FormatVersion = 3;
        readonly ReadOnlyCollection<GameplayEffectDefinitionLayout> m_Definitions;
        readonly Dictionary<string, int> m_DefinitionIndices;

        public GameplayEffectContentLayout(int tagCount, int attributeCount, int initialTagCount,
            GameplayEffectDefinitionLayout[] definitions)
        {
            TagCount = tagCount;
            AttributeCount = attributeCount;
            InitialTagCount = initialTagCount;
            m_Definitions = Array.AsReadOnly((GameplayEffectDefinitionLayout[])definitions.Clone());
            m_DefinitionIndices = new Dictionary<string, int>(definitions.Length, StringComparer.Ordinal);
            for (int index = 0; index < definitions.Length; index++)
            {
                GameplayEffectDefinitionLayout definition = definitions[index];
                m_DefinitionIndices.Add(definition.EffectId, index);
                MaximumParameterWidth = Math.Max(MaximumParameterWidth, definition.ParameterIds.Count);
                MaximumSourceCaptureWidth = Math.Max(MaximumSourceCaptureWidth, definition.SourceAttributes.Count);
                MaximumTargetCaptureWidth = Math.Max(MaximumTargetCaptureWidth, definition.TargetAttributes.Count);
                MaximumPersistentModifiers = Math.Max(MaximumPersistentModifiers, definition.PersistentModifiers);
                MaximumAttributeMutations = Math.Max(MaximumAttributeMutations, definition.AttributeMutations);
                MaximumGrantedTags = Math.Max(MaximumGrantedTags, definition.GrantedTags);
                MaximumCues = Math.Max(MaximumCues, definition.Cues);
                MaximumApplicationAdditionalCount = Math.Max(MaximumApplicationAdditionalCount,
                    definition.MaximumApplicationAdditionalCount);
                MaximumPeriodAdditionalCount = Math.Max(MaximumPeriodAdditionalCount,
                    definition.AdditionalApplicationCount(1));
                MaximumRemovalAdditionalCount = Math.Max(MaximumRemovalAdditionalCount,
                    definition.AdditionalApplicationCount(2));
                HasActiveEffects |= definition.CreatesActiveEffect;
                HasPeriodicEffects |= definition.HasPeriod;
            }
        }

        public int TagCount { get; }
        public int AttributeCount { get; }
        public int InitialTagCount { get; }
        public IReadOnlyList<GameplayEffectDefinitionLayout> Definitions => m_Definitions;
        public int MaximumParameterWidth { get; }
        public int MaximumSourceCaptureWidth { get; }
        public int MaximumTargetCaptureWidth { get; }
        public int MaximumPersistentModifiers { get; }
        public int MaximumAttributeMutations { get; }
        public int MaximumGrantedTags { get; }
        public int MaximumCues { get; }
        public int MaximumApplicationAdditionalCount { get; }
        public int MaximumPeriodAdditionalCount { get; }
        public int MaximumRemovalAdditionalCount { get; }
        public bool HasActiveEffects { get; }
        public bool HasPeriodicEffects { get; }
        public int DefinitionIndex(string effectId) => m_DefinitionIndices[effectId];
        public GameplayEffectDefinitionLayout Definition(string effectId) => m_Definitions[m_DefinitionIndices[effectId]];
    }


}
