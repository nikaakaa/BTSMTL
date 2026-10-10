using System;
using System.Collections.Generic;
using System.IO;
using static ThirdPersonSimulation.GameplayAbilitySemanticsCodec;

namespace ThirdPersonSimulation
{
    public static class Float32GraphExecutionDataCodec
    {
        const int Version = 1;

        public static byte[] Write(Float32GraphExecutionData data)
        {
            using var writer = new CanonicalWriter();
            writer.WriteInt32(Version);
            writer.WriteInt32(data.RootOperation.Value);
            WriteTables(writer, data);
            return writer.ToArray();
        }

        public static Float32GraphExecutionData Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes);
            if (reader.ReadInt32() != Version)
                throw new InvalidDataException("图执行数据版本不匹配，请重新编译。");
            var entry = new OperationHandle(reader.ReadInt32());
            return ReadTables(reader, references => entry);
        }

        internal static void WriteTables(CanonicalWriter writer, Float32GraphExecutionData data)
        {
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
        }

        internal static Float32GraphExecutionData ReadTables(CanonicalReader reader,
            Func<IReadOnlyList<ProgramReference>, OperationHandle> resolveEntry)
        {
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
            return new Float32GraphExecutionData(resolveEntry(references), operationDefinitions, operations,
                constants, constantInputBindings, controlFlow, references, graphCallFrames, stateSlots, scopes,
                outputChannels, catalogEntries, sourceMap, producers);
        }

        internal static void WriteConstantInputBinding(CanonicalWriter writer, ProgramConstantInputBinding binding)
        {
            writer.WriteInt32(binding.TargetOperation.Value);
            writer.WriteText(binding.TargetPort);
            writer.WriteInt32(binding.ConstantIndex);
            writer.WriteByte((byte)binding.ResolvedValueKind);
        }

        internal static ProgramConstantInputBinding ReadConstantInputBinding(CanonicalReader reader)
        {
            var operation = new OperationHandle(reader.ReadInt32());
            string port = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            int constant = reader.ReadInt32();
            byte kindValue = reader.ReadByte();
            var kind = (SemanticValueKind)kindValue;
            if (kind < SemanticValueKind.Boolean || kind > SemanticValueKind.Enum)
                throw new InvalidDataException($"Program constant input contains unknown value kind '{kindValue}'.");
            return new ProgramConstantInputBinding(operation, port, constant, kind);
        }

        internal static void WriteConstant(CanonicalWriter writer, ProgramConstant value)
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
                case ProgramConstantKind.Quaternion:
                    writer.WriteVector3(value.Vector3); writer.WriteScalar(value.Scalar); break;
                case ProgramConstantKind.Enum:
                    writer.WriteText(value.Text); writer.WriteInt32(value.Int32); break;
                default: throw new InvalidDataException($"Unsupported constant kind '{value.Kind}'.");
            }
        }

        internal static ProgramConstant ReadConstant(CanonicalReader reader)
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
                case ProgramConstantKind.Quaternion:
                    Float32Vector3 vector = reader.ReadVector3();
                    return ProgramConstant.FromQuaternion(index, identity,
                        new Float32Quaternion(vector.X, vector.Y, vector.Z, reader.ReadScalar()));
                case ProgramConstantKind.Enum:
                    return ProgramConstant.FromEnum(index, identity,
                        CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), reader.ReadInt32());
                default: throw new InvalidDataException($"Unsupported constant kind '{kind}'.");
            }
        }

        internal static ProgramConstantKind ReadConstantKind(byte value)
        {
            if (value < (byte)ProgramConstantKind.Boolean || value > (byte)ProgramConstantKind.Enum)
                throw new InvalidDataException($"Enum value '{value}' is invalid for 'ProgramConstantKind'.");
            return (ProgramConstantKind)value;
        }

        internal static void WriteOperationDefinition(CanonicalWriter writer, SimulationOperationDefinition value)
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

        internal static SimulationOperationDefinition ReadOperationDefinition(CanonicalReader reader)
        {
            return new SimulationOperationDefinition(
                reader.ReadInt32(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                GameplayAbilitySemanticsCodec.ReadEnum<SimulationOperationCode>(reader.ReadInt32()),
                ReadIntArray(reader),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadUInt64(),
                reader.ReadScalar(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                reader.ReadUInt32());
        }

        internal static void WriteOperation(CanonicalWriter writer, SimulationOperation value)
        {
            writer.WriteInt32(value.Handle.Value);
            writer.WriteInt32(value.DefinitionIndex);
            WriteIntArray(writer, value.Operands);
            WriteIntArray(writer, value.StateSlots);
        }

        internal static SimulationOperation ReadOperation(CanonicalReader reader, IReadOnlyList<SimulationOperationDefinition> definitions)
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

        internal static void WriteTable<T>(CanonicalWriter writer, IReadOnlyList<T> values, Action<CanonicalWriter, T> write)
        {
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
                write(writer, values[i]);
        }

        internal static T[] ReadTable<T>(CanonicalReader reader, Func<CanonicalReader, T> read)
        {
            int count = ReadCount(reader);
            var values = new T[count];
            for (int i = 0; i < count; i++)
                values[i] = read(reader);
            return values;
        }
    }
}
