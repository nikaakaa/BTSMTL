using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public sealed class SimulationSessionSourceDescriptor
    {
        readonly ReadOnlyCollection<SimulationPipelinePassRequirement> m_RequiredPipelinePasses;
        readonly ReadOnlyCollection<SimulationPipelinePortRequirement> m_RequiredPipelineSourcePorts;

        public SimulationSessionSourceDescriptor(
            SimulationComponentIdentity identity,
            NumericProfileId numericProfileId,
            TargetAbiVersion targetAbiVersion,
            SimulationTickSourceKind outerTickKind,
            SimulationPipelineExecutionSupport executionSupport,
            bool deterministic,
            string requiredBackendId,
            SimulationPipelineId requiredPipelineId,
            SimulationComponentIdentity? model = null,
            SimulationComponentIdentity? endpoint = null,
            SimulationProtocolIdentity? protocol = null,
            WorldCapability requiredSolverCapabilities = WorldCapability.None,
            IEnumerable<SimulationPipelinePassRequirement> requiredPipelinePasses = null,
            IEnumerable<SimulationPipelinePortRequirement> requiredPipelineSourcePorts = null)
        {
            if (!identity.IsValid || identity.Role != SimulationComponentRole.SessionSource ||
                !numericProfileId.IsValid || !targetAbiVersion.IsValid ||
                !Enum.IsDefined(typeof(SimulationTickSourceKind), outerTickKind) ||
                (executionSupport & SimulationPipelineExecutionSupport.Forward) == 0 ||
                !requiredPipelineId.IsValid)
            {
                throw new ArgumentException("Session Source descriptor is incomplete.");
            }
            if (model.HasValue && (!model.Value.IsValid || model.Value.Role != SimulationComponentRole.Model))
                throw new ArgumentException("Session Source Model identity is invalid.", nameof(model));
            if (endpoint.HasValue && (!endpoint.Value.IsValid || endpoint.Value.Role != SimulationComponentRole.Endpoint))
                throw new ArgumentException("Session Source Endpoint identity is invalid.", nameof(endpoint));
            bool hasNetworkModel = model.HasValue || endpoint.HasValue || protocol.HasValue;
            if (hasNetworkModel && (!model.HasValue || !endpoint.HasValue || !protocol.HasValue || !protocol.Value.IsValid))
                throw new ArgumentException("Network Model Source requires Model, Endpoint, and Protocol identities together.");
            Identity = identity;
            NumericProfileId = numericProfileId;
            TargetAbiVersion = targetAbiVersion;
            OuterTickKind = outerTickKind;
            ExecutionSupport = executionSupport;
            Deterministic = deterministic;
            RequiredBackendId = SimulationIdentity.Require(requiredBackendId, nameof(requiredBackendId));
            RequiredPipelineId = requiredPipelineId;
            Model = model;
            Endpoint = endpoint;
            Protocol = protocol;
            RequiredSolverCapabilities = requiredSolverCapabilities;
            m_RequiredPipelinePasses = FreezePassRequirements(requiredPipelinePasses);
            m_RequiredPipelineSourcePorts = FreezePortRequirements(requiredPipelineSourcePorts);
            if (hasNetworkModel && (requiredSolverCapabilities == WorldCapability.None ||
                                    m_RequiredPipelinePasses.Count == 0 ||
                                    m_RequiredPipelineSourcePorts.Count == 0))
            {
                throw new ArgumentException("Network Model Source requires Solver capability, Pipeline Pass, and Source port requirements.");
            }
        }

        public SimulationComponentIdentity Identity { get; }
        public NumericProfileId NumericProfileId { get; }
        public TargetAbiVersion TargetAbiVersion { get; }
        public SimulationTickSourceKind OuterTickKind { get; }
        public SimulationPipelineExecutionSupport ExecutionSupport { get; }
        public bool Deterministic { get; }
        public string RequiredBackendId { get; }
        public SimulationPipelineId RequiredPipelineId { get; }
        public SimulationComponentIdentity? Model { get; }
        public SimulationComponentIdentity? Endpoint { get; }
        public SimulationProtocolIdentity? Protocol { get; }
        public WorldCapability RequiredSolverCapabilities { get; }
        public IReadOnlyList<SimulationPipelinePassRequirement> RequiredPipelinePasses => m_RequiredPipelinePasses;
        public IReadOnlyList<SimulationPipelinePortRequirement> RequiredPipelineSourcePorts => m_RequiredPipelineSourcePorts;

        static ReadOnlyCollection<SimulationPipelinePassRequirement> FreezePassRequirements(
            IEnumerable<SimulationPipelinePassRequirement> source)
        {
            var values = source == null
                ? new List<SimulationPipelinePassRequirement>()
                : new List<SimulationPipelinePassRequirement>(source);
            values.Sort((left, right) =>
            {
                int id = left.PassId.CompareTo(right.PassId);
                if (id != 0)
                    return id;
                int version = string.CompareOrdinal(left.ImplementationVersion.Value, right.ImplementationVersion.Value);
                return version != 0 ? version : left.Phase.CompareTo(right.Phase);
            });
            for (int i = 0; i < values.Count; i++)
            {
                if (!values[i].PassId.IsValid || !values[i].ImplementationVersion.IsValid ||
                    !Enum.IsDefined(typeof(SimulationPipelinePhase), values[i].Phase) ||
                    i > 0 && values[i - 1].PassId.Equals(values[i].PassId))
                {
                    throw new ArgumentException("Session Source contains an invalid or duplicate required Pipeline Pass.", nameof(source));
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
                    throw new ArgumentException("Session Source contains an invalid or duplicate required Source port.", nameof(source));
                }
            }
            return values.AsReadOnly();
        }
    }

    public sealed class SimulationWorldSolverDefinitionDescriptor
    {
        public SimulationWorldSolverDefinitionDescriptor(
            SimulationComponentIdentity identity,
            NumericProfileId numericProfileId,
            TargetAbiVersion targetAbiVersion,
            SolverImplementationId implementationId,
            string implementationVersion,
            WorldCapability capabilities,
            WorldFeature features,
            SimulationPipelineExecutionSupport executionSupport,
            bool deterministic)
        {
            if (!identity.IsValid || identity.Role != SimulationComponentRole.WorldSolver ||
                !numericProfileId.IsValid || !targetAbiVersion.IsValid || implementationId.Equals(default) ||
                capabilities == WorldCapability.None || (executionSupport & SimulationPipelineExecutionSupport.Forward) == 0)
            {
                throw new ArgumentException("World Solver Definition descriptor is incomplete.");
            }
            Identity = identity;
            NumericProfileId = numericProfileId;
            TargetAbiVersion = targetAbiVersion;
            ImplementationId = implementationId;
            ImplementationVersion = SimulationIdentity.Require(implementationVersion, nameof(implementationVersion));
            Capabilities = capabilities;
            Features = features;
            ExecutionSupport = executionSupport;
            Deterministic = deterministic;
        }

        public SimulationComponentIdentity Identity { get; }
        public NumericProfileId NumericProfileId { get; }
        public TargetAbiVersion TargetAbiVersion { get; }
        public SolverImplementationId ImplementationId { get; }
        public string ImplementationVersion { get; }
        public WorldCapability Capabilities { get; }
        public WorldFeature Features { get; }
        public SimulationPipelineExecutionSupport ExecutionSupport { get; }
        public bool Deterministic { get; }
    }

    public sealed class SimulationWorldIdentityDescriptor
    {
        public SimulationWorldIdentityDescriptor(
            SimulationWorldSolverDefinitionDescriptor solver,
            SimulationWorldId worldId,
            string mapId,
            WorldRevision worldRevision,
            StableHash worldConfigurationHash,
            StableHash navigationSurfaceArtifactHash,
            StableHash queryProfileHash)
        {
            if (solver == null || !worldId.IsValid || string.IsNullOrWhiteSpace(mapId) ||
                string.IsNullOrEmpty(worldRevision.Value) || !worldConfigurationHash.IsValid ||
                !navigationSurfaceArtifactHash.IsValid || !queryProfileHash.IsValid)
            {
                throw new ArgumentException("World identity descriptor is incomplete.");
            }
            Solver = solver;
            WorldId = worldId;
            MapId = SimulationIdentity.Require(mapId, nameof(mapId));
            WorldRevision = worldRevision;
            WorldConfigurationHash = worldConfigurationHash;
            NavigationSurfaceArtifactHash = navigationSurfaceArtifactHash;
            QueryProfileHash = queryProfileHash;
            IdentityHash = StableHash.Compute(
                "simulation-world-identity/1",
                solver.ImplementationId.Value,
                solver.ImplementationVersion,
                ((ulong)solver.Capabilities).ToString(),
                ((ulong)solver.Features).ToString(),
                worldId.Value,
                MapId,
                worldRevision.Value,
                worldConfigurationHash.Value,
                navigationSurfaceArtifactHash.Value,
                queryProfileHash.Value);
        }

        public SimulationWorldSolverDefinitionDescriptor Solver { get; }
        public SimulationWorldId WorldId { get; }
        public string MapId { get; }
        public WorldRevision WorldRevision { get; }
        public StableHash WorldConfigurationHash { get; }
        public StableHash NavigationSurfaceArtifactHash { get; }
        public StableHash QueryProfileHash { get; }
        public StableHash IdentityHash { get; }
    }
}
