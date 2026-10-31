using System;
using System.Collections.Generic;

namespace BTSMTL.Timeline
{
    public enum TimelineCurveTimeDomain : byte
    {
        ClipNormalized,
        SourceSeconds
    }

    public enum TimelineCurveChannelRole : byte
    {
        Value = 0,
        Weight = 1,
        EaseIn = 2,
        EaseOut = 3
    }

    // 采样片段的权重与缓动曲线：Unity 曲线目录与 .NET 作者查询共用同一份通道定义。
    // Member 是片段上存储该曲线的字段；默认曲线是从 (0, DefaultStart) 到 (1, DefaultEnd) 的直线。
    public sealed class TimelineSampledCurveChannel
    {
        public static readonly TimelineSampledCurveChannel Weight =
            new(".weight", "WeightCurve", "Weight", TimelineCurveChannelRole.Weight, 120, 211, 151, 1f, 1f);
        public static readonly TimelineSampledCurveChannel EaseIn =
            new(".ease-in", "EaseInCurve", "Ease In", TimelineCurveChannelRole.EaseIn, 93, 189, 141, 0f, 1f);
        public static readonly TimelineSampledCurveChannel EaseOut =
            new(".ease-out", "EaseOutCurve", "Ease Out", TimelineCurveChannelRole.EaseOut, 226, 165, 79, 0f, 1f);
        public static readonly IReadOnlyList<TimelineSampledCurveChannel> All = new[] { Weight, EaseIn, EaseOut };

        TimelineSampledCurveChannel(string suffix, string member, string displayName, TimelineCurveChannelRole role,
            byte red, byte green, byte blue, float defaultStart, float defaultEnd)
        {
            Suffix = suffix;
            Member = member;
            DisplayName = displayName;
            Role = role;
            Red = red;
            Green = green;
            Blue = blue;
            DefaultStart = defaultStart;
            DefaultEnd = defaultEnd;
        }

        public string Suffix { get; }
        public string Member { get; }
        public string DisplayName { get; }
        public TimelineCurveChannelRole Role { get; }
        public byte Red { get; }
        public byte Green { get; }
        public byte Blue { get; }
        public float DefaultStart { get; }
        public float DefaultEnd { get; }
        public TimelineCurveValueDomain ValueDomain => TimelineCurveValueDomain.Bounded(0f, 1f);
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

    [ThirdPersonSimulation.ValueStorage(UseConstructor = true)]
    public readonly partial struct TimelineCurveValueDomain
    {
        TimelineCurveValueDomain(bool isBounded, float minimum, float maximum, float zero, string unit)
        {
            IsBounded = isBounded;
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
