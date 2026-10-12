using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    internal static class GameplayAbilitySemanticsCodec
    {
        const int MaximumTableCount = 1000000;
        internal const int SourceMapStringTableVersion = 5;
        internal const int ExecutionSourceStringTableVersion = 2;

        internal static void WriteGraphSourceRevisions(CanonicalWriter writer, ProgramGraphSourceRevisionTable table) =>
            ProgramGraphSourceRevisions.CanonicalWrite(writer, table.Data);

        internal static IReadOnlyList<ProgramGraphSourceRevision> ReadGraphSourceRevisions(CanonicalReader reader) =>
            ProgramGraphSourceRevisions.CanonicalReadMaterialized(reader).Sources;

        internal static void WriteExecutionSourceTable(CanonicalWriter writer, IReadOnlyList<ProgramExecutionSource> values)
        {
            writer.WriteInt32(ExecutionSourceStringTableVersion);
            writer.WriteHash(ProgramExecutionSource.CanonicalSchemaHash);
            var strings = new CanonicalStringTableBuilder();
            for (int i = 0; i < values.Count; i++)
                ProgramExecutionSource.CanonicalVisitTexts(strings, values[i]);
            CanonicalStringTableWriter rows = strings.Write(writer);
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
                ProgramExecutionSource.CanonicalWrite(rows, values[i]);
        }

        internal static ProgramExecutionSource[] ReadExecutionSourceTable(CanonicalReader reader)
        {
            if (reader.ReadInt32() != ExecutionSourceStringTableVersion)
                throw new InvalidDataException("Program execution source-table version is unsupported.");
            ProgramExecutionSource.CanonicalRequireSchema(reader);
            CanonicalStringTableReader rows = CanonicalStringTableReader.Read(reader);
            var entries = new ProgramExecutionSource[ReadCount(reader)];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = ProgramExecutionSource.CanonicalReadMaterialized(rows);
            return entries;
        }

        internal static void WriteSourceMapTable(CanonicalWriter writer, IReadOnlyList<ProgramSourceMapEntry> values)
        {
            writer.WriteInt32(SourceMapStringTableVersion);
            writer.WriteHash(ProgramSourceMapText.CanonicalSchemaHash);
            var strings = new CanonicalStringTableBuilder();
            for (int i = 0; i < values.Count; i++)
                ProgramSourceMapText.CanonicalVisitTexts(strings, values[i].Text);
            CanonicalStringTableWriter rows = strings.Write(writer);
            writer.WriteInt32(values.Count);
            for (int i = 0; i < values.Count; i++)
                ProgramSourceMapText.CanonicalWrite(rows, values[i].Text);
        }

        internal static ProgramSourceMapEntry[] ReadSourceMapTable(CanonicalReader reader,
            IReadOnlyList<ProgramExecutionSource> sources)
        {
            if (reader.ReadInt32() != SourceMapStringTableVersion)
                throw new InvalidDataException("Program diagnostic source-table version is unsupported.");
            ProgramSourceMapText.CanonicalRequireSchema(reader);
            CanonicalStringTableReader rows = CanonicalStringTableReader.Read(reader);
            int count = ReadCount(reader);
            if (count != sources.Count)
                throw new InvalidDataException("Program diagnostic sources do not match the execution source layout.");
            var entries = new ProgramSourceMapEntry[count];
            for (int i = 0; i < count; i++)
                entries[i] = new ProgramSourceMapEntry(sources[i], ProgramSourceMapText.CanonicalReadMaterialized(rows));
            return entries;
        }

        internal static int ReadCount(CanonicalReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaximumTableCount)
                throw new InvalidDataException($"Canonical table count '{count}' is invalid.");
            return count;
        }

    }
}
