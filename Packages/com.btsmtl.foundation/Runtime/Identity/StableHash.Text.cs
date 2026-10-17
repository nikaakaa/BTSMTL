using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ThirdPersonSimulation
{
    public readonly partial struct StableHash
    {
        [ThreadStatic] static IncrementalHash s_TextHash;

        public static StableHash Compute(params string[] values)
        {
            IncrementalHash hash = s_TextHash ??= IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> separator = stackalloc byte[1];
            separator[0] = 0x1f;
            if (values != null)
            {
                for (int index = 0; index < values.Length; index++)
                {
                    if (index != 0)
                        hash.AppendData(separator);
                    AppendText(hash, values[index].AsSpan());
                }
            }
            return Compute(hash);
        }

        public static StableHash Compute(IncrementalHash hash)
        {
            Span<byte> digest = stackalloc byte[32];
            hash.TryGetHashAndReset(digest, out _);
            return new StableHash(digest);
        }

        public static void AppendText(IncrementalHash hash, ReadOnlySpan<char> value)
        {
            if (BitConverter.IsLittleEndian)
            {
                hash.AppendData(MemoryMarshal.AsBytes(value));
                return;
            }
            Span<byte> bytes = stackalloc byte[128];
            while (!value.IsEmpty)
            {
                int count = Math.Min(bytes.Length / sizeof(char), value.Length);
                for (int index = 0; index < count; index++)
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(index * sizeof(char)), value[index]);
                hash.AppendData(bytes.Slice(0, count * sizeof(char)));
                value = value.Slice(count);
            }
        }
    }
}
