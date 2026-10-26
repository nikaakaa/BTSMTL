using System;

namespace BTSMTL.Authoring.Graph
{
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
}
