using System;

namespace ThirdPersonSimulation
{
    public readonly struct GameplayTagSourceIdentityValue : IComparable<GameplayTagSourceIdentityValue>, IEquatable<GameplayTagSourceIdentityValue>
    {
        readonly CanonicalTextValue m_Literal;
        readonly ulong m_Number;
        readonly byte m_Kind;
        readonly ActorId m_Actor;
        readonly EquipmentSlotId m_Slot;

        public static readonly GameplayTagSourceIdentityValue Empty = Literal(string.Empty);

        GameplayTagSourceIdentityValue(CanonicalTextValue literal, ulong number, byte kind)
        {
            m_Literal = literal;
            m_Number = number;
            m_Kind = kind;
            m_Actor = default;
            m_Slot = default;
        }

        public static GameplayTagSourceIdentityValue Literal(CanonicalTextValue source) => new GameplayTagSourceIdentityValue(source, 0, 0);

        public static GameplayTagSourceIdentityValue Action(ulong actionInstanceId)
        {
            if (actionInstanceId == 0)
                throw new ArgumentOutOfRangeException(nameof(actionInstanceId));
            return new GameplayTagSourceIdentityValue(null, actionInstanceId, 1);
        }

        public static GameplayTagSourceIdentityValue Effect(ulong effectHandle) => new GameplayTagSourceIdentityValue(null, effectHandle, 2);

        GameplayTagSourceIdentityValue(ActorId actor, EquipmentSlotId slot, ulong revision)
        {
            m_Literal = default;
            m_Number = revision;
            m_Kind = 3;
            m_Actor = actor;
            m_Slot = slot;
        }

        public static GameplayTagSourceIdentityValue Equipment(ActorId actor, EquipmentSlotId slot, ulong revision) =>
            new GameplayTagSourceIdentityValue(actor, slot, revision);

        public bool IsEmpty => m_Kind == 0 && m_Literal.IsEmpty;
        public bool IsExternalSource => m_Kind == 1 || m_Kind == 3 ||
            m_Kind == 0 && m_Literal != "initial";
        public ActorId Actor => m_Actor;
        public EquipmentSlotId Slot => m_Slot;
        int IdentityBufferLength => m_Kind == 3 ? 14 + DigitCount((ulong)m_Actor.Value.Length) +
            m_Actor.Value.Length + DigitCount((ulong)m_Slot.Value.Length) + m_Slot.Value.Length + 20 : 27;
        public byte Kind => m_Kind;
        public CanonicalTextValue LiteralIdentity => m_Literal;
        public ulong NumericIdentity => m_Number;

        public void Retain()
        {
            if (m_Kind == 0)
                m_Literal.Retain();
            else if (m_Kind == 3)
            {
                CanonicalTextValue actor = m_Actor.Value;
                CanonicalTextValue slot = m_Slot.Value;
                actor.Retain();
                slot.Retain();
            }
        }

        public void Release()
        {
            if (m_Kind == 0)
                m_Literal.Release();
            else if (m_Kind == 3)
            {
                CanonicalTextValue actor = m_Actor.Value;
                CanonicalTextValue slot = m_Slot.Value;
                actor.Release();
                slot.Release();
            }
        }

        public int CompareTo(GameplayTagSourceIdentityValue other)
        {
            if (!m_Literal.IsNull && !other.m_Literal.IsNull)
                return m_Literal.CompareTo(other.m_Literal);
            if (m_Literal.IsNull && other.m_Literal.IsNull)
            {
                if (m_Kind != other.m_Kind)
                    return m_Kind < other.m_Kind ? -1 : 1;
                if (m_Kind != 3)
                    return CompareNumbers(m_Number, other.m_Number);
            }
            Span<char> leftBuffer = stackalloc char[IdentityBufferLength];
            Span<char> rightBuffer = stackalloc char[other.IdentityBufferLength];
            int identityOrder = IdentitySpan(leftBuffer).SequenceCompareTo(other.IdentitySpan(rightBuffer));
            return identityOrder != 0 ? identityOrder : m_Kind.CompareTo(other.m_Kind);
        }

        public bool Equals(GameplayTagSourceIdentityValue other) => CompareTo(other) == 0;

        public override bool Equals(object other) => other is GameplayTagSourceIdentityValue source && Equals(source);

        public override int GetHashCode()
        {
            Span<char> characters = stackalloc char[IdentityBufferLength];
            ReadOnlySpan<char> identity = IdentitySpan(characters);
            uint hash = 2166136261;
            for (int index = 0; index < identity.Length; index++)
                hash = unchecked((hash ^ identity[index]) * 16777619);
            return unchecked((int)hash);
        }

        static int CompareNumbers(ulong left, ulong right)
        {
            int leftLength = DigitCount(left);
            int rightLength = DigitCount(right);
            int lengthDifference = leftLength - rightLength;
            if (lengthDifference == 0)
                return left.CompareTo(right);
            ulong divisor = 1;
            for (int index = 0; index < Math.Abs(lengthDifference); index++)
                divisor *= 10;
            int prefixOrder = lengthDifference > 0
                ? (left / divisor).CompareTo(right)
                : left.CompareTo(right / divisor);
            return prefixOrder != 0 ? prefixOrder : lengthDifference;
        }

        static int DigitCount(ulong number)
        {
            int count = 1;
            while (number >= 10)
            {
                number /= 10;
                count++;
            }
            return count;
        }

        ReadOnlySpan<char> IdentitySpan(Span<char> characters)
        {
            if (m_Kind == 0)
                return m_Literal.Span;
            if (m_Kind == 3)
            {
                int offset = 0;
                Append("equipment:", characters, ref offset);
                AppendNumber((ulong)m_Actor.Value.Length, characters, ref offset);
                Append(":", characters, ref offset);
                Append(m_Actor.Value, characters, ref offset);
                Append(":", characters, ref offset);
                AppendNumber((ulong)m_Slot.Value.Length, characters, ref offset);
                Append(":", characters, ref offset);
                Append(m_Slot.Value, characters, ref offset);
                Append(":", characters, ref offset);
                AppendNumber(m_Number, characters, ref offset);
                return characters.Slice(0, offset);
            }
            string prefix = m_Kind == 1 ? "action:" : "effect:";
            prefix.AsSpan().CopyTo(characters);
            m_Number.TryFormat(characters.Slice(prefix.Length), out int written,
                provider: m_Kind == 1 ? System.Globalization.CultureInfo.InvariantCulture : System.Globalization.CultureInfo.CurrentCulture);
            return characters.Slice(0, prefix.Length + written);
        }

        static void Append(CanonicalTextValue value, Span<char> buffer, ref int offset)
        {
            value.Span.CopyTo(buffer.Slice(offset));
            offset += value.Length;
        }

        static void AppendNumber(ulong value, Span<char> buffer, ref int offset)
        {
            value.TryFormat(buffer.Slice(offset), out int written, provider: System.Globalization.CultureInfo.InvariantCulture);
            offset += written;
        }

        internal void Write(CanonicalWriter writer)
        {
            writer.WriteByte(m_Kind);
            switch (m_Kind)
            {
                case 0:
                    writer.WriteText(in m_Literal);
                    break;
                case 3:
                    writer.WriteText(m_Actor.Value);
                    writer.WriteText(m_Slot.Value);
                    writer.WriteUInt64(m_Number);
                    break;
                default:
                    writer.WriteUInt64(m_Number);
                    break;
            }
        }

        static readonly Func<CanonicalReader, CanonicalTextValue> s_ReadIdentity = reader => reader.ReadText();

        public static GameplayTagSourceIdentityValue Read(CanonicalReader reader) => Read(reader, s_ReadIdentity);

        internal static GameplayTagSourceIdentityValue Read(CanonicalReader reader, Func<CanonicalReader, CanonicalTextValue> readIdentity) => reader.ReadByte() switch
        {
            0 => Literal(readIdentity(reader)),
            1 => Action(reader.ReadUInt64()),
            2 => Effect(reader.ReadUInt64()),
            3 => Equipment(new ActorId(readIdentity(reader)), new EquipmentSlotId(readIdentity(reader)), reader.ReadUInt64()),
            _ => throw new System.IO.InvalidDataException("Gameplay Tag source kind is invalid.")
        };
    }
}

