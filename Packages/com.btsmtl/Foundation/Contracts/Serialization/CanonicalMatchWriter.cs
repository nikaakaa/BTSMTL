using System;
using System.IO;

namespace ThirdPersonSimulation
{
    public sealed class CanonicalMatchWriter : ICanonicalValueWriter
    {
        readonly ICanonicalValueReader m_Reader;

        internal CanonicalMatchWriter(ICanonicalValueReader reader) => m_Reader = reader;

        public void WriteByte(byte value) => Require(m_Reader.ReadByte() == value);
        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));
        public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);
        public void WriteInt16(short value) => WriteUInt16(unchecked((ushort)value));
        public void WriteUInt16(ushort value) => Require(m_Reader.ReadUInt16() == value);
        public void WriteChar(char value) => WriteUInt16(value);
        public void WriteInt32(int value) => Require(m_Reader.ReadInt32() == value);
        public void WriteUInt32(uint value) => Require(m_Reader.ReadUInt32() == value);
        public void WriteInt64(long value) => Require(m_Reader.ReadInt64() == value);
        public void WriteUInt64(ulong value) => Require(m_Reader.ReadUInt64() == value);
        public void WriteSingleBits(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));
        public void WriteDoubleBits(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value));
        public void WriteText(in CanonicalTextValue value) => m_Reader.RequireText(value);
        public void WriteHash(StableHash value) => Require(m_Reader.ReadHash() == value);
        public void WriteHash<TValue>(TValue value) where TValue : struct, ICanonicalHashValue =>
            WriteHash(value.CanonicalHashValue);
        public void WriteRawBytes(ReadOnlySpan<byte> value) =>
            Require(m_Reader.ReadRawBytesSegment(value.Length).AsSpan().SequenceEqual(value));

        static void Require(bool matches)
        {
            if (!matches)
                throw new InvalidDataException("Canonical value differs from its published value.");
        }
    }
}
