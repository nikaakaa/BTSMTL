using System;
using System.Collections;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed class GameplayTagSnapshot : IReadOnlyList<string>
    {
        readonly GameplayTagSnapshotBank m_Owner;
        readonly int m_Index;
        readonly string[] m_Values;
        int m_Retainers;
        int m_Count;

        internal static readonly GameplayTagSnapshot Empty = new GameplayTagSnapshot(null, -1, 0);

        internal GameplayTagSnapshot(GameplayTagSnapshotBank owner, int index, int width)
        {
            m_Owner = owner;
            m_Index = index;
            m_Values = new string[width];
        }

        public int Count => m_Count;
        public string this[int index] => m_Values[index];
        internal ReadOnlySpan<string> Values => m_Values.AsSpan(0, m_Count);

        internal void Begin(int count) => m_Count = count;
        internal void Write(int index, string value) => m_Values[index] = value;
        internal void Sort()
        {
            for (int root = m_Count / 2 - 1; root >= 0; root--)
                SiftDown(root, m_Count);
            for (int end = m_Count - 1; end > 0; end--)
            {
                (m_Values[0], m_Values[end]) = (m_Values[end], m_Values[0]);
                SiftDown(0, end);
            }
        }

        void SiftDown(int root, int count)
        {
            string value = m_Values[root];
            for (int child = 2 * root + 1; child < count; child = 2 * root + 1)
            {
                if (child + 1 < count && string.CompareOrdinal(m_Values[child], m_Values[child + 1]) < 0)
                    child++;
                if (string.CompareOrdinal(value, m_Values[child]) >= 0)
                    break;
                m_Values[root] = m_Values[child];
                root = child;
            }
            m_Values[root] = value;
        }

        internal void Retain()
        {
            if (m_Owner != null)
                m_Retainers++;
        }

        internal void Release()
        {
            if (m_Owner != null && --m_Retainers == 0)
            {
                Array.Clear(m_Values, 0, m_Count);
                m_Count = 0;
                m_Owner.Return(m_Index);
            }
        }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<string> IEnumerable<string>.GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        internal struct Enumerator : IEnumerator<string>
        {
            readonly GameplayTagSnapshot m_Snapshot;
            int m_Position;
            internal Enumerator(GameplayTagSnapshot snapshot)
            {
                m_Snapshot = snapshot;
                m_Position = -1;
            }
            public string Current => m_Snapshot[m_Position];
            object IEnumerator.Current => Current;
            public bool MoveNext() => ++m_Position < m_Snapshot.Count;
            public void Reset() => m_Position = -1;
            public void Dispose() { }
        }
    }

    internal sealed class GameplayTagSnapshotBank
    {
        readonly GameplayTagSnapshot[] m_Pages;
        readonly int[] m_Available;
        int m_AvailableCount;

        internal GameplayTagSnapshotBank(int width, int count)
        {
            m_Pages = new GameplayTagSnapshot[count];
            m_Available = new int[count];
            for (int i = 0; i < count; i++)
            {
                m_Pages[i] = new GameplayTagSnapshot(this, i, width);
                m_Available[i] = i;
            }
            m_AvailableCount = count;
        }

        internal int AvailableCount => m_AvailableCount;
        internal int PageCount => m_Pages.Length;

        internal GameplayTagSnapshot Begin(int length)
        {
            if (length == 0)
                return GameplayTagSnapshot.Empty;
            GameplayTagSnapshot page = m_Pages[m_Available[--m_AvailableCount]];
            page.Begin(length);
            return page;
        }

        internal GameplayTagSnapshot Capture(IReadOnlyList<string> source)
        {
            if (source is GameplayTagSnapshot snapshot)
                return snapshot;
            GameplayTagSnapshot page = Begin(source.Count);
            for (int i = 0; i < source.Count; i++)
                page.Write(i, source[i]);
            return page;
        }

        internal void Return(int index) => m_Available[m_AvailableCount++] = index;
    }
}
