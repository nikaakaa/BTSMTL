using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation.Fixed
{
    internal static class GameplayEffectStateAggregateCodec
    {
        const uint TagsMagic = 0x32544745;
        const uint AttributesMagic = 0x32544741;
        const uint ActiveMagic = 0x32464745;
        const uint PeriodsMagic = 0x32504745;
        const uint JournalMagic = 0x324A4745;
        const int StateVersion = 7;

        internal static void Write(CanonicalWriter writer, GameplayEffectStateAggregate aggregate)
        {
            FixedGameplayEffectRuntimeCatalog catalog = aggregate.Catalog;
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
            FixedGameplayEffectRuntimeCatalog catalog,
            FixedGameplayEffectExecutionScratch scratch,
            GameplayTagSnapshotBank tagPages,
            EquipmentStatePageBank equipment,
            SortedList<GameplayTagSourceIdentityValue, IReadOnlyList<string>> tagSources,
            SortedList<string, PortableAttributeState> attributes,
            List<PortableActiveEffectState> activeEffects,
            SortedList<ulong, ulong> periods,
            SortedList<ulong, List<PortablePredictionRecord>> journal,
            SortedList<GameplayEffectLifecycleRevisionKey, GameplayEffectLifecycleRevisionEntry> lifecycleRevisions)
        {
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
            FixedGameplayEffectRuntimeCatalog catalog)
        {
            long prefixPosition = BeginBlock(writer, TagsMagic);
            writer.WriteInt32(tagSources.Length);
            for (int i = 0; i < tagSources.Length; i++)
            {
                tagSources[i].Key.Write(writer);
                WriteTags(writer, tagSources[i].Value, catalog);
            }
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void ReadTags(CanonicalReader reader, FixedGameplayEffectRuntimeCatalog catalog,
            GameplayTagSnapshotBank pages, EquipmentStatePageBank equipment,
            SortedList<GameplayTagSourceIdentityValue, IReadOnlyList<string>> tagSources)
        {
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
            switch (reader.ReadByte())
            {
                case 0:
                    return GameplayTagSourceIdentityValue.Literal(reader.ReadText());
                case 1:
                    return GameplayTagSourceIdentityValue.Action(reader.ReadUInt64());
                case 2:
                    return GameplayTagSourceIdentityValue.Effect(reader.ReadUInt64());
                case 3:
                    if (equipment == null)
                        throw new InvalidDataException("Gameplay Tag Equipment source requires the published Equipment layout.");
                    ActorId actor = new ActorId(reader.ReadText());
                    EquipmentSlotId slot = new EquipmentSlotId(equipment.ReadIdentity(reader));
                    bool declared = false;
                    for (int i = 0; i < equipment.Layout.Slots.Count; i++)
                        declared |= equipment.Layout.Slots[i].SlotId == slot;
                    if (!declared)
                        throw new InvalidDataException("Gameplay Tag Equipment source Slot is absent from the published layout.");
                    return GameplayTagSourceIdentityValue.Equipment(actor, slot, reader.ReadUInt64());
                default:
                    throw new InvalidDataException("Gameplay Tag source kind is invalid.");
            }
        }

        static void WriteAttributes(CanonicalWriter writer,
            ReadOnlySpan<KeyValuePair<string, PortableAttributeSnapshot>> attributes,
            FixedGameplayEffectRuntimeCatalog catalog)
        {
            long prefixPosition = BeginBlock(writer, AttributesMagic);
            writer.WriteInt32(attributes.Length);
            for (int index = 0; index < attributes.Length; index++)
            {
                PortableAttributeSnapshot attribute = attributes[index].Value;
                writer.WriteInt32(attribute.Definition.Index);
                writer.WriteScalar(attribute.BaseValue);
                writer.WriteScalar(attribute.CurrentValue);
                writer.WriteUInt64(attribute.Revision);
                writer.WriteInt32(attribute.Modifiers.Length);
                for (int i = 0; i < attribute.Modifiers.Length; i++)
                    WriteModifier(writer, attribute.Modifiers[i]);
            }
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void ReadAttributes(CanonicalReader reader, FixedGameplayEffectRuntimeCatalog catalog,
            SortedList<string, PortableAttributeState> attributes)
        {
            int count = ReadCount(reader, catalog.CapacityLayout.AttributeWidth);
            if (count != attributes.Count)
                throw new InvalidDataException("Gameplay Attribute state does not match the runtime catalog.");
            int totalModifiers = 0;
            for (int i = 0; i < count; i++)
            {
                if (reader.ReadInt32() != i)
                    throw new InvalidDataException("Gameplay Attributes are not canonically ordered.");
                PortableAttributeState attribute = attributes.Values[i];
                attribute.BaseValue = reader.ReadScalar();
                attribute.CurrentValue = reader.ReadScalar();
                attribute.Revision = reader.ReadUInt64();
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
            ReadOnlySpan<PortableActiveEffectSnapshot> activeEffects, FixedGameplayEffectRuntimeCatalog catalog)
        {
            long prefixPosition = BeginBlock(writer, ActiveMagic);
            writer.WriteInt32(activeEffects.Length);
            for (int i = 0; i < activeEffects.Length; i++)
            {
                PortableActiveEffectSnapshot active = activeEffects[i];
                writer.WriteUInt64(active.Handle);
                writer.WriteUInt64(active.InstanceId);
                WriteSpec(writer, active.Spec, catalog);
                writer.WriteUInt64(active.StartTick);
                writer.WriteUInt64(active.EndTick);
                writer.WriteUInt64(active.InsertionSequence);
                writer.WriteInt32(active.StackCount);
                writer.WriteBoolean(active.Inhibited);
                writer.WriteUInt64(active.LifecycleRevision);
                WriteContextIdentity(writer, active.LifecycleContext);
            }
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void ReadActiveEffects(CanonicalReader reader, FixedGameplayEffectRuntimeCatalog catalog,
            FixedGameplayEffectExecutionScratch scratch, GameplayTagSnapshotBank tagPages,
            List<PortableActiveEffectState> activeEffects)
        {
            int count = ReadCount(reader, catalog.CapacityLayout.ActiveEffectWidth);
            RequireResource(count, scratch.AvailableActiveCount);
            scratch.ActiveHandles.Clear();
            scratch.ActiveInstances.Clear();
            for (int i = 0; i < count; i++)
            {
                ulong handle = reader.ReadUInt64();
                ulong instance = reader.ReadUInt64();
                PortableEffectSpecState spec = ReadSpec(reader, catalog, scratch, tagPages);
                PortableActiveEffectState active = scratch.AcquireActiveState();
                active.Handle = handle;
                active.InstanceId = instance;
                active.Spec = spec;
                activeEffects.Add(active);
                active.StartTick = reader.ReadUInt64();
                active.EndTick = reader.ReadUInt64();
                active.InsertionSequence = reader.ReadUInt64();
                active.StackCount = reader.ReadInt32();
                active.Inhibited = reader.ReadBoolean();
                active.LifecycleRevision = reader.ReadUInt64();
                active.LifecycleContext = ReadContextIdentity(reader);
                if (handle == 0 || instance == 0 || active.StartTick == 0 ||
                    !scratch.ActiveHandles.Add(handle) || !scratch.ActiveInstances.Add(instance) ||
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
            FixedGameplayEffectRuntimeCatalog catalog)
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

        static void ReadJournal(CanonicalReader reader, FixedGameplayEffectRuntimeCatalog catalog,
            FixedGameplayEffectExecutionScratch scratch, GameplayTagSnapshotBank tagPages,
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
                GameplayEffectLifecycleRevisionCodec.Read(reader, catalog.LifecycleIdentities, out GameplayEffectLifecycleRevisionKey key,
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
            FixedGameplayEffectRuntimeCatalog catalog)
        {
            writer.WriteInt32(spec.Definition.Index);
            WriteContext(writer, spec.Context);
            WriteScalarMap(writer, spec.SetByCaller, catalog, false);
            WriteScalarMap(writer, spec.SourceAttributes, catalog, true);
            WriteScalarMap(writer, spec.TargetAttributes, catalog, true);
            writer.WriteBoolean(spec.SourceTagSnapshotProvided);
            WriteTags(writer, spec.SourceTags, catalog);
            WriteTags(writer, spec.TargetTags, catalog);
            writer.WriteUInt64(spec.DurationTicks);
            writer.WriteUInt64(spec.PeriodTicks);
        }

        static PortableEffectSpecState ReadSpec(CanonicalReader reader, FixedGameplayEffectRuntimeCatalog catalog,
            FixedGameplayEffectExecutionScratch scratch, GameplayTagSnapshotBank tagPages)
        {
            int definitionIndex = ReadIndex(reader, catalog.ContentLayout.Definitions.Count);
            PortableEffectDefinition definition = catalog.EffectAt(definitionIndex);
            SimulationGameplayEffectContext context = ReadContext(reader);
            RequireResource(1, scratch.AvailableSpecCount);
            PortableEffectSpecState spec = scratch.AcquireDecodedSpec(definition, context);
            try
            {
                ReadScalarMap(reader, spec.SetByCaller, definition.SetByCallerParameters, catalog, false);
                ReadScalarMap(reader, spec.SourceAttributes, definition.SourceSnapshotAttributes, catalog, true);
                ReadScalarMap(reader, spec.TargetAttributes, definition.TargetSnapshotAttributes, catalog, true);
                spec.SourceTagSnapshotProvided = reader.ReadBoolean();
                GameplayTagSnapshot source = ReadTags(reader, catalog, tagPages, !spec.SourceTagSnapshotProvided,
                    catalog.CapacityLayout.Actor.SourceTagSnapshotEntries);
                spec.SetSourceTags(source);
                source.Release();
                GameplayTagSnapshot target = ReadTags(reader, catalog, tagPages, true,
                    catalog.CapacityLayout.Actor.SourceTagSnapshotEntries);
                spec.SetTargetTags(target);
                target.Release();
                spec.DurationTicks = reader.ReadUInt64();
                spec.PeriodTicks = reader.ReadUInt64();
                if ((definition.DurationPolicy == PortableEffectDurationPolicy.Duration) != (spec.DurationTicks > 0) ||
                    definition.HasPeriod != (spec.PeriodTicks > 0))
                    throw new InvalidDataException("Gameplay Effect Spec timing does not match its published declaration.");
                return spec;
            }
            catch
            {
                spec.Release();
                throw;
            }
        }

        static void WritePredictionRecord(CanonicalWriter writer, PortablePredictionRecord record,
            FixedGameplayEffectRuntimeCatalog catalog)
        {
            WriteSpec(writer, record.Spec, catalog);
            writer.WriteUInt64(record.Handle);
            writer.WriteUInt64(record.InstanceId);
            WriteContextIdentity(writer, record.CausalContext);
            writer.WriteBoolean(record.CreatedActive);
            writer.WriteBoolean(record.HasActiveBefore);
            writer.WriteByte((byte)record.MutationKind);
            writer.WriteUInt64(record.ActiveBeforeInsertionSequence);
            if (record.HasActiveBefore)
                WriteActiveSnapshot(writer, record.ActiveBefore);
            writer.WriteBoolean(record.Confirmed);
            writer.WriteUInt64(record.PredictedInstanceId);
            writer.WriteUInt64(record.AfterLifecycleRevision);
            writer.WriteUInt64(record.AfterChangeCursor);
            writer.WriteUInt64(record.AuthoritativeLifecycleRevision);
            writer.WriteInt32(record.CueIds.Count);
            string[] cues = record.Spec.Definition.CueIds;
            for (int index = 0; index < cues.Length; index++)
                if (record.CueIds.Contains(cues[index]))
                    writer.WriteInt32(index);
            writer.WriteInt32(record.Attributes.Count);
            for (int i = 0; i < record.Attributes.Count; i++)
            {
                PortablePredictionAttributeSnapshot value = record.Attributes.Values[i];
                writer.WriteInt32(catalog.AttributeDependencies.AttributeIndex(record.Attributes.Keys[i]));
                writer.WriteScalar(value.BaseValue);
                writer.WriteScalar(value.CurrentValue);
                writer.WriteUInt64(value.BeforeRevision);
                writer.WriteUInt64(value.AfterRevision);
            }
        }

        static void ReadPredictionRecord(CanonicalReader reader, FixedGameplayEffectRuntimeCatalog catalog,
            FixedGameplayEffectExecutionScratch scratch, GameplayTagSnapshotBank tagPages,
            ulong predictionKey, List<PortablePredictionRecord> records)
        {
            PortableEffectSpecState spec = ReadSpec(reader, catalog, scratch, tagPages);
            PortablePredictionRecord record = scratch.AcquirePredictionRecord();
            record.Spec = spec;
            record.Retain();
            spec.Release();
            records.Add(record);
            record.Handle = reader.ReadUInt64();
            record.InstanceId = reader.ReadUInt64();
            record.CausalContext = ReadContextIdentity(reader);
            record.CreatedActive = reader.ReadBoolean();
            record.HasActiveBefore = reader.ReadBoolean();
            record.MutationKind = (GameplayEffectLifecycleKind)reader.ReadByte();
            record.ActiveBeforeInsertionSequence = reader.ReadUInt64();
            if (record.HasActiveBefore)
                record.ActiveBefore = ReadActiveSnapshot(reader);
            record.Confirmed = reader.ReadBoolean();
            record.PredictedInstanceId = reader.ReadUInt64();
            record.AfterLifecycleRevision = reader.ReadUInt64();
            record.AfterChangeCursor = reader.ReadUInt64();
            record.AuthoritativeLifecycleRevision = reader.ReadUInt64();
            string[] cues = spec.Definition.CueIds;
            int cueCount = ReadCount(reader, cues.Length);
            int previousCue = -1;
            for (int i = 0; i < cueCount; i++)
            {
                int cueIndex = ReadIndex(reader, cues.Length);
                if (cueIndex <= previousCue)
                    throw new InvalidDataException("Prediction Gameplay Cues are not canonically ordered.");
                previousCue = cueIndex;
                record.CueIds.Add(cues[cueIndex]);
            }
            int attributeCount = ReadCount(reader, catalog.CapacityLayout.AttributeWidth);
            int previousAttribute = -1;
            for (int i = 0; i < attributeCount; i++)
            {
                int index = ReadIndex(reader, catalog.CapacityLayout.AttributeWidth);
                if (index <= previousAttribute)
                    throw new InvalidDataException("Prediction Attributes are not canonically ordered.");
                previousAttribute = index;
                string id = catalog.AttributeDependencies.AttributeId(index);
                record.Attributes.Add(id, new PortablePredictionAttributeSnapshot(id,
                    reader.ReadScalar(), reader.ReadScalar(), reader.ReadUInt64(), reader.ReadUInt64()));
            }
            if (record.InstanceId == 0 || record.PredictedInstanceId == 0 ||
                record.CausalContext.PredictionKey != predictionKey ||
                record.HasActiveBefore && record.ActiveBeforeInsertionSequence == 0)
                throw new InvalidDataException("Gameplay Effect prediction record identity is incomplete.");
        }

        static void WriteActiveSnapshot(CanonicalWriter writer, GameplayEffectActiveControlSnapshot snapshot)
        {
            writer.WriteUInt64(snapshot.InstanceId);
            writer.WriteUInt64(snapshot.StartTick);
            writer.WriteUInt64(snapshot.EndTick);
            writer.WriteUInt64(snapshot.NextPeriodTick);
            writer.WriteInt32(snapshot.StackCount);
            writer.WriteBoolean(snapshot.Inhibited);
            writer.WriteUInt64(snapshot.LifecycleRevision);
            WriteContextIdentity(writer, snapshot.LifecycleContext);
        }

        static GameplayEffectActiveControlSnapshot ReadActiveSnapshot(CanonicalReader reader) =>
            new GameplayEffectActiveControlSnapshot(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(),
                reader.ReadUInt64(), reader.ReadInt32(), reader.ReadBoolean(), reader.ReadUInt64(),
                ReadContextIdentity(reader));

        static void WriteContext(CanonicalWriter writer, SimulationGameplayEffectContext context)
        {
            writer.WriteText(context.SourceActorId.Value);
            writer.WriteText(context.TargetActorId.Value);
            writer.WriteUInt64(context.SourceActionInstanceId);
            writer.WriteUInt64(context.PredictionKey);
            writer.WriteUInt64(context.GameplayResultId);
            writer.WriteUInt64(context.SourceTick);
            writer.WriteByte((byte)context.ApplicationMode);
        }

        static SimulationGameplayEffectContext ReadContext(CanonicalReader reader) =>
            new SimulationGameplayEffectContext(new ActorId(reader.ReadText()), new ActorId(reader.ReadText()),
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(),
                (SimulationGameplayEffectApplicationMode)reader.ReadByte());

        static void WriteContextIdentity(CanonicalWriter writer, GameplayEffectContextIdentity context)
        {
            writer.WriteText(context.SourceActor.Value);
            writer.WriteText(context.TargetActor.Value);
            writer.WriteUInt64(context.SourceActionInstanceId);
            writer.WriteUInt64(context.PredictionKey);
            writer.WriteUInt64(context.GameplayResultId);
            writer.WriteUInt64(context.SourceTick);
            writer.WriteBoolean(context.Predicted);
        }

        static GameplayEffectContextIdentity ReadContextIdentity(CanonicalReader reader) =>
            new GameplayEffectContextIdentity(new ActorId(reader.ReadText()), new ActorId(reader.ReadText()),
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadBoolean());

        static void WriteModifier(CanonicalWriter writer, PortableAttributeModifierState modifier)
        {
            writer.WriteUInt64(modifier.Handle);
            writer.WriteUInt64(modifier.SourceEffectHandle);
            writer.WriteByte((byte)modifier.Operation);
            writer.WriteScalar(modifier.Magnitude);
            writer.WriteInt32(modifier.Priority);
            writer.WriteByte((byte)modifier.ClampBound);
            writer.WriteInt32(modifier.LiveAttributeIndex);
            writer.WriteScalar(modifier.LiveCoefficient);
            writer.WriteScalar(modifier.LivePostAdd);
            writer.WriteUInt64(modifier.InsertionSequence);
        }

        static PortableAttributeModifierState ReadModifier(CanonicalReader reader, GameplayAttributeDependencyLayout layout)
        {
            ulong handle = reader.ReadUInt64();
            ulong source = reader.ReadUInt64();
            PortableModifierOperation operation = (PortableModifierOperation)reader.ReadByte();
            FixedScalar magnitude = reader.ReadScalar();
            int priority = reader.ReadInt32();
            PortableClampBound clamp = (PortableClampBound)reader.ReadByte();
            int liveIndex = reader.ReadInt32();
            FixedScalar coefficient = reader.ReadScalar();
            FixedScalar postAdd = reader.ReadScalar();
            ulong insertion = reader.ReadUInt64();
            if (handle == 0 || source == 0 || insertion == 0 ||
                operation < PortableModifierOperation.Additive || operation > PortableModifierOperation.Clamp ||
                clamp < PortableClampBound.Minimum || clamp > PortableClampBound.Maximum ||
                liveIndex < -1 || liveIndex >= layout.AttributeCount)
                throw new InvalidDataException("Gameplay Attribute modifier identity is invalid.");
            string liveId = liveIndex < 0 ? string.Empty : layout.AttributeId(liveIndex);
            return new PortableAttributeModifierState(handle, source, operation, magnitude, priority, clamp,
                liveId, liveIndex, coefficient, postAdd, insertion);
        }

        static void WriteScalarMap(CanonicalWriter writer, SortedList<string, FixedScalar> values,
            FixedGameplayEffectRuntimeCatalog catalog, bool attributes)
        {
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                writer.WriteInt32(attributes ? catalog.AttributeDependencies.AttributeIndex(values.Keys[i]) : i);
                writer.WriteScalar(values.Values[i]);
            }
        }

        static void ReadScalarMap(CanonicalReader reader, SortedList<string, FixedScalar> destination,
            IReadOnlyList<string> declared, FixedGameplayEffectRuntimeCatalog catalog, bool attributes)
        {
            int count = ReadCount(reader, declared.Count);
            if (count != declared.Count)
                throw new InvalidDataException("Gameplay Effect captured values do not match the published declaration.");
            for (int i = 0; i < count; i++)
            {
                string id = declared[i];
                int expected = attributes ? catalog.AttributeDependencies.AttributeIndex(id) : i;
                if (reader.ReadInt32() != expected)
                    throw new InvalidDataException("Gameplay Effect captured values are not canonically ordered.");
                destination.Add(id, reader.ReadScalar());
            }
        }

        static void WriteTags(CanonicalWriter writer, IReadOnlyList<string> tags, FixedGameplayEffectRuntimeCatalog catalog)
        {
            writer.WriteInt32(tags.Count);
            for (int i = 0; i < tags.Count; i++)
                writer.WriteInt32(catalog.TagIndex(tags[i]));
        }

        static GameplayTagSnapshot ReadTags(CanonicalReader reader, FixedGameplayEffectRuntimeCatalog catalog,
            GameplayTagSnapshotBank pages, bool unique, int width)
        {
            int count = ReadCount(reader, width);
            if (count == 0)
                return GameplayTagSnapshot.Empty;
            RequireResource(1, pages.AvailableCount);
            GameplayTagSnapshot tags = pages.Begin(count);
            tags.Retain();
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
                tags.Release();
                throw;
            }
        }

        static long BeginBlock(CanonicalWriter writer, uint magic)
        {
            long position = writer.BeginLengthPrefixedBlock();
            writer.WriteUInt32(magic);
            writer.WriteInt32(StateVersion);
            return position;
        }

        static CanonicalReader Block(CanonicalReader reader, ArraySegment<byte> bytes, uint magic)
        {
            reader.Reset(bytes);
            if (reader.ReadUInt32() != magic || reader.ReadInt32() != StateVersion)
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

