using System;
using System.Collections.Generic;
using System.IO;
using ThirdPersonSimulation.Fixed;

namespace ThirdPersonSimulation.Serialization
{

    public static class MemoryPackGameplayAbilityContent
    {
        const string Format = "btsmtl.ability.memorypack/1";

        public static byte[] PublishFixed(FixedGameplayAbilityExecutionData data) =>
            MemoryPackContent.Serialize((Format, data.Manifest, data.Tables,
                data.GraphSources.Diagnostics, data.GraphSourceRevisions.Sources));

        public static byte[] PublishFloat32(Float32GameplayAbilityExecutionData data) =>
            MemoryPackContent.Serialize((Format, data.Manifest, data.Tables,
                data.GraphSources.Diagnostics, data.GraphSourceRevisions.Sources));

        public static FixedGameplayAbilityExecutionData ReadFixed(byte[] bytes, string authoringGuid, string bytesHash)
        {
            RequireBytes(bytes, bytesHash);
            var content = MemoryPackContent.Deserialize<(string Format, GameplayAbilityExecutionManifest Manifest,
                FixedGraphExecutionTables Tables, IReadOnlyList<ProgramSourceMapEntry> Diagnostics,
                IReadOnlyList<ProgramGraphSourceRevision> Revisions)>(bytes);
            RequireManifest(content.Format, content.Manifest, authoringGuid, FixedSimulationNumericProfile.Value);
            var graph = new FixedGraphExecutionData(content.Tables,
                MemoryPackGraphContentPublisher.Instance.ReadSources(content.Tables.Structure.ExecutionSources, content.Diagnostics, content.Revisions));
            RequireContent(content.Manifest, graph.Chunks);
            var catalog = new GameplayAbilityExecutionCatalog(graph.CatalogEntries, graph.References);
            return new FixedGameplayAbilityExecutionData(content.Manifest, catalog.Require(content.Manifest.AbilityId),
                GameplayAbilityProviderContract.Create(graph.CatalogEntries, index => graph.Constants[index].Int32), graph);
        }

        public static Float32GameplayAbilityExecutionData ReadFloat32(byte[] bytes, string authoringGuid, string bytesHash)
        {
            RequireBytes(bytes, bytesHash);
            var content = MemoryPackContent.Deserialize<(string Format, GameplayAbilityExecutionManifest Manifest,
                Float32GraphExecutionTables Tables, IReadOnlyList<ProgramSourceMapEntry> Diagnostics,
                IReadOnlyList<ProgramGraphSourceRevision> Revisions)>(bytes);
            RequireManifest(content.Format, content.Manifest, authoringGuid, Float32SimulationNumericProfile.Value);
            var graph = new Float32GraphExecutionData(content.Tables,
                MemoryPackGraphContentPublisher.Instance.ReadSources(content.Tables.Structure.ExecutionSources, content.Diagnostics, content.Revisions));
            RequireContent(content.Manifest, graph.Chunks);
            var catalog = new GameplayAbilityExecutionCatalog(graph.CatalogEntries, graph.References);
            return new Float32GameplayAbilityExecutionData(content.Manifest, catalog.Require(content.Manifest.AbilityId),
                GameplayAbilityProviderContract.Create(graph.CatalogEntries, index => graph.Constants[index].Int32), graph);
        }

        static void RequireBytes(byte[] bytes, string bytesHash)
        {
            if (SimulationCanonicalPayloadHash.Compute(bytes).Value != bytesHash)
                throw new InvalidDataException("Gameplay Ability artifact bytes hash is invalid.");
        }

        static void RequireManifest(string format, GameplayAbilityExecutionManifest manifest,
            string authoringGuid, SimulationNumericProfile profile)
        {
            if (format != Format)
                throw new InvalidDataException("Gameplay Ability artifact format is unsupported.");
            if (manifest.Root.RootIdentity != authoringGuid ||
                manifest.Root.EntryIdentity != "ability:" + manifest.AbilityId.Value)
                throw new InvalidDataException("Gameplay Ability artifact authoring identity is inconsistent.");
            if (manifest.NumericProfile != profile)
                throw new InvalidDataException("Gameplay Ability artifact numeric profile does not match its target.");
            GameplayAbilityOperationSet.RequireVersion(manifest.OperationSetVersion);
        }

        static void RequireContent(GameplayAbilityExecutionManifest manifest, GraphExecutionChunks chunks)
        {
            if (MemoryPackGraphContentPublisher.Instance.ComputeContentHash(chunks, manifest.SemanticHash,
                    manifest.NumericProfile, manifest.TickRate, manifest.AbilityId.Value) != manifest.ContentHash)
                throw new InvalidDataException("Gameplay Ability content hash is invalid.");
        }
    }
}
