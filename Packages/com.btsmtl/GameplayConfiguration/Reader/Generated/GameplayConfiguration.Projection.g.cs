using System;
using System.Collections.Generic;
using System.Linq;
namespace ThirdPersonGameplay.Configuration {
public static class GameplayConfigProjection {
public static PublishedVector3 Publish(Vector3 value) {
return new PublishedVector3(value.X, value.Y, value.Z); }
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
return new PublishedAttributeBound(value.Enabled, value.Source, value.Constant, value.AttributeId); }
public static PublishedAttribute Publish(Attribute value) {
return new PublishedAttribute(value.Id, value.AttributeId, Publish(value.Minimum), Publish(value.Maximum)); }
public static PublishedEffect Publish(Effect value) {
return new PublishedEffect(value.Id, value.EffectId, value.DefinitionRevision, value.ScopePolicy, value.LifetimePolicy, value.EffectTags.ToArray(), value.DurationPolicy, Publish(value.DurationMagnitude), value.HasPeriod, Publish(value.PeriodMagnitude), value.ExecuteOnApplication, value.StackingPolicy, value.MaxStacks, value.DurationUpdate, value.PeriodUpdate, value.OverflowPolicy, value.SetByCallerParameters.ToArray(), value.Components.ToArray()); }
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
return new PublishedModifier(value.AttributeId, value.Application, value.Operation, Publish(value.Magnitude), value.Priority, value.ClampBound, value.ScaleWithStack); }
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
return new PublishedExecution(value.Mutations.ToArray()); }
public static PublishedAdditionalEffectsComponent Publish(AdditionalEffectsComponent value) {
return new PublishedAdditionalEffectsComponent(value.Effects.ToArray()); }
public static PublishedCameraShake Publish(CameraShake value) {
return new PublishedCameraShake(value.ResourceId, value.ShakeOnNotHit); }
public static PublishedAttackCollision Publish(AttackCollision value) {
return new PublishedAttackCollision(value.Kind, Publish(value.CenterOffset), value.Width, value.Height, value.Depth, value.FanAngle, value.Radius, value.InvalidRadius, value.InvalidAngle, value.FollowDirectionType, value.CoreDistance, value.IsSubtractive, value.HitInterval, value.AliveMaxHitCount, value.UnitMaxHitCount, value.FollowAttacker, value.Geometry); }
public static PublishedAttackProperty Publish(AttackProperty value) {
return new PublishedAttackProperty(value.SourceKey, value.HitType, value.HitStrengthType, value.CombatTags.ToArray(), Publish(value.DamagePercentage), Publish(value.AddedDamage), Publish(value.BreakStunRatio), Publish(value.ElementAccumulation), Publish(value.ExhaustedAccumulation), Publish(value.ExhaustedChase), value.DamageElement, value.DamageHitType, value.DamageBreakLevel, Publish(value.DamageBreakLevelProbability), value.TriggerBuffLevel, value.DestructionClass, value.DestructionDurability, value.OverrideDamageStaggerLevel, value.DamageTextId, value.DamageTextWaitMilliseconds, value.FrameHalt, value.AttackerFrameHalt, value.GroundHitEffectId, value.SkyHitEffectId, value.DownHitEffectId, value.StandardConfigKey, value.AbilityTargetKey, value.IsCauseStun, value.IsHeavyAttack, value.IsCauseExhausted, value.IsHeal, value.IsIndirect, value.HitsEnemy, value.HitsAllied, value.HitsNeutral, value.UseAbilityTargetKey, value.BanDamage); }
public static PublishedCueBinding Publish(CueBinding value) {
return new PublishedCueBinding(value.CueId, value.Trigger); }
public static PublishedAttack Publish(Attack value) {
return new PublishedAttack(value.Id, value.AttackId, value.DefinitionRevision, Publish(value.Collision), Publish(value.Property), Publish(value.CameraShake)); }
public static PublishedComponentRecord Publish(ComponentRecord value) {
return new PublishedComponentRecord(value.Id, Publish(value.Value)); }
public static PublishedMutation Publish(Mutation value) {
return new PublishedMutation(value.Id, value.AttributeId, value.Operation, Publish(value.Magnitude), value.ClampBound); }
public static PublishedAdditionalEffect Publish(AdditionalEffect value) {
return new PublishedAdditionalEffect(value.Id, value.Trigger, value.Effect, value.ParameterBindings.ToArray()); }
public static PublishedParameterBinding Publish(ParameterBinding value) {
return new PublishedParameterBinding(value.Id, value.ChildParameterId, value.Source, value.ParentParameterId, value.Constant); }
public static PublishedInitialAttribute Publish(InitialAttribute value) {
return new PublishedInitialAttribute(value.Id, value.Attribute, value.BaseValue); }
public static PublishedInitialEffect Publish(InitialEffect value) {
return new PublishedInitialEffect(value.Id, value.Effect, value.Parameters.ToArray()); }
public static PublishedInitialParameter Publish(InitialParameter value) {
return new PublishedInitialParameter(value.Id, value.ParameterId, value.Value); }
public static PublishedCharacter Publish(Character value) {
return new PublishedCharacter(value.Id, value.Attributes.ToArray(), value.InitialAttributes.ToArray(), value.InitialTags.ToArray(), value.Effects.ToArray(), value.Attacks.ToArray(), value.InitialEffects.ToArray()); }
public static string DisplayName(GameplayTables tables, GameplayConfigRecordKind kind, string id) => kind switch {
GameplayConfigRecordKind.Attributes => tables.Attributes.Get(id).DisplayName,
GameplayConfigRecordKind.Effects => tables.Effects.Get(id).DisplayName,
GameplayConfigRecordKind.Attacks => tables.Attacks.Get(id).DisplayName,
GameplayConfigRecordKind.Components => tables.Components.Get(id).Id,
GameplayConfigRecordKind.Mutations => tables.Mutations.Get(id).Id,
GameplayConfigRecordKind.AdditionalEffects => tables.AdditionalEffects.Get(id).Id,
GameplayConfigRecordKind.ParameterBindings => tables.ParameterBindings.Get(id).Id,
GameplayConfigRecordKind.InitialAttributes => tables.InitialAttributes.Get(id).Id,
GameplayConfigRecordKind.InitialEffects => tables.InitialEffects.Get(id).Id,
GameplayConfigRecordKind.InitialParameters => tables.InitialParameters.Get(id).Id,
GameplayConfigRecordKind.Characters => tables.Characters.Get(id).DisplayName,
_ => throw new ArgumentOutOfRangeException(nameof(kind)) };
public static PublishedGameplayConfiguration Publish(GameplayTables tables) => new PublishedGameplayConfiguration(tables.Attributes.DataList.Select(Publish).ToArray(), tables.Effects.DataList.Select(Publish).ToArray(), tables.Attacks.DataList.Select(Publish).ToArray(), tables.Components.DataList.Select(Publish).ToArray(), tables.Mutations.DataList.Select(Publish).ToArray(), tables.AdditionalEffects.DataList.Select(Publish).ToArray(), tables.ParameterBindings.DataList.Select(Publish).ToArray(), tables.InitialAttributes.DataList.Select(Publish).ToArray(), tables.InitialEffects.DataList.Select(Publish).ToArray(), tables.InitialParameters.DataList.Select(Publish).ToArray(), tables.Characters.DataList.Select(Publish).ToArray()); }
}
