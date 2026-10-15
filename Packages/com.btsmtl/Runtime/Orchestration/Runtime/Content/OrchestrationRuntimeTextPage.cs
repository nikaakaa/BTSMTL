using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationRuntimeTextPage : IDisposable
    {
        readonly FrozenOrchestrationIdentities m_Frozen;
        readonly CanonicalTextReferences m_References;
        readonly int[] m_ContentRanks;
        readonly int[] m_RosterRanks;
        readonly int[] m_RuntimeRanks;
        readonly int[] m_SortedRuntimeIndices;

        public OrchestrationRuntimeTextPage(FrozenOrchestrationIdentities frozen, int referenceCapacity)
        {
            m_Frozen = frozen;
            m_References = new CanonicalTextReferences(referenceCapacity);
            m_ContentRanks = new int[frozen.Table.Count + 1];
            m_RosterRanks = new int[frozen.Ordinals.RosterRanks.Length];
            m_RuntimeRanks = new int[referenceCapacity + 1];
            m_SortedRuntimeIndices = new int[referenceCapacity];
            Ordinals = new OrchestrationIdentityOrdinalView(m_ContentRanks, m_RosterRanks, m_RuntimeRanks);
            Reset();
        }

        public int Count => m_References.Count;
        public int Capacity => m_References.Capacity;
        public OrchestrationIdentityOrdinalView Ordinals { get; }

        public OrchestrationIdentityValue Capture(in CanonicalTextValue text)
        {
            using (CanonicalTextReadLease read = text.AcquireRead())
                if (m_Frozen.Table.TryBind(text.Span, out int identity))
                    return OrchestrationIdentityValue.Content(identity);
            return OrchestrationIdentityValue.RuntimeText(m_References.CaptureTextIndex(in text));
        }

        public CanonicalTextValue Resolve(in OrchestrationIdentityValue value) => value.Space switch
        {
            OrchestrationIdentitySpace.Content => CanonicalTextValue.FromString(m_Frozen.Table.Resolve(value.Index)),
            OrchestrationIdentitySpace.Roster => CanonicalTextValue.FromString(m_Frozen.Resolve(in value)),
            OrchestrationIdentitySpace.RuntimeText => m_References.ReadText(value.Index),
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

        public void PrepareOrdinals()
        {
            int count = m_References.Count;
            for (int i = 0; i < count; i++)
            {
                int current = i + 1;
                int insertion = i;
                CanonicalTextValue text = m_References.ReadText(current);
                while (insertion > 0 && CanonicalTextValue.CompareOrdinal(
                           m_References.ReadText(m_SortedRuntimeIndices[insertion - 1]), text) > 0)
                {
                    m_SortedRuntimeIndices[insertion] = m_SortedRuntimeIndices[insertion - 1];
                    insertion--;
                }
                m_SortedRuntimeIndices[insertion] = current;
            }

            int content = 1;
            int runtime = 0;
            int rank = 1;
            while (content <= m_Frozen.Table.Count || runtime < count)
            {
                if (runtime == count || content <= m_Frozen.Table.Count && CanonicalTextValue.CompareOrdinal(
                        CanonicalTextValue.FromString(m_Frozen.Table.Resolve(content)),
                        m_References.ReadText(m_SortedRuntimeIndices[runtime])) < 0)
                    m_ContentRanks[content++] = rank++;
                else
                    m_RuntimeRanks[m_SortedRuntimeIndices[runtime++]] = rank++;
            }
            ReadOnlySpan<int> roster = m_Frozen.Ordinals.RosterRanks.Span;
            for (int i = 0; i < roster.Length; i++)
                m_RosterRanks[i] = m_ContentRanks[roster[i]];
        }

        public void Reset()
        {
            m_References.Reset();
            m_Frozen.Ordinals.ContentRanks.Span.CopyTo(m_ContentRanks);
            m_Frozen.Ordinals.RosterRanks.Span.CopyTo(m_RosterRanks);
            Array.Clear(m_RuntimeRanks, 0, m_RuntimeRanks.Length);
        }

        public void Dispose() => Reset();
    }
}
