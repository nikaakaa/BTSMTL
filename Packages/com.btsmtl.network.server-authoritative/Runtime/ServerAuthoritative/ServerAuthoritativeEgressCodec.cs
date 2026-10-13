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
        public const string ClientInputSchema = "server-authoritative-client-input/4|header:" +
            OwnerCanonicalInputHeader.CanonicalSchema + "|input:" + Float32SimulationInputCodec.Schema;
        public const string AuthorityReplicationSchema = "server-authoritative-authority-replication/14|tag-source:" + GameplayTagSourceIdentityValue.CanonicalSchema + "|effect-context:" + SimulationGameplayEffectContext.CanonicalSchema + "|lifecycle:" + GameplayEffectReconciliationResult.CanonicalSchema + "|source:" + SimulationExecutionSource.CanonicalSchema + "|ack:" + AuthoritativeInputAck.CanonicalSchema + "|remote:" + RemotePresentationSchema;
        public const string RemotePresentationSchema = "server-authoritative-remote-presentation/14|timeline:" + AbilityTimelineProgress.CanonicalSchema + "|effect-context:" + SimulationGameplayEffectContext.CanonicalSchema + "|lifecycle:" + GameplayEffectReconciliationResult.CanonicalSchema + "|source:" + SimulationExecutionSource.CanonicalSchema + "|records:" + CharacterBodySample.CanonicalSchema + ":" + ServerAuthoritativeReliableEvent.CanonicalSchema;
        public const int ClientInputSchemaVersion = 4;
        public const int AuthorityReplicationSchemaVersion = 27;
        public const int RemotePresentationSchemaVersion = 27;
    }

    public static class ServerAuthoritativeEgressCodec
    {
        static readonly Func<CanonicalReader, string> ReadDeclaredIdentity =
            reader => CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());

        const uint InputMagic = 0x49454153;
        const uint ReplicationMagic = 0x52454153;
        const uint RemoteMagic = 0x50454153;
        const int InputVersion = 4;
        static readonly StableHash InputSchemaHash =
            new StableHash(CanonicalContractProtocol.ComposeSchema(ServerAuthoritativeEgressChannels.ClientInputSchema));
        const int ReplicationVersion = 18;
        const int RemoteVersion = 18;
        static readonly StableHash RemoteSchemaHash =
            new StableHash(CanonicalContractProtocol.ComposeSchema(ServerAuthoritativeEgressChannels.RemotePresentationSchema));
        static readonly StableHash AuthoritySchemaHash =
            new StableHash(CanonicalContractProtocol.ComposeSchema(ServerAuthoritativeEgressChannels.AuthorityReplicationSchema));
        const int MaximumCount = 4096;
        internal static readonly long MaximumRemoteTextValues = 1L + MaximumCount *
            (3 + PresentationCommand.CanonicalMaximumTextReferences + ServerAuthoritativeReliableEvent.MaximumTextReferences);

        public static void WriteOwnerInput(CanonicalWriter writer, OwnerCanonicalInputBatch input)
        {
            writer.WriteUInt32(InputMagic);
            writer.WriteInt32(InputVersion);
            writer.WriteHash(InputSchemaHash);
            OwnerCanonicalInputHeader.CanonicalWrite(writer, input.Header);
            ServerAuthoritativeCanonicalCodec.WriteLengthPrefixedInput(writer, input.Input);
        }

        public static OwnerCanonicalInputBatch ReadOwnerInput(ReadOnlyMemory<byte> bytes, CanonicalReader reader,
            CanonicalReader inputReader, Float32SimulationInputFrameStorage inputStorage,
            ActorId ownerActor, ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            if (reader.ReadUInt32() != InputMagic || reader.ReadInt32() != InputVersion)
                throw new InvalidDataException("Owner input egress header is invalid.");
            reader.RequireSchema(InputSchemaHash, "owner input");
            OwnerCanonicalInputHeader header = OwnerCanonicalInputHeader.CanonicalReadBound(reader, in ownerActor);
            SimulationInput input = ServerAuthoritativeCanonicalCodec.ReadInput(reader.ReadBytesSegment(),
                inputReader, reader.TextResolver, inputStorage);
            reader.RequireComplete();
            return new OwnerCanonicalInputBatch(in header, input);
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
            writer.WriteHash(AuthoritySchemaHash);
            writer.WriteUInt64(batch.AuthorityTick.Value);
            writer.WriteInt32(batch.Acks.Count);
            for (int i = 0; i < batch.Acks.Count; i++)
                AuthoritativeInputAck.CanonicalWrite(writer, batch.Acks[i]);
            writer.WriteInt32(batch.Baselines.Count);
            for (int i = 0; i < batch.Baselines.Count; i++)
                ServerAuthoritativeCanonicalCodec.WriteLengthPrefixedBaseline(writer, batch.Baselines[i]);
            writer.WriteInt32(batch.RemotePresentation.Count);
            for (int i = 0; i < batch.RemotePresentation.Count; i++)
                WriteLengthPrefixedRemotePresentation(writer, batch.RemotePresentation[i]);
        }

        public static AuthorityReplicationBatch ReadAuthorityReplication(ReadOnlyMemory<byte> bytes,
            CanonicalReader reader, CanonicalReader elementReader, CanonicalWriter canonicalScratch,
            CanonicalHashWriter bodyHashWriter, ICanonicalTextResolver textResolver,
            CanonicalBuffer<AuthoritativeInputAck> acks,
            CanonicalBuffer<AuthoritativeActorBaseline> baselines,
            CanonicalBuffer<RemotePresentationBatch> remote)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            reader.DeclaredIdentityReader = ReadDeclaredIdentity;
            reader.PublishedValues = Float32SimulationNumericProfile.PublishedValues;
            if (reader.ReadUInt32() != ReplicationMagic || reader.ReadInt32() != ReplicationVersion)
                throw new InvalidDataException("ServerAuthoritative authority replication schema is invalid.");
            reader.RequireSchema(AuthoritySchemaHash, "authority replication");
            var tick = new SimulationTick(reader.ReadUInt64());
            if (!tick.IsValid)
                throw new InvalidDataException("Authority replication Tick is invalid.");
            int ackCount = ReadCount(reader, "input ack");
            if (ackCount == 0)
                throw new InvalidDataException("Authority replication requires Actor acks and presentation streams.");
            acks.PrepareRead(ackCount);
            for (int i = 0; i < ackCount; i++)
            {
                AuthoritativeInputAck ack = AuthoritativeInputAck.CanonicalRead(reader);
                if (ack.AuthorityTick != tick || i > 0 && acks.Element(i - 1).ActorId.CompareTo(ack.ActorId) >= 0)
                    throw new InvalidDataException("Authority replication ack set is invalid or does not match the batch.");
                acks.Element(i) = ack;
                acks.CompleteReadElement();
            }
            int baselineCount = ReadCount(reader, "baseline");
            baselines.PrepareRead(baselineCount);
            for (int i = 0; i < baselineCount; i++)
            {
                AuthoritativeActorBaseline baseline = ServerAuthoritativeCanonicalCodec.ReadBaseline(reader.ReadBytesSegment(),
                    elementReader, bodyHashWriter);
                if (baseline.AuthorityTick != tick || i > 0 && baselines.Element(i - 1).ActorId.CompareTo(baseline.ActorId) >= 0)
                    throw new InvalidDataException("Authority replication baseline set is invalid or does not match the batch.");
                baselines.Element(i) = baseline;
                baselines.CompleteReadElement();
            }
            int remoteCount = ReadCount(reader, "remote presentation");
            if (remoteCount == 0)
                throw new InvalidDataException("Authority replication requires Actor acks and presentation streams.");
            remote.PrepareRead(remoteCount);
            for (int i = 0; i < remoteCount; i++)
            {
                RemotePresentationBatch presentation = ReadRemotePresentation(reader.ReadBytesSegment(), elementReader, canonicalScratch, textResolver);
                if (i > 0 && remote.Element(i - 1).ActorId.CompareTo(presentation.ActorId) >= 0)
                    throw new InvalidDataException("Authority replication presentation set is invalid.");
                remote.Element(i) = presentation;
                remote.CompleteReadElement();
            }
            reader.RequireComplete();
            AuthorityReplicationBatch result = AuthorityReplicationBatch.FromPreparedOutputs(tick, acks, baselines, remote);
            canonicalScratch.Reset();
            WriteAuthorityReplication(canonicalScratch, result);
            RequireCanonical(bytes.Span, canonicalScratch, "authority replication");
            return result;
        }

        public static byte[] WriteRemotePresentation(RemotePresentationBatch batch)
        {
            if (!batch.IsValid)
                throw new ArgumentException("Remote presentation batch is invalid.", nameof(batch));
            using var writer = new CanonicalWriter();
            WriteRemotePresentation(writer, batch);
            return writer.ToArray();
        }

        public static byte[] WriteRemoteReliableEvent(CanonicalWriter writer, ActorId actorId, ServerAuthoritativeReliableEvent value)
        {
            if (!actorId.IsValid)
                throw new ArgumentException("Remote reliable event ActorId is invalid.", nameof(actorId));
            if (value.Header.ActorId != actorId)
                throw new ArgumentException("Remote reliable event belongs to another Actor.", nameof(value));
            writer.Reset();
            writer.WriteUInt32(RemoteMagic);
            writer.WriteInt32(RemoteVersion);
            writer.WriteHash(RemoteSchemaHash);
            writer.WriteText(actorId.Value);
            writer.WriteBoolean(false);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(1);
            ServerAuthoritativeReliableEvent.CanonicalWrite(writer, value);
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
            writer.WriteHash(RemoteSchemaHash);
            writer.WriteText(batch.ActorId.Value);
            writer.WriteBoolean(batch.ResetBodyStream);
            writer.WriteInt32(batch.BodySamples.Count);
            for (int i = 0; i < batch.BodySamples.Count; i++)
                CharacterBodySample.CanonicalWrite(writer, batch.BodySamples[i]);
            writer.WriteInt32(batch.SampleCommands.Count);
            for (int i = 0; i < batch.SampleCommands.Count; i++)
                PresentationCommand.CanonicalWrite(writer, batch.SampleCommands[i]);
            writer.WriteInt32(batch.ReliableEvents.Count);
            for (int i = 0; i < batch.ReliableEvents.Count; i++)
                ServerAuthoritativeReliableEvent.CanonicalWrite(writer, batch.ReliableEvents[i]);
        }

        public static RemotePresentationBatch ReadRemotePresentation(ReadOnlyMemory<byte> bytes,
            CanonicalReader reader, CanonicalWriter canonicalScratch, ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            reader.DeclaredIdentityReader = ReadDeclaredIdentity;
            reader.PublishedValues = Float32SimulationNumericProfile.PublishedValues;
            if (reader.ReadUInt32() != RemoteMagic || reader.ReadInt32() != RemoteVersion)
                throw new InvalidDataException("ServerAuthoritative remote presentation schema is invalid.");
            reader.RequireSchema(RemoteSchemaHash, "remote presentation");
            return ReadRemotePresentation(reader, bytes.Span, canonicalScratch);
        }

        static RemotePresentationBatch ReadRemotePresentation(ArraySegment<byte> bytes,
            CanonicalReader reader, CanonicalWriter canonicalScratch, ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            reader.DeclaredIdentityReader = ReadDeclaredIdentity;
            reader.PublishedValues = Float32SimulationNumericProfile.PublishedValues;
            if (reader.ReadUInt32() != RemoteMagic || reader.ReadInt32() != RemoteVersion)
                throw new InvalidDataException("ServerAuthoritative remote presentation schema is invalid.");
            reader.RequireSchema(RemoteSchemaHash, "remote presentation");
            return ReadRemotePresentation(reader, bytes.AsSpan(), canonicalScratch);
        }

        static RemotePresentationBatch ReadRemotePresentation(CanonicalReader reader, ReadOnlySpan<byte> source,
            CanonicalWriter canonicalScratch)
        {
            var actorId = new ActorId(reader.ReadText());
            bool resetBodyStream = reader.ReadBoolean();
            int bodyCount = ReadCount(reader, "body sample");
            CharacterBodySample[] bodies = bodyCount == 0
                ? Array.Empty<CharacterBodySample>()
                : new CharacterBodySample[bodyCount];
            for (int i = 0; i < bodyCount; i++)
                bodies[i] = CharacterBodySample.CanonicalRead(reader);
            int sampleCount = ReadCount(reader, "presentation sample");
            PresentationCommand[] samples = sampleCount == 0
                ? Array.Empty<PresentationCommand>()
                : new PresentationCommand[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                samples[i] = PresentationCommand.CanonicalRead(reader);
            int eventCount = ReadCount(reader, "reliable event");
            ServerAuthoritativeReliableEvent[] events = eventCount == 0
                ? Array.Empty<ServerAuthoritativeReliableEvent>()
                : new ServerAuthoritativeReliableEvent[eventCount];
            for (int i = 0; i < eventCount; i++)
                events[i] = ServerAuthoritativeReliableEvent.CanonicalRead(reader);
            reader.RequireComplete();
            var result = new RemotePresentationBatch(actorId, bodies, samples, events, resetBodyStream);
            canonicalScratch.Reset();
            WriteRemotePresentation(canonicalScratch, result);
            RequireCanonical(source, canonicalScratch, "remote presentation");
            return result;
        }

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

    }
}
