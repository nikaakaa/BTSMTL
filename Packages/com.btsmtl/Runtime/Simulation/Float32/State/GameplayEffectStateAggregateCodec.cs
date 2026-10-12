using ThirdPersonSimulation;
using GameplayEffectPredictionStateData = ThirdPersonSimulation.GameplayEffectPredictionStateData<ThirdPersonSimulation.PortablePredictionAttributeSnapshot>;
using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    internal static class GameplayEffectStateAggregateCodec
    {
        const uint TagsMagic = 0x32544745;
        const uint AttributesMagic = 0x32544741;
        const uint ActiveMagic = 0x32464745;
        const uint PeriodsMagic = 0x32504745;
        const uint JournalMagic = 0x324A4745;
        const int StateVersion = 15;
        internal static readonly string CanonicalSchema = CanonicalContractProtocol.ComposeSchema(
            "gameplay-effect-state/" + StateVersion,
            PortableEffectSpecData.CanonicalSchema, PortableAttributeData.CanonicalSchema, PortableAttributeModifierState.CanonicalSchema, SimulationGameplayEffectContext.CanonicalSchema,
            GameplayEffectPredictionStateData.CanonicalSchema, GameplayEffectActiveStateData.CanonicalSchema, GameplayEffectActiveControlSnapshot.CanonicalSchema, GameplayEffectLifecycleRevisionKey.CanonicalSchema,
            GameplayEffectLifecycleRevisionEntry.CanonicalSchema);
        static readonly StableHash CanonicalSchemaHash = new StableHash(CanonicalSchema);

        internal static void Write(CanonicalWriter writer, GameplayEffectStateAggregate aggregate)
        {
            Float32GameplayEffectRuntimeCatalog catalog = aggregate.Catalog;
            writer.WriteHash(CanonicalSchemaHash);
            WriteTags(writer, aggregate.TagSources, catalog);
            WriteAttributes(writer, aggregate.Attributes, catalog);
            WriteActiveEffects(writer, aggregate.ActiveEffects, catalog);
            WritePeriods(writer, aggregate.Periods);
            WriteJournal(writer, aggregate.Journal, aggregate.LastLifecycleRevisions, catalog);
            writer.WriteUInt64(aggregate.ChangeCursor);
        }

        internal static ulong Read(
            CanonicalReader reader,
            CanonicalReader blockReader,
            Float32GameplayEffectRuntimeCatalog catalog,
            Float32GameplayEffectExecutionScratch scratch,
            GameplayTagSnapshotBank tagPages,
            EquipmentStatePageBank equipment,
            SortedList<GameplayTagSourceIdentityValue, IReadOnlyList<string>> tagSources,
            SortedList<string, PortableAttributeState> attributes,
            List<PortableActiveEffectState> activeEffects,
            SortedList<ulong, ulong> periods,
            SortedList<ulong, List<PortablePredictionRecord>> journal,
            SortedList<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> lifecycleRevisions)
        {
            reader.RequireSchema(CanonicalSchemaHash, "Gameplay Effect state");
            GameplayEffectRuntimeCapacityLayout capacity = catalog.CapacityLayout;
            ReadTags(Block(blockReader, reader.ReadBytesSegment(), TagsMagic), catalog, tagPages, equipment, tagSources);
            ReadAttributes(Block(blockReader, reader.ReadBytesSegment(), AttributesMagic), catalog, attributes);
            ReadActiveEffects(Block(blockReader, reader.ReadBytesSegment(), ActiveMagic), catalog, scratch, tagPages, activeEffects);
            ReadPeriods(Block(blockReader, reader.ReadBytesSegment(), PeriodsMagic), capacity, periods);
            ReadJournal(Block(blockReader, reader.ReadBytesSegment(), JournalMagic), catalog, scratch, tagPages, journal, lifecycleRevisions);
            ulong cursor = reader.ReadUInt64();
            reader.RequireComplete();
            return cursor;
        }

        static void WriteTags(CanonicalWriter writer,
            ReadOnlySpan<KeyValuePair<GameplayTagSourceIdentityValue, IReadOnlyList<string>>> tagSources,
            Float32GameplayEffectRuntimeCatalog catalog)
        {
            long prefixPosition = BeginBlock(writer, TagsMagic);
            writer.WriteHash(GameplayTagSourceIdentityValue.CanonicalSchemaHash);
            writer.WriteInt32(tagSources.Length);
            for (int i = 0; i < tagSources.Length; i++)
            {
                GameplayTagSourceIdentityValue.CanonicalWrite(writer, tagSources[i].Key);
                WriteTags(writer, tagSources[i].Value, catalog);
            }
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void ReadTags(CanonicalReader reader, Float32GameplayEffectRuntimeCatalog catalog,
            GameplayTagSnapshotBank pages, EquipmentStatePageBank equipment,
            SortedList<GameplayTagSourceIdentityValue, IReadOnlyList<string>> tagSources)
        {
            GameplayTagSourceIdentityValue.CanonicalRequireSchema(reader);
            int count = ReadCount(reader, catalog.CapacityLayout.SourceWidth);
            GameplayTagSourceIdentityValue previous = default;
            int externalSources = 0;
            int effectSources = 0;
            int totalTags = 0;
            for (int i = 0; i < count; i++)
            {
                GameplayTagSourceIdentityValue source = ReadTagSource(reader, equipment);
                if (i > 0 && previous.CompareTo(source) >= 0)
                    throw new InvalidDataException("Gameplay Tag sources are not canonically ordered.");
                previous = source;
                int width = source.Kind == 0 && !source.IsExternalSource ? catalog.InitialTags.Count :
                    source.Kind == 2 ? catalog.ContentLayout.MaximumGrantedTags :
                    catalog.CapacityLayout.Actor.TagsPerExternalSource;
                if (source.IsExternalSource)
                {
                    externalSources++;
                    if (externalSources > catalog.CapacityLayout.Actor.ExternalTagSources)
                        throw new InvalidDataException("Gameplay Tag sources exceed the declared external source capacity.");
                }
                else if (source.Kind == 2 && ++effectSources > catalog.CapacityLayout.ActiveEffectWidth)
                    throw new InvalidDataException("Gameplay Tag sources exceed the declared Active Effect capacity.");
                GameplayTagSnapshot tags = ReadTags(reader, catalog, pages, true, width);
                source.Retain();
                tagSources.Add(source, tags);
                if (tags.Count == 0)
                    throw new InvalidDataException("Gameplay Tag source must contain its installed tags.");
                totalTags += tags.Count;
                if (totalTags > catalog.CapacityLayout.QueryTagWidth)
                    throw new InvalidDataException("Gameplay Tags exceed the declared Actor query capacity.");
            }
            reader.RequireComplete();
        }

        static GameplayTagSourceIdentityValue ReadTagSource(CanonicalReader reader, EquipmentStatePageBank equipment)
        {
            GameplayTagSourceIdentityValue source = GameplayTagSourceIdentityValue.CanonicalRead(reader);
            if (source.Kind == 3)
            {
                if (equipment == null)
                    throw new InvalidDataException("Gameplay Tag Equipment source requires the published Equipment layout.");
                equipment.Layout.RequireSlot(source.Slot);
            }
            return source;
        }

        static void WriteAttributes(CanonicalWriter writer,
            ReadOnlySpan<KeyValuePair<string, PortableAttributeSnapshot>> attributes,
            Float32GameplayEffectRuntimeCatalog catalog)
        {
            long prefixPosition = BeginBlock(writer, AttributesMagic);
            writer.WriteInt32(attributes.Length);
            for (int index = 0; index < attributes.Length; index++)
            {
                PortableAttributeSnapshot attribute = attributes[index].Value;
                PortableAttributeData.CanonicalWrite(writer, attribute.Data);
                writer.WriteInt32(attribute.Modifiers.Length);
                for (int i = 0; i < attribute.Modifiers.Length; i++)
                    PortableAttributeModifierState.CanonicalWrite(writer, attribute.Modifiers[i]);
            }
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void ReadAttributes(CanonicalReader reader, Float32GameplayEffectRuntimeCatalog catalog,
            SortedList<string, PortableAttributeState> attributes)
        {
            int count = ReadCount(reader, catalog.CapacityLayout.AttributeWidth);
            if (count != attributes.Count)
                throw new InvalidDataException("Gameplay Attribute state does not match the runtime catalog.");
            int totalModifiers = 0;
            for (int i = 0; i < count; i++)
            {
                PortableAttributeState attribute = attributes.Values[i];
                PortableAttributeData.CanonicalReadInto(reader, ref attribute.Data);
                if (attribute.Revision == 0)
                    throw new InvalidDataException("Gameplay Attribute revision is invalid.");
                int modifierCount = ReadCount(reader, attribute.Modifiers.Capacity);
                totalModifiers += modifierCount;
                if (totalModifiers > catalog.CapacityLayout.ModifierWidth)
                    throw new InvalidDataException("Gameplay Attribute modifiers exceed the declared Actor capacity.");
                for (int j = 0; j < modifierCount; j++)
                {
                    PortableAttributeModifierState modifier = ReadModifier(reader, catalog.AttributeDependencies);
                    if (j > 0 && PortableAttributeModifierComparer.Compare(attribute.Modifiers[j - 1], modifier) >= 0)
                        throw new InvalidDataException("Gameplay Attribute modifiers are not canonically ordered.");
                    attribute.Modifiers.Add(modifier);
                }
            }
            reader.RequireComplete();
        }

        static void WriteActiveEffects(CanonicalWriter writer,
            ReadOnlySpan<PortableActiveEffectSnapshot> activeEffects, Float32GameplayEffectRuntimeCatalog catalog)
        {
            long prefixPosition = BeginBlock(writer, ActiveMagic);
            writer.WriteInt32(activeEffects.Length);
            for (int i = 0; i < activeEffects.Length; i++)
            {
                ref readonly PortableActiveEffectSnapshot active = ref activeEffects[i];
                WriteSpec(writer, active.Spec, catalog);
                GameplayEffectActiveStateData.CanonicalWrite(writer, in active.Data);
            }
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void ReadActiveEffects(CanonicalReader reader, Float32GameplayEffectRuntimeCatalog catalog,
            Float32GameplayEffectExecutionScratch scratch, GameplayTagSnapshotBank tagPages,
            List<PortableActiveEffectState> activeEffects)
        {
            int count = ReadCount(reader, catalog.CapacityLayout.ActiveEffectWidth);
            RequireResource(count, scratch.AvailableActiveCount);
            scratch.ActiveHandles.Clear();
            scratch.ActiveInstances.Clear();
            for (int i = 0; i < count; i++)
            {
                PortableEffectSpecState spec = ReadSpec(reader, catalog, scratch, tagPages);
                PortableActiveEffectState active = scratch.AcquireActiveState();
                active.Spec = spec;
                activeEffects.Add(active);
                GameplayEffectActiveStateData.CanonicalReadInto(reader, ref active.Data);
                if (active.Handle == 0 || active.InstanceId == 0 || active.StartTick == 0 ||
                    !scratch.ActiveHandles.Add(active.Handle) || !scratch.ActiveInstances.Add(active.InstanceId) ||
                    active.InsertionSequence == 0 || active.StackCount <= 0 ||
                    active.StackCount > spec.Definition.MaxStacks || active.LifecycleRevision == 0 ||
                    !spec.Definition.Layout.CreatesActiveEffect ||
                    i > 0 && PortableActiveEffectComparer.Compare(activeEffects[i - 1], active) >= 0)
                    throw new InvalidDataException("Active Gameplay Effect identity or canonical order is invalid.");
            }
            reader.RequireComplete();
        }

        static void WritePeriods(CanonicalWriter writer, ReadOnlySpan<KeyValuePair<ulong, ulong>> periods)
        {
            long prefixPosition = BeginBlock(writer, PeriodsMagic);
            writer.WriteInt32(periods.Length);
            for (int i = 0; i < periods.Length; i++)
            {
                writer.WriteUInt64(periods[i].Key);
                writer.WriteUInt64(periods[i].Value);
            }
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void ReadPeriods(CanonicalReader reader, GameplayEffectRuntimeCapacityLayout capacity,
            SortedList<ulong, ulong> periods)
        {
            int count = ReadCount(reader, capacity.ActiveEffectWidth);
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong instance = reader.ReadUInt64();
                ulong tick = reader.ReadUInt64();
                if (instance == 0 || tick == 0 || i > 0 && instance <= previous)
                    throw new InvalidDataException("Gameplay Effect periods are invalid or not canonically ordered.");
                previous = instance;
                periods.Add(instance, tick);
            }
            reader.RequireComplete();
        }

        static void WriteJournal(CanonicalWriter writer,
            ReadOnlySpan<KeyValuePair<ulong, List<PortablePredictionRecord>>> journal,
            ReadOnlySpan<KeyValuePair<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry>> revisions,
            Float32GameplayEffectRuntimeCatalog catalog)
        {
            long prefixPosition = BeginBlock(writer, JournalMagic);
            writer.WriteInt32(journal.Length);
            for (int index = 0; index < journal.Length; index++)
            {
                KeyValuePair<ulong, List<PortablePredictionRecord>> pair = journal[index];
                writer.WriteUInt64(pair.Key);
                writer.WriteInt32(pair.Value.Count);
                for (int i = 0; i < pair.Value.Count; i++)
                    WritePredictionRecord(writer, pair.Value[i], catalog);
            }
            writer.WriteInt32(revisions.Length);
            for (int i = 0; i < revisions.Length; i++)
                GameplayEffectLifecycleRevisionCodec.Write(writer, revisions[i].Key, revisions[i].Value);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void ReadJournal(CanonicalReader reader, Float32GameplayEffectRuntimeCatalog catalog,
            Float32GameplayEffectExecutionScratch scratch, GameplayTagSnapshotBank tagPages,
            SortedList<ulong, List<PortablePredictionRecord>> journal,
            SortedList<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> lifecycleRevisions)
        {
            GameplayEffectRuntimeCapacityLayout capacity = catalog.CapacityLayout;
            int keyCount = ReadCount(reader, capacity.Actor.PendingPredictionKeys);
            RequireResource(keyCount, scratch.JournalGroups.AvailableCount);
            ulong previous = 0;
            for (int i = 0; i < keyCount; i++)
            {
                ulong key = reader.ReadUInt64();
                if (key == 0 || i > 0 && key <= previous)
                    throw new InvalidDataException("Gameplay Effect prediction keys are invalid or not canonically ordered.");
                previous = key;
                int recordCount = ReadCount(reader, capacity.Actor.PredictionRecordsPerKey);
                RequireResource(recordCount, scratch.AvailablePredictionCount);
                List<PortablePredictionRecord> records = scratch.JournalGroups.Acquire();
                journal.Add(key, records);
                for (int j = 0; j < recordCount; j++)
                    ReadPredictionRecord(reader, catalog, scratch, tagPages, key, records);
            }
            int revisionCount = ReadCount(reader, capacity.LifecycleRevisionWidth);
            GameplayEffectLifecycleRevisionKey previousRevision = default;
            for (int i = 0; i < revisionCount; i++)
            {
                GameplayEffectLifecycleRevisionCodec.Read(reader, out GameplayEffectLifecycleRevisionKey key,
                    out GameplayEffectLifecycleRevisionEntry entry);
                if (i > 0 && key.CompareTo(previousRevision) <= 0)
                    throw new InvalidDataException("Gameplay Effect lifecycle revisions are not canonically ordered.");
                previousRevision = key;
                key.Retain();
                entry.Retain();
                lifecycleRevisions.Add(key, entry);
            }
            reader.RequireComplete();
        }

        static void WriteSpec(CanonicalWriter writer, PortableEffectSpecState spec,
            Float32GameplayEffectRuntimeCatalog catalog) =>
            PortableEffectSpecData.CanonicalWrite(writer, in spec.Data);

        static PortableEffectSpecState ReadSpec(CanonicalReader reader, Float32GameplayEffectRuntimeCatalog catalog,
            Float32GameplayEffectExecutionScratch scratch, GameplayTagSnapshotBank tagPages)
        {
            RequireResource(1, scratch.AvailableSpecCount);
            PortableEffectSpecState spec = scratch.AcquireDecodedSpec();
            try
            {
                PortableEffectSpecData.CanonicalReadInto(reader, ref spec.Data, tagPages);
                return spec;
            }
            catch
            {
                spec.Release();
                throw;
            }
        }

        static void WritePredictionRecord(CanonicalWriter writer, PortablePredictionRecord record,
            Float32GameplayEffectRuntimeCatalog catalog)
        {
            WriteSpec(writer, record.Spec, catalog);
            GameplayEffectPredictionStateData.CanonicalWrite(writer, in record.Data);
        }

        static void ReadPredictionRecord(CanonicalReader reader, Float32GameplayEffectRuntimeCatalog catalog,
            Float32GameplayEffectExecutionScratch scratch, GameplayTagSnapshotBank tagPages,
            ulong predictionKey, List<PortablePredictionRecord> records)
        {
            PortableEffectSpecState spec = ReadSpec(reader, catalog, scratch, tagPages);
            PortablePredictionRecord record = scratch.AcquirePredictionRecord();
            record.Spec = spec;
            record.Retain();
            spec.Release();
            records.Add(record);
            GameplayEffectPredictionStateData.CanonicalReadInto(reader, ref record.Data);
            for (int i = 0; i < record.CueIds.Count; i++)
                if (Array.IndexOf(spec.Definition.CueIds, record.CueIds[i]) < 0)
                    throw new InvalidDataException("Prediction Gameplay Cue is absent from its published definition.");
            for (int i = 0; i < record.Attributes.Count; i++)
                _ = catalog.AttributeDependencies.AttributeIndex(record.Attributes.Keys[i]);
            if (record.InstanceId == 0 || record.PredictedInstanceId == 0 ||
                record.CausalContext.PredictionKey != predictionKey ||
                record.HasActiveBefore && record.ActiveBeforeInsertionSequence == 0)
                throw new InvalidDataException("Gameplay Effect prediction record identity is incomplete.");
        }

        static PortableAttributeModifierState ReadModifier(CanonicalReader reader, GameplayAttributeDependencyLayout layout)
        {
            PortableAttributeModifierState modifier = PortableAttributeModifierState.CanonicalRead(reader);
            if (modifier.LiveAttributeIndex >= layout.AttributeCount)
                throw new InvalidDataException("Gameplay Attribute modifier identity is absent from the published catalog.");
            return modifier;
        }





        static void WriteTags(CanonicalWriter writer, IReadOnlyList<string> tags, Float32GameplayEffectRuntimeCatalog catalog)
        {
            writer.WriteInt32(tags.Count);
            for (int i = 0; i < tags.Count; i++)
                writer.WriteInt32(catalog.TagIndex(tags[i]));
        }

        static GameplayTagSnapshot ReadTags(CanonicalReader reader, Float32GameplayEffectRuntimeCatalog catalog,
            GameplayTagSnapshotBank pages, bool unique, int width)
        {
            int count = ReadCount(reader, width);
            if (count == 0)
                return GameplayTagSnapshot.Empty;
            RequireResource(1, pages.AvailableCount);
            GameplayTagSnapshot tags = pages.Begin(count);
            tags.RetainStateStorage();
            try
            {
                int previous = -1;
                for (int i = 0; i < count; i++)
                {
                    int index = ReadIndex(reader, catalog.ContentLayout.TagCount);
                    if (unique ? index <= previous : index < previous)
                        throw new InvalidDataException("Gameplay Tags are not canonically ordered.");
                    previous = index;
                    tags.Write(i, catalog.TagId(index));
                }
                return tags;
            }
            catch
            {
                tags.ReleaseStateStorage();
                throw;
            }
        }

        static long BeginBlock(CanonicalWriter writer, uint magic)
        {
            long position = writer.BeginLengthPrefixedBlock();
            writer.WriteUInt32(magic);
            return position;
        }

        static CanonicalReader Block(CanonicalReader reader, ArraySegment<byte> bytes, uint magic)
        {
            reader.Reset(bytes);
            if (reader.ReadUInt32() != magic)
                throw new InvalidDataException("Gameplay Effect state header is invalid.");
            return reader;
        }

        static int ReadCount(CanonicalReader reader, int capacity)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > capacity)
                throw new InvalidDataException("Gameplay Effect state count exceeds the declared capacity.");
            return count;
        }

        static int ReadIndex(CanonicalReader reader, int count)
        {
            int index = reader.ReadInt32();
            if (index < 0 || index >= count)
                throw new InvalidDataException("Gameplay Effect state identity is absent from the published catalog.");
            return index;
        }

        static void RequireResource(int required, int available)
        {
            if (required > available)
                throw new InvalidDataException("Gameplay Effect decode exceeds the prepared lease capacity.");
        }
    }
}
