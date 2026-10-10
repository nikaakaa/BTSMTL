using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public static class ServerAuthoritativeCanonicalCodec
    {
        const uint InputMagic = 0x49415343;
        const uint BaselineMagic = 0x42415343;
        public const int InputSchemaVersion = 4;
        public const int BaselineSchemaVersion = 5;
        const int MaximumCollectionCount = 4096;
        internal const int MaximumInputTextValues = 3 + 3 * MaximumCollectionCount;

        public static byte[] WriteInput(SimulationInput input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            using var writer = new CanonicalWriter();
            WriteInput(writer, input);
            return writer.ToArray();
        }

        public static void WriteLengthPrefixedInput(CanonicalWriter writer, SimulationInput input)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (input == null)
                throw new ArgumentNullException(nameof(input));
            long prefixPosition = writer.BeginLengthPrefixedBlock();
            WriteInput(writer, input);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void WriteInput(CanonicalWriter writer, SimulationInput input)
        {
            writer.WriteUInt32(InputMagic);
            writer.WriteInt32(InputSchemaVersion);
            SimulationNumericProfileCodec.Write(writer, input.NumericProfile);
            Float32SimulationInputCodec.Write(writer, input);
        }

        public static SimulationInput ReadInput(ArraySegment<byte> bytes)
        {
            var reader = new CanonicalReader(Array.Empty<byte>());
            return ReadInput(bytes, reader, CanonicalInitializationTextResolver.Instance);
        }

        public static SimulationInput ReadInput(ArraySegment<byte> bytes, CanonicalReader reader,
            ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            if (reader.ReadUInt32() != InputMagic || reader.ReadInt32() != InputSchemaVersion)
                throw new InvalidDataException("ServerAuthoritative canonical input header is invalid.");
            SimulationNumericProfile profile = SimulationNumericProfileCodec.Read(reader, Float32SimulationNumericProfile.Value);
            SimulationInput result = Float32SimulationInputCodec.Read(reader, profile);
            reader.RequireComplete();
            return result;
        }

        public static byte[] WriteBaseline(AuthoritativeActorBaseline baseline)
        {
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            using var writer = new CanonicalWriter();
            WriteBaseline(writer, baseline);
            return writer.ToArray();
        }

        internal static void WriteLengthPrefixedBaseline(CanonicalWriter writer, AuthoritativeActorBaseline baseline)
        {
            if (!baseline.IsValid)
                throw new ArgumentOutOfRangeException(nameof(baseline));
            long prefixPosition = writer.BeginLengthPrefixedBlock();
            WriteBaseline(writer, baseline);
            writer.EndLengthPrefixedBlock(prefixPosition);
        }

        static void WriteBaseline(CanonicalWriter writer, AuthoritativeActorBaseline baseline)
        {
            writer.WriteUInt32(BaselineMagic);
            writer.WriteInt32(BaselineSchemaVersion);
            writer.WriteText(baseline.ActorId.Value);
            writer.WriteUInt64(baseline.AuthorityTick.Value);
            SimulationNumericProfileCodec.Write(writer, baseline.NumericProfile);
            writer.WriteInt32(baseline.TargetAbiVersion.Value);
            writer.WriteText(baseline.StateCodecIdentity);
            writer.WriteHash(baseline.GameplayContentHash);
            writer.WriteText(baseline.OperationSetVersion.Value);
            writer.WriteBytes(baseline.CharacterStateBytes.Span);
            writer.WriteHash(baseline.StateHash);
            writer.WriteText(baseline.WorldRevision.Value);
            writer.WriteText(baseline.SolverId.Value);
            writer.WriteText(baseline.SolverVersion);
            writer.WriteUInt64((ulong)baseline.SolverCapabilities);
            WriteBody(writer, baseline.Body);
            writer.WriteHash(baseline.BodyHash);
            writer.WriteUInt64(baseline.ConfirmedInputSequence);
            writer.WriteUInt64(baseline.ConfirmedEventHorizon.Sequence);
            writer.WriteEventId(baseline.ConfirmedEventHorizon.EventId);
        }

        public static AuthoritativeActorBaseline ReadBaseline(ArraySegment<byte> bytes)
        {
            var reader = Reader(bytes, BaselineMagic, BaselineSchemaVersion, "ServerAuthoritative baseline");
            var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var tick = new SimulationTick(reader.ReadUInt64());
            SimulationNumericProfile numericProfile = SimulationNumericProfileCodec.Read(reader, Float32SimulationNumericProfile.Value);
            var targetAbiVersion = new TargetAbiVersion(reader.ReadInt32());
            string stateCodecIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var gameplayContentHash = new GameplayContentHash(reader.ReadHash());
            var operationSet = new OperationSetVersion(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            byte[] stateBytes = reader.ReadBytes();
            var stateHash = new CharacterStateHash(reader.ReadHash());
            var worldRevision = new WorldRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var solverId = new SolverImplementationId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            string solverVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var solverCapabilities = (WorldCapability)reader.ReadUInt64();
            WorldBodyState body = ReadBody(reader);
            var encodedBodyHash = reader.ReadHash();
            ulong confirmedInputSequence = reader.ReadUInt64();
            ulong eventSequence = reader.ReadUInt64();
            EventId eventId = reader.ReadEventId();
            reader.RequireComplete();
            if (body.ActorId != actorId)
                throw new InvalidDataException("Authoritative baseline body ActorId does not match the baseline.");
            StableHash actualBodyHash = ComputeBodyHash(body);
            if (!actualBodyHash.Equals(encodedBodyHash))
                throw new InvalidDataException("Authoritative baseline body hash does not match its canonical body.");
            var result = new AuthoritativeActorBaseline(
                actorId,
                tick,
                numericProfile,
                targetAbiVersion,
                stateCodecIdentity,
                gameplayContentHash,
                operationSet,
                stateBytes,
                stateHash,
                worldRevision,
                solverId,
                solverVersion,
                solverCapabilities,
                body,
                confirmedInputSequence,
                new ServerAuthoritativeEventHorizon(eventSequence, eventId));
            using var writer = new CanonicalWriter();
            WriteBaseline(writer, result);
            RequireCanonical(bytes.AsSpan(), writer, "ServerAuthoritative baseline");
            return result;
        }

        public static StableHash ComputeBodyHash(WorldBodyState body)
        {
            using var writer = new CanonicalWriter();
            writer.WriteText("server-authoritative-body/2");
            WriteBody(writer, body);
            return writer.ComputeHash();
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

        static WorldBodyState ReadBody(CanonicalReader reader)
        {
            return new WorldBodyState(
                new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                reader.ReadVector3(),
                reader.ReadYaw(),
                reader.ReadVector3(),
                reader.ReadScalar(),
                reader.ReadBoolean(),
                (WorldCollisionSummary)reader.ReadUInt32());
        }

        static CanonicalReader Reader(ArraySegment<byte> bytes, uint magic, int expectedVersion, string label)
        {
            if (bytes.Array == null)
                throw new ArgumentNullException(nameof(bytes));
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != magic)
                throw new InvalidDataException($"{label} magic is invalid.");
            int version = reader.ReadInt32();
            if (version != expectedVersion)
                throw new InvalidDataException($"{label} schema version '{version}' is unsupported.");
            return reader;
        }

        static int ReadCount(CanonicalReader reader, string label)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumCollectionCount)
                throw new InvalidDataException($"Canonical {label} count '{count}' is invalid.");
            return count;
        }

        static void RequireCanonical(ReadOnlySpan<byte> source, CanonicalWriter canonical, string label)
        {
            if (!canonical.ContentEquals(source))
                throw new InvalidDataException($"{label} is not canonical.");
        }
    }
}
