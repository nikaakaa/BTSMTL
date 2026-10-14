using System;
using System.Collections.Generic;
using System.Linq;

namespace ThirdPersonSimulation
{
    public sealed class CanonicalDeclaredIdentityReader : ICanonicalTextResolver
    {
        readonly Entry[] m_Entries;

        public CanonicalDeclaredIdentityReader(IEnumerable<string> identities)
            : this(identities.Distinct(StringComparer.Ordinal).Select(value => new Entry(value)).ToArray())
        {
        }

        CanonicalDeclaredIdentityReader(Entry[] entries)
        {
            m_Entries = entries;
            Array.Sort(m_Entries, CompareEncodedEntries);
        }

        public static CanonicalDeclaredIdentityReader Combine(IEnumerable<CanonicalDeclaredIdentityReader> readers) =>
            new CanonicalDeclaredIdentityReader(readers.SelectMany(reader => reader.m_Entries)
                .GroupBy(entry => entry.Value, StringComparer.Ordinal).Select(group => group.First()).ToArray());

        public string ResolveInitialization(ArraySegment<byte> bytes)
        {
            if (bytes.Array == null)
                return null;
            if (bytes.Count == 0)
                return string.Empty;
            int index = Find(bytes.AsSpan());
            return index >= 0 ? m_Entries[index].Value : CanonicalTextEncoding.DecodeString(bytes);
        }

        CanonicalTextValue ICanonicalTextResolver.ResolveText(ArraySegment<byte> utf16) =>
            CanonicalTextValue.FromString(ResolvePublished(utf16));

        string ResolvePublished(ArraySegment<byte> bytes)
        {
            if (bytes.Array == null)
                return null;
            if (bytes.Count == 0)
                return string.Empty;
            int index = Find(bytes.AsSpan());
            if (index < 0)
                throw new System.IO.InvalidDataException("Identity is not declared by its published content.");
            return m_Entries[index].Value;
        }

        public string ResolvePublished(in CanonicalTextValue value)
        {
            if (value.IsNull)
                return null;
            return ResolvePublished(value.Span);
        }

        public string ResolvePublished(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty)
                return string.Empty;
            int low = 0;
            int high = m_Entries.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = m_Entries[middle].Value.AsSpan().SequenceCompareTo(value);
                if (comparison < 0)
                    low = middle + 1;
                else if (comparison > 0)
                    high = middle - 1;
                else
                    return m_Entries[middle].Value;
            }
            throw new System.IO.InvalidDataException("Identity is not declared by its published content.");
        }

        int Find(ReadOnlySpan<byte> encoded)
        {
            int low = 0;
            int high = m_Entries.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int comparison = CanonicalTextEncoding.CompareEncoded(m_Entries[middle].Encoded.AsSpan(), encoded);
                if (comparison < 0)
                    low = middle + 1;
                else if (comparison > 0)
                    high = middle - 1;
                else
                    return middle;
            }
            return -1;
        }

        static int CompareEncodedEntries(Entry left, Entry right) =>
            CanonicalTextEncoding.CompareEncoded(left.Encoded.AsSpan(), right.Encoded.AsSpan());

        readonly struct Entry
        {
            internal Entry(string value)
            {
                Value = value;
                Encoded = new byte[CanonicalTextEncoding.ByteCount(value.AsSpan())];
                CanonicalTextEncoding.Encode(value.AsSpan(), Encoded.AsSpan());
            }

            internal readonly string Value;
            internal readonly byte[] Encoded;
        }
    }
}
