using System;
using System.Collections.Generic;
using UnityEngine;

namespace BTSMTL.Authoring.Graph
{
    [Serializable]
    public sealed class GraphAuthoringClipboardEnvelope
    {
        public const string CurrentSchema = "graph-authoring-clipboard.v1";
        public string schema = CurrentSchema;
        public string domainId;
        public string documentRoleId;
        public string payload;

        public static GraphAuthoringClipboardEnvelope Create(IGraphAuthoringDocumentProjection document, string payload)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            return new GraphAuthoringClipboardEnvelope
            {
                domainId = document.DomainId.Value,
                documentRoleId = document.DocumentRoleId.Value,
                payload = payload ?? string.Empty
            };
        }

        public bool Allows(IGraphAuthoringDocumentProjection document) =>
            document != null &&
            Allows(document.DomainId.Value);

        public bool Allows(string expectedDomainId) =>
            string.Equals(
                schema,
                CurrentSchema,
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(expectedDomainId) &&
            string.Equals(
                domainId,
                expectedDomainId,
                StringComparison.Ordinal);

        public static bool TryRead(
            string serialized,
            out GraphAuthoringClipboardEnvelope envelope)
        {
            envelope = null;
            if (string.IsNullOrWhiteSpace(serialized))
                return false;
            try
            {
                envelope =
                    JsonUtility.FromJson<
                        GraphAuthoringClipboardEnvelope>(
                        serialized);
                return envelope != null &&
                       string.Equals(
                           envelope.schema,
                           CurrentSchema,
                           StringComparison.Ordinal) &&
                       !string.IsNullOrWhiteSpace(
                           envelope.domainId) &&
                       envelope.payload != null;
            }
            catch
            {
                return false;
            }
        }
    }

    public static class GraphAuthoringIdentity
    {
        public static string Require(string value, string parameterName)
        {
            string normalized = value?.Trim();
            if (string.IsNullOrEmpty(normalized))
                throw new ArgumentException("Graph authoring identity is missing.", parameterName);
            return normalized;
        }
    }

    public readonly struct GraphAuthoringDomainId : IEquatable<GraphAuthoringDomainId>, IComparable<GraphAuthoringDomainId>
    {
        public GraphAuthoringDomainId(string value) => Value = GraphAuthoringIdentity.Require(value, nameof(value));
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(GraphAuthoringDomainId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(GraphAuthoringDomainId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GraphAuthoringDomainId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct GraphAuthoringDocumentRoleId : IEquatable<GraphAuthoringDocumentRoleId>, IComparable<GraphAuthoringDocumentRoleId>
    {
        public GraphAuthoringDocumentRoleId(string value) => Value = GraphAuthoringIdentity.Require(value, nameof(value));
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(GraphAuthoringDocumentRoleId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(GraphAuthoringDocumentRoleId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GraphAuthoringDocumentRoleId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct GraphAuthoringElementId : IEquatable<GraphAuthoringElementId>, IComparable<GraphAuthoringElementId>
    {
        public GraphAuthoringElementId(string value) => Value = GraphAuthoringIdentity.Require(value, nameof(value));
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(GraphAuthoringElementId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(GraphAuthoringElementId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GraphAuthoringElementId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct GraphAuthoringPortId : IEquatable<GraphAuthoringPortId>, IComparable<GraphAuthoringPortId>
    {
        public GraphAuthoringPortId(string value) => Value = GraphAuthoringIdentity.Require(value, nameof(value));
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(GraphAuthoringPortId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(GraphAuthoringPortId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GraphAuthoringPortId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct GraphAuthoringCapabilityId : IEquatable<GraphAuthoringCapabilityId>, IComparable<GraphAuthoringCapabilityId>
    {
        public GraphAuthoringCapabilityId(string value) => Value = GraphAuthoringIdentity.Require(value, nameof(value));
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(GraphAuthoringCapabilityId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(GraphAuthoringCapabilityId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GraphAuthoringCapabilityId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct GraphAuthoringFieldId : IEquatable<GraphAuthoringFieldId>, IComparable<GraphAuthoringFieldId>
    {
        public GraphAuthoringFieldId(string value) => Value = GraphAuthoringIdentity.Require(value, nameof(value));
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(GraphAuthoringFieldId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(GraphAuthoringFieldId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GraphAuthoringFieldId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct GraphAuthoringCommandId : IEquatable<GraphAuthoringCommandId>, IComparable<GraphAuthoringCommandId>
    {
        public GraphAuthoringCommandId(string value) => Value = GraphAuthoringIdentity.Require(value, nameof(value));
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(GraphAuthoringCommandId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(GraphAuthoringCommandId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GraphAuthoringCommandId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public enum GraphAuthoringPortDirection : byte
    {
        Input = 1,
        Output = 2
    }

    public enum GraphAuthoringPortCapacity : byte
    {
        Single = 1,
        Multiple = 2
    }

    public enum GraphAuthoringSelectionKind : byte
    {
        Document = 1,
        Node = 2,
        Port = 3,
        Edge = 4,
        State = 5,
        Transition = 6
    }

    public enum GraphAuthoringMutationKind : byte
    {
        CreateNode = 1,
        DeleteElement = 2,
        MoveElement = 3,
        ConnectPorts = 4,
        DisconnectEdge = 5,
        SetField = 6,
        AddDynamicPort = 7,
        RemoveDynamicPort = 8,
        OpenChildSurface = 9,
        ExecuteCommand = 10,
        CreateState = 11,
        DeleteState = 12,
        CreateTransition = 13,
        DeleteTransition = 14,
        SetTransitionField = 15,
        CreateStateAlias = 16,
        CreateGroup = 17,
        DeleteGroup = 18,
        CreateStack = 19,
        DeleteStack = 20,
        SetDisplayName = 21,
        DeleteStateAlias = 22,
        SetStateField = 23,
        ConfigureStateAlias = 24
    }

    public enum GraphAuthoringDiagnosticSeverity : byte
    {
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public readonly struct GraphAuthoringPageProjection
    {
        public GraphAuthoringPageProjection(GraphAuthoringElementId pageId, string displayName, string tooltip)
        {
            PageId = pageId.IsValid ? pageId : throw new ArgumentException("Graph page identity is missing.", nameof(pageId));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("Graph page display name is missing.", nameof(displayName)) : displayName;
            Tooltip = tooltip ?? string.Empty;
        }

        public GraphAuthoringElementId PageId { get; }
        public string DisplayName { get; }
        public string Tooltip { get; }
    }

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

    public sealed class GraphAuthoringNodeProjection
    {
        public GraphAuthoringNodeProjection(
            GraphAuthoringElementId nodeId,
            GraphAuthoringCapabilityId capabilityId,
            string displayName,
            Vector2 position,
            IReadOnlyList<GraphAuthoringDynamicPortProjection> dynamicPorts = null,
            string status = "")
        {
            NodeId = nodeId.IsValid ? nodeId : throw new ArgumentException("Graph node identity is missing.", nameof(nodeId));
            CapabilityId = capabilityId.IsValid ? capabilityId : throw new ArgumentException("Graph node capability is missing.", nameof(capabilityId));
            DisplayName = displayName ?? string.Empty;
            Position = position;
            DynamicPorts = dynamicPorts ?? Array.Empty<GraphAuthoringDynamicPortProjection>();
            Status = status ?? string.Empty;
        }

        public GraphAuthoringElementId NodeId { get; }
        public GraphAuthoringCapabilityId CapabilityId { get; }
        public string DisplayName { get; }
        public Vector2 Position { get; }
        public IReadOnlyList<GraphAuthoringDynamicPortProjection> DynamicPorts { get; }
        public string Status { get; }
    }

    public readonly struct GraphAuthoringEdgeProjection
    {
        public GraphAuthoringEdgeProjection(
            GraphAuthoringElementId edgeId,
            GraphAuthoringElementId sourceNodeId,
            GraphAuthoringPortId sourcePortId,
            GraphAuthoringElementId targetNodeId,
            GraphAuthoringPortId targetPortId)
        {
            EdgeId = edgeId.IsValid ? edgeId : throw new ArgumentException("Graph edge identity is missing.", nameof(edgeId));
            SourceNodeId = sourceNodeId.IsValid ? sourceNodeId : throw new ArgumentException("Source node identity is missing.", nameof(sourceNodeId));
            SourcePortId = sourcePortId.IsValid ? sourcePortId : throw new ArgumentException("Source port identity is missing.", nameof(sourcePortId));
            TargetNodeId = targetNodeId.IsValid ? targetNodeId : throw new ArgumentException("Target node identity is missing.", nameof(targetNodeId));
            TargetPortId = targetPortId.IsValid ? targetPortId : throw new ArgumentException("Target port identity is missing.", nameof(targetPortId));
        }

        public GraphAuthoringElementId EdgeId { get; }
        public GraphAuthoringElementId SourceNodeId { get; }
        public GraphAuthoringPortId SourcePortId { get; }
        public GraphAuthoringElementId TargetNodeId { get; }
        public GraphAuthoringPortId TargetPortId { get; }
    }

    public readonly struct GraphAuthoringSelection
    {
        public GraphAuthoringSelection(GraphAuthoringSelectionKind kind, GraphAuthoringElementId elementId)
        {
            Kind = kind;
            ElementId = elementId.IsValid ? elementId : throw new ArgumentException("Selection identity is missing.", nameof(elementId));
        }

        public GraphAuthoringSelectionKind Kind { get; }
        public GraphAuthoringElementId ElementId { get; }
    }

    public sealed class GraphAuthoringMutationRequest
    {
        public GraphAuthoringMutationRequest(
            GraphAuthoringMutationKind kind,
            GraphAuthoringElementId targetId = default,
            GraphAuthoringCapabilityId capabilityId = default,
            GraphAuthoringFieldId fieldId = default,
            GraphAuthoringCommandId commandId = default,
            GraphAuthoringElementId sourceNodeId = default,
            GraphAuthoringPortId sourcePortId = default,
            GraphAuthoringElementId targetNodeId = default,
            GraphAuthoringPortId targetPortId = default,
            GraphAuthoringElementId secondaryTargetId = default,
            object value = null,
            Vector2 position = default)
        {
            Kind = kind;
            TargetId = targetId;
            CapabilityId = capabilityId;
            FieldId = fieldId;
            CommandId = commandId;
            SourceNodeId = sourceNodeId;
            SourcePortId = sourcePortId;
            TargetNodeId = targetNodeId;
            TargetPortId = targetPortId;
            SecondaryTargetId = secondaryTargetId;
            Value = value;
            Position = position;
        }

        public GraphAuthoringMutationKind Kind { get; }
        public GraphAuthoringElementId TargetId { get; }
        public GraphAuthoringCapabilityId CapabilityId { get; }
        public GraphAuthoringFieldId FieldId { get; }
        public GraphAuthoringCommandId CommandId { get; }
        public GraphAuthoringElementId SourceNodeId { get; }
        public GraphAuthoringPortId SourcePortId { get; }
        public GraphAuthoringElementId TargetNodeId { get; }
        public GraphAuthoringPortId TargetPortId { get; }
        public GraphAuthoringElementId SecondaryTargetId { get; }
        public object Value { get; }
        public Vector2 Position { get; }
    }

    public readonly struct GraphAuthoringDiagnosticProjection
    {
        public GraphAuthoringDiagnosticProjection(
            string code,
            GraphAuthoringDiagnosticSeverity severity,
            string message,
            GraphAuthoringElementId elementId = default)
        {
            Code = GraphAuthoringIdentity.Require(code, nameof(code));
            Severity = severity;
            Message = message ?? string.Empty;
            ElementId = elementId;
        }

        public string Code { get; }
        public GraphAuthoringDiagnosticSeverity Severity { get; }
        public string Message { get; }
        public GraphAuthoringElementId ElementId { get; }
    }

    public readonly struct GraphAuthoringRuntimeTraceProjection
    {
        public GraphAuthoringRuntimeTraceProjection(
            GraphAuthoringElementId elementId,
            string status,
            string detail,
            string sourceRevision)
        {
            ElementId = elementId.IsValid ? elementId : throw new ArgumentException("Runtime trace element identity is missing.", nameof(elementId));
            Status = status ?? string.Empty;
            Detail = detail ?? string.Empty;
            SourceRevision = sourceRevision ?? string.Empty;
        }

        public GraphAuthoringElementId ElementId { get; }
        public string Status { get; }
        public string Detail { get; }
        public string SourceRevision { get; }
    }

    public interface IGraphAuthoringDocumentProjection
    {
        GraphAuthoringDomainId DomainId { get; }
        GraphAuthoringDocumentRoleId DocumentRoleId { get; }
        string DocumentId { get; }
        string DisplayName { get; }
        string ContentRevision { get; }
        UnityEngine.Object SerializedOwner { get; }
        IReadOnlyList<GraphAuthoringPageProjection> Pages { get; }
        IReadOnlyList<GraphAuthoringNodeProjection> Nodes { get; }
        IReadOnlyList<GraphAuthoringEdgeProjection> Edges { get; }
    }

    public interface IGraphAuthoringDomainMutation
    {
        bool ReadOnly { get; }
        void Apply(IGraphAuthoringDocumentProjection document, GraphAuthoringMutationRequest request);
        void Apply(IGraphAuthoringDocumentProjection document, IReadOnlyList<GraphAuthoringMutationRequest> requests);
    }

    public interface IGraphAuthoringConnectionPolicy
    {
        bool CanConnect(
            IGraphAuthoringDocumentProjection document,
            GraphAuthoringNodeProjection sourceNode,
            GraphAuthoringPortId sourcePortId,
            GraphAuthoringNodeProjection targetNode,
            GraphAuthoringPortId targetPortId);
    }

    public interface IGraphAuthoringDomainDiagnostics
    {
        IReadOnlyList<GraphAuthoringDiagnosticProjection> GetDiagnostics(IGraphAuthoringDocumentProjection document);
        IReadOnlyList<GraphAuthoringRuntimeTraceProjection> GetRuntimeTrace(IGraphAuthoringDocumentProjection document);
    }
}
