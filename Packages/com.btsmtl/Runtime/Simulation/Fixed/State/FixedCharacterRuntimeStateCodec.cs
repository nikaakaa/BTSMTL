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
        static readonly CanonicalPublishedValues s_PublishedValues = PreparePublishedValues();

        static CanonicalPublishedValues PreparePublishedValues()
        {
            var values = new CanonicalPublishedValues();
            values.Add(FixedSimulationNumericProfile.Value);
            return values;
        }
        const uint Magic = 0x54535243;
        const int Version = 27;
        const string HashIdentity = "fixed-character-runtime-state-hash/21";
        public const string CodecIdentity = "fixed-character-runtime-state/21";

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

        internal static void PreparePayloads(IReadOnlyList<SimulationActorState> actors
#if UNITY_EDITOR
            , ref SimulationSnapshotCaptureProfile profile
#endif
            )
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
                    Encode(state, encoding
#if UNITY_EDITOR
                        , ref profile
#endif
                        );
            }
        }

        internal static int HashPrefixBytes => sizeof(int) + CanonicalTextEncoding.ByteCount(HashIdentity.AsSpan());

        internal static CharacterStateHash Encode(FixedCharacterRuntimeState state,
            SimulationCharacterStatePayloadStorage.Page page
#if UNITY_EDITOR
            , ref SimulationSnapshotCaptureProfile profile
#endif
            )
        {
#if UNITY_EDITOR
            long encodeStarted = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
            CanonicalWriter writer = page.Writer;
            int hashStart = checked((int)writer.Position);
            writer.WriteText(HashIdentity);
            int start = checked((int)writer.Position);
            WriteCanonical(writer, state);
            int end = checked((int)writer.Position);
#if UNITY_EDITOR
            long hashStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            profile.ActorEncodeElapsedStopwatchTicks += hashStarted - encodeStarted;
#endif
            var hash = new CharacterStateHash(SimulationCanonicalPayloadHash.Compute(
                writer.WrittenSpan.Slice(hashStart, end - hashStart)));
#if UNITY_EDITOR
            profile.ActorHashElapsedStopwatchTicks += System.Diagnostics.Stopwatch.GetTimestamp() - hashStarted;
#endif
            state.SetCanonicalPayload(new SimulationCharacterStatePayload(page,
                writer.WrittenSegment.Array.AsMemory(start, end - start), hash));
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
            reader.DeclaredIdentityReader = actor.DeclaredIdentityReader;
            reader.PublishedValues = s_PublishedValues;
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
            {
                throw new InvalidDataException("Fixed Character runtime state header is invalid.");
            }
            reader.RequireText(CodecIdentity);
            AbilityStateValue.CanonicalRequireSchema(reader);
            EquipmentActionContext.CanonicalRequireSchema(reader);
            GameplayEffectOpportunityReference.CanonicalRequireSchema(reader);
            SimulationInputRequestState.CanonicalRequireSchema(reader);
            SimulationExecutionSource.CanonicalRequireSchema(reader);
            SimulationActionActivationRequestState.CanonicalRequireSchema(reader);
            FixedActionInstanceState.CanonicalRequireSchema(reader);
            GameplayAbilityExecutionIdentity.CanonicalRequireSchema(reader);
            AbilityExecutionContext.CanonicalRequireSchema(reader);
            SimulationNumericProfile numericProfile = SimulationNumericProfile.CanonicalReadPublished(reader, FixedSimulationNumericProfile.Value);
            GameplayContentHash gameplayContentHash = GameplayContentHash.CanonicalFromHash(reader.ReadHash());
            if (!gameplayContentHash.Equals(expectedGameplayContentHash))
                throw new InvalidDataException("Fixed Character runtime state GameplayContentHash does not match the active Character Runtime.");
            StableHash stateSchemaHash = reader.ReadHash();
            if (!stateSchemaHash.Equals(actor.StateSchemaHash))
                throw new InvalidDataException("Fixed Character runtime state StateSchemaHash does not match the active Character Runtime.");
            ulong lastCompletedTick = reader.ReadUInt64();
            AbilityStateValuePageReference<AbilityStateValue> characterBlackboards = actor.CharacterBlackboardPages.Read(reader);
            int abilityCount = ReadCount(reader, installations.Installations.Count, "Fixed Character Ability partition");
            if (abilityCount != installations.Installations.Count)
                throw new InvalidDataException("Fixed Character runtime state Ability partitions do not match the installed Ability set.");
            statePage.Data.CharacterBlackboards = characterBlackboards;
            FixedAbilityStateDirectoryReference abilityDirectory = actor.AbilityDirectories.Begin();
            FixedAbilityRuntimeState[] abilities = abilityDirectory.Page.Values;
            MotionWarpStatePageReference<FixedMotionWarpState> motionWarpStates = actor.MotionWarpPages.BeginEmpty();
            for (int i = 0; i < abilityCount; i++)
            {
                GameplayAbilityExecutionIdentity identity = ReadIdentity(reader, actor);
                FixedGameplayAbilityExecutionInstallation installation = installations.Require(identity.AbilityId);
                AbilityStateValuePageReference<AbilityStateValue> stateValues = actor.StateValuePages[identity.AbilityId].Read(reader);
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
                    statePage.ControlStorage, actor.CheckpointControlReader, reader.TextResolver)
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
            AbilityTimelineRuntimeSnapshot.CanonicalRequireSchema(reader);
            int timelineSnapshotCount = ReadCount(reader, FixedTimelineSnapshotPageBank.MaximumSnapshotCount, "Fixed Character Timeline snapshot");
            FixedTimelineSnapshotPageReference timelineSnapshots = actor.TimelineSnapshotPages.BeginDecode(out AbilityTimelineSnapshotStorage timelineStorage);
            for (int i = 0; i < timelineSnapshotCount; i++)
                timelineSnapshots.Page.Values.Add(AbilityTimelineRuntimeSnapshot.CanonicalRead(reader, timelineStorage));
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
            ReadOnlySpan<byte> canonical;
            if (encoding != null)
            {
#if UNITY_EDITOR
                SimulationSnapshotCaptureProfile profile = default;
#endif
                stateHash = Encode(state, encoding
#if UNITY_EDITOR
                    , ref profile
#endif
                    );
                canonical = state.CanonicalPayload.Bytes.Span;
            }
            else
            {
                writer.SetTextCapture(null);
                writer.Reset();
                writer.WriteText(HashIdentity);
                int payloadStart = checked((int)writer.Position);
                WriteCanonical(writer, state);
                stateHash = new CharacterStateHash(writer.ComputeHash());
                canonical = writer.WrittenSpan.Slice(payloadStart);
            }
            if (!canonical.SequenceEqual(bytes.Span))
            {
                int offset = 0;
                int sharedLength = Math.Min(canonical.Length, bytes.Length);
                while (offset < sharedLength && canonical[offset] == bytes.Span[offset])
                    offset++;
                throw new InvalidDataException(
                    $"Fixed Character runtime state is not canonical. Actor '{actor.ActorId}', Tick '{lastCompletedTick}', byte '{offset}', stored length '{bytes.Length}', decoded length '{canonical.Length}'. " +
                    $"StoredPayloadBase64={Convert.ToBase64String(bytes.Span)} DecodedPayloadBase64={Convert.ToBase64String(canonical)}");
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

        internal static void WriteCanonical(CanonicalWriter writer, FixedCharacterRuntimeState state)
        {
            state.MotionWarpStates.RequireReadable();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteText(CodecIdentity);
            writer.WriteHash(AbilityStateValue.CanonicalSchemaHash);
            writer.WriteHash(EquipmentActionContext.CanonicalSchemaHash);
            writer.WriteHash(GameplayEffectOpportunityReference.CanonicalSchemaHash);
            writer.WriteHash(SimulationInputRequestState.CanonicalSchemaHash);
            writer.WriteHash(SimulationExecutionSource.CanonicalSchemaHash);
            writer.WriteHash(SimulationActionActivationRequestState.CanonicalSchemaHash);
            writer.WriteHash(FixedActionInstanceState.CanonicalSchemaHash);
            writer.WriteHash(GameplayAbilityExecutionIdentity.CanonicalSchemaHash);
            writer.WriteHash(AbilityExecutionContext.CanonicalSchemaHash);
            SimulationNumericProfile.CanonicalWrite(writer, state.NumericProfile);
            writer.WriteHash(state.GameplayContentHash);
            writer.WriteHash(state.StateSchemaHash);
            writer.WriteUInt64(state.LastCompletedTick);
            state.CharacterBlackboards.Write(writer);
            writer.WriteInt32(state.Abilities.Count);
            for (int i = 0; i < state.Abilities.Count; i++)
            {
                FixedAbilityRuntimeState ability = state.Abilities[i];
                GameplayAbilityExecutionIdentity.CanonicalWrite(writer, ability.AbilityIdentity);
                ability.StateValues.Write(writer);
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
            writer.WriteHash(AbilityTimelineRuntimeSnapshot.CanonicalSchemaHash);
            writer.WriteInt32(timelineSnapshots.Count);
            for (int i = 0; i < timelineSnapshots.Count; i++)
                AbilityTimelineRuntimeSnapshot.CanonicalWrite(writer, timelineSnapshots[i]);
        }



        static GameplayAbilityExecutionIdentity ReadIdentity(CanonicalReader reader, SimulationActorBinding actor)
        {
            GameplayAbilityExecutionIdentity identity = GameplayAbilityExecutionIdentity.CanonicalRead(reader);
            actor.AbilityInstallations.Require(identity.AbilityId).Identity.Require(identity);
            return identity;
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
                AbilityExecutionContext.CanonicalWrite(writer, frame.Identity);
                frame.StateValues.Write(writer);
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
                AbilityExecutionContext identity = AbilityExecutionContext.CanonicalRead(reader);
                if (identity.EntryOperation.Value >= layout.Operations.Count ||
                    i > 0 && identity.ActionInstanceId <= previous)
                    throw new InvalidDataException("Fixed Ability execution frame operation or canonical order is invalid.");
                GameplayAbilityExecutionFrame<AbilityStateValue> frame = aggregate.AppendRestoredFrame(identity);
                frame.StateValues.Set.Read(reader);
                previous = identity.ActionInstanceId;
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
                SimulationInputRequestState.CanonicalWrite(writer, requests[i].Value);
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
                SimulationInputRequestState value = SimulationInputRequestState.CanonicalRead(reader);
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
                SimulationActionActivationRequestState.CanonicalWrite(writer, in requests[i]);
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
                SimulationActionActivationRequestState request = SimulationActionActivationRequestState.CanonicalRead(reader);
                if (!request.IsValid)
                    continue;
                actor.RequireActionIdentity(request.ActionId);
                RequireEquipmentContext(request.EquipmentContext, equipmentLayout);
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
                FixedActionInstanceState.CanonicalWrite(writer, in actions[i]);
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
                FixedActionInstanceState action = FixedActionInstanceState.CanonicalRead(reader);
                if (!action.IsValid)
                    continue;
                actor.RequireActionIdentity(action.ActionId);
                RequireEquipmentContext(action.EquipmentContext, equipmentLayout);
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
                FixedMotionWarpState.CanonicalWrite(writer, value);
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

        static FixedMotionWarpState ReadMotionWarpState(CanonicalReader reader,
            GameplayAbilityExecutionLayout layout, SimulationActorBinding actor)
        {
            FixedMotionWarpState value = FixedMotionWarpState.CanonicalRead(reader);
            if (value.SourceOperation.IsValid && value.SourceOperation.Value >= layout.Operations.Count ||
                value.ActionInstance.SkillEntryOperation.IsValid &&
                value.ActionInstance.SkillEntryOperation.Value >= layout.Operations.Count)
                throw new InvalidDataException("MotionWarp operation is outside its installed Ability layout.");
            if (value.ActionInstance.IsValid)
                actor.RequireActionIdentity(value.ActionInstance.ActionId);
            return value;
        }







        static void RequireEquipmentContext(in EquipmentActionContext context, EquipmentProgramLayout layout)
        {
            if (!context.IsValid)
                return;
            if (layout == null)
                throw new InvalidDataException("Fixed Character runtime state contains an Equipment Action Context without an Equipment layout.");
            EquipmentProgramItem item = layout.RequireItem(context.EquipmentId);
            EquipmentProgramFeature feature = layout.RequireFeature(context.FeatureId);
            EquipmentProgramRoute route = layout.RequireRoute(context.RouteId);
            if (!layout.TryGetRouteImplementation(context.FeatureId, context.RouteId, out _))
                throw new InvalidDataException($"Equipment Action Context '{context}' has no Feature route implementation.");
            if (item.SlotId != context.SlotId || item.FeatureId != context.FeatureId ||
                route.OwnerSlotId != context.SlotId || feature.FeatureId != context.FeatureId)
                throw new InvalidDataException($"Equipment Action Context '{context}' does not match the Equipment layout.");
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

    }
}
