using System;

namespace BTSMTL.Diagnostics
{
    public readonly struct GraphAuthoringId : IEquatable<GraphAuthoringId>
    {
        public GraphAuthoringId(string value) => Value = value ?? string.Empty;
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(GraphAuthoringId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is GraphAuthoringId other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct ElementAuthoringId : IEquatable<ElementAuthoringId>
    {
        public ElementAuthoringId(string value) => Value = value ?? string.Empty;
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(ElementAuthoringId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ElementAuthoringId other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct TimelineAuthoringId : IEquatable<TimelineAuthoringId>
    {
        public TimelineAuthoringId(string value) => Value = value ?? string.Empty;
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(TimelineAuthoringId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TimelineAuthoringId other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct TrackAuthoringId : IEquatable<TrackAuthoringId>
    {
        public TrackAuthoringId(string value) => Value = value ?? string.Empty;
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(TrackAuthoringId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TrackAuthoringId other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct ClipAuthoringId : IEquatable<ClipAuthoringId>
    {
        public ClipAuthoringId(string value) => Value = value ?? string.Empty;
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(ClipAuthoringId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ClipAuthoringId other && Equals(other);
        public override int GetHashCode() => Value?.GetHashCode() ?? 0;
        public override string ToString() => Value ?? string.Empty;
    }

    public static class AuthoringIdentity
    {
        public static string Create() => Guid.NewGuid().ToString("D");

        public static bool IsValid(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && Guid.TryParseExact(value, "D", out _);
        }
    }
}
