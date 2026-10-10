using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class TimelineAnimationProducerIndex
    {
        readonly OperationExecutionTopology m_Topology;
        readonly IReadOnlyList<OperationHandle>[] m_Representatives;

        public TimelineAnimationProducerIndex(
            OperationExecutionTopology topology,
            Func<OperationHandle, bool> isTrackMuted,
            Func<OperationHandle, string> producerIdentity)
        {
            m_Topology = topology ?? throw new ArgumentNullException(nameof(topology));
            if (isTrackMuted == null)
                throw new ArgumentNullException(nameof(isTrackMuted));
            if (producerIdentity == null)
                throw new ArgumentNullException(nameof(producerIdentity));
            m_Representatives = new IReadOnlyList<OperationHandle>[topology.Operations.Count];
            for (int i = 0; i < m_Representatives.Length; i++)
                m_Representatives[i] = Array.Empty<OperationHandle>();
            for (int timelineIndex = 0; timelineIndex < topology.TimelineOperationCount; timelineIndex++)
            {
                OperationExecutionDescriptor timeline = topology.TimelineOperationAt(timelineIndex);
                m_Representatives[timeline.Handle.Value] = BuildTimeline(
                    topology,
                    timeline.Handle,
                    isTrackMuted,
                    producerIdentity);
            }
        }

        public IReadOnlyList<OperationHandle> Representatives(OperationHandle timeline)
        {
            m_Topology.RequireOperation(timeline);
            if (m_Topology.Operation(timeline).Code != SimulationOperationCode.Timeline)
                throw new ArgumentException($"Operation '{timeline}' is not a Timeline.", nameof(timeline));
            return m_Representatives[timeline.Value];
        }

        static IReadOnlyList<OperationHandle> BuildTimeline(
            OperationExecutionTopology topology,
            OperationHandle timeline,
            Func<OperationHandle, bool> isTrackMuted,
            Func<OperationHandle, string> producerIdentity)
        {
            var representatives = new SortedDictionary<string, OperationHandle>(StringComparer.Ordinal);
            IReadOnlyList<ProgramControlFlowEdge> children = topology.Outgoing(timeline, ProgramControlFlowKind.Child);
            for (int i = 0; i < children.Count; i++)
            {
                OperationHandle operation = children[i].Target;
                if (topology.Operation(operation).Code != SimulationOperationCode.TimelineAnimation ||
                    isTrackMuted(operation))
                {
                    continue;
                }
                string producer = SimulationIdentity.Require(producerIdentity(operation), nameof(producerIdentity));
                if (!representatives.ContainsKey(producer))
                    representatives.Add(producer, operation);
            }
            if (representatives.Count == 0)
                return Array.Empty<OperationHandle>();
            var result = new OperationHandle[representatives.Count];
            int index = 0;
            foreach (OperationHandle operation in representatives.Values)
                result[index++] = operation;
            return Array.AsReadOnly(result);
        }
    }
}
