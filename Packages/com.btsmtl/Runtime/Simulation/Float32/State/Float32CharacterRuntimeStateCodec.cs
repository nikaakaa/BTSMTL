using MotionWarpState = ThirdPersonSimulation.MotionWarpState<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.Float32Yaw, ThirdPersonSimulation.Float32ActionInstanceReference, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Scalar>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Vector3>, ThirdPersonSimulation.CanonicalRecordLeafCodec<ThirdPersonSimulation.Float32Yaw>>;
using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using System.IO;
using SimulationActionActivationRequestState = ThirdPersonSimulation.SimulationActionActivationRequestState<ThirdPersonSimulation.SimulationActionTargetSnapshot>;

namespace ThirdPersonSimulation
{
    public static class Float32CharacterRuntimeStateCodec
    {
        [ThreadStatic] static CanonicalWriter s_StateWriter;
        static readonly CanonicalPublishedValues s_PublishedValues = PreparePublishedValues();

        static CanonicalPublishedValues PreparePublishedValues()
        {
            var values = new CanonicalPublishedValues();
            values.Add(Float32SimulationNumericProfile.Value);
            return values;
        }
        const uint Magic = 0x54535243;
        const int Version = 29;
        const string HashIdentity = "float32-character-runtime-state-hash/23";
        public const string CodecIdentity = "float32-character-runtime-state/23";

        public static byte[] Write(Float32CharacterRuntimeState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            CanonicalWriter writer = s_StateWriter ??= new CanonicalWriter();
            writer.Reset();
            WriteCanonical(writer, state);
            return writer.ToArray();
        }

        public static CharacterStateHash ComputeHash(Float32CharacterRuntimeState state) =>
            ComputeHash(state, s_StateWriter ??= new CanonicalWriter());

        public static byte[] Write(Float32CharacterRuntimeState state, out CharacterStateHash stateHash)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            CanonicalWriter writer = s_StateWriter ??= new CanonicalWriter();
            writer.Reset();
            writer.WriteText(HashIdentity);
            int payloadOffset = (int)writer.Length;
            WriteCanonical(writer, state);
            stateHash = new CharacterStateHash(writer.ComputeHash());
            return writer.WrittenSpan.Slice(payloadOffset).ToArray();
        }

        public static CharacterStateHash ComputeHash(Float32CharacterRuntimeState state, CanonicalWriter writer)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
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
                Float32CharacterRuntimeState state = actors[index].State;
                if (state.CanonicalPayload.Bytes.IsEmpty)
                    Encode(state, encoding);
            }
        }

        internal static int HashPrefixBytes => sizeof(int) + CanonicalTextEncoding.ByteCount(HashIdentity.AsSpan());

        internal static CharacterStateHash Encode(Float32CharacterRuntimeState state,
            SimulationCharacterStatePayloadStorage.Page page)
        {
            CanonicalWriter writer = page.Writer;
            int hashStart = checked((int)writer.Position);
            writer.WriteText(HashIdentity);
            int start = checked((int)writer.Position);
            WriteCanonical(writer, state);
            int end = checked((int)writer.Position);
            var hash = new CharacterStateHash(SimulationCanonicalPayloadHash.Compute(
                writer.WrittenSpan.Slice(hashStart, end - hashStart)));
            state.SetCanonicalPayload(new SimulationCharacterStatePayload(page,
                writer.WrittenSegment.Array.AsMemory(start, end - start), hash));
            return hash;
        }

        public static Float32CharacterRuntimeState Read(
            ReadOnlyMemory<byte> bytes, SimulationActorBinding actor) =>
            Read(bytes, actor, out _);

        public static Float32CharacterRuntimeState Read(
            ReadOnlyMemory<byte> bytes, SimulationActorBinding actor, out CharacterStateHash stateHash)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));
            return Read(bytes, actor, new CanonicalReader(Array.Empty<byte>()), new CanonicalWriter(), null,
                out stateHash);
        }

        public static Float32CharacterRuntimeState Read(
            ReadOnlyMemory<byte> bytes, SimulationActorBinding actor,
            CanonicalReader reader, CanonicalWriter writer, ICanonicalTextResolver resolver,
            out CharacterStateHash stateHash)
        {
            using SimulationCharacterStatePayloadStorage.Page encoding = actor.RuntimeStatePages.EncodingStorage?.Acquire();
            return Read(bytes, actor, reader, writer, resolver, encoding, out stateHash);
        }

        internal static Float32CharacterRuntimeState Read(
            ReadOnlyMemory<byte> bytes, SimulationActorBinding actor,
            CanonicalReader reader, CanonicalWriter writer, ICanonicalTextResolver resolver,
            SimulationCharacterStatePayloadStorage.Page encoding, out CharacterStateHash stateHash)
        {
            if (bytes.Length == 0)
                throw new ArgumentException("Character runtime state payload is empty.", nameof(bytes));
            Float32GameplayAbilityExecutionInstallationSet installations = actor.AbilityInstallations;
            GameplayContentHash expectedGameplayContentHash = new GameplayContentHash(actor.GameplayContentHash);
            reader.Reset(bytes);
            reader.SetTextResolver(resolver);
            reader.DeclaredIdentityReader = actor.DeclaredIdentityReader;
            reader.PublishedValues = s_PublishedValues;
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version ||
                !CanonicalTextValue.Equals(reader.ReadText(), CodecIdentity, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Float32 Character runtime state header is invalid.");
            }
            AbilityStateValue.CanonicalRequireSchema(reader);
            EquipmentActionContext.CanonicalRequireSchema(reader);
            GameplayEffectOpportunityReference.CanonicalRequireSchema(reader);
            SimulationInputRequestState.CanonicalRequireSchema(reader);
            SimulationExecutionSource.CanonicalRequireSchema(reader);
            SimulationActionActivationRequestState.CanonicalRequireSchema(reader);
            Float32ActionInstanceState.CanonicalRequireSchema(reader);
            GameplayAbilityExecutionIdentity.CanonicalRequireSchema(reader);
            AbilityExecutionContext.CanonicalRequireSchema(reader);
            CharacterRuntimeStateMetadata.CanonicalRequireSchema(reader);
            MotionWarpState.CanonicalRequireSchema(reader);
            CharacterRuntimeStateMetadata metadata = CharacterRuntimeStateMetadata.CanonicalReadBound(
                reader, expectedGameplayContentHash, Float32SimulationNumericProfile.Value, actor.StateSchemaHash);
            AbilityStateValuePageReference<AbilityStateValue> characterBlackboards = actor.CharacterBlackboardPages.Read(reader);
            int abilityCount = ReadCount(reader, installations.Installations.Count, "Float32 Character Ability partition");
            if (abilityCount != installations.Installations.Count)
                throw new InvalidDataException("Float32 Character runtime state Ability partitions do not match the installed Ability set.");
            Float32CharacterRuntimeStatePage page = actor.RuntimeStatePages.Begin();
            page.Data.CharacterBlackboards = characterBlackboards;

            Float32AbilityRuntimeState[] abilities = page.Abilities;
            CharacterSkillId previousAbility = default;
            AbilityTimelineSnapshotStorage timelineStorage = null;
            try
            {
                MotionWarpStatePageReference<MotionWarpState> motionWarpStates = actor.MotionWarpPages.BeginEmpty();
                for (int i = 0; i < abilityCount; i++)
                {
                    GameplayAbilityExecutionIdentity identity = ReadIdentity(reader, actor);
                    if (i > 0 && previousAbility.CompareTo(identity.AbilityId) >= 0)
                        throw new InvalidDataException("Float32 Character Ability partitions are duplicated or not canonically ordered.");
                    previousAbility = identity.AbilityId;
                    Float32GameplayAbilityExecutionInstallation installation = installations.Require(identity.AbilityId);
                    AbilityStateValuePageReference<AbilityStateValue> stateValues = actor.StateValuePages[identity.AbilityId].Read(reader);
                    GameplayAbilityExecutionStateBank<AbilityStateValue> executionBank = actor.ExecutionStatePages[identity.AbilityId];
                    GameplayAbilityExecutionAggregate<AbilityStateValue> abilityExecutionState = executionBank.Capture();
                    abilities[i] = Float32AbilityRuntimeState.Adopt(stateValues, abilityExecutionState);
                    ReadAbilityExecutionState(reader, installation.Layout, abilityExecutionState, executionBank.FrameCapacity, actor);
                    ReadMotionWarpStates(reader, installation.Layout, motionWarpStates, identity.AbilityId, actor);
                }
                EquipmentProgramLayout equipmentLayout = actor.EquipmentState.Layout;
                Float32ActionStatePageReference actions = actor.ActionStatePages.Begin();
                ReadActionActivationRequests(reader, actor, equipmentLayout, actions.Page);
                ReadActionInstances(reader, actor, equipmentLayout, actions.Page);
                Float32InputRequestPageReference inputRequests = ReadInputRequests(reader, actor);
                CharacterControlRuntimeState controlState = reader.ReadBoolean()
                    ? CharacterControlRuntimeStateCodec.Read(reader.ReadBytesSegment(), actor.CheckpointControlSchema,
                        page.ControlStorage, actor.CheckpointControlReader, reader.TextResolver)
                    : default;
                GameplayEffectStateAggregate gameplayEffectState = default;
                if (reader.ReadBoolean())
                {
                    if (actor.AbilityInstallations.GameplayEffectCatalog == null)
                        throw new InvalidDataException("Float32 Character runtime state contains Gameplay Effect state without a Character Effect service.");
                    gameplayEffectState = actor.GameplayEffectState.Decode(reader.ReadBytesSegment(), actor.EquipmentState.Storage, reader.TextResolver);
                }
                EquipmentStateAggregate equipmentState = null;
                if (reader.ReadBoolean())
                {
                    if (equipmentLayout == null)
                        throw new InvalidDataException("Float32 Character runtime state contains Equipment state without an installed layout.");
                    equipmentState = EquipmentStateAggregateCodec.Read(reader.ReadBytesSegment(), actor.EquipmentState.Storage, reader.TextResolver);
                }
                AbilityTimelineRuntimeSnapshot.CanonicalRequireSchema(reader);
                int timelineSnapshotCount = ReadCount(reader, Float32CharacterRuntimeStatePageBank.MaximumTimelineSnapshotCount, "Float32 Character Timeline snapshot");
                if (timelineSnapshotCount != 0)
                    timelineStorage = actor.RuntimeStatePages.BeginTimelineDecode();
                ulong previousTimelineHandle = 0;
                for (int i = 0; i < timelineSnapshotCount; i++)
                {
                    AbilityTimelineRuntimeSnapshot snapshot = AbilityTimelineRuntimeSnapshot.CanonicalRead(reader, timelineStorage);
                    if (i > 0 && snapshot.RuntimeHandle <= previousTimelineHandle)
                        throw new InvalidDataException("Float32 Character Timeline snapshots are duplicated or not canonically ordered.");
                    previousTimelineHandle = snapshot.RuntimeHandle;
                    page.TimelineValues.Add(snapshot);
                }
                reader.RequireComplete();
                Float32CharacterRuntimeState state = Float32CharacterRuntimeState.AdoptSnapshot(
                    page,
                    metadata,
                    actions,
                    inputRequests,
                    controlState,
                    gameplayEffectState,
                    equipmentState,
                    motionWarpStates);
                if (encoding != null)
                {
                    stateHash = Encode(state, encoding);
                    if (!state.CanonicalPayload.Bytes.Span.SequenceEqual(bytes.Span))
                        throw new InvalidDataException("Float32 Character runtime state is not canonical.");
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
                        throw new InvalidDataException("Float32 Character runtime state is not canonical.");
                }
                return state;
            }
            catch
            {
                foreach (GameplayAbilityExecutionStateBank<AbilityStateValue> bank in actor.ExecutionStatePages.Values)
                    bank.ReleaseCurrent();
                actor.GameplayEffectState.Dispose();
                actor.EquipmentState.Storage?.ReleaseDecodedState();
                timelineStorage?.BeginDecode();
                page.Clear();
                throw;
            }
        }

        internal static void WriteCanonical(CanonicalWriter writer, Float32CharacterRuntimeState state)
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
            writer.WriteHash(Float32ActionInstanceState.CanonicalSchemaHash);
            writer.WriteHash(GameplayAbilityExecutionIdentity.CanonicalSchemaHash);
            writer.WriteHash(AbilityExecutionContext.CanonicalSchemaHash);
            writer.WriteHash(CharacterRuntimeStateMetadata.CanonicalSchemaHash);
            writer.WriteHash(MotionWarpState.CanonicalSchemaHash);
            CharacterRuntimeStateMetadata.CanonicalWrite(writer, state.Metadata);
            state.CharacterBlackboards.Write(writer);
            writer.WriteInt32(state.Abilities.Count);
            for (int i = 0; i < state.Abilities.Count; i++)
            {
                Float32AbilityRuntimeState ability = state.Abilities[i];
                GameplayAbilityExecutionIdentity.CanonicalWrite(writer, ability.AbilityIdentity);
                ability.StateValues.Write(writer);
                WriteAbilityExecutionState(writer, ability.AbilityExecutionState);
                WriteMotionWarpStates(writer, state.MotionWarpStates, ability.AbilityIdentity.AbilityId);
            }
            WriteActionActivationRequests(writer, state.ActionActivationRequests);
            WriteActionInstances(writer, state.ActionInstances);
            WriteInputRequests(writer, state.InputRequests);
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
            writer.WriteHash(AbilityTimelineRuntimeSnapshot.CanonicalSchemaHash);
            writer.WriteInt32(state.TimelineSnapshots.Count);
            for (int i = 0; i < state.TimelineSnapshots.Count; i++)
                AbilityTimelineRuntimeSnapshot.CanonicalWrite(writer, state.TimelineSnapshots[i]);
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

        static void ReadAbilityExecutionState(
            CanonicalReader reader, GameplayAbilityExecutionLayout layout,
            GameplayAbilityExecutionAggregate<AbilityStateValue> aggregate, int frameCapacity, SimulationActorBinding actor)
        {
            int count = ReadCount(reader, frameCapacity, "Float32 Ability execution frame");
            ulong previous = 0;
            for (int index = 0; index < count; index++)
            {
                AbilityExecutionContext identity = AbilityExecutionContext.CanonicalRead(reader);
                if (identity.EntryOperation.Value >= layout.Operations.Count ||
                    index > 0 && identity.ActionInstanceId <= previous)
                    throw new InvalidDataException("Float32 Ability execution frame operation or canonical order is invalid.");
                GameplayAbilityExecutionFrame<AbilityStateValue> frame = aggregate.AppendRestoredFrame(identity);
                frame.StateValues.Set.Read(reader);
                previous = identity.ActionInstanceId;
            }
        }

        static void WriteInputRequests(
            CanonicalWriter writer,
            ReadOnlySpan<KeyValuePair<CanonicalTextValue, SimulationInputRequestState>> requests)
        {
            writer.WriteInt32(requests.Length);
            for (int i = 0; i < requests.Length; i++)
            {
                writer.WriteText(requests[i].Key);
                SimulationInputRequestState.CanonicalWrite(writer, requests[i].Value);
            }
        }

        static Float32InputRequestPageReference ReadInputRequests(
            CanonicalReader reader,
            SimulationActorBinding actor)
        {
            int count = ReadCount(reader, actor.InputRequestPages.RequestCapacity, "Float32 Character Input request");
            Float32InputRequestPageReference requests = actor.InputRequestPages.Begin(count);
            CanonicalTextValue previous = default;
            for (int i = 0; i < count; i++)
            {
                CanonicalTextValue requestId = SimulationIdentity.Require(reader.ReadText(), "InputRequestId");
                if (!previous.IsNull && CanonicalTextValue.CompareOrdinal(previous, requestId) >= 0 ||
                    !ContainsInputRequest(actor, requestId))
                    throw new InvalidDataException("Float32 Character Input request identities are invalid or not canonically ordered.");
                SimulationInputRequestState value = SimulationInputRequestState.CanonicalRead(reader);
                if (value.IsValid && !CanonicalTextValue.Equals(value.RequestId, requestId, StringComparison.Ordinal))
                    throw new InvalidDataException("Float32 Character Input request state key does not match its value.");
                requests.Page.Values[i] = new(requestId, value);
                previous = requestId;
            }
            return requests;
        }

        static bool ContainsInputRequest(
            SimulationActorBinding actor,
            CanonicalTextValue requestId)
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
            Float32ActionStatePage page)
        {
            Float32GameplayAbilityExecutionInstallationSet installations = actor.AbilityInstallations;
            int count = ReadCount(reader, page.Bank.ActionCapacity, "Float32 Action activation request");
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

        static void WriteActionInstances(CanonicalWriter writer, ReadOnlySpan<Float32ActionInstanceState> actions)
        {
            writer.WriteInt32(actions.Length);
            for (int i = 0; i < actions.Length; i++)
                Float32ActionInstanceState.CanonicalWrite(writer, in actions[i]);
        }

        static void ReadActionInstances(
            CanonicalReader reader,
            SimulationActorBinding actor,
            EquipmentProgramLayout equipmentLayout,
            Float32ActionStatePage page)
        {
            Float32GameplayAbilityExecutionInstallationSet installations = actor.AbilityInstallations;
            int count = ReadCount(reader, page.Bank.ActionCapacity, "Float32 Action instance");
            Float32ActionInstanceState[] actions = page.Actions;
            int actionCount = 0;
            for (int i = 0; i < count; i++)
            {
                Float32ActionInstanceState action = Float32ActionInstanceState.CanonicalRead(reader);
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
            in MotionWarpStatePageReference<MotionWarpState> states,
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
                ref readonly MotionWarpState value = ref states.Page.At(offset + index);
                if (!value.Active)
                    continue;
                writer.WriteInt32(operations[index]);
                MotionWarpState.CanonicalWrite(writer, value);
            }
        }

        static void ReadMotionWarpStates(
            CanonicalReader reader,
            GameplayAbilityExecutionLayout layout,
            in MotionWarpStatePageReference<MotionWarpState> states,
            CharacterSkillId ability, SimulationActorBinding actor)
        {
            int count = ReadCount(reader, layout.MotionWarpOperationIds.Count, "Float32 MotionWarp state");
            int partition = states.Page.Layout.Partition(ability);
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                int operation = reader.ReadInt32();
                if (operation < 0 || operation <= previous || !layout.HasMotionWarp(new OperationHandle(operation)))
                    throw new InvalidDataException("Float32 MotionWarp operation identities are invalid or not canonically ordered.");
                MotionWarpState value = ReadMotionWarpState(reader, layout, actor);
                if (!value.Active)
                    throw new InvalidDataException("Float32 MotionWarp state is not active.");
                states.Page.Set(states.Page.Layout.Slot(partition, new OperationHandle(operation)), in value);
                previous = operation;
            }
        }

        static MotionWarpState ReadMotionWarpState(CanonicalReader reader,
            GameplayAbilityExecutionLayout layout, SimulationActorBinding actor)
        {
            MotionWarpState value = MotionWarpState.CanonicalRead(reader);
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
                throw new InvalidDataException("Float32 Character runtime state contains an Equipment Action Context without an Equipment layout.");
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
            string entryId,
            Float32GameplayAbilityExecutionInstallationSet installations)
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
            Float32GameplayAbilityExecutionInstallationSet installations)
        {
            if (!operation.IsValid)
                return;
            if (!skillId.IsValid)
                throw new InvalidDataException("Float32 Character runtime state operation has no Ability identity.");
            Float32GameplayAbilityExecutionInstallation installation = installations.Require(skillId);
            if (operation.Value >= installation.Layout.Operations.Count)
                throw new InvalidDataException("Float32 Character runtime state Ability operation is outside its installed Ability layout.");
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
