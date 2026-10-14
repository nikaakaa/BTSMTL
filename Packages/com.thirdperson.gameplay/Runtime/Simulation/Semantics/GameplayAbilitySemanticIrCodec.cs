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

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class GameplayAbilitySemanticIrArtifactHeader
    {
        internal GameplayAbilitySemanticIrArtifactHeader(GameplayAbilitySemanticIrManifest manifest, SemanticHash semanticHash)
        {
            Manifest = manifest;
            SemanticHash = semanticHash;
        }

        public uint Magic => GameplayAbilitySemanticIrCodec.ArtifactMagic;
        public int ArtifactVersion => GameplayAbilitySemanticIrCodec.ArtifactVersion;
        public int PayloadVersion => GameplayAbilitySemanticIrCodec.PayloadVersion;
        public GameplayAbilitySemanticIrManifest Manifest { get; }
        public SemanticHash SemanticHash { get; }
        public ProgramId ProgramId => Manifest.ProgramId;
        public string CompilerVersion => Manifest.CompilerVersion;
        public OperationSetVersion OperationSetVersion => Manifest.OperationSetVersion;
        public int TickRate => Manifest.TickRate;
        public ProgramRevision SourceRevision => Manifest.SourceRevision;
        public IReadOnlyList<string> GameplayCapabilities => Manifest.Capabilities.GameplayCapabilities;
        public GameplayAbilityRootDescriptor Root => Manifest.Root;

    }

    public sealed class ValidatedSemanticIrArtifact
    {
        byte[] m_CanonicalBytes;

        internal ValidatedSemanticIrArtifact(
            GameplayAbilitySemanticIrArtifactHeader header,
            byte[] canonicalBytes,
            GameplayAbilitySemanticIr semanticIr)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            m_CanonicalBytes = canonicalBytes == null ? null : (byte[])canonicalBytes.Clone();
            SemanticIr = semanticIr ?? throw new ArgumentNullException(nameof(semanticIr));
        }

        public GameplayAbilitySemanticIrArtifactHeader Header { get; }
        public ReadOnlyMemory<byte> CanonicalBytes => m_CanonicalBytes ??= GameplayAbilitySemanticIrCodec.PackArtifact(SemanticIr);
        public GameplayAbilitySemanticIr SemanticIr { get; }
        public byte[] ToArray() => CanonicalBytes.ToArray();
    }

    public static class GameplayAbilitySemanticIrCodec
    {
        internal const uint ArtifactMagic = 0x52495343;
        internal const int ArtifactVersion = 24;
        internal const int PayloadVersion = 24;

        public static byte[] WriteArtifact(GameplayAbilitySemanticIr semanticIr)
        {
            return CreateValidatedArtifact(semanticIr).ToArray();
        }

        public static ValidatedSemanticIrArtifact CreateValidatedArtifact(GameplayAbilitySemanticIr semanticIr)
        {
            return new ValidatedSemanticIrArtifact(CreateHeader(semanticIr), null, semanticIr);
        }

        static GameplayAbilitySemanticIrArtifactHeader CreateHeader(GameplayAbilitySemanticIr semanticIr) =>
            new GameplayAbilitySemanticIrArtifactHeader(semanticIr.Manifest, semanticIr.SemanticHash);

        internal static byte[] PackArtifact(GameplayAbilitySemanticIr semanticIr)
        {
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(ArtifactMagic);
            writer.WriteInt32(ArtifactVersion);
            writer.WriteInt32(PayloadVersion);
            writer.WriteHash(GameplayAbilitySemanticIrArtifactHeader.CanonicalSchemaHash);
            GameplayAbilitySemanticIrArtifactHeader.CanonicalWrite(writer, CreateHeader(semanticIr));
            GameplayAbilitySemanticsCodec.WriteGraphSourceRevisions(writer, semanticIr.GraphSourceRevisions);
            GraphExecutionChunksCodec.Write(writer, semanticIr.Chunks);
            byte[] bytes = writer.ToArray();
            return bytes;
        }

        public static GameplayAbilitySemanticIrArtifactHeader ReadArtifactHeader(byte[] bytes)
        {
            return ReadEnvelope(bytes).Header;
        }

        public static ValidatedSemanticIrArtifact ReadValidatedArtifact(byte[] bytes)
        {
            ArtifactEnvelope envelope = ReadEnvelope(bytes);
            GameplayAbilitySemanticIr semanticIr = ReadGraph(envelope.Header.Manifest, envelope.Revisions, envelope.Chunks);
            if (!semanticIr.SemanticHash.Equals(envelope.Header.SemanticHash))
                throw new InvalidDataException("Semantic IR artifact hash does not match its graph chunks.");
            return new ValidatedSemanticIrArtifact(envelope.Header, bytes, semanticIr);
        }

        static ArtifactEnvelope ReadEnvelope(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            RequireArtifactFormat(reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32());
            GameplayAbilitySemanticIrArtifactHeader.CanonicalRequireSchema(reader);
            GameplayAbilitySemanticIrArtifactHeader header = GameplayAbilitySemanticIrArtifactHeader.CanonicalReadMaterialized(reader);
            IReadOnlyList<ProgramGraphSourceRevision> revisions = GameplayAbilitySemanticsCodec.ReadGraphSourceRevisions(reader);
            GraphExecutionChunks chunks = GraphExecutionChunksCodec.Read(reader);
            reader.RequireComplete();
            return new ArtifactEnvelope(header, revisions, chunks);
        }

        internal static void RequireArtifactFormat(uint magic, int artifactVersion, int payloadVersion)
        {
            if (magic != ArtifactMagic)
                throw new InvalidDataException("Gameplay Semantic IR artifact magic is invalid.");
            if (artifactVersion != ArtifactVersion)
                throw new SemanticIrArtifactVersionException("Gameplay Semantic IR artifact version is unsupported.");
            if (payloadVersion != PayloadVersion)
                throw new SemanticIrArtifactVersionException("Gameplay Semantic IR payload version is unsupported.");
        }

        public static SemanticHash ComputeHash(GameplayAbilitySemanticIr semanticIr) =>
            new SemanticHash(semanticIr.Chunks.ExecutionHash);

        internal static GraphExecutionChunks CreateChunks(GameplayAbilitySemanticIr semanticIr)
        {
            var structure = GraphExecutionChunk.Encode(writer => WriteStructure(writer, semanticIr));
            var parameters = GraphExecutionChunk.Encode(writer => WriteParameters(writer, semanticIr, structure.Hash));
            return new GraphExecutionChunks(structure, parameters, semanticIr.GraphSources.DiagnosticChunk);
        }

        internal static StableHash ComputeStateLayoutHash(GameplayAbilitySemanticIr semanticIr)
        {
            var writer = new CanonicalHashWriter();
            writer.WriteHash(SemanticStateLayout.CanonicalSchemaHash);
            SemanticStateLayout.CanonicalWrite(writer, semanticIr.StateLayout);
            return writer.ComputeHash();
        }

        static StableHash StructureContract(GameplayAbilitySemanticIrManifest manifest)
        {
            using var writer = new CanonicalWriter();
            writer.WriteInt32(PayloadVersion);
            GameplayAbilitySemanticIrContract.CanonicalWrite(writer, manifest.Contract);
            return writer.ComputeHash();
        }

        static void WriteStructure(CanonicalWriter writer, GameplayAbilitySemanticIr semanticIr)
        {
            writer.WriteHash(StructureContract(semanticIr.Manifest));
            writer.WriteHash(SemanticGraphTables.CanonicalSchemaHash);
            SemanticGraphTables.CanonicalWriteLayout(writer, semanticIr.Tables);
        }

        internal static void WriteParameters(CanonicalWriter writer, GameplayAbilitySemanticIr semanticIr,
            StableHash structureHash)
        {
            writer.WriteHash(structureHash);
            SemanticGraphTables.CanonicalWritePayload(writer, semanticIr.Tables);
        }

        static GameplayAbilitySemanticIr ReadGraph(GameplayAbilitySemanticIrManifest manifest,
            IEnumerable<ProgramGraphSourceRevision> graphSourceRevisions, GraphExecutionChunks chunks)
        {
            var reader = chunks.Structure.CreateReader();
            var parameterReader = chunks.Parameters.CreateReader();
            if (reader.ReadHash() != StructureContract(manifest))
                throw new InvalidDataException("Semantic graph structure contract does not match its manifest.");
            if (parameterReader.ReadHash() != chunks.Structure.Hash)
                throw new InvalidDataException("Semantic graph parameters or diagnostic sources use a different structure.");
            SemanticGraphTables.CanonicalRequireSchema(reader);
            SemanticGraphTables tables = SemanticGraphTables.CanonicalReadMaterialized(reader, parameterReader);
            reader.RequireComplete();
            parameterReader.RequireComplete();
            ProgramExecutionSourceLayout executionSources = ProgramExecutionSourceLayout.FromChunk(tables.Structure.ExecutionSources);
            ProgramGraphSources graphSources = ProgramGraphSources.Read(executionSources, chunks.Sources, graphSourceRevisions);
            var semanticIr = new GameplayAbilitySemanticIr(manifest, tables, graphSources);
            semanticIr.m_Chunks = chunks;
            return semanticIr;
        }

        readonly struct ArtifactEnvelope
        {
            public ArtifactEnvelope(GameplayAbilitySemanticIrArtifactHeader header,
                IReadOnlyList<ProgramGraphSourceRevision> revisions, GraphExecutionChunks chunks)
            {
                Header = header;
                Revisions = revisions;
                Chunks = chunks;
            }

            public GameplayAbilitySemanticIrArtifactHeader Header { get; }
            public IReadOnlyList<ProgramGraphSourceRevision> Revisions { get; }
            public GraphExecutionChunks Chunks { get; }
        }
    }
}
