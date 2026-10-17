using System;
using System.Buffers.Binary;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public static class SimulationIdentity
    {
        public static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Identity is required.", parameter);
            return value.Trim();
        }
    }

    [ValueStorage]
    public readonly partial struct StableHash : IEquatable<StableHash>, IComparable<StableHash>
    {
        readonly ulong m_A;
        readonly ulong m_B;
        readonly ulong m_C;
        readonly ulong m_D;

        public StableHash(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
                throw new ArgumentException("Stable hash must contain 64 lowercase hexadecimal characters.", nameof(value));
            Span<byte> bytes = stackalloc byte[32];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)((Hex(value[i * 2]) << 4) | Hex(value[i * 2 + 1]));
            this = new StableHash(bytes);
        }

        internal StableHash(ReadOnlySpan<byte> bytes)
        {
            m_A = BinaryPrimitives.ReadUInt64BigEndian(bytes);
            m_B = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8));
            m_C = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(16));
            m_D = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(24));
            IsValid = true;
        }

        internal static StableHash ParseUtf8(ReadOnlySpan<byte> value)
        {
            if (value.Length != 64)
                throw new ArgumentException("Stable hash must contain 64 lowercase hexadecimal characters.", nameof(value));
            Span<byte> bytes = stackalloc byte[32];
            for (int index = 0; index < bytes.Length; index++)
                bytes[index] = (byte)((Hex((char)value[index * 2]) << 4) | Hex((char)value[index * 2 + 1]));
            return new StableHash(bytes);
        }

        static int Hex(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'a' && value <= 'f')
                return value - 'a' + 10;
            throw new ArgumentException("Stable hash must contain 64 lowercase hexadecimal characters.", nameof(value));
        }

        public string Value => IsValid ? ToString() : null;
        public bool IsValid { get; }

        public void Format(Span<char> characters)
        {
            Span<byte> bytes = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, m_A);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(8), m_B);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(16), m_C);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(24), m_D);
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                characters[i * 2] = hex[bytes[i] >> 4];
                characters[i * 2 + 1] = hex[bytes[i] & 15];
            }
        }

        public int CompareTo(StableHash other)
        {
            int result = IsValid.CompareTo(other.IsValid);
            if (result == 0) result = m_A.CompareTo(other.m_A);
            if (result == 0) result = m_B.CompareTo(other.m_B);
            if (result == 0) result = m_C.CompareTo(other.m_C);
            if (result == 0) result = m_D.CompareTo(other.m_D);
            return result;
        }

        public bool Equals(StableHash other) => IsValid == other.IsValid &&
            m_A == other.m_A && m_B == other.m_B && m_C == other.m_C && m_D == other.m_D;
        public override bool Equals(object obj) => obj is StableHash other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(IsValid, m_A, m_B, m_C, m_D);
        public override string ToString() => IsValid
            ? string.Create(64, this, static (characters, value) => value.Format(characters)) : string.Empty;
        public static bool operator ==(StableHash left, StableHash right) => left.Equals(right);
        public static bool operator !=(StableHash left, StableHash right) => !left.Equals(right);
    }

    public readonly partial struct NumericProfileId : IEquatable<NumericProfileId>, IComparable<NumericProfileId>
    {
        public NumericProfileId(string value) { Value = value; RequireValid(); }
        [ValueTextRules(Declared = true)]
        public string Value { get; }
        [ValueValidation]
        void RequireValid() => SimulationIdentity.Require(Value, "value");
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(NumericProfileId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(NumericProfileId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NumericProfileId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(NumericProfileId left, NumericProfileId right) => left.Equals(right);
        public static bool operator !=(NumericProfileId left, NumericProfileId right) => !left.Equals(right);
    }

    public readonly partial struct TargetAbiVersion : IEquatable<TargetAbiVersion>
    {
        public TargetAbiVersion(int value)
        {
            Value = value;
            RequireValid();
        }
        public int Value { get; }
        [ValueValidation]
        void RequireValid()
        {
            if (Value <= 0)
                throw new ArgumentOutOfRangeException("value");
        }
        public bool IsValid => Value > 0;
        public bool Equals(TargetAbiVersion other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TargetAbiVersion other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }

    [ValueStorage]
    public readonly partial struct OperationHandle : IEquatable<OperationHandle>, IComparable<OperationHandle>
    {
        readonly int m_EncodedValue;

        public static OperationHandle Invalid => default;

        public OperationHandle(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_EncodedValue = checked(value + 1);
        }
        public int Value => m_EncodedValue - 1;
        public bool IsValid => m_EncodedValue > 0;
        public int CompareTo(OperationHandle other) => Value.CompareTo(other.Value);
        public bool Equals(OperationHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is OperationHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }
}
