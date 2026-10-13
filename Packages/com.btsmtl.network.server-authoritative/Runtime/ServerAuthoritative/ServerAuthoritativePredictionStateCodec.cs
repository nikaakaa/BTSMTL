using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    internal static class ServerAuthoritativePredictionStateCodec
    {
        const uint CorrectionMagic = 0x43524153;
        const int CorrectionVersion = 6;
        const uint HistoryMagic = 0x48524153;
        const int HistoryVersion = 7;
        const uint JournalMagic = 0x4a524153;
        const int MaximumObservedActorCount = 64;
        const int MaximumProjectedParticipantCount = 64;
        internal const int MaximumHistorySnapshotTextValues = 6 + 4 + 4 * MaximumProjectedParticipantCount;
        static readonly Comparison<ServerAuthoritativeJournalEntry> s_CompareJournalEntries =
            (left, right) => left.EventId.CompareTo(right.EventId);

        public static byte[] WriteCorrection(ServerAuthoritativePredictionCorrectionCheckpoint checkpoint)
        {
            using var writer = new CanonicalWriter();
            WriteCorrection(writer, checkpoint.Data);
            return writer.ToArray();
        }

        internal static void WriteCorrection(CanonicalWriter writer,
            ServerAuthoritativePredictionConfirmationData state)
        {
            writer.WriteUInt32(CorrectionMagic);
            writer.WriteInt32(CorrectionVersion);
            writer.WriteHash(ServerAuthoritativePredictionConfirmationData.CanonicalSchemaHash);
            ServerAuthoritativePredictionConfirmationData.CanonicalWrite(writer, state);
        }

        public static void ReadCorrection(CanonicalReader reader,
            ServerAuthoritativePredictionCorrectionCheckpoint checkpoint)
        {
            RequireHeader(reader, CorrectionMagic, CorrectionVersion);
            ServerAuthoritativePredictionConfirmationData.CanonicalRequireSchema(reader);
            ServerAuthoritativePredictionConfirmationData.CanonicalReadInto(reader, checkpoint.Data);
            reader.RequireComplete();
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
                    CharacterBodySample.CanonicalWrite(writer, actor.Samples[sampleIndex]);
            }
            return writer.ToArray();
        }

        internal static void WriteHistoryHeader(CanonicalWriter writer, int count)
        {
            writer.WriteUInt32(HistoryMagic);
            writer.WriteInt32(HistoryVersion);
            writer.WriteHash(OwnerCanonicalInputHeader.CanonicalSchemaHash);
            writer.WriteHash(ObservedWorldConstraint.CanonicalSchemaHash);
            writer.WriteHash(SimulationPipelineIdentity.CanonicalSchemaHash);
            writer.WriteHash(SimulationComponentIdentity.CanonicalSchemaHash);
            writer.WriteHash(SimulationPipelineStateParticipantIdentity.CanonicalSchemaHash);
            writer.WriteInt32(count);
        }

        internal static void WriteHistoryRecord(CanonicalWriter writer, ServerAuthoritativePredictionHistoryRecord record)
        {
            writer.WriteUInt64(record.Tick.Value);
            OwnerCanonicalInputHeader.CanonicalWrite(writer, record.Input.Header);
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
            OwnerCanonicalInputHeader.CanonicalRequireSchema(reader);
            ObservedWorldConstraint.CanonicalRequireSchema(reader);
            SimulationPipelineIdentity.CanonicalRequireSchema(reader);
            SimulationComponentIdentity.CanonicalRequireSchema(reader);
            SimulationPipelineStateParticipantIdentity.CanonicalRequireSchema(reader);
            int count = RequireCount(reader.ReadInt32(), historyCapacity);
            var records = new KeyValuePair<ulong, ServerAuthoritativePredictionHistoryRecord>[count];
            for (int i = 0; i < count; i++)
            {
                var tick = new SimulationTick(reader.ReadUInt64());
                OwnerCanonicalInputHeader inputHeader = OwnerCanonicalInputHeader.CanonicalRead(reader);
                SimulationInput input = ServerAuthoritativeCanonicalCodec.ReadInput(reader.ReadBytesSegment(), inputReader,
                    reader.TextResolver);
                var ownerInput = new OwnerCanonicalInputBatch(in inputHeader, input);
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
                    samples[sampleIndex] = CharacterBodySample.CanonicalRead(reader);
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
            WriteJournalHeader(writer, checkpoint.Cursor, checkpoint.Count);
            ReadOnlySpan<ServerAuthoritativeJournalEntry> entries = checkpoint.Entries;
            for (int i = 0; i < entries.Length; i++)
                ServerAuthoritativeJournalEntry.CanonicalWrite(writer, in entries[i]);
            return writer.ToArray();
        }

        internal static void WriteJournalHeader(CanonicalWriter writer, ulong cursor, int count)
        {
            writer.WriteUInt32(JournalMagic);
            writer.WriteInt32(ServerAuthoritativePredictionPassIds.JournalStateSchemaVersion);
            writer.WriteHash(ServerAuthoritativeJournalEntry.CanonicalSchemaHash);
            writer.WriteUInt64(cursor);
            writer.WriteInt32(count);
        }

        public static void ReadJournal(
            CanonicalReader reader,
            ServerAuthoritativePredictionJournalCheckpoint checkpoint)
        {
            RequireHeader(reader, JournalMagic, ServerAuthoritativePredictionPassIds.JournalStateSchemaVersion);
            ServerAuthoritativeJournalEntry.CanonicalRequireSchema(reader);
            checkpoint.Cursor = reader.ReadUInt64();
            int count = RequireCount(reader.ReadInt32(), checkpoint.Buffer.Length);
            checkpoint.Count = count;
            ServerAuthoritativeJournalEntry[] entries = checkpoint.Buffer;
            for (int i = 0; i < count; i++)
                entries[i] = ServerAuthoritativeJournalEntry.CanonicalRead(reader);
            SimulationArraySort.SortPrefix(entries, count, s_CompareJournalEntries);
            for (int i = 1; i < count; i++)
            {
                if (entries[i - 1].EventId.Equals(entries[i].EventId))
                    throw new InvalidDataException("Prediction disposition journal payload contains a duplicate EventId.");
            }
            reader.RequireComplete();
        }

        static void WriteObservedFrame(CanonicalWriter writer, ObservedWorldConstraintFrame frame)
        {
            writer.WriteUInt64(frame.Tick.Value);
            writer.WriteHash(frame.FrameHash);
            writer.WriteInt32(frame.Constraints.Count);
            for (int i = 0; i < frame.Constraints.Count; i++)
                ObservedWorldConstraint.CanonicalWrite(writer, frame.Constraints[i]);
        }

        static ObservedWorldConstraintFrame ReadObservedFrame(CanonicalReader reader)
        {
            var tick = new SimulationTick(reader.ReadUInt64());
            var expectedHash = reader.ReadHash();
            int count = RequireCount(reader.ReadInt32(), MaximumObservedActorCount);
            var constraints = new ObservedWorldConstraint[count];
            for (int i = 0; i < count; i++)
            {
                constraints[i] = ObservedWorldConstraint.CanonicalRead(reader);
            }
            var frame = new ObservedWorldConstraintFrame(tick, constraints);
            if (frame.FrameHash != expectedHash)
                throw new InvalidDataException("Prediction history observed frame hash does not match its canonical value.");
            return frame;
        }

        static void WritePipelineProjection(CanonicalWriter writer, SimulationPipelineStateSnapshot snapshot)
        {
            SimulationPipelineIdentity.CanonicalWrite(writer, snapshot.Pipeline);
            SimulationComponentIdentity.CanonicalWrite(writer, snapshot.Backend);
            writer.WriteUInt64(snapshot.LastCompletedTick);
            writer.WriteInt32(snapshot.Participants.Count);
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                SimulationPipelinePassStateSnapshot participant = snapshot.Participants[i];
                SimulationPipelineStateParticipantIdentity.CanonicalWrite(writer, in participant.Identity);
                writer.WriteHash(participant.StateHash);
                writer.WriteBytes(participant.Payload.Span);
            }
        }

        static SimulationPipelineStateSnapshot ReadPipelineProjection(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            SimulationPipelineIdentity pipeline = SimulationPipelineIdentity.CanonicalReadMaterialized(reader);
            SimulationComponentIdentity backend = SimulationComponentIdentity.CanonicalReadMaterialized(reader);
            ulong tick = reader.ReadUInt64();
            int count = RequireCount(reader.ReadInt32(), MaximumProjectedParticipantCount);
            var participants = new SimulationPipelinePassStateSnapshot[count];
            for (int i = 0; i < count; i++)
            {
                SimulationPipelineStateParticipantIdentity identity =
                    SimulationPipelineStateParticipantIdentity.CanonicalReadMaterialized(reader);
                participants[i] = SimulationPipelinePassStateSnapshot.FromWirePayload(
                    in identity, reader.ReadHash(), reader.ReadBytes());
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
