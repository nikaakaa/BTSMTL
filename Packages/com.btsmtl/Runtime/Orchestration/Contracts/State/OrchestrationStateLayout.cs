using System;
using ThirdPersonSimulation;

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

        public int SizeBytes { get; }
        public int Alignment { get; }
        public StableHash Identity { get; }

        public OrchestrationStateView BindCandidate(Span<byte> memory) =>
            new OrchestrationStateView(memory.Slice(0, SizeBytes));

        public ReadOnlyOrchestrationStateView BindCommitted(ReadOnlySpan<byte> memory) =>
            new ReadOnlyOrchestrationStateView(memory.Slice(0, SizeBytes));

        public void RequireCompatible(in OrchestrationStateLayout source)
        {
            if (!Identity.Equals(source.Identity))
                throw new ArgumentException("BTSMTL 编排区段布局不匹配，不能采用该状态副本。", nameof(source));
        }
    }
}
