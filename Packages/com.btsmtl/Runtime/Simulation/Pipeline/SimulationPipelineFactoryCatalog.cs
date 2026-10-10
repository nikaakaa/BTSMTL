using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public sealed class SimulationPipelinePassFactoryDescriptor
    {
        public SimulationPipelinePassFactoryDescriptor(
            SimulationPipelinePassFactoryIdentity identity,
            SimulationPipelinePhase phase,
            string backendId,
            string backendSemanticVersion,
            StableHash supportedConfigurationHash,
            SimulationPipelineExecutionSupport executionSupport,
            bool deterministic,
            bool supportsSnapshotCapture,
            bool supportsSnapshotRestore,
            bool supportsReconstruction,
            string stateSchemaId = "",
            int stateSchemaVersion = 0)
        {
            if (!identity.PassId.IsValid || !identity.ImplementationVersion.IsValid ||
                !Enum.IsDefined(typeof(SimulationPipelinePhase), phase) || !supportedConfigurationHash.IsValid ||
                (executionSupport & SimulationPipelineExecutionSupport.Forward) == 0)
            {
                throw new ArgumentException("Pass factory descriptor is incomplete.");
            }
            Identity = identity;
            Phase = phase;
            BackendId = SimulationIdentity.Require(backendId, nameof(backendId));
            BackendSemanticVersion = SimulationIdentity.Require(backendSemanticVersion, nameof(backendSemanticVersion));
            SupportedConfigurationHash = supportedConfigurationHash;
            ExecutionSupport = executionSupport;
            Deterministic = deterministic;
            SupportsSnapshotCapture = supportsSnapshotCapture;
            SupportsSnapshotRestore = supportsSnapshotRestore;
            SupportsReconstruction = supportsReconstruction;
            bool hasSnapshotState = supportsSnapshotCapture || supportsSnapshotRestore;
            if (hasSnapshotState != (!string.IsNullOrWhiteSpace(stateSchemaId) && stateSchemaVersion > 0))
                throw new ArgumentException("Snapshot-capable factory requires one explicit state schema; other factories cannot declare one.");
            StateSchemaId = stateSchemaId?.Trim() ?? string.Empty;
            StateSchemaVersion = stateSchemaVersion;
        }

        public SimulationPipelinePassFactoryIdentity Identity { get; }
        public SimulationPipelinePhase Phase { get; }
        public string BackendId { get; }
        public string BackendSemanticVersion { get; }
        public StableHash SupportedConfigurationHash { get; }
        public SimulationPipelineExecutionSupport ExecutionSupport { get; }
        public bool Deterministic { get; }
        public bool SupportsSnapshotCapture { get; }
        public bool SupportsSnapshotRestore { get; }
        public bool SupportsReconstruction { get; }
        public string StateSchemaId { get; }
        public int StateSchemaVersion { get; }
    }

    public sealed class SimulationPipelinePassFactoryCatalog
    {
        readonly ReadOnlyCollection<SimulationPipelinePassFactoryDescriptor> m_Factories;
        readonly ReadOnlyCollection<SimulationPipelineProductContract> m_Products;

        public SimulationPipelinePassFactoryCatalog(
            SimulationComponentIdentity backend,
            IEnumerable<SimulationPipelinePassFactoryDescriptor> factories,
            IEnumerable<SimulationPipelineProductContract> products)
        {
            if (!backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Execution Backend identity is required.", nameof(backend));
            Backend = backend;
            var factoryValues = factories == null
                ? new List<SimulationPipelinePassFactoryDescriptor>()
                : new List<SimulationPipelinePassFactoryDescriptor>(factories);
            factoryValues.Sort((left, right) =>
            {
                int pass = left.Identity.PassId.CompareTo(right.Identity.PassId);
                return pass != 0
                    ? pass
                    : string.CompareOrdinal(left.Identity.ImplementationVersion.Value, right.Identity.ImplementationVersion.Value);
            });
            for (int i = 0; i < factoryValues.Count; i++)
            {
                SimulationPipelinePassFactoryDescriptor factory = factoryValues[i] ??
                    throw new ArgumentException("Factory catalog contains a missing descriptor.", nameof(factories));
                if (!string.Equals(factory.BackendId, backend.ComponentId, StringComparison.Ordinal) ||
                    !string.Equals(factory.BackendSemanticVersion, backend.SemanticVersion, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Factory '{factory.Identity.PassId}' targets another Backend.", nameof(factories));
                }
                if (i > 0 && factoryValues[i - 1].Identity.PassId.Equals(factory.Identity.PassId) &&
                    factoryValues[i - 1].Identity.ImplementationVersion.Equals(factory.Identity.ImplementationVersion))
                {
                    throw new ArgumentException($"Factory catalog contains duplicate identity '{factory.Identity.PassId}@{factory.Identity.ImplementationVersion}'.", nameof(factories));
                }
            }
            var productValues = products == null
                ? new List<SimulationPipelineProductContract>()
                : new List<SimulationPipelineProductContract>(products);
            productValues.Sort((left, right) => left.ProductId.CompareTo(right.ProductId));
            for (int i = 0; i < productValues.Count; i++)
            {
                if (productValues[i] == null || i > 0 && productValues[i - 1].ProductId.Equals(productValues[i].ProductId))
                    throw new ArgumentException("Factory catalog contains a missing or duplicate Product contract.", nameof(products));
            }
            m_Factories = factoryValues.AsReadOnly();
            m_Products = productValues.AsReadOnly();
        }

        public SimulationComponentIdentity Backend { get; }
        public IReadOnlyList<SimulationPipelinePassFactoryDescriptor> Factories => m_Factories;
        public IReadOnlyList<SimulationPipelineProductContract> Products => m_Products;

        public bool TryGetProduct(SimulationPipelineProductId productId, out SimulationPipelineProductContract product)
        {
            for (int i = 0; i < m_Products.Count; i++)
            {
                if (m_Products[i].ProductId.Equals(productId))
                {
                    product = m_Products[i];
                    return true;
                }
            }
            product = null;
            return false;
        }

        public IReadOnlyList<SimulationPipelinePassFactoryDescriptor> FindFactories(SimulationPipelinePassId passId)
        {
            var values = new List<SimulationPipelinePassFactoryDescriptor>();
            for (int i = 0; i < m_Factories.Count; i++)
            {
                if (m_Factories[i].Identity.PassId.Equals(passId))
                    values.Add(m_Factories[i]);
            }
            return values.AsReadOnly();
        }
    }
}
