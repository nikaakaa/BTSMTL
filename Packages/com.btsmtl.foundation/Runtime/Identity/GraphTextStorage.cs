using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    public sealed class GraphTextStorage
    {
        readonly FrozenOrchestrationIdentities m_Frozen;
        readonly string[] m_Texts;
        readonly int[] m_Buckets;
        readonly int[] m_ContentRanks;
        readonly int[] m_RosterRanks;
        readonly int[] m_RuntimeRanks;
        readonly int[] m_ReferenceRanks;
        readonly int[] m_SortedRuntimeIndices;
        readonly int[] m_ReferenceCounts;
        readonly int[] m_FreeIndices;
        readonly OrchestrationIdentityValue[] m_References;
        int m_Count;
        int m_FreeCount;

        public GraphTextStorage(FrozenOrchestrationIdentities frozen, int referenceCapacity,
            int referenceSlotCount = 0)
        {
            m_Frozen = frozen;
            m_Texts = new string[referenceCapacity];
            m_Buckets = new int[checked(referenceCapacity * 2 + 1)];
            m_ContentRanks = new int[frozen.Table.Count + 1];
            m_RosterRanks = new int[frozen.Ordinals.RosterRanks.Length];
            m_RuntimeRanks = new int[referenceCapacity + 1];
            m_SortedRuntimeIndices = new int[referenceCapacity];
            m_ReferenceCounts = new int[referenceCapacity];
            m_FreeIndices = new int[referenceCapacity];
            m_References = new OrchestrationIdentityValue[referenceSlotCount];
            m_ReferenceRanks = referenceSlotCount == 0 ? m_RuntimeRanks : new int[referenceSlotCount + 1];
            Ordinals = new OrchestrationIdentityOrdinalView(m_ContentRanks, m_RosterRanks, m_ReferenceRanks);
            Reset();
        }

        public FrozenOrchestrationIdentities Frozen => m_Frozen;
        public int Capacity => m_Texts.Length;
        public int ReferenceSlotCount => m_References.Length;
        public OrchestrationIdentityOrdinalView Ordinals { get; }

        public OrchestrationIdentityValue Capture(string text)
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
                {
                    m_ReferenceCounts[index - 1]++;
                    return OrchestrationIdentityValue.RuntimeText(index);
                }
                bucket++;
                if (bucket == m_Buckets.Length)
                    bucket = 0;
            }
            int available = m_FreeIndices[--m_FreeCount];
            m_Texts[available] = text;
            m_ReferenceCounts[available] = 1;
            m_Count++;
            m_Buckets[bucket] = available + 1;
            return OrchestrationIdentityValue.RuntimeText(available + 1);
        }

        public OrchestrationIdentityValue Capture(int referenceSlot, string text)
        {
            Release(in m_References[referenceSlot]);
            OrchestrationIdentityValue value = Capture(text);
            m_References[referenceSlot] = value;
            return value.Space == OrchestrationIdentitySpace.RuntimeText
                ? OrchestrationIdentityValue.RuntimeText(referenceSlot + 1) : value;
        }

        void Release(in OrchestrationIdentityValue value)
        {
            if (value.Space != OrchestrationIdentitySpace.RuntimeText)
                return;
            int index = value.Index - 1;
            if (--m_ReferenceCounts[index] != 0)
                return;
            int bucket = Bucket(m_Texts[index]);
            while (m_Buckets[bucket] != value.Index)
                bucket = NextBucket(bucket);
            m_Buckets[bucket] = 0;
            bucket = NextBucket(bucket);
            while (m_Buckets[bucket] != 0)
            {
                int displaced = m_Buckets[bucket];
                m_Buckets[bucket] = 0;
                InsertBucket(displaced);
                bucket = NextBucket(bucket);
            }
            m_Texts[index] = null;
            m_RuntimeRanks[value.Index] = 0;
            m_FreeIndices[m_FreeCount++] = index;
            m_Count--;
        }

        int Bucket(string text) => (int)(unchecked((uint)StringComparer.Ordinal.GetHashCode(text)) %
            (uint)m_Buckets.Length);

        int NextBucket(int bucket) => bucket + 1 == m_Buckets.Length ? 0 : bucket + 1;

        void InsertBucket(int index)
        {
            int bucket = Bucket(m_Texts[index - 1]);
            while (m_Buckets[bucket] != 0)
                bucket = NextBucket(bucket);
            m_Buckets[bucket] = index;
        }

        public string Resolve(in OrchestrationIdentityValue value) =>
            value.Space == OrchestrationIdentitySpace.RuntimeText && m_References.Length != 0
                ? ResolveCanonical(in m_References[value.Index - 1]) : ResolveCanonical(in value);

        string ResolveCanonical(in OrchestrationIdentityValue value) => value.Space switch
        {
            OrchestrationIdentitySpace.Null => null,
            OrchestrationIdentitySpace.Content => m_Frozen.Table.Resolve(value.Index),
            OrchestrationIdentitySpace.Roster => m_Frozen.Resolve(in value),
            OrchestrationIdentitySpace.RuntimeText => m_Texts[value.Index - 1],
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

        public void PrepareOrdinals()
        {
            int written = 0;
            for (int i = 0; i < m_Texts.Length; i++)
            {
                if (m_ReferenceCounts[i] == 0)
                    continue;
                int current = i + 1;
                int insertion = written++;
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
            for (int i = 0; i < m_References.Length; i++)
                m_ReferenceRanks[i + 1] = m_References[i].Space == OrchestrationIdentitySpace.RuntimeText
                    ? m_RuntimeRanks[m_References[i].Index] : m_ContentRanks[m_References[i].Index];
        }

        public void CopyFrom(GraphTextStorage source)
        {
            source.m_Texts.AsSpan().CopyTo(m_Texts);
            source.m_Buckets.AsSpan().CopyTo(m_Buckets);
            source.m_ContentRanks.AsSpan().CopyTo(m_ContentRanks);
            source.m_RosterRanks.AsSpan().CopyTo(m_RosterRanks);
            source.m_RuntimeRanks.AsSpan().CopyTo(m_RuntimeRanks);
            source.m_SortedRuntimeIndices.AsSpan().CopyTo(m_SortedRuntimeIndices);
            source.m_ReferenceCounts.AsSpan().CopyTo(m_ReferenceCounts);
            source.m_FreeIndices.AsSpan().CopyTo(m_FreeIndices);
            source.m_References.AsSpan().CopyTo(m_References);
            if (m_References.Length != 0)
                source.m_ReferenceRanks.AsSpan().CopyTo(m_ReferenceRanks);
            m_Count = source.m_Count;
            m_FreeCount = source.m_FreeCount;
        }

        public void RestoreReferences(GraphTextStorage source, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++)
            {
                OrchestrationIdentityValue identity = source.m_References[i];
                Capture(i, source.ResolveCanonical(in identity));
            }
        }

        public void WriteRecordedState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            for (int i = 0; i < m_Texts.Length; i++)
                writer.WriteText(m_Texts[i]);
            writer.WriteSpan<int>(m_ReferenceCounts);
            writer.WriteSpan<OrchestrationIdentityValue>(m_References);
            writer.Write(m_FreeCount);
            writer.WriteSpan<int>(m_FreeIndices);
        }

        public void RestoreRecordedState(ref RecordedRuntimeStateReader reader)
        {
            Reset();
            for (int i = 0; i < m_Texts.Length; i++)
                m_Texts[i] = reader.ReadText();
            reader.ReadSpan<int>(m_ReferenceCounts.Length).CopyTo(m_ReferenceCounts);
            reader.ReadSpan<OrchestrationIdentityValue>(m_References.Length).CopyTo(m_References);
            m_FreeCount = reader.Read<int>();
            reader.ReadSpan<int>(m_FreeIndices.Length).CopyTo(m_FreeIndices);
            for (int i = 0; i < m_Texts.Length; i++)
                if (m_ReferenceCounts[i] != 0)
                {
                    InsertBucket(i + 1);
                    m_Count++;
                }
            PrepareOrdinals();
        }

        public void Reset()
        {
            Array.Clear(m_Texts, 0, m_Texts.Length);
            Array.Clear(m_Buckets, 0, m_Buckets.Length);
            Array.Clear(m_ReferenceCounts, 0, m_ReferenceCounts.Length);
            Array.Clear(m_References, 0, m_References.Length);
            for (int i = 0; i < m_FreeIndices.Length; i++)
                m_FreeIndices[i] = m_FreeIndices.Length - i - 1;
            m_Count = 0;
            m_FreeCount = m_Texts.Length;
            m_Frozen.Ordinals.ContentRanks.Span.CopyTo(m_ContentRanks);
            m_Frozen.Ordinals.RosterRanks.Span.CopyTo(m_RosterRanks);
            Array.Clear(m_RuntimeRanks, 0, m_RuntimeRanks.Length);
            if (m_References.Length != 0)
                Array.Clear(m_ReferenceRanks, 0, m_ReferenceRanks.Length);
        }
    }
}
