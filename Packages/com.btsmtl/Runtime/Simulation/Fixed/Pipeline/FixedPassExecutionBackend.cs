using ThirdPersonSimulation;
using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation.Fixed
{
    public static class FixedPassExecutionBackend
    {
        public const string BackendId = "thirdperson.simulation.backend.fixed-pass";
        public const string SemanticVersion = "2";

        static readonly SimulationExecutionBackendDescriptor s_Descriptor =
            new SimulationExecutionBackendDescriptor(
                BackendId,
                SemanticVersion,
                SimulationExecutionBackendCapability.PhasePassExecution |
                SimulationExecutionBackendCapability.MultiStepTransaction |
                SimulationExecutionBackendCapability.AtomicStatePublish |
                SimulationExecutionBackendCapability.AtomicSessionRestore |
                SimulationExecutionBackendCapability.PipelineStateSnapshot |
                SimulationExecutionBackendCapability.SolverReconstruction |
                SimulationExecutionBackendCapability.FailStopCommit,
                new[]
                {
                    new SimulationExecutionBackendTargetSupport(
                        FixedSimulationNumericProfile.Value.Id,
                        FixedSimulationNumericProfile.Value.AbiVersion,
                        new SimulationPipelineSchemaVersion(1),
                        SimulationPipelineExecutionSupport.Forward |
                        SimulationPipelineExecutionSupport.Replay |
                        SimulationPipelineExecutionSupport.Restore |
                        SimulationPipelineExecutionSupport.Authoritative,
                        true)
                });

        public static SimulationExecutionBackendDescriptor Descriptor => s_Descriptor;

        public static FixedPassBackendCompositionResult Create(FixedPassBackendCompositionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Backend.Identity.Equals(s_Descriptor.Identity))
            {
                throw new SimulationSessionCompositionException(new SimulationSessionFailure(
                    SimulationSessionFailureStage.Composition,
                    "Fixed_backend_definition_mismatch",
                    "Composition request was validated against another Execution Backend descriptor.",
                    request.Backend.Identity.ToString()));
            }
            return FixedPassPipelineRuntimeBuilder.Build(request);
        }

        public static FixedPipelineProductRuntimeCatalog CreateProductRuntimeCatalog(
            IEnumerable<IFixedPipelineProductSlotFactory> extensions = null)
        {
            var factories = new List<IFixedPipelineProductSlotFactory>
            {
                new FixedExclusiveProductSlotFactory<FixedCanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs, FixedPipelineProductLifetime.OuterTransaction),
                new FixedExclusiveProductSlotFactory<FixedTypedIngressBatch>(SimulationPipelineProducts.TypedIngress, FixedPipelineProductLifetime.OuterTransaction),
                new FixedExclusiveProductSlotFactory<SimulationSessionExecutionPlan<FixedSimulationStep>>(SimulationPipelineProducts.ExecutionPlan, FixedPipelineProductLifetime.OuterTransaction),
                new FixedExclusiveProductSlotFactory<FixedCharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults, FixedPipelineProductLifetime.SimulationStep),
                new FixedExclusiveProductSlotFactory<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest, FixedPipelineProductLifetime.SimulationStep),
                new FixedExclusiveProductSlotFactory<WorldSolveBatchResult>(SimulationPipelineProducts.WorldSolveBatchResult, FixedPipelineProductLifetime.SimulationStep),
                new FixedExclusiveProductSlotFactory<FixedCombatBatchResult>(SimulationPipelineProducts.CombatBatchResult, FixedPipelineProductLifetime.SimulationStep),
                new FixedAppendProductSlotFactory<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult, FixedPipelineProductLifetime.OuterTransaction),
                new FixedAppendProductSlotFactory<SimulationPipelinePassStateSnapshot>(SimulationPipelineProducts.PipelineSnapshotContribution, FixedPipelineProductLifetime.OuterTransaction),
                new FixedExclusiveProductSlotFactory<SimulationPipelineOutputDispositionSet>(SimulationPipelineProducts.OutputDispositionSet, FixedPipelineProductLifetime.OuterTransaction),
                new FixedAppendProductSlotFactory<FixedSourceEgressRecord>(SimulationPipelineProducts.SourceEgress, FixedPipelineProductLifetime.OuterTransaction)
            };
            if (extensions != null)
                factories.AddRange(extensions);
            return new FixedPipelineProductRuntimeCatalog(factories);
        }
    }

    public sealed class FixedPassBackendCompositionResult
    {
        public FixedPassBackendCompositionResult(
            SimulationSessionLaunchPlan launchPlan,
            SimulationPipelineStateSnapshot initialPipelineState,
            ISimulationSessionRuntimeHandle runtimeHandle)
        {
            LaunchPlan = launchPlan ?? throw new ArgumentNullException(nameof(launchPlan));
            InitialPipelineState = initialPipelineState ?? throw new ArgumentNullException(nameof(initialPipelineState));
            RuntimeHandle = runtimeHandle ?? throw new ArgumentNullException(nameof(runtimeHandle));
        }

        public SimulationSessionLaunchPlan LaunchPlan { get; }
        public SimulationPipelineStateSnapshot InitialPipelineState { get; }
        public ISimulationSessionRuntimeHandle RuntimeHandle { get; }
    }

    static class FixedPassPipelineRuntimeBuilder
    {
        public static FixedPassBackendCompositionResult Build(FixedPassBackendCompositionRequest request)
        {
            var resources = new SimulationSessionResourceRegistry();
            try
            {
                for (int i = 0; i < request.SourceResources.Count; i++)
                    resources.Register(SimulationSessionResourceReleasePhase.SourceAndEndpoint, request.SourceResources[i]);
                resources.Register(SimulationSessionResourceReleasePhase.Solver, request.Solver);
                for (int i = 0; i < request.ActorResources.Count; i++)
                    resources.Register(SimulationSessionResourceReleasePhase.ActorAndPresentationRegistration, request.ActorResources[i]);
                for (int i = 0; i < request.CharacterRuntime.Roster.Count; i++)
                    resources.Register(SimulationSessionResourceReleasePhase.ActorAndPresentationRegistration, request.CharacterRuntime.Roster[i]);
                var graphBatchExecution = new GraphBatchExecution(
                    request.GraphBatchOptions, request.Roster.Count, request.GraphBatchResource);

                FixedPipelineProductStore products = request.ProductRuntimeFactories.CreateStore(request.CompiledPipeline.Products);
                var stateStore = new SimulationWorldStateStore(request.CharacterRuntime, request.InitialState);
                resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, stateStore);
                resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, products);
                var characterRuntimePort = new FixedCharacterRuntimePort(
                    request.Backend.Identity,
                    request.CharacterRuntime);
                var workingStatePort = new FixedWorkingStatePort(request.Backend.Identity);
                var completedStepPort = new FixedCompletedStepPort(request.Backend.Identity);
                IReadOnlyList<FixedLocalEnemyBinding> localEnemies = Array.Empty<FixedLocalEnemyBinding>();
                for (int i = 0; i < request.SourcePorts.Ports.Count; i++)
                    if (request.SourcePorts.Ports[i] is IFixedLocalInputSourcePort localInput)
                    {
                        localEnemies = localInput.LocalEnemies;
                        for (int actor = 0; actor < request.Roster.Count; actor++)
                        {
                            SimulationActorBinding binding = request.Roster[actor];
                            for (int ability = 0; ability < binding.AbilityInstallations.Installations.Count; ability++)
                            {
                                var installation = binding.AbilityInstallations.Installations[ability];
                                binding.GetInvocation(installation.Data.AbilityId).BindValuePorts(localInput.ValuePorts.Declare(binding.ActorId, installation,
                                    request.Descriptor.RecordedFrameCapacity > 0));
                            }
                        }
                        localInput.ValuePorts.Prepare();
                    }
                var committedObservationPort = new FixedCommittedActorPoseReadPort(
                    request.Backend.Identity,
                    stateStore,
                    localEnemies);
                var solverPort = new FixedWorldSolverRuntimePort(request.Descriptor.WorldSolver, request.Solver);
                var diagnosticsPort = new FixedDiagnosticsRuntimePort(request.DiagnosticsIdentity, request.Diagnostics);
                var combatPort = new FixedCombatRuntimePort(request.Backend.Identity, request.CharacterRuntime, localEnemies,
                    request.Descriptor.MaximumSessionCheckpointPayloadBytes);
                resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, combatPort.Combat);
                var targetPorts = new SimulationRuntimePortSet(new ISimulationRuntimePort[]
                {
                    characterRuntimePort,
                    workingStatePort,
                    completedStepPort,
                    committedObservationPort,
                    combatPort
                });
                var solverPorts = new SimulationRuntimePortSet(new ISimulationRuntimePort[] { solverPort });
                var diagnosticsPorts = new SimulationRuntimePortSet(new ISimulationRuntimePort[] { diagnosticsPort });

                var runtimes = new List<IFixedCompiledPipelinePassRuntime>(request.CompiledPipeline.Passes.Count);
                var stateParticipants = new List<ISimulationPipelineStateParticipant>();
                var reconstructible = new List<ISimulationPipelineReconstructiblePass>();
                for (int i = 0; i < request.CompiledPipeline.Passes.Count; i++)
                {
                    CompiledSimulationPipelinePass pass = request.CompiledPipeline.Passes[i];
                    IFixedPipelinePassRuntimeFactory factory = request.PassRuntimeFactories.GetRequired(pass);
                    var context = new FixedPipelinePassRuntimeFactoryContext(
                        pass,
                        products,
                        request.SourcePorts,
                        targetPorts,
                        solverPorts,
                        diagnosticsPorts,
                        request.Descriptor,
                        graphBatchExecution);
                    IFixedCompiledPipelinePassRuntime runtime = factory.Create(context) ??
                        throw Failure("pass_runtime_missing", $"Factory for Pass '{pass.Descriptor.PassId}' returned no runtime.", pass.Descriptor.PassId);
                    context.CompleteBindings();
                    RequireRuntime(pass, runtime);
                    resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, runtime);
                    runtime.Activate();
                    runtimes.Add(runtime);
                    if (runtime.StateParticipant != null)
                        stateParticipants.Add(runtime.StateParticipant);
                    if (runtime.Reconstructible != null)
                        reconstructible.Add(runtime.Reconstructible);
                }

                IFixedCommittedInputReadPort availabilityInput = null;
                for (int participant = 0; participant < stateParticipants.Count; participant++)
                    if (stateParticipants[participant] is IFixedCommittedInputReadPort input)
                    {
                        if (availabilityInput != null)
                            throw new InvalidOperationException("Session contains duplicate current input read owners.");
                        availabilityInput = input;
                        input.PrepareAvailabilityRead(request.Descriptor.MaximumSessionCheckpointPayloadBytes, request.Roster);
                        for (int actor = 0; actor < request.Roster.Count; actor++)
                            request.Roster[actor].BindCommittedInput(input);
                    }
                int maximumSteps = 0;
                for (int passIndex = 0; passIndex < runtimes.Count; passIndex++)
                    if (runtimes[passIndex].Phase == SimulationPipelinePhase.Schedule)
                        maximumSteps = ((ISimulationScheduleCapacity)runtimes[passIndex]).MaximumStepCount;
                int retainedSnapshots = request.Committer is ISimulationPipelineSnapshotRetentionCapacity retention
                    ? retention.MaximumRetainedStepSnapshots : 0;
                int snapshotCapacity = checked(1 + maximumSteps + request.Descriptor.RecordedFrameCapacity + retainedSnapshots);
                for (int participant = 0; participant < stateParticipants.Count; participant++)
                    if (stateParticipants[participant] is ISimulationPipelineStepStateStorage storage)
                        storage.PrepareStepStorage(snapshotCapacity);

                if (request.PipelineInitialState.Mode == SimulationPipelineInitialStateMode.RestoreProvidedSnapshot)
                {
                    using SimulationPipelineStateRestoreTransaction initialPipelineRestore =
                        SimulationPipelineStateSnapshotCoordinator.PrepareRestore(
                            request.CompiledPipeline,
                            request.PipelineInitialState.Snapshot,
                            stateParticipants);
                    initialPipelineRestore.Apply();
                    initialPipelineRestore.ValidateApplied();
                    initialPipelineRestore.CompleteAfterSessionPublish();
                }
                var reconstructionContext = new SimulationPipelineReconstructionContext(
                    request.Descriptor.Identity,
                    request.CompiledPipeline.Identity,
                    request.CharacterRuntime.GameplayContentHash,
                    request.Descriptor.Roster.RosterHash,
                    request.InitialState.WorldState.WorldRevision);
                for (int i = 0; i < reconstructible.Count; i++)
                    reconstructible[i].Reconstruct(reconstructionContext);

                for (int i = 0; i < request.Roster.Count; i++)
                    request.Solver.RequireBodyBinding(request.Roster[i].ActorId, request.Roster[i].WorldBodyBindingId);
                request.Solver.Reconstruct(request.InitialState.WorldState);
                SimulationPipelineStateSnapshot initialPipelineState =
                    SimulationPipelineStateSnapshotCoordinator.Capture(
                        request.CompiledPipeline,
                        request.InitialState.LastCompletedTick,
                        stateParticipants);
                if (request.PipelineInitialState.Mode == SimulationPipelineInitialStateMode.RestoreProvidedSnapshot &&
                    !initialPipelineState.SnapshotHash.Equals(request.PipelineInitialState.Snapshot.SnapshotHash))
                {
                    throw Failure(
                        "initial_pipeline_state_restore_mismatch",
                        "Activated Pipeline runtime did not restore the provided initial state exactly.");
                }
                var transaction = new FixedPipelineTransaction(
                    request.Descriptor,
                    request.CompiledPipeline,
                    request.CharacterRuntime,
                    stateStore,
                    request.Solver,
                    request.RestoreSource,
                    request.SnapshotCodec,
                    request.Committer,
                    request.Diagnostics,
                    runtimes.AsReadOnly(),
                    stateParticipants.AsReadOnly(),
                    reconstructible.AsReadOnly(),
                    products,
                    workingStatePort,
                    completedStepPort,
                    maximumSteps,
                    availabilityInput);
                resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, transaction);
                SimulationSessionLaunchPlan launchPlan = BuildLaunchPlan(request, initialPipelineState);
                var handle = new FixedPassPipelineRuntimeHandle(
                    request.Descriptor,
                    request.CompiledPipeline,
                    stateStore,
                    transaction,
                    runtimes.AsReadOnly(),
                    resources,
                    request.Solver as ICharacterFutureBodyTranslationSource,
                    request.SourcePorts,
                    combatPort.Combat,
                    graphBatchExecution);
                return new FixedPassBackendCompositionResult(
                    launchPlan,
                    initialPipelineState,
                    handle);
            }
            catch
            {
                resources.Dispose();
                throw;
            }
        }

        static SimulationSessionLaunchPlan BuildLaunchPlan(
            FixedPassBackendCompositionRequest request,
            SimulationPipelineStateSnapshot initialPipelineState)
        {
            var states = new List<SimulationInitialStateIdentity>(request.Roster.Count + 2);
            for (int i = 0; i < request.InitialState.Actors.Count; i++)
            {
                states.Add(new SimulationInitialStateIdentity(
                    SimulationInitialStateKind.Character,
                    "fixed-character-state",
                    1,
                    request.Descriptor.MaximumSessionCheckpointPayloadBytes > 0
                        ? request.InitialState.Actors[i].State.CanonicalPayload.Hash.Value
                        : FixedCharacterRuntimeStateCodec.ComputeHash(request.InitialState.Actors[i].State).Value,
                    request.InitialState.Actors[i].ActorId));
            }
            states.Add(new SimulationInitialStateIdentity(
                SimulationInitialStateKind.World,
                "fixed-world-state",
                1,
                WorldSimulationStateCodec.ComputeHash(request.InitialState.WorldState)));
            states.Add(new SimulationInitialStateIdentity(
                SimulationInitialStateKind.Pipeline,
                "simulation-pipeline-state",
                1,
                initialPipelineState.SnapshotHash));
            return new SimulationSessionLaunchPlan(
                request.Descriptor,
                request.CompiledPipeline.LaunchIdentity,
                request.ExpectedSourcePorts,
                states,
                request.OutputRoutes,
                request.DiagnosticsIdentity);
        }

        static void RequireRuntime(
            CompiledSimulationPipelinePass pass,
            IFixedCompiledPipelinePassRuntime runtime)
        {
            if (!runtime.Descriptor.DescriptorHash.Equals(pass.Descriptor.DescriptorHash) ||
                runtime.Phase != pass.Descriptor.Phase || runtime.State != SimulationPipelinePassRuntimeState.Created)
            {
                throw Failure("pass_runtime_identity_mismatch", $"Runtime for Pass '{pass.Descriptor.PassId}' does not match the compiled descriptor.", pass.Descriptor.PassId);
            }
            switch (pass.Descriptor.StateClass)
            {
                case SimulationPipelinePassStateClass.Stateless:
                case SimulationPipelinePassStateClass.ExternalSource:
                    if (runtime.StateParticipant != null || runtime.Reconstructible != null)
                        throw Failure("pass_runtime_state_owner_mismatch", $"Pass '{pass.Descriptor.PassId}' exposes undeclared runtime state.", pass.Descriptor.PassId);
                    break;
                case SimulationPipelinePassStateClass.Reconstructible:
                    if (runtime.Reconstructible == null || runtime.StateParticipant != null)
                        throw Failure("pass_runtime_reconstruct_owner_missing", $"Pass '{pass.Descriptor.PassId}' has no reconstructible runtime owner.", pass.Descriptor.PassId);
                    break;
                case SimulationPipelinePassStateClass.SnapshotParticipant:
                    if (runtime.StateParticipant == null || runtime.Reconstructible != null)
                        throw Failure("pass_runtime_snapshot_owner_missing", $"Pass '{pass.Descriptor.PassId}' has no snapshot participant runtime owner.", pass.Descriptor.PassId);
                    SimulationPipelineStateParticipantIdentity state = runtime.StateParticipant.StateIdentity;
                    if (!state.PassId.Equals(pass.Descriptor.PassId) ||
                        !state.ImplementationVersion.Equals(pass.Descriptor.ImplementationVersion) ||
                        !string.Equals(state.StateOwner, pass.Descriptor.StateOwner, StringComparison.Ordinal) ||
                        !string.Equals(state.StateSchemaId, pass.Factory.StateSchemaId, StringComparison.Ordinal) ||
                        state.StateSchemaVersion != pass.Factory.StateSchemaVersion)
                    {
                        throw Failure("pass_runtime_snapshot_identity_mismatch", $"Pass '{pass.Descriptor.PassId}' snapshot identity does not match the compiled plan.", pass.Descriptor.PassId);
                    }
                    break;
                default:
                    throw Failure("pass_runtime_state_class_invalid", $"Pass '{pass.Descriptor.PassId}' has an invalid state class.", pass.Descriptor.PassId);
            }
        }

        static SimulationSessionCompositionException Failure(
            string code,
            string message,
            SimulationPipelinePassId passId = default)
        {
            return new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Composition,
                code,
                message,
                passIdentity: passId.ToString()));
        }
    }
}

