using System;
using System.Buffers.Binary;
using System.Globalization;

namespace ThirdPersonSimulation
{
    static class SimulationIdentity
    {
        public static CanonicalTextValue Require(CanonicalTextValue value, string parameter)
        {
            CanonicalTextValue identity = value.Trim();
            if (identity.IsEmpty)
                throw new ArgumentException("Identity is required.", parameter);
            return identity;
        }

        public static string Require(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Identity is required.", parameter);
            return value.Trim();
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct ProgramId : IEquatable<ProgramId>, IComparable<ProgramId>
    {
        public ProgramId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(ProgramId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(ProgramId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ProgramId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(ProgramId left, ProgramId right) => left.Equals(right);
        public static bool operator !=(ProgramId left, ProgramId right) => !left.Equals(right);
    }

    [CanonicalContract]
    public readonly partial struct ActorId : IEquatable<ActorId>, IComparable<ActorId>
    {
        public ActorId(string value) => Value = CanonicalTextValue.FromString(SimulationIdentity.Require(value, nameof(value)));

        public ActorId(CanonicalTextValue value)
        {
            Value = value.Trim();
            if (Value.IsEmpty)
                throw new ArgumentException("Identity is required.", nameof(value));
        }
        [CanonicalTextRules(Required = true)]
        public CanonicalTextValue Value { get; }
        [CanonicalPresence]
        public bool IsValid => !Value.IsEmpty;
        public int CompareTo(ActorId other) => Value.CompareTo(other.Value);
        public bool Equals(ActorId other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is ActorId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public void Retain() => Value.Retain();
        public void Release() => Value.Release();
        public static bool operator ==(ActorId left, ActorId right) => left.Equals(right);
        public static bool operator !=(ActorId left, ActorId right) => !left.Equals(right);
    }

    [CanonicalContract]
    public readonly partial struct StableHash : IEquatable<StableHash>, IComparable<StableHash>
    {
        [ThreadStatic] static CanonicalWriter s_HashWriter;
        readonly ulong m_A;
        readonly ulong m_B;
        readonly ulong m_C;
        readonly ulong m_D;

        public StableHash(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
                throw new ArgumentException("Stable hash must contain 64 lowercase hexadecimal characters.", nameof(value));
            Span<byte> bytes = stackalloc byte[32];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)((Hex(value[i * 2]) << 4) | Hex(value[i * 2 + 1]));
            this = new StableHash(bytes);
        }

        internal StableHash(ReadOnlySpan<byte> bytes)
        {
            m_A = BinaryPrimitives.ReadUInt64BigEndian(bytes);
            m_B = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(8));
            m_C = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(16));
            m_D = BinaryPrimitives.ReadUInt64BigEndian(bytes.Slice(24));
            IsValid = true;
        }

        internal static StableHash ParseUtf8(ReadOnlySpan<byte> value)
        {
            if (value.Length != 64)
                throw new ArgumentException("Stable hash must contain 64 lowercase hexadecimal characters.", nameof(value));
            Span<byte> bytes = stackalloc byte[32];
            for (int index = 0; index < bytes.Length; index++)
                bytes[index] = (byte)((Hex((char)value[index * 2]) << 4) | Hex((char)value[index * 2 + 1]));
            return new StableHash(bytes);
        }

        static int Hex(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'a' && value <= 'f')
                return value - 'a' + 10;
            throw new ArgumentException("Stable hash must contain 64 lowercase hexadecimal characters.", nameof(value));
        }

        public string Value => IsValid ? ToString() : null;
        public bool IsValid { get; }

        public void Format(Span<char> characters)
        {
            Span<byte> bytes = stackalloc byte[32];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, m_A);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(8), m_B);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(16), m_C);
            BinaryPrimitives.WriteUInt64BigEndian(bytes.Slice(24), m_D);
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                characters[i * 2] = hex[bytes[i] >> 4];
                characters[i * 2 + 1] = hex[bytes[i] & 15];
            }
        }

        public int CompareTo(StableHash other)
        {
            int result = IsValid.CompareTo(other.IsValid);
            if (result == 0) result = m_A.CompareTo(other.m_A);
            if (result == 0) result = m_B.CompareTo(other.m_B);
            if (result == 0) result = m_C.CompareTo(other.m_C);
            if (result == 0) result = m_D.CompareTo(other.m_D);
            return result;
        }

        public bool Equals(StableHash other) => IsValid == other.IsValid &&
            m_A == other.m_A && m_B == other.m_B && m_C == other.m_C && m_D == other.m_D;
        public override bool Equals(object obj) => obj is StableHash other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(IsValid, m_A, m_B, m_C, m_D);
        public override string ToString() => IsValid
            ? string.Create(64, this, static (characters, value) => value.Format(characters)) : string.Empty;
        public static StableHash Compute(params string[] values)
        {
            CanonicalWriter writer = s_HashWriter ??= new CanonicalWriter();
            writer.Reset();
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0)
                        writer.WriteByte(0x1f);
                    writer.WriteRawText(values[i].AsSpan());
                }
            }
            return writer.ComputeHash();
        }

        public static CanonicalWriter BeginHash()
        {
            CanonicalWriter writer = s_HashWriter ??= new CanonicalWriter();
            writer.Reset();
            return writer;
        }

        public static bool operator ==(StableHash left, StableHash right) => left.Equals(right);
        public static bool operator !=(StableHash left, StableHash right) => !left.Equals(right);
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct ProgramRevision : IEquatable<ProgramRevision>
    {
        public ProgramRevision(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool Equals(ProgramRevision other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ProgramRevision other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    [CanonicalContract]
    public readonly partial struct SemanticHash : IEquatable<SemanticHash>, ICanonicalHashValue
    {
        public SemanticHash(StableHash value) { Value = value; }
        public StableHash Value { get; }
        public bool IsValid => Value.IsValid;
        public bool Equals(SemanticHash other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SemanticHash other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct NumericProfileId : IEquatable<NumericProfileId>, IComparable<NumericProfileId>
    {
        public NumericProfileId(string value) { Value = value; RequireValid(); }
        public string Value { get; }
        [CanonicalValidation]
        void RequireValid() => SimulationIdentity.Require(Value, "value");
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(NumericProfileId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(NumericProfileId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NumericProfileId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(NumericProfileId left, NumericProfileId right) => left.Equals(right);
        public static bool operator !=(NumericProfileId left, NumericProfileId right) => !left.Equals(right);
    }

    [CanonicalContract(MaterializedRead = true)]
    public readonly partial struct OperationSetVersion : IEquatable<OperationSetVersion>
    {
        public OperationSetVersion(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        [CanonicalDeclaredText, CanonicalTextRules(Required = true)]
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(OperationSetVersion other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is OperationSetVersion other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;


    }

    [CanonicalContract]
    public readonly partial struct TargetAbiVersion : IEquatable<TargetAbiVersion>
    {
        public TargetAbiVersion(int value)
        {
            Value = value;
            RequireValid();
        }
        public int Value { get; }
        [CanonicalValidation]
        void RequireValid()
        {
            if (Value <= 0)
                throw new ArgumentOutOfRangeException("value");
        }
        public bool IsValid => Value > 0;
        public bool Equals(TargetAbiVersion other) => Value == other.Value;
        public override bool Equals(object obj) => obj is TargetAbiVersion other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }

    [CanonicalContract]
    public readonly partial struct LayoutHash : IEquatable<LayoutHash>, ICanonicalHashValue
    {
        public LayoutHash(StableHash value) { Value = value; }
        public StableHash Value { get; }
        public bool IsValid => Value.IsValid;
        public bool Equals(LayoutHash other) => Value == other.Value;
        public override bool Equals(object obj) => obj is LayoutHash other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
    }

    [CanonicalContract]
    public readonly partial struct GameplayContentHash : IEquatable<GameplayContentHash>, ICanonicalHashValue
    {
        public GameplayContentHash(StableHash value) { Value = value; }
        public StableHash Value { get; }
        public bool IsValid => Value.IsValid;
        public bool Equals(GameplayContentHash other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GameplayContentHash other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct WorldRevision : IEquatable<WorldRevision>
    {
        public WorldRevision(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        [CanonicalTextRules(Required = true)]
        public string Value { get; }
        public bool Equals(WorldRevision other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is WorldRevision other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct SolverImplementationId : IEquatable<SolverImplementationId>
    {
        public SolverImplementationId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        [CanonicalTextRules(Required = true)]
        public string Value { get; }
        public bool Equals(SolverImplementationId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SolverImplementationId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct HostProductId : IEquatable<HostProductId>, IComparable<HostProductId>
    {
        public HostProductId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(HostProductId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(HostProductId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is HostProductId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(HostProductId left, HostProductId right) => left.Equals(right);
        public static bool operator !=(HostProductId left, HostProductId right) => !left.Equals(right);
    }

    [CanonicalContract]
    public readonly partial struct SimulationTick : IEquatable<SimulationTick>, IComparable<SimulationTick>
    {
        public SimulationTick(ulong value)
        {
            Value = value;
            RequireValid();
        }
        public ulong Value { get; }
        [CanonicalValidation]
        void RequireValid()
        {
            if (Value == 0)
                throw new ArgumentOutOfRangeException("value");
        }
        public bool IsValid => Value != 0;
        public int CompareTo(SimulationTick other) => Value.CompareTo(other.Value);
        public bool Equals(SimulationTick other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SimulationTick other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(SimulationTick left, SimulationTick right) => left.Equals(right);
        public static bool operator !=(SimulationTick left, SimulationTick right) => !left.Equals(right);
    }

    public readonly struct CommittedMovementPlaybackClock : IEquatable<CommittedMovementPlaybackClock>
    {
        public CommittedMovementPlaybackClock(
            string ownerIdentity,
            ulong generation,
            SimulationTick authorityTick,
            double continuousTicks,
            int tickRate)
        {
            OwnerIdentity = SimulationIdentity.Require(ownerIdentity, nameof(ownerIdentity));
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (!authorityTick.IsValid)
                throw new ArgumentException("Movement playback authority tick is invalid.", nameof(authorityTick));
            if (continuousTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(continuousTicks));
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            Generation = generation;
            AuthorityTick = authorityTick;
            ContinuousTicks = continuousTicks;
            TickRate = tickRate;
        }

        public string OwnerIdentity { get; }
        public ulong Generation { get; }
        public SimulationTick AuthorityTick { get; }
        public double ContinuousTicks { get; }
        public int TickRate { get; }
        public bool IsValid =>
            !string.IsNullOrEmpty(OwnerIdentity) &&
            Generation != 0 &&
            AuthorityTick.IsValid &&
            ContinuousTicks >= 0 &&
            TickRate > 0;
        public double ElapsedSeconds => IsValid ? ContinuousTicks / TickRate : 0d;
        public bool Equals(CommittedMovementPlaybackClock other) =>
            Generation == other.Generation &&
            AuthorityTick == other.AuthorityTick &&
            ContinuousTicks == other.ContinuousTicks &&
            TickRate == other.TickRate &&
            string.Equals(OwnerIdentity, other.OwnerIdentity, StringComparison.Ordinal);
        public override bool Equals(object obj) =>
            obj is CommittedMovementPlaybackClock other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(
            OwnerIdentity == null ? 0 : StringComparer.Ordinal.GetHashCode(OwnerIdentity),
            Generation,
            AuthorityTick,
            ContinuousTicks,
            TickRate);
        public static bool operator ==(
            CommittedMovementPlaybackClock left,
            CommittedMovementPlaybackClock right) => left.Equals(right);
        public static bool operator !=(
            CommittedMovementPlaybackClock left,
            CommittedMovementPlaybackClock right) => !left.Equals(right);
    }

    public readonly struct CommittedLocomotionPlanarMotionTimeline : IEquatable<CommittedLocomotionPlanarMotionTimeline>
    {
        public CommittedLocomotionPlanarMotionTimeline(
            string ownerIdentity,
            ulong generation,
            SimulationTick authorityTick,
            int tickRate,
            float currentVelocityX,
            float currentVelocityZ,
            float bodyYawVelocityDegreesPerSecond,
            float maximumBodyYawVelocityDegreesPerSecond,
            int currentSegmentDurationTicks,
            string continuationOwnerIdentity,
            float continuationVelocityX,
            float continuationVelocityZ)
        {
            OwnerIdentity = SimulationIdentity.Require(ownerIdentity, nameof(ownerIdentity));
            if (generation == 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (!authorityTick.IsValid)
                throw new ArgumentException("Locomotion motion timeline authority tick is invalid.", nameof(authorityTick));
            if (tickRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (!float.IsFinite(currentVelocityX) || !float.IsFinite(currentVelocityZ) ||
                !float.IsFinite(bodyYawVelocityDegreesPerSecond) ||
                !float.IsFinite(maximumBodyYawVelocityDegreesPerSecond) ||
                maximumBodyYawVelocityDegreesPerSecond < 0f ||
                currentSegmentDurationTicks < 0 ||
                !float.IsFinite(continuationVelocityX) || !float.IsFinite(continuationVelocityZ))
            {
                throw new ArgumentException("Locomotion motion timeline contains invalid motion data.");
            }
            if (currentSegmentDurationTicks == 0 && !string.IsNullOrEmpty(continuationOwnerIdentity))
                throw new ArgumentException("Continuous Locomotion motion timeline cannot declare a continuation.", nameof(continuationOwnerIdentity));
            if (string.IsNullOrEmpty(continuationOwnerIdentity) &&
                (continuationVelocityX != 0f || continuationVelocityZ != 0f))
            {
                throw new ArgumentException("Locomotion continuation velocity has no owner.", nameof(continuationOwnerIdentity));
            }
            OwnerIdentity = ownerIdentity;
            Generation = generation;
            AuthorityTick = authorityTick;
            TickRate = tickRate;
            CurrentVelocityX = currentVelocityX;
            CurrentVelocityZ = currentVelocityZ;
            BodyYawVelocityDegreesPerSecond = bodyYawVelocityDegreesPerSecond;
            MaximumBodyYawVelocityDegreesPerSecond = maximumBodyYawVelocityDegreesPerSecond;
            CurrentSegmentDurationTicks = currentSegmentDurationTicks;
            ContinuationOwnerIdentity = continuationOwnerIdentity ?? string.Empty;
            ContinuationVelocityX = continuationVelocityX;
            ContinuationVelocityZ = continuationVelocityZ;
        }

        public string OwnerIdentity { get; }
        public ulong Generation { get; }
        public SimulationTick AuthorityTick { get; }
        public int TickRate { get; }
        public float CurrentVelocityX { get; }
        public float CurrentVelocityZ { get; }
        public float BodyYawVelocityDegreesPerSecond { get; }
        public float MaximumBodyYawVelocityDegreesPerSecond { get; }
        public int CurrentSegmentDurationTicks { get; }
        public string ContinuationOwnerIdentity { get; }
        public float ContinuationVelocityX { get; }
        public float ContinuationVelocityZ { get; }
        public bool HasContinuation => !string.IsNullOrEmpty(ContinuationOwnerIdentity);
        public bool IsValid =>
            !string.IsNullOrEmpty(OwnerIdentity) &&
            Generation != 0 &&
            AuthorityTick.IsValid &&
            TickRate > 0 &&
            float.IsFinite(CurrentVelocityX) &&
            float.IsFinite(CurrentVelocityZ) &&
            float.IsFinite(BodyYawVelocityDegreesPerSecond) &&
            float.IsFinite(MaximumBodyYawVelocityDegreesPerSecond) &&
            MaximumBodyYawVelocityDegreesPerSecond >= 0f &&
            CurrentSegmentDurationTicks >= 0 &&
            float.IsFinite(ContinuationVelocityX) &&
            float.IsFinite(ContinuationVelocityZ) &&
            (HasContinuation || ContinuationVelocityX == 0f && ContinuationVelocityZ == 0f);
        public double CurrentSegmentDurationSeconds =>
            CurrentSegmentDurationTicks > 0 ? (double)CurrentSegmentDurationTicks / TickRate : 0d;
        public bool Matches(CommittedMovementPlaybackClock clock) =>
            IsValid && clock.IsValid &&
            Generation == clock.Generation &&
            AuthorityTick == clock.AuthorityTick &&
            TickRate == clock.TickRate &&
            string.Equals(OwnerIdentity, clock.OwnerIdentity, StringComparison.Ordinal);
        public bool Equals(CommittedLocomotionPlanarMotionTimeline other) =>
            Generation == other.Generation &&
            AuthorityTick == other.AuthorityTick &&
            TickRate == other.TickRate &&
            CurrentVelocityX.Equals(other.CurrentVelocityX) &&
            CurrentVelocityZ.Equals(other.CurrentVelocityZ) &&
            BodyYawVelocityDegreesPerSecond.Equals(other.BodyYawVelocityDegreesPerSecond) &&
            MaximumBodyYawVelocityDegreesPerSecond.Equals(other.MaximumBodyYawVelocityDegreesPerSecond) &&
            CurrentSegmentDurationTicks == other.CurrentSegmentDurationTicks &&
            ContinuationVelocityX.Equals(other.ContinuationVelocityX) &&
            ContinuationVelocityZ.Equals(other.ContinuationVelocityZ) &&
            string.Equals(OwnerIdentity, other.OwnerIdentity, StringComparison.Ordinal) &&
            string.Equals(ContinuationOwnerIdentity, other.ContinuationOwnerIdentity, StringComparison.Ordinal);
        public override bool Equals(object obj) =>
            obj is CommittedLocomotionPlanarMotionTimeline other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(
            HashCode.Combine(
                OwnerIdentity == null ? 0 : StringComparer.Ordinal.GetHashCode(OwnerIdentity),
                Generation,
                AuthorityTick,
                TickRate,
                CurrentVelocityX,
                CurrentVelocityZ,
                BodyYawVelocityDegreesPerSecond,
                CurrentSegmentDurationTicks),
            MaximumBodyYawVelocityDegreesPerSecond,
            ContinuationOwnerIdentity == null ? 0 : StringComparer.Ordinal.GetHashCode(ContinuationOwnerIdentity),
            ContinuationVelocityX,
            ContinuationVelocityZ);
        public static bool operator ==(
            CommittedLocomotionPlanarMotionTimeline left,
            CommittedLocomotionPlanarMotionTimeline right) => left.Equals(right);
        public static bool operator !=(
            CommittedLocomotionPlanarMotionTimeline left,
            CommittedLocomotionPlanarMotionTimeline right) => !left.Equals(right);
    }

    public enum SimulationTickSourceKind : byte
    {
        LocalLogic = 1,
        Authoritative = 2,
        Replay = 3
    }

    [CanonicalContract]
    public readonly partial struct SimulationTickSourceIdentity : IEquatable<SimulationTickSourceIdentity>
    {
        public SimulationTickSourceIdentity(SimulationTickSourceKind kind, CanonicalTextValue clockId, ulong sourceTick)
        {
            Kind = kind;
            ClockId = clockId;
            SourceTick = sourceTick;
            RequireClock();
        }
        public SimulationTickSourceKind Kind { get; }
        public CanonicalTextValue ClockId { get; }
        public ulong SourceTick { get; }
        [CanonicalValidation]
        void RequireClock() => SimulationIdentity.Require(ClockId, nameof(ClockId));
        public void Retain() => ClockId.Retain();
        public void Release() => ClockId.Release();
        public bool Equals(SimulationTickSourceIdentity other) => Kind == other.Kind && SourceTick == other.SourceTick && CanonicalTextValue.Equals(ClockId, other.ClockId, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationTickSourceIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Kind, ClockId.GetHashCode(), SourceTick);
    }

    [CanonicalContract]
    public readonly partial struct OperationHandle : IEquatable<OperationHandle>, IComparable<OperationHandle>
    {
        readonly int m_EncodedValue;

        public static OperationHandle Invalid => default;

        public OperationHandle(int value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            m_EncodedValue = checked(value + 1);
        }
        public int Value => m_EncodedValue - 1;
        public bool IsValid => m_EncodedValue > 0;
        public int CompareTo(OperationHandle other) => Value.CompareTo(other.Value);
        public bool Equals(OperationHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is OperationHandle other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }


    public readonly struct WorldRequestId : IEquatable<WorldRequestId>
    {
        public WorldRequestId(ActorId actorId, SimulationTick tick, ulong sequence)
        {
            if (!actorId.IsValid || !tick.IsValid || sequence == 0)
                throw new ArgumentException("World request identity is incomplete.");
            ActorId = actorId;
            Tick = tick;
            Sequence = sequence;
        }
        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public ulong Sequence { get; }
        public bool IsValid => ActorId.IsValid && Tick.IsValid && Sequence != 0;
        public bool Equals(WorldRequestId other) => ActorId == other.ActorId && Tick == other.Tick && Sequence == other.Sequence;
        public override bool Equals(object obj) => obj is WorldRequestId other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ActorId, Tick, Sequence);
        public override string ToString() => $"{ActorId}/{Tick}/{Sequence}";
    }

    [CanonicalContract]
    public readonly partial struct CharacterStateHash : IEquatable<CharacterStateHash>, ICanonicalHashValue
    {
        public CharacterStateHash(StableHash value) { Value = value; }
        public StableHash Value { get; }
        public bool IsValid => Value.IsValid;
        public bool Equals(CharacterStateHash other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CharacterStateHash other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
    }

    [CanonicalContract]
    public readonly partial struct SimulationWorldHash : IEquatable<SimulationWorldHash>, ICanonicalHashValue
    {
        public SimulationWorldHash(StableHash value) { Value = value; }
        public StableHash Value { get; }
        public bool IsValid => Value.IsValid;
        public bool Equals(SimulationWorldHash other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SimulationWorldHash other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
    }
}
