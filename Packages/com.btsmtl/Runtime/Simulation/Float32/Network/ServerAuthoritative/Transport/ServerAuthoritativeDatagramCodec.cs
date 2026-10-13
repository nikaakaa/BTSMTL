using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ThirdPersonSimulation.ServerAuthoritative.Transport
{
    public enum ServerAuthoritativeDatagramKind : byte
    {
        DataPlaneHello = 1,
        DataPlaneHelloAck = 2,
        Command = 3,
        Snapshot = 4
    }

    [CanonicalContract]
    public readonly partial struct ServerAuthoritativeDatagramIdentity : IEquatable<ServerAuthoritativeDatagramIdentity>
    {
        public ServerAuthoritativeDatagramIdentity(
            ServerAuthoritativeRoomId roomId,
            ServerAuthoritativeSessionId sessionId,
            ServerAuthoritativePlayerId playerId,
            ActorId actorId)
        {
            RoomId = roomId;
            SessionId = sessionId;
            PlayerId = playerId;
            ActorId = actorId;
            RequireRecord();
        }

        public ServerAuthoritativeRoomId RoomId { get; }
        public ServerAuthoritativeSessionId SessionId { get; }
        public ServerAuthoritativePlayerId PlayerId { get; }
        public ActorId ActorId { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (!RoomId.IsValid || !SessionId.IsValid || !PlayerId.IsValid || !ActorId.IsValid)
                throw new ArgumentException("Gameplay datagram identity is incomplete.");
        }

        public bool Equals(ServerAuthoritativeDatagramIdentity other) =>
            RoomId.Equals(other.RoomId) && SessionId.Equals(other.SessionId) &&
            PlayerId.Equals(other.PlayerId) && ActorId.Equals(other.ActorId);
        public override bool Equals(object obj) => obj is ServerAuthoritativeDatagramIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RoomId, SessionId, PlayerId, ActorId);
        public override string ToString() => $"{RoomId}/{SessionId}/{PlayerId}/{ActorId}";
    }

    [CanonicalContract]
    public readonly partial struct ServerAuthoritativeDatagramHeader
    {
        public ServerAuthoritativeDatagramHeader(
            ServerAuthoritativeDatagramIdentity identity,
            ServerAuthoritativeDatagramKind kind,
            ulong packetSequence,
            int payloadLength)
        {
            Identity = identity;
            Kind = kind;
            PacketSequence = packetSequence;
            PayloadLength = payloadLength;
            RequireRecord();
        }

        public int ProtocolVersion => ServerAuthoritativeGameplayDatagramCodec.ProtocolVersion;
        public ServerAuthoritativeDatagramIdentity Identity { get; }
        public ServerAuthoritativeDatagramKind Kind { get; }
        public ulong PacketSequence { get; }
        public int PayloadLength { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (!ServerAuthoritativeGameplayDatagramCodec.IsSupportedKind(Kind) || PacketSequence == 0 || PayloadLength < 0)
                throw new ArgumentException("Gameplay datagram header is invalid.");
        }
    }

    public readonly struct ServerAuthoritativeDatagramPacket
    {
        internal ServerAuthoritativeDatagramPacket(in ServerAuthoritativeDatagramHeader header, byte[] payloadBuffer)
        {
            Header = header;
            PayloadBuffer = payloadBuffer;
        }

        public ServerAuthoritativeDatagramHeader Header { get; }
        public byte[] PayloadBuffer { get; }
        public int PayloadLength => Header.PayloadLength;
        public ReadOnlyMemory<byte> Payload => PayloadBuffer.AsMemory(0, PayloadLength);
    }

    public static class ServerAuthoritativeGameplayDatagramCodec
    {
        const uint Magic = 0x44504153;
        public const int ProtocolVersion = 5;

        internal static bool IsSupportedKind(ServerAuthoritativeDatagramKind kind) =>
            kind == ServerAuthoritativeDatagramKind.DataPlaneHello ||
            kind == ServerAuthoritativeDatagramKind.DataPlaneHelloAck ||
            kind == ServerAuthoritativeDatagramKind.Command ||
            kind == ServerAuthoritativeDatagramKind.Snapshot;

        internal static int Write(
            ServerAuthoritativeDatagramHeader header,
            ReadOnlySpan<byte> payload,
            CanonicalWriter writer,
            int maximumBytes)
        {
            if (header.PayloadLength != payload.Length)
                throw new ArgumentException("Gameplay datagram payload length does not match its header.", nameof(payload));
            int length = checked(sizeof(uint) + sizeof(int) + CanonicalContractProtocol.HashEncodedSize +
                ServerAuthoritativeDatagramHeader.CanonicalMeasure(in header) + payload.Length);
            if (length > maximumBytes)
                throw new InvalidDataException($"Gameplay datagram size '{length}' exceeds budget '{maximumBytes}'.");
            writer.Reset();
            writer.WriteUInt32(Magic);
            writer.WriteInt32(ProtocolVersion);
            writer.WriteHash(ServerAuthoritativeDatagramHeader.CanonicalSchemaHash);
            ServerAuthoritativeDatagramHeader.CanonicalWrite(writer, in header);
            writer.WriteRawBytes(payload);
            return length;
        }

        internal static ServerAuthoritativeDatagramPacket Read(
            ArraySegment<byte> bytes,
            CanonicalReader reader,
            byte[] payloadBuffer)
        {
            reader.Reset(bytes);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Gameplay datagram magic is invalid.");
            int version = reader.ReadInt32();
            if (version != ProtocolVersion)
                throw new InvalidDataException($"Gameplay datagram protocol version '{version}' is unsupported.");
            ServerAuthoritativeDatagramHeader.CanonicalRequireSchema(reader);
            ServerAuthoritativeDatagramHeader header = ServerAuthoritativeDatagramHeader.CanonicalRead(reader);
            ArraySegment<byte> payload = reader.ReadRawBytesSegment(header.PayloadLength);
            reader.RequireComplete();
            payload.AsSpan().CopyTo(payloadBuffer);
            return new ServerAuthoritativeDatagramPacket(
                in header,
                payloadBuffer);
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct DataPlaneHello
    {
        public DataPlaneHello(string ticketId, string nonce, long clientClockMicros)
        {
            TicketId = ticketId;
            Nonce = nonce;
            ClientClockMicros = clientClockMicros;
            RequireRecord();
        }

        public string TicketId { get; }
        public string Nonce { get; }
        public long ClientClockMicros { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            RequireToken(TicketId, nameof(TicketId));
            RequireToken(Nonce, nameof(Nonce));
            if (ClientClockMicros <= 0)
                throw new ArgumentOutOfRangeException(nameof(ClientClockMicros));
        }

        internal static string RequireToken(string value, string parameter)
        {
            if (CanonicalTextValue.IsNullOrWhiteSpace(value) || value.Length > 128)
                throw new ArgumentException("Data-plane token is invalid.", parameter);
            return value;
        }
    }

    [CanonicalContract]
    public readonly partial struct DataPlaneHelloAck
    {
        public DataPlaneHelloAck(ulong authorityTick, long echoedClientClockMicros, long authorityClockMicros)
        {
            AuthorityTick = authorityTick;
            EchoedClientClockMicros = echoedClientClockMicros;
            AuthorityClockMicros = authorityClockMicros;
            RequireRecord();
        }

        public ulong AuthorityTick { get; }
        public long EchoedClientClockMicros { get; }
        public long AuthorityClockMicros { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (EchoedClientClockMicros <= 0 || AuthorityClockMicros <= 0)
                throw new ArgumentException("Data-plane hello acknowledgement is invalid.");
        }
    }

    [CanonicalContract]
    internal readonly partial struct CanonicalInputSampleHeader
    {
        internal CanonicalInputSampleHeader(ulong targetAuthorityTick, ulong inputSequence)
        {
            TargetAuthorityTick = targetAuthorityTick;
            InputSequence = inputSequence;
            RequireRecord();
        }

        public ulong TargetAuthorityTick { get; }
        public ulong InputSequence { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (TargetAuthorityTick == 0 || InputSequence == 0)
                throw new ArgumentException("Canonical input sample identity is invalid.");
        }
    }

    [CanonicalStorageOwner]
    public readonly partial struct CanonicalInputSample
    {
        [CanonicalStorage] readonly CanonicalInputSampleHeader m_Header;

        public CanonicalInputSample(ulong targetAuthorityTick, ulong inputSequence, SimulationInput input)
            : this(new CanonicalInputSampleHeader(targetAuthorityTick, inputSequence), input)
        {
        }

        internal CanonicalInputSample(in CanonicalInputSampleHeader header, SimulationInput input)
        {
            m_Header = header;
            Input = input ?? throw new ArgumentNullException(nameof(input));
            if (input.Sequence != header.InputSequence || input.TickSource.SourceTick == 0)
                throw new ArgumentException("Canonical input sample identity does not match its input.", nameof(input));
        }

        internal CanonicalInputSampleHeader Header => m_Header;
        public SimulationInput Input { get; }
        public bool IsValid => TargetAuthorityTick != 0 && InputSequence != 0 && Input != null;
    }

    [CanonicalContract]
    internal readonly partial struct CommandDatagramHeader
    {
        [CanonicalSchemaConstant]
        public const int MaximumSampleCount = 4;

        internal CommandDatagramHeader(ulong latestSnapshotSequence, ulong latestBaseSnapshotSequence)
        {
            LatestSnapshotSequence = latestSnapshotSequence;
            LatestBaseSnapshotSequence = latestBaseSnapshotSequence;
        }

        public ulong LatestSnapshotSequence { get; }
        public ulong LatestBaseSnapshotSequence { get; }
    }

    [CanonicalStorageOwner]
    public readonly partial struct CommandDatagram
    {
        public const int MaximumSampleCount = CommandDatagramHeader.MaximumSampleCount;
        [CanonicalStorage] readonly CommandDatagramHeader m_Header;
        readonly CanonicalInputSample[] m_Samples;

        public CommandDatagram(
            ulong latestSnapshotSequence,
            ulong latestBaseSnapshotSequence,
            IReadOnlyList<CanonicalInputSample> samples)
            : this(new CommandDatagramHeader(latestSnapshotSequence, latestBaseSnapshotSequence), PrepareSamples(samples))
        {
        }

        public static CommandDatagram FromOwnedSamples(
            ulong latestSnapshotSequence,
            ulong latestBaseSnapshotSequence,
            CanonicalInputSample[] samples)
        {
            ValidateSamples(samples);
            return new CommandDatagram(new CommandDatagramHeader(latestSnapshotSequence, latestBaseSnapshotSequence), samples);
        }

        internal CommandDatagram(in CommandDatagramHeader header, CanonicalInputSample[] samples)
        {
            for (int i = 1; i < samples.Length; i++)
            {
                if (samples[i - 1].TargetAuthorityTick <= samples[i].TargetAuthorityTick ||
                    samples[i - 1].InputSequence <= samples[i].InputSequence)
                    throw new ArgumentException("Command datagram samples must be newest-first and strictly ordered.", nameof(samples));
            }
            m_Header = header;
            m_Samples = samples;
        }

        internal CommandDatagramHeader Header => m_Header;
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
            if (samples.Count == 0 || samples.Count > MaximumSampleCount)
                throw new ArgumentException("Command datagram requires one to four input samples.", nameof(samples));
            for (int i = 0; i < samples.Count; i++)
            {
                if (!samples[i].IsValid)
                    throw new ArgumentException("Command datagram sample is invalid.", nameof(samples));
            }
        }
    }

    [CanonicalContract]
    internal readonly partial struct SnapshotDatagramHeader
    {
        internal SnapshotDatagramHeader(
            ulong snapshotSequence,
            ulong baseSnapshotSequence,
            ulong authorityTick,
            ulong acknowledgedInputSequence,
            ulong reliableEventHorizon)
        {
            SnapshotSequence = snapshotSequence;
            BaseSnapshotSequence = baseSnapshotSequence;
            AuthorityTick = authorityTick;
            AcknowledgedInputSequence = acknowledgedInputSequence;
            ReliableEventHorizon = reliableEventHorizon;
            RequireRecord();
        }

        public ulong SnapshotSequence { get; }
        public ulong BaseSnapshotSequence { get; }
        public ulong AuthorityTick { get; }
        public ulong AcknowledgedInputSequence { get; }
        public ulong ReliableEventHorizon { get; }

        [CanonicalValidation]
        void RequireRecord()
        {
            if (SnapshotSequence == 0 || AuthorityTick == 0)
                throw new ArgumentException("Snapshot datagram identity is invalid.");
        }
    }

    [CanonicalStorageOwner]
    public readonly partial struct SnapshotDatagram
    {
        [CanonicalStorage] readonly SnapshotDatagramHeader m_Header;
        readonly ReadOnlyMemory<byte> m_DeltaPayload;

        internal SnapshotDatagram(in SnapshotDatagramHeader header, ArraySegment<byte> deltaPayload)
        {
            m_Header = header;
            m_DeltaPayload = deltaPayload.Array.AsMemory(deltaPayload.Offset, deltaPayload.Count);
        }

        public bool IsValid => SnapshotSequence != 0 && AuthorityTick != 0;
        public ReadOnlyMemory<byte> DeltaPayload => m_DeltaPayload;
    }

    public static class ServerAuthoritativeDatagramPayloadCodec
    {
        const int SchemaVersion = 3;

        public static void Write(DataPlaneHello value, CanonicalWriter writer)
        {
            Writer(writer, ServerAuthoritativeDatagramKind.DataPlaneHello);
            writer.WriteHash(DataPlaneHello.CanonicalSchemaHash);
            DataPlaneHello.CanonicalWrite(writer, in value);
        }

        public static DataPlaneHello ReadHello(ReadOnlyMemory<byte> bytes)
        {
            CanonicalReader reader = Reader(bytes, ServerAuthoritativeDatagramKind.DataPlaneHello);
            DataPlaneHello.CanonicalRequireSchema(reader);
            DataPlaneHello value = DataPlaneHello.CanonicalReadMaterialized(reader);
            reader.RequireComplete();
            return value;
        }

        public static void Write(DataPlaneHelloAck value, CanonicalWriter writer)
        {
            Writer(writer, ServerAuthoritativeDatagramKind.DataPlaneHelloAck);
            writer.WriteHash(DataPlaneHelloAck.CanonicalSchemaHash);
            DataPlaneHelloAck.CanonicalWrite(writer, in value);
        }

        public static DataPlaneHelloAck ReadHelloAck(ReadOnlyMemory<byte> bytes)
        {
            CanonicalReader reader = Reader(bytes, ServerAuthoritativeDatagramKind.DataPlaneHelloAck);
            DataPlaneHelloAck.CanonicalRequireSchema(reader);
            DataPlaneHelloAck value = DataPlaneHelloAck.CanonicalRead(reader);
            reader.RequireComplete();
            return value;
        }

        public static void Write(CommandDatagram value, CanonicalWriter writer)
        {
            if (!value.IsValid)
                throw new ArgumentException("Command datagram is invalid.", nameof(value));
            Writer(writer, ServerAuthoritativeDatagramKind.Command);
            writer.WriteHash(CommandDatagramHeader.CanonicalSchemaHash);
            writer.WriteHash(CanonicalInputSampleHeader.CanonicalSchemaHash);
            CommandDatagramHeader.CanonicalWrite(writer, value.Header);
            writer.WriteInt32(value.Samples.Count);
            for (int i = 0; i < value.Samples.Count; i++)
            {
                CanonicalInputSample sample = value.Samples[i];
                CanonicalInputSampleHeader.CanonicalWrite(writer, sample.Header);
                ServerAuthoritativeCanonicalCodec.WriteLengthPrefixedInput(writer, sample.Input);
            }
        }

        public static CommandDatagram ReadCommand(ReadOnlyMemory<byte> bytes, CanonicalReader reader,
            CanonicalReader inputReader, ICanonicalTextResolver textResolver, CanonicalInputSample[][] sampleStorage,
            Float32SimulationInputFrameStorage[] inputStorage)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            ReadHeader(reader, ServerAuthoritativeDatagramKind.Command);
            CommandDatagramHeader.CanonicalRequireSchema(reader);
            CanonicalInputSampleHeader.CanonicalRequireSchema(reader);
            CommandDatagramHeader header = CommandDatagramHeader.CanonicalRead(reader);
            int count = CanonicalCollection.ReadCount(reader, CanonicalInputSampleHeader.CanonicalMinimumSize + sizeof(int));
            if (count == 0 || count > sampleStorage.Length)
                throw new InvalidDataException($"Command sample count '{count}' is invalid.");
            CanonicalInputSample[] samples = sampleStorage[count - 1];
            for (int i = 0; i < count; i++)
            {
                CanonicalInputSampleHeader sampleHeader = CanonicalInputSampleHeader.CanonicalRead(reader);
                SimulationInput input = ServerAuthoritativeCanonicalCodec.ReadInput(reader.ReadBytesSegment(), inputReader,
                    reader.TextResolver, inputStorage[i]);
                samples[i] = new CanonicalInputSample(in sampleHeader, input);
            }
            reader.RequireComplete();
            return new CommandDatagram(in header, samples);
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
            writer.WriteHash(SnapshotDatagramHeader.CanonicalSchemaHash);
            var header = new SnapshotDatagramHeader(snapshotSequence, baseSnapshotSequence, value.AuthorityTick.Value,
                acknowledgedInputSequence, value.ConfirmedEventHorizon.Sequence);
            SnapshotDatagramHeader.CanonicalWrite(writer, in header);
            long prefixPosition = writer.BeginLengthPrefixedBlock();
            NetworkCheckpointCodec.WriteDelta(writer, layout, in baseline, in target, in remote);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        public static SnapshotDatagram ReadSnapshot(ReadOnlyMemory<byte> bytes, CanonicalReader reader)
        {
            reader.Reset(bytes);
            ReadHeader(reader, ServerAuthoritativeDatagramKind.Snapshot);
            SnapshotDatagramHeader.CanonicalRequireSchema(reader);
            SnapshotDatagramHeader header = SnapshotDatagramHeader.CanonicalRead(reader);
            var value = new SnapshotDatagram(in header, reader.ReadBytesSegment());
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
