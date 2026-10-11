using System;
using System.Collections.Generic;
using ThirdPersonPerformance.Instrumentation;

namespace ThirdPersonSimulation
{
    public sealed class Float32PipelineTransaction : IDisposable
    {
        readonly Float32PipelineTransactionPort m_Target;
        readonly PipelineTransactionCoordinator<
            Float32SimulationStep,
            Float32PipelineWorkingState,
            Float32CompletedSimulationStep,
            Float32SourceEgressRecord,
            Float32SimulationCommitBatch> m_Coordinator;

        internal Float32PipelineTransaction(
            SimulationSessionCompositionDescriptor descriptor,
            CompiledSimulationPipelinePlan plan,
            Float32CharacterRuntime characterRuntime,
            SimulationWorldStateStore stateStore,
            ICharacterWorldSolver solver,
            IFloat32SimulationRestoreSource restoreSource,
            IFloat32SimulationSessionSnapshotCodec snapshotCodec,
            IFloat32SimulationCommitter committer,
            ISimulationDiagnosticsSink diagnostics,
            IReadOnlyList<IFloat32CompiledPipelinePassRuntime> passes,
            IReadOnlyList<ISimulationPipelineStateParticipant> stateParticipants,
            IReadOnlyList<ISimulationPipelineReconstructiblePass> reconstructiblePasses,
            Float32PipelineProductStore products,
            Float32WorkingStatePort workingStatePort,
            Float32CompletedStepPort completedStepPort,
            int maximumSteps,
            SimulationPipelineStepSnapshotStorage stepProjections,
            IFloat32CommittedInputReadPort availabilityInput)
        {
            var services = new PipelineTransactionRuntimeServices(
                descriptor,
                plan,
                passes,
                stateParticipants,
                reconstructiblePasses);
            m_Target = new Float32PipelineTransactionPort(
                services,
                characterRuntime,
                stateStore,
                solver,
                diagnostics,
                restoreSource,
                snapshotCodec,
                committer,
                products,
                workingStatePort,
                completedStepPort,
                maximumSteps,
                stepProjections,
                availabilityInput);
            m_Coordinator = new PipelineTransactionCoordinator<
                Float32SimulationStep,
                Float32PipelineWorkingState,
                Float32CompletedSimulationStep,
                Float32SourceEgressRecord,
                Float32SimulationCommitBatch>(services, m_Target);
        }

        [PerformanceProbe("simulation.pipeline.transaction")]
        public void Execute(SimulationSessionLogicTickContext outer)
        {
            m_Coordinator.Execute(outer);
        }

        internal void PrepareInputRecording(ActorId actorId) => m_Target.PrepareInputRecording(actorId);

        public void RestoreRecordedControlInput(ArraySegment<byte> bytes) => m_Target.RestoreRecordedControlInput(bytes);

        public void QueryAbilityAvailability(ActorId actorId, GameplayAbilityRulePhase phase,
            out GameplayAbilityAvailabilityResult result) => m_Target.QueryAbilityAvailability(actorId, phase, out result);

        public SimulationSessionCheckpoint CaptureCheckpoint() => m_Target.CaptureCheckpoint();

        public void RestoreCheckpoint(SimulationSessionCheckpoint checkpoint) => m_Target.RestoreCheckpoint(checkpoint);

        public void Dispose()
        {
            m_Target.Dispose();
        }
    }

    internal sealed class Float32PipelineTransactionPort :
        IPipelineTransactionTargetPort<
            Float32SimulationStep,
            Float32PipelineWorkingState,
            Float32CompletedSimulationStep,
            Float32SourceEgressRecord,
            Float32SimulationCommitBatch>
    {
        readonly IFloat32SimulationRestoreSource m_RestoreSource;
        readonly PipelineTransactionRuntimeServices m_Services;
        readonly Float32CharacterRuntime m_CharacterRuntime;
        readonly IFloat32CommittedInputReadPort m_AvailabilityInput;
        IReadOnlyList<SimulationActorBinding> m_Roster;
        readonly SimulationWorldStateStore m_StateStore;
        readonly ICharacterWorldSolver m_Solver;
        readonly ISimulationDiagnosticsSink m_Diagnostics;
        readonly IFloat32SimulationSessionSnapshotCodec m_SnapshotCodec;
        SimulationPipelineStepSnapshotStorage m_StepProjections;
        readonly IFloat32SimulationCommitter m_Committer;
        readonly Float32PipelineProductStore m_Products;
        readonly IReadOnlySimulationPipelineProductPort<Float32CharacterEvaluationResultBatch> m_CharacterEvaluationResults;
        readonly Float32WorkingStatePort m_WorkingStatePort;
        readonly Float32CompletedStepPort m_CompletedStepPort;
        Float32PipelineWorkingState m_WorkingStateShell;
        readonly List<SimulationTickResult> m_StepResults;
        int m_ActiveStepResultCount;
        readonly Float32PipelineWorkingState m_CheckpointWorkingState;
        readonly Float32CharacterRestoreTransaction m_CheckpointCharacterRestore;
        readonly Float32WorldRestoreTransaction m_CheckpointWorldRestore;
        readonly SimulationPipelineStateRestoreTransaction m_CheckpointPipelineRestore;
        readonly SimulationSessionRestoreTransaction m_CheckpointRestore;
        readonly Float32CharacterRestoreTransaction m_SourceCharacterRestore;
        readonly Float32WorldRestoreTransaction m_SourceWorldRestore;
        readonly SimulationPipelineStateRestoreTransaction m_SourcePipelineRestore;
        readonly SimulationSessionRestoreTransaction m_SourceRestore;

        public Float32PipelineTransactionPort(
            PipelineTransactionRuntimeServices services,
            Float32CharacterRuntime characterRuntime,
            SimulationWorldStateStore stateStore,
            ICharacterWorldSolver solver,
            ISimulationDiagnosticsSink diagnostics,
            IFloat32SimulationRestoreSource restoreSource,
            IFloat32SimulationSessionSnapshotCodec snapshotCodec,
            IFloat32SimulationCommitter committer,
            Float32PipelineProductStore products,
            Float32WorkingStatePort workingStatePort,
            Float32CompletedStepPort completedStepPort,
            int maximumSteps,
            SimulationPipelineStepSnapshotStorage stepProjections,
            IFloat32CommittedInputReadPort availabilityInput)
        {
            m_Services = services ?? throw new ArgumentNullException(nameof(services));
            m_CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            m_Roster = m_CharacterRuntime.Roster;
            m_AvailabilityInput = availabilityInput;
            for (int actor = 0; actor < m_Roster.Count; actor++)
                m_Roster[actor].PrepareEvaluationOutputCapacity(maximumSteps);
            int stepOutputCapacity = 0;
            for (int actor = 0; actor < m_Roster.Count; actor++)
                stepOutputCapacity = checked(stepOutputCapacity + m_Roster[actor].GameplayOutputCapacity);
            committer.PrepareOutputCapacity(stepOutputCapacity, checked(stepOutputCapacity * maximumSteps));
            m_StepResults = new List<SimulationTickResult>(maximumSteps);
            for (int index = 0; index < maximumSteps; index++)
                m_StepResults.Add(new SimulationTickResult(m_Roster.Count));
            m_StateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            m_Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            m_Solver.PrepareStepStorage(checked(maximumSteps + services.Descriptor.RecordedFrameCapacity));
            m_Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            m_RestoreSource = restoreSource;
            m_SnapshotCodec = snapshotCodec ?? throw new ArgumentNullException(nameof(snapshotCodec));
            m_StepProjections = stepProjections;
            m_Committer = committer ?? throw new ArgumentNullException(nameof(committer));
            m_Products = products ?? throw new ArgumentNullException(nameof(products));
            m_CharacterEvaluationResults = m_Products.GetRequired<Float32ExclusiveProductSlot<Float32CharacterEvaluationResultBatch>>(
                SimulationPipelineProducts.CharacterEvaluationResults);
            m_WorkingStatePort = workingStatePort ?? throw new ArgumentNullException(nameof(workingStatePort));
            m_CompletedStepPort = completedStepPort ?? throw new ArgumentNullException(nameof(completedStepPort));
            for (int actor = 0; actor < m_Roster.Count; actor++)
                m_Roster[actor].PrepareExecutionTextBudget(services.Descriptor.MaximumSessionCheckpointPayloadBytes);
            GameplayEffectSessionCapacity sessionCapacity = m_Roster[0].SessionCapacity;
            m_StateStore.PrepareStateStorage(sessionCapacity.StatePageCount);
            m_StateStore.PrepareRestoreStorage(solver.MaximumStatePayloadBytes);
            int maximumWorldPayloadBytes = WorldSimulationStateCodec.MaximumEncodedBytes(m_StateStore.Current.WorldState,
                solver.MaximumStatePayloadBytes);
            m_SnapshotCodec.PrepareCheckpointStorage(services.Descriptor, characterRuntime,
                services.StateParticipants, in sessionCapacity, m_StateStore.Current.WorldState);
            using (SimulationCharacterStatePayloadStorage.Page initialEncoding = m_Roster[0].RuntimeStatePages.EncodingStorage?.Acquire())
            {
                if (initialEncoding != null)
                    for (int actor = 0; actor < m_Roster.Count; actor++)
                        Float32CharacterRuntimeStateCodec.Encode(m_StateStore.Current.Actors[actor].State, initialEncoding, actor);
            }
            m_WorkingStateShell = new Float32PipelineWorkingState(m_StateStore.Current);
            m_CheckpointWorkingState = new Float32PipelineWorkingState(m_StateStore.Current);
            m_CheckpointCharacterRestore = new Float32CharacterRestoreTransaction(m_CheckpointWorkingState, m_Roster);
            m_CheckpointWorldRestore = new Float32WorldRestoreTransaction(m_CheckpointWorkingState, m_Solver,
                new CanonicalWriter(new byte[maximumWorldPayloadBytes]));
            m_CheckpointPipelineRestore = new SimulationPipelineStateRestoreTransaction(services.Plan, services.StateParticipants);
            m_CheckpointRestore = new SimulationSessionRestoreTransaction();
            m_SourceCharacterRestore = new Float32CharacterRestoreTransaction(m_WorkingStateShell, m_Roster);
            m_SourceWorldRestore = new Float32WorldRestoreTransaction(m_WorkingStateShell, m_Solver,
                new CanonicalWriter(new byte[maximumWorldPayloadBytes]));
            m_SourcePipelineRestore = new SimulationPipelineStateRestoreTransaction(services.Plan, services.StateParticipants);
            m_SourceRestore = new SimulationSessionRestoreTransaction();
        }

        public string TransactionIdentityDomain => "float32-pipeline-transaction/1";
        public bool DiagnosticsEnabled => m_Diagnostics.IsEnabled;
        internal void RestoreRecordedControlInput(ArraySegment<byte> bytes) => m_AvailabilityInput.RestoreControlInput(bytes);
        internal void PrepareInputRecording(ActorId actorId)
        {
            if (m_AvailabilityInput == null) throw new NotSupportedException("This Session does not publish a Local input recording owner.");
            m_AvailabilityInput.GetInputCapacity(actorId, out int values, out int requests);
            Float32CharacterInputTraceModule.PrepareRecording(actorId, m_Services.Descriptor.RecordedFrameCapacity, values, requests);
        }

        internal void QueryAbilityAvailability(ActorId actorId, GameplayAbilityRulePhase phase,
            out GameplayAbilityAvailabilityResult result)
        {
            if (m_AvailabilityInput == null)
                throw new NotSupportedException("This Session does not publish a current input read owner for Availability.");
            int actor = m_CharacterRuntime.GetActorIndex(actorId);
            var current = m_StateStore.Current;
            WorldBodyState body = default;
            for (int index = 0; index < current.WorldState.Bodies.Count; index++)
                if (current.WorldState.Bodies[index].ActorId == actorId) { body = current.WorldState.Bodies[index]; break; }
            m_Roster[actor].QueryAbilityAvailability(current.Actors[actor].State, body,
                m_AvailabilityInput, m_CharacterRuntime.ControlRuntime(actorId), phase, out result);
        }

        public ulong BaselineCompletedTick => m_StateStore.Current.LastCompletedTick;
        public WorldRevision BaselineWorldRevision => m_StateStore.Current.WorldState.WorldRevision;

        public SimulationSessionCheckpoint CaptureCheckpoint()
        {
            SimulationWorldStateSet state = m_StateStore.Current;
            if (state.LastCompletedTick == 0)
                throw new InvalidOperationException("Session checkpoint requires a completed Logic Tick.");
            return m_SnapshotCodec.CaptureCheckpoint(in state);
        }

        public void RestoreCheckpoint(SimulationSessionCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            if (!checkpoint.SessionId.Equals(m_Services.Descriptor.SessionId))
                throw new InvalidOperationException("Checkpoint belongs to another Simulation Session.");
            Float32SimulationSessionSnapshot snapshot = m_SnapshotCodec.ReadCheckpoint(checkpoint);
            m_SnapshotCodec.RequireRestore(
                m_Services.Descriptor,
                m_CharacterRuntime,
                m_Solver,
                checkpoint.RestoreDirective,
                snapshot);
            SimulationWorldStateSet restored = m_StateStore.PrepareRestore(snapshot.World);
            m_CheckpointWorkingState.Replace(m_StateStore.Current);
            Float32CharacterRestoreTransaction character = m_CheckpointCharacterRestore.Prepare(restored, snapshot.World.WorldHash.Value);
            SimulationSessionRestoreTransaction restore;
            SimulationPipelineStateRestoreTransaction pipeline = null;
            try
            {
                pipeline = m_CheckpointPipelineRestore.Prepare(snapshot.Pipeline);
                restore = m_CheckpointRestore.Prepare(character,
                    m_CheckpointWorldRestore.Prepare(restored, snapshot.World.WorldHash.Value), pipeline);
            }
            catch
            {
                pipeline?.Dispose();
                character.Dispose();
                throw;
            }
            using (restore)
            {
                restore.ApplyAndValidate();
                m_StateStore.ReplaceValidated(restored);
                restore.CompleteAfterAtomicSessionPublish();
                PublishTimelineOwners(in restored);
                m_SnapshotCodec.CompleteCheckpointReception(checkpoint);
            }
        }

        public Float32PipelineWorkingState CreateWorkingState()
        {
            m_WorkingStateShell.Replace(m_StateStore.Current);
            return m_WorkingStateShell;
        }

        public ulong GetLastCompletedTick(Float32PipelineWorkingState workingState)
        {
            return workingState.LastCompletedTick;
        }

        public void BeginOuterTransaction()
        {
            m_Products.BeginOuterTransaction();
        }

        public void BeginSimulationStep(Float32PipelineWorkingState workingState, Float32SimulationStep step)
        {
            m_Products.BeginSimulationStep();
            m_WorkingStatePort.Set(workingState.Current, step);
        }

        public void SetCompletedSteps(IReadOnlyList<Float32CompletedSimulationStep> steps)
        {
            m_CompletedStepPort.Set(steps);
        }

        public void ClearTransientState()
        {
            m_WorkingStatePort.Clear();
            m_CompletedStepPort.Clear();
            for (int i = 0; i < m_ActiveStepResultCount; i++)
                m_StepResults[i].ReleaseFrame();
            m_ActiveStepResultCount = 0;
        }

        public void DiscardUnconsumedEvaluations()
        {
            if (m_CharacterEvaluationResults.HasValue)
                m_CharacterEvaluationResults.Read().DiscardUnconsumed();
        }

        public SimulationSessionExecutionPlan<Float32SimulationStep> ReadExecutionPlan()
        {
            return m_Products
                .GetRequired<Float32ExclusiveProductSlot<SimulationSessionExecutionPlan<Float32SimulationStep>>>(
                    SimulationPipelineProducts.ExecutionPlan)
                .Read();
        }

        public void ValidateTargetExecutionPlan(
            SimulationSessionExecutionPlan<Float32SimulationStep> plan,
            SimulationSessionLogicTickContext outer)
        {
            _ = outer;
            if ((plan.Requirements & (SimulationSessionPlanRequirement.Snapshot | SimulationSessionPlanRequirement.StateHash)) != 0 &&
                m_Services.Descriptor.MaximumSessionCheckpointPayloadBytes <= 0)
                throw Failure("snapshot_payload_capacity_missing", "Snapshot and StateHash plans require MaximumSessionCheckpointPayloadBytes.");
            Float32CharacterRuntime characterRuntime = m_CharacterRuntime;
            IReadOnlyList<SimulationActorBinding> roster = m_Roster;
            for (int stepIndex = 0; stepIndex < plan.Steps.Count; stepIndex++)
            {
                Float32SimulationStep step = plan.Steps[stepIndex];
                if (step.Inputs.Count != roster.Count)
                    throw Failure("execution_plan_roster_mismatch", "ExecutionPlan Step input count does not match the locked roster.");
                for (int i = 0; i < step.Inputs.Count; i++)
                {
                    SimulationPipelineActorInput<Float32StepInput> input = step.Inputs[i];
                    SimulationInput value = input.Value.Input;
                    if (!input.ActorId.Equals(roster[i].ActorId) || value == null ||
                        !value.NumericProfile.Equals(characterRuntime.NumericProfile) ||
                        !value.TickSource.Equals(step.Source) || value.Sequence != input.Sequence)
                    {
                        throw Failure("execution_plan_input_mismatch", $"ExecutionPlan input for Actor '{input.ActorId}' is invalid.");
                    }
                }
                ValidateIngress(step.Ingress, characterRuntime.NumericProfile);
            }
        }

        public PipelineRestorePreparation PrepareRestore(
            SimulationRestoreDirective directive,
            Float32PipelineWorkingState workingState,
            PipelineTransactionRuntimeServices services)
        {
            if (m_RestoreSource == null)
                throw Failure("restore_source_missing", "ExecutionPlan requested restore without a bound restore Source.");
            Float32SimulationSessionSnapshot snapshot = m_RestoreSource.GetRequiredSnapshot(directive) ??
                throw Failure("restore_snapshot_missing", "Restore Source returned no Session snapshot.");
            m_SnapshotCodec.RequireRestore(
                services.Descriptor,
                m_CharacterRuntime,
                m_Solver,
                directive,
                snapshot);
            SimulationWorldStateSet restored = m_StateStore.PrepareRestore(snapshot.World);
            Float32CharacterRestoreTransaction character = m_SourceCharacterRestore.Prepare(restored, snapshot.World.WorldHash.Value);
            SimulationSessionRestoreTransaction transaction;
            SimulationPipelineStateRestoreTransaction pipeline = null;
            try
            {
                pipeline = m_SourcePipelineRestore.Prepare(snapshot.Pipeline);
                transaction = m_SourceRestore.Prepare(character,
                    m_SourceWorldRestore.Prepare(restored, snapshot.World.WorldHash.Value), pipeline);
            }
            catch
            {
                pipeline?.Dispose();
                character.Dispose();
                throw;
            }
            return new PipelineRestorePreparation(transaction, snapshot.Pipeline, snapshot.Tick);
        }

        internal void Dispose()
        {
            ClearTransientState();
            m_SnapshotCodec.DisposeCheckpointStorage();
            m_StepProjections = null;
        }

        public bool TryGetCoreStepStage(
            ICompiledSimulationPipelinePassRuntime pass,
            out PipelineTransactionStage stage)
        {
            SimulationPipelinePassId passId = pass.Descriptor.PassId;
            if (passId.Equals(StandardFloat32PipelinePassContracts.AbilityEvaluate.PassId))
            {
                stage = PipelineTransactionStage.Evaluate;
                return true;
            }
            if (passId.Equals(StandardFloat32PipelinePassContracts.WorldResolveBatch.PassId))
            {
                stage = PipelineTransactionStage.ResolveBatch;
                return true;
            }
            if (passId.Equals(StandardFloat32PipelinePassContracts.AbilityFinalize.PassId))
            {
                stage = PipelineTransactionStage.Finalize;
                return true;
            }
            stage = default;
            return false;
        }

        public int FinalizedResultCount => GetFinalizedSlot().UnsealedCount;

        public Float32CompletedSimulationStep CompleteStep(
            SimulationSessionExecutionPlan<Float32SimulationStep> executionPlan,
            Float32SimulationStep step,
            int finalizedStart,
            Float32PipelineWorkingState workingState)
        {
            _ = workingState;
            Float32AppendProductSlot<SimulationActorTickResult> finalized = GetFinalizedSlot();
            int finalizedCount = finalized.UnsealedCount - finalizedStart;
            if (finalizedCount != m_Roster.Count)
                throw Failure("finalized_actor_count_mismatch", "Ability Finalize Pass did not produce exactly one result per Actor.", SimulationSessionFailureStage.Step);
            SimulationTickResult tickResult = m_StepResults[m_ActiveStepResultCount++];
            SimulationActorTickResult[] actorResults = tickResult.ActorArray;
            for (int i = finalizedStart; i < finalized.UnsealedCount; i++)
            {
                SimulationActorTickResult result = finalized.GetUnsealed(i).Value;
                if (result.Tick != step.Tick)
                    throw Failure("finalized_actor_tick_mismatch", "Ability Finalize Pass produced a result for another Tick.", SimulationSessionFailureStage.Step);
                actorResults[i - finalizedStart] = result;
            }
            SimulationTickResult.SortByActorId(actorResults);
            for (int i = 0; i < actorResults.Length; i++)
            {
                if (!actorResults[i].ActorId.Equals(m_Roster[i].ActorId))
                    throw Failure("finalized_actor_roster_mismatch", "Ability Finalize Pass result roster does not match the locked roster.", SimulationSessionFailureStage.Step);
            }
            WorldSolveBatchResult worldResult = m_Products
                .GetRequired<Float32ExclusiveProductSlot<WorldSolveBatchResult>>(
                    SimulationPipelineProducts.WorldSolveBatchResult)
                .Read();
            ValidateWorldResult(worldResult, step.Tick);
            SimulationWorldStateStore.StatePage candidateStorage = m_StateStore.AcquireStatePage();
            tickResult.SetCandidateStorage(candidateStorage);
            SimulationActorState[] nextActors = candidateStorage.State.ActorArray;
            for (int i = 0; i < actorResults.Length; i++)
                nextActors[i] = new SimulationActorState(actorResults[i].ActorId, actorResults[i].State);
            SimulationWorldStateSet candidateState = candidateStorage.State;
            candidateState.CompleteCandidate(step.Tick.Value, worldResult.NextWorldState);
            bool capture = (executionPlan.Requirements &
                (SimulationSessionPlanRequirement.Snapshot | SimulationSessionPlanRequirement.StateHash)) != 0;
            SimulationPipelineStateSnapshot pipelineSnapshot = capture
                ? m_StepProjections.Capture(step.Tick.Value)
                : null;
            tickResult.SetPipelineProjection(pipelineSnapshot);
            SimulationWorldSnapshot worldSnapshot = capture
                ? SimulationWorldSnapshotFactory.Capture(
                    m_CharacterRuntime,
                    step.Tick,
                    nextActors,
                    worldResult.NextWorldState,
                    m_Solver.Descriptor.Capabilities)
                : null;
            Float32SimulationStepSnapshot stepSnapshot = capture
                ? new Float32SimulationStepSnapshot(m_Services.Descriptor.Identity, worldSnapshot, pipelineSnapshot)
                : default;
            tickResult.InitializeFrame(
                m_CharacterRuntime.NumericProfile,
                m_CharacterRuntime.GameplayContentHash,
                step.Tick,
                (executionPlan.Requirements & SimulationSessionPlanRequirement.Snapshot) != 0 ? worldSnapshot : null);
            return new Float32CompletedSimulationStep(step, tickResult, candidateState, stepSnapshot);
        }

        public SimulationPipelineStateSnapshot GetPipelineProjection(Float32CompletedSimulationStep completedStep)
        {
            return completedStep.PipelineProjection;
        }

        public void ApplyCompletedStep(
            Float32PipelineWorkingState workingState,
            Float32CompletedSimulationStep completedStep)
        {
            workingState.Replace(completedStep.State);
        }

        public Float32SimulationCommitBatch FreezeCommitBatch(
            StableHash transactionIdentity,
            ExecutionWorkspaceBuffer<Float32CompletedSimulationStep> completedSteps,
            SessionExecutionWorkspace<
                Float32CompletedSimulationStep,
                Float32SourceEgressRecord> workspace)
        {
            SimulationPipelineOutputDispositionSet dispositions = m_Products
                .GetRequired<Float32ExclusiveProductSlot<SimulationPipelineOutputDispositionSet>>(
                    SimulationPipelineProducts.OutputDispositionSet)
                .Read();
            if (!dispositions.TransactionIdentity.Equals(transactionIdentity))
                throw Failure("output_disposition_transaction_mismatch", "OutputDispositionSet belongs to another outer transaction.", SimulationSessionFailureStage.Egress);
            IReadOnlyList<Float32SourceEgressRecord> sourceEgress = ReadSourceEgress(workspace.Egress);
            return new Float32SimulationCommitBatch(
                transactionIdentity,
                completedSteps,
                dispositions,
                sourceEgress);
        }

        public void Commit(Float32SimulationCommitBatch commitBatch)
        {
            m_Committer.Commit(commitBatch);
        }

        public void CompleteStatePublish(Float32PipelineWorkingState workingState)
        {
            for (int step = 0; step < m_ActiveStepResultCount; step++)
                for (int actor = 0; actor < m_Roster.Count; actor++)
                    Float32PipelineDiagnostics.PublishOperations(m_Diagnostics,
                        m_StepResults[step].Actors[actor].TraceRecords, 0);
            SimulationWorldStateSet state = workingState.Current;
            PublishTimelineOwners(in state);
        }

        void PublishTimelineOwners(in SimulationWorldStateSet state)
        {
            for (int index = 0; index < m_Roster.Count; index++)
                m_Roster[index].TimelineRuntime?.ReleaseUnreferencedPlaybacks(state.Actors[index].State.TimelineSnapshots, state.LastCompletedTick);
        }

        public void PublishWorkingState(Float32PipelineWorkingState workingState)
        {
            m_StateStore.ReplaceValidated(workingState.Current);
        }

        public void RestoreTimelineBaseline()
        {
            for (int actorIndex = 0; actorIndex < m_Roster.Count; actorIndex++)
            {
                IReadOnlyList<AbilityTimelineRuntimeSnapshot> snapshots = m_StateStore.Current.Actors[actorIndex].State.TimelineSnapshots;
                for (int index = 0; index < snapshots.Count; index++)
                    m_Roster[actorIndex].TimelineRuntime.ApplyRestore(snapshots[index], m_StateStore.Current.LastCompletedTick);
                m_Roster[actorIndex].TimelineRuntime?.DiscardUnpublishedPlaybacks(snapshots);
            }
        }

        public void RestoreSolverBaseline()
        {
            m_Solver.Restore(m_StateStore.Current.WorldState);
        }

        public void PublishTrace(PipelineTransactionTrace trace)
        {
            if (!m_Diagnostics.IsEnabled)
                return;
            m_Diagnostics.PublishPipeline(new SimulationPipelineTraceRecord(
                m_Services.Descriptor.Identity,
                m_Services.Plan.Identity,
                trace.Source,
                trace.CompletedTick,
                (SimulationPipelineTraceKind)(byte)trace.Kind,
                trace.Success,
                trace.Detail,
                trace.Phase,
                trace.PassId,
                trace.PassVersion,
                trace.ScheduleStatus,
                trace.RestoreRequested,
                trace.StepCount,
                trace.ElapsedStopwatchTicks,
                trace.ProductInputs,
                trace.ProductOutputs,
                trace.SnapshotParticipant,
                trace.SnapshotHash));
        }

        void ValidateWorldResult(WorldSolveBatchResult result, SimulationTick tick)
        {
            if (result == null || result.Tick != tick ||
                !result.SolverId.Equals(m_Solver.Descriptor.ImplementationId) ||
                !CanonicalTextValue.Equals(result.SolverVersion, m_Solver.Descriptor.Version, StringComparison.Ordinal) ||
                !result.NextWorldState.WorldRevision.Equals(m_StateStore.Current.WorldState.WorldRevision))
            {
                throw Failure(
                    "world_result_identity_mismatch",
                    "WorldSolve Pass returned a result for another Tick, Solver or WorldRevision.",
                    SimulationSessionFailureStage.Step);
            }
        }

        void ValidateIngress(
            IReadOnlyList<SimulationPipelineTypedIngress<SimulationIngress>> ingressValues,
            SimulationNumericProfile numericProfile)
        {
            for (int i = 0; i < ingressValues.Count; i++)
            {
                SimulationPipelineTypedIngress<SimulationIngress> ingress = ingressValues[i];
                SimulationIngressHeader header = ingress.Value.Header;
                if (!header.ActorId.Equals(ingress.ActorId) ||
                    !header.NumericProfile.Equals(numericProfile) ||
                    header.SourceTick != ingress.Source.SourceTick || header.Sequence != ingress.Sequence ||
                    !CanonicalTextValue.Equals(header.FactIdentity.Value, ingress.FactIdentity, StringComparison.Ordinal))
                {
                    throw Failure("execution_plan_ingress_mismatch", "ExecutionPlan typed ingress identity is invalid.");
                }
            }
        }

        Float32AppendProductSlot<SimulationActorTickResult> GetFinalizedSlot()
        {
            return m_Products.GetRequired<Float32AppendProductSlot<SimulationActorTickResult>>(
                SimulationPipelineProducts.FinalizedStepResult);
        }

        IReadOnlyList<Float32SourceEgressRecord> ReadSourceEgress(
            ExecutionWorkspaceBuffer<Float32SourceEgressRecord> values)
        {
            if (!m_Products.TryGet<Float32AppendProductSlot<Float32SourceEgressRecord>>(
                    SimulationPipelineProducts.SourceEgress,
                    out Float32AppendProductSlot<Float32SourceEgressRecord> slot))
            {
                return values;
            }
            values.EnsureCapacity(slot.Count);
            for (int i = 0; i < slot.Count; i++)
                values.Add(slot.Get(i).Value);
            return values;
        }

        static SimulationSessionCompositionException Failure(
            string code,
            string message,
            SimulationSessionFailureStage stage = SimulationSessionFailureStage.Schedule)
        {
            return PipelineTransactionFailure.Create(stage, code, message);
        }
    }
}
