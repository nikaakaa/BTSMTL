using System;

namespace ThirdPersonSimulation
{
    public readonly struct SimulationSessionId : IEquatable<SimulationSessionId>
    {
        public SimulationSessionId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(SimulationSessionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationSessionId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct SimulationWorldId : IEquatable<SimulationWorldId>
    {
        public SimulationWorldId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(SimulationWorldId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationWorldId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct SimulationSourceClockId : IEquatable<SimulationSourceClockId>
    {
        public SimulationSourceClockId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(SimulationSourceClockId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationSourceClockId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct SimulationProtocolIdentity : IEquatable<SimulationProtocolIdentity>
    {
        public SimulationProtocolIdentity(string protocolId, string semanticVersion, StableHash schemaHash)
        {
            if (!schemaHash.IsValid)
                throw new ArgumentException("Protocol schema hash is required.", nameof(schemaHash));
            ProtocolId = SimulationIdentity.Require(protocolId, nameof(protocolId));
            SemanticVersion = SimulationIdentity.Require(semanticVersion, nameof(semanticVersion));
            SchemaHash = schemaHash;
        }

        public string ProtocolId { get; }
        public string SemanticVersion { get; }
        public StableHash SchemaHash { get; }
        public bool IsValid => !string.IsNullOrEmpty(ProtocolId) && !string.IsNullOrEmpty(SemanticVersion) && SchemaHash.IsValid;
        public bool Equals(SimulationProtocolIdentity other) =>
            string.Equals(ProtocolId, other.ProtocolId, StringComparison.Ordinal) &&
            string.Equals(SemanticVersion, other.SemanticVersion, StringComparison.Ordinal) &&
            SchemaHash.Equals(other.SchemaHash);
        public override bool Equals(object obj) => obj is SimulationProtocolIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ProtocolId, SemanticVersion, SchemaHash);
        public override string ToString() => $"{ProtocolId}@{SemanticVersion}/{SchemaHash}";
    }

    public enum SimulationComponentRole : byte
    {
        ExecutionBackend = 2,
        SessionSource = 3,
        WorldSolver = 4,
        SnapshotCodec = 5,
        Committer = 6,
        Model = 7,
        Endpoint = 8,
        Diagnostics = 9
    }

    public readonly struct SimulationComponentIdentity : IEquatable<SimulationComponentIdentity>
    {
        public SimulationComponentIdentity(
            SimulationComponentRole role,
            string componentId,
            string semanticVersion,
            StableHash configurationHash)
        {
            if (!Enum.IsDefined(typeof(SimulationComponentRole), role))
                throw new ArgumentOutOfRangeException(nameof(role));
            if (!configurationHash.IsValid)
                throw new ArgumentException("Component configuration hash is required.", nameof(configurationHash));
            Role = role;
            ComponentId = SimulationIdentity.Require(componentId, nameof(componentId));
            SemanticVersion = SimulationIdentity.Require(semanticVersion, nameof(semanticVersion));
            ConfigurationHash = configurationHash;
        }

        public SimulationComponentRole Role { get; }
        public string ComponentId { get; }
        public string SemanticVersion { get; }
        public StableHash ConfigurationHash { get; }
        public bool IsValid => !string.IsNullOrEmpty(ComponentId) && !string.IsNullOrEmpty(SemanticVersion) && ConfigurationHash.IsValid;

        public bool Equals(SimulationComponentIdentity other)
        {
            return Role == other.Role &&
                   string.Equals(ComponentId, other.ComponentId, StringComparison.Ordinal) &&
                   string.Equals(SemanticVersion, other.SemanticVersion, StringComparison.Ordinal) &&
                   ConfigurationHash.Equals(other.ConfigurationHash);
        }

        public override bool Equals(object obj) => obj is SimulationComponentIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Role, ComponentId, SemanticVersion, ConfigurationHash);
        public override string ToString() => $"{Role}:{ComponentId}@{SemanticVersion}/{ConfigurationHash}";
    }

    public readonly struct SimulationSessionCompositionIdentity : IEquatable<SimulationSessionCompositionIdentity>
    {
        public SimulationSessionCompositionIdentity(StableHash value) { Value = value; }
        public StableHash Value { get; }
        public bool IsValid => Value.IsValid;
        public bool Equals(SimulationSessionCompositionIdentity other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is SimulationSessionCompositionIdentity other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
    }
}
