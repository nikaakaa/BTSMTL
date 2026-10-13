using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public sealed class ServerAuthoritativeAuthorityHostLaunchRequest
    {
        readonly ReadOnlyCollection<ActorId> m_LockedRoster;

        public ServerAuthoritativeAuthorityHostLaunchRequest(
            Float32SimulationSessionCompositionRequest composition,
            ServerAuthoritativeAuthoritySourcePolicy sourcePolicy,
            SimulationPipelineIdentity expectedAuthorityPipeline,
            IEnumerable<ActorId> lockedRoster)
        {
            Composition = composition ?? throw new ArgumentNullException(nameof(composition));
            SourcePolicy = sourcePolicy ?? throw new ArgumentNullException(nameof(sourcePolicy));
            if (!expectedAuthorityPipeline.IsValid)
                throw new ArgumentException("Expected Authority Pipeline identity is incomplete.", nameof(expectedAuthorityPipeline));
            ExpectedAuthorityPipeline = expectedAuthorityPipeline;
            var actors = lockedRoster == null ? new List<ActorId>() : new List<ActorId>(lockedRoster);
            actors.Sort();
            if (actors.Count == 0)
                throw new ArgumentException("Authority Host launch requires a locked Actor roster.", nameof(lockedRoster));
            for (int i = 0; i < actors.Count; i++)
            {
                if (!actors[i].IsValid || i > 0 && actors[i - 1] == actors[i])
                    throw new ArgumentException("Authority Host launch roster contains an invalid or duplicate ActorId.", nameof(lockedRoster));
            }
            m_LockedRoster = actors.AsReadOnly();
            Validate();
        }

        public Float32SimulationSessionCompositionRequest Composition { get; }
        public ServerAuthoritativeAuthoritySourcePolicy SourcePolicy { get; }
        public SimulationPipelineIdentity ExpectedAuthorityPipeline { get; }
        public IReadOnlyList<ActorId> LockedRoster => m_LockedRoster;
        public Float32CharacterRuntime CharacterRuntime => Composition.CharacterRuntime;
        public SimulationExecutionBackendDescriptor Backend => Composition.Backend;
        public Float32SimulationPipelineRuntimePackage RuntimePackage => Composition.PipelineRuntimePackage;
        public SimulationPipelineDescriptor Pipeline => RuntimePackage.Pipeline;
        public SimulationSessionSourceDescriptor Source => Composition.Source;
        public SimulationRuntimePortSet SourcePorts => Composition.SourcePorts;
        public IFloat32SimulationRestoreSource RestoreSource => Composition.RestoreSource;
        public SimulationWorldSolverDefinitionDescriptor SolverDefinition => Composition.SolverDefinition;
        public ICharacterWorldSolver Solver => Composition.Solver;
        public SimulationWorldStateSet InitialState => Composition.InitialState;
        public IFloat32SimulationCommitter Committer => Composition.Committer;
        public ISimulationDiagnosticsSink Diagnostics => Composition.Diagnostics;
        public IReadOnlyList<SimulationOutputRouteDescriptor> OutputRoutes => Composition.OutputRoutes;

        public Float32PassBackendCompositionResult Launch()
        {
            Validate();
            return Float32SimulationSessionComposer.Compose(Composition, ExpectedAuthorityPipeline);
        }

        void Validate()
        {
            if (!Backend.Identity.Equals(Float32PassExecutionBackend.Descriptor.Identity))
                throw Failure("authority_backend_mismatch", "Authority Host requires the canonical Float32 Pass Backend.");
            if (!Pipeline.PipelineId.Equals(ExpectedAuthorityPipeline.Id) ||
                !Pipeline.Revision.Equals(ExpectedAuthorityPipeline.Revision) ||
                !Pipeline.SchemaVersion.Equals(ExpectedAuthorityPipeline.SchemaVersion))
            {
                throw Failure(
                    "authority_pipeline_descriptor_identity_mismatch",
                    "Authority Runtime Package descriptor does not match the expected Authority Pipeline identity.");
            }
            try
            {
                ServerAuthoritativeAuthorityPipelineCatalog.ValidateRuntimePackage(
                    RuntimePackage,
                    SourcePolicy.ModelPolicy);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                throw Failure("authority_pipeline_runtime_package_mismatch", exception.Message);
            }
            if (SourcePolicy.ModelPolicy.SimulationTickRate != Composition.TickRate ||
                Source.OuterTickKind != SimulationTickSourceKind.Authoritative ||
                (Source.ExecutionSupport & SimulationPipelineExecutionSupport.Authoritative) == 0 ||
                !Source.RequiredPipelineId.Equals(Pipeline.PipelineId))
            {
                throw Failure("authority_source_policy_mismatch", "Authority Source policy, TickRate, execution mode, and Pipeline identity do not match.");
            }
            if (RestoreSource != null)
                throw Failure("authority_restore_source_forbidden", "Authority Host Pipeline does not accept a restore Source port.");
            if (Composition.SourceResources.Count == 0)
                throw Failure("authority_source_runtime_missing", "Authority Host requires explicit ownership of its prepared Source runtime.");
            RequirePort<IServerAuthoritativeAcceptedInputSourcePort>(ServerAuthoritativeSourcePortContracts.AcceptedInput);
            RequirePort<IServerAuthoritativeAuthorityClockSourcePort>(ServerAuthoritativeSourcePortContracts.AuthorityClock);
            RequirePort<IServerAuthoritativeFullBaselineRequestSourcePort>(ServerAuthoritativeSourcePortContracts.FullBaselineRequest);
            RequirePort<IServerAuthoritativeNetworkSendPort>(ServerAuthoritativeSourcePortContracts.AuthoritySend);
            if (SourcePorts.Ports.Count != 4)
                throw Failure("authority_source_port_set_mismatch", "Authority Host Source runtime must expose exactly the canonical four ports.");
            ValidateRoster();
            ValidateOutputRoutes();
            if (!Solver.Descriptor.ImplementationId.Equals(SolverDefinition.ImplementationId) ||
                !CanonicalTextValue.Equals(Solver.Descriptor.Version, SolverDefinition.ImplementationVersion, StringComparison.Ordinal) ||
                !InitialState.WorldState.SolverId.Equals(SolverDefinition.ImplementationId))
            {
                throw Failure("authority_solver_identity_mismatch", "Authority Host Solver runtime, descriptor, and initial World state do not match.");
            }
            if (Committer == null || Diagnostics == null)
                throw Failure("authority_output_boundary_missing", "Authority Host Committer or diagnostics boundary is missing.");
        }

        void ValidateRoster()
        {
            if (CharacterRuntime.Roster.Count != m_LockedRoster.Count ||
                InitialState.Actors.Count != m_LockedRoster.Count ||
                InitialState.WorldState.Bodies.Count != m_LockedRoster.Count)
            {
                throw Failure("authority_roster_count_mismatch", "Authority Host roster does not match Character Runtime or initial state.");
            }
            for (int i = 0; i < m_LockedRoster.Count; i++)
            {
                ActorId actorId = m_LockedRoster[i];
                if (CharacterRuntime.Roster[i].ActorId != actorId ||
                    InitialState.Actors[i].ActorId != actorId ||
                    InitialState.WorldState.Bodies[i].ActorId != actorId)
                {
                    throw Failure("authority_roster_identity_mismatch", $"Authority Host Actor '{actorId}' is not in the canonical roster order.");
                }
            }
        }

        void ValidateOutputRoutes()
        {
            if (OutputRoutes.Count != m_LockedRoster.Count)
                throw Failure("authority_output_route_count_mismatch", "Authority Host requires exactly one output route per Actor.");
            var routed = new HashSet<ActorId>();
            for (int i = 0; i < OutputRoutes.Count; i++)
            {
                SimulationOutputRouteDescriptor route = OutputRoutes[i];
                if (!routed.Add(route.ActorId))
                    throw Failure("authority_output_route_duplicate", $"Authority Host has duplicate output routes for Actor '{route.ActorId}'.");
            }
            for (int i = 0; i < m_LockedRoster.Count; i++)
            {
                if (!routed.Contains(m_LockedRoster[i]))
                    throw Failure("authority_output_route_missing", $"Authority Host has no output route for Actor '{m_LockedRoster[i]}'.");
            }
        }

        void RequirePort<TPort>(SimulationPipelinePortRequirement requirement)
            where TPort : class, ISimulationRuntimePort
        {
            try
            {
                SourcePorts.GetRequired<TPort>(requirement);
            }
            catch (Exception exception)
            {
                throw Failure("authority_source_port_mismatch", exception.Message);
            }
        }

        static SimulationSessionCompositionException Failure(string code, string message)
        {
            return new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Composition,
                code,
                message,
                ServerAuthoritativePipelineIdentity.AuthorityPipelineId));
        }
    }

    public sealed class ServerAuthoritativeAuthoritySessionRuntimeLauncher :
        IFloat32SimulationSessionRuntimeLauncher
    {
        const string LauncherId = "thirdperson.server-authoritative.runtime-launcher.authority";
        readonly SimulationComponentIdentity m_SourceIdentity;
        readonly ServerAuthoritativeAuthoritySourcePolicy m_SourcePolicy;
        readonly SimulationPipelineIdentity m_ExpectedAuthorityPipeline;
        readonly ReadOnlyCollection<ActorId> m_LockedRoster;
        int m_Launched;

        public ServerAuthoritativeAuthoritySessionRuntimeLauncher(
            SimulationSessionSourceDescriptor source,
            ServerAuthoritativeAuthoritySourcePolicy sourcePolicy,
            SimulationPipelineIdentity expectedAuthorityPipeline,
            IEnumerable<ActorId> lockedRoster)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            m_SourceIdentity = source.Identity;
            m_SourcePolicy = sourcePolicy ?? throw new ArgumentNullException(nameof(sourcePolicy));
            if (!expectedAuthorityPipeline.IsValid)
                throw new ArgumentException("Expected Authority Pipeline identity is incomplete.", nameof(expectedAuthorityPipeline));
            m_ExpectedAuthorityPipeline = expectedAuthorityPipeline;
            var actors = lockedRoster == null ? new List<ActorId>() : new List<ActorId>(lockedRoster);
            actors.Sort();
            if (actors.Count == 0)
                throw new ArgumentException("Authority Runtime Launcher requires a locked Actor roster.", nameof(lockedRoster));
            for (int i = 0; i < actors.Count; i++)
            {
                if (!actors[i].IsValid || i > 0 && actors[i - 1] == actors[i])
                    throw new ArgumentException("Authority Runtime Launcher roster contains an invalid or duplicate ActorId.", nameof(lockedRoster));
            }
            m_LockedRoster = actors.AsReadOnly();
            var identityParts = new string[actors.Count + 6];
            identityParts[0] = "server-authoritative-authority-runtime-launcher/2";
            identityParts[1] = source.Identity.ToString();
            identityParts[2] = source.NumericProfileId.Value;
            identityParts[3] = source.TargetAbiVersion.Value.ToString(CultureInfo.InvariantCulture);
            identityParts[4] = sourcePolicy.ConfigurationHash.ToString();
            identityParts[5] = expectedAuthorityPipeline.ToString();
            for (int i = 0; i < actors.Count; i++)
                identityParts[i + 6] = actors[i].ToString();
            Descriptor = new Float32SimulationSessionRuntimeLauncherDescriptor(
                LauncherId,
                "2",
                source.NumericProfileId,
                source.TargetAbiVersion,
                StableHash.Compute(identityParts));
        }

        public Float32SimulationSessionRuntimeLauncherDescriptor Descriptor { get; }

        public Float32PassBackendCompositionResult Launch(Float32SimulationSessionCompositionRequest request)
        {
            if (Interlocked.Exchange(ref m_Launched, 1) != 0)
                throw Failure("authority_runtime_launcher_already_used", "Authority Runtime Launcher is single-use.");
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Source.Identity.Equals(m_SourceIdentity))
                throw Failure("authority_runtime_launcher_source_mismatch", "Authority Runtime Launcher does not belong to the prepared Session Source.");
            if (!request.Source.NumericProfileId.Equals(Descriptor.NumericProfileId) ||
                !request.Source.TargetAbiVersion.Equals(Descriptor.TargetAbiVersion) ||
                !request.CharacterRuntime.NumericProfile.Id.Equals(Descriptor.NumericProfileId) ||
                !request.CharacterRuntime.NumericProfile.AbiVersion.Equals(Descriptor.TargetAbiVersion))
            {
                throw Failure("authority_runtime_launcher_target_abi_mismatch", "Authority Runtime Launcher, Session Source, and Character Runtime Target ABI do not match.");
            }
            return new ServerAuthoritativeAuthorityHostLaunchRequest(
                request,
                m_SourcePolicy,
                m_ExpectedAuthorityPipeline,
                m_LockedRoster).Launch();
        }

        static SimulationSessionCompositionException Failure(string code, string message)
        {
            return new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Composition,
                code,
                message,
                LauncherId));
        }
    }
}
