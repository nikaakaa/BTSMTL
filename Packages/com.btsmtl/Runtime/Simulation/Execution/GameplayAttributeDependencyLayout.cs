using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation
{
    public sealed class GameplayAttributeDependencyLayout
    {
        readonly string[] m_AttributeIds;
        readonly Dictionary<string, int> m_AttributeIndices;
        readonly Dictionary<(int, int), int> m_EdgeIndices;
        readonly int[] m_EdgeInputs;
        readonly int[] m_EdgeTargets;
        readonly int[] m_StaticSourceCounts;
        readonly string[][] m_EdgeSources;
        readonly int[][] m_OutgoingEdges;
        readonly int[][] m_IncomingEdges;
        readonly int[] m_TopologicalOrder;
        readonly int[] m_Ranks;

        internal GameplayAttributeDependencyLayout(string[] attributeIds, Dictionary<string, int> attributeIndices,
            Dictionary<(int, int), int> edgeIndices, int[] edgeInputs, int[] edgeTargets,
            int[] staticSourceCounts, string[][] edgeSources, int[][] outgoingEdges, int[][] incomingEdges,
            int[] topologicalOrder, int[] ranks)
        {
            m_AttributeIds = attributeIds;
            m_AttributeIndices = attributeIndices;
            m_EdgeIndices = edgeIndices;
            m_EdgeInputs = edgeInputs;
            m_EdgeTargets = edgeTargets;
            m_StaticSourceCounts = staticSourceCounts;
            m_EdgeSources = edgeSources;
            m_OutgoingEdges = outgoingEdges;
            m_IncomingEdges = incomingEdges;
            m_TopologicalOrder = topologicalOrder;
            m_Ranks = ranks;
        }

        public int AttributeCount => m_AttributeIds.Length;
        public int EdgeCount => m_EdgeInputs.Length;
        public string AttributeId(int index) => m_AttributeIds[index];
        public int AttributeIndex(string id) => m_AttributeIndices[id];
        public bool TryGetAttributeIndex(string id, out int index) => m_AttributeIndices.TryGetValue(id, out index);
        public int EdgeIndex(int input, int target) => m_EdgeIndices[(input, target)];
        public int EdgeInput(int edge) => m_EdgeInputs[edge];
        public int EdgeTarget(int edge) => m_EdgeTargets[edge];
        public int StaticSourceCount(int edge) => m_StaticSourceCounts[edge];
        public ReadOnlySpan<string> EdgeSources(int edge) => m_EdgeSources[edge];
        public ReadOnlySpan<int> OutgoingEdges(int attribute) => m_OutgoingEdges[attribute];
        public ReadOnlySpan<int> IncomingEdges(int attribute) => m_IncomingEdges[attribute];
        public int TopologicalAttribute(int rank) => m_TopologicalOrder[rank];
        public int Rank(int attribute) => m_Ranks[attribute];
    }

    public sealed class GameplayAttributeDependencyBuilder
    {
        readonly List<string> m_Attributes = new List<string>();
        readonly List<(string Input, string Target, string Source, bool Static)> m_Dependencies =
            new List<(string, string, string, bool)>();

        public void AddAttribute(string id) => m_Attributes.Add(id);
        public void AddDependency(string input, string target, string source, bool isStatic) =>
            m_Dependencies.Add((input, target, source, isStatic));

        public GameplayAttributeDependencyLayout Build()
        {
            var errors = new List<string>();
            if (!TryBuild(out GameplayAttributeDependencyLayout layout, errors))
                throw new InvalidDataException(string.Join(" ", errors));
            return layout;
        }

        public bool TryBuild(out GameplayAttributeDependencyLayout layout, List<string> errors)
        {
            layout = null;
            string[] ids = m_Attributes.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var indices = new Dictionary<string, int>(ids.Length, StringComparer.Ordinal);
            for (int index = 0; index < ids.Length; index++)
                indices.Add(ids[index], index);
            var sources = new SortedDictionary<(int Input, int Target), List<(string Source, bool Static)>>();
            for (int index = 0; index < m_Dependencies.Count; index++)
            {
                var dependency = m_Dependencies[index];
                if (!indices.TryGetValue(dependency.Input, out int input))
                {
                    errors.Add($"{dependency.Source}: missing Gameplay Attribute '{dependency.Input}'.");
                    return false;
                }
                if (!indices.TryGetValue(dependency.Target, out int target))
                {
                    errors.Add($"{dependency.Source}: missing Gameplay Attribute '{dependency.Target}'.");
                    return false;
                }
                var key = (input, target);
                if (!sources.TryGetValue(key, out var values))
                    sources.Add(key, values = new List<(string, bool)>());
                values.Add((dependency.Source, dependency.Static));
            }
            var edgeIndices = new Dictionary<(int, int), int>(sources.Count);
            var inputs = new int[sources.Count];
            var targets = new int[sources.Count];
            var staticCounts = new int[sources.Count];
            var edgeSources = new string[sources.Count][];
            var outgoing = new List<int>[ids.Length];
            var incoming = new List<int>[ids.Length];
            for (int index = 0; index < ids.Length; index++)
            {
                outgoing[index] = new List<int>();
                incoming[index] = new List<int>();
            }
            int edge = 0;
            foreach (var pair in sources)
            {
                inputs[edge] = pair.Key.Input;
                targets[edge] = pair.Key.Target;
                edgeIndices.Add(pair.Key, edge);
                edgeSources[edge] = pair.Value.Select(value => value.Source).ToArray();
                staticCounts[edge] = pair.Value.Count(value => value.Static);
                outgoing[pair.Key.Input].Add(edge);
                incoming[pair.Key.Target].Add(edge);
                edge++;
            }
            var indegrees = incoming.Select(value => value.Count).ToArray();
            var ready = new SortedSet<int>();
            for (int index = 0; index < ids.Length; index++)
                if (indegrees[index] == 0)
                    ready.Add(index);
            var order = new int[ids.Length];
            var ranks = new int[ids.Length];
            int count = 0;
            while (ready.Count > 0)
            {
                int index = ready.Min;
                ready.Remove(index);
                ranks[index] = count;
                order[count++] = index;
                foreach (int currentEdge in outgoing[index])
                    if (--indegrees[targets[currentEdge]] == 0)
                        ready.Add(targets[currentEdge]);
            }
            if (count != ids.Length)
            {
                errors.Add(DescribeCycle(ids, outgoing, inputs, targets, edgeSources));
                return false;
            }
            layout = new GameplayAttributeDependencyLayout(ids, indices, edgeIndices, inputs, targets,
                staticCounts, edgeSources, outgoing.Select(value => value.ToArray()).ToArray(),
                incoming.Select(value => value.ToArray()).ToArray(), order, ranks);
            return true;
        }

        static string DescribeCycle(string[] ids, List<int>[] outgoing, int[] inputs, int[] targets, string[][] sources)
        {
            var visited = new byte[ids.Length];
            var path = new List<int>();
            string cycle = string.Empty;
            for (int index = 0; index < ids.Length; index++)
                if (visited[index] == 0 && Visit(index))
                    break;
            return "Gameplay Attribute dependency cycle: " + cycle;

            bool Visit(int index)
            {
                visited[index] = 1;
                foreach (int edge in outgoing[index])
                {
                    int target = targets[edge];
                    path.Add(edge);
                    if (visited[target] == 1)
                    {
                        int start = path.FindIndex(value => inputs[value] == target);
                        cycle = string.Join(" -> ", path.Skip(start).Select(value =>
                            $"{ids[inputs[value]]} -> {ids[targets[value]]} [{string.Join(", ", sources[value])}]"));
                        return true;
                    }
                    if (visited[target] == 0 && Visit(target))
                        return true;
                    path.RemoveAt(path.Count - 1);
                }
                visited[index] = 2;
                return false;
            }
        }
    }
}
