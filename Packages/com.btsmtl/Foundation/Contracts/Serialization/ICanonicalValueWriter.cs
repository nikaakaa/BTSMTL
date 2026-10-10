using System;

namespace ThirdPersonSimulation
{
    public interface ICanonicalValueWriter
    {
        void WriteByte(byte value);
        void WriteSByte(sbyte value);
        void WriteBoolean(bool value);
        void WriteInt16(short value);
        void WriteUInt16(ushort value);
        void WriteChar(char value);
        void WriteInt32(int value);
        void WriteUInt32(uint value);
        void WriteInt64(long value);
        void WriteUInt64(ulong value);
        void WriteSingleBits(float value);
        void WriteDoubleBits(double value);
        void WriteText(in CanonicalTextValue value);
        void WriteHash(StableHash value);
        void WriteRawBytes(ReadOnlySpan<byte> value);
    }
}
