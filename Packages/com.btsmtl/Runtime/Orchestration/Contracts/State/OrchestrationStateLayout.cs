using System;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

[assembly: InternalsVisibleTo("BTSMTL.Orchestration.Runtime")]

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationStateLayout
    {
        public OrchestrationStateLayout(int sizeBytes, int alignment, StableHash identity)
        {
            SizeBytes = sizeBytes;
            Alignment = alignment;
            Identity = identity;
        }

        public OrchestrationStateLayout(int sizeBytes, int alignment,
            ulong identity0, ulong identity1, ulong identity2, ulong identity3)
            : this(sizeBytes, alignment, StableHash.FromValueStorage(IsValid: true,
                m_A: identity0, m_B: identity1, m_C: identity2, m_D: identity3))
        {
        }

        public int SizeBytes { get; }
        public int Alignment { get; }
        public StableHash Identity { get; }

        public void ReadIdentityWords(out ulong identity0, out ulong identity1,
            out ulong identity2, out ulong identity3) =>
            Identity.ReadValueStorage(IsValid: out _, m_A: out identity0, m_B: out identity1,
                m_C: out identity2, m_D: out identity3);

        public OrchestrationStateView BindCandidate(Span<byte> memory) =>
            new OrchestrationStateView(memory);

        public ReadOnlyOrchestrationStateView BindCommitted(ReadOnlySpan<byte> memory) =>
            new ReadOnlyOrchestrationStateView(memory);

        public void RequireCompatible(in OrchestrationStateLayout source)
        {
            if (!Identity.Equals(source.Identity) || SizeBytes != source.SizeBytes ||
                Alignment != source.Alignment)
                throw new ArgumentException("BTSMTL 编排区段布局不匹配，不能采用该状态副本。", nameof(source));
        }
    }
}
