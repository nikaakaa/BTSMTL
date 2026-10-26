using System;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Serialization;
namespace ThirdPersonGameplay.Configuration {
public sealed class GameplayConfigClosure {
readonly GameplayConfigurationData m_Data;
readonly SortedDictionary<string, Attribute> m_Attributes = new SortedDictionary<string, Attribute>(StringComparer.Ordinal);
readonly SortedDictionary<string, Effect> m_Effects = new SortedDictionary<string, Effect>(StringComparer.Ordinal);
readonly SortedDictionary<string, Character> m_Characters = new SortedDictionary<string, Character>(StringComparer.Ordinal);
public GameplayConfigClosure(GameplayConfigurationData data) => m_Data = data;
public void Include(Attribute value) {
if (!m_Attributes.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(Effect value) {
if (!m_Effects.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(Character value) {
if (!m_Characters.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(GameplayConfigRecordKind kind, string id) { switch (kind) {
case GameplayConfigRecordKind.Attributes: Include(m_Data.Tables.Attributes.GetById(id)); break;
case GameplayConfigRecordKind.Effects: Include(m_Data.Tables.Effects.GetById(id)); break;
case GameplayConfigRecordKind.Characters: Include(m_Data.Tables.Characters.Get(id)); break;
default: throw new ArgumentOutOfRangeException(nameof(kind)); } }
void Visit(Magnitude value) {
switch (value) {
case SourceAttributeSnapshotMagnitude child: Visit(child); break;
case TargetAttributeSnapshotMagnitude child: Visit(child); break;
case TargetAttributeLiveMagnitude child: Visit(child); break;
}
}
void Visit(SourceAttributeSnapshotMagnitude value) {
Include(value.AttributeId_Ref);
}
void Visit(TargetAttributeSnapshotMagnitude value) {
Include(value.AttributeId_Ref);
}
void Visit(TargetAttributeLiveMagnitude value) {
Include(value.AttributeId_Ref);
}
void Visit(AttributeBound value) {
switch (value) {
case AttributeReferenceBound child: Visit(child); break;
}
}
void Visit(AttributeReferenceBound value) {
Include(value.AttributeId_Ref);
}
void Visit(Attribute value) {
Visit(value.Minimum);
Visit(value.Maximum);
}
void Visit(EffectDuration value) {
switch (value) {
case TimedDuration child: Visit(child); break;
}
}
void Visit(TimedDuration value) {
Visit(value.Magnitude);
}
void Visit(EffectPeriod value) {
switch (value) {
case FixedPeriod child: Visit(child); break;
}
}
void Visit(FixedPeriod value) {
Visit(value.Magnitude);
}
void Visit(Effect value) {
Visit(value.Duration);
Visit(value.Period);
foreach (var child in value.Components) Visit(child);
}
void Visit(Component value) {
switch (value) {
case Modifier child: Visit(child); break;
case AttributeRequirements child: Visit(child); break;
case Execution child: Visit(child); break;
case AdditionalEffectsComponent child: Visit(child); break;
}
}
void Visit(Modifier value) {
Include(value.AttributeId_Ref);
Visit(value.Magnitude);
}
void Visit(AttributeRequirements value) {
Include(value.AttributeId_Ref);
Visit(value.Threshold);
}
void Visit(Execution value) {
foreach (var child in value.Mutations) Visit(child);
}
void Visit(AdditionalEffectsComponent value) {
foreach (var child in value.Effects) Visit(child);
}
void Visit(Mutation value) {
Include(value.AttributeId_Ref);
Visit(value.Magnitude);
}
void Visit(AdditionalEffect value) {
Include(value.Effect_Ref);
}
void Visit(InitialAttribute value) {
Include(value.Attribute_Ref);
}
void Visit(InitialEffect value) {
Include(value.Effect_Ref);
}
void Visit(Character value) {
foreach (var child in value.Attributes_Ref) Include(child);
foreach (var child in value.InitialAttributes) Visit(child);
foreach (var child in value.InitialEffects) Visit(child);
}
public PublishedGameplayConfiguration Publish() => new PublishedGameplayConfiguration(
m_Attributes.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Effects.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Characters.Values.Select(GameplayConfigProjection.Publish).ToArray());
public StableHash ContentIdentity() => MemoryPackGameplayConfigurationContent.ComputeIdentity(m_Data.Metadata.SourceId, Publish());
} }
