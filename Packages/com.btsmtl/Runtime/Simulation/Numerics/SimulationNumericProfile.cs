using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public enum SimulationNumericRoundingMode : byte
    {
        Ieee754NearestEven = 1,
        FixedNearestEven = 2
    }

    public enum SimulationNumericOverflowMode : byte
    {
        RejectNonFinite = 1,
        RejectOverflow = 2
    }

    public readonly struct SimulationNumericProfile : IEquatable<SimulationNumericProfile>
    {
        public SimulationNumericProfile(
            NumericProfileId id,
            TargetAbiVersion abiVersion,
            int scalarBits,
            SimulationNumericRoundingMode rounding,
            SimulationNumericOverflowMode overflow,
            bool deterministicReplay)
        {
            if (!id.IsValid || !abiVersion.IsValid)
                throw new ArgumentException("Numeric profile identity is incomplete.");
            if (scalarBits <= 0)
                throw new ArgumentOutOfRangeException(nameof(scalarBits));
            Id = id;
            AbiVersion = abiVersion;
            ScalarBits = scalarBits;
            Rounding = rounding;
            Overflow = overflow;
            DeterministicReplay = deterministicReplay;
        }

        public NumericProfileId Id { get; }
        public TargetAbiVersion AbiVersion { get; }
        public int ScalarBits { get; }
        public SimulationNumericRoundingMode Rounding { get; }
        public SimulationNumericOverflowMode Overflow { get; }
        public bool DeterministicReplay { get; }
        public bool IsValid => Id.IsValid && AbiVersion.IsValid && ScalarBits > 0;
        public bool Equals(SimulationNumericProfile other)
        {
            return Id == other.Id &&
                   AbiVersion.Equals(other.AbiVersion) &&
                   ScalarBits == other.ScalarBits &&
                   Rounding == other.Rounding &&
                   Overflow == other.Overflow &&
                   DeterministicReplay == other.DeterministicReplay;
        }

        public override bool Equals(object obj) => obj is SimulationNumericProfile other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, AbiVersion, ScalarBits, (int)Rounding, (int)Overflow, DeterministicReplay);
        public static bool operator ==(SimulationNumericProfile left, SimulationNumericProfile right) => left.Equals(right);
        public static bool operator !=(SimulationNumericProfile left, SimulationNumericProfile right) => !left.Equals(right);
    }
}
