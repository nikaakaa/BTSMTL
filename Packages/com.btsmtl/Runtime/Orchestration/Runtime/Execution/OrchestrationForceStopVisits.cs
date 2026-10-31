using System;

namespace BTSMTL.Orchestration
{
    // 强制停止的访问集合：位图按 (帧, 操作) 记访问，链表记被动过的字，最外层停止结束时只清这些字。
    // 链表值存 next + 2，零表示未入链，使草稿初值全零即可。
    internal ref struct OrchestrationForceStopVisits
    {
        const int ListEnd = 1;

        readonly Span<ulong> m_Visited;
        readonly Span<int> m_Next;
        readonly int m_OperationCount;
        int m_HeadWord;

        internal OrchestrationForceStopVisits(Span<ulong> visited, Span<int> next, int operationCount)
        {
            m_Visited = visited;
            m_Next = next;
            m_OperationCount = operationCount;
            m_HeadWord = -1;
        }

        internal bool Visit(int frameSlot, int operationIndex)
        {
            int index = checked(frameSlot * m_OperationCount + operationIndex);
            int word = index >> 6;
            ulong mask = 1UL << (index & 63);
            if ((m_Visited[word] & mask) != 0)
                return false;
            if (m_Next[word] == 0)
            {
                m_Next[word] = m_HeadWord < 0 ? ListEnd : m_HeadWord + 2;
                m_HeadWord = word;
            }
            m_Visited[word] |= mask;
            return true;
        }

        internal void Clear()
        {
            while (m_HeadWord >= 0)
            {
                int word = m_HeadWord;
                int next = m_Next[word];
                m_HeadWord = next == ListEnd ? -1 : next - 2;
                m_Next[word] = 0;
                m_Visited[word] = 0;
            }
        }

        // 帧复用只清访问位，字的链表归属保留到最外层停止结束，避免同一字重复入链。
        internal void ClearFrame(int frameSlot)
        {
            int index = checked(frameSlot * m_OperationCount);
            int remaining = m_OperationCount;
            int word = index >> 6;
            int offset = index & 63;
            while (remaining > 0)
            {
                int count = Math.Min(64 - offset, remaining);
                ulong mask = (ulong.MaxValue >> (64 - count)) << offset;
                m_Visited[word++] &= ~mask;
                remaining -= count;
                offset = 0;
            }
        }
    }
}
