using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ThirdPersonSimulation.ServerAuthoritative.Transport
{
    internal interface IServerAuthoritativeDatagramIdentityResolver
    {
        bool TryResolveIdentity(
            ReadOnlySpan<byte> room,
            ReadOnlySpan<byte> session,
            ReadOnlySpan<byte> player,
            ReadOnlySpan<byte> actor,
            out ServerAuthoritativeDatagramIdentity identity);
    }

    public enum ServerAuthoritativeDatagramKind : byte
    {
        DataPlaneHello = 1,
        DataPlaneHelloAck = 2,
        Command = 3,
        Snapshot = 4
    }

    public readonly struct ServerAuthoritativeDatagramIdentity : IEquatable<ServerAuthoritativeDatagramIdentity>
    {
        public ServerAuthoritativeDatagramIdentity(
            ServerAuthoritativeRoomId roomId,
            ServerAuthoritativeSessionId sessionId,
            ServerAuthoritativePlayerId playerId,
            ActorId actorId)
        {
            if (!roomId.IsValid || !sessionId.IsValid || !playerId.IsValid || !actorId.IsValid)
                throw new ArgumentException("Gameplay datagram identity is incomplete.");
            RoomId = roomId;
            SessionId = sessionId;
            PlayerId = playerId;
            ActorId = actorId;
        }

        public ServerAuthoritativeRoomId RoomId { get; }
        public ServerAuthoritativeSessionId SessionId { get; }
        public ServerAuthoritativePlayerId PlayerId { get; }
        public ActorId ActorId { get; }

        public bool Equals(ServerAuthoritativeDatagramIdentity other) =>
            RoomId.Equals(other.RoomId) && SessionId.Equals(other.SessionId) &&
            PlayerId.Equals(other.PlayerId) && ActorId.Equals(other.ActorId);
        public override bool Equals(object obj) => obj is ServerAuthoritativeDatagramIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RoomId, SessionId, PlayerId, ActorId);
        public override string ToString() => $"{RoomId}/{SessionId}/{PlayerId}/{ActorId}";
    }

    public readonly struct ServerAuthoritativeDatagramHeader
    {
        public ServerAuthoritativeDatagramHeader(
            ServerAuthoritativeDatagramIdentity identity,
            ServerAuthoritativeDatagramKind kind,
            ulong packetSequence,
            int payloadLength)
        {
            if (!ServerAuthoritativeGameplayDatagramCodec.IsSupportedKind(kind) || packetSequence == 0 || payloadLength < 0)
                throw new ArgumentException("Gameplay datagram header is invalid.");
            Identity = identity;
            Kind = kind;
            PacketSequence = packetSequence;
            PayloadLength = payloadLength;
        }

        public int ProtocolVersion => ServerAuthoritativeGameplayDatagramCodec.ProtocolVersion;
        public ServerAuthoritativeDatagramIdentity Identity { get; }
        public ServerAuthoritativeDatagramKind Kind { get; }
        public ulong PacketSequence { get; }
        public int PayloadLength { get; }
    }

    public readonly struct ServerAuthoritativeDatagramPacket
    {
        public ServerAuthoritativeDatagramPacket(ServerAuthoritativeDatagramHeader header, byte[] payloadBuffer, int payloadLength)
        {
            if (payloadBuffer == null)
                throw new ArgumentNullException(nameof(payloadBuffer));
            if (payloadLength < 0 || payloadLength > payloadBuffer.Length || header.PayloadLength != payloadLength)
                throw new ArgumentException("Gameplay datagram payload length does not match its header.", nameof(payloadBuffer));
            Header = header;
            PayloadBuffer = payloadBuffer;
            PayloadLength = payloadLength;
        }

        public ServerAuthoritativeDatagramHeader Header { get; }
        public byte[] PayloadBuffer { get; }
        public int PayloadLength { get; }
        public bool IsValid => PayloadBuffer != null;
        public ReadOnlyMemory<byte> Payload => PayloadBuffer.AsMemory(0, PayloadLength);
    }

    public static class ServerAuthoritativeGameplayDatagramCodec
    {
        const uint Magic = 0x44504153;
        public const int ProtocolVersion = 4;

        internal static bool IsSupportedKind(ServerAuthoritativeDatagramKind kind) =>
            kind == ServerAuthoritativeDatagramKind.DataPlaneHello ||
            kind == ServerAuthoritativeDatagramKind.DataPlaneHelloAck ||
            kind == ServerAuthoritativeDatagramKind.Command ||
            kind == ServerAuthoritativeDatagramKind.Snapshot;

        public static int Write(
            ServerAuthoritativeDatagramHeader header,
            ReadOnlySpan<byte> payload,
            CanonicalWriter writer,
            int maximumBytes)
        {
            if (header.PayloadLength != payload.Length)
                throw new ArgumentException("Gameplay datagram payload length does not match its header.", nameof(payload));
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            writer.Reset();
            Write(writer, header, payload, maximumBytes);
            int length = checked((int)writer.Length);
            if (length > maximumBytes)
                throw new InvalidDataException($"Gameplay datagram size '{length}' exceeds budget '{maximumBytes}'.");
            return length;
        }

        static void Write(
            CanonicalWriter writer,
            ServerAuthoritativeDatagramHeader header,
            ReadOnlySpan<byte> payload,
            int maximumBytes)
        {
            if (maximumBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            writer.WriteUInt32(Magic);
            writer.WriteInt32(ProtocolVersion);
            writer.WriteByte((byte)header.Kind);
            writer.WriteText(header.Identity.RoomId.Value);
            writer.WriteText(header.Identity.SessionId.Value);
            writer.WriteText(header.Identity.PlayerId.Value);
            writer.WriteText(header.Identity.ActorId.Value);
            writer.WriteUInt64(header.PacketSequence);
            writer.WriteBytes(payload);
            if (writer.Length > maximumBytes)
                throw new InvalidDataException($"Gameplay datagram size '{writer.Length}' exceeds budget '{maximumBytes}'.");
        }

        internal static ServerAuthoritativeDatagramPacket Read(
            ArraySegment<byte> bytes,
            CanonicalReader reader,
            int maximumBytes,
            byte[] payloadBuffer,
            IServerAuthoritativeDatagramIdentityResolver identityResolver)
        {
            if (bytes.Count == 0 || bytes.Count > maximumBytes)
                throw new InvalidDataException("Gameplay datagram length is invalid.");
            if (payloadBuffer == null || payloadBuffer.Length < maximumBytes)
                throw new ArgumentException("Gameplay datagram payload buffer is invalid.", nameof(payloadBuffer));
            if (identityResolver == null)
                throw new ArgumentNullException(nameof(identityResolver));
            reader.Reset(bytes);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Gameplay datagram magic is invalid.");
            int version = reader.ReadInt32();
            if (version != ProtocolVersion)
                throw new InvalidDataException($"Gameplay datagram protocol version '{version}' is unsupported.");
            var kind = (ServerAuthoritativeDatagramKind)reader.ReadByte();
            if (!IsSupportedKind(kind))
                throw new InvalidDataException($"Gameplay datagram kind '{kind}' is unsupported.");
            ArraySegment<byte> room = reader.ReadTextSegment();
            ArraySegment<byte> session = reader.ReadTextSegment();
            ArraySegment<byte> player = reader.ReadTextSegment();
            ArraySegment<byte> actor = reader.ReadTextSegment();
            if (!identityResolver.TryResolveIdentity(
                    room.AsSpan(), session.AsSpan(), player.AsSpan(), actor.AsSpan(), out ServerAuthoritativeDatagramIdentity identity))
            {
                identity = new ServerAuthoritativeDatagramIdentity(
                    new ServerAuthoritativeRoomId(CanonicalTextEncoding.DecodeString(room)),
                    new ServerAuthoritativeSessionId(CanonicalTextEncoding.DecodeString(session)),
                    new ServerAuthoritativePlayerId(CanonicalTextEncoding.DecodeString(player)),
                    new ActorId(CanonicalTextEncoding.DecodeString(actor)));
            }
            ulong packetSequence = reader.ReadUInt64();
            ArraySegment<byte> payload = reader.ReadBytesSegment();
            reader.RequireComplete();
            payload.AsSpan().CopyTo(payloadBuffer);
            return new ServerAuthoritativeDatagramPacket(
                new ServerAuthoritativeDatagramHeader(identity, kind, packetSequence, payload.Count),
                payloadBuffer,
                payload.Count);
        }
    }

    public readonly struct DataPlaneHello
    {
        public DataPlaneHello(string ticketId, string nonce, long clientClockMicros)
        {
            TicketId = RequireToken(ticketId, nameof(ticketId));
            Nonce = RequireToken(nonce, nameof(nonce));
            if (clientClockMicros <= 0)
                throw new ArgumentOutOfRangeException(nameof(clientClockMicros));
            ClientClockMicros = clientClockMicros;
        }

        public string TicketId { get; }
        public string Nonce { get; }
        public long ClientClockMicros { get; }

        internal static string RequireToken(string value, string parameter)
        {
            if (CanonicalTextValue.IsNullOrWhiteSpace(value) || value.Length > 128)
                throw new ArgumentException("Data-plane token is invalid.", parameter);
            return value;
        }
    }

    public readonly struct DataPlaneHelloAck
    {
        public DataPlaneHelloAck(ulong authorityTick, long echoedClientClockMicros, long authorityClockMicros)
        {
            if (echoedClientClockMicros <= 0 || authorityClockMicros <= 0)
                throw new ArgumentException("Data-plane hello acknowledgement is invalid.");
            AuthorityTick = authorityTick;
            EchoedClientClockMicros = echoedClientClockMicros;
            AuthorityClockMicros = authorityClockMicros;
        }

        public ulong AuthorityTick { get; }
        public long EchoedClientClockMicros { get; }
        public long AuthorityClockMicros { get; }
    }

    public readonly struct CanonicalInputSample
    {
        public CanonicalInputSample(ulong targetAuthorityTick, ulong inputSequence, SimulationInput input)
        {
            if (targetAuthorityTick == 0 || inputSequence == 0)
                throw new ArgumentException("Canonical input sample identity is invalid.");
            Input = input ?? throw new ArgumentNullException(nameof(input));
            if (input.Sequence != inputSequence || input.TickSource.SourceTick == 0)
                throw new ArgumentException("Canonical input sample identity does not match its input.", nameof(input));
            TargetAuthorityTick = targetAuthorityTick;
            InputSequence = inputSequence;
        }

        public ulong TargetAuthorityTick { get; }
        public ulong InputSequence { get; }
        public SimulationInput Input { get; }
        public bool IsValid => TargetAuthorityTick != 0 && InputSequence != 0 && Input != null;
    }

    public readonly struct CommandDatagram
    {
        readonly CanonicalInputSample[] m_Samples;

        public CommandDatagram(
            ulong latestSnapshotSequence,
            ulong latestBaseSnapshotSequence,
            IReadOnlyList<CanonicalInputSample> samples)
            : this(latestSnapshotSequence, latestBaseSnapshotSequence, PrepareSamples(samples))
        {
        }

        public static CommandDatagram FromOwnedSamples(
            ulong latestSnapshotSequence,
            ulong latestBaseSnapshotSequence,
            CanonicalInputSample[] samples)
        {
            ValidateSamples(samples);
            return new CommandDatagram(latestSnapshotSequence, latestBaseSnapshotSequence, samples);
        }

        CommandDatagram(
            ulong latestSnapshotSequence,
            ulong latestBaseSnapshotSequence,
            CanonicalInputSample[] samples)
        {
            LatestSnapshotSequence = latestSnapshotSequence;
            LatestBaseSnapshotSequence = latestBaseSnapshotSequence;
            m_Samples = samples;
        }

        public ulong LatestSnapshotSequence { get; }
        public ulong LatestBaseSnapshotSequence { get; }
        public bool IsValid => m_Samples != null && m_Samples.Length > 0;
        public ulong SourceTick => m_Samples[0].Input.TickSource.SourceTick;
        public IReadOnlyList<CanonicalInputSample> Samples => m_Samples;

        static CanonicalInputSample[] PrepareSamples(IReadOnlyList<CanonicalInputSample> samples)
        {
            ValidateSamples(samples);
            var values = new CanonicalInputSample[samples.Count];
            for (int i = 0; i < samples.Count; i++)
                values[i] = samples[i];
            return values;
        }

        static void ValidateSamples(IReadOnlyList<CanonicalInputSample> samples)
        {
            if (samples == null)
                throw new ArgumentNullException(nameof(samples));
            if (samples.Count == 0 || samples.Count > 4)
                throw new ArgumentException("Command datagram requires one to four input samples.", nameof(samples));
            if (!samples[0].IsValid)
                throw new ArgumentException("Command datagram sample is invalid.", nameof(samples));
            for (int i = 1; i < samples.Count; i++)
            {
                if (!samples[i].IsValid ||
                    samples[i - 1].TargetAuthorityTick <= samples[i].TargetAuthorityTick ||
                    samples[i - 1].InputSequence <= samples[i].InputSequence)
                {
                    throw new ArgumentException("Command datagram samples must be newest-first and strictly ordered.", nameof(samples));
                }
            }
        }
    }

    public readonly struct SnapshotDatagram
    {
        readonly ReadOnlyMemory<byte> m_DeltaPayload;

        internal SnapshotDatagram(
            ulong snapshotSequence,
            ulong baseSnapshotSequence,
            ulong authorityTick,
            ulong acknowledgedInputSequence,
            ulong reliableEventHorizon,
            ArraySegment<byte> deltaPayload)
        {
            if (snapshotSequence == 0 || authorityTick == 0)
                throw new ArgumentException("Snapshot datagram identity is invalid.");
            if (deltaPayload.Array == null)
                throw new ArgumentNullException(nameof(deltaPayload));
            SnapshotSequence = snapshotSequence;
            BaseSnapshotSequence = baseSnapshotSequence;
            AuthorityTick = authorityTick;
            AcknowledgedInputSequence = acknowledgedInputSequence;
            ReliableEventHorizon = reliableEventHorizon;
            m_DeltaPayload = deltaPayload.Array.AsMemory(deltaPayload.Offset, deltaPayload.Count);
        }

        public ulong SnapshotSequence { get; }
        public ulong BaseSnapshotSequence { get; }
        public ulong AuthorityTick { get; }
        public ulong AcknowledgedInputSequence { get; }
        public ulong ReliableEventHorizon { get; }
        public bool IsValid => SnapshotSequence != 0 && AuthorityTick != 0;
        public ReadOnlyMemory<byte> DeltaPayload => m_DeltaPayload;
    }

    public static class ServerAuthoritativeDatagramPayloadCodec
    {
        const int SchemaVersion = 2;

        public static void Write(DataPlaneHello value, CanonicalWriter writer)
        {
            Writer(writer, ServerAuthoritativeDatagramKind.DataPlaneHello);
            writer.WriteText(value.TicketId);
            writer.WriteText(value.Nonce);
            writer.WriteInt64(value.ClientClockMicros);
        }

        public static DataPlaneHello ReadHello(ReadOnlyMemory<byte> bytes)
        {
            CanonicalReader reader = Reader(bytes, ServerAuthoritativeDatagramKind.DataPlaneHello);
            var value = new DataPlaneHello(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadInt64());
            reader.RequireComplete();
            return value;
        }

        public static void Write(DataPlaneHelloAck value, CanonicalWriter writer)
        {
            Writer(writer, ServerAuthoritativeDatagramKind.DataPlaneHelloAck);
            writer.WriteUInt64(value.AuthorityTick);
            writer.WriteInt64(value.EchoedClientClockMicros);
            writer.WriteInt64(value.AuthorityClockMicros);
        }

        public static DataPlaneHelloAck ReadHelloAck(ReadOnlyMemory<byte> bytes)
        {
            CanonicalReader reader = Reader(bytes, ServerAuthoritativeDatagramKind.DataPlaneHelloAck);
            var value = new DataPlaneHelloAck(reader.ReadUInt64(), reader.ReadInt64(), reader.ReadInt64());
            reader.RequireComplete();
            return value;
        }

        public static void Write(CommandDatagram value, CanonicalWriter writer)
        {
            if (!value.IsValid)
                throw new ArgumentException("Command datagram is invalid.", nameof(value));
            Writer(writer, ServerAuthoritativeDatagramKind.Command);
            writer.WriteUInt64(value.LatestSnapshotSequence);
            writer.WriteUInt64(value.LatestBaseSnapshotSequence);
            writer.WriteInt32(value.Samples.Count);
            for (int i = 0; i < value.Samples.Count; i++)
            {
                CanonicalInputSample sample = value.Samples[i];
                writer.WriteUInt64(sample.TargetAuthorityTick);
                writer.WriteUInt64(sample.InputSequence);
                ServerAuthoritativeCanonicalCodec.WriteLengthPrefixedInput(writer, sample.Input);
            }
        }

        public static CommandDatagram ReadCommand(ReadOnlyMemory<byte> bytes, CanonicalReader reader,
            CanonicalReader inputReader, ICanonicalTextResolver textResolver, CanonicalInputSample[][] sampleStorage,
            Float32SimulationInputFrameStorage[] inputStorage)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            if (reader.ReadInt32() != SchemaVersion ||
                reader.ReadByte() != (byte)ServerAuthoritativeDatagramKind.Command)
                throw new InvalidDataException("ServerAuthoritative command payload header is invalid.");
            ulong latestSnapshot = reader.ReadUInt64();
            ulong latestBase = reader.ReadUInt64();
            int count = reader.ReadInt32();
            if (count <= 0 || count > 4)
                throw new InvalidDataException($"Command sample count '{count}' is invalid.");
            CanonicalInputSample[] samples = sampleStorage[count - 1];
            for (int i = 0; i < count; i++)
            {
                ulong targetTick = reader.ReadUInt64();
                ulong inputSequence = reader.ReadUInt64();
                SimulationInput input = ServerAuthoritativeCanonicalCodec.ReadInput(reader.ReadBytesSegment(), inputReader,
                    reader.TextResolver, inputStorage[i]);
                samples[i] = new CanonicalInputSample(targetTick, inputSequence, input);
            }
            reader.RequireComplete();
            return CommandDatagram.FromOwnedSamples(latestSnapshot, latestBase, samples);
        }

        public static void WriteSnapshot(
            CanonicalWriter writer,
            ulong snapshotSequence,
            ulong baseSnapshotSequence,
            ulong acknowledgedInputSequence,
            NetworkCheckpointLayout layout,
            in NetworkCheckpoint baseline,
            in NetworkCheckpoint target,
            in RemotePresentationBatch remote)
        {
            AuthoritativeActorBaseline value = target.Baseline;
            Writer(writer, ServerAuthoritativeDatagramKind.Snapshot);
            writer.WriteUInt64(snapshotSequence);
            writer.WriteUInt64(baseSnapshotSequence);
            writer.WriteUInt64(value.AuthorityTick.Value);
            writer.WriteUInt64(acknowledgedInputSequence);
            writer.WriteUInt64(value.ConfirmedEventHorizon.Sequence);
            long prefixPosition = writer.BeginLengthPrefixedBlock();
            NetworkCheckpointCodec.WriteDelta(writer, layout, in baseline, in target, in remote);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        public static SnapshotDatagram ReadSnapshot(ReadOnlyMemory<byte> bytes, CanonicalReader reader)
        {
            reader.Reset(bytes);
            ReadHeader(reader, ServerAuthoritativeDatagramKind.Snapshot);
            var value = new SnapshotDatagram(
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                reader.ReadUInt64(),
                reader.ReadBytesSegment());
            reader.RequireComplete();
            return value;
        }

        static void Writer(CanonicalWriter writer, ServerAuthoritativeDatagramKind kind)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            writer.Reset();
            writer.WriteInt32(SchemaVersion);
            writer.WriteByte((byte)kind);
        }

        static CanonicalReader Reader(ReadOnlyMemory<byte> bytes, ServerAuthoritativeDatagramKind expectedKind)
        {
            var reader = new CanonicalReader(bytes);
            ReadHeader(reader, expectedKind);
            return reader;
        }

        static void ReadHeader(CanonicalReader reader, ServerAuthoritativeDatagramKind expectedKind)
        {
            int version = reader.ReadInt32();
            if (version != SchemaVersion)
                throw new InvalidDataException($"Datagram payload schema version '{version}' is unsupported.");
            var kind = (ServerAuthoritativeDatagramKind)reader.ReadByte();
            if (kind != expectedKind)
                throw new InvalidDataException($"Datagram payload kind '{kind}' does not match '{expectedKind}'.");
        }
    }
}
