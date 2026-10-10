using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace BTSMTL.Authoring.Graph
{
    public enum GraphAuthoringNodePresentationKind : byte
    {
        Standard = 0,
        StateMachineEntry = 1,
        State = 2,
        StateAlias = 3,
        TransitionRule = 4
    }

    public enum GraphAuthoringCommandPresentationKind : byte
    {
        Button = 1,
        Custom = 2
    }

    public sealed class GraphAuthoringCommandDescriptor
    {
        public GraphAuthoringCommandDescriptor(
            GraphAuthoringCommandId commandId,
            string displayName,
            bool destructive,
            GraphAuthoringCommandPresentationKind presentationKind =
                GraphAuthoringCommandPresentationKind.Button)
        {
            CommandId = commandId.IsValid ? commandId : throw new ArgumentException("Command identity is missing.", nameof(commandId));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("Command display name is missing.", nameof(displayName)) : displayName;
            Destructive = destructive;
            PresentationKind = presentationKind;
        }

        public GraphAuthoringCommandId CommandId { get; }
        public string DisplayName { get; }
        public bool Destructive { get; }
        public GraphAuthoringCommandPresentationKind PresentationKind
        {
            get;
        }
    }

    public sealed class GraphAuthoringChildSurfaceDescriptor
    {
        public GraphAuthoringChildSurfaceDescriptor(GraphAuthoringCommandId commandId, GraphAuthoringDocumentRoleId documentRoleId, string displayName)
        {
            CommandId = commandId.IsValid ? commandId : throw new ArgumentException("Child surface command identity is missing.", nameof(commandId));
            DocumentRoleId = documentRoleId.IsValid ? documentRoleId : throw new ArgumentException("Child surface role identity is missing.", nameof(documentRoleId));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("Child surface display name is missing.", nameof(displayName)) : displayName;
        }

        public GraphAuthoringCommandId CommandId { get; }
        public GraphAuthoringDocumentRoleId DocumentRoleId { get; }
        public string DisplayName { get; }
    }

    public sealed class GraphAuthoringCapabilityDescriptor
    {
        readonly HashSet<GraphAuthoringDocumentRoleId> m_AllowedDocumentRoles;
        readonly Dictionary<GraphAuthoringFieldId, GraphAuthoringFieldDescriptor> m_Fields;
        readonly Dictionary<GraphAuthoringPortId, GraphAuthoringPortDescriptor> m_FixedPorts;
        readonly IReadOnlyList<GraphAuthoringPortVariantDescriptor> m_PortVariants;

        public GraphAuthoringCapabilityDescriptor(
            GraphAuthoringCapabilityId capabilityId,
            GraphAuthoringDomainId domainId,
            IReadOnlyList<GraphAuthoringDocumentRoleId> allowedDocumentRoles,
            string displayName,
            string category,
            Color color,
            IReadOnlyList<GraphAuthoringFieldDescriptor> fields = null,
            IReadOnlyList<GraphAuthoringPortDescriptor> fixedPorts = null,
            GraphAuthoringDynamicPortPolicy dynamicPortPolicy = GraphAuthoringDynamicPortPolicy.None,
            IReadOnlyList<GraphAuthoringChildSurfaceDescriptor> childSurfaces = null,
            IReadOnlyList<GraphAuthoringCommandDescriptor> commands = null,
            string iconName = "",
            GraphAuthoringNodePresentationKind presentationKind = GraphAuthoringNodePresentationKind.Standard,
            string mutationBindingId = "",
            string validationBindingId = "",
            string compilerBindingId = "",
            Type authoringType = null,
            string externalKind = "",
            bool systemOwned = false,
            string anchorId = "",
            string executionDomainId = "",
            IReadOnlyList<GraphAuthoringPortVariantDescriptor> portVariants = null,
            GraphAuthoringDynamicPortSource dynamicPortSource = GraphAuthoringDynamicPortSource.None)
        {
            CapabilityId = capabilityId.IsValid ? capabilityId : throw new ArgumentException("Capability identity is missing.", nameof(capabilityId));
            DomainId = domainId.IsValid ? domainId : throw new ArgumentException("Capability domain identity is missing.", nameof(domainId));
            if (allowedDocumentRoles == null || allowedDocumentRoles.Count == 0)
                throw new ArgumentException("Capability must allow at least one document role.", nameof(allowedDocumentRoles));
            m_AllowedDocumentRoles = new HashSet<GraphAuthoringDocumentRoleId>(allowedDocumentRoles);
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("Capability display name is missing.", nameof(displayName)) : displayName;
            Category = category ?? string.Empty;
            Color = color;
            m_Fields = Index(fields ?? Array.Empty<GraphAuthoringFieldDescriptor>(), value => value.FieldId, "field");
            m_FixedPorts = Index(fixedPorts ?? Array.Empty<GraphAuthoringPortDescriptor>(), value => value.PortId, "port");
            DynamicPortPolicy = dynamicPortPolicy;
            DynamicPortSource = dynamicPortSource;
            m_PortVariants = ValidatePortVariants(
                portVariants ?? Array.Empty<GraphAuthoringPortVariantDescriptor>());
            ChildSurfaces = childSurfaces ?? Array.Empty<GraphAuthoringChildSurfaceDescriptor>();
            Commands = commands ?? Array.Empty<GraphAuthoringCommandDescriptor>();
            IconName = iconName ?? string.Empty;
            PresentationKind = presentationKind;
            MutationBindingId = mutationBindingId ?? string.Empty;
            ValidationBindingId = validationBindingId ?? string.Empty;
            CompilerBindingId = compilerBindingId ?? string.Empty;
            AuthoringType = authoringType;
            ExternalKind = externalKind ?? string.Empty;
            SystemOwned = systemOwned;
            AnchorId = anchorId ?? string.Empty;
            ExecutionDomainId = executionDomainId ?? string.Empty;
            if (SystemOwned &&
                string.IsNullOrWhiteSpace(AnchorId))
            {
                throw new ArgumentException(
                    "System-owned capability requires an anchor identity.",
                    nameof(anchorId));
            }
            if (!SystemOwned &&
                !string.IsNullOrEmpty(AnchorId))
            {
                throw new ArgumentException(
                    "Only system-owned capability can declare an anchor identity.",
                    nameof(anchorId));
            }
        }

        public GraphAuthoringCapabilityId CapabilityId { get; }
        public GraphAuthoringDomainId DomainId { get; }
        public IReadOnlyCollection<GraphAuthoringDocumentRoleId> AllowedDocumentRoles => m_AllowedDocumentRoles;
        public string DisplayName { get; }
        public string Category { get; }
        public Color Color { get; }
        public IReadOnlyCollection<GraphAuthoringFieldDescriptor> Fields => m_Fields.Values;
        public IReadOnlyCollection<GraphAuthoringPortDescriptor> FixedPorts => m_FixedPorts.Values;
        public GraphAuthoringDynamicPortPolicy DynamicPortPolicy { get; }
        public GraphAuthoringDynamicPortSource DynamicPortSource { get; }
        public IReadOnlyList<GraphAuthoringPortVariantDescriptor> PortVariants => m_PortVariants;
        public IReadOnlyList<GraphAuthoringChildSurfaceDescriptor> ChildSurfaces { get; }
        public IReadOnlyList<GraphAuthoringCommandDescriptor> Commands { get; }
        public string IconName { get; }
        public GraphAuthoringNodePresentationKind PresentationKind { get; }
        public string MutationBindingId { get; }
        public string ValidationBindingId { get; }
        public string CompilerBindingId { get; }
        public Type AuthoringType { get; }
        public string ExternalKind { get; }
        public bool SystemOwned { get; }
        public string AnchorId { get; }
        public string ExecutionDomainId { get; }

        public bool Allows(GraphAuthoringDocumentRoleId documentRoleId) => m_AllowedDocumentRoles.Contains(documentRoleId);
        public bool TryGetField(GraphAuthoringFieldId fieldId, out GraphAuthoringFieldDescriptor descriptor) => m_Fields.TryGetValue(fieldId, out descriptor);
        public bool TryGetFixedPort(GraphAuthoringPortId portId, out GraphAuthoringPortDescriptor descriptor) => m_FixedPorts.TryGetValue(portId, out descriptor);

        static Dictionary<TKey, TValue> Index<TKey, TValue>(IReadOnlyList<TValue> values, Func<TValue, TKey> key, string label)
            where TValue : class
        {
            var result = new Dictionary<TKey, TValue>();
            for (int i = 0; i < values.Count; i++)
            {
                TValue value = values[i] ?? throw new ArgumentException($"Capability {label} descriptor is missing.");
                TKey identity = key(value);
                if (!result.TryAdd(identity, value))
                    throw new InvalidOperationException($"Capability contains duplicate {label} identity '{identity}'.");
            }
            return result;
        }

        IReadOnlyList<GraphAuthoringPortVariantDescriptor> ValidatePortVariants(
            IReadOnlyList<GraphAuthoringPortVariantDescriptor> variants)
        {
            if (variants.Count == 0)
                return Array.Empty<GraphAuthoringPortVariantDescriptor>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            GraphAuthoringPortVariantCondition discriminator = variants[0]?.When ??
                throw new ArgumentException("Capability port variant is missing.", nameof(variants));
            foreach (GraphAuthoringPortVariantDescriptor variant in variants)
            {
                if (variant == null || !ids.Add(variant.VariantId))
                    throw new InvalidOperationException($"Capability '{CapabilityId}' contains a missing or duplicate port variant identity.");
                if (!variant.When.FieldId.Equals(discriminator.FieldId) ||
                    variant.When.ValueKind != discriminator.ValueKind)
                {
                    throw new InvalidOperationException($"Capability '{CapabilityId}' port variants must use one typed discriminator.");
                }
                foreach (GraphAuthoringPortDescriptor port in variant.Ports)
                {
                    if (m_FixedPorts.ContainsKey(port.PortId))
                    {
                        throw new InvalidOperationException(
                            $"Capability '{CapabilityId}' port variant '{variant.VariantId}' overlaps fixed port '{port.PortId}'.");
                    }
                }
            }
            return variants.ToArray();
        }
    }

    public sealed class GraphAuthoringCapabilityCatalog
    {
        readonly Dictionary<GraphAuthoringCapabilityId, GraphAuthoringCapabilityDescriptor> m_Descriptors =
            new Dictionary<GraphAuthoringCapabilityId, GraphAuthoringCapabilityDescriptor>();

        public IReadOnlyCollection<GraphAuthoringCapabilityDescriptor> Descriptors => m_Descriptors.Values;

        public void Register(GraphAuthoringCapabilityDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (descriptor.AuthoringType != null &&
                m_Descriptors.Values.Any(value =>
                    value.DomainId.Equals(descriptor.DomainId) &&
                    value.AuthoringType == descriptor.AuthoringType))
            {
                throw new InvalidOperationException(
                    $"Graph authoring type '{descriptor.AuthoringType.FullName}' is already registered in domain '{descriptor.DomainId}'.");
            }
            if (!string.IsNullOrEmpty(descriptor.ExternalKind) &&
                m_Descriptors.Values.Any(value =>
                    value.DomainId.Equals(descriptor.DomainId) &&
                    string.Equals(
                        value.ExternalKind,
                        descriptor.ExternalKind,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Graph authoring external kind '{descriptor.ExternalKind}' is already registered in domain '{descriptor.DomainId}'.");
            }
            if (!m_Descriptors.TryAdd(descriptor.CapabilityId, descriptor))
                throw new InvalidOperationException($"Graph authoring capability '{descriptor.CapabilityId}' is already registered.");
        }

        public bool TryGetByAuthoringType(
            GraphAuthoringDomainId domainId,
            Type authoringType,
            out GraphAuthoringCapabilityDescriptor descriptor)
        {
            descriptor = authoringType == null
                ? null
                : m_Descriptors.Values.SingleOrDefault(value =>
                    value.DomainId.Equals(domainId) &&
                    value.AuthoringType == authoringType);
            return descriptor != null;
        }

        public bool TryGetByExternalKind(
            GraphAuthoringDomainId domainId,
            string externalKind,
            out GraphAuthoringCapabilityDescriptor descriptor)
        {
            descriptor = string.IsNullOrWhiteSpace(externalKind)
                ? null
                : m_Descriptors.Values.SingleOrDefault(value =>
                    value.DomainId.Equals(domainId) &&
                    string.Equals(
                        value.ExternalKind,
                        externalKind,
                        StringComparison.Ordinal));
            return descriptor != null;
        }

        public IReadOnlyList<GraphAuthoringCapabilityDescriptor>
            GetDomain(GraphAuthoringDomainId domainId) =>
            m_Descriptors.Values
                .Where(value =>
                    value.DomainId.Equals(domainId))
                .OrderBy(
                    value => value.CapabilityId.Value,
                    StringComparer.Ordinal)
                .ToArray();

        public GraphAuthoringCapabilityDescriptor Require(GraphAuthoringCapabilityId capabilityId)
        {
            if (!m_Descriptors.TryGetValue(capabilityId, out GraphAuthoringCapabilityDescriptor descriptor))
                throw new InvalidOperationException($"Graph authoring capability '{capabilityId}' is not registered.");
            return descriptor;
        }

        public GraphAuthoringCapabilityDescriptor Require(
            GraphAuthoringCapabilityId capabilityId,
            GraphAuthoringDomainId domainId,
            GraphAuthoringDocumentRoleId documentRoleId)
        {
            GraphAuthoringCapabilityDescriptor descriptor = Require(capabilityId);
            if (!descriptor.DomainId.Equals(domainId) || !descriptor.Allows(documentRoleId))
                throw new InvalidOperationException($"Capability '{capabilityId}' is not allowed in domain '{domainId}' role '{documentRoleId}'.");
            return descriptor;
        }

        public IReadOnlyList<GraphAuthoringCapabilityDescriptor> GetAllowed(
            GraphAuthoringDomainId domainId,
            GraphAuthoringDocumentRoleId documentRoleId)
        {
            return m_Descriptors.Values
                .Where(value => value.DomainId.Equals(domainId) && value.Allows(documentRoleId))
                .OrderBy(value => value.Category, StringComparer.Ordinal)
                .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.CapabilityId.Value, StringComparer.Ordinal)
                .ToArray();
        }

        public GraphAuthoringFieldDescriptor RequireField(
            GraphAuthoringCapabilityId capabilityId,
            GraphAuthoringFieldId fieldId,
            bool requireWritable)
        {
            GraphAuthoringCapabilityDescriptor descriptor = Require(capabilityId);
            if (!descriptor.TryGetField(fieldId, out GraphAuthoringFieldDescriptor field))
                throw new InvalidOperationException($"Capability '{capabilityId}' does not declare field '{fieldId}'.");
            if (requireWritable && !field.AuthoringWritable)
                throw new InvalidOperationException($"Capability field '{capabilityId}.{fieldId}' is read-only.");
            return field;
        }

        public GraphAuthoringPortDescriptor RequireFixedPort(
            GraphAuthoringCapabilityId capabilityId,
            GraphAuthoringPortId portId)
        {
            GraphAuthoringCapabilityDescriptor descriptor = Require(capabilityId);
            if (!descriptor.TryGetFixedPort(portId, out GraphAuthoringPortDescriptor port))
                throw new InvalidOperationException($"Capability '{capabilityId}' does not declare fixed port '{portId}'.");
            return port;
        }
    }

    public static class GraphAuthoringCapabilityRegistrationRoot
    {
        static readonly GraphAuthoringCapabilityCatalog s_Catalog = new GraphAuthoringCapabilityCatalog();
        static readonly HashSet<string> s_Registrations = new HashSet<string>(StringComparer.Ordinal);

        public static GraphAuthoringCapabilityCatalog Catalog => s_Catalog;

        public static void RegisterDomain(string registrationId, Action<GraphAuthoringCapabilityCatalog> register)
        {
            string identity = GraphAuthoringIdentity.Require(registrationId, nameof(registrationId));
            if (register == null)
                throw new ArgumentNullException(nameof(register));
            if (!s_Registrations.Add(identity))
                throw new InvalidOperationException($"Graph authoring domain registration '{identity}' already ran.");
            register(s_Catalog);
        }
    }
}
