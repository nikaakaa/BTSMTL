using System;
using BTSMTL.Orchestration;

namespace BTSMTL.EventGraphs
{
    internal sealed class EventGraphTextStorage
    {
        readonly FrozenOrchestrationIdentities m_Frozen;
        readonly string[] m_Texts;
        readonly int[] m_Buckets;
        readonly int[] m_ContentRanks;
        readonly int[] m_RosterRanks;
        readonly int[] m_RuntimeRanks;
        readonly int[] m_SortedRuntimeIndices;
        int m_Count;

        internal EventGraphTextStorage(FrozenOrchestrationIdentities frozen, int referenceCapacity)
        {
            m_Frozen = frozen;
            m_Texts = new string[referenceCapacity];
            m_Buckets = new int[checked(referenceCapacity * 2 + 1)];
            m_ContentRanks = new int[frozen.Table.Count + 1];
            m_RosterRanks = new int[frozen.Ordinals.RosterRanks.Length];
            m_RuntimeRanks = new int[referenceCapacity + 1];
            m_SortedRuntimeIndices = new int[referenceCapacity];
            Ordinals = new OrchestrationIdentityOrdinalView(m_ContentRanks, m_RosterRanks, m_RuntimeRanks);
            Reset();
        }

        internal OrchestrationIdentityOrdinalView Ordinals { get; }

        internal OrchestrationIdentityValue Capture(string text)
        {
            if (text is null)
                return OrchestrationIdentityValue.Null;
            if (m_Frozen.Table.TryBind(text.AsSpan(), out int identity))
                return OrchestrationIdentityValue.Content(identity);
            int bucket = (int)(unchecked((uint)StringComparer.Ordinal.GetHashCode(text)) % (uint)m_Buckets.Length);
            while (m_Buckets[bucket] != 0)
            {
                int index = m_Buckets[bucket];
                if (string.Equals(m_Texts[index - 1], text, StringComparison.Ordinal))
                    return OrchestrationIdentityValue.RuntimeText(index);
                bucket++;
                if (bucket == m_Buckets.Length)
                    bucket = 0;
            }
            m_Texts[m_Count] = text;
            m_Count++;
            m_Buckets[bucket] = m_Count;
            return OrchestrationIdentityValue.RuntimeText(m_Count);
        }

        internal string Resolve(in OrchestrationIdentityValue value) => value.Space switch
        {
            OrchestrationIdentitySpace.Null => null,
            OrchestrationIdentitySpace.Content => m_Frozen.Table.Resolve(value.Index),
            OrchestrationIdentitySpace.Roster => m_Frozen.Resolve(in value),
            OrchestrationIdentitySpace.RuntimeText => m_Texts[value.Index - 1],
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

        internal void PrepareOrdinals()
        {
            for (int i = 0; i < m_Count; i++)
            {
                int current = i + 1;
                int insertion = i;
                string text = m_Texts[i];
                while (insertion > 0 && string.CompareOrdinal(
                           m_Texts[m_SortedRuntimeIndices[insertion - 1] - 1], text) > 0)
                {
                    m_SortedRuntimeIndices[insertion] = m_SortedRuntimeIndices[insertion - 1];
                    insertion--;
                }
                m_SortedRuntimeIndices[insertion] = current;
            }

            ReadOnlySpan<int> contentIds = m_Frozen.Table.SortedIdentityIds.Span;
            int content = 0;
            int runtime = 0;
            int rank = 1;
            while (content < contentIds.Length || runtime < m_Count)
            {
                if (runtime == m_Count || content < contentIds.Length && string.CompareOrdinal(
                        m_Frozen.Table.Resolve(contentIds[content]), m_Texts[m_SortedRuntimeIndices[runtime] - 1]) < 0)
                    m_ContentRanks[contentIds[content++]] = rank++;
                else
                    m_RuntimeRanks[m_SortedRuntimeIndices[runtime++]] = rank++;
            }
            ReadOnlySpan<int> roster = m_Frozen.Ordinals.RosterRanks.Span;
            for (int i = 0; i < roster.Length; i++)
                m_RosterRanks[i] = m_ContentRanks[roster[i]];
        }

        internal void Reset()
        {
            Array.Clear(m_Texts, 0, m_Count);
            Array.Clear(m_Buckets, 0, m_Buckets.Length);
            m_Count = 0;
            m_Frozen.Ordinals.ContentRanks.Span.CopyTo(m_ContentRanks);
            m_Frozen.Ordinals.RosterRanks.Span.CopyTo(m_RosterRanks);
            Array.Clear(m_RuntimeRanks, 0, m_RuntimeRanks.Length);
        }
    }
}
