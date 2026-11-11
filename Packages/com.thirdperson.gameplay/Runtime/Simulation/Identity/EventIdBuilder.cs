using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ThirdPersonSimulation
{
    public ref struct EventIdBuilder
    {
        public const int BlockSize = 64;

        readonly Span<byte> m_Block;
        uint m_A, m_B, m_C, m_D, m_E, m_F, m_G, m_H;
        ulong m_Length;
        int m_Buffered;
        bool m_HasField;

        public EventIdBuilder(Span<byte> block)
        {
            this = default;
            m_Block = block.Slice(0, BlockSize);
            ResetHash();
        }

        public void Append(ReadOnlySpan<char> value)
        {
            BeginField();
            if (BitConverter.IsLittleEndian)
            {
                AppendBytes(MemoryMarshal.AsBytes(value));
                return;
            }
            for (int index = 0; index < value.Length; index++)
            {
                AppendByte(unchecked((byte)value[index]));
                AppendByte((byte)(value[index] >> 8));
            }
        }

        public void Append(ulong value)
        {
            BeginField();
            Span<char> characters = stackalloc char[20];
            value.TryFormat(characters, out int count, provider: CultureInfo.InvariantCulture);
            for (int index = 0; index < count; index++)
                AppendByte((byte)characters[index]);
        }

        void BeginField()
        {
            if (m_HasField)
                AppendByte(0x1f);
            m_HasField = true;
        }

        public EventId Build()
        {
            m_Block[m_Buffered++] = 0x80;
            if (m_Buffered > 56)
            {
                m_Block.Slice(m_Buffered).Clear();
                ProcessBlock();
                m_Buffered = 0;
            }
            m_Block.Slice(m_Buffered, 56 - m_Buffered).Clear();
            BinaryPrimitives.WriteUInt64BigEndian(m_Block.Slice(56), unchecked(m_Length * 8));
            ProcessBlock();
            Span<byte> digest = stackalloc byte[32];
            BinaryPrimitives.WriteUInt32BigEndian(digest, m_A);
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(4), m_B);
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(8), m_C);
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(12), m_D);
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(16), m_E);
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(20), m_F);
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(24), m_G);
            BinaryPrimitives.WriteUInt32BigEndian(digest.Slice(28), m_H);
            ResetHash();
            return EventId.FromBytes(digest);
        }

        void AppendBytes(ReadOnlySpan<byte> value)
        {
            m_Length = unchecked(m_Length + (ulong)value.Length);
            while (!value.IsEmpty)
            {
                int count = Math.Min(BlockSize - m_Buffered, value.Length);
                value.Slice(0, count).CopyTo(m_Block.Slice(m_Buffered));
                m_Buffered += count;
                value = value.Slice(count);
                if (m_Buffered == BlockSize)
                {
                    ProcessBlock();
                    m_Buffered = 0;
                }
            }
        }

        void AppendByte(byte value)
        {
            m_Length = unchecked(m_Length + 1);
            m_Block[m_Buffered++] = value;
            if (m_Buffered == BlockSize)
            {
                ProcessBlock();
                m_Buffered = 0;
            }
        }

        void ResetHash()
        {
            m_A = 0x6a09e667;
            m_B = 0xbb67ae85;
            m_C = 0x3c6ef372;
            m_D = 0xa54ff53a;
            m_E = 0x510e527f;
            m_F = 0x9b05688c;
            m_G = 0x1f83d9ab;
            m_H = 0x5be0cd19;
            m_Length = 0;
            m_Buffered = 0;
        }

        void ProcessBlock()
        {
            ReadOnlySpan<uint> constants = stackalloc uint[]
            {
                0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
                0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
                0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
                0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
                0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
                0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
                0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
                0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
                0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
                0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
                0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
                0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
                0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
                0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
                0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
                0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
            };
            Span<uint> schedule = stackalloc uint[64];
            for (int index = 0; index < 16; index++)
                schedule[index] = BinaryPrimitives.ReadUInt32BigEndian(m_Block.Slice(index * 4));
            for (int index = 16; index < schedule.Length; index++)
            {
                uint first = schedule[index - 15];
                uint second = schedule[index - 2];
                uint sigma0 = RotateRight(first, 7) ^ RotateRight(first, 18) ^ (first >> 3);
                uint sigma1 = RotateRight(second, 17) ^ RotateRight(second, 19) ^ (second >> 10);
                schedule[index] = unchecked(schedule[index - 16] + sigma0 + schedule[index - 7] + sigma1);
            }
            uint a = m_A, b = m_B, c = m_C, d = m_D;
            uint e = m_E, f = m_F, g = m_G, h = m_H;
            for (int index = 0; index < schedule.Length; index++)
            {
                uint sum1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                uint choice = (e & f) ^ (~e & g);
                uint first = unchecked(h + sum1 + choice + constants[index] + schedule[index]);
                uint sum0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                uint majority = (a & b) ^ (a & c) ^ (b & c);
                uint second = unchecked(sum0 + majority);
                h = g;
                g = f;
                f = e;
                e = unchecked(d + first);
                d = c;
                c = b;
                b = a;
                a = unchecked(first + second);
            }
            m_A = unchecked(m_A + a);
            m_B = unchecked(m_B + b);
            m_C = unchecked(m_C + c);
            m_D = unchecked(m_D + d);
            m_E = unchecked(m_E + e);
            m_F = unchecked(m_F + f);
            m_G = unchecked(m_G + g);
            m_H = unchecked(m_H + h);
        }

        static uint RotateRight(uint value, int count) => (value >> count) | (value << (32 - count));
    }
}
