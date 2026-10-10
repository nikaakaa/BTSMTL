using System;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public readonly struct SimulationPipelineId : IEquatable<SimulationPipelineId>, IComparable<SimulationPipelineId>
    {
        public SimulationPipelineId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(SimulationPipelineId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(SimulationPipelineId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationPipelineId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct SimulationPipelineRevision : IEquatable<SimulationPipelineRevision>
    {
        public SimulationPipelineRevision(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(SimulationPipelineRevision other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationPipelineRevision other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct SimulationPipelineSchemaVersion : IEquatable<SimulationPipelineSchemaVersion>
    {
        public SimulationPipelineSchemaVersion(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public int Value { get; }
        public bool IsValid => Value > 0;
        public bool Equals(SimulationPipelineSchemaVersion other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SimulationPipelineSchemaVersion other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }

    public readonly struct SimulationPipelineIdentity : IEquatable<SimulationPipelineIdentity>
    {
        public SimulationPipelineIdentity(
            SimulationPipelineId id,
            SimulationPipelineRevision revision,
            SimulationPipelineSchemaVersion schemaVersion,
            SimulationPipelineHash hash)
        {
            if (!id.IsValid || !revision.IsValid || !schemaVersion.IsValid || !hash.IsValid)
                throw new ArgumentException("Pipeline identity is incomplete.");
            Id = id;
            Revision = revision;
            SchemaVersion = schemaVersion;
            Hash = hash;
        }

        public SimulationPipelineId Id { get; }
        public SimulationPipelineRevision Revision { get; }
        public SimulationPipelineSchemaVersion SchemaVersion { get; }
        public SimulationPipelineHash Hash { get; }
        public bool IsValid => Id.IsValid && Revision.IsValid && SchemaVersion.IsValid && Hash.IsValid;

        public bool Equals(SimulationPipelineIdentity other)
        {
            return Id.Equals(other.Id) && Revision.Equals(other.Revision) &&
                   SchemaVersion.Equals(other.SchemaVersion) && Hash.Equals(other.Hash);
        }

        public override bool Equals(object obj) => obj is SimulationPipelineIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, Revision, SchemaVersion, Hash);
        public override string ToString() => $"{Id}@{Revision}/schema{SchemaVersion}/{Hash}";
    }
}
