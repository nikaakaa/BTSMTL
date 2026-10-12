using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedGameplayAbilityExecutionInstallationSet : IFixedAbilityActionBindingProvider
    {
        readonly ReadOnlyCollection<FixedGameplayAbilityExecutionInstallation> m_Installations;
        readonly Dictionary<CharacterSkillId, FixedGameplayAbilityExecutionInstallation> m_ByAbility;

        public FixedGameplayAbilityExecutionInstallationSet(
            GameplayAbilityExecutionDataSet<FixedGameplayAbilityExecutionData> data,
            CharacterInputRuntimeBinding inputRuntimeBinding,
            CharacterGameplayEffectRuntimeBinding gameplayEffectBinding,
            CharacterEquipmentRuntimeBinding equipmentRuntimeBinding,
            GeneratedBlackboardConfiguration<AbilityStateValue> blackboards)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            var values = new List<FixedGameplayAbilityExecutionInstallation>();
            m_ByAbility = new Dictionary<CharacterSkillId, FixedGameplayAbilityExecutionInstallation>();
            bool requiresTagCatalog = false;
            bool hasEquipmentAbility = false;
            for (int i = 0; i < data.Data.Count; i++)
            {
                FixedGameplayAbilityExecutionData ability = data.Data[i];
                if (ability == null)
                    throw new ArgumentException("Ability execution data contains a missing Ability.", nameof(data));
                requiresTagCatalog |= FixedGameplayAbilityExecutionServices.RequiresTagCatalog(ability);
                hasEquipmentAbility |= ability.Capabilities.HasGameplayCapability("Equipment");
            }
            FixedGameplayEffectRuntimeCatalog gameplayEffectCatalog = requiresTagCatalog
                ? new FixedGameplayEffectRuntimeCatalog(gameplayEffectBinding ??
                    throw new ArgumentNullException(nameof(gameplayEffectBinding)))
                : null;
            if (hasEquipmentAbility && equipmentRuntimeBinding == null)
                throw new ArgumentNullException(nameof(equipmentRuntimeBinding));
            for (int i = 0; i < data.Data.Count; i++)
            {
                FixedGameplayAbilityExecutionData ability = data.Data[i];
                EquipmentProgramLayout equipmentLayout = ability.Capabilities.HasGameplayCapability("Equipment")
                    ? EquipmentProgramLayoutCompiler.Compile(
                        equipmentRuntimeBinding,
                        ability.CatalogEntries,
                        ability.References,
                        ability.Producers)
                    : null;
                var installation = new FixedGameplayAbilityExecutionInstallation(
                    ability,
                    inputRuntimeBinding.RequireAbilityGrant(ability.AbilityId),
                    gameplayEffectCatalog,
                    equipmentLayout,
                    blackboards);
                values.Add(installation);
                m_ByAbility.Add(installation.Data.AbilityId, installation);
            }
            values.Sort((left, right) => left.Data.AbilityId.CompareTo(right.Data.AbilityId));
            m_Installations = values.AsReadOnly();
            var sources = new GeneratedCharacterBlackboardSource<AbilityStateValue>[values.Count];
            for (int index = 0; index < sources.Length; index++)
            {
                FixedGameplayAbilityExecutionInstallation installation = values[index];
                sources[index] = new GeneratedCharacterBlackboardSource<AbilityStateValue>(installation.Data.AbilityId,
                    installation.Data.StateSlots, installation.Data.Scopes,
                    installation.BlackboardDeclarations,
                    installation.Blackboards);
            }
            CharacterBlackboards = new GeneratedCharacterBlackboardLayout<AbilityStateValue>(blackboards, sources);
            BlackboardContentHash = blackboards.ContentHash;
            var projectedInputs = new HashSet<int>();
            for (int index = 0; index < values.Count; index++)
            {
                values[index].CharacterBlackboardSlots = CharacterBlackboards.Slots(values[index].Data.AbilityId);
                values[index].BindCharacterInputs(projectedInputs);
            }
            RequiresEquipment = hasEquipmentAbility;
            GameplayEffectCatalog = gameplayEffectCatalog;
        }

        public IReadOnlyList<FixedGameplayAbilityExecutionInstallation> Installations => m_Installations;
        internal GeneratedCharacterBlackboardLayout<AbilityStateValue> CharacterBlackboards { get; }
        internal StableHash BlackboardContentHash { get; }
        public bool RequiresEquipment { get; }
        internal FixedGameplayEffectRuntimeCatalog GameplayEffectCatalog { get; }

        public void CopyAttributeIds(List<string> ids)
        {
            ids.Clear();
            if (GameplayEffectCatalog == null)
                return;
            GameplayAttributeDependencyLayout attributes = GameplayEffectCatalog.AttributeDependencies;
            for (int i = 0; i < attributes.AttributeCount; i++)
                ids.Add(attributes.AttributeId(i));
        }

        public FixedGameplayAbilityExecutionInstallation Require(CharacterSkillId abilityId) =>
            abilityId.IsValid && m_ByAbility.TryGetValue(abilityId, out FixedGameplayAbilityExecutionInstallation installation)
                ? installation
                : throw new InvalidOperationException($"Fixed Ability '{abilityId}' is not installed.");

        public GameplayAbilityExecutionBinding RequireActionBinding(CharacterSkillId abilityId) =>
            Require(abilityId).Data.Binding;

        ActionAdmissionProfile IFixedAbilityActionBindingProvider.RequireAdmissionProfile(CharacterSkillId abilityId, string actionId) =>
            Require(abilityId).Services.RequireAdmissionProfile(actionId);

    }
}
