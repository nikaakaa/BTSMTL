using System;
using ThirdPersonSimulation.Fixed;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct TimelineRuntimeSampleView<T>
    {
        readonly TimelineRuntimeSampleBuffer<T> m_Buffer;
        readonly ulong m_Version;

        internal TimelineRuntimeSampleView(TimelineRuntimeSampleBuffer<T> buffer)
        {
            m_Buffer = buffer;
            m_Version = buffer.Version;
        }

        internal bool IsCurrent => m_Buffer != null && m_Buffer.Version == m_Version;

        public int Count
        {
            get
            {
                if (m_Buffer == null)
                    return 0;
                RequireCurrent();
                return m_Buffer.Count;
            }
        }

        public T this[int index]
        {
            get
            {
                RequireCurrent();
                return m_Buffer[index];
            }
        }

        void RequireCurrent()
        {
            if (m_Buffer == null || m_Buffer.Version != m_Version)
                throw new InvalidOperationException("Timeline sample view no longer belongs to a live frame.");
        }
    }

    sealed class TimelineRuntimeSampleBuffer<T> : ICollection<T>, IReadOnlyList<T>
    {
        readonly List<T> m_Values;

        public TimelineRuntimeSampleBuffer(int capacity)
        {
            m_Values = new List<T>(capacity);
        }

        public int Count => m_Values.Count;
        public int Capacity => m_Values.Capacity;
        public ulong Version { get; private set; }
        public bool IsReadOnly => false;
        public T this[int index] => m_Values[index];
        public TimelineRuntimeSampleView<T> View => new(this);

        public void Add(T value)
        {
            if (Count == m_Values.Capacity)
                throw new InvalidOperationException("Timeline sample count exceeds the prepared content capacity.");
            m_Values.Add(value);
        }

        public void Clear()
        {
            m_Values.Clear();
            Version = checked(Version + 1);
        }

        public void Insert(int index, T value)
        {
            if (Count == m_Values.Capacity)
                throw new InvalidOperationException("Timeline sample count exceeds the prepared content capacity.");
            m_Values.Insert(index, value);
        }

        public void RemoveAt(int index) => m_Values.RemoveAt(index);

        public void Sort(Comparison<T> comparison) => m_Values.Sort(comparison);

        public bool Contains(T value) => m_Values.Contains(value);
        public void CopyTo(T[] array, int arrayIndex) => m_Values.CopyTo(array, arrayIndex);
        public bool Remove(T value) => throw new NotSupportedException();
        public IEnumerator<T> GetEnumerator() => throw new NotSupportedException("Use the indexed Timeline sample view.");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
