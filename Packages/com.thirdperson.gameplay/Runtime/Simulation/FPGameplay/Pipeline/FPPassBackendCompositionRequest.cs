using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPPassBackendCompositionRequest
    {
        readonly ReadOnlyCollection<SimulationActorBinding> m_Roster;
        readonly ReadOnlyCollection<SimulationPortDescriptor> m_ExpectedSourcePorts;
        readonly ReadOnlyCollection<SimulationOutputRouteDescriptor> m_OutputRoutes;
        readonly ReadOnlyCollection<IDisposable> m_SourceResources;
        readonly ReadOnlyCollection<IDisposable> m_ActorResources;

        public FPPassBackendCompositionRequest(
            SimulationSessionCompositionDescriptor descriptor,
            SimulationExecutionBackendDescriptor backend,
            CompiledSimulationPipelinePlan compiledPipeline,
            FPCharacterRuntime characterRuntime,
            SimulationWorldStateSet initialState,
            SimulationPipelineInitialStateSource pipelineInitialState,
            SimulationRuntimePortSet sourcePorts,
            IEnumerable<SimulationPortDescriptor> expectedSourcePorts,
            IFPSimulationRestoreSource restoreSource,
            ICharacterWorldSolver solver,
            IFPSimulationSessionSnapshotProvider snapshotProvider,
            IFPSimulationCommitter committer,
            SimulationComponentIdentity diagnosticsIdentity,
            ISimulationDiagnosticsSink diagnostics,
            IEnumerable<SimulationOutputRouteDescriptor> outputRoutes,
            FPPipelinePassRuntimeFactoryCatalog passRuntimeFactories,
            FPPipelineProductRuntimeCatalog productRuntimeFactories,
            IEnumerable<IDisposable> sourceResources = null,
            IEnumerable<IDisposable> actorResources = null,
            GraphBatchExecutionOptions graphBatchOptions = default,
            IGraphBatchExecutionResource graphBatchResource = null)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            CompiledPipeline = compiledPipeline ?? throw new ArgumentNullException(nameof(compiledPipeline));
            CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            GraphBatchOptions = graphBatchOptions;
            GraphBatchResource = graphBatchResource;
            InitialState = initialState;
            PipelineInitialState = pipelineInitialState ?? throw new ArgumentNullException(nameof(pipelineInitialState));
            SourcePorts = sourcePorts ?? throw new ArgumentNullException(nameof(sourcePorts));
            RestoreSource = restoreSource;
            Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            SnapshotProvider = snapshotProvider;
            Committer = committer ?? throw new ArgumentNullException(nameof(committer));
            if (!diagnosticsIdentity.IsValid || diagnosticsIdentity.Role != SimulationComponentRole.Diagnostics)
                throw new ArgumentException("Diagnostics identity is invalid.", nameof(diagnosticsIdentity));
            DiagnosticsIdentity = diagnosticsIdentity;
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            PassRuntimeFactories = passRuntimeFactories ?? throw new ArgumentNullException(nameof(passRuntimeFactories));
            ProductRuntimeFactories = productRuntimeFactories ?? throw new ArgumentNullException(nameof(productRuntimeFactories));
            m_Roster = FreezeRoster(characterRuntime.Roster);
            m_ExpectedSourcePorts = FreezePorts(expectedSourcePorts);
            m_OutputRoutes = FreezeOutputRoutes(outputRoutes);
            m_SourceResources = FreezeResources(sourceResources, nameof(sourceResources));
            m_ActorResources = FreezeResources(actorResources, nameof(actorResources));
            Validate();
        }

        public SimulationSessionCompositionDescriptor Descriptor { get; }
        public SimulationExecutionBackendDescriptor Backend { get; }
        public CompiledSimulationPipelinePlan CompiledPipeline { get; }
        public FPCharacterRuntime CharacterRuntime { get; }
        public GraphBatchExecutionOptions GraphBatchOptions { get; }
        public IGraphBatchExecutionResource GraphBatchResource { get; }
        public IReadOnlyList<SimulationActorBinding> Roster => m_Roster;
        public SimulationWorldStateSet InitialState { get; }
        public SimulationPipelineInitialStateSource PipelineInitialState { get; }
        public SimulationRuntimePortSet SourcePorts { get; }
        public IReadOnlyList<SimulationPortDescriptor> ExpectedSourcePorts => m_ExpectedSourcePorts;
        public IFPSimulationRestoreSource RestoreSource { get; }
        public ICharacterWorldSolver Solver { get; }
        public IFPSimulationSessionSnapshotProvider SnapshotProvider { get; }
        public IFPSimulationCommitter Committer { get; }
        public SimulationComponentIdentity DiagnosticsIdentity { get; }
        public ISimulationDiagnosticsSink Diagnostics { get; }
        public IReadOnlyList<SimulationOutputRouteDescriptor> OutputRoutes => m_OutputRoutes;
        public FPPipelinePassRuntimeFactoryCatalog PassRuntimeFactories { get; }
        public FPPipelineProductRuntimeCatalog ProductRuntimeFactories { get; }
        public IReadOnlyList<IDisposable> SourceResources => m_SourceResources;
        public IReadOnlyList<IDisposable> ActorResources => m_ActorResources;

        void Validate()
        {
            if (!Backend.Identity.Equals(Descriptor.ExecutionBackend) ||
                !CompiledPipeline.Backend.Equals(Descriptor.ExecutionBackend) ||
                !CompiledPipeline.Identity.Equals(Descriptor.Pipeline) ||
                !CharacterRuntime.GameplayContentHash.Equals(Descriptor.GameplayContentHash) ||
                InitialState.WorldState.NumericProfile != Descriptor.ExecutionTarget.NumericProfile ||
                !InitialState.WorldState.SolverId.Equals(Solver.Descriptor.ImplementationId) ||
                !string.Equals(InitialState.WorldState.SolverVersion, Solver.Descriptor.Version, StringComparison.Ordinal))
            {
                throw Failure("fp_character_runtime_mismatch", "FP Character Runtime does not match the locked Session composition.");
            }
            SimulationExecutionBackendTargetSupport support = Backend.RequireTarget(
                Descriptor.NumericProfileId,
                Descriptor.TargetAbiVersion,
                Descriptor.Pipeline.SchemaVersion);
            if ((support.ExecutionSupport & CompiledPipeline.RequiredExecutionSupport) != CompiledPipeline.RequiredExecutionSupport ||
                CompiledPipeline.Deterministic && !support.Deterministic)
            {
                throw Failure("backend_execution_support_mismatch", "Execution Backend Target support does not cover the compiled Pipeline.");
            }
            ValidateRoster();
            ValidateSourcePorts();
            ValidateOutputRoutes();
            WorldCapability requiredCapabilities = CharacterRuntime.RequiredWorldCapabilities | CollectPassWorldCapabilities();
            if (!Solver.Descriptor.Supports(requiredCapabilities) ||
                (Solver.Descriptor.Capabilities & WorldCapability.Reconstructible) == 0)
            {
                throw Failure("world_solver_capability_missing", "World Solver does not cover the required Pipeline capabilities.");
            }
            SimulationComponentIdentity snapshotIdentity = SnapshotProvider == null ? default : SnapshotProvider.Identity;
            if (!snapshotIdentity.Equals(Descriptor.SnapshotProvider) || !Committer.Identity.Equals(Descriptor.Committer))
                throw Failure("session_output_component_mismatch", "Snapshot codec or Committer identity does not match the Session descriptor.");
            bool requiresRestore = (CompiledPipeline.RequiredExecutionSupport & SimulationPipelineExecutionSupport.Restore) != 0;
            if (requiresRestore != (RestoreSource != null))
                throw Failure("restore_source_mismatch", "Restore-capable Pipeline requires exactly one explicit restore Source port.");
            if (RestoreSource != null && !ContainsPortInstance(SourcePorts, RestoreSource))
                throw Failure("restore_source_port_missing", "Restore Source is not part of the locked Source port set.");
            ProductRuntimeFactories.RequireProducts(CompiledPipeline.Products);
            for (int i = 0; i < CompiledPipeline.Passes.Count; i++)
                PassRuntimeFactories.GetRequired(CompiledPipeline.Passes[i]);
        }

        WorldCapability CollectPassWorldCapabilities()
        {
            WorldCapability capabilities = WorldCapability.None;
            for (int i = 0; i < CompiledPipeline.Passes.Count; i++)
                capabilities |= CompiledPipeline.Passes[i].Descriptor.RequiredSolverCapabilities;
            return capabilities;
        }

        void ValidateRoster()
        {
            if (m_Roster.Count != Descriptor.Roster.Actors.Count || m_Roster.Count != InitialState.Actors.Count)
                throw Failure("actor_roster_count_mismatch", "Character Runtime roster does not match Session descriptor and initial state.");
            for (int i = 0; i < m_Roster.Count; i++)
            {
                SimulationActorBinding binding = m_Roster[i];
                if (binding.ActorId != Descriptor.Roster.Actors[i] ||
                    binding.ActorId != InitialState.Actors[i].ActorId ||
                    binding.ActorId != InitialState.WorldState.Bodies[i].ActorId)
                {
                    throw Failure("actor_roster_binding_mismatch", $"Actor roster binding at index {i} does not match the Session descriptor.");
                }
            }
            if (PipelineInitialState.Mode == SimulationPipelineInitialStateMode.RestoreProvidedSnapshot)
            {
                SimulationPipelineStateSnapshot snapshot = PipelineInitialState.Snapshot;
                if (snapshot == null || snapshot.LastCompletedTick != InitialState.LastCompletedTick ||
                    !snapshot.Pipeline.Equals(Descriptor.Pipeline) || !snapshot.Backend.Equals(Descriptor.ExecutionBackend))
                    throw Failure("initial_pipeline_state_identity_mismatch", "Provided Pipeline state does not match the initial Character and World state.");
            }
            else if (PipelineInitialState.Mode != SimulationPipelineInitialStateMode.CaptureActivatedDefaults ||
                     PipelineInitialState.Snapshot != null)
            {
                throw Failure("initial_pipeline_state_mode_invalid", "Pipeline initial state source is invalid.");
            }
        }

        void ValidateSourcePorts()
        {
            if (SourcePorts.Ports.Count != m_ExpectedSourcePorts.Count)
                throw Failure("source_port_count_mismatch", "Runtime Source port set does not match the compiled Source descriptor set.");
            for (int i = 0; i < SourcePorts.Ports.Count; i++)
            {
                SimulationPortDescriptor actual = SourcePorts.Ports[i].Descriptor;
                SimulationPortDescriptor expected = m_ExpectedSourcePorts[i];
                if (!string.Equals(actual.PortId, expected.PortId, StringComparison.Ordinal) ||
                    !string.Equals(actual.SchemaId, expected.SchemaId, StringComparison.Ordinal) ||
                    actual.SchemaVersion != expected.SchemaVersion || actual.Direction != expected.Direction ||
                    !string.Equals(actual.OwnerComponentId, expected.OwnerComponentId, StringComparison.Ordinal) ||
                    !actual.ConfigurationHash.Equals(expected.ConfigurationHash))
                {
                    throw Failure("source_port_identity_mismatch", $"Runtime Source port '{actual.PortId}' does not match the compiled Source descriptor.");
                }
            }
        }

        void ValidateOutputRoutes()
        {
            if (m_OutputRoutes.Count != Descriptor.Roster.Actors.Count)
                throw Failure("output_route_count_mismatch", "Session requires exactly one output route per Actor.");
            var actors = new HashSet<ActorId>();
            for (int i = 0; i < m_OutputRoutes.Count; i++)
                if (!actors.Add(m_OutputRoutes[i].ActorId))
                    throw Failure("output_route_actor_duplicate", $"Actor '{m_OutputRoutes[i].ActorId}' has multiple output routes.");
            for (int i = 0; i < Descriptor.Roster.Actors.Count; i++)
                if (!actors.Contains(Descriptor.Roster.Actors[i]))
                    throw Failure("output_route_actor_missing", $"Actor '{Descriptor.Roster.Actors[i]}' has no output route.");
        }

        static bool ContainsPortInstance(SimulationRuntimePortSet ports, ISimulationRuntimePort expected)
        {
            for (int i = 0; i < ports.Ports.Count; i++)
                if (ReferenceEquals(ports.Ports[i], expected))
                    return true;
            return false;
        }

        static ReadOnlyCollection<SimulationActorBinding> FreezeRoster(IEnumerable<SimulationActorBinding> roster)
        {
            var values = roster == null ? new List<SimulationActorBinding>() : new List<SimulationActorBinding>(roster);
            values.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
            if (values.Count == 0)
                throw new ArgumentException("Character Runtime roster cannot be empty.", nameof(roster));
            for (int i = 0; i < values.Count; i++)
                if (values[i] == null || i > 0 && values[i - 1].ActorId == values[i].ActorId)
                    throw new ArgumentException("Character Runtime roster contains a null or duplicate ActorId.", nameof(roster));
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SimulationPortDescriptor> FreezePorts(IEnumerable<SimulationPortDescriptor> ports)
        {
            var values = ports == null ? new List<SimulationPortDescriptor>() : new List<SimulationPortDescriptor>(ports);
            values.Sort((left, right) => string.CompareOrdinal(left.PortId, right.PortId));
            for (int i = 1; i < values.Count; i++)
                if (string.Equals(values[i - 1].PortId, values[i].PortId, StringComparison.Ordinal))
                    throw new ArgumentException("Expected Source port set contains duplicate identities.", nameof(ports));
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SimulationOutputRouteDescriptor> FreezeOutputRoutes(IEnumerable<SimulationOutputRouteDescriptor> routes)
        {
            var values = routes == null ? new List<SimulationOutputRouteDescriptor>() : new List<SimulationOutputRouteDescriptor>(routes);
            values.Sort((left, right) => string.CompareOrdinal(left.RouteId, right.RouteId));
            for (int i = 1; i < values.Count; i++)
                if (string.Equals(values[i - 1].RouteId, values[i].RouteId, StringComparison.Ordinal))
                    throw new ArgumentException("Output route set contains duplicate identities.", nameof(routes));
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<IDisposable> FreezeResources(IEnumerable<IDisposable> resources, string parameter)
        {
            var values = resources == null ? new List<IDisposable>() : new List<IDisposable>(resources);
            var unique = new HashSet<IDisposable>(ReferenceEqualityComparer<IDisposable>.Instance);
            for (int i = 0; i < values.Count; i++)
                if (values[i] == null || !unique.Add(values[i]))
                    throw new ArgumentException("Owned resource list contains a missing or duplicate instance.", parameter);
            return values.AsReadOnly();
        }

        SimulationSessionCompositionException Failure(string code, string message) =>
            new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Composition,
                code,
                message,
                Descriptor.ExecutionBackend.ToString()));
    }

    sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();
        public bool Equals(T left, T right) => ReferenceEquals(left, right);
        public int GetHashCode(T value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
