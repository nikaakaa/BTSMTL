using System;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationReadContextView
    {
        readonly ReadOnlySpan<object> m_Slots;

        public OrchestrationReadContextView(ReadOnlySpan<object> slots, int instanceIndex)
        {
            m_Slots = slots;
            InstanceIndex = instanceIndex;
        }

        public int InstanceIndex { get; }

        public ref readonly TDomain Get<TDomain>(int slot) where TDomain : struct =>
            ref ((TDomain[])m_Slots[slot])[InstanceIndex];
    }

    public readonly ref struct OrchestrationCandidateContextView
    {
        readonly ReadOnlySpan<object> m_Slots;

        public OrchestrationCandidateContextView(ReadOnlySpan<object> slots, int instanceIndex)
        {
            m_Slots = slots;
            InstanceIndex = instanceIndex;
        }

        public int InstanceIndex { get; }

        public ref TDomain Get<TDomain>(int slot) where TDomain : struct =>
            ref ((TDomain[])m_Slots[slot])[InstanceIndex];
    }
}
