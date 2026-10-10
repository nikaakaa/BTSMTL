using System;
using System.Buffers.Binary;

namespace ThirdPersonSimulation
{
    public readonly struct EventId : IEquatable<EventId>, IComparable<EventId>
    {
        readonly ulong m_A;
        readonly ulong m_B;
        readonly ulong m_C;
        readonly ulong m_D;

        internal EventId(ulong a, ulong b, ulong c, ulong d)
        {
            m_A = a;
            m_B = b;
            m_C = c;
            m_D = d;
            IsValid = true;
        }

        public bool IsValid { get; }

        public static EventId Parse(ReadOnlySpan<char> value)
        {
            if (value.Length != 64)
                throw new ArgumentException("Event identity requires 64 lowercase hexadecimal characters.", nameof(value));
            Span<byte> bytes = stackalloc byte[32];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)((Hex(value[i * 2]) << 4) | Hex(value[i * 2 + 1]));
            return FromBytes(bytes);
        }

        static int Hex(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'a' && value <= 'f')
                return value - 'a' + 10;
            throw new ArgumentException("Event identity contains an invalid hexadecimal character.");
        }

        public static EventId FromBytes(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 32)
                throw new ArgumentException("Event identity requires 32 bytes.", nameof(bytes));
            return new EventId(BinaryPrimitives.ReadUInt64BigEndian(bytes),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(16)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(24)));
        }

        public void CopyTo(Span<byte> bytes)
        {
            if (!IsValid || bytes.Length < 32)
                throw new ArgumentException("A valid event identity and 32 bytes are required.");
            BinaryPrimitives.WriteUInt64BigEndian(bytes, m_A);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(8), m_B);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(16), m_C);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(24), m_D);
        }

        public void Format(Span<char> characters)
        {
            if (characters.Length < 64)
                throw new ArgumentException("Event identity formatting requires 64 characters.", nameof(characters));
            Span<byte> bytes = stackalloc byte[32];
            CopyTo(bytes);
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                characters[i * 2] = hex[bytes[i] >> 4];
                characters[i * 2 + 1] = hex[bytes[i] & 15];
            }
        }

        public int CompareTo(EventId other)
        {
            int result = IsValid.CompareTo(other.IsValid);
            if (result == 0) result = m_A.CompareTo(other.m_A);
            if (result == 0) result = m_B.CompareTo(other.m_B);
            if (result == 0) result = m_C.CompareTo(other.m_C);
            if (result == 0) result = m_D.CompareTo(other.m_D);
            return result;
        }

        public bool Equals(EventId other) => IsValid == other.IsValid &&
            m_A == other.m_A && m_B == other.m_B && m_C == other.m_C && m_D == other.m_D;
        public override bool Equals(object obj) => obj is EventId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(IsValid, m_A, m_B, m_C, m_D);
        public override string ToString() => IsValid
            ? string.Create(64, this, static (characters, value) => value.Format(characters)) : string.Empty;

    }
}
