using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationTime : IEquatable<OrchestrationTime>, IComparable<OrchestrationTime>
    {
        public const int Precision = 16;
        public const long Scale = 1L << Precision;

        OrchestrationTime(long rawValue) => RawValue = rawValue;

        public long RawValue { get; }
        public float AsSingle => (float)RawValue / Scale;

        public static OrchestrationTime Zero => default;
        public static OrchestrationTime One => new OrchestrationTime(Scale);
        public static OrchestrationTime MinValue => new OrchestrationTime(long.MinValue);
        public static OrchestrationTime MaxValue => new OrchestrationTime(long.MaxValue);

        public static OrchestrationTime FromRaw(long value) => new OrchestrationTime(value);
        public static OrchestrationTime FromSeconds(float value) => FromSeconds((double)value);
        public static OrchestrationTime FromSeconds(double value) =>
            new OrchestrationTime(unchecked((long)Math.Round(value * Scale)));
        public static OrchestrationTime FromInt32(int value) =>
            new OrchestrationTime((long)value << Precision);
        public static OrchestrationTime FromInt64(long value) =>
            new OrchestrationTime(value << Precision);

        public static OrchestrationTime Min(OrchestrationTime left, OrchestrationTime right) =>
            left < right ? left : right;
        public static OrchestrationTime Max(OrchestrationTime left, OrchestrationTime right) =>
            left > right ? left : right;
        public static OrchestrationTime Clamp(OrchestrationTime value,
            OrchestrationTime minimum, OrchestrationTime maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;

        public bool Equals(OrchestrationTime other) => RawValue == other.RawValue;
        public override bool Equals(object obj) => obj is OrchestrationTime other && Equals(other);
        public override int GetHashCode() => RawValue.GetHashCode();
        public int CompareTo(OrchestrationTime other) => RawValue.CompareTo(other.RawValue);

        public static bool operator ==(OrchestrationTime left, OrchestrationTime right) =>
            left.RawValue == right.RawValue;
        public static bool operator !=(OrchestrationTime left, OrchestrationTime right) =>
            left.RawValue != right.RawValue;
        public static bool operator <(OrchestrationTime left, OrchestrationTime right) =>
            left.RawValue < right.RawValue;
        public static bool operator <=(OrchestrationTime left, OrchestrationTime right) =>
            left.RawValue <= right.RawValue;
        public static bool operator >(OrchestrationTime left, OrchestrationTime right) =>
            left.RawValue > right.RawValue;
        public static bool operator >=(OrchestrationTime left, OrchestrationTime right) =>
            left.RawValue >= right.RawValue;

        public static OrchestrationTime operator +(OrchestrationTime left, OrchestrationTime right) =>
            new OrchestrationTime(unchecked(left.RawValue + right.RawValue));
        public static OrchestrationTime operator -(OrchestrationTime left, OrchestrationTime right) =>
            new OrchestrationTime(unchecked(left.RawValue - right.RawValue));
        public static OrchestrationTime operator -(OrchestrationTime value) =>
            new OrchestrationTime(unchecked(-value.RawValue));
        public static OrchestrationTime operator *(OrchestrationTime left, OrchestrationTime right) =>
            new OrchestrationTime(unchecked(left.RawValue * right.RawValue + (Scale >> 1)) >> Precision);
        public static OrchestrationTime operator /(OrchestrationTime left, OrchestrationTime right) =>
            new OrchestrationTime((left.RawValue << Precision) / right.RawValue);
        public static OrchestrationTime operator %(OrchestrationTime left, OrchestrationTime right) =>
            new OrchestrationTime(left.RawValue % right.RawValue);
    }
}
