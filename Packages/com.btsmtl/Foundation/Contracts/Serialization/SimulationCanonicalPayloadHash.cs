using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace ThirdPersonSimulation
{
    public static class SimulationCanonicalPayloadHash
    {
        static readonly uint[] s_RoundConstants =
        {
            0x428a2f98u, 0x71374491u, 0xb5c0fbcfu, 0xe9b5dba5u, 0x3956c25bu, 0x59f111f1u, 0x923f82a4u, 0xab1c5ed5u,
            0xd807aa98u, 0x12835b01u, 0x243185beu, 0x550c7dc3u, 0x72be5d74u, 0x80deb1feu, 0x9bdc06a7u, 0xc19bf174u,
            0xe49b69c1u, 0xefbe4786u, 0x0fc19dc6u, 0x240ca1ccu, 0x2de92c6fu, 0x4a7484aau, 0x5cb0a9dcu, 0x76f988dau,
            0x983e5152u, 0xa831c66du, 0xb00327c8u, 0xbf597fc7u, 0xc6e00bf3u, 0xd5a79147u, 0x06ca6351u, 0x14292967u,
            0x27b70a85u, 0x2e1b2138u, 0x4d2c6dfcu, 0x53380d13u, 0x650a7354u, 0x766a0abbu, 0x81c2c92eu, 0x92722c85u,
            0xa2bfe8a1u, 0xa81a664bu, 0xc24b8b70u, 0xc76c51a3u, 0xd192e819u, 0xd6990624u, 0xf40e3585u, 0x106aa070u,
            0x19a4c116u, 0x1e376c08u, 0x2748774cu, 0x34b0bcb5u, 0x391c0cb3u, 0x4ed8aa4au, 0x5b9cca4fu, 0x682e6ff3u,
            0x748f82eeu, 0x78a5636fu, 0x84c87814u, 0x8cc70208u, 0x90befffau, 0xa4506cebu, 0xbef9a3f7u, 0xc67178f2u
        };

        public static StableHash Compute(byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            return Compute(payload.AsSpan());
        }

        public static StableHash Compute(ReadOnlySpan<byte> payload)
        {
            Span<uint> state = stackalloc uint[8];
            Begin(state);
            Span<uint> schedule = stackalloc uint[64];
            int fullBytes = payload.Length & ~63;
            for (int offset = 0; offset < fullBytes; offset += 64)
                ProcessBlock(payload.Slice(offset, 64), state, schedule);
            Span<byte> block = stackalloc byte[64];
            payload.Slice(fullBytes).CopyTo(block);
            return Complete(block, payload.Length - fullBytes, (ulong)payload.Length, state, schedule);
        }

        static void Begin(Span<uint> state)
        {
            state[0] = 0x6a09e667u;
            state[1] = 0xbb67ae85u;
            state[2] = 0x3c6ef372u;
            state[3] = 0xa54ff53au;
            state[4] = 0x510e527fu;
            state[5] = 0x9b05688cu;
            state[6] = 0x1f83d9abu;
            state[7] = 0x5be0cd19u;
        }

        static StableHash Complete(Span<byte> block, int buffered, ulong byteLength,
            Span<uint> state, Span<uint> schedule)
        {
            block.Slice(buffered).Clear();
            block[buffered] = 0x80;
            if (buffered >= 56)
            {
                ProcessBlock(block, state, schedule);
                block.Clear();
            }
            BinaryPrimitives.WriteUInt64BigEndian(block.Slice(56), unchecked(byteLength * 8));
            ProcessBlock(block, state, schedule);
            Span<byte> hash = stackalloc byte[32];
            for (int index = 0; index < state.Length; index++)
                BinaryPrimitives.WriteUInt32BigEndian(hash.Slice(index * 4), state[index]);
            return new StableHash(hash);
        }

        internal sealed class Accumulator
        {
            readonly uint[] m_Words = new uint[72];
            readonly byte[] m_Block = new byte[64];
            int m_Buffered;
            ulong m_ByteLength;

            internal void Begin()
            {
                SimulationCanonicalPayloadHash.Begin(m_Words.AsSpan(0, 8));
                m_Buffered = 0;
                m_ByteLength = 0;
            }

            internal void Append(ReadOnlySpan<byte> value)
            {
                m_ByteLength = unchecked(m_ByteLength + (ulong)value.Length);
                if (m_Buffered != 0)
                {
                    int count = Math.Min(64 - m_Buffered, value.Length);
                    value.Slice(0, count).CopyTo(m_Block.AsSpan(m_Buffered));
                    value = value.Slice(count);
                    m_Buffered += count;
                    if (m_Buffered != 64)
                        return;
                    ProcessBlock(m_Block, m_Words.AsSpan(0, 8), m_Words.AsSpan(8));
                    m_Buffered = 0;
                }
                int fullBytes = value.Length & ~63;
                for (int offset = 0; offset < fullBytes; offset += 64)
                    ProcessBlock(value.Slice(offset, 64), m_Words.AsSpan(0, 8), m_Words.AsSpan(8));
                value.Slice(fullBytes).CopyTo(m_Block);
                m_Buffered = value.Length - fullBytes;
            }

            internal void AppendAscii(byte value)
            {
                m_ByteLength = unchecked(m_ByteLength + 1);
                m_Block[m_Buffered++] = value;
                if (m_Buffered == 64)
                {
                    ProcessBlock(m_Block, m_Words.AsSpan(0, 8), m_Words.AsSpan(8));
                    m_Buffered = 0;
                }
            }

            internal void AppendHash(StableHash value)
            {
                Span<char> characters = stackalloc char[64];
                Span<byte> bytes = stackalloc byte[64];
                value.Format(characters);
                for (int index = 0; index < characters.Length; index++)
                    bytes[index] = (byte)characters[index];
                Append(bytes);
            }

            internal void AppendInt32(int value)
            {
                if (value < 0)
                {
                    AppendAscii((byte)'-');
                    AppendUInt64((ulong)(-(long)value));
                }
                else
                {
                    AppendUInt64((ulong)value);
                }
            }

            internal void AppendUInt64(ulong value)
            {
                Span<byte> digits = stackalloc byte[20];
                int start = digits.Length;
                do
                {
                    digits[--start] = (byte)('0' + value % 10);
                    value /= 10;
                } while (value != 0);
                Append(digits.Slice(start));
            }

            internal void AppendText(CanonicalTextValue value)
            {
                ReadOnlySpan<char> characters = value.Span;
                Span<byte> bytes = stackalloc byte[128];
                while (characters.Length != 0)
                {
                    int count = Math.Min(bytes.Length / sizeof(char), characters.Length);
                    CanonicalTextEncoding.Encode(characters.Slice(0, count), bytes);
                    Append(bytes.Slice(0, count * sizeof(char)));
                    characters = characters.Slice(count);
                }
            }

            internal StableHash Complete()
            {
                Span<uint> state = stackalloc uint[8];
                Span<byte> block = stackalloc byte[64];
                m_Words.AsSpan(0, 8).CopyTo(state);
                m_Block.AsSpan().CopyTo(block);
                return SimulationCanonicalPayloadHash.Complete(
                    block, m_Buffered, m_ByteLength, state, m_Words.AsSpan(8));
            }
        }

        static void ProcessBlock(ReadOnlySpan<byte> block, Span<uint> state, Span<uint> schedule)
        {
            unchecked
            {
                for (int index = 0; index < 16; index++)
                    schedule[index] = BinaryPrimitives.ReadUInt32BigEndian(block.Slice(index * 4));
                for (int index = 16; index < 64; index++)
                {
                    uint first = schedule[index - 15];
                    uint second = schedule[index - 2];
                    uint sigma0 = RotateRight(first, 7) ^ RotateRight(first, 18) ^ (first >> 3);
                    uint sigma1 = RotateRight(second, 17) ^ RotateRight(second, 19) ^ (second >> 10);
                    schedule[index] = schedule[index - 16] + sigma0 + schedule[index - 7] + sigma1;
                }
                uint a = state[0];
                uint b = state[1];
                uint c = state[2];
                uint d = state[3];
                uint e = state[4];
                uint f = state[5];
                uint g = state[6];
                uint h = state[7];
                uint[] constants = s_RoundConstants;
                for (int index = 0; index < 64; index += 8)
                {
                    Round(a, b, c, ref d, e, f, g, ref h, schedule[index], constants[index]);
                    Round(h, a, b, ref c, d, e, f, ref g, schedule[index + 1], constants[index + 1]);
                    Round(g, h, a, ref b, c, d, e, ref f, schedule[index + 2], constants[index + 2]);
                    Round(f, g, h, ref a, b, c, d, ref e, schedule[index + 3], constants[index + 3]);
                    Round(e, f, g, ref h, a, b, c, ref d, schedule[index + 4], constants[index + 4]);
                    Round(d, e, f, ref g, h, a, b, ref c, schedule[index + 5], constants[index + 5]);
                    Round(c, d, e, ref f, g, h, a, ref b, schedule[index + 6], constants[index + 6]);
                    Round(b, c, d, ref e, f, g, h, ref a, schedule[index + 7], constants[index + 7]);
                }
                state[0] += a;
                state[1] += b;
                state[2] += c;
                state[3] += d;
                state[4] += e;
                state[5] += f;
                state[6] += g;
                state[7] += h;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Round(uint a, uint b, uint c, ref uint d, uint e, uint f, uint g, ref uint h,
            uint word, uint constant)
        {
            unchecked
            {
                uint sigma1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                uint choose = (e & (f ^ g)) ^ g;
                uint first = h + sigma1 + choose + constant + word;
                uint sigma0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                uint majority = (a & b) | (c & (a | b));
                d += first;
                h = first + sigma0 + majority;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint RotateRight(uint value, int shift) => (value >> shift) | (value << (32 - shift));
    }

}
