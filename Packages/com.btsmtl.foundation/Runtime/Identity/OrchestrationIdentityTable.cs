using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationIdentityTable
    {
        [ValueStorageIgnore] readonly int[] m_SortedIdentityIds;
        [ValueStorageIgnore] readonly Lazy<StableHash> m_Identity;

        public OrchestrationIdentityTable(ReadOnlyMemory<string> entries, StableHash identity)
        {
            Entries = entries;
            m_Identity = new Lazy<StableHash>(() => identity);
            m_SortedIdentityIds = new int[entries.Length];
            for (int i = 0; i < m_SortedIdentityIds.Length; i++)
                m_SortedIdentityIds[i] = i + 1;
            Array.Sort(m_SortedIdentityIds, (left, right) =>
                string.CompareOrdinal(Entries.Span[left - 1], Entries.Span[right - 1]));
        }

        public OrchestrationIdentityTable(ReadOnlyMemory<string> entries, int[] sortedIdentityIds,
            Func<StableHash> computeIdentity)
        {
            Entries = entries;
            m_SortedIdentityIds = sortedIdentityIds;
            m_Identity = new Lazy<StableHash>(computeIdentity);
        }

        public ReadOnlyMemory<string> Entries { get; }
        public StableHash Identity => m_Identity.Value;
        public int Count => Entries.Length;
        public ReadOnlyMemory<int> SortedIdentityIds => m_SortedIdentityIds;

        public string Resolve(int identity) => identity == 0 ? string.Empty : Entries.Span[identity - 1];

        public int Bind(string text) => Bind(text.AsSpan());

        public int Bind(ReadOnlySpan<char> text)
        {
            if (TryBind(text, out int identity))
                return identity;
            throw new KeyNotFoundException($"身份文本 '{text.ToString()}' 未包含在已发布内容表 '{Identity}' 中。");
        }

        public bool TryBind(ReadOnlySpan<char> text, out int identity)
        {
            if (text.IsEmpty)
            {
                identity = 0;
                return true;
            }
            ReadOnlySpan<string> entries = Entries.Span;
            int low = 0;
            int high = m_SortedIdentityIds.Length - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int candidate = m_SortedIdentityIds[middle];
                int order = entries[candidate - 1].AsSpan().CompareTo(text, StringComparison.Ordinal);
                if (order < 0)
                    low = middle + 1;
                else if (order > 0)
                    high = middle - 1;
                else
                {
                    identity = candidate;
                    return true;
                }
            }
            identity = 0;
            return false;
        }
    }
}
