using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public enum SimulationPipelinePhase : byte
    {
        Ingress = 1,
        Schedule = 2,
        Step = 3,
        Egress = 4
    }

    public readonly struct SimulationPipelinePassId : IEquatable<SimulationPipelinePassId>, IComparable<SimulationPipelinePassId>
    {
        public SimulationPipelinePassId(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public int CompareTo(SimulationPipelinePassId other) => string.CompareOrdinal(Value, other.Value);
        public bool Equals(SimulationPipelinePassId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationPipelinePassId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct SimulationPipelinePassImplementationVersion : IEquatable<SimulationPipelinePassImplementationVersion>
    {
        public SimulationPipelinePassImplementationVersion(string value) { Value = SimulationIdentity.Require(value, nameof(value)); }
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(SimulationPipelinePassImplementationVersion other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SimulationPipelinePassImplementationVersion other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    [Flags]
    public enum SimulationPipelineExecutionSupport : byte
    {
        None = 0,
        Forward = 1 << 0,
        Replay = 1 << 1,
        Restore = 1 << 2,
        Authoritative = 1 << 3
    }

    public enum SimulationPipelinePassStateClass : byte
    {
        Stateless = 1,
        Reconstructible = 2,
        SnapshotParticipant = 3,
        ExternalSource = 4
    }

    public enum SimulationPipelineProductAccessKind : byte
    {
        ExclusiveProducer = 1,
        AppendOnlyProducer = 2,
        ReadOnlyConsumer = 3
    }

    public readonly struct SimulationPipelineProductAccess
    {
        public SimulationPipelineProductAccess(
            SimulationPipelineProductContract product,
            SimulationPipelineProductAccessKind access,
            bool required = true)
        {
            Product = product ?? throw new ArgumentNullException(nameof(product));
            if (!Enum.IsDefined(typeof(SimulationPipelineProductAccessKind), access))
                throw new ArgumentOutOfRangeException(nameof(access));
            if (access == SimulationPipelineProductAccessKind.ExclusiveProducer &&
                product.Multiplicity != SimulationPipelineProductMultiplicity.Exclusive)
            {
                throw new ArgumentException("Exclusive producer requires an exclusive Product.", nameof(product));
            }
            if (access == SimulationPipelineProductAccessKind.AppendOnlyProducer &&
                product.Multiplicity != SimulationPipelineProductMultiplicity.AppendOnly)
            {
                throw new ArgumentException("Append-only producer requires an append-only Product.", nameof(product));
            }
            if (access != SimulationPipelineProductAccessKind.ReadOnlyConsumer && !required)
                throw new ArgumentException("Producer Product access cannot be optional.", nameof(required));
            Access = access;
            Required = required;
        }

        public SimulationPipelineProductContract Product { get; }
        public SimulationPipelineProductAccessKind Access { get; }
        public bool Required { get; }
        public bool IsProducer => Access != SimulationPipelineProductAccessKind.ReadOnlyConsumer;
    }

    public enum SimulationPipelineBindingPortRole : byte
    {
        Source = 1,
        Target = 2,
        Solver = 3,
        Diagnostics = 4
    }

    public readonly struct SimulationPipelinePortRequirement
    {
        public SimulationPipelinePortRequirement(
            SimulationPipelineBindingPortRole role,
            string portId,
            string schemaId,
            int schemaVersion,
            SimulationPortDirection direction)
        {
            if (!Enum.IsDefined(typeof(SimulationPipelineBindingPortRole), role) ||
                schemaVersion <= 0 || !Enum.IsDefined(typeof(SimulationPortDirection), direction))
            {
                throw new ArgumentException("Pipeline binding port requirement is incomplete.");
            }
            Role = role;
            PortId = SimulationIdentity.Require(portId, nameof(portId));
            SchemaId = SimulationIdentity.Require(schemaId, nameof(schemaId));
            SchemaVersion = schemaVersion;
            Direction = direction;
        }

        public SimulationPipelineBindingPortRole Role { get; }
        public string PortId { get; }
        public string SchemaId { get; }
        public int SchemaVersion { get; }
        public SimulationPortDirection Direction { get; }
    }

    public readonly struct SimulationPipelinePassRequirement : IEquatable<SimulationPipelinePassRequirement>
    {
        public SimulationPipelinePassRequirement(
            SimulationPipelinePassId passId,
            SimulationPipelinePassImplementationVersion implementationVersion,
            SimulationPipelinePhase phase)
        {
            if (!passId.IsValid || !implementationVersion.IsValid || !Enum.IsDefined(typeof(SimulationPipelinePhase), phase))
                throw new ArgumentException("Pipeline Pass requirement is incomplete.");
            PassId = passId;
            ImplementationVersion = implementationVersion;
            Phase = phase;
        }

        public SimulationPipelinePassId PassId { get; }
        public SimulationPipelinePassImplementationVersion ImplementationVersion { get; }
        public SimulationPipelinePhase Phase { get; }
        public bool Equals(SimulationPipelinePassRequirement other) =>
            PassId.Equals(other.PassId) &&
            ImplementationVersion.Equals(other.ImplementationVersion) &&
            Phase == other.Phase;
        public override bool Equals(object obj) => obj is SimulationPipelinePassRequirement other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(PassId, ImplementationVersion, (int)Phase);
        public override string ToString() => $"{PassId}@{ImplementationVersion}/{Phase}";
    }

    public sealed class SimulationPipelinePassDescriptor
    {
        readonly ReadOnlyCollection<SimulationPipelineProductAccess> m_ProductAccesses;
        readonly ReadOnlyCollection<SimulationPipelinePortRequirement> m_PortRequirements;

        public SimulationPipelinePassDescriptor(
            SimulationPipelinePassId passId,
            SimulationPipelinePassImplementationVersion implementationVersion,
            SimulationPipelinePhase phase,
            StableHash configurationHash,
            NumericProfileId numericProfileId,
            TargetAbiVersion targetAbiVersion,
            string backendId,
            string backendSemanticVersion,
            WorldCapability requiredSolverCapabilities,
            SimulationPipelineExecutionSupport executionSupport,
            SimulationPipelinePassStateClass stateClass,
            string stateOwner,
            IEnumerable<SimulationPipelineProductAccess> productAccesses,
            IEnumerable<SimulationPipelinePortRequirement> portRequirements = null)
        {
            if (!passId.IsValid || !implementationVersion.IsValid ||
                !Enum.IsDefined(typeof(SimulationPipelinePhase), phase) || !configurationHash.IsValid ||
                !numericProfileId.IsValid || !targetAbiVersion.IsValid ||
                (executionSupport & SimulationPipelineExecutionSupport.Forward) == 0 ||
                !Enum.IsDefined(typeof(SimulationPipelinePassStateClass), stateClass))
            {
                throw new ArgumentException("Pipeline Pass descriptor identity or capability is incomplete.");
            }
            bool ownsState = stateClass != SimulationPipelinePassStateClass.Stateless;
            if (ownsState != !string.IsNullOrWhiteSpace(stateOwner))
                throw new ArgumentException("Stateful Pass requires one explicit state owner; Stateless Pass cannot declare one.", nameof(stateOwner));

            PassId = passId;
            ImplementationVersion = implementationVersion;
            Phase = phase;
            ConfigurationHash = configurationHash;
            NumericProfileId = numericProfileId;
            TargetAbiVersion = targetAbiVersion;
            BackendId = SimulationIdentity.Require(backendId, nameof(backendId));
            BackendSemanticVersion = SimulationIdentity.Require(backendSemanticVersion, nameof(backendSemanticVersion));
            RequiredSolverCapabilities = requiredSolverCapabilities;
            ExecutionSupport = executionSupport;
            StateClass = stateClass;
            StateOwner = stateOwner?.Trim() ?? string.Empty;
            m_ProductAccesses = FreezeProductAccesses(productAccesses);
            m_PortRequirements = FreezePortRequirements(portRequirements);
            DescriptorHash = ComputeDescriptorHash();
        }

        public SimulationPipelinePassId PassId { get; }
        public SimulationPipelinePassImplementationVersion ImplementationVersion { get; }
        public SimulationPipelinePhase Phase { get; }
        public StableHash ConfigurationHash { get; }
        public NumericProfileId NumericProfileId { get; }
        public TargetAbiVersion TargetAbiVersion { get; }
        public string BackendId { get; }
        public string BackendSemanticVersion { get; }
        public WorldCapability RequiredSolverCapabilities { get; }
        public SimulationPipelineExecutionSupport ExecutionSupport { get; }
        public SimulationPipelinePassStateClass StateClass { get; }
        public string StateOwner { get; }
        public IReadOnlyList<SimulationPipelineProductAccess> ProductAccesses => m_ProductAccesses;
        public IReadOnlyList<SimulationPipelinePortRequirement> PortRequirements => m_PortRequirements;
        public StableHash DescriptorHash { get; }
        public string VersionedIdentity => $"{PassId}@{ImplementationVersion}";

        static ReadOnlyCollection<SimulationPipelineProductAccess> FreezeProductAccesses(
            IEnumerable<SimulationPipelineProductAccess> source)
        {
            var values = source == null ? new List<SimulationPipelineProductAccess>() : new List<SimulationPipelineProductAccess>(source);
            values.Sort((left, right) =>
            {
                int product = left.Product.ProductId.CompareTo(right.Product.ProductId);
                return product != 0 ? product : left.Access.CompareTo(right.Access);
            });
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i - 1].Product.ProductId.Equals(values[i].Product.ProductId) &&
                    values[i - 1].Access == values[i].Access)
                {
                    throw new ArgumentException("Pass descriptor contains duplicate Product access.", nameof(source));
                }
            }
            return values.AsReadOnly();
        }

        public IReadOnlyList<SimulationPipelinePortRequirement> GetPortRequirements(SimulationPipelineBindingPortRole role)
        {
            if (!Enum.IsDefined(typeof(SimulationPipelineBindingPortRole), role))
                throw new ArgumentOutOfRangeException(nameof(role));
            var values = new List<SimulationPipelinePortRequirement>();
            for (int i = 0; i < m_PortRequirements.Count; i++)
            {
                if (m_PortRequirements[i].Role == role)
                    values.Add(m_PortRequirements[i]);
            }
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SimulationPipelinePortRequirement> FreezePortRequirements(
            IEnumerable<SimulationPipelinePortRequirement> source)
        {
            var values = source == null ? new List<SimulationPipelinePortRequirement>() : new List<SimulationPipelinePortRequirement>(source);
            values.Sort((left, right) =>
            {
                int role = left.Role.CompareTo(right.Role);
                return role != 0 ? role : string.CompareOrdinal(left.PortId, right.PortId);
            });
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i - 1].Role == values[i].Role &&
                    string.Equals(values[i - 1].PortId, values[i].PortId, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Pass descriptor contains duplicate binding port requirement.", nameof(source));
                }
            }
            return values.AsReadOnly();
        }

        StableHash ComputeDescriptorHash()
        {
            var values = new List<string>
            {
                "simulation-pipeline-pass/1",
                PassId.Value,
                ImplementationVersion.Value,
                ((int)Phase).ToString(CultureInfo.InvariantCulture),
                ConfigurationHash.ToString(),
                NumericProfileId.Value,
                TargetAbiVersion.ToString(),
                BackendId,
                BackendSemanticVersion,
                checked((ulong)RequiredSolverCapabilities).ToString(CultureInfo.InvariantCulture),
                ((int)ExecutionSupport).ToString(CultureInfo.InvariantCulture),
                ((int)StateClass).ToString(CultureInfo.InvariantCulture),
                StateOwner
            };
            for (int i = 0; i < m_ProductAccesses.Count; i++)
            {
                SimulationPipelineProductAccess access = m_ProductAccesses[i];
                values.Add($"product:{access.Product.VersionedIdentity}:{(int)access.Access}:{access.Required}:{access.Product.CanonicalIdentity}:{access.Product.DiagnosticsShape}:{(int)access.Product.ProducerPhases}:{(int)access.Product.ConsumerPhases}:{(int)access.Product.Consumption}");
            }
            for (int i = 0; i < m_PortRequirements.Count; i++)
            {
                SimulationPipelinePortRequirement port = m_PortRequirements[i];
                values.Add($"port:{(int)port.Role}:{port.PortId}:{port.SchemaId}:{port.SchemaVersion}:{(int)port.Direction}");
            }
            return StableHash.Compute(values.ToArray());
        }
    }

    public sealed class SimulationPipelineDescriptor
    {
        readonly ReadOnlyCollection<SimulationPipelinePassDescriptor> m_Passes;
        readonly ReadOnlyCollection<SimulationPipelinePassDescriptor>[] m_Phases;

        public SimulationPipelineDescriptor(
            SimulationPipelineId pipelineId,
            SimulationPipelineRevision revision,
            SimulationPipelineSchemaVersion schemaVersion,
            IEnumerable<SimulationPipelinePassDescriptor> ingress,
            IEnumerable<SimulationPipelinePassDescriptor> schedule,
            IEnumerable<SimulationPipelinePassDescriptor> step,
            IEnumerable<SimulationPipelinePassDescriptor> egress)
        {
            if (!pipelineId.IsValid || !revision.IsValid || !schemaVersion.IsValid)
                throw new ArgumentException("Pipeline descriptor identity is incomplete.");
            PipelineId = pipelineId;
            Revision = revision;
            SchemaVersion = schemaVersion;
            m_Phases = new ReadOnlyCollection<SimulationPipelinePassDescriptor>[5];
            m_Phases[(int)SimulationPipelinePhase.Ingress] = FreezePhase(ingress, SimulationPipelinePhase.Ingress);
            m_Phases[(int)SimulationPipelinePhase.Schedule] = FreezePhase(schedule, SimulationPipelinePhase.Schedule);
            m_Phases[(int)SimulationPipelinePhase.Step] = FreezePhase(step, SimulationPipelinePhase.Step);
            m_Phases[(int)SimulationPipelinePhase.Egress] = FreezePhase(egress, SimulationPipelinePhase.Egress);
            var all = new List<SimulationPipelinePassDescriptor>();
            var identities = new HashSet<SimulationPipelinePassId>();
            for (int phase = 1; phase < m_Phases.Length; phase++)
            {
                ReadOnlyCollection<SimulationPipelinePassDescriptor> values = m_Phases[phase];
                for (int i = 0; i < values.Count; i++)
                {
                    if (!identities.Add(values[i].PassId))
                        throw new ArgumentException($"Pipeline contains duplicate PassId '{values[i].PassId}'.");
                    all.Add(values[i]);
                }
            }
            if (all.Count == 0)
                throw new ArgumentException("Pipeline descriptor has no Passes.");
            m_Passes = all.AsReadOnly();
            DescriptorHash = ComputeDescriptorHash();
        }

        public SimulationPipelineId PipelineId { get; }
        public SimulationPipelineRevision Revision { get; }
        public SimulationPipelineSchemaVersion SchemaVersion { get; }
        public IReadOnlyList<SimulationPipelinePassDescriptor> Passes => m_Passes;
        public StableHash DescriptorHash { get; }
        public IReadOnlyList<SimulationPipelinePassDescriptor> GetPhase(SimulationPipelinePhase phase)
        {
            if (!Enum.IsDefined(typeof(SimulationPipelinePhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            return m_Phases[(int)phase];
        }

        static ReadOnlyCollection<SimulationPipelinePassDescriptor> FreezePhase(
            IEnumerable<SimulationPipelinePassDescriptor> source,
            SimulationPipelinePhase phase)
        {
            var values = source == null ? new List<SimulationPipelinePassDescriptor>() : new List<SimulationPipelinePassDescriptor>(source);
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null || values[i].Phase != phase)
                    throw new ArgumentException($"Pipeline phase '{phase}' contains an invalid Pass descriptor.", nameof(source));
            }
            return values.AsReadOnly();
        }

        StableHash ComputeDescriptorHash()
        {
            var values = new string[m_Passes.Count + 4];
            values[0] = "simulation-pipeline-descriptor/1";
            values[1] = PipelineId.Value;
            values[2] = Revision.Value;
            values[3] = SchemaVersion.ToString();
            for (int i = 0; i < m_Passes.Count; i++)
                values[i + 4] = $"{(int)m_Passes[i].Phase}:{i}:{m_Passes[i].DescriptorHash}";
            return StableHash.Compute(values);
        }
    }
}
