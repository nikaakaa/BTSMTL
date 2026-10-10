using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public enum SimulationPortDirection : byte
    {
        Input = 1,
        Output = 2,
        Bidirectional = 3
    }

    public readonly struct SimulationPortDescriptor : IEquatable<SimulationPortDescriptor>
    {
        public SimulationPortDescriptor(
            string portId,
            string schemaId,
            int schemaVersion,
            SimulationPortDirection direction,
            string ownerComponentId,
            StableHash configurationHash)
        {
            if (schemaVersion <= 0 || !Enum.IsDefined(typeof(SimulationPortDirection), direction) || !configurationHash.IsValid)
                throw new ArgumentException("Port descriptor is incomplete.");
            PortId = SimulationIdentity.Require(portId, nameof(portId));
            SchemaId = SimulationIdentity.Require(schemaId, nameof(schemaId));
            SchemaVersion = schemaVersion;
            Direction = direction;
            OwnerComponentId = SimulationIdentity.Require(ownerComponentId, nameof(ownerComponentId));
            ConfigurationHash = configurationHash;
        }

        public string PortId { get; }
        public string SchemaId { get; }
        public int SchemaVersion { get; }
        public SimulationPortDirection Direction { get; }
        public string OwnerComponentId { get; }
        public StableHash ConfigurationHash { get; }

        public static SimulationPortDescriptor CreateSource(
            SimulationPipelinePortRequirement requirement,
            SimulationComponentIdentity source)
        {
            if (requirement.Role != SimulationPipelineBindingPortRole.Source)
                throw new ArgumentException("Source port requirement must use the Source binding role.", nameof(requirement));
            if (!source.IsValid || source.Role != SimulationComponentRole.SessionSource)
                throw new ArgumentException("Session Source identity is invalid.", nameof(source));
            return new SimulationPortDescriptor(
                requirement.PortId,
                requirement.SchemaId,
                requirement.SchemaVersion,
                requirement.Direction,
                source.ComponentId,
                StableHash.Compute(
                    "network-model-source-authoring-port/1",
                    source.ConfigurationHash.ToString(),
                    requirement.PortId,
                    requirement.SchemaId,
                    requirement.SchemaVersion.ToString(),
                    ((int)requirement.Direction).ToString()));
        }

        public bool Equals(SimulationPortDescriptor other) => string.Equals(PortId, other.PortId, StringComparison.Ordinal) && ConfigurationHash.Equals(other.ConfigurationHash);
        public override bool Equals(object obj) => obj is SimulationPortDescriptor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(PortId, ConfigurationHash);
    }

    public enum SimulationInitialStateKind : byte
    {
        Character = 1,
        World = 2,
        Pipeline = 3
    }

    public readonly struct SimulationInitialStateIdentity
    {
        public SimulationInitialStateIdentity(
            SimulationInitialStateKind kind,
            string schemaId,
            int schemaVersion,
            StableHash stateHash,
            ActorId actorId = default)
        {
            if (!Enum.IsDefined(typeof(SimulationInitialStateKind), kind) || schemaVersion <= 0 || !stateHash.IsValid)
                throw new ArgumentException("Initial state identity is incomplete.");
            if (kind == SimulationInitialStateKind.Character != actorId.IsValid)
                throw new ArgumentException("Only Character initial state requires an ActorId.", nameof(actorId));
            Kind = kind;
            SchemaId = SimulationIdentity.Require(schemaId, nameof(schemaId));
            SchemaVersion = schemaVersion;
            StateHash = stateHash;
            ActorId = actorId;
        }

        public SimulationInitialStateKind Kind { get; }
        public string SchemaId { get; }
        public int SchemaVersion { get; }
        public StableHash StateHash { get; }
        public ActorId ActorId { get; }
    }

    public readonly struct SimulationOutputRouteDescriptor
    {
        public SimulationOutputRouteDescriptor(string routeId, string schemaId, int schemaVersion, ActorId actorId, StableHash configurationHash)
        {
            if (schemaVersion <= 0 || !actorId.IsValid || !configurationHash.IsValid)
                throw new ArgumentException("Output route descriptor is incomplete.");
            RouteId = SimulationIdentity.Require(routeId, nameof(routeId));
            SchemaId = SimulationIdentity.Require(schemaId, nameof(schemaId));
            SchemaVersion = schemaVersion;
            ActorId = actorId;
            ConfigurationHash = configurationHash;
        }

        public string RouteId { get; }
        public string SchemaId { get; }
        public int SchemaVersion { get; }
        public ActorId ActorId { get; }
        public StableHash ConfigurationHash { get; }
    }

    public readonly struct SimulationCompiledPipelinePlanIdentity
    {
        public SimulationCompiledPipelinePlanIdentity(SimulationPipelineIdentity pipeline, StableHash planHash, int passCount)
        {
            if (!pipeline.IsValid || !planHash.IsValid || passCount <= 0)
                throw new ArgumentException("Compiled Pipeline plan identity is incomplete.");
            Pipeline = pipeline;
            PlanHash = planHash;
            PassCount = passCount;
        }

        public SimulationPipelineIdentity Pipeline { get; }
        public StableHash PlanHash { get; }
        public int PassCount { get; }
    }

    public sealed class SimulationSessionLaunchPlan
    {
        readonly ReadOnlyCollection<SimulationPortDescriptor> m_SourcePorts;
        readonly ReadOnlyCollection<SimulationInitialStateIdentity> m_InitialStates;
        readonly ReadOnlyCollection<SimulationOutputRouteDescriptor> m_OutputRoutes;

        public SimulationSessionLaunchPlan(
            SimulationSessionCompositionDescriptor descriptor,
            SimulationCompiledPipelinePlanIdentity compiledPipeline,
            IEnumerable<SimulationPortDescriptor> sourcePorts,
            IEnumerable<SimulationInitialStateIdentity> initialStates,
            IEnumerable<SimulationOutputRouteDescriptor> outputRoutes,
            SimulationComponentIdentity diagnosticsIdentity)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            if (!compiledPipeline.Pipeline.Equals(descriptor.Pipeline))
                throw new ArgumentException("Compiled Pipeline identity does not match the composition.", nameof(compiledPipeline));
            if (!diagnosticsIdentity.IsValid || diagnosticsIdentity.Role != SimulationComponentRole.Diagnostics)
                throw new ArgumentException("Diagnostics identity is required.", nameof(diagnosticsIdentity));
            CompiledPipeline = compiledPipeline;
            DiagnosticsIdentity = diagnosticsIdentity;
            m_SourcePorts = FreezeUnique(sourcePorts, value => value.PortId, nameof(sourcePorts));
            m_InitialStates = FreezeInitialStates(initialStates, descriptor.Roster);
            m_OutputRoutes = FreezeOutputRoutes(outputRoutes, descriptor.Roster);
        }

        public SimulationSessionCompositionDescriptor Descriptor { get; }
        public SimulationCompiledPipelinePlanIdentity CompiledPipeline { get; }
        public SimulationExecutionTargetManifest ExecutionTarget => Descriptor.ExecutionTarget;
        public SimulationComponentIdentity ExecutionBackend => Descriptor.ExecutionBackend;
        public SimulationComponentIdentity SessionSource => Descriptor.SessionSource;
        public SimulationComponentIdentity WorldSolver => Descriptor.WorldSolver;
        public SimulationComponentIdentity SnapshotCodec => Descriptor.SnapshotCodec;
        public SimulationComponentIdentity Committer => Descriptor.Committer;
        public GameplayContentHash GameplayContentHash => Descriptor.GameplayContentHash;
        public SimulationActorRosterDescriptor Roster => Descriptor.Roster;
        public IReadOnlyList<SimulationPortDescriptor> SourcePorts => m_SourcePorts;
        public IReadOnlyList<SimulationInitialStateIdentity> InitialStates => m_InitialStates;
        public IReadOnlyList<SimulationOutputRouteDescriptor> OutputRoutes => m_OutputRoutes;
        public SimulationComponentIdentity DiagnosticsIdentity { get; }

        static ReadOnlyCollection<T> FreezeUnique<T>(IEnumerable<T> source, Func<T, string> identity, string parameter)
        {
            var values = source == null ? new List<T>() : new List<T>(source);
            values.Sort((left, right) => string.CompareOrdinal(identity(left), identity(right)));
            for (int i = 0; i < values.Count; i++)
            {
                string current = identity(values[i]);
                if (string.IsNullOrEmpty(current) || i > 0 && string.Equals(identity(values[i - 1]), current, StringComparison.Ordinal))
                    throw new ArgumentException("Launch plan contains an invalid or duplicate identity.", parameter);
            }
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SimulationInitialStateIdentity> FreezeInitialStates(
            IEnumerable<SimulationInitialStateIdentity> source,
            SimulationActorRosterDescriptor roster)
        {
            var values = source == null ? new List<SimulationInitialStateIdentity>() : new List<SimulationInitialStateIdentity>(source);
            int worldCount = 0;
            int pipelineCount = 0;
            var actors = new HashSet<ActorId>();
            for (int i = 0; i < values.Count; i++)
            {
                SimulationInitialStateIdentity state = values[i];
                if (state.Kind == SimulationInitialStateKind.World)
                    worldCount++;
                else if (state.Kind == SimulationInitialStateKind.Pipeline)
                    pipelineCount++;
                else if (!actors.Add(state.ActorId))
                    throw new ArgumentException($"Launch plan contains duplicate Character initial state '{state.ActorId}'.", nameof(source));
            }
            if (worldCount != 1 || pipelineCount != 1 || actors.Count != roster.Actors.Count)
                throw new ArgumentException("Launch plan requires one Character state per Actor and exactly one World and Pipeline state.", nameof(source));
            for (int i = 0; i < roster.Actors.Count; i++)
            {
                if (!actors.Contains(roster.Actors[i]))
                    throw new ArgumentException($"Launch plan is missing Character initial state '{roster.Actors[i]}'.", nameof(source));
            }
            values.Sort((left, right) =>
            {
                int kind = left.Kind.CompareTo(right.Kind);
                return kind != 0 ? kind : left.ActorId.CompareTo(right.ActorId);
            });
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SimulationOutputRouteDescriptor> FreezeOutputRoutes(
            IEnumerable<SimulationOutputRouteDescriptor> source,
            SimulationActorRosterDescriptor roster)
        {
            ReadOnlyCollection<SimulationOutputRouteDescriptor> routes = FreezeUnique(source, value => value.RouteId, nameof(source));
            var actorSet = new HashSet<ActorId>(roster.Actors);
            for (int i = 0; i < routes.Count; i++)
            {
                if (!actorSet.Contains(routes[i].ActorId))
                    throw new ArgumentException($"Output route targets unknown Actor '{routes[i].ActorId}'.", nameof(source));
            }
            return routes;
        }
    }
}
