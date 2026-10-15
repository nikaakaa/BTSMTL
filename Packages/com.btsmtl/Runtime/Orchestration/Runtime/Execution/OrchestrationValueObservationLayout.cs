using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    internal readonly struct OrchestrationValueObservationPort
    {
        internal OrchestrationValueObservationPort(int operationIndex, bool isInput, int valueIndex,
            int outputPortIndex)
        {
            OperationIndex = operationIndex;
            IsInput = isInput;
            ValueIndex = valueIndex;
            OutputPortIndex = outputPortIndex;
        }

        internal int OperationIndex { get; }
        internal bool IsInput { get; }
        internal int ValueIndex { get; }
        internal int OutputPortIndex { get; }
    }

    internal sealed class OrchestrationValueObservationLayout<TValue> where TValue : unmanaged
    {
        readonly OrchestrationValueObservationPort[][] m_Ports;

        internal OrchestrationValueObservationLayout(OrchestrationProgram<TValue> program,
            OrchestrationGraphContent<TValue> content)
        {
            m_Ports = new OrchestrationValueObservationPort[program.OperationCount][];
            var declared = new Dictionary<(int Operation, string Graph, string Node, string Port, bool Input),
                OrchestrationGraphPort>();
            foreach (ref readonly OrchestrationGraphPort port in content.Ports.Span)
                declared[(port.OperationIndex, content.Identities.Resolve(port.GraphIdentityId),
                    content.Identities.Resolve(port.NodeIdentityId), content.Identities.Resolve(port.PortIdentityId),
                    port.IsInput)] = port;
            var groups = new Dictionary<string, List<OrchestrationValueObservationPort>>(StringComparer.Ordinal);
            foreach (OrchestrationGraphSource map in content.Sources.Span)
            {
                OrchestrationGraphExecutionSource source = map.Source;
                if (source.TargetKind != OrchestrationGraphSourceTargetKind.OperationPort)
                    continue;
                OrchestrationGraphPort port = declared[(source.TargetIndex, source.GraphId, source.NodeId,
                    source.PortId, source.IsInputPort)];
                if (port.IsTrigger || !port.IsInput && program.Operation(port.OperationIndex).OutputSlotOffset >= 0)
                    continue;
                if (!groups.TryGetValue(source.SourceInvocationPath, out List<OrchestrationValueObservationPort> ports))
                {
                    ports = new List<OrchestrationValueObservationPort>();
                    groups.Add(source.SourceInvocationPath, ports);
                }
                ports.Add(new OrchestrationValueObservationPort(port.OperationIndex, port.IsInput, port.ValueIndex,
                    port.IsInput ? -1 : port.ValueIndex - program.ValueEvaluationSlot(port.OperationIndex, -1) - 1));
            }
            var prepared = new Dictionary<string, OrchestrationValueObservationPort[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<OrchestrationValueObservationPort>> group in groups)
                prepared.Add(group.Key, group.Value.ToArray());
            foreach (OrchestrationGraphSource map in content.Sources.Span)
                if (map.Source.TargetKind == OrchestrationGraphSourceTargetKind.Operation &&
                    prepared.TryGetValue(map.Source.GraphInvocationPath, out OrchestrationValueObservationPort[] ports))
                    m_Ports[map.Source.TargetIndex] = ports;
        }

        internal ReadOnlySpan<OrchestrationValueObservationPort> Ports(int operationIndex) => m_Ports[operationIndex];
    }
}
