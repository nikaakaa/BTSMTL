using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationIdentityOrdinalView
    {
        private readonly ReadOnlyMemory<int> m_RosterOrdinals;

        public OrchestrationIdentityOrdinalView(ReadOnlyMemory<int> rosterOrdinals)
        {
            m_RosterOrdinals = rosterOrdinals;
        }

        public int Ordinal(in OrchestrationIdentityValue value) =>
            value.Space == OrchestrationIdentitySpace.Content ? value.Index : m_RosterOrdinals.Span[value.Index];

        public bool IsEmpty(in OrchestrationIdentityValue value) =>
            value.Space == OrchestrationIdentitySpace.Content && value.Index == 0;

        public bool Equal(in OrchestrationIdentityValue left, in OrchestrationIdentityValue right) =>
            Ordinal(in left) == Ordinal(in right);
    }
}
