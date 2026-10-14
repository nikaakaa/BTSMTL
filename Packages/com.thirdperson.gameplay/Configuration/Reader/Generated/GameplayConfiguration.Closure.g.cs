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
void Visit(Effect value) {
foreach (var child in value.Components) Visit(child);
}
void Visit(Component value) {
switch (value) {
case AdditionalEffectsComponent child: Visit(child); break;
}
}
void Visit(AdditionalEffectsComponent value) {
foreach (var child in value.Effects) Visit(child);
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
foreach (var child in value.Effects_Ref) Include(child);
foreach (var child in value.InitialEffects) Visit(child);
}
public PublishedGameplayConfiguration Publish() => new PublishedGameplayConfiguration(
m_Attributes.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Effects.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Characters.Values.Select(GameplayConfigProjection.Publish).ToArray());
public StableHash ContentIdentity() => MemoryPackGameplayConfigurationContent.ComputeIdentity(m_Data.Metadata.SourceId, Publish());
} }
