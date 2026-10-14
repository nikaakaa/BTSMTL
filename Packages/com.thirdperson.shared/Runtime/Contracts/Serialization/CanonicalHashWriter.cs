using System;
using System.Buffers.Binary;

namespace ThirdPersonSimulation
{
    public sealed class CanonicalHashWriter : ICanonicalValueWriter
    {
        readonly SimulationCanonicalPayloadHash.Accumulator m_Accumulator = new SimulationCanonicalPayloadHash.Accumulator();

        public CanonicalHashWriter() => m_Accumulator.Begin();

        public void Reset() => m_Accumulator.Begin();
        public StableHash ComputeHash() => m_Accumulator.Complete();
        public void WriteByte(byte value) => m_Accumulator.AppendAscii(value);
        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));
        public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);
        public void WriteInt16(short value) => WriteUInt16(unchecked((ushort)value));
        public void WriteChar(char value) => WriteUInt16(value);
        public void WriteSingleBits(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));
        public void WriteDoubleBits(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value));

        public void WriteUInt16(ushort value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            m_Accumulator.Append(bytes);
        }

        public void WriteInt32(int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            m_Accumulator.Append(bytes);
        }

        public void WriteUInt32(uint value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
            m_Accumulator.Append(bytes);
        }

        public void WriteInt64(long value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
            m_Accumulator.Append(bytes);
        }

        public void WriteUInt64(ulong value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(ulong)];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
            m_Accumulator.Append(bytes);
        }

        public void WriteText(in CanonicalTextValue value)
        {
            if (value.IsNull)
            {
                WriteInt32(-1);
                return;
            }
            WriteInt32(checked(value.Length * sizeof(char)));
            using CanonicalTextReadLease read = value.AcquireRead();
            m_Accumulator.AppendText(value);
        }

        public void WriteHash(StableHash value)
        {
            WriteInt32(value.IsValid ? 64 : 0);
            if (value.IsValid)
                m_Accumulator.AppendHash(value);
        }

        public void WriteHash<TValue>(TValue value) where TValue : struct, ICanonicalHashValue =>
            WriteHash(value.CanonicalHashValue);

        public void WriteRawBytes(ReadOnlySpan<byte> value) => m_Accumulator.Append(value);
    }
}
