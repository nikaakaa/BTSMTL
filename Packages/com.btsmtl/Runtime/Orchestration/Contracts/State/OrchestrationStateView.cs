using System;
using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationStateView
    {
        readonly Span<byte> m_Memory;

        internal OrchestrationStateView(Span<byte> memory) => m_Memory = memory;

        public Span<T> Get<T>(OrchestrationStateRegion<T> region) where T : unmanaged =>
            MemoryMarshal.Cast<byte, T>(m_Memory.Slice(region.OffsetBytes, region.SizeBytes));
    }
}
