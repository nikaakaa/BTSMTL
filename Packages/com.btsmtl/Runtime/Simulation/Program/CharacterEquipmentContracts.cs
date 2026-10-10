using System;

namespace ThirdPersonSimulation
{
    static class CharacterEquipmentIdentity
    {
        public static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Equipment identity is required.", parameter);
            return value.Trim();
        }

        public static int Compare(string left, string right) => string.CompareOrdinal(left, right);
        public static bool Equals(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
        public static int Hash(string value) => value == null ? 0 : StringComparer.Ordinal.GetHashCode(value);
    }

    public readonly struct EquipmentSlotId : IEquatable<EquipmentSlotId>, IComparable<EquipmentSlotId>
    {
        public EquipmentSlotId(string value) => Value = CanonicalTextValue.FromString(CharacterEquipmentIdentity.Require(value, nameof(value)));

        public EquipmentSlotId(CanonicalTextValue value)
        {
            Value = value.Trim();
            if (Value.IsEmpty)
                throw new ArgumentException("Equipment identity is required.", nameof(value));
        }
        public CanonicalTextValue Value { get; }
        public bool IsValid => !Value.IsEmpty;
        public int CompareTo(EquipmentSlotId other) => Value.CompareTo(other.Value);
        public bool Equals(EquipmentSlotId other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is EquipmentSlotId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public void Retain() => Value.Retain();
        public void Release() => Value.Release();
        public static bool operator ==(EquipmentSlotId left, EquipmentSlotId right) => left.Equals(right);
        public static bool operator !=(EquipmentSlotId left, EquipmentSlotId right) => !left.Equals(right);
    }

    public readonly struct EquipmentActionRouteId : IEquatable<EquipmentActionRouteId>, IComparable<EquipmentActionRouteId>
    {
        public EquipmentActionRouteId(string value) { Value = CharacterEquipmentIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(EquipmentActionRouteId other) => CharacterEquipmentIdentity.Compare(Value, other.Value);
        public bool Equals(EquipmentActionRouteId other) => CharacterEquipmentIdentity.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is EquipmentActionRouteId other && Equals(other);
        public override int GetHashCode() => CharacterEquipmentIdentity.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(EquipmentActionRouteId left, EquipmentActionRouteId right) => left.Equals(right);
        public static bool operator !=(EquipmentActionRouteId left, EquipmentActionRouteId right) => !left.Equals(right);
    }

    public readonly struct EquipmentId : IEquatable<EquipmentId>, IComparable<EquipmentId>
    {
        public EquipmentId(string value) { Value = CharacterEquipmentIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(EquipmentId other) => CharacterEquipmentIdentity.Compare(Value, other.Value);
        public bool Equals(EquipmentId other) => CharacterEquipmentIdentity.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is EquipmentId other && Equals(other);
        public override int GetHashCode() => CharacterEquipmentIdentity.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(EquipmentId left, EquipmentId right) => left.Equals(right);
        public static bool operator !=(EquipmentId left, EquipmentId right) => !left.Equals(right);
    }

    public readonly struct EquipmentFeatureId : IEquatable<EquipmentFeatureId>, IComparable<EquipmentFeatureId>
    {
        public EquipmentFeatureId(string value) { Value = CharacterEquipmentIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(EquipmentFeatureId other) => CharacterEquipmentIdentity.Compare(Value, other.Value);
        public bool Equals(EquipmentFeatureId other) => CharacterEquipmentIdentity.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is EquipmentFeatureId other && Equals(other);
        public override int GetHashCode() => CharacterEquipmentIdentity.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(EquipmentFeatureId left, EquipmentFeatureId right) => left.Equals(right);
        public static bool operator !=(EquipmentFeatureId left, EquipmentFeatureId right) => !left.Equals(right);
    }

    public readonly struct EquipmentFeatureRevision : IEquatable<EquipmentFeatureRevision>, IComparable<EquipmentFeatureRevision>
    {
        public EquipmentFeatureRevision(ulong value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0;
        public int CompareTo(EquipmentFeatureRevision other) => Value.CompareTo(other.Value);
        public bool Equals(EquipmentFeatureRevision other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EquipmentFeatureRevision other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool operator ==(EquipmentFeatureRevision left, EquipmentFeatureRevision right) => left.Equals(right);
        public static bool operator !=(EquipmentFeatureRevision left, EquipmentFeatureRevision right) => !left.Equals(right);
    }

    public readonly struct EquipmentParameterId : IEquatable<EquipmentParameterId>, IComparable<EquipmentParameterId>
    {
        public EquipmentParameterId(string value) { Value = CharacterEquipmentIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(EquipmentParameterId other) => CharacterEquipmentIdentity.Compare(Value, other.Value);
        public bool Equals(EquipmentParameterId other) => CharacterEquipmentIdentity.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is EquipmentParameterId other && Equals(other);
        public override int GetHashCode() => CharacterEquipmentIdentity.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(EquipmentParameterId left, EquipmentParameterId right) => left.Equals(right);
        public static bool operator !=(EquipmentParameterId left, EquipmentParameterId right) => !left.Equals(right);
    }

    public readonly struct EquipmentLocalStateId : IEquatable<EquipmentLocalStateId>, IComparable<EquipmentLocalStateId>
    {
        public EquipmentLocalStateId(string value) { Value = CharacterEquipmentIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(EquipmentLocalStateId other) => CharacterEquipmentIdentity.Compare(Value, other.Value);
        public bool Equals(EquipmentLocalStateId other) => CharacterEquipmentIdentity.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is EquipmentLocalStateId other && Equals(other);
        public override int GetHashCode() => CharacterEquipmentIdentity.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(EquipmentLocalStateId left, EquipmentLocalStateId right) => left.Equals(right);
        public static bool operator !=(EquipmentLocalStateId left, EquipmentLocalStateId right) => !left.Equals(right);
    }

    public readonly struct EquipmentVisualBindingId : IEquatable<EquipmentVisualBindingId>, IComparable<EquipmentVisualBindingId>
    {
        public EquipmentVisualBindingId(string value) { Value = CharacterEquipmentIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(EquipmentVisualBindingId other) => CharacterEquipmentIdentity.Compare(Value, other.Value);
        public bool Equals(EquipmentVisualBindingId other) => CharacterEquipmentIdentity.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is EquipmentVisualBindingId other && Equals(other);
        public override int GetHashCode() => CharacterEquipmentIdentity.Hash(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(EquipmentVisualBindingId left, EquipmentVisualBindingId right) => left.Equals(right);
        public static bool operator !=(EquipmentVisualBindingId left, EquipmentVisualBindingId right) => !left.Equals(right);
    }

    public readonly struct EquipmentChangeId : IEquatable<EquipmentChangeId>, IComparable<EquipmentChangeId>
    {
        public EquipmentChangeId(ulong value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0;
        public int CompareTo(EquipmentChangeId other) => Value.CompareTo(other.Value);
        public bool Equals(EquipmentChangeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EquipmentChangeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool operator ==(EquipmentChangeId left, EquipmentChangeId right) => left.Equals(right);
        public static bool operator !=(EquipmentChangeId left, EquipmentChangeId right) => !left.Equals(right);
    }

    public enum EquipmentSlotRequirement : byte
    {
        Required = 1,
        Optional = 2
    }

    public enum EquipmentRouteRequestConsumption : byte
    {
        OnActivated = 1,
        Always = 2
    }

    public enum EquipmentRouteMissingImplementation : byte
    {
        ReturnFailure = 1,
        RejectComposition = 2
    }

    public enum EquipmentParameterValueKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        Scalar = 3,
        Vector2 = 4,
        Vector3 = 5,
        Yaw = 6,
        GameplayTag = 7,
        GameplayEffect = 8,
        AnimationProducer = 9
    }

    public enum EquipmentExpectedAnimationBlendMode : byte
    {
        Override = 1,
        Additive = 2
    }

    public enum EquipmentExpectedAnimationOutputPolicy : byte
    {
        RequireSelection = 1,
        AllowEmpty = 2
    }

    public enum EquipmentVisualBindingKind : byte
    {
        ExistingRigObject = 1,
        SpawnedVisualAsset = 2
    }

    public enum EquipmentVisualLifecyclePolicy : byte
    {
        KeepWhileEquipped = 1
    }

    public readonly struct EquipmentActionContext : IEquatable<EquipmentActionContext>
    {
        public EquipmentActionContext(
            EquipmentSlotId slotId,
            EquipmentId equipmentId,
            EquipmentFeatureId featureId,
            ulong equipmentRevision,
            EquipmentActionRouteId routeId)
        {
            if (!slotId.IsValid || !equipmentId.IsValid || !featureId.IsValid || equipmentRevision == 0 || !routeId.IsValid)
                throw new ArgumentException("Equipment Action Context is incomplete.");
            SlotId = slotId;
            EquipmentId = equipmentId;
            FeatureId = featureId;
            EquipmentRevision = equipmentRevision;
            RouteId = routeId;
        }

        public EquipmentSlotId SlotId { get; }
        public EquipmentId EquipmentId { get; }
        public EquipmentFeatureId FeatureId { get; }
        public ulong EquipmentRevision { get; }
        public EquipmentActionRouteId RouteId { get; }
        public bool IsValid => SlotId.IsValid && EquipmentId.IsValid && FeatureId.IsValid && EquipmentRevision != 0 && RouteId.IsValid;
        public bool Equals(EquipmentActionContext other) =>
            SlotId == other.SlotId && EquipmentId == other.EquipmentId && FeatureId == other.FeatureId &&
            EquipmentRevision == other.EquipmentRevision && RouteId == other.RouteId;
        public override bool Equals(object obj) => obj is EquipmentActionContext other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SlotId, EquipmentId, FeatureId, EquipmentRevision, RouteId);
        public override string ToString() => IsValid
            ? $"{SlotId}/{EquipmentId}/{FeatureId}@{EquipmentRevision}/{RouteId}"
            : "None";
        public static bool operator ==(EquipmentActionContext left, EquipmentActionContext right) => left.Equals(right);
        public static bool operator !=(EquipmentActionContext left, EquipmentActionContext right) => !left.Equals(right);
    }

    public readonly struct EquipmentVisualSelection : IEquatable<EquipmentVisualSelection>
    {
        public EquipmentVisualSelection(
            ActorId actorId,
            EquipmentSlotId slotId,
            EquipmentId equipmentId,
            EquipmentVisualBindingId visualBindingId,
            ulong equipmentRevision,
            ulong sourceTick)
        {
            if (!actorId.IsValid || !slotId.IsValid || equipmentRevision == 0 || equipmentId.IsValid != visualBindingId.IsValid)
                throw new ArgumentException("Equipment visual selection is incomplete.");
            ActorId = actorId;
            SlotId = slotId;
            EquipmentId = equipmentId;
            VisualBindingId = visualBindingId;
            EquipmentRevision = equipmentRevision;
            SourceTick = sourceTick;
        }

        public ActorId ActorId { get; }
        public EquipmentSlotId SlotId { get; }
        public EquipmentId EquipmentId { get; }
        public EquipmentVisualBindingId VisualBindingId { get; }
        public ulong EquipmentRevision { get; }
        public ulong SourceTick { get; }
        public bool IsEquipped => EquipmentId.IsValid;
        public bool Equals(EquipmentVisualSelection other) =>
            ActorId == other.ActorId && SlotId == other.SlotId && EquipmentId == other.EquipmentId &&
            VisualBindingId == other.VisualBindingId && EquipmentRevision == other.EquipmentRevision && SourceTick == other.SourceTick;
        public override bool Equals(object obj) => obj is EquipmentVisualSelection other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ActorId, SlotId, EquipmentId, VisualBindingId, EquipmentRevision, SourceTick);
    }

    public static class EquipmentTagSourceIdentity
    {
        public static GameplayTagSourceIdentityValue Create(ActorId actorId, EquipmentSlotId slotId, ulong revision)
        {
            if (!actorId.IsValid || !slotId.IsValid || revision == 0)
                throw new ArgumentException("Equipment Tag source identity is incomplete.");
            return GameplayTagSourceIdentityValue.Equipment(actorId, slotId, revision);
        }
    }
}
