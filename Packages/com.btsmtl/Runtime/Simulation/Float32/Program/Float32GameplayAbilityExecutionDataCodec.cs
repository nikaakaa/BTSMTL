using System;
using System.Collections.Generic;
using System.IO;
using static ThirdPersonSimulation.GameplayAbilitySemanticsCodec;

namespace ThirdPersonSimulation
{
    public static class Float32GameplayAbilityExecutionDataCodec
    {
        const uint ArtifactMagic = 0x46414244;
        const int ArtifactVersion = 10;
        const int PayloadVersion = 11;

        public static byte[] WriteArtifact(Float32GameplayAbilityExecutionData data)
        {
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(ArtifactMagic);
            writer.WriteInt32(ArtifactVersion);
            writer.WriteInt32(PayloadVersion);
            writer.WriteHash(GameplayAbilityExecutionManifest.CanonicalSchemaHash);
            GameplayAbilityExecutionManifest.CanonicalWrite(writer, data.Manifest);
            Float32GraphExecutionDataCodec.WriteTables(writer, data);
            return writer.ToArray();
        }

        public static Float32GameplayAbilityExecutionData ReadArtifact(byte[] bytes, string definitionGuid,
            string canonicalBytesHash, GameplayAbilityExecutionManifest published)
        {
            CanonicalReader reader = CreateArtifactReader(bytes, canonicalBytesHash);
            GameplayAbilityExecutionManifest.CanonicalWrite(reader.MatchWriter, published);
            RequireAuthoringIdentity(published, definitionGuid);
            return ReadGraph(reader, published);
        }

        public static CanonicalReader CreateArtifactReader(byte[] bytes, string canonicalBytesHash)
        {
            if (!string.Equals(ComputeCanonicalBytesHash(bytes).ToString(), canonicalBytesHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Float32 Gameplay Ability artifact canonical bytes hash is invalid.");
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != ArtifactMagic)
                throw new InvalidDataException("Float32 Gameplay Ability artifact magic is invalid.");
            if (reader.ReadInt32() != ArtifactVersion)
                throw new InvalidDataException("Float32 Gameplay Ability artifact version is unsupported.");
            if (reader.ReadInt32() != PayloadVersion)
                throw new InvalidDataException("Float32 Gameplay Ability payload version is unsupported.");
            GameplayAbilityExecutionManifest.CanonicalRequireSchema(reader);
            return reader;
        }

        public static Float32GameplayAbilityExecutionData ReadGraph(CanonicalReader reader,
            GameplayAbilityExecutionManifest manifest)
        {
            Float32GraphExecutionData graph = Float32GraphExecutionDataCodec.ReadTables(reader);
            if (graph.Chunks.ComputeContentHash(manifest.SemanticHash, manifest.NumericProfile, manifest.TickRate,
                    manifest.AbilityId.Value) != manifest.ContentHash)
                throw new InvalidDataException("Float32 Gameplay Ability content hash is invalid.");
            var catalog = new GameplayAbilityExecutionCatalog(graph.CatalogEntries, graph.References);
            GameplayAbilityExecutionBinding binding = catalog.Require(manifest.AbilityId);
            GameplayAbilityProviderContract providerContract = GameplayAbilityProviderContract.Create(
                graph.CatalogEntries, index => graph.Constants[index].Int32);
            return new Float32GameplayAbilityExecutionData(manifest, binding, providerContract, graph);
        }

        public static StableHash ComputeCanonicalBytesHash(byte[] bytes) => SimulationCanonicalPayloadHash.Compute(bytes);

        public static void RequireAuthoringIdentity(GameplayAbilityExecutionManifest manifest, string definitionGuid)
        {
            RequireDefinitionGuid(definitionGuid);
            if (!string.Equals(manifest.Root.RootIdentity, definitionGuid, StringComparison.Ordinal) ||
                !string.Equals(manifest.Root.EntryIdentity, "ability:" + manifest.AbilityId.Value, StringComparison.Ordinal))
                throw new InvalidDataException("Gameplay Ability artifact authoring identity is inconsistent.");
        }

        static void RequireDefinitionGuid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 32)
                throw new InvalidDataException("Gameplay Ability definition GUID is invalid.");
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') ||
                      (c >= 'a' && c <= 'f') ||
                      (c >= 'A' && c <= 'F')))
                    throw new InvalidDataException("Gameplay Ability definition GUID is invalid.");
            }
        }

    }
}
