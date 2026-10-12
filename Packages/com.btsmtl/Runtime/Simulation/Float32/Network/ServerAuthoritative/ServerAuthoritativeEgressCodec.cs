using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public static class ServerAuthoritativeEgressChannels
    {
        public const string ClientInput = "server-authoritative.client-input";
        public const string AuthorityReplication = "server-authoritative.authority-replication";
        public const string RemotePresentation = "server-authoritative.remote-presentation";
        public const string ClientInputSchema = "server-authoritative-client-input/2";
        public const string AuthorityReplicationSchema = "server-authoritative-authority-replication/10";
        public const string RemotePresentationSchema = "server-authoritative-remote-presentation/10";
        public const int SchemaVersion = 2;
        public const int AuthorityReplicationSchemaVersion = 22;
        public const int RemotePresentationSchemaVersion = 22;
    }

    public static class ServerAuthoritativeEgressCodec
    {
        static readonly Func<CanonicalReader, string> ReadDeclaredIdentity =
            reader => CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());

        const uint InputMagic = 0x49454153;
        const uint ReplicationMagic = 0x52454153;
        const uint RemoteMagic = 0x50454153;
        const int InputVersion = 2;
        const int ReplicationVersion = 14;
        const int RemoteVersion = 14;
        const int MaximumCount = 4096;
        internal const long MaximumRemoteTextValues = 1L + MaximumCount *
            (3 + PresentationCommand.MaximumTextReferences + ServerAuthoritativeReliableEvent.MaximumTextReferences);

        public static byte[] WriteOwnerInput(OwnerCanonicalInputBatch input)
        {
            if (!input.IsValid)
                throw new ArgumentNullException(nameof(input));
            using var writer = new CanonicalWriter();
            WriteOwnerInput(writer, input);
            return writer.ToArray();
        }

        public static void WriteOwnerInput(CanonicalWriter writer, OwnerCanonicalInputBatch input)
        {
            writer.WriteUInt32(InputMagic);
            writer.WriteInt32(InputVersion);
            writer.WriteText(input.ActorId.Value);
            writer.WriteUInt64(input.SourceTick);
            writer.WriteUInt64(input.InputSequence);
            ServerAuthoritativeCanonicalCodec.WriteLengthPrefixedInput(writer, input.Input);
        }

        public static OwnerCanonicalInputBatch ReadOwnerInput(ReadOnlyMemory<byte> bytes, CanonicalReader reader,
            CanonicalReader inputReader, Float32SimulationInputFrameStorage inputStorage,
            CanonicalWriter canonicalScratch, ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            if (reader.ReadUInt32() != InputMagic || reader.ReadInt32() != InputVersion)
                throw new InvalidDataException("Owner input egress header is invalid.");
            var actorId = new ActorId(reader.ReadText());
            ulong sourceTick = reader.ReadUInt64();
            ulong inputSequence = reader.ReadUInt64();
            SimulationInput input = ServerAuthoritativeCanonicalCodec.ReadInput(reader.ReadBytesSegment(),
                inputReader, reader.TextResolver, inputStorage);
            reader.RequireComplete();
            var result = new OwnerCanonicalInputBatch(actorId, sourceTick, inputSequence, input);
            canonicalScratch.Reset();
            WriteOwnerInput(canonicalScratch, result);
            RequireCanonical(bytes.Span, canonicalScratch, "owner input");
            return result;
        }

        public static byte[] WriteAuthorityReplication(AuthorityReplicationBatch batch)
        {
            if (!batch.IsValid)
                throw new ArgumentException("Authority replication batch is invalid.", nameof(batch));
            using var writer = new CanonicalWriter();
            WriteAuthorityReplication(writer, batch);
            return writer.ToArray();
        }

        public static void WriteAuthorityReplication(CanonicalWriter writer, AuthorityReplicationBatch batch)
        {
            writer.WriteUInt32(ReplicationMagic);
            writer.WriteInt32(ReplicationVersion);
            writer.WriteUInt64(batch.AuthorityTick.Value);
            writer.WriteInt32(batch.Acks.Count);
            for (int i = 0; i < batch.Acks.Count; i++)
                WriteAck(writer, batch.Acks[i]);
            writer.WriteInt32(batch.Baselines.Count);
            for (int i = 0; i < batch.Baselines.Count; i++)
                ServerAuthoritativeCanonicalCodec.WriteLengthPrefixedBaseline(writer, batch.Baselines[i]);
            writer.WriteInt32(batch.RemotePresentation.Count);
            for (int i = 0; i < batch.RemotePresentation.Count; i++)
                WriteLengthPrefixedRemotePresentation(writer, batch.RemotePresentation[i]);
        }

        public static AuthorityReplicationBatch ReadAuthorityReplication(ReadOnlyMemory<byte> bytes,
            CanonicalReader reader, CanonicalReader elementReader, CanonicalWriter canonicalScratch,
            CanonicalHashWriter bodyHashWriter, ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            if (reader.ReadUInt32() != ReplicationMagic || reader.ReadInt32() != ReplicationVersion)
                throw new InvalidDataException("ServerAuthoritative authority replication schema is invalid.");
            var tick = new SimulationTick(reader.ReadUInt64());
            int ackCount = ReadCount(reader, "input ack");
            var acks = new AuthoritativeInputAck[ackCount];
            for (int i = 0; i < ackCount; i++)
                acks[i] = ReadAck(reader);
            int baselineCount = ReadCount(reader, "baseline");
            var baselines = new AuthoritativeActorBaseline[baselineCount];
            for (int i = 0; i < baselineCount; i++)
                baselines[i] = ServerAuthoritativeCanonicalCodec.ReadBaseline(reader.ReadBytesSegment(),
                    elementReader, bodyHashWriter);
            int remoteCount = ReadCount(reader, "remote presentation");
            var remote = new RemotePresentationBatch[remoteCount];
            for (int i = 0; i < remoteCount; i++)
                remote[i] = ReadRemotePresentation(reader.ReadBytesSegment(), elementReader, canonicalScratch, textResolver);
            reader.RequireComplete();
            var result = new AuthorityReplicationBatch(tick, acks, baselines, remote);
            canonicalScratch.Reset();
            WriteAuthorityReplication(canonicalScratch, result);
            RequireCanonical(bytes.Span, canonicalScratch, "authority replication");
            return result;
        }

        static void WriteAck(CanonicalWriter writer, AuthoritativeInputAck ack)
        {
            writer.WriteText(ack.ActorId.Value);
            writer.WriteUInt64(ack.AuthorityTick.Value);
            writer.WriteUInt64(ack.ConfirmedInputSequence);
            writer.WriteUInt64(ack.ConfirmedEventHorizon.Sequence);
            writer.WriteEventId(ack.ConfirmedEventHorizon.EventId);
        }

        static AuthoritativeInputAck ReadAck(CanonicalReader reader)
        {
            var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var tick = new SimulationTick(reader.ReadUInt64());
            ulong inputSequence = reader.ReadUInt64();
            ulong eventSequence = reader.ReadUInt64();
            EventId eventId = reader.ReadEventId();
            if ((eventSequence == 0) != !eventId.IsValid)
                throw new InvalidDataException("Authority input ack Event horizon is invalid.");
            ServerAuthoritativeEventHorizon horizon = eventSequence == 0
                ? ServerAuthoritativeEventHorizon.Empty
                : new ServerAuthoritativeEventHorizon(eventSequence, eventId);
            return new AuthoritativeInputAck(actorId, tick, inputSequence, horizon);
        }

        public static byte[] WriteRemotePresentation(RemotePresentationBatch batch)
        {
            if (!batch.IsValid)
                throw new ArgumentException("Remote presentation batch is invalid.", nameof(batch));
            using var writer = new CanonicalWriter();
            WriteRemotePresentation(writer, batch);
            return writer.ToArray();
        }

        public static byte[] WriteRemoteReliableEvent(ActorId actorId, ServerAuthoritativeReliableEvent value)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("Remote reliable event ActorId is invalid.", nameof(actorId));
            if (value.Header.ActorId != actorId)
                throw new ArgumentException("Remote reliable event belongs to another Actor.", nameof(value));
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(RemoteMagic);
            writer.WriteInt32(RemoteVersion);
            writer.WriteText(actorId.Value);
            writer.WriteBoolean(false);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(1);
            WriteReliableEvent(writer, value);
            return writer.ToArray();
        }

        static void WriteLengthPrefixedRemotePresentation(CanonicalWriter writer, RemotePresentationBatch batch)
        {
            if (!batch.IsValid)
                throw new ArgumentException("Remote presentation batch is invalid.", nameof(batch));
            long prefixPosition = writer.BeginLengthPrefixedBlock();
            WriteRemotePresentation(writer, batch);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        public static void WriteRemotePresentation(CanonicalWriter writer, RemotePresentationBatch batch)
        {
            writer.WriteUInt32(RemoteMagic);
            writer.WriteInt32(RemoteVersion);
            writer.WriteText(batch.ActorId.Value);
            writer.WriteBoolean(batch.ResetBodyStream);
            writer.WriteInt32(batch.BodySamples.Count);
            for (int i = 0; i < batch.BodySamples.Count; i++)
                WriteBodySample(writer, batch.BodySamples[i]);
            writer.WriteInt32(batch.SampleCommands.Count);
            for (int i = 0; i < batch.SampleCommands.Count; i++)
                WritePresentationCommand(writer, batch.SampleCommands[i]);
            writer.WriteInt32(batch.ReliableEvents.Count);
            for (int i = 0; i < batch.ReliableEvents.Count; i++)
                WriteReliableEvent(writer, batch.ReliableEvents[i]);
        }

        public static RemotePresentationBatch ReadRemotePresentation(ReadOnlyMemory<byte> bytes,
            CanonicalReader reader, CanonicalWriter canonicalScratch, ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            if (reader.ReadUInt32() != RemoteMagic || reader.ReadInt32() != RemoteVersion)
                throw new InvalidDataException("ServerAuthoritative remote presentation schema is invalid.");
            return ReadRemotePresentation(reader, bytes.Span, canonicalScratch);
        }

        static RemotePresentationBatch ReadRemotePresentation(ArraySegment<byte> bytes,
            CanonicalReader reader, CanonicalWriter canonicalScratch, ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            if (reader.ReadUInt32() != RemoteMagic || reader.ReadInt32() != RemoteVersion)
                throw new InvalidDataException("ServerAuthoritative remote presentation schema is invalid.");
            return ReadRemotePresentation(reader, bytes.AsSpan(), canonicalScratch);
        }

        static RemotePresentationBatch ReadRemotePresentation(CanonicalReader reader, ReadOnlySpan<byte> source,
            CanonicalWriter canonicalScratch)
        {
            var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            bool resetBodyStream = reader.ReadBoolean();
            int bodyCount = ReadCount(reader, "body sample");
            var bodies = new CharacterBodySample[bodyCount];
            for (int i = 0; i < bodyCount; i++)
                bodies[i] = ReadBodySample(reader);
            int sampleCount = ReadCount(reader, "presentation sample");
            var samples = new PresentationCommand[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                samples[i] = ReadPresentationCommand(reader);
            int eventCount = ReadCount(reader, "reliable event");
            var events = new ServerAuthoritativeReliableEvent[eventCount];
            for (int i = 0; i < eventCount; i++)
                events[i] = ReadReliableEvent(reader);
            reader.RequireComplete();
            var result = new RemotePresentationBatch(actorId, bodies, samples, events, resetBodyStream);
            canonicalScratch.Reset();
            WriteRemotePresentation(canonicalScratch, result);
            RequireCanonical(source, canonicalScratch, "remote presentation");
            return result;
        }

        static void WriteReliableEvent(CanonicalWriter writer, ServerAuthoritativeReliableEvent value)
        {
            writer.WriteBoolean(value.IsGameplay);
            if (value.IsGameplay)
                WriteGameplayFact(writer, value.GameplayFact);
            else
                WritePresentationCommand(writer, value.PresentationCommand);
        }

        static ServerAuthoritativeReliableEvent ReadReliableEvent(CanonicalReader reader) =>
            reader.ReadBoolean()
                ? new ServerAuthoritativeReliableEvent(ReadGameplayFact(reader))
                : new ServerAuthoritativeReliableEvent(ReadPresentationCommand(reader));

        static void WriteGameplayFact(CanonicalWriter writer, GameplayFact fact)
        {
            WriteHeader(writer, fact.Header);
            writer.WriteByte((byte)fact.Kind);
            writer.WriteText(fact.Cause);
            writer.WriteUInt64(fact.MutationCursor);
            switch (fact.Kind)
            {
                case GameplayFactKind.Action:
                    WriteAction(writer, fact.Action);
                    break;
                case GameplayFactKind.ActionWindow:
                    WriteActionWindow(writer, fact.ActionWindow);
                    break;
                case GameplayFactKind.Effect:
                    WriteEffect(writer, fact.Effect);
                    break;
                case GameplayFactKind.Attribute:
                    WriteAttribute(writer, fact.Attribute);
                    break;
                case GameplayFactKind.Cue:
                    WriteCue(writer, fact.Cue);
                    break;
                case GameplayFactKind.Tag:
                    WriteTag(writer, fact.Tag);
                    break;
                case GameplayFactKind.Failure:
                    WriteExecutionFailure(writer, fact.Failure);
                    break;
                case GameplayFactKind.Reconciliation:
                    WriteReconciliation(writer, fact.Reconciliation);
                    WriteOptionalEffectContext(writer, fact.EffectContext);
                    break;
                case GameplayFactKind.Equipment:
                    WriteEquipment(writer, fact.Equipment);
                    break;
                case GameplayFactKind.Motion:
                case GameplayFactKind.State:
                    writer.WriteText(fact.SubjectId);
                    writer.WriteText(fact.StateId);
                    writer.WriteScalar(fact.Scalar);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported GameplayFact kind '{fact.Kind}'.");
            }
        }

        static GameplayFact ReadGameplayFact(CanonicalReader reader)
        {
            SimulationEventHeader header = ReadHeader(reader);
            var kind = (GameplayFactKind)reader.ReadByte();
            string cause = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            ulong mutationCursor = reader.ReadUInt64();
            return kind switch
            {
                GameplayFactKind.Action => new GameplayFact(header, ReadAction(reader)),
                GameplayFactKind.ActionWindow => new GameplayFact(header, ReadActionWindow(reader)),
                GameplayFactKind.Effect => new GameplayFact(header, ReadEffect(reader), cause, mutationCursor),
                GameplayFactKind.Attribute => new GameplayFact(header, ReadAttribute(reader), cause, mutationCursor),
                GameplayFactKind.Cue => new GameplayFact(header, ReadCue(reader), cause, mutationCursor),
                GameplayFactKind.Tag => new GameplayFact(header, ReadTag(reader), cause, mutationCursor),
                GameplayFactKind.Failure => new GameplayFact(header, ReadExecutionFailure(reader), cause, mutationCursor),
                GameplayFactKind.Reconciliation => new GameplayFact(header, ReadReconciliation(reader),
                    ReadOptionalEffectContext(reader), cause, mutationCursor),
                GameplayFactKind.Equipment => new GameplayFact(header, ReadEquipment(reader)),
                GameplayFactKind.Motion or GameplayFactKind.State => new GameplayFact(
                    header,
                    kind,
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    reader.ReadScalar()),
                _ => throw new InvalidDataException($"ServerAuthoritative gameplay fact kind '{(byte)kind}' is invalid.")
            };
        }

        static void WriteEquipment(CanonicalWriter writer, EquipmentLifecycleFact value)
        {
            writer.WriteText(value.SlotId.Value);
            writer.WriteText(value.BeforeEquipmentId.Value);
            writer.WriteText(value.EquipmentId.Value);
            writer.WriteUInt64(value.BeforeRevision);
            writer.WriteUInt64(value.Revision);
            writer.WriteUInt64(value.ChangeId.Value);
            writer.WriteByte((byte)value.State);
        }

        static EquipmentLifecycleFact ReadEquipment(CanonicalReader reader)
        {
            var slot = new EquipmentSlotId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            string before = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            string after = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            ulong beforeRevision = reader.ReadUInt64();
            ulong revision = reader.ReadUInt64();
            var changeId = new EquipmentChangeId(reader.ReadUInt64());
            var state = (PendingEquipmentChangeState)reader.ReadByte();
            if (revision == 0 || state < PendingEquipmentChangeState.Pending || state > PendingEquipmentChangeState.Cancelled)
                throw new InvalidDataException("Equipment lifecycle fact is invalid.");
            return new EquipmentLifecycleFact(slot,
                before.Length == 0 ? default : new EquipmentId(before),
                after.Length == 0 ? default : new EquipmentId(after),
                beforeRevision, revision, changeId, state);
        }

        static void WriteTag(CanonicalWriter writer, GameplayTagRuntimeChange value)
        {
            value.SourceIdentity.Write(writer);
            writer.WriteText(value.TagId);
            writer.WriteInt32(value.BeforeCount);
            writer.WriteInt32(value.AfterCount);
            writer.WriteText(value.CauseEffectId);
            writer.WriteUInt64(value.CauseInstanceId);
            WriteContextIdentity(writer, value.Context);
        }

        static GameplayTagRuntimeChange ReadTag(CanonicalReader reader) => new GameplayTagRuntimeChange(
            GameplayTagSourceIdentityValue.Read(reader), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadInt32(), reader.ReadInt32(),
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadUInt64(), ReadContextIdentity(reader));

        static void WriteExecutionFailure(CanonicalWriter writer, GameplayEffectExecutionFailure value)
        {
            writer.WriteBoolean(value.IsValid);
            if (!value.IsValid)
                return;
            writer.WriteText(value.OwnerEffectId);
            writer.WriteUInt64(value.OwnerInstanceId);
            writer.WriteByte((byte)value.Trigger);
            writer.WriteText(value.RequestedEffectId);
            WriteContextIdentity(writer, value.Context);
            writer.WriteUInt64(value.Tick);
            writer.WriteByte((byte)value.Code);
            writer.WriteText(value.Reason);
            writer.WriteText(value.ParameterId);
            writer.WriteText(value.CapacityDimension);
            writer.WriteInt32(value.Required);
            writer.WriteInt32(value.Available);
        }

        static GameplayEffectExecutionFailure ReadExecutionFailure(CanonicalReader reader) =>
            reader.ReadBoolean() ? new GameplayEffectExecutionFailure(
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadUInt64(), (GameplayEffectLifecycleKind)reader.ReadByte(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), ReadContextIdentity(reader), reader.ReadUInt64(),
                (GameplayEffectApplyResultKind)reader.ReadByte(), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadInt32(), reader.ReadInt32()) : default;

        static void WriteReconciliation(CanonicalWriter writer, GameplayEffectReconciliationResult value)
        {
            writer.WriteByte((byte)value.Kind);
            writer.WriteByte((byte)value.Scope.Kind);
            writer.WriteUInt64(value.Scope.PredictionKey);
            writer.WriteText(value.Scope.EffectId);
            writer.WriteUInt64(value.Scope.PredictedInstanceId);
            writer.WriteUInt64(value.AuthoritativeInstanceId);
            writer.WriteUInt64(value.AuthoritativeRevision);
            writer.WriteInt32(value.RecordCount);
            writer.WriteUInt64(value.PredictedInstanceId);
            writer.WriteUInt64(value.PredictedRevision);
            writer.WriteUInt64(value.CurrentRevision);
            writer.WriteText(value.AttributeId);
            writer.WriteText(value.Reason);
            WriteExecutionFailure(writer, value.Failure);
        }

        static GameplayEffectReconciliationResult ReadReconciliation(CanonicalReader reader) =>
            new GameplayEffectReconciliationResult((GameplayEffectReconciliationResultKind)reader.ReadByte(),
                new GameplayEffectReconciliationScope((GameplayEffectReconciliationScopeKind)reader.ReadByte(),
                    reader.ReadUInt64(), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadUInt64()),
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadInt32(), reader.ReadUInt64(),
                reader.ReadUInt64(), reader.ReadUInt64(), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                ReadExecutionFailure(reader));

        static void WriteContextIdentity(CanonicalWriter writer, GameplayEffectContextIdentity context)
        {
            writer.WriteBoolean(context.IsValid);
            if (!context.IsValid)
                return;
            writer.WriteText(context.SourceActor.Value);
            writer.WriteText(context.TargetActor.Value);
            writer.WriteUInt64(context.SourceActionInstanceId);
            writer.WriteUInt64(context.PredictionKey);
            writer.WriteUInt64(context.GameplayResultId);
            writer.WriteUInt64(context.SourceTick);
            writer.WriteBoolean(context.Predicted);
        }

        static GameplayEffectContextIdentity ReadContextIdentity(CanonicalReader reader) =>
            reader.ReadBoolean() ? new GameplayEffectContextIdentity(new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())), reader.ReadUInt64(), reader.ReadUInt64(),
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadBoolean()) : default;

        static void WritePresentationCommand(CanonicalWriter writer, PresentationCommand command)
        {
            WriteHeader(writer, command.Header);
            writer.WriteByte((byte)command.Kind);
            writer.WriteText(command.ProducerId);
            writer.WriteScalar(command.SampleTime);
            writer.WriteScalar(command.Weight);
            writer.WriteUInt64(command.ProducerGeneration);
            writer.WriteInt32(command.Cycle);
            writer.WriteUInt64(command.SourceActionInstanceId);
            writer.WriteScalar(command.VisualTimeScale);
            if (command.Kind == PresentationCommandKind.TimelineProgress)
                AbilityTimelineProgressCodec.Write(writer, command.TimelineProgress);
        }

        static PresentationCommand ReadPresentationCommand(CanonicalReader reader)
        {
            SimulationEventHeader header = ReadHeader(reader);
            PresentationCommandKind kind = ReadPresentationCommandKind(reader.ReadByte());
            string producerId = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var sampleTime = reader.ReadScalar();
            var weight = reader.ReadScalar();
            ulong generation = reader.ReadUInt64();
            int cycle = reader.ReadInt32();
            ulong actionInstanceId = reader.ReadUInt64();
            var visualTimeScale = reader.ReadScalar();
            AbilityTimelineProgress progress = kind == PresentationCommandKind.TimelineProgress
                ? AbilityTimelineProgressCodec.Read(reader) : default;
            return new PresentationCommand(header, kind, producerId, sampleTime, weight,
                generation, cycle, actionInstanceId, visualTimeScale, timelineProgress: progress);
        }

        static PresentationCommandKind ReadPresentationCommandKind(byte value)
        {
            var kind = (PresentationCommandKind)value;
            return kind switch
            {
                PresentationCommandKind.SelectProducer or
                PresentationCommandKind.SampleProducer or
                PresentationCommandKind.CompleteProducer or
                PresentationCommandKind.ReleaseProducer or
                PresentationCommandKind.Camera or
                PresentationCommandKind.Cue or
                PresentationCommandKind.Vfx or
                PresentationCommandKind.Ui or
                PresentationCommandKind.ForceProducer or
                PresentationCommandKind.DomainEvent or
                PresentationCommandKind.ForceReleaseProducer or
                PresentationCommandKind.TimelineProgress => kind,
                _ => throw new InvalidDataException($"ServerAuthoritative presentation command kind '{value}' is invalid.")
            };
        }

        static void WriteHeader(CanonicalWriter writer, SimulationEventHeader header)
        {
            SimulationNumericProfileCodec.Write(writer, header.NumericProfile);
            writer.WriteEventId(header.EventId);
            writer.WriteText(header.ActorId.Value);
            writer.WriteUInt64(header.Tick.Value);
            SimulationExecutionSourceCodec.Write(writer, header.Activation.Source);
            writer.WriteUInt64(header.Activation.Generation);
            writer.WriteUInt64(header.Sequence);
            writer.WriteText(header.Channel);
        }

        static SimulationEventHeader ReadHeader(CanonicalReader reader) => new SimulationEventHeader(
            SimulationNumericProfileCodec.Read(reader, Float32SimulationNumericProfile.Value),
            reader.ReadEventId(),
            new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
            new SimulationTick(reader.ReadUInt64()),
            new ActivationId(SimulationExecutionSourceCodec.Read(reader, ReadDeclaredIdentity), reader.ReadUInt64()),
            reader.ReadUInt64(),
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));

        static void WriteBodySample(CanonicalWriter writer, CharacterBodySample sample)
        {
            writer.WriteText(sample.ActorId.Value);
            writer.WriteUInt64(sample.Tick.Value);
            WriteBody(writer, sample.BeforeBody);
            WriteBody(writer, sample.FinalBody);
            writer.WriteVector3(sample.AppliedDisplacement);
            writer.WriteScalar(sample.AppliedYawDegrees);
        }

        static CharacterBodySample ReadBodySample(CanonicalReader reader) => new CharacterBodySample(
            new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
            new SimulationTick(reader.ReadUInt64()),
            ReadBody(reader),
            ReadBody(reader),
            reader.ReadVector3(),
            reader.ReadScalar());

        static void WriteBody(CanonicalWriter writer, WorldBodyState body)
        {
            writer.WriteText(body.ActorId.Value);
            writer.WriteVector3(body.Position);
            writer.WriteYaw(body.Yaw);
            writer.WriteVector3(body.Velocity);
            writer.WriteScalar(body.VerticalVelocity);
            writer.WriteBoolean(body.Grounded);
            writer.WriteUInt32((uint)body.Collision);
        }

        static WorldBodyState ReadBody(CanonicalReader reader) => new WorldBodyState(
            new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
            reader.ReadVector3(),
            reader.ReadYaw(),
            reader.ReadVector3(),
            reader.ReadScalar(),
            reader.ReadBoolean(),
            (WorldCollisionSummary)reader.ReadUInt32());

        static void WriteAction(CanonicalWriter writer, ActionFact value)
        {
            writer.WriteUInt64(value.ActionInstanceId);
            writer.WriteUInt64(value.PredictionKey);
            writer.WriteUInt64(value.InputSequence);
            writer.WriteText(value.ActionId);
            writer.WriteText(value.SkillId.Value ?? string.Empty);
            writer.WriteByte((byte)value.TransitionType);
            writer.WriteByte((byte)value.Phase);
            writer.WriteByte((byte)value.State);
            writer.WriteText(value.Reason);
        }

        static ActionFact ReadAction(CanonicalReader reader) => new ActionFact(
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
            ReadOptionalSkillId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
            ReadActionTransition(reader.ReadByte()),
            ReadActionPhase(reader.ReadByte()),
            ReadActionState(reader.ReadByte()),
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
            default);

        static CharacterSkillId ReadOptionalSkillId(string value) =>
            CanonicalTextValue.IsNullOrEmpty(value) ? default : new CharacterSkillId(value);

        static void WriteActionWindow(CanonicalWriter writer, ActionWindowFact value)
        {
            writer.WriteText(value.SourceContext.SkillId.Value ?? string.Empty);
            writer.WriteUInt64(value.SourceContext.InstanceId);
            writer.WriteUInt64(value.SourceContext.SkillExecutionGeneration);
            writer.WriteUInt64(value.SourceContext.SegmentGeneration);
            writer.WriteText(value.ActionId);
            writer.WriteText(value.WindowId);
            writer.WriteText(value.WindowType);
            writer.WriteText(value.WindowTag);
            writer.WriteUInt64(value.StartTick);
            writer.WriteUInt64(value.EndTick);
            writer.WriteUInt64(value.Digest);
        }

        static ActionWindowFact ReadActionWindow(CanonicalReader reader) => new ActionWindowFact(
            new GameplayAbilitySourceContext(
                ReadOptionalSkillId(ReadDeclaredIdentity(reader)),
                reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64()),
            ReadDeclaredIdentity(reader),
            ReadDeclaredIdentity(reader),
            ReadDeclaredIdentity(reader),
            ReadDeclaredIdentity(reader),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadUInt64());

        static void WriteEffect(CanonicalWriter writer, GameplayEffectFact value)
        {
            writer.WriteText(value.EffectId);
            writer.WriteUInt64(value.InstanceId);
            writer.WriteByte((byte)value.Operation);
            WriteEffectContext(writer, value.Context);
            writer.WriteUInt64(value.StartTick);
            writer.WriteUInt64(value.EndTick);
            writer.WriteInt32(value.StackCount);
            writer.WriteUInt64(value.LifecycleRevision);
            writer.WriteUInt32(value.DefinitionRevision);
            writer.WriteBoolean(value.Instant);
        }

        static GameplayEffectFact ReadEffect(CanonicalReader reader) => new GameplayEffectFact(
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
            reader.ReadUInt64(),
            ReadEffectOperation(reader.ReadByte()),
            ReadEffectContext(reader),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadInt32(),
            reader.ReadUInt64(),
            reader.ReadUInt32(),
            reader.ReadBoolean());

        static void WriteAttribute(CanonicalWriter writer, GameplayAttributeFact value)
        {
            writer.WriteText(value.AttributeId);
            writer.WriteScalar(value.BeforeBase);
            writer.WriteScalar(value.BaseValue);
            writer.WriteScalar(value.BeforeCurrent);
            writer.WriteScalar(value.CurrentValue);
            writer.WriteUInt64(value.ValueRevision);
            writer.WriteText(value.CauseEffectId);
            writer.WriteUInt64(value.CauseEffectInstanceId);
            WriteOptionalEffectContext(writer, value.CauseContext);
        }

        static GameplayAttributeFact ReadAttribute(CanonicalReader reader) => new GameplayAttributeFact(
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
            reader.ReadScalar(),
            reader.ReadScalar(),
            reader.ReadScalar(),
            reader.ReadScalar(),
            reader.ReadUInt64(),
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
            reader.ReadUInt64(),
            ReadOptionalEffectContext(reader));

        static void WriteCue(CanonicalWriter writer, GameplayCueFact value)
        {
            writer.WriteText(value.CueId);
            writer.WriteText(value.TriggerId);
            writer.WriteText(value.SourceId);
            writer.WriteUInt64(value.SourceInstanceId);
            WriteOptionalEffectContext(writer, value.EffectContext);
        }

        static GameplayCueFact ReadCue(CanonicalReader reader) => new GameplayCueFact(
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
            CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
            reader.ReadUInt64(),
            ReadOptionalEffectContext(reader));

        static void WriteOptionalEffectContext(CanonicalWriter writer, SimulationGameplayEffectContext context)
        {
            writer.WriteBoolean(context.IsValid);
            if (context.IsValid)
                WriteEffectContext(writer, context);
        }

        static SimulationGameplayEffectContext ReadOptionalEffectContext(CanonicalReader reader) =>
            reader.ReadBoolean() ? ReadEffectContext(reader) : default;

        static void WriteEffectContext(CanonicalWriter writer, SimulationGameplayEffectContext context)
        {
            if (!context.IsValid)
                throw new InvalidDataException("Gameplay Effect context is invalid.");
            writer.WriteText(context.SourceActorId.Value);
            writer.WriteText(context.TargetActorId.Value);
            writer.WriteUInt64(context.SourceActionInstanceId);
            writer.WriteUInt64(context.PredictionKey);
            writer.WriteUInt64(context.GameplayResultId);
            writer.WriteUInt64(context.SourceTick);
            writer.WriteByte((byte)context.ApplicationMode);
        }

        static SimulationGameplayEffectContext ReadEffectContext(CanonicalReader reader) => new SimulationGameplayEffectContext(
            new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
            new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            ReadEffectApplicationMode(reader.ReadByte()));

        static int ReadCount(CanonicalReader reader, string label)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumCount)
                throw new InvalidDataException($"ServerAuthoritative {label} count '{count}' is invalid.");
            return count;
        }

        static void RequireCanonical(ReadOnlySpan<byte> source, CanonicalWriter canonical, string label)
        {
            if (canonical == null || !canonical.ContentEquals(source))
                throw new InvalidDataException($"ServerAuthoritative {label} payload is not canonical.");
        }

        static SimulationActionLifecycleTransitionType ReadActionTransition(byte value) =>
            (SimulationActionLifecycleTransitionType)value switch
            {
                SimulationActionLifecycleTransitionType.None or
                SimulationActionLifecycleTransitionType.Confirm or
                SimulationActionLifecycleTransitionType.Complete or
                SimulationActionLifecycleTransitionType.Cancel or
                SimulationActionLifecycleTransitionType.Interrupt or
                SimulationActionLifecycleTransitionType.Reject or
                SimulationActionLifecycleTransitionType.Correct or
                SimulationActionLifecycleTransitionType.Abort => (SimulationActionLifecycleTransitionType)value,
                _ => throw new InvalidDataException($"ServerAuthoritative action transition '{value}' is invalid.")
            };

        static SimulationActionPhase ReadActionPhase(byte value) =>
            (SimulationActionPhase)value switch
            {
                SimulationActionPhase.Startup or
                SimulationActionPhase.Active or
                SimulationActionPhase.Recovery or
                SimulationActionPhase.Cancel or
                SimulationActionPhase.Ended => (SimulationActionPhase)value,
                _ => throw new InvalidDataException($"ServerAuthoritative action phase '{value}' is invalid.")
            };

        static SimulationActionState ReadActionState(byte value) =>
            (SimulationActionState)value switch
            {
                SimulationActionState.Requested or
                SimulationActionState.Predicted or
                SimulationActionState.Confirmed or
                SimulationActionState.Rejected or
                SimulationActionState.Cancelled or
                SimulationActionState.Interrupted or
                SimulationActionState.Aborted or
                SimulationActionState.Ended or
                SimulationActionState.Corrected => (SimulationActionState)value,
                _ => throw new InvalidDataException($"ServerAuthoritative action state '{value}' is invalid.")
            };

        static SimulationGameplayEffectLifecycleOperation ReadEffectOperation(byte value) =>
            (SimulationGameplayEffectLifecycleOperation)value switch
            {
                SimulationGameplayEffectLifecycleOperation.Applied or
                SimulationGameplayEffectLifecycleOperation.Confirmed or
                SimulationGameplayEffectLifecycleOperation.Rejected or
                SimulationGameplayEffectLifecycleOperation.StackChanged or
                SimulationGameplayEffectLifecycleOperation.Inhibited or
                SimulationGameplayEffectLifecycleOperation.Resumed or
                SimulationGameplayEffectLifecycleOperation.PeriodExecuted or
                SimulationGameplayEffectLifecycleOperation.Removed or
                SimulationGameplayEffectLifecycleOperation.Expired or
                SimulationGameplayEffectLifecycleOperation.Corrected or
                SimulationGameplayEffectLifecycleOperation.Overflow => (SimulationGameplayEffectLifecycleOperation)value,
                _ => throw new InvalidDataException($"ServerAuthoritative effect operation '{value}' is invalid.")
            };

        static SimulationGameplayEffectApplicationMode ReadEffectApplicationMode(byte value) =>
            (SimulationGameplayEffectApplicationMode)value switch
            {
                SimulationGameplayEffectApplicationMode.Confirmed or
                SimulationGameplayEffectApplicationMode.Predicted => (SimulationGameplayEffectApplicationMode)value,
                _ => throw new InvalidDataException($"ServerAuthoritative effect application mode '{value}' is invalid.")
            };
    }
}
