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
        public ReadOnlySpan<T> Span => m_Items.AsSpan(0, Count);
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

        public Span<T> Reserve(int count, out CanonicalArraySlice<T> items)
        {
            PrepareRead(count);
            items = new CanonicalArraySlice<T>(m_Items, count);
            return m_Items.AsSpan(0, count);
        }

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

        public void CopyFrom(ReadOnlySpan<T> source)
        {
            PrepareRead(source.Length);
            source.CopyTo(m_Items);
            Count = source.Length;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return m_Items[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [CanonicalBorrowedSequence(typeof(CanonicalBuffer<>), HoldStorage = false, RetainElementsOnRead = true)]
    public readonly struct CanonicalArraySlice<T>
    {
        readonly T[] m_Items;
        readonly int m_Count;

        public CanonicalArraySlice(T[] items) : this(items, items.Length) { }

        internal CanonicalArraySlice(T[] items, int count)
        {
            m_Items = items;
            m_Count = count;
        }

        public int Count => m_Count;
        internal T[] Array => m_Items;
        public ReadOnlySpan<T> Span => m_Items.AsSpan(0, m_Count);
        public T this[int index] => Span[index];
    }
}
