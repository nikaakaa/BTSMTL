using System;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public static class ServerAuthoritativeCanonicalCodec
    {
        const uint InputMagic = 0x49415343;
        const uint BaselineMagic = 0x42415343;
        public const int InputSchemaVersion = 5;
        public const int BaselineSchemaVersion = 7;
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
            SimulationNumericProfile.CanonicalWrite(writer, input.NumericProfile);
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
            SimulationNumericProfile profile = BeginReadInput(bytes, reader, textResolver);
            SimulationInput result = Float32SimulationInputCodec.Read(reader, profile);
            reader.RequireComplete();
            return result;
        }

        public static SimulationInput ReadInput(ArraySegment<byte> bytes, CanonicalReader reader,
            ICanonicalTextResolver textResolver, Float32SimulationInputFrameStorage storage)
        {
            SimulationNumericProfile profile = BeginReadInput(bytes, reader, textResolver);
            SimulationInput result = Float32SimulationInputCodec.Read(reader, profile, storage);
            reader.RequireComplete();
            return result;
        }

        static SimulationNumericProfile BeginReadInput(ArraySegment<byte> bytes, CanonicalReader reader,
            ICanonicalTextResolver textResolver)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(textResolver);
            if (reader.ReadUInt32() != InputMagic || reader.ReadInt32() != InputSchemaVersion)
                throw new InvalidDataException("ServerAuthoritative canonical input header is invalid.");
            return SimulationNumericProfile.CanonicalReadPublished(reader, Float32SimulationNumericProfile.Value);
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
            writer.WriteHash(WorldBodyState.CanonicalSchemaHash);
            writer.WriteText(baseline.ActorId.Value);
            writer.WriteUInt64(baseline.AuthorityTick.Value);
            SimulationNumericProfile.CanonicalWrite(writer, baseline.NumericProfile);
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
            WorldBodyState.CanonicalWrite(writer, baseline.Body);
            writer.WriteHash(baseline.BodyHash);
            writer.WriteUInt64(baseline.ConfirmedInputSequence);
            writer.WriteUInt64(baseline.ConfirmedEventHorizon.Sequence);
            writer.WriteEventId(baseline.ConfirmedEventHorizon.EventId);
        }

        internal static AuthoritativeActorBaseline ReadBaseline(ArraySegment<byte> bytes,
            CanonicalReader reader, CanonicalHashWriter bodyHashWriter)
        {
            reader.Reset(bytes);
            reader.SetTextResolver(CanonicalInitializationTextResolver.Instance);
            if (reader.ReadUInt32() != BaselineMagic)
                throw new InvalidDataException("ServerAuthoritative baseline magic is invalid.");
            int version = reader.ReadInt32();
            if (version != BaselineSchemaVersion)
                throw new InvalidDataException($"ServerAuthoritative baseline schema version '{version}' is unsupported.");
            WorldBodyState.CanonicalRequireSchema(reader);
            var actorId = new ActorId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var tick = new SimulationTick(reader.ReadUInt64());
            SimulationNumericProfile numericProfile = SimulationNumericProfile.CanonicalReadPublished(reader, Float32SimulationNumericProfile.Value);
            var targetAbiVersion = new TargetAbiVersion(reader.ReadInt32());
            string stateCodecIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var gameplayContentHash = GameplayContentHash.CanonicalFromHash(reader.ReadHash());
            var operationSet = new OperationSetVersion(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            byte[] stateBytes = reader.ReadBytes();
            var stateHash = CharacterStateHash.CanonicalFromHash(reader.ReadHash());
            var worldRevision = new WorldRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var solverId = new SolverImplementationId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            string solverVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var solverCapabilities = (WorldCapability)reader.ReadUInt64();
            WorldBodyState body = WorldBodyState.CanonicalRead(reader);
            var encodedBodyHash = reader.ReadHash();
            ulong confirmedInputSequence = reader.ReadUInt64();
            ulong eventSequence = reader.ReadUInt64();
            EventId eventId = reader.ReadEventId();
            reader.RequireComplete();
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
                new ServerAuthoritativeEventHorizon(eventSequence, eventId),
                bodyHashWriter);
            if (!result.BodyHash.Equals(encodedBodyHash))
                throw new InvalidDataException("Authoritative baseline body hash does not match its canonical body.");
            return result;
        }

        public static StableHash ComputeBodyHash(WorldBodyState body, CanonicalHashWriter writer)
        {
            writer.Reset();
            writer.WriteText("server-authoritative-body/3");
            writer.WriteHash(WorldBodyState.CanonicalSchemaHash);
            WorldBodyState.CanonicalWrite(writer, body);
            return writer.ComputeHash();
        }






    }
}
