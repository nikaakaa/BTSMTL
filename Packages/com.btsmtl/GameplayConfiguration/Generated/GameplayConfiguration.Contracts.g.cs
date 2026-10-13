using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
namespace ThirdPersonGameplay.Configuration {
[CanonicalUnionTemplate]
public readonly struct MagnitudeValue {
[CanonicalUnionCase] public readonly PublishedConstantMagnitude ConstantMagnitude;
[CanonicalUnionCase] public readonly PublishedScaledConstantMagnitude ScaledConstantMagnitude;
[CanonicalUnionCase] public readonly PublishedSetByCallerMagnitude SetByCallerMagnitude;
[CanonicalUnionCase] public readonly PublishedSourceAttributeSnapshotMagnitude SourceAttributeSnapshotMagnitude;
[CanonicalUnionCase] public readonly PublishedTargetAttributeSnapshotMagnitude TargetAttributeSnapshotMagnitude;
[CanonicalUnionCase] public readonly PublishedTargetAttributeLiveMagnitude TargetAttributeLiveMagnitude;
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
[CanonicalUnion(typeof(MagnitudeValue))]
public readonly partial struct PublishedMagnitude {}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedConstantMagnitude {
public PublishedConstantMagnitude(float value_Value) {
Value = value_Value;
}
public float Value { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedEffect {
public PublishedEffect(string value_Id, string value_EffectId, uint value_DefinitionRevision, ScopePolicy value_ScopePolicy, LifetimePolicy value_LifetimePolicy, IReadOnlyList<string> value_EffectTags, DurationPolicy value_DurationPolicy, PublishedMagnitude value_DurationMagnitude, bool value_HasPeriod, PublishedMagnitude value_PeriodMagnitude, bool value_ExecuteOnApplication, StackingPolicy value_StackingPolicy, int value_MaxStacks, DurationUpdate value_DurationUpdate, PeriodUpdate value_PeriodUpdate, OverflowPolicy value_OverflowPolicy, IReadOnlyList<string> value_SetByCallerParameters, IReadOnlyList<string> value_Components) {
Id = value_Id;
EffectId = value_EffectId;
DefinitionRevision = value_DefinitionRevision;
ScopePolicy = value_ScopePolicy;
LifetimePolicy = value_LifetimePolicy;
EffectTags = value_EffectTags;
DurationPolicy = value_DurationPolicy;
DurationMagnitude = value_DurationMagnitude;
HasPeriod = value_HasPeriod;
PeriodMagnitude = value_PeriodMagnitude;
ExecuteOnApplication = value_ExecuteOnApplication;
StackingPolicy = value_StackingPolicy;
MaxStacks = value_MaxStacks;
DurationUpdate = value_DurationUpdate;
PeriodUpdate = value_PeriodUpdate;
OverflowPolicy = value_OverflowPolicy;
SetByCallerParameters = value_SetByCallerParameters;
Components = value_Components;
}
public string Id { get; }
public string EffectId { get; }
public uint DefinitionRevision { get; }
public ScopePolicy ScopePolicy { get; }
public LifetimePolicy LifetimePolicy { get; }
public IReadOnlyList<string> EffectTags { get; }
public DurationPolicy DurationPolicy { get; }
public PublishedMagnitude DurationMagnitude { get; }
public bool HasPeriod { get; }
public PublishedMagnitude PeriodMagnitude { get; }
public bool ExecuteOnApplication { get; }
public StackingPolicy StackingPolicy { get; }
public int MaxStacks { get; }
public DurationUpdate DurationUpdate { get; }
public PeriodUpdate PeriodUpdate { get; }
public OverflowPolicy OverflowPolicy { get; }
public IReadOnlyList<string> SetByCallerParameters { get; }
public IReadOnlyList<string> Components { get; }
}
[CanonicalUnionTemplate]
public readonly struct ComponentValue {
[CanonicalUnionCase] public readonly PublishedModifier Modifier;
[CanonicalUnionCase] public readonly PublishedGrantedTags GrantedTags;
[CanonicalUnionCase] public readonly PublishedBlockAbilities BlockAbilities;
[CanonicalUnionCase] public readonly PublishedReplacementSourceRequirement ReplacementSourceRequirement;
[CanonicalUnionCase] public readonly PublishedTagRequirements TagRequirements;
[CanonicalUnionCase] public readonly PublishedAttributeRequirements AttributeRequirements;
[CanonicalUnionCase] public readonly PublishedExecution Execution;
[CanonicalUnionCase] public readonly PublishedAdditionalEffectsComponent AdditionalEffectsComponent;
[CanonicalUnionCase] public readonly PublishedCueBinding CueBinding;
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
[CanonicalUnion(typeof(ComponentValue))]
public readonly partial struct PublishedComponent {}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedGrantedTags {
public PublishedGrantedTags(IReadOnlyList<string> value_Tags) {
Tags = value_Tags;
}
public IReadOnlyList<string> Tags { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedBlockAbilities {
public PublishedBlockAbilities(PublishedTagQuery value_Query) {
Query = value_Query;
}
public PublishedTagQuery Query { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedReplacementSourceRequirement {
public PublishedReplacementSourceRequirement(PublishedTagQuery value_CandidateQuery, PublishedTagQuery value_SelectedSourceQuery) {
CandidateQuery = value_CandidateQuery;
SelectedSourceQuery = value_SelectedSourceQuery;
}
public PublishedTagQuery CandidateQuery { get; }
public PublishedTagQuery SelectedSourceQuery { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
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
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedExecution {
public PublishedExecution(IReadOnlyList<string> value_Mutations) {
Mutations = value_Mutations;
}
public IReadOnlyList<string> Mutations { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedAdditionalEffectsComponent {
public PublishedAdditionalEffectsComponent(IReadOnlyList<string> value_Effects) {
Effects = value_Effects;
}
public IReadOnlyList<string> Effects { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedCueBinding {
public PublishedCueBinding(string value_CueId, CueTrigger value_Trigger) {
CueId = value_CueId;
Trigger = value_Trigger;
}
public string CueId { get; }
public CueTrigger Trigger { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedComponentRecord {
public PublishedComponentRecord(string value_Id, PublishedComponent value_Value) {
Id = value_Id;
Value = value_Value;
}
public string Id { get; }
public PublishedComponent Value { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedMutation {
public PublishedMutation(string value_Id, string value_AttributeId, ModifierOperation value_Operation, PublishedMagnitude value_Magnitude, ClampBound value_ClampBound) {
Id = value_Id;
AttributeId = value_AttributeId;
Operation = value_Operation;
Magnitude = value_Magnitude;
ClampBound = value_ClampBound;
}
public string Id { get; }
public string AttributeId { get; }
public ModifierOperation Operation { get; }
public PublishedMagnitude Magnitude { get; }
public ClampBound ClampBound { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedAdditionalEffect {
public PublishedAdditionalEffect(string value_Id, AdditionalTrigger value_Trigger, string value_Effect, IReadOnlyList<string> value_ParameterBindings) {
Id = value_Id;
Trigger = value_Trigger;
Effect = value_Effect;
ParameterBindings = value_ParameterBindings;
}
public string Id { get; }
public AdditionalTrigger Trigger { get; }
public string Effect { get; }
public IReadOnlyList<string> ParameterBindings { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedParameterBinding {
public PublishedParameterBinding(string value_Id, string value_ChildParameterId, ParameterSource value_Source, string value_ParentParameterId, float value_Constant) {
Id = value_Id;
ChildParameterId = value_ChildParameterId;
Source = value_Source;
ParentParameterId = value_ParentParameterId;
Constant = value_Constant;
}
public string Id { get; }
public string ChildParameterId { get; }
public ParameterSource Source { get; }
public string ParentParameterId { get; }
public float Constant { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedInitialAttribute {
public PublishedInitialAttribute(string value_Id, string value_Attribute, float value_BaseValue) {
Id = value_Id;
Attribute = value_Attribute;
BaseValue = value_BaseValue;
}
public string Id { get; }
public string Attribute { get; }
public float BaseValue { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedInitialEffect {
public PublishedInitialEffect(string value_Id, string value_Effect, IReadOnlyList<string> value_Parameters) {
Id = value_Id;
Effect = value_Effect;
Parameters = value_Parameters;
}
public string Id { get; }
public string Effect { get; }
public IReadOnlyList<string> Parameters { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedInitialParameter {
public PublishedInitialParameter(string value_Id, string value_ParameterId, float value_Value) {
Id = value_Id;
ParameterId = value_ParameterId;
Value = value_Value;
}
public string Id { get; }
public string ParameterId { get; }
public float Value { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)]
public sealed partial class PublishedCharacter {
public PublishedCharacter(string value_Id, IReadOnlyList<string> value_Attributes, IReadOnlyList<string> value_InitialAttributes, IReadOnlyList<string> value_InitialTags, IReadOnlyList<string> value_Effects, IReadOnlyList<string> value_InitialEffects) {
Id = value_Id;
Attributes = value_Attributes;
InitialAttributes = value_InitialAttributes;
InitialTags = value_InitialTags;
Effects = value_Effects;
InitialEffects = value_InitialEffects;
}
public string Id { get; }
public IReadOnlyList<string> Attributes { get; }
public IReadOnlyList<string> InitialAttributes { get; }
public IReadOnlyList<string> InitialTags { get; }
public IReadOnlyList<string> Effects { get; }
public IReadOnlyList<string> InitialEffects { get; }
}
[CanonicalContract(MaterializedRead = true, PreparedRead = false)] public sealed partial class PublishedGameplayConfiguration {
public PublishedGameplayConfiguration(IReadOnlyList<PublishedAttribute> value_Attributes, IReadOnlyList<PublishedEffect> value_Effects, IReadOnlyList<PublishedComponentRecord> value_Components, IReadOnlyList<PublishedMutation> value_Mutations, IReadOnlyList<PublishedAdditionalEffect> value_AdditionalEffects, IReadOnlyList<PublishedParameterBinding> value_ParameterBindings, IReadOnlyList<PublishedInitialAttribute> value_InitialAttributes, IReadOnlyList<PublishedInitialEffect> value_InitialEffects, IReadOnlyList<PublishedInitialParameter> value_InitialParameters, IReadOnlyList<PublishedCharacter> value_Characters) {
Attributes = value_Attributes;
Effects = value_Effects;
Components = value_Components;
Mutations = value_Mutations;
AdditionalEffects = value_AdditionalEffects;
ParameterBindings = value_ParameterBindings;
InitialAttributes = value_InitialAttributes;
InitialEffects = value_InitialEffects;
InitialParameters = value_InitialParameters;
Characters = value_Characters;
}
public IReadOnlyList<PublishedAttribute> Attributes { get; }
public IReadOnlyList<PublishedEffect> Effects { get; }
public IReadOnlyList<PublishedComponentRecord> Components { get; }
public IReadOnlyList<PublishedMutation> Mutations { get; }
public IReadOnlyList<PublishedAdditionalEffect> AdditionalEffects { get; }
public IReadOnlyList<PublishedParameterBinding> ParameterBindings { get; }
public IReadOnlyList<PublishedInitialAttribute> InitialAttributes { get; }
public IReadOnlyList<PublishedInitialEffect> InitialEffects { get; }
public IReadOnlyList<PublishedInitialParameter> InitialParameters { get; }
public IReadOnlyList<PublishedCharacter> Characters { get; }
}
public enum GameplayConfigRecordKind {
Attributes,
Effects,
Components,
Mutations,
AdditionalEffects,
ParameterBindings,
InitialAttributes,
InitialEffects,
InitialParameters,
Characters,
} }
namespace ThirdPersonGameplay.Configuration { public static class GameplayConfigGeneratedContract { public const string SchemaIdentity = "34328e2b2fcc0f423be691d5862deec5b9c860c39b62da561caf6541ff35287f"; } }
