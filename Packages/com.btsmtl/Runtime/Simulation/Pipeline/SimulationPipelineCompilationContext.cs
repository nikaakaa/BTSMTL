using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public sealed class SimulationPipelineCompilationContext
    {
        readonly ReadOnlyCollection<SimulationPortDescriptor> m_SourcePorts;
        readonly ReadOnlyCollection<SimulationPipelinePassRequirement> m_SourceRequiredPasses;
        readonly ReadOnlyCollection<SimulationPipelinePortRequirement> m_SourceRequiredPorts;

        public SimulationPipelineCompilationContext(
            SimulationExecutionTargetManifest executionTarget,
            WorldCapability executionTargetRequiredWorldCapabilities,
            SimulationPipelineExecutionSupport executionTargetExecutionSupport,
            bool executionTargetDeterministic,
            SimulationComponentIdentity backend,
            SimulationPipelineExecutionSupport backendExecutionSupport,
            bool backendDeterministic,
            SimulationComponentIdentity sessionSource,
            IEnumerable<SimulationPortDescriptor> sourcePorts,
            WorldCapability sourceRequiredWorldCapabilities,
            IEnumerable<SimulationPipelinePassRequirement> sourceRequiredPasses,
            IEnumerable<SimulationPipelinePortRequirement> sourceRequiredPorts,
            SimulationComponentIdentity worldSolver,
            WorldCapability solverCapabilities,
            SimulationPipelineExecutionSupport solverExecutionSupport,
            bool solverDeterministic,
            SimulationComponentIdentity snapshotCodec,
            SimulationPipelineExecutionSupport snapshotExecutionSupport,
            bool snapshotDeterministic,
            SimulationPipelineExecutionSupport requiredExecutionSupport,
            bool requiresDeterministic)
        {
            ExecutionTarget = executionTarget ?? throw new ArgumentNullException(nameof(executionTarget));
            RequireRole(backend, SimulationComponentRole.ExecutionBackend, nameof(backend));
            RequireRole(sessionSource, SimulationComponentRole.SessionSource, nameof(sessionSource));
            RequireRole(worldSolver, SimulationComponentRole.WorldSolver, nameof(worldSolver));
            RequireRole(snapshotCodec, SimulationComponentRole.SnapshotCodec, nameof(snapshotCodec));
            if ((requiredExecutionSupport & SimulationPipelineExecutionSupport.Forward) == 0)
            {
                throw new ArgumentException("Pipeline compilation Target requirements are incomplete.");
            }
            ExecutionTargetRequiredWorldCapabilities = executionTargetRequiredWorldCapabilities;
            ExecutionTargetExecutionSupport = executionTargetExecutionSupport;
            ExecutionTargetDeterministic = executionTargetDeterministic;
            Backend = backend;
            BackendExecutionSupport = backendExecutionSupport;
            BackendDeterministic = backendDeterministic;
            SessionSource = sessionSource;
            m_SourcePorts = FreezePorts(sourcePorts);
            SourceRequiredWorldCapabilities = sourceRequiredWorldCapabilities;
            m_SourceRequiredPasses = FreezePassRequirements(sourceRequiredPasses);
            m_SourceRequiredPorts = FreezePortRequirements(sourceRequiredPorts);
            WorldSolver = worldSolver;
            SolverCapabilities = solverCapabilities;
            SolverExecutionSupport = solverExecutionSupport;
            SolverDeterministic = solverDeterministic;
            SnapshotCodec = snapshotCodec;
            SnapshotExecutionSupport = snapshotExecutionSupport;
            SnapshotDeterministic = snapshotDeterministic;
            RequiredExecutionSupport = requiredExecutionSupport;
            RequiresDeterministic = requiresDeterministic;
        }

        public SimulationExecutionTargetManifest ExecutionTarget { get; }
        public NumericProfileId NumericProfileId => ExecutionTarget.NumericProfile.Id;
        public TargetAbiVersion TargetAbiVersion => ExecutionTarget.NumericProfile.AbiVersion;
        public WorldCapability ExecutionTargetRequiredWorldCapabilities { get; }
        public SimulationPipelineExecutionSupport ExecutionTargetExecutionSupport { get; }
        public bool ExecutionTargetDeterministic { get; }
        public SimulationComponentIdentity Backend { get; }
        public SimulationPipelineExecutionSupport BackendExecutionSupport { get; }
        public bool BackendDeterministic { get; }
        public SimulationComponentIdentity SessionSource { get; }
        public IReadOnlyList<SimulationPortDescriptor> SourcePorts => m_SourcePorts;
        public WorldCapability SourceRequiredWorldCapabilities { get; }
        public IReadOnlyList<SimulationPipelinePassRequirement> SourceRequiredPasses => m_SourceRequiredPasses;
        public IReadOnlyList<SimulationPipelinePortRequirement> SourceRequiredPorts => m_SourceRequiredPorts;
        public SimulationComponentIdentity WorldSolver { get; }
        public WorldCapability SolverCapabilities { get; }
        public SimulationPipelineExecutionSupport SolverExecutionSupport { get; }
        public bool SolverDeterministic { get; }
        public SimulationComponentIdentity SnapshotCodec { get; }
        public SimulationPipelineExecutionSupport SnapshotExecutionSupport { get; }
        public bool SnapshotDeterministic { get; }
        public SimulationPipelineExecutionSupport RequiredExecutionSupport { get; }
        public bool RequiresDeterministic { get; }

        static ReadOnlyCollection<SimulationPortDescriptor> FreezePorts(IEnumerable<SimulationPortDescriptor> source)
        {
            var values = source == null ? new List<SimulationPortDescriptor>() : new List<SimulationPortDescriptor>(source);
            values.Sort((left, right) => string.CompareOrdinal(left.PortId, right.PortId));
            for (int i = 1; i < values.Count; i++)
            {
                if (string.Equals(values[i - 1].PortId, values[i].PortId, StringComparison.Ordinal))
                    throw new ArgumentException("Compilation context contains duplicate Source port identity.", nameof(source));
            }
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SimulationPipelinePassRequirement> FreezePassRequirements(
            IEnumerable<SimulationPipelinePassRequirement> source)
        {
            var values = source == null
                ? new List<SimulationPipelinePassRequirement>()
                : new List<SimulationPipelinePassRequirement>(source);
            values.Sort((left, right) => left.PassId.CompareTo(right.PassId));
            for (int i = 0; i < values.Count; i++)
            {
                if (!values[i].PassId.IsValid || !values[i].ImplementationVersion.IsValid ||
                    !Enum.IsDefined(typeof(SimulationPipelinePhase), values[i].Phase) ||
                    i > 0 && values[i - 1].PassId.Equals(values[i].PassId))
                {
                    throw new ArgumentException("Compilation context contains an invalid or duplicate Source-required Pass.", nameof(source));
                }
            }
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SimulationPipelinePortRequirement> FreezePortRequirements(
            IEnumerable<SimulationPipelinePortRequirement> source)
        {
            var values = source == null
                ? new List<SimulationPipelinePortRequirement>()
                : new List<SimulationPipelinePortRequirement>(source);
            values.Sort((left, right) => string.CompareOrdinal(left.PortId, right.PortId));
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i].Role != SimulationPipelineBindingPortRole.Source ||
                    i > 0 && string.Equals(values[i - 1].PortId, values[i].PortId, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Compilation context contains an invalid or duplicate Source-required port.", nameof(source));
                }
            }
            return values.AsReadOnly();
        }

        static void RequireRole(SimulationComponentIdentity identity, SimulationComponentRole role, string parameter)
        {
            if (!identity.IsValid || identity.Role != role)
                throw new ArgumentException($"Component role must be {role}.", parameter);
        }
    }
}
