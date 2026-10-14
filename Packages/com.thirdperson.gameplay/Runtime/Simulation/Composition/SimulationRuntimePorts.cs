using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public interface ISimulationRuntimePort
    {
        SimulationPortDescriptor Descriptor { get; }
    }

    public sealed class SimulationRuntimePortSet
    {
        readonly ReadOnlyCollection<ISimulationRuntimePort> m_Ports;

        public SimulationRuntimePortSet(IEnumerable<ISimulationRuntimePort> ports)
        {
            var values = ports == null
                ? new List<ISimulationRuntimePort>()
                : new List<ISimulationRuntimePort>(ports);
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null)
                    throw new ArgumentException("Runtime port set contains a missing port.", nameof(ports));
            }
            values.Sort((left, right) => string.CompareOrdinal(left.Descriptor.PortId, right.Descriptor.PortId));
            for (int i = 0; i < values.Count; i++)
            {
                SimulationPortDescriptor descriptor = values[i].Descriptor;
                if (string.IsNullOrEmpty(descriptor.PortId) ||
                    i > 0 && string.Equals(values[i - 1].Descriptor.PortId, descriptor.PortId, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Runtime port set contains an invalid or duplicate port identity.", nameof(ports));
                }
            }
            m_Ports = values.AsReadOnly();
        }

        public IReadOnlyList<ISimulationRuntimePort> Ports => m_Ports;

        public TPort GetRequired<TPort>(SimulationPipelinePortRequirement requirement)
            where TPort : class, ISimulationRuntimePort
        {
            for (int i = 0; i < m_Ports.Count; i++)
            {
                ISimulationRuntimePort port = m_Ports[i];
                SimulationPortDescriptor descriptor = port.Descriptor;
                if (!string.Equals(descriptor.PortId, requirement.PortId, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(descriptor.SchemaId, requirement.SchemaId, StringComparison.Ordinal) ||
                    descriptor.SchemaVersion != requirement.SchemaVersion || descriptor.Direction != requirement.Direction)
                {
                    throw new InvalidOperationException($"Runtime port '{requirement.PortId}' does not match its declared schema or direction.");
                }
                if (port is not TPort typed)
                    throw new InvalidOperationException($"Runtime port '{requirement.PortId}' does not implement '{typeof(TPort).FullName}'.");
                return typed;
            }
            throw new KeyNotFoundException($"Required runtime port '{requirement.PortId}' is missing.");
        }
    }
}
