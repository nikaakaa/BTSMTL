using System;
using System.Globalization;
using System.Security.Cryptography;

namespace ThirdPersonSimulation
{
    public ref struct EventIdBuilder
    {
        readonly IncrementalHash m_Hash;
        bool m_HasField;

        public EventIdBuilder(IncrementalHash hash)
        {
            m_Hash = hash;
            m_HasField = false;
        }

        public void Append(ReadOnlySpan<char> value)
        {
            BeginField();
            StableHash.AppendText(m_Hash, value);
        }

        public void Append(ulong value)
        {
            BeginField();
            Span<char> characters = stackalloc char[20];
            value.TryFormat(characters, out int count, provider: CultureInfo.InvariantCulture);
            Span<byte> bytes = stackalloc byte[20];
            for (int index = 0; index < count; index++)
                bytes[index] = (byte)characters[index];
            m_Hash.AppendData(bytes.Slice(0, count));
        }

        void BeginField()
        {
            if (m_HasField)
            {
                Span<byte> separator = stackalloc byte[1];
                separator[0] = 0x1f;
                m_Hash.AppendData(separator);
            }
            m_HasField = true;
        }

        public EventId Build()
        {
            Span<byte> digest = stackalloc byte[32];
            m_Hash.TryGetHashAndReset(digest, out _);
            return EventId.FromBytes(digest);
        }
    }
}
