using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BTSMTL.Authoring.Graph
{
    public enum GraphAuthoringDynamicPortPolicy : byte
    {
        None = 0,
        OrderedInputs = 1,
        OrderedOutputs = 2,
        OrderedBidirectional = 3
    }

    public enum GraphAuthoringDynamicPortSource : byte
    {
        None = 0,
        OrderedSteps = 1,
        MacroParameters = 2,
        BlackboardValue = 3,
        MacroInputs = 4,
        MacroOutputs = 5
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

    public sealed class GraphAuthoringPortVariantCondition
    {
        public GraphAuthoringPortVariantCondition(
            GraphAuthoringFieldId fieldId,
            GraphAuthoringFieldValueKind valueKind,
            string equals)
        {
            FieldId = fieldId.IsValid
                ? fieldId
                : throw new ArgumentException("Port variant discriminator field identity is missing.", nameof(fieldId));
            ValueKind = valueKind;
            ExpectedValue = GraphAuthoringIdentity.Require(equals, nameof(equals));
        }

        public GraphAuthoringFieldId FieldId { get; }
        public GraphAuthoringFieldValueKind ValueKind { get; }
        public string ExpectedValue { get; }

        public bool Matches(GraphAuthoringTypedPropertyValue value) =>
            value.FieldId.Equals(FieldId) &&
            value.ValueKind == ValueKind &&
            string.Equals(value.CanonicalValue, ExpectedValue, StringComparison.Ordinal);
    }

    public sealed class GraphAuthoringPortVariantDescriptor
    {
        readonly Dictionary<GraphAuthoringPortId, GraphAuthoringPortDescriptor> m_Ports;

        public GraphAuthoringPortVariantDescriptor(
            string variantId,
            GraphAuthoringPortVariantCondition when,
            IReadOnlyList<GraphAuthoringPortDescriptor> ports)
        {
            VariantId = GraphAuthoringIdentity.Require(variantId, nameof(variantId));
            When = when ?? throw new ArgumentNullException(nameof(when));
            m_Ports = new Dictionary<GraphAuthoringPortId, GraphAuthoringPortDescriptor>();
            foreach (GraphAuthoringPortDescriptor port in ports ?? Array.Empty<GraphAuthoringPortDescriptor>())
            {
                if (port == null)
                    throw new ArgumentException($"Port variant '{VariantId}' contains a missing port.", nameof(ports));
                if (!m_Ports.TryAdd(port.PortId, port))
                    throw new InvalidOperationException($"Port variant '{VariantId}' contains duplicate port identity '{port.PortId}'.");
            }
        }

        public string VariantId { get; }
        public GraphAuthoringPortVariantCondition When { get; }
        public IReadOnlyCollection<GraphAuthoringPortDescriptor> Ports => m_Ports.Values;
    }

    public sealed class GraphAuthoringPortShapeException : InvalidOperationException
    {
        public GraphAuthoringPortShapeException(string code, string message)
            : base(message)
        {
            Code = GraphAuthoringIdentity.Require(code, nameof(code));
        }

        public string Code { get; }
    }

    public static class GraphAuthoringNodePortShapeProjector
    {
        public static IReadOnlyList<GraphAuthoringDynamicPortProjection>
            ProjectComplete(
                GraphAuthoringCapabilityDescriptor capability,
                IReadOnlyList<GraphAuthoringTypedPropertyValue> properties,
                IReadOnlyList<GraphAuthoringDynamicPortProjection>
                    authoredDynamicPorts = null)
        {
            if (capability == null)
                throw new ArgumentNullException(nameof(capability));
            GraphAuthoringDynamicPortProjection[] fixedPorts = capability
                .FixedPorts
                .OrderBy(value => value.Order)
                .ThenBy(value => value.PortId.Value, StringComparer.Ordinal)
                .Select(value =>
                    new GraphAuthoringDynamicPortProjection(
                        value.PortId,
                        value.DisplayName,
                        value.ValueTypeId,
                        value.Direction,
                        value.Capacity,
                        value.Required,
                        value.Order,
                        value.InterfacePortId))
                .ToArray();
            return fixedPorts
                .Concat(Project(
                    capability,
                    properties,
                    authoredDynamicPorts))
                .OrderBy(value => value.Order)
                .ThenBy(value => value.PortId.Value, StringComparer.Ordinal)
                .ToArray();
        }

        public static IReadOnlyList<GraphAuthoringDynamicPortProjection> Project(
            GraphAuthoringCapabilityDescriptor capability,
            IReadOnlyList<GraphAuthoringTypedPropertyValue> properties,
            IReadOnlyList<GraphAuthoringDynamicPortProjection> authoredDynamicPorts = null)
        {
            if (capability == null)
                throw new ArgumentNullException(nameof(capability));

            GraphAuthoringPortVariantDescriptor variant = ResolveVariant(
                capability,
                properties ?? Array.Empty<GraphAuthoringTypedPropertyValue>());
            var occupied = new HashSet<GraphAuthoringPortId>(
                capability.FixedPorts.Select(value => value.PortId));
            var interfacePorts = new HashSet<string>(
                capability.FixedPorts
                    .Select(value => value.InterfacePortId)
                    .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
            var result = new List<GraphAuthoringDynamicPortProjection>();
            if (variant != null)
            {
                foreach (GraphAuthoringPortDescriptor port in variant.Ports
                             .OrderBy(value => value.Order)
                             .ThenBy(value => value.PortId.Value, StringComparer.Ordinal))
                {
                    AddProjectedPort(
                        result,
                        occupied,
                        interfacePorts,
                        new GraphAuthoringDynamicPortProjection(
                            port.PortId,
                            port.DisplayName,
                            port.ValueTypeId,
                            port.Direction,
                            port.Capacity,
                            port.Required,
                            port.Order,
                            port.InterfacePortId),
                        capability.CapabilityId);
                }
            }
            foreach (GraphAuthoringDynamicPortProjection port in
                     authoredDynamicPorts ?? Array.Empty<GraphAuthoringDynamicPortProjection>())
            {
                ValidateDynamicPort(capability, port);
                AddProjectedPort(
                    result,
                    occupied,
                    interfacePorts,
                    port,
                    capability.CapabilityId);
            }
            return result
                .OrderBy(value => value.Order)
                .ThenBy(value => value.PortId.Value, StringComparer.Ordinal)
                .ToArray();
        }

        static GraphAuthoringPortVariantDescriptor ResolveVariant(
            GraphAuthoringCapabilityDescriptor capability,
            IReadOnlyList<GraphAuthoringTypedPropertyValue> properties)
        {
            if (capability.PortVariants.Count == 0)
                return null;

            GraphAuthoringPortVariantCondition discriminator = capability.PortVariants[0].When;
            GraphAuthoringTypedPropertyValue[] values = properties
                .Where(value => value.FieldId.Equals(discriminator.FieldId))
                .ToArray();
            if (values.Length != 1 || values[0].ValueKind != discriminator.ValueKind)
            {
                throw new GraphAuthoringPortShapeException(
                    "port_shape_discriminator_unknown",
                    $"Capability '{capability.CapabilityId}' requires one '{discriminator.ValueKind}' discriminator '{discriminator.FieldId}'.");
            }

            GraphAuthoringPortVariantDescriptor[] matches = capability.PortVariants
                .Where(value => value.When.Matches(values[0]))
                .ToArray();
            if (matches.Length == 0)
            {
                throw new GraphAuthoringPortShapeException(
                    "port_shape_variant_missing",
                    $"Capability '{capability.CapabilityId}' has no port variant for '{discriminator.FieldId}={values[0].CanonicalValue}'.");
            }
            if (matches.Length > 1)
            {
                throw new GraphAuthoringPortShapeException(
                    "port_shape_variant_ambiguous",
                    $"Capability '{capability.CapabilityId}' has multiple port variants for '{discriminator.FieldId}={values[0].CanonicalValue}'.");
            }
            return matches[0];
        }

        static void AddProjectedPort(
            ICollection<GraphAuthoringDynamicPortProjection> result,
            ISet<GraphAuthoringPortId> occupied,
            ISet<string> interfacePorts,
            GraphAuthoringDynamicPortProjection port,
            GraphAuthoringCapabilityId capabilityId)
        {
            if (!occupied.Add(port.PortId))
            {
                throw new GraphAuthoringPortShapeException(
                    "port_shape_identity_duplicate",
                    $"Capability '{capabilityId}' projects duplicate port identity '{port.PortId}'.");
            }
            if (!string.IsNullOrWhiteSpace(port.InterfacePortId) &&
                !interfacePorts.Add(port.InterfacePortId))
            {
                throw new GraphAuthoringPortShapeException(
                    "port_shape_interface_identity_duplicate",
                    $"Capability '{capabilityId}' projects duplicate interface port identity '{port.InterfacePortId}'.");
            }
            result.Add(port);
        }

        static void ValidateDynamicPort(
            GraphAuthoringCapabilityDescriptor capability,
            GraphAuthoringDynamicPortProjection port)
        {
            if (capability.DynamicPortPolicy ==
                GraphAuthoringDynamicPortPolicy.None ||
                capability.DynamicPortPolicy ==
                GraphAuthoringDynamicPortPolicy.OrderedInputs &&
                port.Direction != GraphAuthoringPortDirection.Input ||
                capability.DynamicPortPolicy ==
                GraphAuthoringDynamicPortPolicy.OrderedOutputs &&
                port.Direction != GraphAuthoringPortDirection.Output)
            {
                throw new GraphAuthoringPortShapeException(
                    "port_shape_dynamic_port_forbidden",
                    $"Capability '{capability.CapabilityId}' does not allow dynamic port '{port.PortId}' with direction '{port.Direction}'.");
            }
        }
    }
}
