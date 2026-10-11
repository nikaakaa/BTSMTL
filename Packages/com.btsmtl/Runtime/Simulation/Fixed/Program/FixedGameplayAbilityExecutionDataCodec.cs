using System;
using System.Collections.Generic;
using System.IO;
using static ThirdPersonSimulation.GameplayAbilitySemanticsCodec;

namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedGameplayAbilityExecutionDataLoadExpectation
    {
        public FixedGameplayAbilityExecutionDataLoadExpectation(
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

    public static class FixedGameplayAbilityExecutionDataCodec
    {
        const uint ArtifactMagic = 0x46414246;
        const int ArtifactVersion = 4;
        const int PayloadVersion = 5;

        public static byte[] WriteArtifact(FixedGameplayAbilityExecutionData data)
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

        public static FixedGameplayAbilityExecutionData ReadArtifact(
            byte[] bytes,
            FixedGameplayAbilityExecutionDataLoadExpectation expectation)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("Fixed Gameplay Ability artifact is empty.");
            RequireDefinitionGuid(expectation.DefinitionGuid);
            StableHash bytesHash = ComputeCanonicalBytesHash(bytes);
            if (!bytesHash.IsValid || !string.Equals(bytesHash.ToString(), expectation.CanonicalBytesHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Fixed Gameplay Ability artifact canonical bytes hash is invalid.");
            var reader = new CanonicalReader(bytes);
            if (reader.ReadUInt32() != ArtifactMagic)
                throw new InvalidDataException("Fixed Gameplay Ability artifact magic is invalid.");
            if (reader.ReadInt32() != ArtifactVersion)
                throw new InvalidDataException("Fixed Gameplay Ability artifact version is unsupported.");
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
                throw new InvalidDataException("Fixed Gameplay Ability artifact Ability identity is inconsistent.");
            FixedGameplayAbilityExecutionData data = ReadPayload(payload);
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
                throw new InvalidDataException("Fixed Gameplay Ability artifact header does not match its payload.");
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

        static byte[] WritePayload(FixedGameplayAbilityExecutionData data)
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
            WriteTable(writer, data.Constants, WriteConstant);
            WriteTable(writer, data.OperationDefinitions, WriteOperationDefinition);
            WriteTable(writer, data.Operations, WriteOperation);
            WriteTable(writer, data.ConstantInputBindings, WriteConstantInputBinding);
            WriteTable(writer, data.ControlFlow, WriteControlFlow);
            WriteTable(writer, data.References, WriteReference);
            WriteTable(writer, data.GraphCallFrames, WriteGraphCallFrame);
            WriteTable(writer, data.StateSlots, (target, value) => WriteStateSlot(target, value, true));
            WriteTable(writer, data.Scopes, WriteScope);
            WriteTable(writer, data.OutputChannels, WriteOutputChannel);
            WriteTable(writer, data.CatalogEntries, WriteCatalogEntry);
            WriteSourceMapTable(writer, data.SourceMap);
            WriteTable(writer, data.Producers, WriteProducer);
            return writer.ToArray();
        }

        static FixedGameplayAbilityExecutionData ReadPayload(
            byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadInt32() != PayloadVersion)
                throw new InvalidDataException("Fixed Gameplay Ability payload version is unsupported.");
            string compilerVersion = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            OperationSetVersion operationSetVersion = new OperationSetVersion(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            int tickRate = reader.ReadInt32();
            ProgramRevision sourceRevision = new ProgramRevision(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            SemanticHash semanticHash = new SemanticHash(reader.ReadHash());
            SimulationNumericProfile numericProfile = SimulationNumericProfileCodec.Read(reader);
            string executionIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            StableHash contentHash = reader.ReadHash();
            StableHash stateSchemaHash = reader.ReadHash();
            GameplayAbilityCapabilityManifest capabilities = ReadCapabilities(reader);
            GameplayAbilityRootDescriptor root = GameplayAbilityRootDescriptorCodec.Read(reader);
            CharacterSkillId abilityId = new CharacterSkillId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            ProgramConstant[] constants = ReadTable(reader, ReadConstant);
            SimulationOperationDefinition[] operationDefinitions = ReadTable(reader, ReadOperationDefinition);
            int operationCount = ReadCount(reader);
            var operations = new SimulationOperation[operationCount];
            for (int i = 0; i < operationCount; i++)
                operations[i] = ReadOperation(reader, operationDefinitions);
            ProgramConstantInputBinding[] constantInputBindings = ReadTable(reader, ReadConstantInputBinding);
            ProgramControlFlowEdge[] controlFlow = ReadTable(reader, ReadControlFlow);
            ProgramReference[] references = ReadTable(reader, ReadReference);
            ProgramGraphCallFrame[] graphCallFrames = ReadTable(reader, ReadGraphCallFrame);
            ProgramStateSlot[] stateSlots = ReadTable(reader, ReadStateSlot);
            ProgramScopeLayout[] scopes = ReadTable(reader, ReadScope);
            ProgramOutputChannelLayout[] outputChannels = ReadTable(reader, ReadOutputChannel);
            ProgramCatalogEntry[] catalogEntries = ReadTable(reader, ReadCatalogEntry);
            ProgramSourceMapEntry[] sourceMap = ReadSourceMapTable(reader);
            ProgramProducer[] producers = ReadTable(reader, ReadProducer);
            reader.RequireComplete();
            GameplayAbilityExecutionCatalog catalog = new GameplayAbilityExecutionCatalog(catalogEntries, references);
            GameplayAbilityExecutionBinding binding = catalog.Require(abilityId);
            GameplayAbilityProviderContract providerContract = GameplayAbilityProviderContract.Create(
                catalogEntries,
                index => constants[index].Int32);
            FixedGameplayAbilityExecutionData data = FixedGameplayAbilityExecutionData.Create(
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
                operationDefinitions,
                operations,
                constants,
                constantInputBindings,
                controlFlow,
                references,
                graphCallFrames,
                stateSlots,
                scopes,
                outputChannels,
                catalogEntries,
                sourceMap,
                producers);
            if (!data.ContentHash.Equals(contentHash))
                throw new InvalidDataException("Fixed Gameplay Ability payload content hash is invalid.");
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
            FixedGameplayAbilityExecutionDataLoadExpectation expectation)
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
                throw new InvalidDataException("Fixed Gameplay Ability artifact metadata is invalid.");
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

        static void WriteConstantInputBinding(CanonicalWriter writer, ProgramConstantInputBinding binding)
        {
            writer.WriteInt32(binding.TargetOperation.Value);
            writer.WriteText(binding.TargetPort);
            writer.WriteInt32(binding.ConstantIndex);
            writer.WriteByte((byte)binding.ResolvedValueKind);
        }

        static ProgramConstantInputBinding ReadConstantInputBinding(CanonicalReader reader)
        {
            var operation = new OperationHandle(reader.ReadInt32());
            string port = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            int constant = reader.ReadInt32();
            byte kindValue = reader.ReadByte();
            var kind = (SemanticValueKind)kindValue;
            if (kind < SemanticValueKind.Boolean || kind > SemanticValueKind.Identity)
                throw new InvalidDataException($"Program constant input contains unknown value kind '{kindValue}'.");
            return new ProgramConstantInputBinding(operation, port, constant, kind);
        }

        static void WriteConstant(CanonicalWriter writer, ProgramConstant value)
        {
            writer.WriteInt32(value.Index);
            writer.WriteText(value.Identity);
            writer.WriteByte((byte)value.Kind);
            switch (value.Kind)
            {
                case ProgramConstantKind.Boolean: writer.WriteBoolean(value.Boolean); break;
                case ProgramConstantKind.Int32: writer.WriteInt32(value.Int32); break;
                case ProgramConstantKind.UInt64: writer.WriteUInt64(value.UInt64); break;
                case ProgramConstantKind.Scalar: writer.WriteScalar(value.Scalar); break;
                case ProgramConstantKind.Vector2: writer.WriteVector2(value.Vector2); break;
                case ProgramConstantKind.Vector3: writer.WriteVector3(value.Vector3); break;
                case ProgramConstantKind.Yaw: writer.WriteYaw(value.Yaw); break;
                case ProgramConstantKind.String: writer.WriteText(value.Text); break;
                case ProgramConstantKind.Bytes: writer.WriteBytes(value.Bytes.ToArray()); break;
                default: throw new InvalidDataException($"Unsupported constant kind '{value.Kind}'.");
            }
        }

        static ProgramConstant ReadConstant(CanonicalReader reader)
        {
            int index = reader.ReadInt32();
            string identity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            ProgramConstantKind kind = ReadConstantKind(reader.ReadByte());
            switch (kind)
            {
                case ProgramConstantKind.Boolean: return ProgramConstant.FromBoolean(index, identity, reader.ReadBoolean());
                case ProgramConstantKind.Int32: return ProgramConstant.FromInt32(index, identity, reader.ReadInt32());
                case ProgramConstantKind.UInt64: return ProgramConstant.FromUInt64(index, identity, reader.ReadUInt64());
                case ProgramConstantKind.Scalar: return ProgramConstant.FromScalar(index, identity, reader.ReadScalar());
                case ProgramConstantKind.Vector2: return ProgramConstant.FromVector2(index, identity, reader.ReadVector2());
                case ProgramConstantKind.Vector3: return ProgramConstant.FromVector3(index, identity, reader.ReadVector3());
                case ProgramConstantKind.Yaw: return ProgramConstant.FromYaw(index, identity, reader.ReadYaw());
                case ProgramConstantKind.String: return ProgramConstant.FromString(index, identity, CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
                case ProgramConstantKind.Bytes: return ProgramConstant.FromBytes(index, identity, reader.ReadBytes());
                default: throw new InvalidDataException($"Unsupported constant kind '{kind}'.");
            }
        }

        static ProgramConstantKind ReadConstantKind(byte value)
        {
            if (value < (byte)ProgramConstantKind.Boolean || value > (byte)ProgramConstantKind.Bytes)
                throw new InvalidDataException($"Enum value '{value}' is invalid for 'ProgramConstantKind'.");
            return (ProgramConstantKind)value;
        }

        static void WriteOperationDefinition(CanonicalWriter writer, SimulationOperationDefinition value)
        {
            writer.WriteInt32(value.Index);
            writer.WriteText(value.Identity);
            writer.WriteInt32((int)value.Code);
            WriteIntArray(writer, value.ConstantReferences);
            writer.WriteInt32(value.Integer0);
            writer.WriteInt32(value.Integer1);
            writer.WriteUInt64(value.Unsigned0);
            writer.WriteScalar(value.Scalar0);
            writer.WriteText(value.Text0);
            writer.WriteUInt32(value.Flags);
        }

        static SimulationOperationDefinition ReadOperationDefinition(CanonicalReader reader)
        {
            return new SimulationOperationDefinition(
                reader.ReadInt32(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                GameplayAbilitySemanticsCodec.ReadOperationCode(reader.ReadInt32()),
                ReadIntArray(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadUInt64(),
                reader.ReadScalar(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                reader.ReadUInt32());
        }

        static void WriteOperation(CanonicalWriter writer, SimulationOperation value)
        {
            writer.WriteInt32(value.Handle.Value);
            writer.WriteInt32(value.DefinitionIndex);
            WriteIntArray(writer, value.Operands);
            WriteIntArray(writer, value.StateSlots);
        }

        static SimulationOperation ReadOperation(CanonicalReader reader, IReadOnlyList<SimulationOperationDefinition> definitions)
        {
            var handle = new OperationHandle(reader.ReadInt32());
            int definitionIndex = reader.ReadInt32();
            if (definitionIndex < 0 || definitionIndex >= definitions.Count)
                throw new InvalidDataException($"Operation '{handle}' definition index '{definitionIndex}' is invalid.");
            return new SimulationOperation(
                handle,
                definitions[definitionIndex],
                ReadIntArray(reader),
                ReadIntArray(reader));
        }

        static void WriteTable<T>(CanonicalWriter writer, IReadOnlyList<T> values, Action<CanonicalWriter, T> write)
        {
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
                write(writer, values[i]);
        }

        static T[] ReadTable<T>(CanonicalReader reader, Func<CanonicalReader, T> read)
        {
            int count = ReadCount(reader);
            var values = new T[count];
            for (int i = 0; i < count; i++)
                values[i] = read(reader);
            return values;
        }
    }
}
