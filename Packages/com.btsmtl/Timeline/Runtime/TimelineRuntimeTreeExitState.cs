using System;
using BTSMTL.Orchestration;

namespace BTSMTL.Timeline.Runtime
{
    public readonly ref struct TimelineTreeExitView
    {
        readonly ReadOnlySpan<OrchestrationTimelineTreeCycleWindow> m_Windows;
        readonly ReadOnlySpan<long> m_FirstCycles;
        readonly ReadOnlySpan<ulong> m_Bits;

        public TimelineTreeExitView(ReadOnlySpan<OrchestrationTimelineTreeCycleWindow> windows,
            ReadOnlySpan<long> firstCycles, ReadOnlySpan<ulong> bits)
        {
            m_Windows = windows;
            m_FirstCycles = firstCycles;
            m_Bits = bits;
        }

        public long FirstCycle(int ordinal) => m_FirstCycles[m_Windows[ordinal].StateIndex];

        public bool Exited(int ordinal, long cycle)
        {
            ref readonly OrchestrationTimelineTreeCycleWindow window = ref m_Windows[ordinal];
            return cycle < m_FirstCycles[window.StateIndex] || ReadStatus(in window, m_Bits, cycle) == 2;
        }

        public bool Pending(int ordinal, long cycle)
        {
            ref readonly OrchestrationTimelineTreeCycleWindow window = ref m_Windows[ordinal];
            return cycle >= m_FirstCycles[window.StateIndex] && ReadStatus(in window, m_Bits, cycle) == 1;
        }

        public bool Requested(int ordinal, long cycle)
        {
            ref readonly OrchestrationTimelineTreeCycleWindow window = ref m_Windows[ordinal];
            return cycle < m_FirstCycles[window.StateIndex] || ReadStatus(in window, m_Bits, cycle) != 0;
        }

        internal static int ReadStatus(in OrchestrationTimelineTreeCycleWindow window,
            ReadOnlySpan<ulong> bits, long cycle)
        {
            int position = (int)(cycle % window.CycleCapacity);
            return (int)(bits[window.WordOffset + position / 32] >> (2 * (position % 32)) & 3UL);
        }
    }

    public readonly ref struct TimelineTreeExitState
    {
        readonly ReadOnlySpan<OrchestrationTimelineTreeCycleWindow> m_Windows;
        readonly Span<long> m_FirstCycles;
        readonly Span<ulong> m_Bits;

        public TimelineTreeExitState(ReadOnlySpan<OrchestrationTimelineTreeCycleWindow> windows,
            Span<long> firstCycles, Span<ulong> bits)
        {
            m_Windows = windows;
            m_FirstCycles = firstCycles;
            m_Bits = bits;
        }

        public TimelineTreeExitView View => new(m_Windows, m_FirstCycles, m_Bits);

        public void Clear()
        {
            m_FirstCycles.Clear();
            m_Bits.Clear();
        }

        public void SetFirstCycle(int ordinal, long firstCycle) =>
            m_FirstCycles[m_Windows[ordinal].StateIndex] = firstCycle;

        public void SetPending(int ordinal, int cycle) => SetStatus(in m_Windows[ordinal], cycle, 1);

        public void SetExited(int ordinal, int cycle)
        {
            ref readonly OrchestrationTimelineTreeCycleWindow window = ref m_Windows[ordinal];
            SetStatus(in window, cycle, 2);
            ref long first = ref m_FirstCycles[window.StateIndex];
            while (TimelineTreeExitView.ReadStatus(in window, m_Bits, first) == 2)
            {
                SetStatus(in window, first, 0);
                first++;
            }
        }

        void SetStatus(in OrchestrationTimelineTreeCycleWindow window, long cycle, int status)
        {
            int position = (int)(cycle % window.CycleCapacity);
            int shift = 2 * (position % 32);
            ref ulong word = ref m_Bits[window.WordOffset + position / 32];
            word = word & ~(3UL << shift) | (ulong)status << shift;
        }
    }
}
