using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    internal static class GameplayAbilitySemanticsCodec
    {
        const int MaximumTableCount = 1000000;
        internal const int SourceMapStringTableVersion = 2;

        internal static void WriteControlFlow(CanonicalWriter writer, ProgramControlFlowEdge value)
        {
            writer.WriteText(value.Identity);
            writer.WriteInt32(value.Source.Value);
            writer.WriteInt32(value.Target.Value);
            writer.WriteText(value.SourcePort);
            writer.WriteText(value.TargetPort);
            writer.WriteByte((byte)value.Kind);
            writer.WriteInt32(value.Order);
            writer.WriteInt32(value.Priority);
            writer.WriteByte((byte)value.AbortPolicy);
            writer.WriteBoolean(value.HasCondition);
            if (value.HasCondition)
                writer.WriteInt32(value.Condition.Value);
        }

        internal static ProgramControlFlowEdge ReadControlFlow(CanonicalReader reader)
        {
            string identity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            var source = new OperationHandle(reader.ReadInt32());
            var target = new OperationHandle(reader.ReadInt32());
            string sourcePort = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            string targetPort = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            ProgramControlFlowKind kind = ReadEnum<ProgramControlFlowKind>(reader.ReadByte());
            int order = reader.ReadInt32();
            int priority = reader.ReadInt32();
            ProgramAbortPolicy abortPolicy = ReadEnum<ProgramAbortPolicy>(reader.ReadByte());
            bool hasCondition = reader.ReadBoolean();
            OperationHandle condition = hasCondition ? new OperationHandle(reader.ReadInt32()) : default;
            return new ProgramControlFlowEdge(identity, source, target, sourcePort, targetPort, kind, order, priority, abortPolicy, hasCondition, condition);
        }

        internal static void WriteReference(CanonicalWriter writer, ProgramReference value)
        {
            writer.WriteText(value.Identity);
            writer.WriteBoolean(value.HasSourceOperation);
            if (value.HasSourceOperation)
                writer.WriteInt32(value.SourceOperation.Value);
            writer.WriteByte((byte)value.Kind);
            writer.WriteInt32(value.TargetIndex);
            writer.WriteText(value.ExternalIdentity);
        }

        internal static ProgramReference ReadReference(CanonicalReader reader)
        {
            string identity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            OperationHandle sourceOperation = reader.ReadBoolean() ? new OperationHandle(reader.ReadInt32()) : OperationHandle.Invalid;
            return new ProgramReference(identity, sourceOperation, ReadEnum<ProgramReferenceKind>(reader.ReadByte()), reader.ReadInt32(), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
        }

        internal static void WriteGraphCallFrame(CanonicalWriter writer, ProgramGraphCallFrame value)
        {
            writer.WriteInt32(value.Index);
            writer.WriteText(value.Identity);
            writer.WriteInt32(value.OwnerOperation.Value);
            writer.WriteInt32(value.EntryOperation.Value);
            writer.WriteText(value.ChildGraphIdentity);
            WriteGraphParameterBindings(writer, value.Inputs);
            WriteGraphParameterBindings(writer, value.Outputs);
        }

        internal static ProgramGraphCallFrame ReadGraphCallFrame(CanonicalReader reader)
        {
            return new ProgramGraphCallFrame(
                reader.ReadInt32(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                new OperationHandle(reader.ReadInt32()),
                new OperationHandle(reader.ReadInt32()),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                ReadGraphParameterBindings(reader),
                ReadGraphParameterBindings(reader));
        }

        static void WriteGraphParameterBindings(
            CanonicalWriter writer,
            IReadOnlyList<ProgramGraphParameterBinding> values)
        {
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                ProgramGraphParameterBinding value = values[i];
                writer.WriteByte((byte)value.Direction);
                writer.WriteText(value.ParameterName);
                writer.WriteText(value.DeclarationIdentity);
                writer.WriteInt32(value.StateSlot);
                writer.WriteText(value.PortId);
                writer.WriteByte((byte)value.ValueKind);
            }
        }

        static ProgramGraphParameterBinding[] ReadGraphParameterBindings(CanonicalReader reader)
        {
            int count = ReadCount(reader);
            var values = new ProgramGraphParameterBinding[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = new ProgramGraphParameterBinding(
                    ReadEnum<ProgramGraphParameterDirection>(reader.ReadByte()),
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    reader.ReadInt32(),
                    CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                    ReadEnum<SemanticValueKind>(reader.ReadByte()));
            }
            return values;
        }

        internal static void WriteStateSlot(CanonicalWriter writer, ProgramStateSlot value, bool includeDefault)
        {
            writer.WriteInt32(value.Index);
            writer.WriteText(value.Identity);
            writer.WriteByte((byte)value.ValueKind);
            writer.WriteByte((byte)value.OwnerKind);
            writer.WriteInt32((int)value.Semantic);
            writer.WriteText(value.OwnerIdentity);
            writer.WriteText(value.StateCodecIdentity);
            if (includeDefault)
                writer.WriteInt32(value.DefaultConstantIndex);
        }

        internal static ProgramStateSlot ReadStateSlot(CanonicalReader reader)
        {
            var slot = new ProgramStateSlot(
                reader.ReadInt32(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                ReadEnum<ProgramStateValueKind>(reader.ReadByte()),
                ReadEnum<ProgramStateOwnerKind>(reader.ReadByte()),
                ReadEnum<ProgramStateSemantic>(reader.ReadInt32()),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                reader.ReadInt32());
            return slot;
        }

        internal static void WriteScope(CanonicalWriter writer, ProgramScopeLayout value)
        {
            writer.WriteInt32(value.CompiledOwnerIndex);
            writer.WriteText(value.Identity);
            writer.WriteByte((byte)value.Kind);
            writer.WriteText(value.OwnerIdentity);
            writer.WriteInt32(value.OwnerOperation.IsValid ? value.OwnerOperation.Value : -1);
            WriteIntArray(writer, value.StateSlots);
        }

        internal static ProgramScopeLayout ReadScope(CanonicalReader reader)
        {
            int compiledOwnerIndex = reader.ReadInt32();
            string identity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            ProgramScopeKind kind = ReadEnum<ProgramScopeKind>(reader.ReadByte());
            string ownerIdentity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            int operation = reader.ReadInt32();
            return new ProgramScopeLayout(
                compiledOwnerIndex,
                identity,
                kind,
                ownerIdentity,
                operation >= 0 ? new OperationHandle(operation) : OperationHandle.Invalid,
                ReadIntArray(reader));
        }

        internal static void WriteOutputChannel(CanonicalWriter writer, ProgramOutputChannelLayout value)
        {
            writer.WriteInt32(value.Index);
            writer.WriteText(value.Identity);
            writer.WriteByte((byte)value.Kind);
        }

        internal static ProgramOutputChannelLayout ReadOutputChannel(CanonicalReader reader)
        {
            return new ProgramOutputChannelLayout(reader.ReadInt32(), CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()), ReadEnum<ProgramOutputChannelKind>(reader.ReadByte()));
        }

        internal static void WriteCatalogEntry(CanonicalWriter writer, ProgramCatalogEntry value)
        {
            writer.WriteInt32(value.Index);
            writer.WriteByte((byte)value.Kind);
            writer.WriteText(value.Identity);
            writer.WriteInt32(value.Revision);
            writer.WriteInt32(value.Fields.Count);
            for (int i = 0; i < value.Fields.Count; i++)
            {
                ProgramCatalogField field = value.Fields[i];
                writer.WriteText(field.Name);
                writer.WriteByte((byte)field.Kind);
                if (field.Kind == ProgramCatalogFieldKind.Constant)
                    writer.WriteInt32(field.ConstantIndex);
                else
                    writer.WriteText(field.Identity);
            }
        }

        internal static ProgramCatalogEntry ReadCatalogEntry(CanonicalReader reader)
        {
            int index = reader.ReadInt32();
            ProgramCatalogEntryKind kind = ReadEnum<ProgramCatalogEntryKind>(reader.ReadByte());
            string identity = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
            int revision = reader.ReadInt32();
            int fieldCount = ReadCount(reader);
            var fields = new ProgramCatalogField[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                string name = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                ProgramCatalogFieldKind fieldKind = ReadEnum<ProgramCatalogFieldKind>(reader.ReadByte());
                fields[i] = fieldKind == ProgramCatalogFieldKind.Constant
                    ? new ProgramCatalogField(name, fieldKind, reader.ReadInt32(), null)
                    : new ProgramCatalogField(name, fieldKind, -1, CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()));
            }
            return new ProgramCatalogEntry(index, kind, identity, revision, fields);
        }

        internal static void WriteSourceMapTable(CanonicalWriter writer, IReadOnlyList<ProgramSourceMapEntry> values)
        {
            writer.WriteInt32(SourceMapStringTableVersion);
            var strings = new SortedSet<string>(StringComparer.Ordinal) { string.Empty };
            for (int i = 0; i < values.Count; i++)
            {
                ProgramSourceMapEntry value = values[i];
                strings.Add(value.SourceType);
                strings.Add(value.GraphId);
                strings.Add(value.NodeId);
                strings.Add(value.PortId);
                strings.Add(value.EdgeId);
                strings.Add(value.DeclarationId);
                strings.Add(value.TimelineId);
                strings.Add(value.TrackId);
                strings.Add(value.ClipId);
                strings.Add(value.ContentHash);
                strings.Add(value.GraphInvocationPath);
                strings.Add(value.CompiledPortId);
                strings.Add(value.SourceInvocationPath);
                strings.Add(value.ParentInvocationPath);
                strings.Add(value.InvocationCallerId);
                strings.Add(value.InvocationCallerClipId);
                string[] segments = SplitDisplayPath(value.DisplayPath);
                for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                    strings.Add(segments[segmentIndex]);
            }

            var stringIndex = new Dictionary<string, int>(strings.Count, StringComparer.Ordinal);
            writer.WriteInt32(strings.Count);
            int index = 0;
            foreach (string value in strings)
            {
                writer.WriteText(value);
                stringIndex.Add(value, index++);
            }

            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                ProgramSourceMapEntry value = values[i];
                writer.WriteByte((byte)value.TargetKind);
                writer.WriteInt32(value.TargetIndex);
                writer.WriteInt32(stringIndex[value.SourceType]);
                writer.WriteInt32(stringIndex[value.GraphId]);
                writer.WriteInt32(stringIndex[value.NodeId]);
                writer.WriteInt32(stringIndex[value.PortId]);
                writer.WriteInt32(stringIndex[value.EdgeId]);
                writer.WriteInt32(stringIndex[value.DeclarationId]);
                writer.WriteInt32(stringIndex[value.TimelineId]);
                writer.WriteInt32(stringIndex[value.TrackId]);
                writer.WriteInt32(stringIndex[value.ClipId]);
                writer.WriteInt32(stringIndex[value.ContentHash]);
                writer.WriteInt32(stringIndex[value.GraphInvocationPath]);
                writer.WriteInt32(stringIndex[value.CompiledPortId]);
                writer.WriteByte((byte)value.ValuePortDirection);
                writer.WriteInt32(stringIndex[value.SourceInvocationPath]);
                writer.WriteInt32(stringIndex[value.ParentInvocationPath]);
                writer.WriteByte((byte)value.InvocationCallerKind);
                writer.WriteInt32(stringIndex[value.InvocationCallerId]);
                writer.WriteInt32(stringIndex[value.InvocationCallerClipId]);
                string[] segments = SplitDisplayPath(value.DisplayPath);
                writer.WriteInt32(segments.Length);
                for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
                    writer.WriteInt32(stringIndex[segments[segmentIndex]]);
            }
        }

        internal static ProgramSourceMapEntry[] ReadSourceMapTable(CanonicalReader reader)
        {
            if (reader.ReadInt32() != SourceMapStringTableVersion)
                throw new InvalidDataException("Program source-map string-table version is unsupported.");
            int stringCount = ReadCount(reader);
            if (stringCount == 0)
                throw new InvalidDataException("Program source-map string table is empty.");
            var strings = new string[stringCount];
            for (int i = 0; i < stringCount; i++)
            {
                strings[i] = CanonicalTextEncoding.DecodeString(reader.ReadTextSegment());
                if (i == 0 && strings[i].Length != 0)
                    throw new InvalidDataException("Program source-map string table must begin with the empty string.");
                if (i > 0 && string.CompareOrdinal(strings[i - 1], strings[i]) >= 0)
                    throw new InvalidDataException("Program source-map string table is not canonical.");
            }

            int entryCount = ReadCount(reader);
            var entries = new ProgramSourceMapEntry[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                ProgramSourceTargetKind targetKind = ReadSourceTargetKind(reader.ReadByte());
                int targetIndex = reader.ReadInt32();
                string sourceType = ReadSourceMapString(reader, strings);
                string graphId = ReadSourceMapString(reader, strings);
                string nodeId = ReadSourceMapString(reader, strings);
                string portId = ReadSourceMapString(reader, strings);
                string edgeId = ReadSourceMapString(reader, strings);
                string declarationId = ReadSourceMapString(reader, strings);
                string timelineId = ReadSourceMapString(reader, strings);
                string trackId = ReadSourceMapString(reader, strings);
                string clipId = ReadSourceMapString(reader, strings);
                string contentHash = ReadSourceMapString(reader, strings);
                string graphInvocationPath = ReadSourceMapString(reader, strings);
                string compiledPortId = ReadSourceMapString(reader, strings);
                ProgramValuePortDirection valuePortDirection = ReadValuePortDirection(reader.ReadByte());
                string sourceInvocationPath = ReadSourceMapString(reader, strings);
                string parentInvocationPath = ReadSourceMapString(reader, strings);
                ProgramInvocationCallerKind callerKind = ReadInvocationCallerKind(reader.ReadByte());
                string callerId = ReadSourceMapString(reader, strings);
                string callerClipId = ReadSourceMapString(reader, strings);
                int segmentCount = ReadCount(reader);
                var pathSegments = new string[segmentCount];
                for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
                    pathSegments[segmentIndex] = ReadSourceMapString(reader, strings);
                entries[i] = new ProgramSourceMapEntry(
                    targetKind,
                    targetIndex,
                    sourceType,
                    graphId,
                    nodeId,
                    portId,
                    edgeId,
                    declarationId,
                    timelineId,
                    trackId,
                    clipId,
                    string.Join("/", pathSegments),
                    contentHash,
                    graphInvocationPath,
                    compiledPortId,
                    valuePortDirection,
                    sourceInvocationPath,
                    parentInvocationPath,
                    callerKind,
                    callerId,
                    callerClipId);
            }
            return entries;
        }

        static string ReadSourceMapString(CanonicalReader reader, IReadOnlyList<string> strings)
        {
            int index = reader.ReadInt32();
            if (index < 0 || index >= strings.Count)
                throw new InvalidDataException($"Program source-map string index '{index}' is out of range.");
            return strings[index];
        }

        static string[] SplitDisplayPath(string value)
        {
            return value.Split('/');
        }

        static ProgramSourceTargetKind ReadSourceTargetKind(byte value)
        {
            return value switch
            {
                1 => ProgramSourceTargetKind.Operation,
                2 => ProgramSourceTargetKind.Constant,
                3 => ProgramSourceTargetKind.StateSlot,
                4 => ProgramSourceTargetKind.Reference,
                5 => ProgramSourceTargetKind.Producer,
                6 => ProgramSourceTargetKind.CatalogEntry,
                7 => ProgramSourceTargetKind.BodyMotion,
                8 => ProgramSourceTargetKind.ControlModule,
                10 => ProgramSourceTargetKind.ControlTransition,
                11 => ProgramSourceTargetKind.OperationPort,
                12 => ProgramSourceTargetKind.GraphInvocation,
                13 => ProgramSourceTargetKind.OptimizedAway,
                _ => throw new InvalidDataException($"Enum value '{value}' is invalid for 'ProgramSourceTargetKind'.")
            };
        }

        static ProgramValuePortDirection ReadValuePortDirection(byte value)
        {
            if (value > (byte)ProgramValuePortDirection.Output)
                throw new InvalidDataException($"Enum value '{value}' is invalid for 'ProgramValuePortDirection'.");
            return (ProgramValuePortDirection)value;
        }

        static ProgramInvocationCallerKind ReadInvocationCallerKind(byte value)
        {
            if (value > (byte)ProgramInvocationCallerKind.AbilityRule)
                throw new InvalidDataException($"Enum value '{value}' is invalid for 'ProgramInvocationCallerKind'.");
            return (ProgramInvocationCallerKind)value;
        }

        internal static void WriteProducer(CanonicalWriter writer, ProgramProducer value)
        {
            writer.WriteInt32(value.Index);
            writer.WriteText(value.Identity);
            writer.WriteText(value.AnimationChannelId.Value);
            writer.WriteText(value.SourceIdentity);
            writer.WriteByte((byte)value.ChannelKind);
        }

        internal static ProgramProducer ReadProducer(CanonicalReader reader)
        {
            return new ProgramProducer(
                reader.ReadInt32(),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                new AnimationChannelId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                CanonicalTextEncoding.DecodeString(reader.ReadTextSegment()),
                ReadEnum<ProgramOutputChannelKind>(reader.ReadByte()));
        }

        internal static void WriteIntArray(CanonicalWriter writer, IReadOnlyList<int> values)
        {
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
                writer.WriteInt32(values[i]);
        }

        internal static int[] ReadIntArray(CanonicalReader reader)
        {
            int count = ReadCount(reader);
            var values = new int[count];
            for (int i = 0; i < count; i++)
                values[i] = reader.ReadInt32();
            return values;
        }

        internal static int ReadCount(CanonicalReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumTableCount)
                throw new InvalidDataException($"Canonical table count '{count}' is invalid.");
            return count;
        }

        internal static SimulationOperationCode ReadOperationCode(int value)
        {
            if (value < 0 || value > ushort.MaxValue || !GameplayAbilityOperationSet.IsOperation((SimulationOperationCode)value))
                throw new InvalidDataException($"Enum value '{value}' is invalid for 'SimulationOperationCode'.");
            return (SimulationOperationCode)value;
        }

        internal static T ReadEnum<T>(int value) where T : struct
        {
            object candidate = Enum.ToObject(typeof(T), value);
            if (!Enum.IsDefined(typeof(T), candidate))
                throw new InvalidDataException($"Enum value '{value}' is invalid for {typeof(T).Name}.");
            return (T)candidate;
        }
    }
}
