using System;
using System.Buffers.Binary;
using System.Globalization;

namespace ThirdPersonSimulation
{
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

    public readonly partial struct ActorId : IEquatable<ActorId>, IComparable<ActorId>
    {
        public ActorId(string value) => Value = SimulationIdentity.Require(value, nameof(value));
        [ValueTextRules(Required = true, Trim = true)]
        public string Value { get; }
        [ValuePresence]
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(ActorId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(ActorId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ActorId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(ActorId left, ActorId right) => left.Equals(right);
        public static bool operator !=(ActorId left, ActorId right) => !left.Equals(right);
    }

    public readonly partial struct ProgramRevision : IEquatable<ProgramRevision>
    {
        public ProgramRevision(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool Equals(ProgramRevision other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ProgramRevision other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly partial struct OperationSetVersion : IEquatable<OperationSetVersion>
    {
        public OperationSetVersion(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        [ValueTextRules(Required = true, Declared = true)]
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(OperationSetVersion other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is OperationSetVersion other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;


    }

    public readonly partial struct WorldRevision : IEquatable<WorldRevision>
    {
        public WorldRevision(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        [ValueTextRules(Required = true)]
        public string Value { get; }
        public bool Equals(WorldRevision other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is WorldRevision other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly partial struct SolverImplementationId : IEquatable<SolverImplementationId>
    {
        public SolverImplementationId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        [ValueTextRules(Required = true)]
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

    public readonly partial struct SimulationTick : IEquatable<SimulationTick>, IComparable<SimulationTick>
    {
        public SimulationTick(ulong value)
        {
            Value = value;
            RequireValid();
        }
        public ulong Value { get; }
        [ValueValidation]
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

}
