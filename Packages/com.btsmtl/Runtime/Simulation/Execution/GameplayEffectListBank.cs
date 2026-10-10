using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal sealed class GameplayEffectListBank<T>
    {
        readonly List<T>[] m_Lists;
        readonly int[] m_Available;
        readonly Dictionary<List<T>, int> m_Indices;
        int m_AvailableCount;

        internal GameplayEffectListBank(int count, int width)
        {
            m_Lists = new List<T>[count];
            m_Available = new int[count];
            m_Indices = new Dictionary<List<T>, int>(count);
            for (int i = 0; i < count; i++)
            {
                var list = new List<T>(width);
                m_Lists[i] = list;
                m_Indices.Add(list, i);
                m_Available[i] = i;
            }
            m_AvailableCount = count;
        }

        internal int AvailableCount => m_AvailableCount;

        internal List<T> Acquire() => m_Lists[m_Available[--m_AvailableCount]];

        internal void Release(List<T> list)
        {
            list.Clear();
            m_Available[m_AvailableCount++] = m_Indices[list];
        }
    }
}
