using System;

namespace ThirdPersonSimulation
{
    public readonly partial struct CharacterControlModuleId : IEquatable<CharacterControlModuleId>, IComparable<CharacterControlModuleId>
    {
        public CharacterControlModuleId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        [ValueValidation]
        void RequireValid() => SimulationIdentity.Require(Value, "value");
        [ValueTextRules(Declared = true)]
        public string Value { get; }
        [ValuePresence]
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(CharacterControlModuleId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(CharacterControlModuleId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CharacterControlModuleId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(CharacterControlModuleId left, CharacterControlModuleId right) => left.Equals(right);
        public static bool operator !=(CharacterControlModuleId left, CharacterControlModuleId right) => !left.Equals(right);
    }

    public readonly partial struct CharacterControlStateId : IEquatable<CharacterControlStateId>, IComparable<CharacterControlStateId>
    {
        public CharacterControlStateId(string value) => Value = SimulationIdentity.Require(value, nameof(value));
        [ValueTextRules(Declared = true)]
        public string Value { get; }
        [ValuePresence]
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(CharacterControlStateId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(CharacterControlStateId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CharacterControlStateId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(CharacterControlStateId left, CharacterControlStateId right) => left.Equals(right);
        public static bool operator !=(CharacterControlStateId left, CharacterControlStateId right) => !left.Equals(right);

        [ValueValidation]
        void RequireValid() => SimulationIdentity.Require(Value, "value");
}

    public readonly partial struct CharacterControlTransitionId : IEquatable<CharacterControlTransitionId>, IComparable<CharacterControlTransitionId>
    {
        public CharacterControlTransitionId(string value) => Value = SimulationIdentity.Require(value, nameof(value));
        [ValueTextRules(Declared = true)]
        public string Value { get; }
        [ValuePresence]
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(CharacterControlTransitionId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(CharacterControlTransitionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CharacterControlTransitionId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(CharacterControlTransitionId left, CharacterControlTransitionId right) => left.Equals(right);
        public static bool operator !=(CharacterControlTransitionId left, CharacterControlTransitionId right) => !left.Equals(right);

        [ValueValidation]
        void RequireValid() => SimulationIdentity.Require(Value, "value");
}

    public readonly partial struct CharacterSkillId : IEquatable<CharacterSkillId>, IComparable<CharacterSkillId>
    {
        public CharacterSkillId(string value) => Value = SimulationIdentity.Require(value, nameof(value));
        [ValueTextRules(Declared = true)]
        public string Value { get; }
        [ValuePresence]
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(CharacterSkillId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(CharacterSkillId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CharacterSkillId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(CharacterSkillId left, CharacterSkillId right) => left.Equals(right);
        public static bool operator !=(CharacterSkillId left, CharacterSkillId right) => !left.Equals(right);
    }

}
