using System;
using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    public readonly ref struct ReadOnlyOrchestrationStateView
    {
        readonly ReadOnlySpan<byte> m_Memory;

        internal ReadOnlyOrchestrationStateView(ReadOnlySpan<byte> memory) => m_Memory = memory;

        public ReadOnlySpan<T> Get<T>(in OrchestrationStateRegion<T> region) where T : unmanaged =>
            MemoryMarshal.Cast<byte, T>(m_Memory.Slice(region.OffsetBytes, region.SizeBytes));
    }
}
