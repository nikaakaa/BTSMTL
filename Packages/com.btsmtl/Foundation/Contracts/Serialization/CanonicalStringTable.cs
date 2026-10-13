using System;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    public sealed class CanonicalStringTableBuilder : ICanonicalTextVisitor
    {
        readonly SortedSet<string> m_Values = new SortedSet<string>(StringComparer.Ordinal) { string.Empty };

        public IEnumerable<string> Values => m_Values;

        public void Visit(in CanonicalTextValue value)
        {
            if (!value.IsNull)
                m_Values.Add(value.ToString());
        }

        public CanonicalStringTableWriter Write(CanonicalWriter writer)
        {
            var indexes = new Dictionary<string, int>(m_Values.Count, StringComparer.Ordinal);
            writer.WriteInt32(m_Values.Count);
            foreach (string value in m_Values)
            {
                writer.WriteText(value);
                indexes.Add(value, indexes.Count);
            }
            return new CanonicalStringTableWriter(writer, indexes);
        }
    }

    public sealed class CanonicalStringTableWriter : ICanonicalValueWriter
    {
        readonly CanonicalWriter m_Writer;
        readonly IReadOnlyDictionary<string, int> m_Indexes;

        internal CanonicalStringTableWriter(CanonicalWriter writer, IReadOnlyDictionary<string, int> indexes)
        {
            m_Writer = writer;
            m_Indexes = indexes;
        }

        public void WriteText(in CanonicalTextValue value) =>
            m_Writer.WriteInt32(value.IsNull ? -1 : m_Indexes[value.ToString()]);

        public void WriteByte(byte value) => m_Writer.WriteByte(value);
        public void WriteSByte(sbyte value) => m_Writer.WriteSByte(value);
        public void WriteBoolean(bool value) => m_Writer.WriteBoolean(value);
        public void WriteInt16(short value) => m_Writer.WriteInt16(value);
        public void WriteUInt16(ushort value) => m_Writer.WriteUInt16(value);
        public void WriteChar(char value) => m_Writer.WriteChar(value);
        public void WriteInt32(int value) => m_Writer.WriteInt32(value);
        public void WriteUInt32(uint value) => m_Writer.WriteUInt32(value);
        public void WriteInt64(long value) => m_Writer.WriteInt64(value);
        public void WriteUInt64(ulong value) => m_Writer.WriteUInt64(value);
        public void WriteSingleBits(float value) => m_Writer.WriteSingleBits(value);
        public void WriteDoubleBits(double value) => m_Writer.WriteDoubleBits(value);
        public void WriteHash(StableHash value) => m_Writer.WriteHash(value);
        public void WriteHash<TValue>(TValue value) where TValue : struct, ICanonicalHashValue => m_Writer.WriteHash(value);
        public void WriteRawBytes(ReadOnlySpan<byte> value) => m_Writer.WriteRawBytes(value);
    }

    public sealed class CanonicalStringTableReader : ICanonicalValueReader
    {
        readonly CanonicalReader m_Reader;
        readonly string[] m_Strings;

        CanonicalStringTableReader(CanonicalReader reader, string[] strings)
        {
            m_Reader = reader;
            m_Strings = strings;
            MatchWriter = new CanonicalMatchWriter(this);
        }

        public static CanonicalStringTableReader Read(CanonicalReader reader)
        {
            int count = reader.ReadInt32();
            if (count <= 0 || count > reader.Remaining / 4)
                throw new InvalidDataException("Canonical string table length is invalid.");
            var strings = new string[count];
            for (int i = 0; i < count; i++)
            {
                strings[i] = reader.ReadString();
                if (i == 0 && strings[i] != string.Empty ||
                    i > 0 && string.CompareOrdinal(strings[i - 1], strings[i]) >= 0)
                    throw new InvalidDataException("Canonical string table is not sorted and unique.");
            }
            return new CanonicalStringTableReader(reader, strings);
        }

        public string ReadString()
        {
            int index = m_Reader.ReadInt32();
            if (index == -1)
                return null;
            if ((uint)index >= (uint)m_Strings.Length)
                throw new InvalidDataException("Canonical string table index is out of range.");
            return m_Strings[index];
        }

        public CanonicalMatchWriter MatchWriter { get; }
        public CanonicalPublishedValues PublishedValues => m_Reader.PublishedValues;
        public void RequireText(in CanonicalTextValue expected)
        {
            string actual = ReadString();
            if (!CanonicalTextValue.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidDataException("Canonical string table text differs from its published value.");
        }
        public StableHash ReadHash() => m_Reader.ReadHash();
        public ArraySegment<byte> ReadRawBytesSegment(int length) => m_Reader.ReadRawBytesSegment(length);

        public CanonicalTextValue ReadText() => CanonicalTextValue.FromString(ReadString());
        public string ReadDeclaredString() => ReadString();
        public byte[] ReadBytes() => m_Reader.ReadBytes();
        public void SkipBytes() => m_Reader.SkipBytes();
        public void SkipText() => ReadString();
        public int Remaining => m_Reader.Remaining;
        public byte ReadByte() => m_Reader.ReadByte();
        public sbyte ReadSByte() => m_Reader.ReadSByte();
        public bool ReadBoolean() => m_Reader.ReadBoolean();
        public short ReadInt16() => m_Reader.ReadInt16();
        public ushort ReadUInt16() => m_Reader.ReadUInt16();
        public char ReadChar() => m_Reader.ReadChar();
        public int ReadInt32() => m_Reader.ReadInt32();
        public uint ReadUInt32() => m_Reader.ReadUInt32();
        public long ReadInt64() => m_Reader.ReadInt64();
        public ulong ReadUInt64() => m_Reader.ReadUInt64();
        public float ReadSingleBits() => m_Reader.ReadSingleBits();
        public double ReadDoubleBits() => m_Reader.ReadDoubleBits();
    }
}
