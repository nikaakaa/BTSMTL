using System;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonSimulation;
namespace ThirdPersonGameplay.Configuration {
public sealed class GameplayConfigClosure {
readonly GameplayConfigurationData m_Data;
readonly SortedDictionary<string, Attribute> m_Attributes = new SortedDictionary<string, Attribute>(StringComparer.Ordinal);
readonly SortedDictionary<string, Effect> m_Effects = new SortedDictionary<string, Effect>(StringComparer.Ordinal);
readonly SortedDictionary<string, Attack> m_Attacks = new SortedDictionary<string, Attack>(StringComparer.Ordinal);
readonly SortedDictionary<string, ComponentRecord> m_Components = new SortedDictionary<string, ComponentRecord>(StringComparer.Ordinal);
readonly SortedDictionary<string, Mutation> m_Mutations = new SortedDictionary<string, Mutation>(StringComparer.Ordinal);
readonly SortedDictionary<string, AdditionalEffect> m_AdditionalEffects = new SortedDictionary<string, AdditionalEffect>(StringComparer.Ordinal);
readonly SortedDictionary<string, ParameterBinding> m_ParameterBindings = new SortedDictionary<string, ParameterBinding>(StringComparer.Ordinal);
readonly SortedDictionary<string, InitialAttribute> m_InitialAttributes = new SortedDictionary<string, InitialAttribute>(StringComparer.Ordinal);
readonly SortedDictionary<string, InitialEffect> m_InitialEffects = new SortedDictionary<string, InitialEffect>(StringComparer.Ordinal);
readonly SortedDictionary<string, InitialParameter> m_InitialParameters = new SortedDictionary<string, InitialParameter>(StringComparer.Ordinal);
readonly SortedDictionary<string, Character> m_Characters = new SortedDictionary<string, Character>(StringComparer.Ordinal);
public GameplayConfigClosure(GameplayConfigurationData data) => m_Data = data;
public void Include(Attribute value) {
if (!m_Attributes.TryAdd(value.Id, value)) return;
}
public void Include(Effect value) {
if (!m_Effects.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(Attack value) {
if (!m_Attacks.TryAdd(value.Id, value)) return;
}
public void Include(ComponentRecord value) {
if (!m_Components.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(Mutation value) {
if (!m_Mutations.TryAdd(value.Id, value)) return;
}
public void Include(AdditionalEffect value) {
if (!m_AdditionalEffects.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(ParameterBinding value) {
if (!m_ParameterBindings.TryAdd(value.Id, value)) return;
}
public void Include(InitialAttribute value) {
if (!m_InitialAttributes.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(InitialEffect value) {
if (!m_InitialEffects.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(InitialParameter value) {
if (!m_InitialParameters.TryAdd(value.Id, value)) return;
}
public void Include(Character value) {
if (!m_Characters.TryAdd(value.Id, value)) return;
Visit(value);
}
public void Include(GameplayConfigRecordKind kind, string id) { switch (kind) {
case GameplayConfigRecordKind.Attributes: Include(m_Data.Tables.Attributes.Get(id)); break;
case GameplayConfigRecordKind.Effects: Include(m_Data.Tables.Effects.Get(id)); break;
case GameplayConfigRecordKind.Attacks: Include(m_Data.Tables.Attacks.Get(id)); break;
case GameplayConfigRecordKind.Components: Include(m_Data.Tables.Components.Get(id)); break;
case GameplayConfigRecordKind.Mutations: Include(m_Data.Tables.Mutations.Get(id)); break;
case GameplayConfigRecordKind.AdditionalEffects: Include(m_Data.Tables.AdditionalEffects.Get(id)); break;
case GameplayConfigRecordKind.ParameterBindings: Include(m_Data.Tables.ParameterBindings.Get(id)); break;
case GameplayConfigRecordKind.InitialAttributes: Include(m_Data.Tables.InitialAttributes.Get(id)); break;
case GameplayConfigRecordKind.InitialEffects: Include(m_Data.Tables.InitialEffects.Get(id)); break;
case GameplayConfigRecordKind.InitialParameters: Include(m_Data.Tables.InitialParameters.Get(id)); break;
case GameplayConfigRecordKind.Characters: Include(m_Data.Tables.Characters.Get(id)); break;
default: throw new ArgumentOutOfRangeException(nameof(kind)); } }
void Visit(Effect value) {
foreach (var child in value.Components_Ref) Include(child);
}
void Visit(Component value) {
switch (value) {
case Execution child: Visit(child); break;
case AdditionalEffectsComponent child: Visit(child); break;
}
}
void Visit(Execution value) {
foreach (var child in value.Mutations_Ref) Include(child);
}
void Visit(AdditionalEffectsComponent value) {
foreach (var child in value.Effects_Ref) Include(child);
}
void Visit(ComponentRecord value) {
Visit(value.Value);
}
void Visit(AdditionalEffect value) {
Include(value.Effect_Ref);
foreach (var child in value.ParameterBindings_Ref) Include(child);
}
void Visit(InitialAttribute value) {
Include(value.Attribute_Ref);
}
void Visit(InitialEffect value) {
Include(value.Effect_Ref);
foreach (var child in value.Parameters_Ref) Include(child);
}
void Visit(Character value) {
foreach (var child in value.Attributes_Ref) Include(child);
foreach (var child in value.InitialAttributes_Ref) Include(child);
foreach (var child in value.Effects_Ref) Include(child);
foreach (var child in value.Attacks_Ref) Include(child);
foreach (var child in value.InitialEffects_Ref) Include(child);
}
public PublishedGameplayConfiguration Publish() => new PublishedGameplayConfiguration(
m_Attributes.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Effects.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Attacks.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Components.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Mutations.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_AdditionalEffects.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_ParameterBindings.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_InitialAttributes.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_InitialEffects.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_InitialParameters.Values.Select(GameplayConfigProjection.Publish).ToArray(),
m_Characters.Values.Select(GameplayConfigProjection.Publish).ToArray());
public StableHash ContentIdentity() { using var writer = new CanonicalWriter(); writer.WriteText(m_Data.Metadata.SourceId); writer.WriteHash(PublishedGameplayConfiguration.CanonicalSchemaHash); PublishedGameplayConfiguration.CanonicalWrite(writer, Publish()); return SimulationCanonicalPayloadHash.Compute(writer.ToArray()); }
} }
