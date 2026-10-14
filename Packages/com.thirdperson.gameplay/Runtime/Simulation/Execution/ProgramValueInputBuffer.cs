using System;

namespace ThirdPersonSimulation
{
    internal sealed class ProgramValueInputBuffer<TValue> where TValue : struct
    {
        readonly TValue[] m_Values;

        public ProgramValueInputBuffer(int capacity)
        {
            m_Values = new TValue[capacity];
        }

        public int Count { get; private set; }
        public ReadOnlySpan<TValue> Values => new ReadOnlySpan<TValue>(m_Values, 0, Count);
        public ref readonly TValue this[int index] => ref m_Values[index];

        public void Add(in TValue value) => m_Values[Count++] = value;

        public void Clear()
        {
            Array.Clear(m_Values, 0, Count);
            Count = 0;
        }
    }
}
