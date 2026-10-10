using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace ThirdPersonSimulation
{
    public sealed class SemanticIrArtifactVersionException : IOException
    {
        public SemanticIrArtifactVersionException(string message) : base(message) { }
    }

    public readonly struct SemanticIrLoadExpectation
    {
        public SemanticIrLoadExpectation(
            ProgramId programId,
            string compilerVersion,
            OperationSetVersion operationSetVersion,
            int tickRate,
            ProgramRevision sourceRevision,
            SemanticHash semanticHash,
            GameplayAbilityRootDescriptor root)
        {
            if (!programId.IsValid)
                throw new ArgumentException("Program id is required.", nameof(programId));
            CompilerVersion = SimulationIdentity.Require(compilerVersion, nameof(compilerVersion));
            if (!operationSetVersion.IsValid)
                throw new ArgumentException("Operation-set version is required.", nameof(operationSetVersion));
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (string.IsNullOrEmpty(sourceRevision.Value))
                throw new ArgumentException("Source revision is required.", nameof(sourceRevision));
            if (!semanticHash.IsValid)
                throw new ArgumentException("Semantic hash is required.", nameof(semanticHash));
            if (!root.IsValid || !root.IsAbility)
                throw new ArgumentException("Semantic IR requires an Ability root descriptor.", nameof(root));
            ProgramId = programId;
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            SourceRevision = sourceRevision;
            SemanticHash = semanticHash;
            Root = root;
        }

        public ProgramId ProgramId { get; }
        public string CompilerVersion { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public ProgramRevision SourceRevision { get; }
        public SemanticHash SemanticHash { get; }
        public GameplayAbilityRootDescriptor Root { get; }
    }

    public sealed class GameplayAbilitySemanticIrArtifactHeader
    {
        readonly ReadOnlyCollection<string> m_GameplayCapabilities;

        internal GameplayAbilitySemanticIrArtifactHeader(
            uint magic,
            int artifactVersion,
            int payloadVersion,
            ProgramId programId,
            string compilerVersion,
            OperationSetVersion operationSetVersion,
            int tickRate,
            ProgramRevision sourceRevision,
            SemanticHash semanticHash,
            IEnumerable<string> gameplayCapabilities,
            GameplayAbilityRootDescriptor root)
        {
            Magic = magic;
            ArtifactVersion = artifactVersion;
            PayloadVersion = payloadVersion;
            ProgramId = programId;
            CompilerVersion = SimulationIdentity.Require(compilerVersion, nameof(compilerVersion));
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            SourceRevision = sourceRevision;
            SemanticHash = semanticHash;
            var capabilities = new GameplayAbilityCapabilityManifest(gameplayCapabilities);
            m_GameplayCapabilities = new List<string>(capabilities.GameplayCapabilities).AsReadOnly();
            if (!root.IsValid || !root.IsAbility)
                throw new ArgumentException("Semantic IR artifact requires an Ability root descriptor.", nameof(root));
            Root = root;
        }

        public uint Magic { get; }
        public int ArtifactVersion { get; }
        public int PayloadVersion { get; }
        public ProgramId ProgramId { get; }
        public string CompilerVersion { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public ProgramRevision SourceRevision { get; }
        public SemanticHash SemanticHash { get; }
        public IReadOnlyList<string> GameplayCapabilities => m_GameplayCapabilities;
        public GameplayAbilityRootDescriptor Root { get; }
    }

    public sealed class ValidatedSemanticIrArtifact
    {
        readonly byte[] m_CanonicalBytes;

        internal ValidatedSemanticIrArtifact(
            GameplayAbilitySemanticIrArtifactHeader header,
            byte[] canonicalBytes,
            GameplayAbilitySemanticIr semanticIr)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            m_CanonicalBytes = canonicalBytes == null ? throw new ArgumentNullException(nameof(canonicalBytes)) : (byte[])canonicalBytes.Clone();
            SemanticIr = semanticIr ?? throw new ArgumentNullException(nameof(semanticIr));
        }

        public GameplayAbilitySemanticIrArtifactHeader Header { get; }
        public ReadOnlyMemory<byte> CanonicalBytes => m_CanonicalBytes;
        public GameplayAbilitySemanticIr SemanticIr { get; }
        public byte[] ToArray() => (byte[])m_CanonicalBytes.Clone();
    }

    public static class GameplayAbilitySemanticIrCodec
    {
        const uint ArtifactMagic = 0x52495343;
        const int ArtifactVersion = 19;
        const int PayloadVersion = 19;

        public static byte[] WriteArtifact(GameplayAbilitySemanticIr semanticIr)
        {
            return CreateValidatedArtifact(semanticIr).ToArray();
        }

        public static ValidatedSemanticIrArtifact CreateValidatedArtifact(GameplayAbilitySemanticIr semanticIr)
        {
            if (semanticIr == null)
                throw new ArgumentNullException(nameof(semanticIr));
            byte[] payload = WritePayload(semanticIr);
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(ArtifactMagic);
            writer.WriteInt32(ArtifactVersion);
            writer.WriteInt32(PayloadVersion);
            writer.WriteText(semanticIr.Manifest.ProgramId.Value);
            writer.WriteText(semanticIr.Manifest.CompilerVersion);
            writer.WriteText(semanticIr.Manifest.OperationSetVersion.Value);
            writer.WriteInt32(semanticIr.Manifest.TickRate);
            writer.WriteText(semanticIr.Manifest.SourceRevision.Value);
            writer.WriteHash(semanticIr.SemanticHash);
            writer.WriteInt32(semanticIr.Manifest.Capabilities.GameplayCapabilities.Count);
            for (int i = 0; i < semanticIr.Manifest.Capabilities.GameplayCapabilities.Count; i++)
                writer.WriteText(semanticIr.Manifest.Capabilities.GameplayCapabilities[i]);
            GameplayAbilityRootDescriptorCodec.Write(writer, semanticIr.Manifest.Root);
            writer.WriteBytes(payload);
            byte[] bytes = writer.ToArray();
            var header = new GameplayAbilitySemanticIrArtifactHeader(
                    ArtifactMagic,
                    ArtifactVersion,
                    PayloadVersion,
                    semanticIr.Manifest.ProgramId,
                    semanticIr.Manifest.CompilerVersion,
                    semanticIr.Manifest.OperationSetVersion,
                    semanticIr.Manifest.TickRate,
                    semanticIr.Manifest.SourceRevision,
                    semanticIr.SemanticHash,
                    semanticIr.Manifest.Capabilities.GameplayCapabilities,
                    semanticIr.Manifest.Root);
            return new ValidatedSemanticIrArtifact(header, bytes, semanticIr);
        }

        public static GameplayAbilitySemanticIrArtifactHeader ReadArtifactHeader(byte[] bytes)
        {
            return ReadEnvelope(bytes).Header;
        }

        public static ValidatedSemanticIrArtifact ReadValidatedArtifact(byte[] bytes)
        {
            ArtifactEnvelope envelope = ReadEnvelope(bytes);
            GameplayAbilitySemanticIr semanticIr = ReadPayload(envelope.Payload);
            ValidateHeaderAgainstPayload(envelope.Header, semanticIr);
            return new ValidatedSemanticIrArtifact(envelope.Header, bytes, semanticIr);
        }

        public static ValidatedSemanticIrArtifact ReadValidatedArtifact(byte[] bytes, SemanticIrLoadExpectation expectation)
        {
            ValidatedSemanticIrArtifact artifact = ReadValidatedArtifact(bytes);
            ValidateExpectation(artifact.Header, expectation);
            return artifact;
        }

        public static GameplayAbilitySemanticIr ReadArtifact(byte[] bytes, SemanticIrLoadExpectation expectation)
        {
            return ReadValidatedArtifact(bytes, expectation).SemanticIr;
        }

        static ArtifactEnvelope ReadEnvelope(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            uint magic = reader.ReadUInt32();
            if (magic != ArtifactMagic)
                throw new InvalidDataException("Gameplay Semantic IR artifact magic is invalid.");
            int artifactVersion = reader.ReadInt32();
            if (artifactVersion != ArtifactVersion)
                throw new SemanticIrArtifactVersionException("Gameplay Semantic IR artifact version is unsupported.");
            int payloadVersion = reader.ReadInt32();
            if (payloadVersion != PayloadVersion)
                throw new SemanticIrArtifactVersionException("Gameplay Semantic IR payload version is unsupported.");
            var programId = new ProgramId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            string compilerVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var operationSetVersion = new OperationSetVersion(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            int tickRate = reader.ReadInt32();
            if (tickRate <= 0)
                throw new InvalidDataException("Gameplay Semantic IR tick rate is invalid.");
            var sourceRevision = new ProgramRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            var semanticHash = new SemanticHash(reader.ReadHash());
            int capabilityCount = GameplayAbilitySemanticsCodec.ReadCount(reader);
            var gameplayCapabilities = new string[capabilityCount];
            for (int i = 0; i < capabilityCount; i++)
                gameplayCapabilities[i] = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            GameplayAbilityRootDescriptor root = GameplayAbilityRootDescriptorCodec.Read(reader);
            byte[] payload = reader.ReadBytes();
            reader.RequireComplete();
            var header = new GameplayAbilitySemanticIrArtifactHeader(
                magic,
                artifactVersion,
                payloadVersion,
                programId,
                compilerVersion,
                operationSetVersion,
                tickRate,
                sourceRevision,
                semanticHash,
                gameplayCapabilities,
                root);
            if (!SequenceEqual(gameplayCapabilities, header.GameplayCapabilities))
                throw new InvalidDataException("Gameplay Semantic IR capability identities are not in canonical order.");
            return new ArtifactEnvelope(header, payload);
        }

        static void ValidateHeaderAgainstPayload(GameplayAbilitySemanticIrArtifactHeader header, GameplayAbilitySemanticIr semanticIr)
        {
            GameplayAbilitySemanticIrManifest manifest = semanticIr.Manifest;
            if (!manifest.ProgramId.Equals(header.ProgramId) ||
                !string.Equals(manifest.CompilerVersion, header.CompilerVersion, StringComparison.Ordinal) ||
                !manifest.OperationSetVersion.Equals(header.OperationSetVersion) ||
                manifest.TickRate != header.TickRate ||
                !manifest.SourceRevision.Equals(header.SourceRevision) ||
                !semanticIr.SemanticHash.Equals(header.SemanticHash) ||
                manifest.Root != header.Root ||
                !SequenceEqual(manifest.Capabilities.GameplayCapabilities, header.GameplayCapabilities))
            {
                throw new InvalidDataException("Semantic IR artifact header does not match its payload manifest.");
            }
        }

        static void ValidateExpectation(GameplayAbilitySemanticIrArtifactHeader header, SemanticIrLoadExpectation expectation)
        {
            if (!header.ProgramId.Equals(expectation.ProgramId))
                throw new InvalidDataException($"Semantic IR ProgramId '{header.ProgramId}' does not match expected '{expectation.ProgramId}'.");
            if (!string.Equals(header.CompilerVersion, expectation.CompilerVersion, StringComparison.Ordinal))
                throw new InvalidDataException($"Semantic IR compiler version '{header.CompilerVersion}' does not match expected '{expectation.CompilerVersion}'.");
            if (!header.OperationSetVersion.Equals(expectation.OperationSetVersion))
                throw new InvalidDataException($"Semantic IR operation-set version '{header.OperationSetVersion}' does not match expected '{expectation.OperationSetVersion}'.");
            if (header.TickRate != expectation.TickRate)
                throw new InvalidDataException($"Semantic IR tick rate '{header.TickRate}' does not match expected '{expectation.TickRate}'.");
            if (!header.SourceRevision.Equals(expectation.SourceRevision))
                throw new InvalidDataException($"Semantic IR source revision '{header.SourceRevision}' does not match expected '{expectation.SourceRevision}'.");
            if (!header.SemanticHash.Equals(expectation.SemanticHash))
                throw new InvalidDataException($"Semantic IR hash '{header.SemanticHash}' does not match expected '{expectation.SemanticHash}'.");
            if (header.Root != expectation.Root)
                throw new InvalidDataException($"Semantic IR root '{header.Root}' does not match expected '{expectation.Root}'.");
        }

        static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            if (left.Count != right.Count)
                return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        public static SemanticHash ComputeHash(GameplayAbilitySemanticIr semanticIr)
        {
            using var writer = new CanonicalWriter();
            WritePayload(writer, semanticIr);
            return new SemanticHash(writer.ComputeHash());
        }

        static byte[] WritePayload(GameplayAbilitySemanticIr semanticIr)
        {
            using var writer = new CanonicalWriter();
            WritePayload(writer, semanticIr);
            return writer.ToArray();
        }

        static void WritePayload(CanonicalWriter writer, GameplayAbilitySemanticIr semanticIr)
        {
            writer.WriteInt32(PayloadVersion);
            WriteManifest(writer, semanticIr.Manifest);
            WriteTable(writer, semanticIr.Literals, WriteLiteral);
            WriteTable(writer, semanticIr.Operations, WriteOperation);
            WriteTable(writer, semanticIr.ConstantInputBindings, WriteConstantInputBinding);
            WriteTable(writer, semanticIr.ControlFlow, GameplayAbilitySemanticsCodec.WriteControlFlow);
            WriteTable(writer, semanticIr.References, GameplayAbilitySemanticsCodec.WriteReference);
            WriteTable(writer, semanticIr.GraphCallFrames, GameplayAbilitySemanticsCodec.WriteGraphCallFrame);
            WriteTable(writer, semanticIr.StateDeclarations, (target, value) => GameplayAbilitySemanticsCodec.WriteStateSlot(target, value, true));
            WriteTable(writer, semanticIr.Scopes, GameplayAbilitySemanticsCodec.WriteScope);
            WriteTable(writer, semanticIr.OutputChannels, GameplayAbilitySemanticsCodec.WriteOutputChannel);
            WriteTable(writer, semanticIr.CatalogEntries, GameplayAbilitySemanticsCodec.WriteCatalogEntry);
            GameplayAbilitySemanticsCodec.WriteSourceMapTable(writer, semanticIr.SourceMap);
            WriteTable(writer, semanticIr.Producers, GameplayAbilitySemanticsCodec.WriteProducer);
        }

        static GameplayAbilitySemanticIr ReadPayload(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadInt32() != PayloadVersion)
                throw new SemanticIrArtifactVersionException("Gameplay Semantic IR payload version is unsupported.");
            GameplayAbilitySemanticIrManifest manifest = ReadManifest(reader);
            SemanticLiteral[] literals = ReadTable(reader, ReadLiteral);
            SemanticOperation[] operations = ReadTable(reader, ReadOperation);
            SemanticConstantInputBinding[] constantInputBindings = ReadTable(reader, ReadConstantInputBinding);
            ProgramControlFlowEdge[] controlFlow = ReadTable(reader, GameplayAbilitySemanticsCodec.ReadControlFlow);
            ProgramReference[] references = ReadTable(reader, GameplayAbilitySemanticsCodec.ReadReference);
            ProgramGraphCallFrame[] graphCallFrames = ReadTable(reader, GameplayAbilitySemanticsCodec.ReadGraphCallFrame);
            ProgramStateSlot[] stateDeclarations = ReadTable(reader, GameplayAbilitySemanticsCodec.ReadStateSlot);
            ProgramScopeLayout[] scopes = ReadTable(reader, GameplayAbilitySemanticsCodec.ReadScope);
            ProgramOutputChannelLayout[] outputChannels = ReadTable(reader, GameplayAbilitySemanticsCodec.ReadOutputChannel);
            ProgramCatalogEntry[] catalogEntries = ReadTable(reader, GameplayAbilitySemanticsCodec.ReadCatalogEntry);
            ProgramSourceMapEntry[] sourceMap = GameplayAbilitySemanticsCodec.ReadSourceMapTable(reader);
            ProgramProducer[] producers = ReadTable(reader, GameplayAbilitySemanticsCodec.ReadProducer);
            reader.RequireComplete();
            return new GameplayAbilitySemanticIr(
                manifest,
                operations,
                literals,
                constantInputBindings,
                controlFlow,
                references,
                stateDeclarations,
                scopes,
                outputChannels,
                catalogEntries,
                sourceMap,
                producers,
                graphCallFrames);
        }

        static void WriteConstantInputBinding(CanonicalWriter writer, SemanticConstantInputBinding binding)
        {
            writer.WriteInt32(binding.TargetOperation.Value);
            writer.WriteText(binding.TargetPort);
            writer.WriteInt32(binding.ConstantIndex);
            writer.WriteByte((byte)binding.ResolvedValueKind);
        }

        static SemanticConstantInputBinding ReadConstantInputBinding(CanonicalReader reader)
        {
            var operation = new OperationHandle(reader.ReadInt32());
            string port = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            int constant = reader.ReadInt32();
            SemanticValueKind valueKind = ReadSemanticValueKind(reader.ReadByte());
            return new SemanticConstantInputBinding(operation, port, constant, valueKind);
        }

        static void WriteManifest(CanonicalWriter writer, GameplayAbilitySemanticIrManifest manifest)
        {
            writer.WriteText(manifest.ProgramId.Value);
            writer.WriteText(manifest.CompilerVersion);
            writer.WriteText(manifest.OperationSetVersion.Value);
            writer.WriteInt32(manifest.TickRate);
            writer.WriteText(manifest.SourceRevision.Value);
            writer.WriteInt32(manifest.Capabilities.GameplayCapabilities.Count);
            for (int i = 0; i < manifest.Capabilities.GameplayCapabilities.Count; i++)
                writer.WriteText(manifest.Capabilities.GameplayCapabilities[i]);
            GameplayAbilityRootDescriptorCodec.Write(writer, manifest.Root);
        }

        static GameplayAbilitySemanticIrManifest ReadManifest(CanonicalReader reader)
        {
            var programId = new ProgramId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            string compilerVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var operationSetVersion = new OperationSetVersion(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            int tickRate = reader.ReadInt32();
            var sourceRevision = new ProgramRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            int capabilityCount = GameplayAbilitySemanticsCodec.ReadCount(reader);
            var gameplayCapabilities = new string[capabilityCount];
            for (int i = 0; i < capabilityCount; i++)
                gameplayCapabilities[i] = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            GameplayAbilityRootDescriptor root = GameplayAbilityRootDescriptorCodec.Read(reader);
            return new GameplayAbilitySemanticIrManifest(
                programId,
                compilerVersion,
                operationSetVersion,
                tickRate,
                sourceRevision,
                new GameplayAbilityCapabilityManifest(gameplayCapabilities),
                root);
        }

        static void WriteLiteral(CanonicalWriter writer, SemanticLiteral literal)
        {
            writer.WriteInt32(literal.Index);
            writer.WriteText(literal.Identity);
            writer.WriteByte((byte)literal.Kind);
            writer.WriteByte((byte)literal.Precision);
            switch (literal.Kind)
            {
                case SemanticLiteralKind.Boolean: writer.WriteBoolean(literal.Boolean); break;
                case SemanticLiteralKind.Int32: writer.WriteInt32(literal.Int32); break;
                case SemanticLiteralKind.UInt64: writer.WriteUInt64(literal.UInt64); break;
                case SemanticLiteralKind.Number: writer.WriteDouble(literal.X); break;
                case SemanticLiteralKind.Vector2: writer.WriteDouble(literal.X); writer.WriteDouble(literal.Y); break;
                case SemanticLiteralKind.Vector3: writer.WriteDouble(literal.X); writer.WriteDouble(literal.Y); writer.WriteDouble(literal.Z); break;
                case SemanticLiteralKind.Yaw: writer.WriteDouble(literal.X); break;
                case SemanticLiteralKind.String: writer.WriteText(literal.Text); break;
                case SemanticLiteralKind.Document: WriteDocument(writer, literal.Document); break;
                case SemanticLiteralKind.Quaternion:
                    writer.WriteDouble(literal.X); writer.WriteDouble(literal.Y);
                    writer.WriteDouble(literal.Z); writer.WriteDouble(literal.W); break;
                case SemanticLiteralKind.Enum:
                    writer.WriteText(literal.Text); writer.WriteInt32(literal.Int32); break;
                default: throw new InvalidDataException($"Unsupported Semantic literal kind '{literal.Kind}'.");
            }
        }

        static SemanticLiteral ReadLiteral(CanonicalReader reader)
        {
            int index = reader.ReadInt32();
            string identity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            SemanticLiteralKind kind = ReadLiteralKind(reader.ReadByte());
            SemanticNumericPrecision precision = ReadNumericPrecision(reader.ReadByte());
            return kind switch
            {
                SemanticLiteralKind.Boolean => SemanticLiteral.FromBoolean(index, identity, reader.ReadBoolean()),
                SemanticLiteralKind.Int32 => SemanticLiteral.FromInt32(index, identity, reader.ReadInt32()),
                SemanticLiteralKind.UInt64 => SemanticLiteral.FromUInt64(index, identity, reader.ReadUInt64()),
                SemanticLiteralKind.Number => SemanticLiteral.FromNumber(index, identity, reader.ReadDouble(), precision),
                SemanticLiteralKind.Vector2 => SemanticLiteral.FromVector2(index, identity, reader.ReadDouble(), reader.ReadDouble(), precision),
                SemanticLiteralKind.Vector3 => SemanticLiteral.FromVector3(index, identity, reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), precision),
                SemanticLiteralKind.Yaw => SemanticLiteral.FromYaw(index, identity, reader.ReadDouble(), precision),
                SemanticLiteralKind.String => SemanticLiteral.FromString(index, identity, CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                SemanticLiteralKind.Document => SemanticLiteral.FromDocument(index, identity, ReadDocument(reader)),
                SemanticLiteralKind.Quaternion => SemanticLiteral.FromQuaternion(index, identity,
                    reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()),
                SemanticLiteralKind.Enum => SemanticLiteral.FromEnum(index, identity,
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadInt32()),
                _ => throw new InvalidDataException($"Unsupported Semantic literal kind '{kind}'.")
            };
        }

        static void WriteDocument(CanonicalWriter writer, SemanticDataDocument document)
        {
            writer.WriteInt32(document.Tokens.Count);
            for (int i = 0; i < document.Tokens.Count; i++)
            {
                SemanticDataToken token = document.Tokens[i];
                writer.WriteByte((byte)token.Kind);
                switch (token.Kind)
                {
                    case SemanticDataTokenKind.Boolean: writer.WriteBoolean(token.Boolean); break;
                    case SemanticDataTokenKind.Int32: writer.WriteInt32(token.Int32); break;
                    case SemanticDataTokenKind.UInt32: writer.WriteUInt32(token.UInt32); break;
                    case SemanticDataTokenKind.UInt64: writer.WriteUInt64(token.UInt64); break;
                    case SemanticDataTokenKind.String: writer.WriteText(token.Text); break;
                    case SemanticDataTokenKind.Number:
                        writer.WriteDouble(token.Number);
                        writer.WriteText(token.SourceIdentity);
                        writer.WriteByte((byte)token.Precision);
                        break;
                    case SemanticDataTokenKind.Bytes: writer.WriteBytes(token.Bytes.ToArray()); break;
                    default: throw new InvalidDataException($"Unsupported Semantic document token '{token.Kind}'.");
                }
            }
        }

        static SemanticDataDocument ReadDocument(CanonicalReader reader)
        {
            int count = GameplayAbilitySemanticsCodec.ReadCount(reader);
            var tokens = new SemanticDataToken[count];
            for (int i = 0; i < count; i++)
            {
                SemanticDataTokenKind kind = ReadDataTokenKind(reader.ReadByte());
                tokens[i] = kind switch
                {
                    SemanticDataTokenKind.Boolean => SemanticDataToken.FromBoolean(reader.ReadBoolean()),
                    SemanticDataTokenKind.Int32 => SemanticDataToken.FromInt32(reader.ReadInt32()),
                    SemanticDataTokenKind.UInt32 => SemanticDataToken.FromUInt32(reader.ReadUInt32()),
                    SemanticDataTokenKind.UInt64 => SemanticDataToken.FromUInt64(reader.ReadUInt64()),
                    SemanticDataTokenKind.String => SemanticDataToken.FromString(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                    SemanticDataTokenKind.Number => SemanticDataToken.FromNumber(reader.ReadDouble(), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), ReadNumericPrecision(reader.ReadByte())),
                    SemanticDataTokenKind.Bytes => SemanticDataToken.FromBytes(reader.ReadBytes()),
                    _ => throw new InvalidDataException($"Unsupported Semantic document token '{kind}'.")
                };
            }
            return new SemanticDataDocument(tokens);
        }

        static SemanticLiteralKind ReadLiteralKind(byte value)
        {
            if (value < (byte)SemanticLiteralKind.Boolean || value > (byte)SemanticLiteralKind.Enum)
                throw new InvalidDataException($"Enum value '{value}' is invalid for SemanticLiteralKind.");
            return (SemanticLiteralKind)value;
        }

        static SemanticValueKind ReadSemanticValueKind(byte value)
        {
            if (value < (byte)SemanticValueKind.Boolean || value > (byte)SemanticValueKind.Enum)
                throw new InvalidDataException($"Semantic constant input contains unknown value kind '{value}'.");
            return (SemanticValueKind)value;
        }

        static SemanticNumericPrecision ReadNumericPrecision(byte value)
        {
            if (value < (byte)SemanticNumericPrecision.Exact || value > (byte)SemanticNumericPrecision.TargetRounded)
                throw new InvalidDataException($"Enum value '{value}' is invalid for SemanticNumericPrecision.");
            return (SemanticNumericPrecision)value;
        }

        static SemanticDataTokenKind ReadDataTokenKind(byte value)
        {
            if (value < (byte)SemanticDataTokenKind.Boolean || value > (byte)SemanticDataTokenKind.Bytes)
                throw new InvalidDataException($"Enum value '{value}' is invalid for SemanticDataTokenKind.");
            return (SemanticDataTokenKind)value;
        }

        static void WriteOperation(CanonicalWriter writer, SemanticOperation operation)
        {
            writer.WriteInt32(operation.Handle.Value);
            writer.WriteText(operation.TemplateIdentity);
            writer.WriteInt32((int)operation.Code);
            GameplayAbilitySemanticsCodec.WriteIntArray(writer, operation.Operands);
            GameplayAbilitySemanticsCodec.WriteIntArray(writer, operation.LiteralReferences);
            GameplayAbilitySemanticsCodec.WriteIntArray(writer, operation.StateSlots);
            writer.WriteInt32(operation.Integer0);
            writer.WriteInt32(operation.Integer1);
            writer.WriteUInt64(operation.Unsigned0);
            writer.WriteDouble(operation.Number0);
            writer.WriteText(operation.Number0SourceIdentity);
            writer.WriteText(operation.Text0);
            writer.WriteUInt32(operation.Flags);
        }

        static SemanticOperation ReadOperation(CanonicalReader reader)
        {
            return new SemanticOperation(
                new OperationHandle(reader.ReadInt32()),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                GameplayAbilitySemanticsCodec.ReadOperationCode(reader.ReadInt32()),
                GameplayAbilitySemanticsCodec.ReadIntArray(reader),
                GameplayAbilitySemanticsCodec.ReadIntArray(reader),
                GameplayAbilitySemanticsCodec.ReadIntArray(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadUInt64(),
                reader.ReadDouble(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                reader.ReadUInt32());
        }

        static void WriteTable<T>(CanonicalWriter writer, IReadOnlyList<T> values, Action<CanonicalWriter, T> write)
        {
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
                write(writer, values[i]);
        }

        static T[] ReadTable<T>(CanonicalReader reader, Func<CanonicalReader, T> read)
        {
            int count = GameplayAbilitySemanticsCodec.ReadCount(reader);
            var values = new T[count];
            for (int i = 0; i < count; i++)
                values[i] = read(reader);
            return values;
        }

        readonly struct ArtifactEnvelope
        {
            public ArtifactEnvelope(GameplayAbilitySemanticIrArtifactHeader header, byte[] payload)
            {
                Header = header;
                Payload = payload;
            }

            public GameplayAbilitySemanticIrArtifactHeader Header { get; }
            public byte[] Payload { get; }
        }
    }
}
