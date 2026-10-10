using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ThirdPersonSimulation
{
    [Flags]
    public enum SimulationExecutionBackendCapability : ushort
    {
        None = 0,
        PhasePassExecution = 1 << 0,
        MultiStepTransaction = 1 << 1,
        AtomicStatePublish = 1 << 2,
        AtomicSessionRestore = 1 << 3,
        PipelineStateSnapshot = 1 << 4,
        SolverReconstruction = 1 << 5,
        FailStopCommit = 1 << 6,
        DeterministicExecution = 1 << 7
    }

    public readonly struct SimulationExecutionBackendTargetSupport : IEquatable<SimulationExecutionBackendTargetSupport>
    {
        public SimulationExecutionBackendTargetSupport(
            NumericProfileId numericProfileId,
            TargetAbiVersion targetAbiVersion,
            SimulationPipelineSchemaVersion pipelineSchemaVersion,
            SimulationPipelineExecutionSupport executionSupport,
            bool deterministic)
        {
            if (!numericProfileId.IsValid || !targetAbiVersion.IsValid || !pipelineSchemaVersion.IsValid ||
                (executionSupport & SimulationPipelineExecutionSupport.Forward) == 0)
            {
                throw new ArgumentException("Execution Backend Target support is incomplete.");
            }
            NumericProfileId = numericProfileId;
            TargetAbiVersion = targetAbiVersion;
            PipelineSchemaVersion = pipelineSchemaVersion;
            ExecutionSupport = executionSupport;
            Deterministic = deterministic;
        }

        public NumericProfileId NumericProfileId { get; }
        public TargetAbiVersion TargetAbiVersion { get; }
        public SimulationPipelineSchemaVersion PipelineSchemaVersion { get; }
        public SimulationPipelineExecutionSupport ExecutionSupport { get; }
        public bool Deterministic { get; }

        public bool Equals(SimulationExecutionBackendTargetSupport other)
        {
            return NumericProfileId.Equals(other.NumericProfileId) &&
                   TargetAbiVersion.Equals(other.TargetAbiVersion) &&
                   PipelineSchemaVersion.Equals(other.PipelineSchemaVersion) &&
                   ExecutionSupport == other.ExecutionSupport && Deterministic == other.Deterministic;
        }

        public override bool Equals(object obj) => obj is SimulationExecutionBackendTargetSupport other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(NumericProfileId, TargetAbiVersion, PipelineSchemaVersion, (int)ExecutionSupport, Deterministic);
    }

    public sealed class SimulationExecutionBackendDescriptor
    {
        readonly ReadOnlyCollection<SimulationExecutionBackendTargetSupport> m_Targets;

        public SimulationExecutionBackendDescriptor(
            string backendId,
            string semanticVersion,
            SimulationExecutionBackendCapability capabilities,
            IEnumerable<SimulationExecutionBackendTargetSupport> targets)
        {
            BackendId = SimulationIdentity.Require(backendId, nameof(backendId));
            SemanticVersion = SimulationIdentity.Require(semanticVersion, nameof(semanticVersion));
            SimulationExecutionBackendCapability required =
                SimulationExecutionBackendCapability.PhasePassExecution |
                SimulationExecutionBackendCapability.MultiStepTransaction |
                SimulationExecutionBackendCapability.AtomicStatePublish |
                SimulationExecutionBackendCapability.FailStopCommit;
            if ((capabilities & required) != required)
                throw new ArgumentException("Execution Backend is missing required Session transaction capabilities.", nameof(capabilities));
            var values = targets == null
                ? new List<SimulationExecutionBackendTargetSupport>()
                : new List<SimulationExecutionBackendTargetSupport>(targets);
            values.Sort((left, right) =>
            {
                int profile = string.CompareOrdinal(left.NumericProfileId.Value, right.NumericProfileId.Value);
                if (profile != 0)
                    return profile;
                int abi = left.TargetAbiVersion.Value.CompareTo(right.TargetAbiVersion.Value);
                return abi != 0 ? abi : left.PipelineSchemaVersion.Value.CompareTo(right.PipelineSchemaVersion.Value);
            });
            if (values.Count == 0)
                throw new ArgumentException("Execution Backend must declare at least one Target ABI.", nameof(targets));
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i - 1].NumericProfileId.Equals(values[i].NumericProfileId) &&
                    values[i - 1].TargetAbiVersion.Equals(values[i].TargetAbiVersion) &&
                    values[i - 1].PipelineSchemaVersion.Equals(values[i].PipelineSchemaVersion))
                {
                    throw new ArgumentException("Execution Backend contains duplicate Target support.", nameof(targets));
                }
            }
            Capabilities = capabilities;
            m_Targets = values.AsReadOnly();
            Identity = new SimulationComponentIdentity(
                SimulationComponentRole.ExecutionBackend,
                BackendId,
                SemanticVersion,
                ComputeConfigurationHash());
        }

        public string BackendId { get; }
        public string SemanticVersion { get; }
        public SimulationExecutionBackendCapability Capabilities { get; }
        public SimulationComponentIdentity Identity { get; }
        public IReadOnlyList<SimulationExecutionBackendTargetSupport> Targets => m_Targets;

        public SimulationExecutionBackendTargetSupport RequireTarget(
            NumericProfileId numericProfileId,
            TargetAbiVersion targetAbiVersion,
            SimulationPipelineSchemaVersion pipelineSchemaVersion)
        {
            for (int i = 0; i < m_Targets.Count; i++)
            {
                SimulationExecutionBackendTargetSupport target = m_Targets[i];
                if (target.NumericProfileId.Equals(numericProfileId) &&
                    target.TargetAbiVersion.Equals(targetAbiVersion) &&
                    target.PipelineSchemaVersion.Equals(pipelineSchemaVersion))
                {
                    return target;
                }
            }
            throw new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Composition,
                "backend_target_unsupported",
                $"Execution Backend '{BackendId}@{SemanticVersion}' does not support Target '{numericProfileId}/abi{targetAbiVersion}/pipeline{pipelineSchemaVersion}'.",
                Identity.ToString()));
        }

        StableHash ComputeConfigurationHash()
        {
            var values = new string[m_Targets.Count + 4];
            values[0] = "simulation-execution-backend/1";
            values[1] = BackendId;
            values[2] = SemanticVersion;
            values[3] = checked((ulong)Capabilities).ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < m_Targets.Count; i++)
            {
                SimulationExecutionBackendTargetSupport target = m_Targets[i];
                values[i + 4] = $"{target.NumericProfileId.Value}:{target.TargetAbiVersion}:{target.PipelineSchemaVersion}:{(int)target.ExecutionSupport}:{target.Deterministic}";
            }
            return StableHash.Compute(values);
        }
    }
}
