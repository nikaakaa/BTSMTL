using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
using GameplayEffectPredictionStateData = ThirdPersonSimulation.GameplayEffectPredictionStateData<ThirdPersonSimulation.FPGameplay.PortablePredictionAttributeSnapshot>;

namespace ThirdPersonSimulation.FPGameplay
{
    internal sealed partial class FPCharacterGameplayEffectRuntimeState
    {
        internal SimulationGameplayEffectState PrepareReadState() => m_Working;
    }

    internal sealed partial class SimulationGameplayEffectState
    {
        internal GameplayEffectStateAggregate BeginPreparedRead()
        {
            var value = new GameplayEffectStateAggregate(
                m_Catalog,
                m_TagSourceSnapshotPages.Begin(0),
                m_AttributeSnapshotPages.Begin(),
                m_ActiveEffectSnapshotPages.Begin(0),
                m_PeriodSnapshotPages.Begin(0),
                m_JournalSnapshotPages.Begin(0),
                m_Scratch,
                m_LifecycleSnapshotPages.Begin(0),
                GameplayTagSnapshot.Empty,
                0);
            value.RetainSnapshots();
            return value;
        }

        internal PortableAttributeDefinition ResolvePreparedAttribute(string id) => m_Catalog.Attributes[id];

        internal PortableEffectDefinition ResolvePreparedEffect(string id) => m_Catalog.Effects[id];

        internal Span<string> ReservePreparedTags(int count, out GameplayTagSnapshot value)
        {
            Span<string> values = m_TagSnapshotPages.Reserve(count, out value);
            value.RetainStateStorage();
            return values;
        }

        internal PortableEffectSpecState PrepareReadSpec() => m_Scratch.AcquireDecodedSpec();

        internal void RestorePreparedSpec(PortableEffectSpecState value, in PortableEffectSpecData data) =>
            value.Data.CopyFrom(in data);

        internal PortablePredictionRecord PrepareReadPredictionRecord() => m_Scratch.AcquirePredictionRecord();

        internal PortablePredictionRecord CompleteReadPredictionRecord(PortablePredictionRecord value,
            PortableEffectSpecState spec, in GameplayEffectPredictionStateData data)
        {
            value.Spec = spec;
            value.Data.CopyFrom(in data);
            value.Retain();
            return value;
        }

        internal void ReleaseIncompletePredictionRecord(PortablePredictionRecord value) =>
            m_Scratch.CancelPredictionRecord(value);

        internal GameplayEffectStateAggregate CompleteRead(in GameplayEffectStateAggregate value, ulong changeCursor) =>
            value.CompleteRead(m_OwnedTagSnapshotPages, m_Scratch.FrozenOwnedTags, changeCursor);
    }

    internal readonly partial struct GameplayEffectStateAggregate
    {
        internal FPGameplayEffectModifierSnapshotReference PrepareReadModifiers(int attributeIndex) =>
            new FPGameplayEffectModifierSnapshotReference(
                m_Attributes.Page.ModifierBuffers[attributeIndex], m_Attributes.Page);

        internal void RestorePreparedAttribute(int index, string id, in PortableAttributeData data,
            in FPGameplayEffectModifierSnapshotReference modifiers)
        {
            m_Attributes.Page.Values[index] = new KeyValuePair<string, PortableAttributeSnapshot>(id,
                new PortableAttributeSnapshot(in data, modifiers));
        }

        internal void AddPreparedTagSource(GameplayTagSourceIdentityValue source, GameplayTagSnapshot tags)
        {
            source.Retain();
            tags.RetainStateStorage();
            FPGameplayEffectSnapshotPage<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> page =
                m_TagSources.Page;
            page.Values[page.Count++] = new KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>(
                source, tags);
        }

        internal void AddPreparedActiveEffect(in PortableActiveEffectSnapshot value)
        {
            value.Spec.Retain();
            value.LifecycleContext.Retain();
            FPGameplayEffectSnapshotPage<PortableActiveEffectSnapshot> page = m_ActiveEffects.Page;
            page.Values[page.Count++] = value;
        }

        internal void AddPreparedPeriod(in KeyValuePair<ulong, ulong> value)
        {
            FPGameplayEffectSnapshotPage<KeyValuePair<ulong, ulong>> page = m_Periods.Page;
            page.Values[page.Count++] = value;
        }

        internal void AddPreparedLifecycleRevision(
            in KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> value)
        {
            value.Key.Retain();
            value.Value.Retain();
            FPGameplayEffectSnapshotPage<KeyValuePair<GameplayEffectLifecycleRevisionKey,
                GameplayEffectLifecycleRevisionEntry>> page = m_LastLifecycleRevisions.Page;
            page.Values[page.Count++] = value;
        }

        internal List<PortablePredictionRecord> PrepareReadJournalGroup(ulong predictionKey)
        {
            List<PortablePredictionRecord> values = m_JournalOwner.JournalGroups.Acquire();
            FPGameplayEffectSnapshotPage<KeyValuePair<ulong, List<PortablePredictionRecord>>> page = m_Journal.Page;
            page.Values[page.Count++] = new KeyValuePair<ulong, List<PortablePredictionRecord>>(predictionKey, values);
            return values;
        }

        internal void AddPreparedPredictionRecord(List<PortablePredictionRecord> group, PortablePredictionRecord value)
        {
            value.Retain();
            group.Add(value);
        }

        internal GameplayEffectStateAggregate CompleteRead(GameplayTagSnapshotBank ownedTagPages,
            HashSet<string> ownedTagSet, ulong changeCursor)
        {
            GameplayTagSnapshot ownedTags = CollectOwnedTags(m_TagSources.Values, ownedTagSet,
                ownedTagPages, GameplayTagSnapshot.Empty);
            ownedTags.RetainStateStorage();
            return new GameplayEffectStateAggregate(m_Catalog, m_TagSources, m_Attributes, m_ActiveEffects,
                m_Periods, m_Journal, m_JournalOwner, m_LastLifecycleRevisions, ownedTags, changeCursor);
        }
    }

    internal readonly partial struct FPGameplayEffectModifierSnapshotReference
    {
        internal void AddPreparedModifier(in PortableAttributeModifierState value) => m_Buffer.Add(value);
    }

    internal readonly partial struct PortableAttributeSnapshot
    {
        internal PortableAttributeSnapshot(in PortableAttributeData data,
            FPGameplayEffectModifierSnapshotReference modifiers)
        {
            Data = data;
            m_Modifiers = modifiers;
        }
    }

    internal readonly partial struct PortableActiveEffectSnapshot
    {
        internal PortableActiveEffectSnapshot(in GameplayEffectActiveStateData data, PortableEffectSpecState spec)
        {
            Data = data;
            Spec = spec;
        }
    }
}
