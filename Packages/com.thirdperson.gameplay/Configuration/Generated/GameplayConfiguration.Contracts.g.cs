using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
namespace ThirdPersonGameplay.Configuration {
[ValueUnionTemplate]
public readonly struct MagnitudeValue {
[ValueUnionCase] public readonly PublishedConstantMagnitude ConstantMagnitude;
[ValueUnionCase] public readonly PublishedScaledConstantMagnitude ScaledConstantMagnitude;
[ValueUnionCase] public readonly PublishedSetByCallerMagnitude SetByCallerMagnitude;
[ValueUnionCase] public readonly PublishedSourceAttributeSnapshotMagnitude SourceAttributeSnapshotMagnitude;
[ValueUnionCase] public readonly PublishedTargetAttributeSnapshotMagnitude TargetAttributeSnapshotMagnitude;
[ValueUnionCase] public readonly PublishedTargetAttributeLiveMagnitude TargetAttributeLiveMagnitude;
}
[ValueUnion(typeof(MagnitudeValue))]
public readonly partial struct PublishedMagnitude {}
public sealed partial class PublishedConstantMagnitude {
public PublishedConstantMagnitude(float value_Value) {
Value = value_Value;
}
public float Value { get; }
}
public sealed partial class PublishedScaledConstantMagnitude {
public PublishedScaledConstantMagnitude(float value_Value, float value_Coefficient, float value_PostAdd) {
Value = value_Value;
Coefficient = value_Coefficient;
PostAdd = value_PostAdd;
}
public float Value { get; }
public float Coefficient { get; }
public float PostAdd { get; }
}
public sealed partial class PublishedSetByCallerMagnitude {
public PublishedSetByCallerMagnitude(string value_ParameterId, float value_Coefficient, float value_PostAdd) {
ParameterId = value_ParameterId;
Coefficient = value_Coefficient;
PostAdd = value_PostAdd;
}
public string ParameterId { get; }
public float Coefficient { get; }
public float PostAdd { get; }
}
public sealed partial class PublishedSourceAttributeSnapshotMagnitude {
public PublishedSourceAttributeSnapshotMagnitude(string value_AttributeId, float value_Coefficient, float value_PostAdd) {
AttributeId = value_AttributeId;
Coefficient = value_Coefficient;
PostAdd = value_PostAdd;
}
public string AttributeId { get; }
public float Coefficient { get; }
public float PostAdd { get; }
}
public sealed partial class PublishedTargetAttributeSnapshotMagnitude {
public PublishedTargetAttributeSnapshotMagnitude(string value_AttributeId, float value_Coefficient, float value_PostAdd) {
AttributeId = value_AttributeId;
Coefficient = value_Coefficient;
PostAdd = value_PostAdd;
}
public string AttributeId { get; }
public float Coefficient { get; }
public float PostAdd { get; }
}
public sealed partial class PublishedTargetAttributeLiveMagnitude {
public PublishedTargetAttributeLiveMagnitude(string value_AttributeId, float value_Coefficient, float value_PostAdd) {
AttributeId = value_AttributeId;
Coefficient = value_Coefficient;
PostAdd = value_PostAdd;
}
public string AttributeId { get; }
public float Coefficient { get; }
public float PostAdd { get; }
}
public sealed partial class PublishedTagQuery {
public PublishedTagQuery(IReadOnlyList<string> value_All, IReadOnlyList<string> value_Any, IReadOnlyList<string> value_None) {
All = value_All;
Any = value_Any;
None = value_None;
}
public IReadOnlyList<string> All { get; }
public IReadOnlyList<string> Any { get; }
public IReadOnlyList<string> None { get; }
}
public sealed partial class PublishedAttributeBound {
public PublishedAttributeBound(bool value_Enabled, BoundSource value_Source, float value_Constant, string value_AttributeId) {
Enabled = value_Enabled;
Source = value_Source;
Constant = value_Constant;
AttributeId = value_AttributeId;
}
public bool Enabled { get; }
public BoundSource Source { get; }
public float Constant { get; }
public string AttributeId { get; }
}
public sealed partial class PublishedAttribute {
public PublishedAttribute(string value_Id, string value_AttributeId, PublishedAttributeBound value_Minimum, PublishedAttributeBound value_Maximum) {
Id = value_Id;
AttributeId = value_AttributeId;
Minimum = value_Minimum;
Maximum = value_Maximum;
}
public string Id { get; }
public string AttributeId { get; }
public PublishedAttributeBound Minimum { get; }
public PublishedAttributeBound Maximum { get; }
}
public sealed partial class PublishedEffectDuration {
public PublishedEffectDuration(DurationPolicy value_Policy, PublishedMagnitude value_Magnitude) {
Policy = value_Policy;
Magnitude = value_Magnitude;
}
public DurationPolicy Policy { get; }
public PublishedMagnitude Magnitude { get; }
}
public sealed partial class PublishedEffectPeriod {
public PublishedEffectPeriod(bool value_Enabled, PublishedMagnitude value_Magnitude) {
Enabled = value_Enabled;
Magnitude = value_Magnitude;
}
public bool Enabled { get; }
public PublishedMagnitude Magnitude { get; }
}
public sealed partial class PublishedEffectStacking {
public PublishedEffectStacking(StackingPolicy value_Policy, int value_MaxStacks, DurationUpdate value_DurationUpdate, PeriodUpdate value_PeriodUpdate, OverflowPolicy value_OverflowPolicy) {
Policy = value_Policy;
MaxStacks = value_MaxStacks;
DurationUpdate = value_DurationUpdate;
PeriodUpdate = value_PeriodUpdate;
OverflowPolicy = value_OverflowPolicy;
}
public StackingPolicy Policy { get; }
public int MaxStacks { get; }
public DurationUpdate DurationUpdate { get; }
public PeriodUpdate PeriodUpdate { get; }
public OverflowPolicy OverflowPolicy { get; }
}
public sealed partial class PublishedEffect {
public PublishedEffect(string value_Id, string value_EffectId, uint value_DefinitionRevision, ScopePolicy value_ScopePolicy, LifetimePolicy value_LifetimePolicy, IReadOnlyList<string> value_EffectTags, PublishedEffectDuration value_Duration, PublishedEffectPeriod value_Period, bool value_ExecuteOnApplication, PublishedEffectStacking value_Stacking, IReadOnlyList<string> value_SetByCallerParameters, IReadOnlyList<PublishedComponent> value_Components) {
Id = value_Id;
EffectId = value_EffectId;
DefinitionRevision = value_DefinitionRevision;
ScopePolicy = value_ScopePolicy;
LifetimePolicy = value_LifetimePolicy;
EffectTags = value_EffectTags;
Duration = value_Duration;
Period = value_Period;
ExecuteOnApplication = value_ExecuteOnApplication;
Stacking = value_Stacking;
SetByCallerParameters = value_SetByCallerParameters;
Components = value_Components;
}
public string Id { get; }
public string EffectId { get; }
public uint DefinitionRevision { get; }
public ScopePolicy ScopePolicy { get; }
public LifetimePolicy LifetimePolicy { get; }
public IReadOnlyList<string> EffectTags { get; }
public PublishedEffectDuration Duration { get; }
public PublishedEffectPeriod Period { get; }
public bool ExecuteOnApplication { get; }
public PublishedEffectStacking Stacking { get; }
public IReadOnlyList<string> SetByCallerParameters { get; }
public IReadOnlyList<PublishedComponent> Components { get; }
}
[ValueUnionTemplate]
public readonly struct ComponentValue {
[ValueUnionCase] public readonly PublishedModifier Modifier;
[ValueUnionCase] public readonly PublishedGrantedTags GrantedTags;
[ValueUnionCase] public readonly PublishedBlockAbilities BlockAbilities;
[ValueUnionCase] public readonly PublishedReplacementSourceRequirement ReplacementSourceRequirement;
[ValueUnionCase] public readonly PublishedTagRequirements TagRequirements;
[ValueUnionCase] public readonly PublishedAttributeRequirements AttributeRequirements;
[ValueUnionCase] public readonly PublishedExecution Execution;
[ValueUnionCase] public readonly PublishedAdditionalEffectsComponent AdditionalEffectsComponent;
[ValueUnionCase] public readonly PublishedCueBinding CueBinding;
}
[ValueUnion(typeof(ComponentValue))]
public readonly partial struct PublishedComponent {}
public sealed partial class PublishedModifier {
public PublishedModifier(string value_AttributeId, ModifierApplication value_Application, ModifierOperation value_Operation, PublishedMagnitude value_Magnitude, int value_Priority, ClampBound value_ClampBound, bool value_ScaleWithStack) {
AttributeId = value_AttributeId;
Application = value_Application;
Operation = value_Operation;
Magnitude = value_Magnitude;
Priority = value_Priority;
ClampBound = value_ClampBound;
ScaleWithStack = value_ScaleWithStack;
}
public string AttributeId { get; }
public ModifierApplication Application { get; }
public ModifierOperation Operation { get; }
public PublishedMagnitude Magnitude { get; }
public int Priority { get; }
public ClampBound ClampBound { get; }
public bool ScaleWithStack { get; }
}
public sealed partial class PublishedGrantedTags {
public PublishedGrantedTags(IReadOnlyList<string> value_Tags) {
Tags = value_Tags;
}
public IReadOnlyList<string> Tags { get; }
}
public sealed partial class PublishedBlockAbilities {
public PublishedBlockAbilities(PublishedTagQuery value_Query) {
Query = value_Query;
}
public PublishedTagQuery Query { get; }
}
public sealed partial class PublishedReplacementSourceRequirement {
public PublishedReplacementSourceRequirement(PublishedTagQuery value_CandidateQuery, PublishedTagQuery value_SelectedSourceQuery) {
CandidateQuery = value_CandidateQuery;
SelectedSourceQuery = value_SelectedSourceQuery;
}
public PublishedTagQuery CandidateQuery { get; }
public PublishedTagQuery SelectedSourceQuery { get; }
}
public sealed partial class PublishedTagRequirements {
public PublishedTagRequirements(RequirementPhase value_Phase, PublishedTagQuery value_Source, PublishedTagQuery value_Target) {
Phase = value_Phase;
Source = value_Source;
Target = value_Target;
}
public RequirementPhase Phase { get; }
public PublishedTagQuery Source { get; }
public PublishedTagQuery Target { get; }
}
public sealed partial class PublishedAttributeRequirements {
public PublishedAttributeRequirements(RequirementPhase value_Phase, AttributeSource value_Source, string value_AttributeId, AttributeComparison value_Comparison, PublishedMagnitude value_Threshold) {
Phase = value_Phase;
Source = value_Source;
AttributeId = value_AttributeId;
Comparison = value_Comparison;
Threshold = value_Threshold;
}
public RequirementPhase Phase { get; }
public AttributeSource Source { get; }
public string AttributeId { get; }
public AttributeComparison Comparison { get; }
public PublishedMagnitude Threshold { get; }
}
public sealed partial class PublishedExecution {
public PublishedExecution(IReadOnlyList<PublishedMutation> value_Mutations) {
Mutations = value_Mutations;
}
public IReadOnlyList<PublishedMutation> Mutations { get; }
}
public sealed partial class PublishedAdditionalEffectsComponent {
public PublishedAdditionalEffectsComponent(IReadOnlyList<PublishedAdditionalEffect> value_Effects) {
Effects = value_Effects;
}
public IReadOnlyList<PublishedAdditionalEffect> Effects { get; }
}
public sealed partial class PublishedCueBinding {
public PublishedCueBinding(string value_CueId, CueTrigger value_Trigger) {
CueId = value_CueId;
Trigger = value_Trigger;
}
public string CueId { get; }
public CueTrigger Trigger { get; }
}
public sealed partial class PublishedMutation {
public PublishedMutation(string value_AttributeId, ModifierOperation value_Operation, PublishedMagnitude value_Magnitude, ClampBound value_ClampBound) {
AttributeId = value_AttributeId;
Operation = value_Operation;
Magnitude = value_Magnitude;
ClampBound = value_ClampBound;
}
public string AttributeId { get; }
public ModifierOperation Operation { get; }
public PublishedMagnitude Magnitude { get; }
public ClampBound ClampBound { get; }
}
public sealed partial class PublishedAdditionalEffect {
public PublishedAdditionalEffect(AdditionalTrigger value_Trigger, string value_Effect, IReadOnlyList<PublishedParameterBinding> value_ParameterBindings) {
Trigger = value_Trigger;
Effect = value_Effect;
ParameterBindings = value_ParameterBindings;
}
public AdditionalTrigger Trigger { get; }
public string Effect { get; }
public IReadOnlyList<PublishedParameterBinding> ParameterBindings { get; }
}
public sealed partial class PublishedParameterBinding {
public PublishedParameterBinding(string value_ChildParameterId, ParameterSource value_Source, string value_ParentParameterId, float value_Constant) {
ChildParameterId = value_ChildParameterId;
Source = value_Source;
ParentParameterId = value_ParentParameterId;
Constant = value_Constant;
}
public string ChildParameterId { get; }
public ParameterSource Source { get; }
public string ParentParameterId { get; }
public float Constant { get; }
}
public sealed partial class PublishedInitialAttribute {
public PublishedInitialAttribute(string value_Attribute, float value_BaseValue) {
Attribute = value_Attribute;
BaseValue = value_BaseValue;
}
public string Attribute { get; }
public float BaseValue { get; }
}
public sealed partial class PublishedInitialEffect {
public PublishedInitialEffect(string value_Effect, IReadOnlyList<PublishedInitialParameter> value_Parameters) {
Effect = value_Effect;
Parameters = value_Parameters;
}
public string Effect { get; }
public IReadOnlyList<PublishedInitialParameter> Parameters { get; }
}
public sealed partial class PublishedInitialParameter {
public PublishedInitialParameter(string value_ParameterId, float value_Value) {
ParameterId = value_ParameterId;
Value = value_Value;
}
public string ParameterId { get; }
public float Value { get; }
}
public sealed partial class PublishedCharacter {
public PublishedCharacter(string value_Id, IReadOnlyList<string> value_Attributes, IReadOnlyList<PublishedInitialAttribute> value_InitialAttributes, IReadOnlyList<string> value_InitialTags, IReadOnlyList<string> value_Effects, IReadOnlyList<PublishedInitialEffect> value_InitialEffects) {
Id = value_Id;
Attributes = value_Attributes;
InitialAttributes = value_InitialAttributes;
InitialTags = value_InitialTags;
Effects = value_Effects;
InitialEffects = value_InitialEffects;
}
public string Id { get; }
public IReadOnlyList<string> Attributes { get; }
public IReadOnlyList<PublishedInitialAttribute> InitialAttributes { get; }
public IReadOnlyList<string> InitialTags { get; }
public IReadOnlyList<string> Effects { get; }
public IReadOnlyList<PublishedInitialEffect> InitialEffects { get; }
}
public sealed partial class PublishedGameplayConfiguration {
public PublishedGameplayConfiguration(IReadOnlyList<PublishedAttribute> value_Attributes, IReadOnlyList<PublishedEffect> value_Effects, IReadOnlyList<PublishedCharacter> value_Characters) {
Attributes = value_Attributes;
Effects = value_Effects;
Characters = value_Characters;
}
public IReadOnlyList<PublishedAttribute> Attributes { get; }
public IReadOnlyList<PublishedEffect> Effects { get; }
public IReadOnlyList<PublishedCharacter> Characters { get; }
}
public enum GameplayConfigRecordKind {
Attributes,
Effects,
Characters,
} }
namespace ThirdPersonGameplay.Configuration { public static class GameplayConfigGeneratedContract { public const string SchemaIdentity = "09965fb7db093f7708d3925003e612bb74b7a0fa5b4b20cba698d77726ccf694"; } }
