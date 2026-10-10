using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using System.IO;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.Fixed.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation.Fixed
{
    internal static class FixedCharacterRuntimeStateCodec
    {
        [ThreadStatic] static CanonicalWriter s_StateWriter;
        const uint Magic = 0x54535243;
        const int Version = 19;
        const string HashIdentity = "fixed-character-runtime-state-hash/14";
        public const string CodecIdentity = "fixed-character-runtime-state/15";
        [ThreadStatic] static List<int> s_StateKeys;

        public static byte[] Write(FixedCharacterRuntimeState state)
        {
            CanonicalWriter writer = s_StateWriter ??= new CanonicalWriter();
            writer.Reset();
            WriteCanonical(writer, state);
            return writer.ToArray();
        }

        public static CharacterStateHash ComputeHash(FixedCharacterRuntimeState state) =>
            ComputeHash(state, s_StateWriter ??= new CanonicalWriter());

        public static byte[] Write(FixedCharacterRuntimeState state, out CharacterStateHash stateHash)
        {
            CanonicalWriter writer = s_StateWriter ??= new CanonicalWriter();
            writer.Reset();
            writer.WriteText(HashIdentity);
            int payloadOffset = (int)writer.Length;
            WriteCanonical(writer, state);
            stateHash = new CharacterStateHash(writer.ComputeHash());
            return writer.WrittenSpan.Slice(payloadOffset).ToArray();
        }

        public static CharacterStateHash ComputeHash(FixedCharacterRuntimeState state, CanonicalWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            writer.Reset();
            writer.WriteText(HashIdentity);
            WriteCanonical(writer, state);
            return new CharacterStateHash(writer.ComputeHash());
        }

        internal static void PreparePayloads(IReadOnlyList<SimulationActorState> actors)
        {
            int first = 0;
            while (first < actors.Count && !actors[first].State.CanonicalPayload.Bytes.IsEmpty)
                first++;
            if (first == actors.Count)
                return;
            using SimulationCharacterStatePayloadStorage.Page encoding =
                actors[first].State.StatePages.EncodingStorage.Acquire();
            for (int index = first; index < actors.Count; index++)
            {
                FixedCharacterRuntimeState state = actors[index].State;
                if (state.CanonicalPayload.Bytes.IsEmpty)
                    Encode(state, encoding, state.StatePages.EncodingActorIndex);
            }
        }

        internal static int HashPrefixBytes => sizeof(int) + CanonicalTextEncoding.ByteCount(HashIdentity.AsSpan());

        internal static CharacterStateHash Encode(FixedCharacterRuntimeState state,
            SimulationCharacterStatePayloadStorage.Page page, int actorIndex)
        {
            CanonicalWriter writer = page.Writer;
            int hashStart = checked((int)writer.Position);
            writer.SetTextCapture(null);
            writer.WriteText(HashIdentity);
            int start = checked((int)writer.Position);
            CanonicalTextReferences text = page.Text[actorIndex];
            writer.SetTextCapture(text);
            WriteCanonical(writer, state);
            int end = checked((int)writer.Position);
            var hash = new CharacterStateHash(SimulationCanonicalPayloadHash.Compute(
                writer.WrittenSpan.Slice(hashStart, end - hashStart)));
            state.SetCanonicalPayload(new SimulationCharacterStatePayload(page,
                writer.WrittenSegment.Array.AsMemory(start, end - start), hash, text));
            return hash;
        }

        internal static FixedCharacterRuntimeState Read(
            ReadOnlyMemory<byte> bytes, SimulationActorBinding actor,
            CanonicalReader reader, CanonicalWriter writer, ICanonicalTextResolver resolver,
            SimulationCharacterStatePayloadStorage.Page encoding, out CharacterStateHash stateHash)
        {
            if (bytes.IsEmpty)
                throw new ArgumentException("Character runtime state payload is empty.", nameof(bytes));
            FixedCharacterRuntimeStatePage statePage = actor.RuntimeStatePages.Begin();
            try
            {
            FixedGameplayAbilityExecutionInstallationSet installations = actor.AbilityInstallations;
            GameplayContentHash expectedGameplayContentHash = new GameplayContentHash(actor.GameplayContentHash);
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
            {
                throw new InvalidDataException("Fixed Character runtime state header is invalid.");
            }
            reader.RequireText(CodecIdentity);
            SimulationNumericProfile numericProfile = SimulationNumericProfileCodec.Read(reader, FixedSimulationNumericProfile.Value);
            GameplayContentHash gameplayContentHash = new GameplayContentHash(reader.ReadHash());
            if (!gameplayContentHash.Equals(expectedGameplayContentHash))
                throw new InvalidDataException("Fixed Character runtime state GameplayContentHash does not match the active Character Runtime.");
            StableHash stateSchemaHash = reader.ReadHash();
            if (!stateSchemaHash.Equals(actor.StateSchemaHash))
                throw new InvalidDataException("Fixed Character runtime state StateSchemaHash does not match the active Character Runtime.");
            ulong lastCompletedTick = reader.ReadUInt64();
            int abilityCount = ReadCount(reader, installations.Installations.Count, "Fixed Character Ability partition");
            if (abilityCount != installations.Installations.Count)
                throw new InvalidDataException("Fixed Character runtime state Ability partitions do not match the installed Ability set.");
            FixedAbilityStateDirectoryReference abilityDirectory = actor.AbilityDirectories.Begin();
            FixedAbilityRuntimeState[] abilities = abilityDirectory.Page.Values;
            MotionWarpStatePageReference<FixedMotionWarpState> motionWarpStates = actor.MotionWarpPages.BeginEmpty();
            for (int i = 0; i < abilityCount; i++)
            {
                GameplayAbilityExecutionIdentity identity = ReadIdentity(reader, actor);
                FixedGameplayAbilityExecutionInstallation installation = installations.Require(identity.AbilityId);
                AbilityStateValuePageReference<AbilityStateValue> stateValues = ReadValuePages(
                    reader, installation.Layout, actor.StateValuePages[identity.AbilityId], actor.DeclaredIdentityReader);
                GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState = ReadAbilityExecutionState(reader, installation.Layout, actor.ExecutionStatePages[identity.AbilityId], actor);
                ReadMotionWarpStates(reader, installation.Layout, motionWarpStates, identity.AbilityId, actor);
                abilities[i] = FixedAbilityRuntimeState.Adopt(
                    stateValues,
                    abilityExecutionState);
            }
            EquipmentProgramLayout equipmentLayout = actor.EquipmentState.Layout;
            FixedActionStatePageReference actions = actor.ActionStatePages.Begin();
            ReadActionActivationRequests(reader, actor, equipmentLayout, actions.Page);
            ReadActionInstances(reader, actor, equipmentLayout, actions.Page);
            FixedInputRequestPageReference inputRequests = ReadInputRequests(reader, actor);
            ulong eventSequence = reader.ReadUInt64();
            ulong actionEventSequence = reader.ReadUInt64();
            ulong handleAllocator = reader.ReadUInt64();
            CharacterControlRuntimeState controlState = reader.ReadBoolean()
                ? CharacterControlRuntimeStateCodec.Read(reader.ReadBytesSegment(), actor.CheckpointControlSchema,
                    statePage.ControlStorage, actor.CheckpointControlReader, writer, reader.TextResolver)
                : default;
            GameplayEffectStateAggregate gameplayEffectState = default;
            if (reader.ReadBoolean())
            {
                if (actor.AbilityInstallations.GameplayEffectCatalog == null)
                    throw new InvalidDataException("Fixed Character runtime state contains Gameplay Effect state without a Character Effect service.");
                gameplayEffectState = actor.GameplayEffectState.Decode(reader.ReadBytesSegment(), actor.EquipmentState.Storage, reader.TextResolver);
            }
            EquipmentStateAggregate equipmentState = null;
            if (reader.ReadBoolean())
            {
                if (equipmentLayout == null)
                    throw new InvalidDataException("Fixed Character runtime state contains Equipment state without an installed layout.");
                equipmentState = EquipmentStateAggregateCodec.Read(reader.ReadBytesSegment(), actor.EquipmentState.Storage, reader.TextResolver);
            }
            int timelineSnapshotCount = ReadCount(reader, FixedTimelineSnapshotPageBank.MaximumSnapshotCount, "Fixed Character Timeline snapshot");
            FixedTimelineSnapshotPageReference timelineSnapshots = actor.TimelineSnapshotPages.BeginDecode();
            for (int i = 0; i < timelineSnapshotCount; i++)
                timelineSnapshots.Page.Values.Add(ReadTimelineSnapshot(reader, timelineSnapshots.Page.DecodeStorage, actor));
            reader.RequireComplete();
            var state = FixedCharacterRuntimeState.AdoptPrepared(
                statePage,
                numericProfile,
                gameplayContentHash,
                stateSchemaHash,
                lastCompletedTick,
                abilityDirectory,
                actions,
                inputRequests,
                eventSequence,
                actionEventSequence,
                handleAllocator,
                controlState,
                gameplayEffectState,
                equipmentState,
                timelineSnapshots,
                motionWarpStates);
            if (encoding != null)
            {
                stateHash = Encode(state, encoding, actor.RuntimeStatePages.EncodingActorIndex);
                if (!state.CanonicalPayload.Bytes.Span.SequenceEqual(bytes.Span))
                    throw new InvalidDataException("Fixed Character runtime state is not canonical.");
            }
            else
            {
                writer.SetTextCapture(null);
                writer.Reset();
                writer.WriteText(HashIdentity);
                int payloadStart = checked((int)writer.Position);
                WriteCanonical(writer, state);
                stateHash = new CharacterStateHash(writer.ComputeHash());
                if (!writer.WrittenSpan.Slice(payloadStart).SequenceEqual(bytes.Span))
                    throw new InvalidDataException("Fixed Character runtime state is not canonical.");
            }
            return state;
            }
            catch
            {
                statePage.ClearCanonicalPayload();
                foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in actor.ExecutionStatePages.Values)
                    bank.ReleaseCurrent();
                statePage.ControlStorage.Clear();
                actor.GameplayEffectState.Dispose();
                actor.EquipmentState.Storage?.ReleaseDecodedState();
                throw;
            }
        }

        internal static int MaximumTextValues(SimulationActorBinding actor, CharacterControlStateSchema controlSchema,
            int timelineTextValues)
        {
            int maximum = checked(2 + timelineTextValues + 30 * actor.ActionStatePages.ActionCapacity +
                2 * actor.InputRequestPages.RequestCapacity + 2 + controlSchema.FieldCount);
            for (int field = 0; field < controlSchema.FieldCount; field++)
                if (controlSchema.Fields[field].ValueKind == CharacterControlStateValueKind.Identity)
                    maximum = checked(maximum + 1);
            for (int ability = 0; ability < actor.AbilityInstallations.Installations.Count; ability++)
            {
                FixedGameplayAbilityExecutionInstallation installation = actor.AbilityInstallations.Installations[ability];
                GameplayAbilityExecutionLayout layout = installation.Layout;
                int textSlots = 0;
                for (int slot = 0; slot < layout.StateSlots.Count; slot++)
                    if (layout.StateSlots[slot].ValueKind == ProgramStateValueKind.Identity ||
                        layout.StateSlots[slot].ValueKind == ProgramStateValueKind.ActionTargetSnapshot ||
                        layout.StateSlots[slot].ValueKind == ProgramStateValueKind.BlackboardWriteStamp)
                        textSlots++;
                int frames = actor.ExecutionStatePages[installation.Identity.AbilityId].FrameCapacity;
                maximum = checked(maximum + 3 + textSlots * (frames + 1) + frames +
                    3 * layout.MotionWarpOperationIds.Count);
            }
            FixedGameplayEffectRuntimeCatalog catalog = actor.AbilityInstallations.GameplayEffectCatalog;
            if (catalog != null)
            {
                GameplayEffectRuntimeCapacityLayout layout = catalog.CapacityLayout;
                maximum = checked(maximum + 2 * layout.SourceWidth + 4 * layout.ActiveEffectWidth +
                    6 * layout.PredictionRecordWidth + 12 * layout.LifecycleRevisionWidth);
            }
            if (actor.AbilityInstallations.RequiresEquipment)
            {
                EquipmentProgramLayout equipment = actor.EquipmentState.Layout;
                maximum = checked(maximum + 6 * equipment.Slots.Count + 3 * equipment.LocalStates.Count + 6);
            }
            return maximum;
        }

        static void WriteCanonical(CanonicalWriter writer, FixedCharacterRuntimeState state)
        {
            state.MotionWarpStates.RequireReadable();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteText(CodecIdentity);
            SimulationNumericProfileCodec.Write(writer, state.NumericProfile);
            writer.WriteHash(state.GameplayContentHash.Value);
            writer.WriteHash(state.StateSchemaHash);
            writer.WriteUInt64(state.LastCompletedTick);
            writer.WriteInt32(state.Abilities.Count);
            for (int i = 0; i < state.Abilities.Count; i++)
            {
                FixedAbilityRuntimeState ability = state.Abilities[i];
                WriteIdentity(writer, ability.AbilityIdentity);
                WriteValues(writer, ability.StateValues);
                WriteAbilityExecutionState(writer, ability.AbilityExecutionState);
                WriteMotionWarpStates(writer, state.MotionWarpStates, ability.AbilityIdentity.AbilityId);
            }
            WriteActionActivationRequests(writer, state.ActionActivationRequests);
            WriteActionInstances(writer, state.ActionInstances);
            WriteInputRequests(writer, state.InputRequests);
            writer.WriteUInt64(state.EventSequence);
            writer.WriteUInt64(state.ActionEventSequence);
            writer.WriteUInt64(state.HandleAllocator);
            writer.WriteBoolean(state.ControlState.IsValid);
            if (state.ControlState.IsValid)
                CharacterControlRuntimeStateCodec.WriteLengthPrefixed(writer, state.ControlState);
            writer.WriteBoolean(state.GameplayEffectState.IsValid);
            if (state.GameplayEffectState.IsValid)
            {
                long effectPrefixPosition = writer.BeginLengthPrefixedBlock();
                GameplayEffectStateAggregateCodec.Write(
                    writer,
                    state.GameplayEffectState);
                writer.EndLengthPrefixedBlock(effectPrefixPosition);
            }
            writer.WriteBoolean(state.EquipmentState != null);
            if (state.EquipmentState != null)
            {
                long equipmentPrefixPosition = writer.BeginLengthPrefixedBlock();
                EquipmentStateAggregateCodec.Write(writer, state.EquipmentState);
                writer.EndLengthPrefixedBlock(equipmentPrefixPosition);
            }
            IReadOnlyList<AbilityTimelineRuntimeSnapshot> timelineSnapshots = state.TimelineSnapshots;
            writer.WriteInt32(timelineSnapshots.Count);
            for (int i = 0; i < timelineSnapshots.Count; i++)
                WriteTimelineSnapshot(writer, timelineSnapshots[i]);
        }

        static void WriteTimelineSnapshot(CanonicalWriter writer, AbilityTimelineRuntimeSnapshot snapshot)
        {
            writer.WriteUInt64(snapshot.RuntimeHandle);
            writer.WriteUInt64(snapshot.Generation);
            writer.WriteText(snapshot.RequestId);
            writer.WriteText(snapshot.OwnerIdentity);
            writer.WriteText(snapshot.CallIdentity);
            writer.WriteUInt64(snapshot.ExecutionInstanceId);
            writer.WriteByte((byte)snapshot.PlaybackMode);
            writer.WriteText(snapshot.ContentRevision);
            writer.WriteByte((byte)snapshot.State);
            writer.WriteInt64(snapshot.CursorTime.Raw);
            writer.WriteInt32(snapshot.Cycle);
            writer.WriteInt32(snapshot.TimeCarry);
            writer.WriteInt64(snapshot.Control.Rate.Raw);
            writer.WriteBoolean(snapshot.Control.Paused);
            writer.WriteInt32(snapshot.TreeDecisionExits.Count);
            for (int i = 0; i < snapshot.TreeDecisionExits.Count; i++)
            {
                writer.WriteText(snapshot.TreeDecisionExits[i].ClipAuthoringId);
                writer.WriteInt32(snapshot.TreeDecisionExits[i].Cycle);
            }
            writer.WriteInt32(snapshot.PendingTreeDecisionExits.Count);
            for (int i = 0; i < snapshot.PendingTreeDecisionExits.Count; i++)
            {
                writer.WriteText(snapshot.PendingTreeDecisionExits[i].ClipAuthoringId);
                writer.WriteInt32(snapshot.PendingTreeDecisionExits[i].Cycle);
            }
            writer.WriteText(snapshot.SectionId);
            writer.WriteInt32(snapshot.ActiveClipIds.Count);
            for (int i = 0; i < snapshot.ActiveClipIds.Count; i++)
                writer.WriteText(snapshot.ActiveClipIds[i]);
            writer.WriteInt32(snapshot.ActiveTreeClips.Count);
            for (int i = 0; i < snapshot.ActiveTreeClips.Count; i++)
            {
                AbilityTimelineTreeClipState clip = snapshot.ActiveTreeClips[i];
                writer.WriteText(clip.ClipAuthoringId);
                writer.WriteText(clip.TreeGraphId);
                writer.WriteText(clip.TreeGraphRevision);
                writer.WriteText(clip.NodeAuthoringId);
                writer.WriteUInt64(clip.PlaybackGeneration);
                writer.WriteUInt64(clip.BranchRevision);
                writer.WriteInt32(clip.Cycle);
            }
            writer.WriteBoolean(snapshot.HasStopContext);
            writer.WriteByte((byte)snapshot.StopCause);
            writer.WriteUInt64(snapshot.StopLocalLogicTick);
            writer.WriteBoolean(snapshot.InitialBoundaryPending);
            writer.WriteText(snapshot.TimelineId);
            writer.WriteText(snapshot.ActionContext.ActionId);
            writer.WriteText(snapshot.ActionContext.ContextId);
            writer.WriteUInt64(snapshot.ActionContext.InstanceId);
            writer.WriteUInt64(snapshot.ActionContext.PredictionKey);
            writer.WriteText(snapshot.ActionContext.TargetActorId);
            writer.WriteText(snapshot.ActionContext.SkillId.IsValid ? snapshot.ActionContext.SkillId.Value : string.Empty);
            writer.WriteInt32(snapshot.ActionContext.SkillEntryOperation.IsValid ? snapshot.ActionContext.SkillEntryOperation.Value : -1);
            writer.WriteUInt64(snapshot.ActionContext.SkillExecutionGeneration);
            writer.WriteInt32(snapshot.InvocationSource.OperationIndex);
            writer.WriteText(snapshot.InvocationSource.GraphAuthoringId);
            writer.WriteText(snapshot.InvocationSource.NodeAuthoringId);
            writer.WriteText(snapshot.InvocationSource.GraphInvocationPath);
            writer.WriteText(snapshot.InvocationSource.OperationExecutionPath);
            writer.WriteUInt64(snapshot.InvocationSource.InvocationGeneration);
            writer.WriteUInt64(snapshot.InputSequence);
            writer.WriteUInt64(snapshot.StartTick.Value);
            writer.WriteInt64(snapshot.StartTime.Raw);
        }

        static AbilityTimelineRuntimeSnapshot ReadTimelineSnapshot(CanonicalReader reader, AbilityTimelineSnapshotStorage storage, SimulationActorBinding actor)
        {
            ulong runtimeHandle = reader.ReadUInt64();
            ulong generation = reader.ReadUInt64();
            CanonicalTextValue requestId = reader.ReadText();
            CanonicalTextValue ownerIdentity = reader.ReadText();
            CanonicalTextValue callIdentity = reader.ReadText();
            ulong executionInstanceId = reader.ReadUInt64();
            AbilityTimelineSnapshotMode playbackMode = ReadTimelineSnapshotMode(reader.ReadByte());
            CanonicalTextValue contentRevision = reader.ReadText();
            AbilityTimelineSnapshotState state = ReadTimelineSnapshotState(reader.ReadByte());
            FixedScalar cursorTime = FixedScalar.FromRaw(reader.ReadInt64());
            int cycle = reader.ReadInt32();
            int timeCarry = reader.ReadInt32();
            var control = new AbilityTimelinePlaybackControl(ThirdPersonSimulation.Fixed.FixedScalar.FromRaw(reader.ReadInt64()), reader.ReadBoolean());
            int treeDecisionExitCount = ReadCount(reader, 1024, "timeline tree decision exits");
            Span<AbilityTimelineTreeClipExit> treeDecisionExits = storage.TreeDecisionExits.Reserve(
                treeDecisionExitCount, out TimelineSnapshotItems<AbilityTimelineTreeClipExit> treeDecisionExitItems);
            for (int i = 0; i < treeDecisionExitCount; i++)
                treeDecisionExits[i] = new AbilityTimelineTreeClipExit(reader.ReadText(), reader.ReadInt32());
            int pendingTreeDecisionExitCount = ReadCount(reader, 1024 * (AbilityTimelineRuntimeLimits.MaximumCycleAdvance + 1), "Fixed Character Timeline pending tree decision exits");
            Span<AbilityTimelineTreeClipExit> pendingTreeDecisionExits = storage.PendingTreeDecisionExits.Reserve(
                pendingTreeDecisionExitCount, out TimelineSnapshotItems<AbilityTimelineTreeClipExit> pendingTreeDecisionExitItems);
            for (int i = 0; i < pendingTreeDecisionExitCount; i++)
                pendingTreeDecisionExits[i] = new AbilityTimelineTreeClipExit(reader.ReadText(), reader.ReadInt32());
            CanonicalTextValue sectionId = reader.ReadText();
            int clipCount = ReadCount(reader, 1024, "Fixed Character Timeline active clips");
            Span<CanonicalTextValue> clips = storage.ActiveClipIds.Reserve(clipCount, out TimelineSnapshotItems<CanonicalTextValue> activeClipItems);
            for (int i = 0; i < clipCount; i++)
                clips[i] = reader.ReadText();
            int treeClipCount = ReadCount(reader, reader.Remaining / sizeof(int), "Timeline active TreeClip invocations");
            Span<AbilityTimelineTreeClipState> treeClips = storage.ActiveTreeClips.Reserve(
                treeClipCount, out TimelineSnapshotItems<AbilityTimelineTreeClipState> treeClipItems);
            for (int i = 0; i < treeClipCount; i++)
                treeClips[i] = new AbilityTimelineTreeClipState(
                    reader.ReadText(),
                    reader.ReadText(),
                    reader.ReadText(),
                    reader.ReadText(),
                    reader.ReadUInt64(),
                    reader.ReadUInt64(),
                    reader.ReadInt32());
            bool hasStopContext = reader.ReadBoolean();
            AbilityTimelineSnapshotStopCause stopCause = ReadTimelineSnapshotStopCause(reader.ReadByte());
            ulong stopLocalLogicTick = reader.ReadUInt64();
            bool initialBoundaryPending = reader.ReadBoolean();
            CanonicalTextValue timelineId = reader.ReadText();
            string actionId = actor.DeclaredIdentityReader(reader);
            CanonicalTextValue contextId = reader.ReadText();
            ulong actionInstanceId = reader.ReadUInt64();
            ulong predictionKey = reader.ReadUInt64();
            CanonicalTextValue targetActorId = reader.ReadText();
            string skillIdValue = actor.DeclaredIdentityReader(reader);
            int skillEntryOperationValue = reader.ReadInt32();
            ulong skillExecutionGeneration = reader.ReadUInt64();
            int invocationOperationIndex = reader.ReadInt32();
            CanonicalTextValue invocationGraphAuthoringId = reader.ReadText();
            CanonicalTextValue invocationNodeAuthoringId = reader.ReadText();
            CanonicalTextValue invocationPath = reader.ReadText();
            CanonicalTextValue operationExecutionPath = reader.ReadText();
            ulong invocationGeneration = reader.ReadUInt64();
            ulong inputSequence = reader.ReadUInt64();
            ulong startTickValue = reader.ReadUInt64();
            FixedScalar startTime = FixedScalar.FromRaw(reader.ReadInt64());
            if (runtimeHandle == 0 || generation == 0 || executionInstanceId == 0 || startTickValue == 0)
                throw new InvalidDataException("Fixed Character runtime state Timeline snapshot identity is invalid.");
            CharacterSkillId skillId = skillIdValue.Length == 0 ? default : new CharacterSkillId(skillIdValue);
            OperationHandle skillEntryOperation = skillEntryOperationValue < 0 ? OperationHandle.Invalid : new OperationHandle(skillEntryOperationValue);
            var actionContext = new TimelineActionContextIdentity(
                actionId,
                contextId,
                actionInstanceId,
                predictionKey,
                targetActorId,
                skillId,
                skillEntryOperation,
                skillExecutionGeneration);
            var invocationSource = new AbilityTimelineInvocationSource(
                invocationOperationIndex,
                invocationGraphAuthoringId,
                invocationNodeAuthoringId,
                invocationPath,
                operationExecutionPath,
                invocationGeneration);
            return new AbilityTimelineRuntimeSnapshot(
                runtimeHandle,
                generation,
                requestId,
                ownerIdentity,
                callIdentity,
                executionInstanceId,
                playbackMode,
                contentRevision,
                state,
                cursorTime,
                cycle,
                timeCarry,
                control,
                treeDecisionExitItems,
                pendingTreeDecisionExitItems,
                sectionId,
                activeClipItems,
                treeClipItems,
                hasStopContext,
                stopCause,
                stopLocalLogicTick,
                initialBoundaryPending,
                timelineId,
                actionContext,
                invocationSource,
                inputSequence,
                new SimulationTick(startTickValue),
                startTime);
        }

        static void WriteIdentity(CanonicalWriter writer, GameplayAbilityExecutionIdentity identity)
        {
            writer.WriteText(identity.AbilityId.Value);
            writer.WriteHash(identity.ContentHash);
            writer.WriteHash(identity.StateSchemaHash);
            writer.WriteText(identity.OperationSetVersion.Value);
            SimulationNumericProfileCodec.Write(writer, identity.NumericProfile);
        }

        static GameplayAbilityExecutionIdentity ReadIdentity(CanonicalReader reader, SimulationActorBinding actor)
        {
            CharacterSkillId skill = new CharacterSkillId(actor.DeclaredIdentityReader(reader));
            GameplayAbilityExecutionIdentity expected = actor.AbilityInstallations.Require(skill).Identity;
            StableHash content = reader.ReadHash();
            StableHash schema = reader.ReadHash();
            reader.RequireText(expected.OperationSetVersion.Value);
            SimulationNumericProfileCodec.Read(reader, expected.NumericProfile);
            if (!content.Equals(expected.ContentHash) || !schema.Equals(expected.StateSchemaHash))
                throw new InvalidDataException("Ability partition identity does not match its installed declaration.");
            return expected;
        }

        static void WriteValues(CanonicalWriter writer, Dictionary<int, AbilityStateValue> values)
        {
            List<int> keys = s_StateKeys ??= new List<int>();
            keys.Clear();
            foreach (KeyValuePair<int, AbilityStateValue> value in values)
                keys.Add(value.Key);
            keys.Sort();
            writer.WriteInt32(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                writer.WriteInt32(keys[i]);
                WriteValue(writer, values[keys[i]]);
            }
        }

        static void WriteValues(CanonicalWriter writer, in AbilityStateValuePageReference<AbilityStateValue> values)
        {
            AbilityStateValuePageSet<AbilityStateValue>.Enumerator enumerator = values.GetEnumerator();
            writer.WriteInt32(values.Set.Count);
            while (enumerator.MoveNext(out int slot, out AbilityStateValue value))
            {
                writer.WriteInt32(slot);
                WriteValue(writer, value);
            }
        }

        static AbilityStateValuePageReference<AbilityStateValue> ReadValuePages(
            CanonicalReader reader,
            GameplayAbilityExecutionLayout layout,
            AbilityStateValuePageBank<AbilityStateValue> bank, Func<CanonicalReader, string> readDeclaredIdentity)
        {
            int count = ReadCount(reader, layout.StateSlots.Count, "Fixed Character state value");
            AbilityStateValuePageReference<AbilityStateValue> values = bank.BeginEmpty();
            int previous = -1;
            for (int index = 0; index < count; index++)
            {
                AbilityStateValue value = ReadStateValue(reader, layout, readDeclaredIdentity, ref previous, out int slot);
                values.Set.RestoreValue(slot, value);
            }
            return values;
        }

        static AbilityStateValue ReadStateValue(CanonicalReader reader,
            GameplayAbilityExecutionLayout layout, Func<CanonicalReader, string> readDeclaredIdentity, ref int previous, out int slotIndex)
        {
            slotIndex = reader.ReadInt32();
            if (slotIndex < 0 || slotIndex >= layout.StateSlots.Count || slotIndex <= previous)
                throw new InvalidDataException("Fixed Character state value indexes are invalid or not canonically ordered.");
            AbilityStateValue value = ReadValue(reader, readDeclaredIdentity);
            if (value.Kind != layout.StateSlots[slotIndex].ValueKind)
                throw new InvalidDataException($"Fixed Character state value slot '{slotIndex}' kind does not match the Ability layout.");
            previous = slotIndex;
            return value;
        }

        static void WriteAbilityExecutionState(
            CanonicalWriter writer,
            GameplayAbilityExecutionAggregate<AbilityStateValue> aggregate)
        {
            IReadOnlyList<GameplayAbilityExecutionFrame<AbilityStateValue>> frames = aggregate?.Frames ??
                Array.Empty<GameplayAbilityExecutionFrame<AbilityStateValue>>();
            writer.WriteInt32(frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                GameplayAbilityExecutionFrame<AbilityStateValue> frame = frames[i];
                writer.WriteText(frame.SkillId.Value);
                writer.WriteInt32(frame.EntryOperation.Value);
                writer.WriteUInt64(frame.ActionInstanceId);
                writer.WriteUInt64(frame.PredictionKey);
                writer.WriteUInt64(frame.Generation);
                if (frame.OwnsValues)
                    WriteValues(writer, frame.OwnedValues);
                else
                    WriteValues(writer, frame.StateValues);
            }
        }

        static GameplayAbilityExecutionAggregate<AbilityStateValue> ReadAbilityExecutionState(
            CanonicalReader reader,
            GameplayAbilityExecutionLayout layout,
            GameplayAbilityExecutionStateBank<AbilityStateValue> bank, SimulationActorBinding actor)
        {
            int count = ReadCount(reader, bank.FrameCapacity, "Fixed Ability execution frame");
            GameplayAbilityExecutionAggregate<AbilityStateValue> aggregate = bank.Capture();
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                var skillId = new CharacterSkillId(actor.DeclaredIdentityReader(reader));
                OperationHandle entryOperation = ReadRequiredOperation(reader, layout);
                ulong actionInstanceId = reader.ReadUInt64();
                ulong predictionKey = reader.ReadUInt64();
                ulong generation = reader.ReadUInt64();
                if (actionInstanceId == 0 || i > 0 && actionInstanceId <= previous)
                    throw new InvalidDataException("Fixed Ability execution frame identities are invalid or not canonically ordered.");
                var identity = new AbilityExecutionContext(skillId, entryOperation, actionInstanceId,
                    predictionKey, generation);
                if (!identity.IsValid)
                    throw new ArgumentException("Skill execution frame identity is incomplete.");
                GameplayAbilityExecutionFrame<AbilityStateValue> frame = aggregate.AppendRestoredFrame(identity);
                int valueCount = ReadCount(reader, layout.StateSlots.Count, "Fixed Character state value");
                int previousSlot = -1;
                for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
                {
                    AbilityStateValue value = ReadStateValue(reader, layout, actor.DeclaredIdentityReader, ref previousSlot, out int slot);
                    frame.StateValues.Set.RestoreValue(slot, value);
                }
                previous = actionInstanceId;
            }
            return aggregate;
        }

        static void WriteInputRequests(
            CanonicalWriter writer,
            ReadOnlySpan<KeyValuePair<string, SimulationInputRequestState>> requests)
        {
            writer.WriteInt32(requests.Length);
            for (int i = 0; i < requests.Length; i++)
            {
                writer.WriteText(requests[i].Key);
                SimulationInputRequestStateCodec.Write(writer, requests[i].Value);
            }
        }

        static FixedInputRequestPageReference ReadInputRequests(
            CanonicalReader reader,
            SimulationActorBinding actor)
        {
            int count = ReadCount(reader, actor.InputRequestPages.RequestCapacity, "Fixed Character Input request");
            FixedInputRequestPageReference page = actor.InputRequestPages.Begin(count);
            KeyValuePair<string, SimulationInputRequestState>[] requests = page.Page.Values;
            string previous = null;
            for (int i = 0; i < count; i++)
            {
                string requestId = SimulationIdentity.Require(actor.DeclaredIdentityReader(reader), "InputRequestId");
                if (previous != null && CanonicalTextValue.CompareOrdinal(previous, requestId) >= 0 ||
                    !ContainsInputRequest(actor, requestId))
                    throw new InvalidDataException("Fixed Character Input request identities are invalid or not canonically ordered.");
                SimulationInputRequestState value = SimulationInputRequestStateCodec.Read(reader);
                if (value.IsValid && !CanonicalTextValue.Equals(value.RequestId, requestId, StringComparison.Ordinal))
                    throw new InvalidDataException("Fixed Character Input request state key does not match its value.");
                requests[i] = new(requestId, value);
                previous = requestId;
            }
            return page;
        }

        static bool ContainsInputRequest(
            SimulationActorBinding actor,
            string requestId)
        {
            for (int i = 0; i < actor.InputRuntimeBinding.InputRequestIds.Count; i++)
                if (CanonicalTextValue.Equals(actor.InputRuntimeBinding.InputRequestIds[i], requestId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        static void WriteActionActivationRequests(
            CanonicalWriter writer,
            ReadOnlySpan<SimulationActionActivationRequestState> requests)
        {
            writer.WriteInt32(requests.Length);
            for (int i = 0; i < requests.Length; i++)
            {
                SimulationActionActivationRequestState request = requests[i];
                writer.WriteBoolean(request.IsValid);
                if (!request.IsValid)
                    continue;
                writer.WriteText(request.ActionId);
                WriteOptionalSkill(writer, request.SkillId);
                WriteOptionalOperation(writer, request.SkillEntryOperation);
                writer.WriteText(request.ContextId);
                SimulationInputRequestStateCodec.Write(writer, request.SourceInputRequest);
                writer.WriteUInt64(request.InputSequence);
                writer.WriteUInt64(request.StartTick);
                writer.WriteText(request.TargetKey);
                WriteTarget(writer, request.TargetSnapshot);
                SimulationExecutionSourceCodec.Write(writer, request.Source);
                WriteEquipmentContext(writer, request.EquipmentContext);
                WriteSourceContext(writer, request.ReplacementSource);
                writer.WriteText(request.ActivationEntryId);
                writer.WriteBoolean(request.ConsumeSourceInputRequest);
            }
        }

        static void ReadActionActivationRequests(
            CanonicalReader reader,
            SimulationActorBinding actor,
            EquipmentProgramLayout equipmentLayout,
            FixedActionStatePage page)
        {
            FixedGameplayAbilityExecutionInstallationSet installations = actor.AbilityInstallations;
            int count = ReadCount(reader, page.Bank.ActionCapacity, "Fixed Action activation request");
            SimulationActionActivationRequestState[] requests = page.Requests;
            int requestCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (!reader.ReadBoolean())
                    continue;
                var request = new SimulationActionActivationRequestState(
                    actor.ReadActionIdentity(reader),
                    ReadOptionalSkill(reader, actor),
                    ReadOptionalOperation(reader, null),
                    reader.ReadText(),
                    SimulationInputRequestStateCodec.Read(reader),
                    reader.ReadUInt64(),
                    reader.ReadUInt64(),
                    reader.ReadText(),
                    ReadTarget(reader),
                    SimulationExecutionSourceCodec.Read(reader, actor.DeclaredIdentityReader),
                    ReadEquipmentContext(reader, equipmentLayout, actor),
                    ReadSourceContext(reader, actor.DeclaredIdentityReader),
                    actor.DeclaredIdentityReader(reader),
                    reader.ReadBoolean());
                if (!request.IsValid)
                    throw new InvalidDataException("Fixed Action activation request identity is invalid.");
                RequireSkillExecution(request.SkillId, request.SkillEntryOperation, installations);
                RequireActivationEntry(request.SkillId, request.ActivationEntryId, installations);
                requests[requestCount++] = request;
            }
            page.RequestCount = requestCount;
        }

        static void WriteActionInstances(CanonicalWriter writer, ReadOnlySpan<FixedActionInstanceState> actions)
        {
            writer.WriteInt32(actions.Length);
            for (int i = 0; i < actions.Length; i++)
            {
                FixedActionInstanceState action = actions[i];
                writer.WriteBoolean(action.IsValid);
                if (!action.IsValid)
                    continue;
                writer.WriteText(action.ActionId);
                WriteOptionalSkill(writer, action.SkillId);
                WriteOptionalOperation(writer, action.SkillEntryOperation);
                writer.WriteUInt64(action.SkillExecutionGeneration);
                writer.WriteText(action.ContextId);
                writer.WriteUInt64(action.InstanceId);
                writer.WriteUInt64(action.PredictionKey);
                writer.WriteText(action.SourceInputRequestId);
                writer.WriteUInt64(action.InputSequence);
                writer.WriteUInt64(action.StartTick);
                writer.WriteText(action.TargetKey);
                WriteTarget(writer, action.TargetSnapshot);
                SimulationExecutionSourceCodec.Write(writer, action.Source);
                writer.WriteByte((byte)action.Phase);
                writer.WriteByte((byte)action.State);
                writer.WriteByte((byte)action.LastTransition);
                writer.WriteUInt64(action.LastTransitionTick);
                writer.WriteUInt64(action.LastTransitionSourceTick);
                writer.WriteText(action.Reason);
                WriteEquipmentContext(writer, action.EquipmentContext);
                writer.WriteUInt64(action.SegmentGeneration);
                writer.WriteText(action.ActivationEntryId);
            }
        }

        static void ReadActionInstances(
            CanonicalReader reader,
            SimulationActorBinding actor,
            EquipmentProgramLayout equipmentLayout,
            FixedActionStatePage page)
        {
            FixedGameplayAbilityExecutionInstallationSet installations = actor.AbilityInstallations;
            int count = ReadCount(reader, page.Bank.ActionCapacity, "Fixed Action instance");
            FixedActionInstanceState[] actions = page.Actions;
            int actionCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (!reader.ReadBoolean())
                    continue;
                var action = new FixedActionInstanceState(
                    actor.ReadActionIdentity(reader),
                    ReadOptionalSkill(reader, actor),
                    ReadOptionalOperation(reader, null),
                    reader.ReadUInt64(),
                    reader.ReadText(),
                    reader.ReadUInt64(),
                    reader.ReadUInt64(),
                    reader.ReadText(),
                    reader.ReadUInt64(),
                    reader.ReadUInt64(),
                    reader.ReadText(),
                    ReadTarget(reader),
                    SimulationExecutionSourceCodec.Read(reader, actor.DeclaredIdentityReader),
                    ReadActionPhase(reader.ReadByte()),
                    ReadActionState(reader.ReadByte()),
                    ReadActionTransition(reader.ReadByte()),
                    reader.ReadUInt64(),
                    reader.ReadUInt64(),
                    reader.ReadText(),
                    ReadEquipmentContext(reader, equipmentLayout, actor),
                    reader.ReadUInt64(),
                    actor.DeclaredIdentityReader(reader));
                if (!action.IsValid)
                    throw new InvalidDataException("Fixed Action instance identity is invalid.");
                RequireSkillExecution(action.SkillId, action.SkillEntryOperation, installations);
                RequireActivationEntry(action.SkillId, action.ActivationEntryId, installations);
                actions[actionCount++] = action;
            }
            page.ActionCount = actionCount;
        }

        static void WriteMotionWarpStates(
            CanonicalWriter writer,
            in MotionWarpStatePageReference<FixedMotionWarpState> states,
            CharacterSkillId ability)
        {
            int partition = states.Page.Layout.Partition(ability);
            int offset = states.Page.Layout.Offset(partition);
            IReadOnlyList<int> operations = states.Page.Layout.Operations(partition);
            int count = 0;
            for (int index = 0; index < operations.Count; index++)
                if (states.Page.At(offset + index).Active)
                    count++;
            writer.WriteInt32(count);
            for (int index = 0; index < operations.Count; index++)
            {
                ref readonly FixedMotionWarpState value = ref states.Page.At(offset + index);
                if (!value.Active)
                    continue;
                writer.WriteInt32(operations[index]);
                WriteMotionWarpState(writer, value);
            }
        }

        static void ReadMotionWarpStates(
            CanonicalReader reader,
            GameplayAbilityExecutionLayout layout,
            in MotionWarpStatePageReference<FixedMotionWarpState> states,
            CharacterSkillId ability, SimulationActorBinding actor)
        {
            int count = ReadCount(reader, layout.MotionWarpOperationIds.Count, "Fixed MotionWarp state");
            int partition = states.Page.Layout.Partition(ability);
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                int operation = reader.ReadInt32();
                if (operation < 0 || operation <= previous || !layout.HasMotionWarp(new OperationHandle(operation)))
                    throw new InvalidDataException("Fixed MotionWarp operation identities are invalid or not canonically ordered.");
                FixedMotionWarpState value = ReadMotionWarpState(reader, layout, actor);
                if (!value.Active)
                    throw new InvalidDataException("Fixed MotionWarp state is not active.");
                states.Page.Set(states.Page.Layout.Slot(partition, new OperationHandle(operation)), in value);
                previous = operation;
            }
        }

        static void WriteMotionWarpState(CanonicalWriter writer, FixedMotionWarpState value)
        {
            writer.WriteBoolean(value.Active);
            if (!value.Active)
                return;
            writer.WriteBoolean(value.Initialized);
            writer.WriteUInt64(value.PlaybackGeneration);
            writer.WriteInt32(value.Cycle);
            WriteActionReference(writer, value.ActionInstance);
            writer.WriteVector3(value.StartBodyPosition);
            writer.WriteYaw(value.StartBodyYaw);
            writer.WriteVector3(value.SourceWindowStartPosition);
            writer.WriteScalar(value.SourceWindowStartYaw);
            writer.WriteVector3(value.ResolvedTargetPosition);
            writer.WriteYaw(value.ResolvedTargetYaw);
            writer.WriteByte((byte)value.LimitResult);
            writer.WriteVector3(value.PreviousWarpedPosition);
            writer.WriteYaw(value.PreviousWarpedYaw);
            writer.WriteScalar(value.LastPositionProgress);
            writer.WriteScalar(value.LastYawProgress);
            WriteOptionalOperation(writer, value.SourceOperation);
            writer.WriteByte((byte)value.Decision);
            writer.WriteBoolean(value.HasTarget);
            writer.WriteVector3(value.CapturedTargetPosition);
            writer.WriteYaw(value.CapturedTargetYaw);
        }

        static FixedMotionWarpState ReadMotionWarpState(
            CanonicalReader reader,
            GameplayAbilityExecutionLayout layout, SimulationActorBinding actor)
        {
            if (!reader.ReadBoolean())
                return default;
            return new FixedMotionWarpState(
                true,
                reader.ReadBoolean(),
                reader.ReadUInt64(),
                reader.ReadInt32(),
                ReadActionReference(reader, layout, actor),
                reader.ReadVector3(),
                reader.ReadYaw(),
                reader.ReadVector3(),
                reader.ReadScalar(),
                reader.ReadVector3(),
                reader.ReadYaw(),
                ReadMotionWarpLimitResult(reader.ReadByte()),
                reader.ReadVector3(),
                reader.ReadYaw(),
                reader.ReadScalar(),
                reader.ReadScalar(),
                ReadOptionalOperation(reader, layout),
                ReadMotionWarpWindowDecision(reader.ReadByte()),
                reader.ReadBoolean(),
                reader.ReadVector3(),
                reader.ReadYaw());
        }

        static MotionWarpWindowDecision ReadMotionWarpWindowDecision(byte value) => value is >= 1 and <= 4
            ? (MotionWarpWindowDecision)value
            : throw new InvalidDataException("MotionWarp 窗口进入结果无效。");

        internal static void WriteValue(CanonicalWriter writer, AbilityStateValue value)
        {
            writer.WriteByte((byte)value.Kind);
            switch (value.Kind)
            {
                case ProgramStateValueKind.Boolean: writer.WriteBoolean(value.Boolean); break;
                case ProgramStateValueKind.Int32: writer.WriteInt32(value.Int32); break;
                case ProgramStateValueKind.UInt64: writer.WriteUInt64(value.UInt64); break;
                case ProgramStateValueKind.Scalar: writer.WriteScalar(value.Scalar); break;
                case ProgramStateValueKind.Vector2: writer.WriteVector2(value.Vector2); break;
                case ProgramStateValueKind.Vector3: writer.WriteVector3(value.Vector3); break;
                case ProgramStateValueKind.Yaw: writer.WriteYaw(value.Yaw); break;
                case ProgramStateValueKind.Identity: writer.WriteText(value.Identity); break;
                case ProgramStateValueKind.BlackboardOwnerToken: WriteBlackboardOwnerToken(writer, value.BlackboardOwnerToken); break;
                case ProgramStateValueKind.BlackboardWriteStamp: WriteBlackboardWriteStamp(writer, value.BlackboardWriteStamp); break;
                case ProgramStateValueKind.ActionTargetSnapshot: WriteTarget(writer, value.ActionTargetSnapshot); break;
                default: throw new InvalidDataException($"Unsupported Fixed Character state value kind '{value.Kind}'.");
            }
        }

        internal static AbilityStateValue ReadValue(CanonicalReader reader, Func<CanonicalReader, string> readDeclaredIdentity)
        {
            ProgramStateValueKind kind = (ProgramStateValueKind)reader.ReadByte();
            return kind switch
            {
                ProgramStateValueKind.Boolean => AbilityStateValue.FromBoolean(reader.ReadBoolean()),
                ProgramStateValueKind.Int32 => AbilityStateValue.FromInt32(reader.ReadInt32()),
                ProgramStateValueKind.UInt64 => AbilityStateValue.FromUInt64(reader.ReadUInt64()),
                ProgramStateValueKind.Scalar => AbilityStateValue.FromScalar(reader.ReadScalar()),
                ProgramStateValueKind.Vector2 => AbilityStateValue.FromVector2(reader.ReadVector2()),
                ProgramStateValueKind.Vector3 => AbilityStateValue.FromVector3(reader.ReadVector3()),
                ProgramStateValueKind.Yaw => AbilityStateValue.FromYaw(reader.ReadYaw()),
                ProgramStateValueKind.Identity => AbilityStateValue.FromIdentity(reader.ReadText()),
                ProgramStateValueKind.BlackboardOwnerToken => AbilityStateValue.FromBlackboardOwnerToken(ReadBlackboardOwnerToken(reader)),
                ProgramStateValueKind.BlackboardWriteStamp => AbilityStateValue.FromBlackboardWriteStamp(ReadBlackboardWriteStamp(reader, readDeclaredIdentity)),
                ProgramStateValueKind.ActionTargetSnapshot => AbilityStateValue.FromActionTargetSnapshot(ReadTarget(reader)),
                _ => throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(ProgramStateValueKind)}' value '{(byte)kind}' is invalid.")
            };
        }

        static void WriteBlackboardOwnerToken(CanonicalWriter writer, BlackboardOwnerToken value)
        {
            writer.WriteByte(value.IsValid ? (byte)value.ScopeKind : (byte)0);
            writer.WriteInt32(value.IsValid ? value.CompiledOwnerIndex : -1);
            writer.WriteUInt64(value.IsValid ? value.Generation : 0);
        }

        static BlackboardOwnerToken ReadBlackboardOwnerToken(CanonicalReader reader)
        {
            byte scope = reader.ReadByte();
            int owner = reader.ReadInt32();
            ulong generation = reader.ReadUInt64();
            if (scope == 0 && owner == -1 && generation == 0)
                return default;
            return new BlackboardOwnerToken(
                ReadProgramScopeKind(scope),
                owner,
                generation);
        }

        static void WriteBlackboardWriteStamp(CanonicalWriter writer, BlackboardWriteStamp value)
        {
            writer.WriteInt32(value.IsValid && value.SourceOperation.IsValid ? value.SourceOperation.Value : -1);
            writer.WriteUInt64(value.IsValid ? value.LogicTick : 0);
            WriteSourceContext(writer, value.IsValid ? value.SourceContext : default);
            writer.WriteInt32(value.IsValid && value.TimelineOperation.IsValid ? value.TimelineOperation.Value : -1);
            writer.WriteInt32(value.IsValid && value.ClipOperation.IsValid ? value.ClipOperation.Value : -1);
            writer.WriteInt32(value.IsValid ? value.Cycle : 0);
            writer.WriteUInt64(value.IsValid ? value.CommandSequence : 0);
        }

        static BlackboardWriteStamp ReadBlackboardWriteStamp(CanonicalReader reader, Func<CanonicalReader, string> readDeclaredIdentity)
        {
            int source = reader.ReadInt32();
            ulong tick = reader.ReadUInt64();
            GameplayAbilitySourceContext sourceContext = ReadSourceContext(reader, readDeclaredIdentity);
            int timeline = reader.ReadInt32();
            int clip = reader.ReadInt32();
            int cycle = reader.ReadInt32();
            ulong commandSequence = reader.ReadUInt64();
            if (source == -1 && tick == 0 && sourceContext.Equals(default) && timeline == -1 && clip == -1 && cycle == 0 && commandSequence == 0)
                return default;
            return new BlackboardWriteStamp(
                source < 0 ? OperationHandle.Invalid : new OperationHandle(source),
                tick,
                sourceContext,
                timeline < 0 ? OperationHandle.Invalid : new OperationHandle(timeline),
                clip < 0 ? OperationHandle.Invalid : new OperationHandle(clip),
                cycle,
                commandSequence);
        }

        static void WriteSourceContext(CanonicalWriter writer, in GameplayAbilitySourceContext source)
        {
            WriteOptionalSkill(writer, source.SkillId);
            writer.WriteUInt64(source.InstanceId);
            writer.WriteUInt64(source.SkillExecutionGeneration);
            writer.WriteUInt64(source.SegmentGeneration);
        }

        static GameplayAbilitySourceContext ReadSourceContext(CanonicalReader reader,
            Func<CanonicalReader, string> readDeclaredIdentity)
        {
            CharacterSkillId skill = reader.ReadBoolean() ? new CharacterSkillId(readDeclaredIdentity(reader)) : default;
            var source = new GameplayAbilitySourceContext(skill, reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());
            if (source.InstanceId == 0 && (skill.IsValid || source.SkillExecutionGeneration != 0 || source.SegmentGeneration != 0))
                throw new InvalidDataException("Action source context identity is incomplete.");
            return source;
        }

        static void WriteTarget(CanonicalWriter writer, SimulationActionTargetSnapshot target)
        {
            writer.WriteText(target.TargetId);
            writer.WriteVector3(target.Position);
            writer.WriteYaw(target.Yaw);
        }

        static SimulationActionTargetSnapshot ReadTarget(CanonicalReader reader) =>
            new SimulationActionTargetSnapshot(
                reader.ReadText(),
                reader.ReadVector3(),
                reader.ReadYaw());

        static void WriteActionReference(CanonicalWriter writer, FixedActionInstanceReference value)
        {
            writer.WriteBoolean(value.IsValid);
            if (!value.IsValid)
                return;
            writer.WriteText(value.ActionId);
            writer.WriteText(value.ContextId);
            writer.WriteUInt64(value.InstanceId);
            writer.WriteUInt64(value.PredictionKey);
            WriteOptionalSkill(writer, value.SkillId);
            WriteOptionalOperation(writer, value.SkillEntryOperation);
            writer.WriteUInt64(value.SkillExecutionGeneration);
        }

        static FixedActionInstanceReference ReadActionReference(
            CanonicalReader reader,
            GameplayAbilityExecutionLayout layout, SimulationActorBinding actor)
        {
            if (!reader.ReadBoolean())
                return default;
            return new FixedActionInstanceReference(
                actor.ReadActionIdentity(reader),
                reader.ReadText(),
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                ReadOptionalSkill(reader, actor),
                ReadOptionalOperation(reader, layout),
                reader.ReadUInt64());
        }

        static void WriteOptionalSkill(CanonicalWriter writer, CharacterSkillId value)
        {
            writer.WriteBoolean(value.IsValid);
            if (value.IsValid)
                writer.WriteText(value.Value);
        }

        static CharacterSkillId ReadOptionalSkill(CanonicalReader reader, SimulationActorBinding actor) =>
            reader.ReadBoolean() ? new CharacterSkillId(actor.DeclaredIdentityReader(reader)) : default;

        static void WriteOptionalOperation(CanonicalWriter writer, OperationHandle value) =>
            writer.WriteInt32(value.IsValid ? value.Value : -1);

        static OperationHandle ReadOptionalOperation(CanonicalReader reader, GameplayAbilityExecutionLayout layout)
        {
            int value = reader.ReadInt32();
            if (value < -1 || layout != null && value >= layout.Operations.Count)
                throw new InvalidDataException("Fixed Character runtime state operation handle is invalid.");
            return value < 0 ? OperationHandle.Invalid : new OperationHandle(value);
        }

        static OperationHandle ReadRequiredOperation(CanonicalReader reader, GameplayAbilityExecutionLayout layout)
        {
            OperationHandle operation = ReadOptionalOperation(reader, layout);
            return operation.IsValid
                ? operation
                : throw new InvalidDataException("Fixed Character runtime state requires an operation handle.");
        }

        static void WriteEquipmentContext(CanonicalWriter writer, EquipmentActionContext context)
        {
            writer.WriteBoolean(context.IsValid);
            if (!context.IsValid)
                return;
            writer.WriteText(context.SlotId.Value);
            writer.WriteText(context.EquipmentId.Value);
            writer.WriteText(context.FeatureId.Value);
            writer.WriteUInt64(context.EquipmentRevision);
            writer.WriteText(context.RouteId.Value);
        }

        static EquipmentActionContext ReadEquipmentContext(CanonicalReader reader, EquipmentProgramLayout layout, SimulationActorBinding actor)
        {
            if (!reader.ReadBoolean())
                return default;
            var context = new EquipmentActionContext(
                new EquipmentSlotId(reader.ReadText()),
                new EquipmentId(actor.DeclaredIdentityReader(reader)),
                new EquipmentFeatureId(actor.DeclaredIdentityReader(reader)),
                reader.ReadUInt64(),
                new EquipmentActionRouteId(actor.DeclaredIdentityReader(reader)));
            if (layout == null)
                throw new InvalidDataException("Fixed Character runtime state contains an Equipment Action Context without an Equipment layout.");
            EquipmentProgramItem item = layout.RequireItem(context.EquipmentId);
            EquipmentProgramFeature feature = layout.RequireFeature(context.FeatureId);
            EquipmentProgramRoute route = layout.RequireRoute(context.RouteId);
            if (!layout.TryGetRouteImplementation(context.FeatureId, context.RouteId, out _))
                throw new InvalidDataException($"Equipment Action Context '{context}' has no Feature route implementation.");
            if (item.SlotId != context.SlotId || item.FeatureId != context.FeatureId ||
                route.OwnerSlotId != context.SlotId || feature.FeatureId != context.FeatureId)
            {
                throw new InvalidDataException($"Equipment Action Context '{context}' does not match the Equipment layout.");
            }
            return context;
        }

        static void RequireActivationEntry(
            CharacterSkillId skillId,
            CanonicalTextValue entryId,
            FixedGameplayAbilityExecutionInstallationSet installations)
        {
            if (CanonicalTextValue.IsNullOrEmpty(entryId))
                return;
            var operations = installations.Require(skillId).Layout.Operations;
            for (int i = 0; i < operations.Count; i++)
                if (operations[i].Code == SimulationOperationCode.ActivationEntry &&
                    CanonicalTextValue.Equals(operations[i].Text0, entryId, StringComparison.Ordinal))
                    return;
            throw new InvalidDataException($"Action activation entry '{entryId}' is absent from its installed Ability.");
        }

        static void RequireSkillExecution(
            CharacterSkillId skillId,
            OperationHandle operation,
            FixedGameplayAbilityExecutionInstallationSet installations)
        {
            if (!operation.IsValid)
                return;
            if (!skillId.IsValid)
                throw new InvalidDataException("Fixed Character runtime state operation has no Ability identity.");
            FixedGameplayAbilityExecutionInstallation installation = installations.Require(skillId);
            if (operation.Value >= installation.Layout.Operations.Count)
                throw new InvalidDataException("Fixed Character runtime state Ability operation is outside its installed Ability layout.");
        }

        static int ReadCount(CanonicalReader reader, int maximum, string label)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum)
                throw new InvalidDataException($"{label} count '{count}' is invalid.");
            return count;
        }

        static SimulationActionPhase ReadActionPhase(byte value)
        {
            var result = (SimulationActionPhase)value;
            if (result is not (SimulationActionPhase.Startup or
                SimulationActionPhase.Active or
                SimulationActionPhase.Recovery or
                SimulationActionPhase.Cancel or
                SimulationActionPhase.Ended))
                throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(SimulationActionPhase)}' value '{value}' is invalid.");
            return result;
        }

        static SimulationActionState ReadActionState(byte value)
        {
            var result = (SimulationActionState)value;
            if (result is not (SimulationActionState.Requested or
                SimulationActionState.Predicted or
                SimulationActionState.Confirmed or
                SimulationActionState.Rejected or
                SimulationActionState.Cancelled or
                SimulationActionState.Interrupted or
                SimulationActionState.Aborted or
                SimulationActionState.Ended or
                SimulationActionState.Corrected))
                throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(SimulationActionState)}' value '{value}' is invalid.");
            return result;
        }

        static SimulationActionLifecycleTransitionType ReadActionTransition(byte value)
        {
            var result = (SimulationActionLifecycleTransitionType)value;
            if (result is not (SimulationActionLifecycleTransitionType.None or
                SimulationActionLifecycleTransitionType.Confirm or
                SimulationActionLifecycleTransitionType.Complete or
                SimulationActionLifecycleTransitionType.Cancel or
                SimulationActionLifecycleTransitionType.Interrupt or
                SimulationActionLifecycleTransitionType.Reject or
                SimulationActionLifecycleTransitionType.Correct or
                SimulationActionLifecycleTransitionType.Abort))
                throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(SimulationActionLifecycleTransitionType)}' value '{value}' is invalid.");
            return result;
        }

        static ProgramMotionWarpLimitResult ReadMotionWarpLimitResult(byte value)
        {
            var result = (ProgramMotionWarpLimitResult)value;
            if (result is not (ProgramMotionWarpLimitResult.Applied or ProgramMotionWarpLimitResult.AppliedClamped or
                ProgramMotionWarpLimitResult.PreservedByLimitPolicy))
                throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(ProgramMotionWarpLimitResult)}' value '{value}' is invalid.");
            return result;
        }

        static AbilityTimelineSnapshotMode ReadTimelineSnapshotMode(byte value)
        {
            if (value > (byte)AbilityTimelineSnapshotMode.HoldLastFrame)
                throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(AbilityTimelineSnapshotMode)}' value '{value}' is invalid.");
            return (AbilityTimelineSnapshotMode)value;
        }

        static AbilityTimelineSnapshotState ReadTimelineSnapshotState(byte value)
        {
            if (value > (byte)AbilityTimelineSnapshotState.Disposed)
                throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(AbilityTimelineSnapshotState)}' value '{value}' is invalid.");
            return (AbilityTimelineSnapshotState)value;
        }

        static AbilityTimelineSnapshotStopCause ReadTimelineSnapshotStopCause(byte value)
        {
            if (value > (byte)AbilityTimelineSnapshotStopCause.Shutdown)
                throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(AbilityTimelineSnapshotStopCause)}' value '{value}' is invalid.");
            return (AbilityTimelineSnapshotStopCause)value;
        }

        static ProgramScopeKind ReadProgramScopeKind(byte value)
        {
            if (value < (byte)ProgramScopeKind.Character || value > (byte)ProgramScopeKind.Frame)
                throw new InvalidDataException($"Fixed Character runtime state enum '{nameof(ProgramScopeKind)}' value '{value}' is invalid.");
            return (ProgramScopeKind)value;
        }

    }
}
