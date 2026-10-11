using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    internal static class ServerAuthoritativePredictionStateCodec
    {
        const uint CorrectionMagic = 0x43524153;
        const int CorrectionVersion = 5;
        const uint HistoryMagic = 0x48524153;
        const int HistoryVersion = 4;
        const uint JournalMagic = 0x4a524153;
        const int MaximumObservedActorCount = 64;
        const int MaximumProjectedParticipantCount = 64;
        internal const int MaximumHistoryRecordTextValues =
            1 + ServerAuthoritativeCanonicalCodec.MaximumInputTextValues + 6 +
            4 + 4 * MaximumProjectedParticipantCount + 3 * MaximumObservedActorCount;
        static readonly Comparison<SimulationInputRequest> s_CompareRequestsBySequence =
            (left, right) => left.Sequence.CompareTo(right.Sequence);
        static readonly Comparison<KeyValuePair<EventId, ServerAuthoritativeJournalEntry>> s_CompareJournalEntries =
            (left, right) => left.Key.CompareTo(right.Key);

        public static byte[] WriteCorrection(ServerAuthoritativePredictionCorrectionCheckpoint checkpoint)
        {
            using var writer = new CanonicalWriter();
            WriteCorrection(writer, checkpoint.ConfirmedInputSequence, checkpoint.ConfirmedEventHorizon,
                checkpoint.LastAuthorityAckTick, checkpoint.LastBaselineTick, checkpoint.LastAuthorityClockEstimate,
                checkpoint.PendingRequests, checkpoint.PendingRequests.Count);
            return writer.ToArray();
        }

        internal static void WriteCorrection(CanonicalWriter writer, ulong confirmedInputSequence,
            ServerAuthoritativeEventHorizon horizon, ulong authorityAck, ulong baselineTick, ulong clockEstimate,
            IReadOnlyList<SimulationInputRequest> requests, int count)
        {
            writer.WriteUInt32(CorrectionMagic);
            writer.WriteInt32(CorrectionVersion);
            writer.WriteUInt64(confirmedInputSequence);
            writer.WriteUInt64(horizon.Sequence);
            writer.WriteEventId(horizon.EventId);
            writer.WriteUInt64(authorityAck);
            writer.WriteUInt64(baselineTick);
            writer.WriteUInt64(clockEstimate);
            writer.WriteInt32(count);
            for (int i = 0; i < count; i++)
            {
                SimulationInputRequest request = requests[i];
                writer.WriteText(request.RequestId);
                writer.WriteUInt64(request.Sequence);
                writer.WriteUInt64(request.SourceTick);
                writer.WriteUInt64(request.ExpireSimulationTick);
                writer.WriteInt32(request.Priority);
            }
        }

        public static ServerAuthoritativePredictionCorrectionCheckpoint ReadCorrection(
            CanonicalReader reader,
            int requestCapacity)
        {
            RequireHeader(reader, CorrectionMagic, CorrectionVersion);
            ulong confirmedInputSequence = reader.ReadUInt64();
            ulong horizonSequence = reader.ReadUInt64();
            EventId eventId = reader.ReadEventId();
            ServerAuthoritativeEventHorizon horizon = horizonSequence == 0
                ? ServerAuthoritativeEventHorizon.Empty
                : new ServerAuthoritativeEventHorizon(horizonSequence, eventId);
            ulong lastAuthorityAckTick = reader.ReadUInt64();
            ulong lastBaselineTick = reader.ReadUInt64();
            ulong lastAuthorityClockEstimate = reader.ReadUInt64();
            int pendingCount = RequireCount(reader.ReadInt32(), requestCapacity);
            var pending = new SimulationInputRequest[pendingCount];
            for (int i = 0; i < pendingCount; i++)
            {
                var request = new SimulationInputRequest(
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    reader.ReadUInt64(),
                    reader.ReadUInt64(),
                    reader.ReadUInt64(),
                    reader.ReadInt32());
                pending[i] = request;
            }
            Array.Sort(pending, s_CompareRequestsBySequence);
            for (int i = 1; i < pending.Length; i++)
            {
                if (pending[i - 1].Sequence == pending[i].Sequence)
                    throw new InvalidDataException("Prediction pending request payload contains a duplicate sequence.");
            }
            reader.RequireComplete();
            return new ServerAuthoritativePredictionCorrectionCheckpoint(
                confirmedInputSequence,
                horizon,
                lastAuthorityAckTick,
                lastBaselineTick,
                lastAuthorityClockEstimate,
                pending);
        }

        public static byte[] WriteHistory(ServerAuthoritativePredictionHistoryCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            using var writer = new CanonicalWriter();
            WriteHistoryHeader(writer, checkpoint.Records.Count);
            for (int i = 0; i < checkpoint.Records.Count; i++)
            {
                ServerAuthoritativePredictionHistoryRecord record = checkpoint.Records[i].Value;
                WriteHistoryRecord(writer, record);
            }
            writer.WriteInt32(checkpoint.RemoteBodies.Actors.Count);
            for (int actorIndex = 0; actorIndex < checkpoint.RemoteBodies.Actors.Count; actorIndex++)
            {
                ServerAuthoritativeRemoteBodyActorCheckpoint actor = checkpoint.RemoteBodies.Actors[actorIndex];
                writer.WriteText(actor.ActorId.Value);
                writer.WriteInt32(actor.Samples.Count);
                for (int sampleIndex = 0; sampleIndex < actor.Samples.Count; sampleIndex++)
                    WriteBodySample(writer, actor.Samples[sampleIndex]);
            }
            return writer.ToArray();
        }

        internal static void WriteHistoryHeader(CanonicalWriter writer, int count)
        {
            writer.WriteUInt32(HistoryMagic);
            writer.WriteInt32(HistoryVersion);
            writer.WriteInt32(count);
        }

        internal static void WriteHistoryRecord(CanonicalWriter writer, ServerAuthoritativePredictionHistoryRecord record)
        {
            writer.WriteUInt64(record.Tick.Value);
            writer.WriteText(record.Input.ActorId.Value);
            writer.WriteUInt64(record.Input.SourceTick);
            writer.WriteUInt64(record.Input.InputSequence);
            ServerAuthoritativeCanonicalCodec.WriteLengthPrefixedInput(writer, record.Input.Input);
            writer.WriteHash(record.CompositionIdentity);
            SimulationWorldSnapshotCodec.WriteLengthPrefixed(writer, record.World);
            long pipelinePrefix = writer.BeginLengthPrefixedBlock();
            WritePipelineProjection(writer, record.PipelineProjection);
            writer.EndLengthPrefixedBlock(pipelinePrefix);
            WriteObservedFrame(writer, record.ObservedWorldConstraints);
            writer.WriteUInt64(record.JournalCursor);
        }

        public static ServerAuthoritativePredictionHistoryCheckpoint ReadHistory(
            CanonicalReader reader, CanonicalReader inputReader, CanonicalWriter canonicalScratch,
            int historyCapacity)
        {
            RequireHeader(reader, HistoryMagic, HistoryVersion);
            int count = RequireCount(reader.ReadInt32(), historyCapacity);
            var records = new KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>[count];
            for (int i = 0; i < count; i++)
            {
                var tick = new SimulationTick(reader.ReadUInt64());
                var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                ulong sourceTick = reader.ReadUInt64();
                ulong inputSequence = reader.ReadUInt64();
                SimulationInput input = ServerAuthoritativeCanonicalCodec.ReadInput(reader.ReadBytesSegment(), inputReader,
                    reader.TextResolver);
                var ownerInput = new OwnerCanonicalInputBatch(actorId, sourceTick, inputSequence, input);
                var composition = SimulationSessionCompositionIdentity.CanonicalFromHash(reader.ReadHash());
                SimulationWorldSnapshot world = SimulationWorldSnapshotCodec.Read(reader.ReadBytes());
                SimulationPipelineStateSnapshot pipeline = ReadPipelineProjection(reader.ReadBytes());
                ObservedWorldConstraintFrame observed = ReadObservedFrame(reader);
                ulong journalCursor = reader.ReadUInt64();
                var record = new ServerAuthoritativePredictionHistoryRecord(
                    ownerInput,
                    composition,
                    world,
                    pipeline,
                    observed,
                    journalCursor);
                if (record.Tick != tick || i > 0 && records[i - 1].Key >= tick.Value)
                    throw new InvalidDataException("Prediction history payload Tick order is invalid.");
                records[i] = new KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>(tick.Value, record);
            }
            int actorCount = RequireCount(reader.ReadInt32(), 64);
            var remoteActors = new ServerAuthoritativeRemoteBodyActorCheckpoint[actorCount];
            for (int actorIndex = 0; actorIndex < actorCount; actorIndex++)
            {
                var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                int sampleCount = RequireCount(reader.ReadInt32(), checked(historyCapacity * 4));
                var samples = new CharacterBodySample[sampleCount];
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    samples[sampleIndex] = ReadBodySample(reader);
                remoteActors[actorIndex] = new ServerAuthoritativeRemoteBodyActorCheckpoint(actorId, samples);
            }
            reader.RequireComplete();
            return new ServerAuthoritativePredictionHistoryCheckpoint(
                records,
                new ServerAuthoritativeRemoteBodyTimelineCheckpoint(remoteActors));
        }

        public static byte[] WriteJournal(ServerAuthoritativePredictionJournalCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            using var writer = new CanonicalWriter();
            WriteJournalHeader(writer, checkpoint.Cursor, checkpoint.Entries.Count);
            for (int i = 0; i < checkpoint.Entries.Count; i++)
            {
                KeyValuePair<EventId, ServerAuthoritativeJournalEntry> pair = checkpoint.Entries[i];
                WriteJournalEntry(writer, pair.Key, pair.Value);
            }
            return writer.ToArray();
        }

        internal static void WriteJournalHeader(CanonicalWriter writer, ulong cursor, int count)
        {
            writer.WriteUInt32(JournalMagic);
            writer.WriteInt32(ServerAuthoritativePredictionPassIds.JournalStateSchemaVersion);
            writer.WriteUInt64(cursor);
            writer.WriteInt32(count);
        }

        internal static void WriteJournalEntry(CanonicalWriter writer, EventId key, ServerAuthoritativeJournalEntry entry)
        {
            writer.WriteEventId(key);
            writer.WriteUInt64(entry.Tick.Value);
            writer.WriteUInt64(entry.Sequence);
            writer.WriteByte((byte)entry.Disposition);
        }

        public static ServerAuthoritativePredictionJournalCheckpoint ReadJournal(
            CanonicalReader reader,
            int historyCapacity)
        {
            RequireHeader(reader, JournalMagic, ServerAuthoritativePredictionPassIds.JournalStateSchemaVersion);
            ulong cursor = reader.ReadUInt64();
            int count = RequireCount(reader.ReadInt32(), checked(historyCapacity * 64));
            var entries = new KeyValuePair<EventId, ServerAuthoritativeJournalEntry>[count];
            for (int i = 0; i < count; i++)
            {
                var eventId = reader.ReadEventId();
                var tick = new SimulationTick(reader.ReadUInt64());
                ulong sequence = reader.ReadUInt64();
                var disposition = (ServerAuthoritativeEventDisposition)reader.ReadByte();
                if (!eventId.IsValid || !tick.IsValid || sequence == 0 ||
                    !ServerAuthoritativeJournalEntry.IsValidDisposition(disposition))
                {
                    throw new InvalidDataException("Prediction disposition journal payload is invalid.");
                }
                entries[i] = new KeyValuePair<EventId, ServerAuthoritativeJournalEntry>(
                    eventId,
                    new ServerAuthoritativeJournalEntry(eventId, tick, sequence, disposition));
            }
            Array.Sort(entries, s_CompareJournalEntries);
            for (int i = 1; i < entries.Length; i++)
            {
                if (entries[i - 1].Key.Equals(entries[i].Key))
                    throw new InvalidDataException("Prediction disposition journal payload contains a duplicate EventId.");
            }
            reader.RequireComplete();
            return new ServerAuthoritativePredictionJournalCheckpoint(entries, cursor, 0);
        }

        static void WriteObservedFrame(CanonicalWriter writer, ObservedWorldConstraintFrame frame)
        {
            writer.WriteUInt64(frame.Tick.Value);
            writer.WriteHash(frame.FrameHash);
            writer.WriteInt32(frame.Constraints.Count);
            for (int i = 0; i < frame.Constraints.Count; i++)
            {
                ObservedWorldConstraint value = frame.Constraints[i];
                writer.WriteText(value.ActorId.Value);
                writer.WriteUInt64(value.TargetTick.Value);
                WriteBody(writer, value.BeforeBody);
                WriteBody(writer, value.FinalBody);
                writer.WriteUInt64(value.SourcePreviousTick.Value);
                writer.WriteUInt64(value.SourceCurrentTick.Value);
                writer.WriteByte((byte)value.SamplingKind);
                writer.WriteHash(value.ContactShapeConfigurationHash);
            }
        }

        static ObservedWorldConstraintFrame ReadObservedFrame(CanonicalReader reader)
        {
            var tick = new SimulationTick(reader.ReadUInt64());
            var expectedHash = reader.ReadHash();
            int count = RequireCount(reader.ReadInt32(), MaximumObservedActorCount);
            var constraints = new ObservedWorldConstraint[count];
            for (int i = 0; i < count; i++)
            {
                constraints[i] = new ObservedWorldConstraint(
                    new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    new SimulationTick(reader.ReadUInt64()),
                    ReadBody(reader),
                    ReadBody(reader),
                    new SimulationTick(reader.ReadUInt64()),
                    new SimulationTick(reader.ReadUInt64()),
                    (ObservedWorldConstraintSamplingKind)reader.ReadByte(),
                    reader.ReadHash());
            }
            var frame = new ObservedWorldConstraintFrame(tick, constraints);
            if (frame.FrameHash != expectedHash)
                throw new InvalidDataException("Prediction history observed frame hash does not match its canonical value.");
            return frame;
        }

        internal static void WriteBodySample(CanonicalWriter writer, CharacterBodySample sample)
        {
            writer.WriteText(sample.ActorId.Value);
            writer.WriteUInt64(sample.Tick.Value);
            WriteBody(writer, sample.BeforeBody);
            WriteBody(writer, sample.FinalBody);
            writer.WriteVector3(sample.AppliedDisplacement);
            writer.WriteScalar(sample.AppliedYawDegrees);
        }

        static CharacterBodySample ReadBodySample(CanonicalReader reader)
        {
            var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            return new CharacterBodySample(
                actorId,
                new SimulationTick(reader.ReadUInt64()),
                ReadBody(reader),
                ReadBody(reader),
                reader.ReadVector3(),
                reader.ReadScalar());
        }

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

        static void WritePipelineProjection(CanonicalWriter writer, SimulationPipelineStateSnapshot snapshot)
        {
            writer.WriteText(snapshot.Pipeline.Id.Value);
            writer.WriteText(snapshot.Pipeline.Revision.Value);
            writer.WriteInt32(snapshot.Pipeline.SchemaVersion.Value);
            writer.WriteHash(snapshot.Pipeline.Hash);
            writer.WriteByte((byte)snapshot.Backend.Role);
            writer.WriteText(snapshot.Backend.ComponentId);
            writer.WriteText(snapshot.Backend.SemanticVersion);
            writer.WriteHash(snapshot.Backend.ConfigurationHash);
            writer.WriteUInt64(snapshot.LastCompletedTick);
            writer.WriteInt32(snapshot.Participants.Count);
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                SimulationPipelinePassStateSnapshot participant = snapshot.Participants[i];
                writer.WriteText(participant.PassId.Value);
                writer.WriteText(participant.ImplementationVersion.Value);
                writer.WriteText(participant.StateOwner);
                writer.WriteText(participant.StateSchemaId);
                writer.WriteInt32(participant.StateSchemaVersion);
                writer.WriteHash(participant.StateHash);
                writer.WriteBytes(participant.Payload.Span);
            }
        }

        static SimulationPipelineStateSnapshot ReadPipelineProjection(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            var pipeline = new SimulationPipelineIdentity(
                new SimulationPipelineId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                new SimulationPipelineRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                new SimulationPipelineSchemaVersion(reader.ReadInt32()),
                SimulationPipelineHash.CanonicalFromHash(reader.ReadHash()));
            var backend = new SimulationComponentIdentity(
                (SimulationComponentRole)reader.ReadByte(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                reader.ReadHash());
            ulong tick = reader.ReadUInt64();
            int count = RequireCount(reader.ReadInt32(), MaximumProjectedParticipantCount);
            var participants = new SimulationPipelinePassStateSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                participants[i] = SimulationPipelinePassStateSnapshot.FromWirePayload(
                    new SimulationPipelinePassId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    new SimulationPipelinePassImplementationVersion(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    reader.ReadInt32(),
                    reader.ReadHash(),
                    reader.ReadBytes());
            }
            reader.RequireComplete();
            return SimulationPipelineStateSnapshot.FromOwnedParticipants(pipeline, backend, tick, participants);
        }

        static void RequireHeader(CanonicalReader reader, uint magic, int version)
        {
            if (reader.ReadUInt32() != magic || reader.ReadInt32() != version)
                throw new InvalidDataException("ServerAuthoritative prediction state header is invalid.");
        }

        static int RequireCount(int count, int maximum)
        {
            if (count < 0 || count > maximum)
                throw new InvalidDataException("ServerAuthoritative prediction state count is invalid.");
            return count;
        }
    }
}
