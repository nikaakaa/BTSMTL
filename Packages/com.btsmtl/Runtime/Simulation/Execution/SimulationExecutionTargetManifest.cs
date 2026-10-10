using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ThirdPersonSimulation
{
    public sealed class SimulationExecutionTargetManifest
    {
        readonly ReadOnlyCollection<SimulationOperationCode> m_SupportedOperations;

        public SimulationExecutionTargetManifest(
            string backendIdentity,
            SimulationNumericProfile numericProfile,
            OperationSetVersion operationSetVersion,
            IReadOnlyList<SimulationOperationCode> supportedOperations)
        {
            BackendIdentity = SimulationIdentity.Require(backendIdentity, nameof(backendIdentity));
            if (!numericProfile.IsValid)
                throw new ArgumentException("Execution Target Numeric Profile is incomplete.", nameof(numericProfile));
            NumericProfile = numericProfile;
            OperationSetVersion = operationSetVersion;
            var operations = supportedOperations == null
                ? Array.Empty<SimulationOperationCode>()
                : supportedOperations.ToArray();
            GameplayAbilityOperationSet.RequireCompleteBackend(operationSetVersion, operations, BackendIdentity);
            m_SupportedOperations = Array.AsReadOnly(operations);
            Identity = StableHash.Compute(
                "simulation-execution-target/1",
                BackendIdentity,
                NumericProfile.Id.Value,
                NumericProfile.AbiVersion.Value.ToString(),
                OperationSetVersion.Value);
        }

        public string BackendIdentity { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public StableHash Identity { get; }
        public IReadOnlyList<SimulationOperationCode> SupportedOperations => m_SupportedOperations;
    }
}
