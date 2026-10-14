using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using ThirdPersonGameplay.Configuration;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPGameplayAbilityExecutionInstallationSet : IFPAbilityActionBindingProvider
    {
        readonly ReadOnlyCollection<FPGameplayAbilityExecutionInstallation> m_Installations;
        readonly Dictionary<CharacterSkillId, FPGameplayAbilityExecutionInstallation> m_ByAbility;

        public FPGameplayAbilityExecutionInstallationSet(
            GameplayAbilityExecutionDataSet<FPGameplayAbilityExecutionData> data,
            CharacterInputRuntimeBinding inputRuntimeBinding,
            CharacterGameplayEffectRuntimeBinding gameplayEffectBinding,
            CharacterEquipmentRuntimeBinding equipmentRuntimeBinding,
            GeneratedBlackboardConfiguration<AbilityStateValue> blackboards,
            CharacterCombatRuntimeBinding combatBinding = null,
            GameplayConfigurationRuntimeBinding configurations = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            CombatCatalog = combatBinding == null ? null : new FPCombatAttackCatalog(combatBinding.AttackCatalog);
            var values = new List<FPGameplayAbilityExecutionInstallation>();
            m_ByAbility = new Dictionary<CharacterSkillId, FPGameplayAbilityExecutionInstallation>();
            bool requiresTagCatalog = false;
            bool hasEquipmentAbility = false;
            for (int i = 0; i < data.Data.Count; i++)
            {
                FPGameplayAbilityExecutionData ability = data.Data[i];
                if (ability == null)
                    throw new ArgumentException("Ability execution data contains a missing Ability.", nameof(data));
                requiresTagCatalog |= FPGameplayAbilityExecutionServices.RequiresTagCatalog(ability);
                hasEquipmentAbility |= ability.Capabilities.HasGameplayCapability("Equipment");
            }
            if (requiresTagCatalog && gameplayEffectBinding == null)
                throw new ArgumentNullException(nameof(gameplayEffectBinding));
            FPGameplayEffectRuntimeCatalog gameplayEffectCatalog = gameplayEffectBinding == null
                ? null : new FPGameplayEffectRuntimeCatalog(gameplayEffectBinding);
            if (hasEquipmentAbility && equipmentRuntimeBinding == null)
                throw new ArgumentNullException(nameof(equipmentRuntimeBinding));
            for (int i = 0; i < data.Data.Count; i++)
            {
                FPGameplayAbilityExecutionData ability = data.Data[i];
                EquipmentProgramLayout equipmentLayout = ability.Capabilities.HasGameplayCapability("Equipment")
                    ? EquipmentProgramLayoutCompiler.Compile(
                        equipmentRuntimeBinding,
                        ability.CatalogEntries,
                        ability.References,
                        ability.Producers)
                    : null;
                var installation = new FPGameplayAbilityExecutionInstallation(
                    ability,
                    inputRuntimeBinding.RequireAbilityGrant(ability.AbilityId),
                    gameplayEffectCatalog,
                    equipmentLayout,
                    blackboards, CombatCatalog, configurations);
                values.Add(installation);
                m_ByAbility.Add(installation.Data.AbilityId, installation);
            }
            values.Sort((left, right) => left.Data.AbilityId.CompareTo(right.Data.AbilityId));
            m_Installations = values.AsReadOnly();
            var sources = new GeneratedCharacterBlackboardSource<AbilityStateValue>[values.Count];
            for (int index = 0; index < sources.Length; index++)
            {
                FPGameplayAbilityExecutionInstallation installation = values[index];
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

        internal FPCombatAttackCatalog CombatCatalog { get; }

        public StableHash ConfigurationContentHash { get; }

        public IReadOnlyList<FPGameplayAbilityExecutionInstallation> Installations => m_Installations;
        internal GeneratedCharacterBlackboardLayout<AbilityStateValue> CharacterBlackboards { get; }
        internal StableHash BlackboardContentHash { get; }
        public bool RequiresEquipment { get; }
        internal FPGameplayEffectRuntimeCatalog GameplayEffectCatalog { get; }

        public void CopyAttributeIds(List<string> ids)
        {
            ids.Clear();
            if (GameplayEffectCatalog == null)
                return;
            GameplayAttributeDependencyLayout attributes = GameplayEffectCatalog.AttributeDependencies;
            for (int i = 0; i < attributes.AttributeCount; i++)
                ids.Add(attributes.AttributeId(i));
        }

        public FPGameplayAbilityExecutionInstallation Require(CharacterSkillId abilityId) =>
            abilityId.IsValid && m_ByAbility.TryGetValue(abilityId, out FPGameplayAbilityExecutionInstallation installation)
                ? installation
                : throw new InvalidOperationException($"FP Ability '{abilityId}' is not installed.");

        public GameplayAbilityExecutionBinding RequireActionBinding(CharacterSkillId abilityId) =>
            Require(abilityId).Data.Binding;

        ActionAdmissionProfile IFPAbilityActionBindingProvider.RequireAdmissionProfile(CharacterSkillId abilityId, string actionId) =>
            Require(abilityId).Services.RequireAdmissionProfile(actionId);

    }
}
