using System;
using System.Collections.Generic;
using System.Linq;
namespace ThirdPersonGameplay.Configuration {
public static class GameplayConfigProjection {
public static PublishedMagnitude Publish(Magnitude value) {
return value switch {
ConstantMagnitude child => PublishedMagnitude.FromConstantMagnitude(Publish(child)),
ScaledConstantMagnitude child => PublishedMagnitude.FromScaledConstantMagnitude(Publish(child)),
SetByCallerMagnitude child => PublishedMagnitude.FromSetByCallerMagnitude(Publish(child)),
SourceAttributeSnapshotMagnitude child => PublishedMagnitude.FromSourceAttributeSnapshotMagnitude(Publish(child)),
TargetAttributeSnapshotMagnitude child => PublishedMagnitude.FromTargetAttributeSnapshotMagnitude(Publish(child)),
TargetAttributeLiveMagnitude child => PublishedMagnitude.FromTargetAttributeLiveMagnitude(Publish(child)),
_ => throw new InvalidOperationException("配置组件不属于正式 schema。") }; }
public static PublishedConstantMagnitude Publish(ConstantMagnitude value) {
return new PublishedConstantMagnitude(value.Value); }
public static PublishedScaledConstantMagnitude Publish(ScaledConstantMagnitude value) {
return new PublishedScaledConstantMagnitude(value.Value, value.Coefficient, value.PostAdd); }
public static PublishedSetByCallerMagnitude Publish(SetByCallerMagnitude value) {
return new PublishedSetByCallerMagnitude(value.ParameterId, value.Coefficient, value.PostAdd); }
public static PublishedSourceAttributeSnapshotMagnitude Publish(SourceAttributeSnapshotMagnitude value) {
return new PublishedSourceAttributeSnapshotMagnitude(value.AttributeId, value.Coefficient, value.PostAdd); }
public static PublishedTargetAttributeSnapshotMagnitude Publish(TargetAttributeSnapshotMagnitude value) {
return new PublishedTargetAttributeSnapshotMagnitude(value.AttributeId, value.Coefficient, value.PostAdd); }
public static PublishedTargetAttributeLiveMagnitude Publish(TargetAttributeLiveMagnitude value) {
return new PublishedTargetAttributeLiveMagnitude(value.AttributeId, value.Coefficient, value.PostAdd); }
public static PublishedTagQuery Publish(TagQuery value) {
return new PublishedTagQuery(value.All.ToArray(), value.Any.ToArray(), value.None.ToArray()); }
public static PublishedAttributeBound Publish(AttributeBound value) {
return value switch {
UnboundedAttributeBound child => PublishedAttributeBound.FromUnboundedAttributeBound(Publish(child)),
ConstantAttributeBound child => PublishedAttributeBound.FromConstantAttributeBound(Publish(child)),
AttributeReferenceBound child => PublishedAttributeBound.FromAttributeReferenceBound(Publish(child)),
_ => throw new InvalidOperationException("配置组件不属于正式 schema。") }; }
public static PublishedUnboundedAttributeBound Publish(UnboundedAttributeBound value) {
return new PublishedUnboundedAttributeBound(); }
public static PublishedConstantAttributeBound Publish(ConstantAttributeBound value) {
return new PublishedConstantAttributeBound(value.Value); }
public static PublishedAttributeReferenceBound Publish(AttributeReferenceBound value) {
return new PublishedAttributeReferenceBound(value.AttributeId); }
public static PublishedAttribute Publish(Attribute value) {
return new PublishedAttribute(value.Id, value.AttributeId, Publish(value.Minimum), Publish(value.Maximum)); }
public static PublishedEffectDuration Publish(EffectDuration value) {
return value switch {
InstantDuration child => PublishedEffectDuration.FromInstantDuration(Publish(child)),
InfiniteDuration child => PublishedEffectDuration.FromInfiniteDuration(Publish(child)),
TimedDuration child => PublishedEffectDuration.FromTimedDuration(Publish(child)),
_ => throw new InvalidOperationException("配置组件不属于正式 schema。") }; }
public static PublishedInstantDuration Publish(InstantDuration value) {
return new PublishedInstantDuration(); }
public static PublishedInfiniteDuration Publish(InfiniteDuration value) {
return new PublishedInfiniteDuration(); }
public static PublishedTimedDuration Publish(TimedDuration value) {
return new PublishedTimedDuration(Publish(value.Magnitude)); }
public static PublishedEffectPeriod Publish(EffectPeriod value) {
return value switch {
NoPeriod child => PublishedEffectPeriod.FromNoPeriod(Publish(child)),
FixedPeriod child => PublishedEffectPeriod.FromFixedPeriod(Publish(child)),
_ => throw new InvalidOperationException("配置组件不属于正式 schema。") }; }
public static PublishedNoPeriod Publish(NoPeriod value) {
return new PublishedNoPeriod(); }
public static PublishedFixedPeriod Publish(FixedPeriod value) {
return new PublishedFixedPeriod(Publish(value.Magnitude)); }
public static PublishedEffectStacking Publish(EffectStacking value) {
return value switch {
IndependentStacking child => PublishedEffectStacking.FromIndependentStacking(Publish(child)),
AggregateStacking child => PublishedEffectStacking.FromAggregateStacking(Publish(child)),
_ => throw new InvalidOperationException("配置组件不属于正式 schema。") }; }
public static PublishedIndependentStacking Publish(IndependentStacking value) {
return new PublishedIndependentStacking(); }
public static PublishedAggregateStacking Publish(AggregateStacking value) {
return new PublishedAggregateStacking(value.By, value.MaxStacks, value.DurationUpdate, value.PeriodUpdate, value.OverflowPolicy); }
public static PublishedAttributeOperation Publish(AttributeOperation value) {
return value switch {
AdditiveOperation child => PublishedAttributeOperation.FromAdditiveOperation(Publish(child)),
MultiplicativeOperation child => PublishedAttributeOperation.FromMultiplicativeOperation(Publish(child)),
OverrideOperation child => PublishedAttributeOperation.FromOverrideOperation(Publish(child)),
ClampOperation child => PublishedAttributeOperation.FromClampOperation(Publish(child)),
_ => throw new InvalidOperationException("配置组件不属于正式 schema。") }; }
public static PublishedAdditiveOperation Publish(AdditiveOperation value) {
return new PublishedAdditiveOperation(); }
public static PublishedMultiplicativeOperation Publish(MultiplicativeOperation value) {
return new PublishedMultiplicativeOperation(); }
public static PublishedOverrideOperation Publish(OverrideOperation value) {
return new PublishedOverrideOperation(); }
public static PublishedClampOperation Publish(ClampOperation value) {
return new PublishedClampOperation(value.Bound); }
public static PublishedEffect Publish(Effect value) {
return new PublishedEffect(value.Id, value.EffectId, value.ScopePolicy, value.LifetimePolicy, value.EffectTags.ToArray(), Publish(value.Duration), Publish(value.Period), value.ExecuteOnApplication, Publish(value.Stacking), value.SetByCallerParameters.ToArray(), value.Components.Select(item => Publish(item)).ToArray()); }
public static PublishedComponent Publish(Component value) {
return value switch {
Modifier child => PublishedComponent.FromModifier(Publish(child)),
GrantedTags child => PublishedComponent.FromGrantedTags(Publish(child)),
BlockAbilities child => PublishedComponent.FromBlockAbilities(Publish(child)),
ReplacementSourceRequirement child => PublishedComponent.FromReplacementSourceRequirement(Publish(child)),
TagRequirements child => PublishedComponent.FromTagRequirements(Publish(child)),
AttributeRequirements child => PublishedComponent.FromAttributeRequirements(Publish(child)),
Execution child => PublishedComponent.FromExecution(Publish(child)),
AdditionalEffectsComponent child => PublishedComponent.FromAdditionalEffectsComponent(Publish(child)),
CueBinding child => PublishedComponent.FromCueBinding(Publish(child)),
_ => throw new InvalidOperationException("配置组件不属于正式 schema。") }; }
public static PublishedModifier Publish(Modifier value) {
return new PublishedModifier(value.AttributeId, value.Application, Publish(value.Operation), Publish(value.Magnitude), value.Priority, value.ScaleWithStack); }
public static PublishedGrantedTags Publish(GrantedTags value) {
return new PublishedGrantedTags(value.Tags.ToArray()); }
public static PublishedBlockAbilities Publish(BlockAbilities value) {
return new PublishedBlockAbilities(Publish(value.Query)); }
public static PublishedReplacementSourceRequirement Publish(ReplacementSourceRequirement value) {
return new PublishedReplacementSourceRequirement(Publish(value.CandidateQuery), Publish(value.SelectedSourceQuery)); }
public static PublishedTagRequirements Publish(TagRequirements value) {
return new PublishedTagRequirements(value.Phase, Publish(value.Source), Publish(value.Target)); }
public static PublishedAttributeRequirements Publish(AttributeRequirements value) {
return new PublishedAttributeRequirements(value.Phase, value.Source, value.AttributeId, value.Comparison, Publish(value.Threshold)); }
public static PublishedExecution Publish(Execution value) {
return new PublishedExecution(value.Mutations.Select(item => Publish(item)).ToArray()); }
public static PublishedAdditionalEffectsComponent Publish(AdditionalEffectsComponent value) {
return new PublishedAdditionalEffectsComponent(value.Effects.Select(item => Publish(item)).ToArray()); }
public static PublishedCueBinding Publish(CueBinding value) {
return new PublishedCueBinding(value.CueId, value.Trigger); }
public static PublishedMutation Publish(Mutation value) {
return new PublishedMutation(value.AttributeId, Publish(value.Operation), Publish(value.Magnitude)); }
public static PublishedAdditionalEffect Publish(AdditionalEffect value) {
return new PublishedAdditionalEffect(value.Trigger, value.Effect, value.ParameterBindings.Select(item => Publish(item)).ToArray()); }
public static PublishedParameterBinding Publish(ParameterBinding value) {
return new PublishedParameterBinding(value.ChildParameterId, Publish(value.Value)); }
public static PublishedParameterValue Publish(ParameterValue value) {
return value switch {
ParentParameterValue child => PublishedParameterValue.FromParentParameterValue(Publish(child)),
ConstantParameterValue child => PublishedParameterValue.FromConstantParameterValue(Publish(child)),
_ => throw new InvalidOperationException("配置组件不属于正式 schema。") }; }
public static PublishedParentParameterValue Publish(ParentParameterValue value) {
return new PublishedParentParameterValue(value.ParentParameterId); }
public static PublishedConstantParameterValue Publish(ConstantParameterValue value) {
return new PublishedConstantParameterValue(value.Value); }
public static PublishedInitialAttribute Publish(InitialAttribute value) {
return new PublishedInitialAttribute(value.Attribute, value.BaseValue); }
public static PublishedInitialEffect Publish(InitialEffect value) {
return new PublishedInitialEffect(value.Effect, value.Parameters.Select(item => Publish(item)).ToArray()); }
public static PublishedInitialParameter Publish(InitialParameter value) {
return new PublishedInitialParameter(value.ParameterId, value.Value); }
public static PublishedCharacter Publish(Character value) {
return new PublishedCharacter(value.Id, value.Attributes.ToArray(), value.InitialAttributes.Select(item => Publish(item)).ToArray(), value.InitialTags.ToArray(), value.Effects.ToArray(), value.InitialEffects.Select(item => Publish(item)).ToArray()); }
public static string DisplayName(GameplayTables tables, GameplayConfigRecordKind kind, string id) => kind switch {
GameplayConfigRecordKind.Attributes => tables.Attributes.GetById(id).DisplayName,
GameplayConfigRecordKind.Effects => tables.Effects.GetById(id).DisplayName,
GameplayConfigRecordKind.Characters => tables.Characters.Get(id).DisplayName,
_ => throw new ArgumentOutOfRangeException(nameof(kind)) };
public static PublishedGameplayConfiguration Publish(GameplayTables tables) => new PublishedGameplayConfiguration(tables.Attributes.DataList.Select(Publish).ToArray(), tables.Effects.DataList.Select(Publish).ToArray(), tables.Characters.DataList.Select(Publish).ToArray()); }
}
