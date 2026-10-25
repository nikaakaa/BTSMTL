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
[ValueUnionTemplate]
public readonly struct AttributeBoundValue {
[ValueUnionCase] public readonly PublishedUnboundedAttributeBound UnboundedAttributeBound;
[ValueUnionCase] public readonly PublishedConstantAttributeBound ConstantAttributeBound;
[ValueUnionCase] public readonly PublishedAttributeReferenceBound AttributeReferenceBound;
}
[ValueUnion(typeof(AttributeBoundValue))]
public readonly partial struct PublishedAttributeBound {}
public sealed partial class PublishedUnboundedAttributeBound {
public PublishedUnboundedAttributeBound() {
}
}
public sealed partial class PublishedConstantAttributeBound {
public PublishedConstantAttributeBound(float value_Value) {
Value = value_Value;
}
public float Value { get; }
}
public sealed partial class PublishedAttributeReferenceBound {
public PublishedAttributeReferenceBound(string value_AttributeId) {
AttributeId = value_AttributeId;
}
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
[ValueUnionTemplate]
public readonly struct EffectDurationValue {
[ValueUnionCase] public readonly PublishedInstantDuration InstantDuration;
[ValueUnionCase] public readonly PublishedInfiniteDuration InfiniteDuration;
[ValueUnionCase] public readonly PublishedTimedDuration TimedDuration;
}
[ValueUnion(typeof(EffectDurationValue))]
public readonly partial struct PublishedEffectDuration {}
public sealed partial class PublishedInstantDuration {
public PublishedInstantDuration() {
}
}
public sealed partial class PublishedInfiniteDuration {
public PublishedInfiniteDuration() {
}
}
public sealed partial class PublishedTimedDuration {
public PublishedTimedDuration(PublishedMagnitude value_Magnitude) {
Magnitude = value_Magnitude;
}
public PublishedMagnitude Magnitude { get; }
}
[ValueUnionTemplate]
public readonly struct EffectPeriodValue {
[ValueUnionCase] public readonly PublishedNoPeriod NoPeriod;
[ValueUnionCase] public readonly PublishedFixedPeriod FixedPeriod;
}
[ValueUnion(typeof(EffectPeriodValue))]
public readonly partial struct PublishedEffectPeriod {}
public sealed partial class PublishedNoPeriod {
public PublishedNoPeriod() {
}
}
public sealed partial class PublishedFixedPeriod {
public PublishedFixedPeriod(PublishedMagnitude value_Magnitude) {
Magnitude = value_Magnitude;
}
public PublishedMagnitude Magnitude { get; }
}
[ValueUnionTemplate]
public readonly struct EffectStackingValue {
[ValueUnionCase] public readonly PublishedIndependentStacking IndependentStacking;
[ValueUnionCase] public readonly PublishedAggregateStacking AggregateStacking;
}
[ValueUnion(typeof(EffectStackingValue))]
public readonly partial struct PublishedEffectStacking {}
public sealed partial class PublishedIndependentStacking {
public PublishedIndependentStacking() {
}
}
public sealed partial class PublishedAggregateStacking {
public PublishedAggregateStacking(StackingAggregation value_By, int value_MaxStacks, DurationUpdate value_DurationUpdate, PeriodUpdate value_PeriodUpdate, OverflowPolicy value_OverflowPolicy) {
By = value_By;
MaxStacks = value_MaxStacks;
DurationUpdate = value_DurationUpdate;
PeriodUpdate = value_PeriodUpdate;
OverflowPolicy = value_OverflowPolicy;
}
public StackingAggregation By { get; }
public int MaxStacks { get; }
public DurationUpdate DurationUpdate { get; }
public PeriodUpdate PeriodUpdate { get; }
public OverflowPolicy OverflowPolicy { get; }
}
[ValueUnionTemplate]
public readonly struct AttributeOperationValue {
[ValueUnionCase] public readonly PublishedAdditiveOperation AdditiveOperation;
[ValueUnionCase] public readonly PublishedMultiplicativeOperation MultiplicativeOperation;
[ValueUnionCase] public readonly PublishedOverrideOperation OverrideOperation;
[ValueUnionCase] public readonly PublishedClampOperation ClampOperation;
}
[ValueUnion(typeof(AttributeOperationValue))]
public readonly partial struct PublishedAttributeOperation {}
public sealed partial class PublishedAdditiveOperation {
public PublishedAdditiveOperation() {
}
}
public sealed partial class PublishedMultiplicativeOperation {
public PublishedMultiplicativeOperation() {
}
}
public sealed partial class PublishedOverrideOperation {
public PublishedOverrideOperation() {
}
}
public sealed partial class PublishedClampOperation {
public PublishedClampOperation(ClampBound value_Bound) {
Bound = value_Bound;
}
public ClampBound Bound { get; }
}
public sealed partial class PublishedEffect {
public PublishedEffect(string value_Id, string value_EffectId, ScopePolicy value_ScopePolicy, LifetimePolicy value_LifetimePolicy, IReadOnlyList<string> value_EffectTags, PublishedEffectDuration value_Duration, PublishedEffectPeriod value_Period, bool value_ExecuteOnApplication, PublishedEffectStacking value_Stacking, IReadOnlyList<string> value_SetByCallerParameters, IReadOnlyList<PublishedComponent> value_Components) {
Id = value_Id;
EffectId = value_EffectId;
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
public PublishedModifier(string value_AttributeId, ModifierApplication value_Application, PublishedAttributeOperation value_Operation, PublishedMagnitude value_Magnitude, int value_Priority, bool value_ScaleWithStack) {
AttributeId = value_AttributeId;
Application = value_Application;
Operation = value_Operation;
Magnitude = value_Magnitude;
Priority = value_Priority;
ScaleWithStack = value_ScaleWithStack;
}
public string AttributeId { get; }
public ModifierApplication Application { get; }
public PublishedAttributeOperation Operation { get; }
public PublishedMagnitude Magnitude { get; }
public int Priority { get; }
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
public PublishedMutation(string value_AttributeId, PublishedAttributeOperation value_Operation, PublishedMagnitude value_Magnitude) {
AttributeId = value_AttributeId;
Operation = value_Operation;
Magnitude = value_Magnitude;
}
public string AttributeId { get; }
public PublishedAttributeOperation Operation { get; }
public PublishedMagnitude Magnitude { get; }
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
public PublishedParameterBinding(string value_ChildParameterId, PublishedParameterValue value_Value) {
ChildParameterId = value_ChildParameterId;
Value = value_Value;
}
public string ChildParameterId { get; }
public PublishedParameterValue Value { get; }
}
[ValueUnionTemplate]
public readonly struct ParameterValueValue {
[ValueUnionCase] public readonly PublishedParentParameterValue ParentParameterValue;
[ValueUnionCase] public readonly PublishedConstantParameterValue ConstantParameterValue;
}
[ValueUnion(typeof(ParameterValueValue))]
public readonly partial struct PublishedParameterValue {}
public sealed partial class PublishedParentParameterValue {
public PublishedParentParameterValue(string value_ParentParameterId) {
ParentParameterId = value_ParentParameterId;
}
public string ParentParameterId { get; }
}
public sealed partial class PublishedConstantParameterValue {
public PublishedConstantParameterValue(float value_Value) {
Value = value_Value;
}
public float Value { get; }
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
namespace ThirdPersonGameplay.Configuration { public static class GameplayConfigGeneratedContract { public const string SchemaIdentity = "8ba8a41dc7f18df3bc1b7b325685fbb4bed7fae1319a9f97a57cd26c19cd09ad"; } }
