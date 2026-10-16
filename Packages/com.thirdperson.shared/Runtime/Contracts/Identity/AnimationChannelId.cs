using System;

namespace ThirdPersonSimulation
{
    public readonly partial struct AnimationChannelId : IEquatable<AnimationChannelId>, IComparable<AnimationChannelId>
    {
        public AnimationChannelId(string value)
        {
            Value = SimulationIdentity.Require(value, nameof(value));
        }

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);

        public int CompareTo(AnimationChannelId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(AnimationChannelId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AnimationChannelId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(AnimationChannelId left, AnimationChannelId right) => left.Equals(right);
        public static bool operator !=(AnimationChannelId left, AnimationChannelId right) => !left.Equals(right);
    }
}
