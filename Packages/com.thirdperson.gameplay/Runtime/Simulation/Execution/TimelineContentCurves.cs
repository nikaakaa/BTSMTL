using System;

namespace BTSMTL.Timeline
{
    public enum TimelineCurveTimeDomain : byte
    {
        ClipNormalized,
        SourceSeconds
    }

    public readonly struct TimelineCurveChannelId : IEquatable<TimelineCurveChannelId>
    {
        public TimelineCurveChannelId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new ArgumentException("Timeline curve channel id must be non-empty and trimmed.", nameof(value));
            Value = value;
        }

        public string Value { get; }
        public bool Equals(TimelineCurveChannelId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TimelineCurveChannelId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(TimelineCurveChannelId left, TimelineCurveChannelId right) => left.Equals(right);
        public static bool operator !=(TimelineCurveChannelId left, TimelineCurveChannelId right) => !left.Equals(right);
    }

    public readonly struct TimelineCurveValueDomain
    {
        TimelineCurveValueDomain(bool bounded, float minimum, float maximum, float zero, string unit)
        {
            IsBounded = bounded;
            Minimum = minimum;
            Maximum = maximum;
            Zero = zero;
            Unit = unit ?? string.Empty;
        }

        public bool IsBounded { get; }
        public float Minimum { get; }
        public float Maximum { get; }
        public float Zero { get; }
        public string Unit { get; }
        public string Summary => IsBounded
            ? $"{Minimum:0.###} - {Maximum:0.###}{UnitSuffix()}"
            : $"Auto{UnitSuffix()}";

        public static TimelineCurveValueDomain Bounded(float minimum, float maximum, string unit = "")
        {
            if (!float.IsFinite(minimum) || !float.IsFinite(maximum) || minimum >= maximum)
                throw new ArgumentException("Bounded Timeline curve domain requires finite minimum < maximum.");
            return new TimelineCurveValueDomain(true, minimum, maximum, Math.Clamp(0f, minimum, maximum), unit);
        }

        public static TimelineCurveValueDomain Unbounded(float zero, string unit)
        {
            if (!float.IsFinite(zero))
                throw new ArgumentException("Unbounded Timeline curve domain requires a finite zero line.");
            return new TimelineCurveValueDomain(false, 0f, 0f, zero, unit);
        }

        string UnitSuffix() => string.IsNullOrEmpty(Unit) ? string.Empty : $" {Unit}";
    }
}
