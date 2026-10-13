using System;
using ThirdPersonGameplay.Configuration;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public sealed class Float32GameplayAbilityExecutionInstallationSet : IFloat32AbilityActionBindingProvider
    {
        readonly ReadOnlyCollection<Float32GameplayAbilityExecutionInstallation> m_Installations;
        readonly Dictionary<CharacterSkillId, Float32GameplayAbilityExecutionInstallation> m_ByAbility;

        public Float32GameplayAbilityExecutionInstallationSet(
            GameplayAbilityExecutionDataSet<Float32GameplayAbilityExecutionData> data,
            CharacterInputRuntimeBinding inputRuntimeBinding,
            CharacterGameplayEffectRuntimeBinding gameplayEffectBinding,
            CharacterEquipmentRuntimeBinding equipmentRuntimeBinding,
            GeneratedBlackboardConfiguration<AbilityStateValue> blackboards,
            GameplayConfigurationRuntimeBinding configurations = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            var values = new List<Float32GameplayAbilityExecutionInstallation>();
            m_ByAbility = new Dictionary<CharacterSkillId, Float32GameplayAbilityExecutionInstallation>();
            bool requiresTagCatalog = false;
            bool hasEquipmentAbility = false;
            for (int i = 0; i < data.Data.Count; i++)
            {
                Float32GameplayAbilityExecutionData ability = data.Data[i];
                if (ability == null)
                    throw new ArgumentException("Ability execution data contains a missing Ability.", nameof(data));
                requiresTagCatalog |= Float32GameplayAbilityExecutionServices.RequiresTagCatalog(ability);
                hasEquipmentAbility |= ability.Capabilities.HasGameplayCapability("Equipment");
            }
            Float32GameplayEffectRuntimeCatalog gameplayEffectCatalog = requiresTagCatalog
                ? new Float32GameplayEffectRuntimeCatalog(gameplayEffectBinding ??
                    throw new ArgumentNullException(nameof(gameplayEffectBinding)))
                : null;
            if (hasEquipmentAbility && equipmentRuntimeBinding == null)
                throw new ArgumentNullException(nameof(equipmentRuntimeBinding));
            for (int i = 0; i < data.Data.Count; i++)
            {
                Float32GameplayAbilityExecutionData ability = data.Data[i];
                EquipmentProgramLayout equipmentLayout = ability.Capabilities.HasGameplayCapability("Equipment")
                    ? EquipmentProgramLayoutCompiler.Compile(
                        equipmentRuntimeBinding,
                        ability.CatalogEntries,
                        ability.References,
                        ability.Producers)
                    : null;
                var installation = new Float32GameplayAbilityExecutionInstallation(
                    ability,
                    inputRuntimeBinding.RequireAbilityGrant(ability.AbilityId),
                    gameplayEffectCatalog,
                    equipmentLayout,
                    blackboards, configurations);
                values.Add(installation);
                m_ByAbility.Add(installation.Data.AbilityId, installation);
            }
            values.Sort((left, right) => left.Data.AbilityId.CompareTo(right.Data.AbilityId));
            m_Installations = values.AsReadOnly();
            var sources = new GeneratedCharacterBlackboardSource<AbilityStateValue>[values.Count];
            for (int index = 0; index < sources.Length; index++)
            {
                Float32GameplayAbilityExecutionInstallation installation = values[index];
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
            ConfigurationContentHash = configurations?.ContentHash ?? default;
            RequiresEquipment = hasEquipmentAbility;
            GameplayEffectCatalog = gameplayEffectCatalog;
        }

        public StableHash ConfigurationContentHash { get; }

        public IReadOnlyList<Float32GameplayAbilityExecutionInstallation> Installations => m_Installations;
        internal GeneratedCharacterBlackboardLayout<AbilityStateValue> CharacterBlackboards { get; }
        internal StableHash BlackboardContentHash { get; }
        public bool RequiresEquipment { get; }
        internal Float32GameplayEffectRuntimeCatalog GameplayEffectCatalog { get; }

        public Float32GameplayAbilityExecutionInstallation Require(CharacterSkillId abilityId) =>
            abilityId.IsValid && m_ByAbility.TryGetValue(abilityId, out Float32GameplayAbilityExecutionInstallation installation)
                ? installation
                : throw new InvalidOperationException($"Float32 Ability '{abilityId}' is not installed.");

        public GameplayAbilityExecutionBinding RequireActionBinding(CharacterSkillId abilityId) =>
            Require(abilityId).Data.Binding;

        ActionAdmissionProfile IFloat32AbilityActionBindingProvider.RequireAdmissionProfile(CharacterSkillId abilityId, string actionId) =>
            Require(abilityId).Services.RequireAdmissionProfile(actionId);

    }
}
