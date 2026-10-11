using System;
using System.Collections.Generic;
using System.IO;
using static ThirdPersonSimulation.GameplayAbilitySemanticsCodec;

namespace ThirdPersonSimulation
{
    public readonly struct Float32GameplayAbilityExecutionDataLoadExpectation
    {
        public Float32GameplayAbilityExecutionDataLoadExpectation(
            string definitionGuid,
            string abilityId,
            string compilerVersion,
            string operationSetVersion,
            string sourceRevision,
            string semanticHash,
            string numericProfileId,
            int targetAbiVersion,
            string executionIdentity,
            string executionDataHash,
            string stateSchemaHash,
            string canonicalBytesHash,
            GameplayAbilityRootDescriptor root)
        {
            DefinitionGuid = definitionGuid;
            AbilityId = abilityId;
            CompilerVersion = compilerVersion;
            OperationSetVersion = operationSetVersion;
            SourceRevision = sourceRevision;
            SemanticHash = semanticHash;
            NumericProfileId = numericProfileId;
            TargetAbiVersion = targetAbiVersion;
            ExecutionIdentity = executionIdentity;
            ExecutionDataHash = executionDataHash;
            StateSchemaHash = stateSchemaHash;
            CanonicalBytesHash = canonicalBytesHash;
            Root = root;
        }

        public string DefinitionGuid { get; }
        public string AbilityId { get; }
        public string CompilerVersion { get; }
        public string OperationSetVersion { get; }
        public string SourceRevision { get; }
        public string SemanticHash { get; }
        public string NumericProfileId { get; }
        public int TargetAbiVersion { get; }
        public string ExecutionIdentity { get; }
        public string ExecutionDataHash { get; }
        public string StateSchemaHash { get; }
        public string CanonicalBytesHash { get; }
        public GameplayAbilityRootDescriptor Root { get; }
    }

    public static class Float32GameplayAbilityExecutionDataCodec
    {
        const uint ArtifactMagic = 0x46414244;
        const int ArtifactVersion = 4;
        const int PayloadVersion = 5;

        public static byte[] WriteArtifact(Float32GameplayAbilityExecutionData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            byte[] payload = WritePayload(data);
            using var writer = new CanonicalWriter();
            writer.WriteUInt32(ArtifactMagic);
            writer.WriteInt32(ArtifactVersion);
            writer.WriteText(data.CompilerVersion);
            writer.WriteText(data.OperationSetVersion.Value);
            writer.WriteText(data.SourceRevision.Value);
            writer.WriteHash(data.SemanticHash);
            SimulationNumericProfileCodec.Write(writer, data.NumericProfile);
            writer.WriteText(data.ExecutionIdentity);
            writer.WriteHash(data.ContentHash);
            writer.WriteHash(data.StateSchemaHash);
            WriteCapabilities(writer, data.Capabilities);
            GameplayAbilityRootDescriptorCodec.Write(writer, data.Root);
            writer.WriteText(data.AbilityId.Value);
            writer.WriteBytes(payload);
            return writer.ToArray();
        }

        public static Float32GameplayAbilityExecutionData ReadArtifact(
            byte[] bytes,
            Float32GameplayAbilityExecutionDataLoadExpectation expectation)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("Float32 Gameplay Ability artifact is empty.");
            RequireDefinitionGuid(expectation.DefinitionGuid);
            StableHash bytesHash = ComputeCanonicalBytesHash(bytes);
            if (!bytesHash.IsValid || !string.Equals(bytesHash.ToString(), expectation.CanonicalBytesHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Float32 Gameplay Ability artifact canonical bytes hash is invalid.");
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != ArtifactMagic)
                throw new InvalidDataException("Float32 Gameplay Ability artifact magic is invalid.");
            if (reader.ReadInt32() != ArtifactVersion)
                throw new InvalidDataException("Float32 Gameplay Ability artifact version is unsupported.");
            string compilerVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            string operationSetVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            string sourceRevision = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            string semanticHash = reader.ReadHash().ToString();
            SimulationNumericProfile numericProfile = SimulationNumericProfileCodec.Read(reader);
            string executionIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            string contentHash = reader.ReadHash().ToString();
            string stateSchemaHash = reader.ReadHash().ToString();
            GameplayAbilityCapabilityManifest capabilities = ReadCapabilities(reader);
            GameplayAbilityRootDescriptor root = GameplayAbilityRootDescriptorCodec.Read(reader);
            string abilityId = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            byte[] payload = reader.ReadBytes();
            reader.RequireComplete();
            RequireMetadata(
                compilerVersion,
                operationSetVersion,
                sourceRevision,
                semanticHash,
                numericProfile,
                executionIdentity,
                contentHash,
                stateSchemaHash,
                root,
                expectation);
            CharacterSkillId expectedAbilityId = new CharacterSkillId(expectation.AbilityId);
            if (!string.Equals(abilityId, expectedAbilityId.Value, StringComparison.Ordinal))
                throw new InvalidDataException("Float32 Gameplay Ability artifact Ability identity is inconsistent.");
            Float32GameplayAbilityExecutionData data = ReadPayload(payload);
            if (!string.Equals(data.CompilerVersion, compilerVersion, StringComparison.Ordinal) ||
                !data.OperationSetVersion.Equals(new OperationSetVersion(operationSetVersion)) ||
                !data.SourceRevision.Equals(new ProgramRevision(sourceRevision)) ||
                !data.SemanticHash.Equals(new SemanticHash(new StableHash(semanticHash))) ||
                data.NumericProfile != numericProfile ||
                !string.Equals(data.ExecutionIdentity, executionIdentity, StringComparison.Ordinal) ||
                !data.ContentHash.Equals(new StableHash(contentHash)) ||
                !data.StateSchemaHash.Equals(new StableHash(stateSchemaHash)) ||
                data.Root != root ||
                !data.AbilityId.Equals(expectedAbilityId) ||
                !CapabilitiesEqual(data.Capabilities, capabilities))
                throw new InvalidDataException("Float32 Gameplay Ability artifact header does not match its payload.");
            return data;
        }

        public static StableHash ComputeCanonicalBytesHash(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            using var writer = new CanonicalWriter();
            writer.WriteRawBytes(bytes, 0, bytes.Length);
            return writer.ComputeHash();
        }

        static byte[] WritePayload(Float32GameplayAbilityExecutionData data)
        {
            using var writer = new CanonicalWriter();
            writer.WriteInt32(PayloadVersion);
            writer.WriteText(data.CompilerVersion);
            writer.WriteText(data.OperationSetVersion.Value);
            writer.WriteInt32(data.TickRate);
            writer.WriteText(data.SourceRevision.Value);
            writer.WriteHash(data.SemanticHash);
            SimulationNumericProfileCodec.Write(writer, data.NumericProfile);
            writer.WriteText(data.ExecutionIdentity);
            writer.WriteHash(data.ContentHash);
            writer.WriteHash(data.StateSchemaHash);
            WriteCapabilities(writer, data.Capabilities);
            GameplayAbilityRootDescriptorCodec.Write(writer, data.Root);
            writer.WriteText(data.AbilityId.Value);
            Float32GraphExecutionDataCodec.WriteTables(writer, data);
            return writer.ToArray();
        }

        static Float32GameplayAbilityExecutionData ReadPayload(
            byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadInt32() != PayloadVersion)
                throw new InvalidDataException("Float32 Gameplay Ability payload version is unsupported.");
            string compilerVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            OperationSetVersion operationSetVersion = new OperationSetVersion(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            int tickRate = reader.ReadInt32();
            ProgramRevision sourceRevision = new ProgramRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            SemanticHash semanticHash = SemanticHash.CanonicalFromHash(reader.ReadHash());
            SimulationNumericProfile numericProfile = SimulationNumericProfileCodec.Read(reader);
            string executionIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            StableHash contentHash = reader.ReadHash();
            StableHash stateSchemaHash = reader.ReadHash();
            GameplayAbilityCapabilityManifest capabilities = ReadCapabilities(reader);
            GameplayAbilityRootDescriptor root = GameplayAbilityRootDescriptorCodec.Read(reader);
            CharacterSkillId abilityId = new CharacterSkillId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            Float32GraphExecutionData graph = Float32GraphExecutionDataCodec.ReadTables(
                reader, Float32GameplayAbilityExecutionData.ResolveRootOperation);
            GameplayAbilityExecutionCatalog catalog = new GameplayAbilityExecutionCatalog(graph.CatalogEntries, graph.References);
            GameplayAbilityExecutionBinding binding = catalog.Require(abilityId);
            GameplayAbilityProviderContract providerContract = GameplayAbilityProviderContract.Create(
                graph.CatalogEntries,
                index => graph.Constants[index].Int32);
            Float32GameplayAbilityExecutionData data = Float32GameplayAbilityExecutionData.Create(
                abilityId,
                binding,
                providerContract,
                compilerVersion,
                operationSetVersion,
                tickRate,
                sourceRevision,
                semanticHash,
                numericProfile,
                capabilities,
                root,
                executionIdentity,
                stateSchemaHash,
                graph);
            if (!data.ContentHash.Equals(contentHash))
                throw new InvalidDataException("Float32 Gameplay Ability payload content hash is invalid.");
            return data;
        }

        static void RequireMetadata(
            string compilerVersion,
            string operationSetVersion,
            string sourceRevision,
            string semanticHash,
            SimulationNumericProfile numericProfile,
            string executionIdentity,
            string contentHash,
            string stateSchemaHash,
            GameplayAbilityRootDescriptor root,
            Float32GameplayAbilityExecutionDataLoadExpectation expectation)
        {
            if (!string.Equals(compilerVersion, expectation.CompilerVersion, StringComparison.Ordinal) ||
                !string.Equals(operationSetVersion, expectation.OperationSetVersion, StringComparison.Ordinal) ||
                !string.Equals(sourceRevision, expectation.SourceRevision, StringComparison.Ordinal) ||
                !string.Equals(semanticHash, expectation.SemanticHash, StringComparison.Ordinal) ||
                !string.Equals(numericProfile.Id.Value, expectation.NumericProfileId, StringComparison.Ordinal) ||
                numericProfile.AbiVersion.Value != expectation.TargetAbiVersion ||
                !string.Equals(executionIdentity, expectation.ExecutionIdentity, StringComparison.Ordinal) ||
                !string.Equals(contentHash, expectation.ExecutionDataHash, StringComparison.Ordinal) ||
                !string.Equals(stateSchemaHash, expectation.StateSchemaHash, StringComparison.Ordinal) ||
                root != expectation.Root ||
                !root.IsAbility ||
                !string.Equals(root.RootIdentity, expectation.DefinitionGuid, StringComparison.Ordinal) ||
                !string.Equals(root.EntryIdentity, "ability:" + expectation.AbilityId, StringComparison.Ordinal))
                throw new InvalidDataException("Float32 Gameplay Ability artifact metadata is invalid.");
        }

        static void WriteCapabilities(CanonicalWriter writer, GameplayAbilityCapabilityManifest capabilities)
        {
            writer.WriteInt32(capabilities.GameplayCapabilities.Count);
            for (int i = 0; i < capabilities.GameplayCapabilities.Count; i++)
                writer.WriteText(capabilities.GameplayCapabilities[i]);
        }

        static GameplayAbilityCapabilityManifest ReadCapabilities(CanonicalReader reader)
        {
            int count = ReadCount(reader);
            var values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            return new GameplayAbilityCapabilityManifest(values);
        }

        static bool CapabilitiesEqual(
            GameplayAbilityCapabilityManifest left,
            GameplayAbilityCapabilityManifest right)
        {
            if (left.GameplayCapabilities.Count != right.GameplayCapabilities.Count)
                return false;
            for (int i = 0; i < left.GameplayCapabilities.Count; i++)
            {
                if (!string.Equals(left.GameplayCapabilities[i], right.GameplayCapabilities[i], StringComparison.Ordinal))
                    return false;
            }
            return true;
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
