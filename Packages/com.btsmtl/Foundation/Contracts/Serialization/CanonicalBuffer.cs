using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace ThirdPersonSimulation
{
    public sealed class CanonicalBuffer<T> : IReadOnlyList<T>
    {
        readonly T[] m_Items;

        public CanonicalBuffer(int capacity) => m_Items = new T[capacity];
        public CanonicalBuffer(T[] items) { m_Items = items; Count = items.Length; }

        public int Count { get; private set; }
        public int Capacity => m_Items.Length;
        public T[] OwnedArray => m_Items;
        public T this[int index] { get => m_Items[index]; set => m_Items[index] = value; }
        public ref T Element(int index) => ref m_Items[index];

        public void PrepareRead(int count)
        {
            if (count < 0 || count > Capacity)
                throw new InvalidDataException("Canonical collection exceeds its prepared capacity.");
            Count = 0;
        }

        public void CompleteReadElement() => Count++;

        public void Add(T value)
        {
            if (Count == Capacity)
                throw new InvalidOperationException("Canonical collection exceeds its prepared capacity.");
            m_Items[Count++] = value;
        }

        public void Clear()
        {
            Array.Clear(m_Items, 0, Count);
            Count = 0;
        }

        public void Sort(IComparer<T> comparison)
        {
            for (int root = Count / 2 - 1; root >= 0; root--)
                Sift(root, Count, comparison);
            for (int end = Count - 1; end > 0; end--)
            {
                Swap(0, end);
                Sift(0, end, comparison);
            }
        }

        void Sift(int root, int count, IComparer<T> comparison)
        {
            while (root * 2 + 1 < count)
            {
                int child = root * 2 + 1;
                if (child + 1 < count && comparison.Compare(m_Items[child], m_Items[child + 1]) < 0)
                    child++;
                if (comparison.Compare(m_Items[root], m_Items[child]) >= 0)
                    return;
                Swap(root, child);
                root = child;
            }
        }

        void Swap(int left, int right)
        {
            T value = m_Items[left];
            m_Items[left] = m_Items[right];
            m_Items[right] = value;
        }

        public void CopyFrom(IReadOnlyList<T> source)
        {
            PrepareRead(source.Count);
            for (int i = 0; i < source.Count; i++)
                m_Items[i] = source[i];
            Count = source.Count;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return m_Items[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
