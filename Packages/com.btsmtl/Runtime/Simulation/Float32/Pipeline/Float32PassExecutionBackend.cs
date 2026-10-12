using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public static class Float32PassExecutionBackend
    {
        public const string BackendId = "thirdperson.simulation.float32-pass-backend";
        public const string SemanticVersion = "1";

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
                        Float32SimulationNumericProfile.Value.Id,
                        Float32SimulationNumericProfile.Value.AbiVersion,
                        new SimulationPipelineSchemaVersion(1),
                        SimulationPipelineExecutionSupport.Forward |
                        SimulationPipelineExecutionSupport.Replay |
                        SimulationPipelineExecutionSupport.Restore |
                        SimulationPipelineExecutionSupport.Authoritative,
                        false)
                });

        public static SimulationExecutionBackendDescriptor Descriptor => s_Descriptor;

        public static Float32PassBackendCompositionResult Create(Float32PassBackendCompositionRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!request.Backend.Identity.Equals(s_Descriptor.Identity))
            {
                throw new SimulationSessionCompositionException(new SimulationSessionFailure(
                    SimulationSessionFailureStage.Composition,
                    "float32_backend_definition_mismatch",
                    "Composition request was validated against another Execution Backend descriptor.",
                    request.Backend.Identity.ToString()));
            }
            return Float32PassPipelineRuntimeBuilder.Build(request);
        }

        public static Float32PipelineProductRuntimeCatalog CreateProductRuntimeCatalog(
            IEnumerable<IFloat32PipelineProductSlotFactory> extensions = null)
        {
            var factories = new List<IFloat32PipelineProductSlotFactory>
            {
                new Float32ExclusiveProductSlotFactory<Float32CanonicalInputBatch>(SimulationPipelineProducts.CanonicalInputs, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<Float32TypedIngressBatch>(SimulationPipelineProducts.TypedIngress, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<SimulationSessionExecutionPlan<Float32SimulationStep>>(SimulationPipelineProducts.ExecutionPlan, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<Float32CharacterEvaluationResultBatch>(SimulationPipelineProducts.CharacterEvaluationResults, Float32PipelineProductLifetime.SimulationStep),
                new Float32ExclusiveProductSlotFactory<WorldSolveBatchRequest>(SimulationPipelineProducts.WorldSolveBatchRequest, Float32PipelineProductLifetime.SimulationStep),
                new Float32ExclusiveProductSlotFactory<WorldSolveBatchResult>(SimulationPipelineProducts.WorldSolveBatchResult, Float32PipelineProductLifetime.SimulationStep),
                new Float32AppendProductSlotFactory<SimulationActorTickResult>(SimulationPipelineProducts.FinalizedStepResult, Float32PipelineProductLifetime.OuterTransaction),
                new Float32AppendProductSlotFactory<SimulationPipelinePassStateSnapshot>(SimulationPipelineProducts.PipelineSnapshotContribution, Float32PipelineProductLifetime.OuterTransaction),
                new Float32ExclusiveProductSlotFactory<SimulationPipelineOutputDispositionSet>(SimulationPipelineProducts.OutputDispositionSet, Float32PipelineProductLifetime.OuterTransaction),
                new Float32AppendProductSlotFactory<Float32SourceEgressRecord>(SimulationPipelineProducts.SourceEgress, Float32PipelineProductLifetime.OuterTransaction)
            };
            if (extensions != null)
                factories.AddRange(extensions);
            return new Float32PipelineProductRuntimeCatalog(factories);
        }
    }

    public sealed class Float32PassBackendCompositionResult
    {
        public Float32PassBackendCompositionResult(
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

    static class Float32PassPipelineRuntimeBuilder
    {
        public static Float32PassBackendCompositionResult Build(Float32PassBackendCompositionRequest request)
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

                Float32PipelineProductStore products = request.ProductRuntimeFactories.CreateStore(request.CompiledPipeline.Products);
                var stateStore = new SimulationWorldStateStore(request.CharacterRuntime, request.InitialState);
                resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, stateStore);
                resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, products);
                var characterRuntimePort = new Float32CharacterRuntimePort(
                    request.Backend.Identity,
                    request.CharacterRuntime);
                var workingStatePort = new Float32WorkingStatePort(request.Backend.Identity);
                var completedStepPort = new Float32CompletedStepPort(request.Backend.Identity);
                var committedObservationPort = new Float32CommittedActorPoseReadPort(
                    request.Backend.Identity,
                    stateStore);
                var solverPort = new Float32WorldSolverRuntimePort(request.Descriptor.WorldSolver, request.Solver);
                var diagnosticsPort = new Float32DiagnosticsRuntimePort(request.DiagnosticsIdentity, request.Diagnostics);
                var targetPorts = new SimulationRuntimePortSet(new ISimulationRuntimePort[]
                {
                    characterRuntimePort,
                    workingStatePort,
                    completedStepPort,
                    committedObservationPort
                });
                var solverPorts = new SimulationRuntimePortSet(new ISimulationRuntimePort[] { solverPort });
                var diagnosticsPorts = new SimulationRuntimePortSet(new ISimulationRuntimePort[] { diagnosticsPort });

                var runtimes = new List<IFloat32CompiledPipelinePassRuntime>(request.CompiledPipeline.Passes.Count);
                var stateParticipants = new List<ISimulationPipelineStateParticipant>();
                var reconstructible = new List<ISimulationPipelineReconstructiblePass>();
                for (int i = 0; i < request.CompiledPipeline.Passes.Count; i++)
                {
                    CompiledSimulationPipelinePass pass = request.CompiledPipeline.Passes[i];
                    IFloat32PipelinePassRuntimeFactory factory = request.PassRuntimeFactories.GetRequired(pass);
                    var context = new Float32PipelinePassRuntimeFactoryContext(
                        pass,
                        products,
                        request.SourcePorts,
                        targetPorts,
                        solverPorts,
                        diagnosticsPorts,
                        request.Descriptor,
                        graphBatchExecution);
                    IFloat32CompiledPipelinePassRuntime runtime = factory.Create(context) ??
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

                IFloat32CommittedInputReadPort availabilityInput = null;
                for (int participant = 0; participant < stateParticipants.Count; participant++)
                    if (stateParticipants[participant] is IFloat32CommittedInputReadPort input)
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
                        maximumSteps = Math.Max(maximumSteps, ((ISimulationScheduleCapacity)runtimes[passIndex]).MaximumStepCount);
                int retainedSnapshots = request.Committer is ISimulationPipelineSnapshotRetentionCapacity retention
                    ? retention.MaximumRetainedStepSnapshots : 0;
                for (int participant = 0; participant < stateParticipants.Count; participant++)
                    if (stateParticipants[participant] is ISimulationPipelineSnapshotRetentionCapacity holder)
                        retainedSnapshots = checked(retainedSnapshots + holder.MaximumRetainedStepSnapshots);
                int snapshotCapacity = checked(1 + maximumSteps + request.Descriptor.RecordedFrameCapacity + retainedSnapshots);
                for (int participant = 0; participant < stateParticipants.Count; participant++)
                    if (stateParticipants[participant] is ISimulationPipelineStepStateStorage storage)
                        storage.PrepareStepStorage(snapshotCapacity);
                SimulationPipelineStepSnapshotStorage stepProjections = request.Descriptor.MaximumSessionCheckpointPayloadBytes > 0
                    ? new SimulationPipelineStepSnapshotStorage(request.CompiledPipeline, stateParticipants,
                        snapshotCapacity, request.Descriptor.MaximumSessionCheckpointPayloadBytes)
                    : null;

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
                resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, initialPipelineState);
                if (request.PipelineInitialState.Mode == SimulationPipelineInitialStateMode.RestoreProvidedSnapshot &&
                    !initialPipelineState.SnapshotHash.Equals(request.PipelineInitialState.Snapshot.SnapshotHash))
                {
                    throw Failure(
                        "initial_pipeline_state_restore_mismatch",
                        "Activated Pipeline runtime did not restore the provided initial state exactly.");
                }
                var transaction = new Float32PipelineTransaction(
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
                    stepProjections,
                    snapshotCapacity,
                    availabilityInput);
                resources.Register(SimulationSessionResourceReleasePhase.RuntimeAndPasses, transaction);
                SimulationSessionLaunchPlan launchPlan = BuildLaunchPlan(request, initialPipelineState);
                var handle = new Float32PassPipelineRuntimeHandle(
                    request.Descriptor,
                    request.CompiledPipeline,
                    stateStore,
                    transaction,
                    runtimes.AsReadOnly(),
                    resources,
                    graphBatchExecution);
                return new Float32PassBackendCompositionResult(
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
            Float32PassBackendCompositionRequest request,
            SimulationPipelineStateSnapshot initialPipelineState)
        {
            var states = new List<SimulationInitialStateIdentity>(request.Roster.Count + 2);
            for (int i = 0; i < request.InitialState.Actors.Count; i++)
            {
                states.Add(new SimulationInitialStateIdentity(
                    SimulationInitialStateKind.Character,
                    "float32-character-state",
                    1,
                    request.Descriptor.MaximumSessionCheckpointPayloadBytes > 0
                        ? request.InitialState.Actors[i].State.CanonicalPayload.Hash.Value
                        : Float32CharacterRuntimeStateCodec.ComputeHash(request.InitialState.Actors[i].State).Value,
                    request.InitialState.Actors[i].ActorId));
            }
            states.Add(new SimulationInitialStateIdentity(
                SimulationInitialStateKind.World,
                "float32-world-state",
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
            IFloat32CompiledPipelinePassRuntime runtime)
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
                        !CanonicalTextValue.Equals(state.StateOwner, pass.Descriptor.StateOwner, StringComparison.Ordinal) ||
                        !CanonicalTextValue.Equals(state.StateSchemaId, pass.Factory.StateSchemaId, StringComparison.Ordinal) ||
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
