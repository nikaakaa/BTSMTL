using System;

namespace BTSMTL.Authoring.Graph
{
    public readonly struct GraphAuthoringDynamicPortProjection
    {
        public GraphAuthoringDynamicPortProjection(
            GraphAuthoringPortId portId,
            string displayName,
            string valueTypeId,
            GraphAuthoringPortDirection direction,
            GraphAuthoringPortCapacity capacity,
            bool required,
            int order,
            string interfacePortId = "")
        {
            PortId = portId.IsValid ? portId : throw new ArgumentException("Dynamic port identity is missing.", nameof(portId));
            if (order < 0)
                throw new ArgumentOutOfRangeException(nameof(order));
            DisplayName = displayName ?? string.Empty;
            ValueTypeId = GraphAuthoringIdentity.Require(valueTypeId, nameof(valueTypeId));
            Direction = direction;
            Capacity = capacity;
            Required = required;
            Order = order;
            InterfacePortId = interfacePortId ?? string.Empty;
        }

        public GraphAuthoringPortId PortId { get; }
        public string DisplayName { get; }
        public string ValueTypeId { get; }
        public GraphAuthoringPortDirection Direction { get; }
        public GraphAuthoringPortCapacity Capacity { get; }
        public bool Required { get; }
        public int Order { get; }
        public string InterfacePortId { get; }
    }

    public sealed class GraphAuthoringPortDescriptor
    {
        public GraphAuthoringPortDescriptor(
            GraphAuthoringPortId portId,
            string displayName,
            string valueTypeId,
            GraphAuthoringPortDirection direction,
            GraphAuthoringPortCapacity capacity,
            bool required,
            int order,
            string interfacePortId = "")
        {
            PortId = portId.IsValid ? portId : throw new ArgumentException("Port identity is missing.", nameof(portId));
            DisplayName = displayName ?? string.Empty;
            ValueTypeId = GraphAuthoringIdentity.Require(valueTypeId, nameof(valueTypeId));
            Direction = direction;
            Capacity = capacity;
            Required = required;
            if (order < 0)
                throw new ArgumentOutOfRangeException(nameof(order));
            Order = order;
            InterfacePortId = interfacePortId ?? string.Empty;
        }

        public GraphAuthoringPortId PortId { get; }
        public string DisplayName { get; }
        public string ValueTypeId { get; }
        public GraphAuthoringPortDirection Direction { get; }
        public GraphAuthoringPortCapacity Capacity { get; }
        public bool Required { get; }
        public int Order { get; }
        public string InterfacePortId { get; }
    }
}
