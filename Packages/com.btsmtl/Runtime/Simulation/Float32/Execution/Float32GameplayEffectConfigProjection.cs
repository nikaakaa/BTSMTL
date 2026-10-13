using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThirdPersonGameplay.Configuration;
using ThirdPersonSimulation.Collision;

namespace ThirdPersonSimulation
{
    sealed partial class Float32GameplayEffectRuntimeCatalog
    {
        public GameplayEffectCatalogData Content { get; private set; }

        void ReadBinding(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            GameplayEffectCatalogData.CanonicalRequireSchema(reader);
            Content = GameplayEffectCatalogData.CanonicalReadMaterialized(reader);
            reader.RequireComplete();
            if (Content.Version != CharacterGameplayEffectRuntimeBinding.CatalogFormatVersion)
                throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.UnsupportedVersion,
                    GameplayEffectCapacityDimension.ContentVersion, m_Binding.SourceIdentity,
                    CharacterGameplayEffectRuntimeBinding.CatalogFormatVersion, Content.Version));
            PublishedGameplayConfiguration data = Content.Configuration;
            foreach (GameplayEffectTagContent tag in Content.Tags)
            {
                string id = NormalizeTag(tag.Id);
                m_TagParents.Add(id, tag.Parent.Length == 0 ? string.Empty : NormalizeTag(tag.Parent));
                if (tag.InitiallyOwned)
                    m_InitialTags.Add(id);
            }
            var attributes = data.Attributes.ToDictionary(value => value.Id, StringComparer.Ordinal);
            foreach (PublishedInitialAttribute initial in data.Characters.Single().InitialAttributes)
            {
                PublishedAttribute attribute = attributes[initial.Attribute];
                string id = NormalizeAttribute(attribute.AttributeId);
                m_Attributes.Add(id, new PortableAttributeDefinition(id, Scalar(initial.BaseValue),
                    Bound(attribute.Minimum), Bound(attribute.Maximum)));
            }
            var effects = data.Effects.ToDictionary(value => value.Id, StringComparer.Ordinal);
            foreach (PublishedEffect effect in data.Effects)
            {
                if (effect.DefinitionRevision == 0 || effect.Stacking.MaxStacks <= 0)
                    throw new InvalidDataException($"Effect '{effect.Id}' requires a positive revision and MaxStacks.");
                PortableEffectComponent[] projected = effect.Components.Select(value => Component(value, effects)).ToArray();
                var definition = new PortableEffectDefinition(NormalizeEffect(effect.EffectId), effect.DefinitionRevision,
                    Strings(effect.EffectTags, true), (PortableEffectDurationPolicy)effect.Duration.Policy,
                    PortableMagnitude.FromConfiguration(effect.Duration.Magnitude), effect.Period.Enabled, PortableMagnitude.FromConfiguration(effect.Period.Magnitude),
                    effect.ExecuteOnApplication, (PortableEffectStackingPolicy)effect.Stacking.Policy, effect.Stacking.MaxStacks,
                    (PortableEffectDurationUpdatePolicy)effect.Stacking.DurationUpdate, (PortableEffectPeriodUpdatePolicy)effect.Stacking.PeriodUpdate,
                    (PortableEffectOverflowPolicy)effect.Stacking.OverflowPolicy, Strings(effect.SetByCallerParameters, false),
                    (GameplayEffectScopeKind)effect.ScopePolicy, (GameplayEffectLifetimeKind)effect.LifetimePolicy, projected);
                m_Effects.Add(definition.Id, definition);
            }
        }

        static PortableAttributeBound Bound(PublishedAttributeBound value) =>
            new PortableAttributeBound(value.Enabled, value.Source == BoundSource.Attribute, Scalar(value.Constant),
                value.Enabled && value.Source == BoundSource.Attribute ? NormalizeAttribute(value.AttributeId) : string.Empty);

        static Float32Scalar Scalar(float value) => Float32Scalar.FromDouble(value);
        static PortableTagQuery Query(PublishedTagQuery value) => new PortableTagQuery(value.All, value.Any, value.None);

        static string[] Strings(IReadOnlyList<string> source, bool tags)
        {
            string[] result = source.Select(value => tags ? NormalizeTag(value) : SimulationIdentity.Require(value, "CatalogString")).ToArray();
            Array.Sort(result, StringComparer.Ordinal);
            for (int index = 1; index < result.Length; index++)
                if (result[index - 1] == result[index])
                    throw new InvalidDataException($"Canonical string array contains duplicate '{result[index]}'.");
            return result;
        }

        static PortableEffectComponent Component(PublishedComponent component, Dictionary<string, PublishedEffect> effects)
        {
            switch (component.Kind)
            {
                case ComponentValueKind.Modifier:
                {
                    PublishedModifier value = component.Modifier;
                    return new PortableModifierComponent(value.AttributeId, (PortableModifierApplication)value.Application,
                        (PortableModifierOperation)value.Operation, PortableMagnitude.FromConfiguration(value.Magnitude), value.Priority,
                        (PortableClampBound)value.ClampBound, value.ScaleWithStack);
                }
                case ComponentValueKind.GrantedTags:
                    return new PortableGrantedTagsComponent(Strings(component.GrantedTags.Tags, true));
                case ComponentValueKind.BlockAbilities:
                    return new PortableBlockAbilitiesComponent(Query(component.BlockAbilities.Query));
                case ComponentValueKind.ReplacementSourceRequirement:
                    return new PortableReplacementSourceRequirementComponent(
                        Query(component.ReplacementSourceRequirement.CandidateQuery), Query(component.ReplacementSourceRequirement.SelectedSourceQuery));
                case ComponentValueKind.TagRequirements:
                    return new PortableTagRequirementsComponent((PortableRequirementPhase)component.TagRequirements.Phase,
                        Query(component.TagRequirements.Source), Query(component.TagRequirements.Target));
                case ComponentValueKind.AttributeRequirements:
                {
                    PublishedAttributeRequirements value = component.AttributeRequirements;
                    return new PortableAttributeRequirementsComponent((PortableRequirementPhase)value.Phase,
                        (PortableAttributeSource)value.Source, value.AttributeId, (PortableAttributeComparison)value.Comparison, PortableMagnitude.FromConfiguration(value.Threshold));
                }
                case ComponentValueKind.Execution:
                    return new PortableExecutionComponent(component.Execution.Mutations.Select(value =>
                        new PortableExecutionMutation(value.AttributeId, (PortableModifierOperation)value.Operation,
                            PortableMagnitude.FromConfiguration(value.Magnitude), (PortableClampBound)value.ClampBound)).ToArray());
                case ComponentValueKind.AdditionalEffectsComponent:
                    return new PortableAdditionalEffectsComponent(component.AdditionalEffectsComponent.Effects.Select(value =>
                        new PortableAdditionalEffect((PortableAdditionalEffectTrigger)value.Trigger,
                            effects[value.Effect].EffectId, value.ParameterBindings.Select(binding =>
                                new PortableAdditionalParameterBinding(binding.ChildParameterId,
                                    (PortableAdditionalParameterSource)binding.Source, binding.ParentParameterId, Scalar(binding.Constant))).ToArray())).ToArray());
                case ComponentValueKind.CueBinding:
                    return new PortableCueComponent(component.CueBinding.CueId, (PortableCueTrigger)component.CueBinding.Trigger);
                default:
                    throw new InvalidDataException($"Gameplay component kind '{component.Kind}' has no projection.");
            }
        }

    }
}
