using System;

namespace ThirdPersonSimulation
{
    public readonly partial struct AnimationChannelId : IEquatable<AnimationChannelId>, IComparable<AnimationChannelId>
    {
        public AnimationChannelId(CanonicalTextValue value)
        {
            Value = SimulationIdentity.Require(value, nameof(value));
        }

        public CanonicalTextValue Value { get; }
        public bool IsValid => !Value.IsEmpty;

        public int CompareTo(AnimationChannelId other) => Value.CompareTo(other.Value);
        public bool Equals(AnimationChannelId other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is AnimationChannelId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString() ?? string.Empty;
        public void Retain() => Value.Retain();
        public void Release() => Value.Release();

        public static bool operator ==(AnimationChannelId left, AnimationChannelId right) => left.Equals(right);
        public static bool operator !=(AnimationChannelId left, AnimationChannelId right) => !left.Equals(right);
    }
}
