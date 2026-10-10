using System;

namespace ThirdPersonSimulation
{
    public sealed class GameplayAttributeDependencyWorkspace
    {
        readonly GameplayAttributeDependencyLayout m_Layout;
        readonly int[] m_SourceCounts;
        readonly int[] m_Heads;
        readonly int[] m_Next;
        readonly int[] m_Previous;
        readonly bool[] m_Dirty;
        readonly int[] m_Affected;
        readonly int[] m_DeviationPositions;
        readonly int[] m_Deviations;
        readonly Comparison<int> m_RankComparison;
        readonly Comparison<int> m_IndexComparison;
        int m_AffectedCount;
        int m_DeviationCount;

        public GameplayAttributeDependencyWorkspace(GameplayAttributeDependencyLayout layout)
        {
            m_Layout = layout;
            m_SourceCounts = new int[layout.EdgeCount];
            m_Heads = new int[layout.AttributeCount];
            m_Next = new int[layout.EdgeCount];
            m_Previous = new int[layout.EdgeCount];
            m_Dirty = new bool[layout.AttributeCount];
            m_Affected = new int[layout.AttributeCount];
            m_DeviationPositions = new int[layout.AttributeCount];
            m_Deviations = new int[layout.AttributeCount];
            Array.Fill(m_DeviationPositions, -1);
            m_RankComparison = CompareRanks;
            m_IndexComparison = CompareIndices;
            ResetSources();
        }

        public ReadOnlySpan<int> Affected => m_Affected.AsSpan(0, m_AffectedCount);
        public bool HasDeviations => m_DeviationCount != 0;

        public void ResetSources()
        {
            ClearMutation();
            Array.Clear(m_SourceCounts, 0, m_SourceCounts.Length);
            Array.Fill(m_Heads, -1);
            for (int i = 0; i < m_Layout.EdgeCount; i++)
            {
                int count = m_Layout.StaticSourceCount(i);
                if (count == 0)
                    continue;
                m_SourceCounts[i] = count;
                Insert(i);
            }
        }

        public void AddSource(int input, int output)
        {
            int edge = m_Layout.EdgeIndex(input, output);
            if (m_SourceCounts[edge]++ == 0)
                Insert(edge);
        }

        public void RemoveSource(int input, int output)
        {
            int edge = m_Layout.EdgeIndex(input, output);
            if (--m_SourceCounts[edge] != 0)
                return;
            int previous = m_Previous[edge];
            int next = m_Next[edge];
            if (previous < 0)
                m_Heads[input] = next;
            else
                m_Next[previous] = next;
            if (next >= 0)
                m_Previous[next] = previous;
        }

        void Insert(int edge)
        {
            int input = m_Layout.EdgeInput(edge);
            int next = m_Heads[input];
            m_Previous[edge] = -1;
            m_Next[edge] = next;
            if (next >= 0)
                m_Previous[next] = edge;
            m_Heads[input] = edge;
        }

        public void CollectAffected(int root)
        {
            ClearMutation();
            m_Dirty[root] = true;
            m_Affected[m_AffectedCount++] = root;
            for (int position = 0; position < m_AffectedCount; position++)
            {
                int input = m_Affected[position];
                for (int edge = m_Heads[input]; edge >= 0; edge = m_Next[edge])
                {
                    int output = m_Layout.EdgeTarget(edge);
                    if (m_Dirty[output])
                        continue;
                    m_Dirty[output] = true;
                    m_Affected[m_AffectedCount++] = output;
                }
            }
        }

        public void SortForEvaluation() => SimulationArraySort.SortPrefix(m_Affected, m_AffectedCount, m_RankComparison);
        public void SortForOutput() => SimulationArraySort.SortPrefix(m_Affected, m_AffectedCount, m_IndexComparison);

        int CompareRanks(int left, int right) => m_Layout.Rank(left).CompareTo(m_Layout.Rank(right));
        static int CompareIndices(int left, int right) => left.CompareTo(right);

        public void ClearMutation()
        {
            for (int i = 0; i < m_AffectedCount; i++)
                m_Dirty[m_Affected[i]] = false;
            m_AffectedCount = 0;
        }

        public void SetDeviation(int index, bool differs)
        {
            int position = m_DeviationPositions[index];
            if (differs)
            {
                if (position >= 0)
                    return;
                m_DeviationPositions[index] = m_DeviationCount;
                m_Deviations[m_DeviationCount++] = index;
            }
            else if (position >= 0)
            {
                int moved = m_Deviations[--m_DeviationCount];
                m_Deviations[position] = moved;
                m_DeviationPositions[moved] = position;
                m_DeviationPositions[index] = -1;
            }
        }

        public void AcceptBaseline()
        {
            for (int i = 0; i < m_DeviationCount; i++)
                m_DeviationPositions[m_Deviations[i]] = -1;
            m_DeviationCount = 0;
            ClearMutation();
        }
    }
}
