using System;
using System.Collections.Generic;

using ThirdPersonPerformance.Instrumentation;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedPipelineTransaction : IDisposable
    {
        readonly FixedPipelineTransactionPort m_Target;
        readonly PipelineTransactionCoordinator<
            FixedSimulationStep,
            FixedPipelineWorkingState,
            FixedCompletedSimulationStep,
            FixedSourceEgressRecord,
            FixedSimulationCommitBatch> m_Coordinator;

        internal FixedPipelineTransaction(
            SimulationSessionCompositionDescriptor descriptor,
            CompiledSimulationPipelinePlan plan,
            FixedCharacterRuntime characterRuntime,
            SimulationWorldStateStore stateStore,
            ICharacterWorldSolver solver,
            IFixedSimulationRestoreSource restoreSource,
            IFixedSimulationSessionSnapshotCodec snapshotCodec,
            IFixedSimulationCommitter committer,
            ISimulationDiagnosticsSink diagnostics,
            IReadOnlyList<IFixedCompiledPipelinePassRuntime> passes,
            IReadOnlyList<ISimulationPipelineStateParticipant> stateParticipants,
            IReadOnlyList<ISimulationPipelineReconstructiblePass> reconstructiblePasses,
            FixedPipelineProductStore products,
            FixedWorkingStatePort workingStatePort,
            FixedCompletedStepPort completedStepPort,
            int maximumSteps,
            IFixedCommittedInputReadPort availabilityInput)
        {
            var services = new PipelineTransactionRuntimeServices(
                descriptor,
                plan,
                passes,
                stateParticipants,
                reconstructiblePasses);
            m_Target = new FixedPipelineTransactionPort(
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
                availabilityInput);
            m_Coordinator = new PipelineTransactionCoordinator<
                FixedSimulationStep,
                FixedPipelineWorkingState,
                FixedCompletedSimulationStep,
                FixedSourceEgressRecord,
                FixedSimulationCommitBatch>(services, m_Target);
        }

        [PerformanceProbe("simulation.pipeline.transaction")]
        public void Execute(SimulationSessionLogicTickContext outer)
        {
            m_Coordinator.Execute(outer);
        }

        public void RestoreRecordedControlInput(ArraySegment<byte> bytes) => m_Target.RestoreRecordedControlInput(bytes);
        public void RestoreRecordedControlSources(ArraySegment<byte> bytes) => m_Target.RestoreRecordedControlSources(bytes);

        public void QueryAbilityAvailability(ActorId actorId, GameplayAbilityRulePhase phase,
            out GameplayAbilityAvailabilityResult result) => m_Target.QueryAbilityAvailability(actorId, phase, out result);

        public SimulationSessionCheckpoint CaptureCheckpoint() => m_Target.CaptureCheckpoint();

        public void RestoreCheckpoint(SimulationSessionCheckpoint checkpoint) => m_Target.RestoreCheckpoint(checkpoint);

        public void Dispose()
        {
            m_Target.ClearTransientState();
            m_Target.DisposeCheckpointStorage();
        }
        public void PrepareRecordedFrames(int capacity) => m_Target.PrepareRecordedFrames(capacity);
        public ReadOnlyMemory<byte> RecordFrame(ulong presentationFrame) => m_Target.RecordFrame(presentationFrame);
        public SimulationSessionCheckpoint CaptureRecordedCheckpoint(ulong presentationFrame) =>
            m_Target.CaptureRecordedCheckpoint(presentationFrame);
    }

    internal sealed class FixedPipelineTransactionPort :
        IPipelineTransactionTargetPort<
            FixedSimulationStep,
            FixedPipelineWorkingState,
            FixedCompletedSimulationStep,
            FixedSourceEgressRecord,
            FixedSimulationCommitBatch>
    {
        readonly IFixedSimulationRestoreSource m_RestoreSource;
        readonly PipelineTransactionRuntimeServices m_Services;
        readonly FixedCharacterRuntime m_CharacterRuntime;
        readonly IFixedCommittedInputReadPort m_AvailabilityInput;
        IReadOnlyList<SimulationActorBinding> m_Roster;
        readonly SimulationWorldStateStore m_StateStore;
        readonly ICharacterWorldSolver m_Solver;
        readonly ISimulationDiagnosticsSink m_Diagnostics;
        readonly IFixedSimulationSessionSnapshotCodec m_SnapshotCodec;
        readonly IFixedSimulationCommitter m_Committer;
        readonly FixedPipelineProductStore m_Products;
        readonly IReadOnlySimulationPipelineProductPort<FixedCharacterEvaluationResultBatch> m_CharacterEvaluationResults;
        readonly FixedWorkingStatePort m_WorkingStatePort;
        readonly FixedCompletedStepPort m_CompletedStepPort;
        FixedPipelineWorkingState m_WorkingStateShell;
        readonly List<SimulationTickResult> m_StepResults;
        readonly FixedSimulationSessionSnapshot[] m_StepSnapshots;
        ISimulationPipelineStepStateStorage[] m_RecordSources;
        SimulationPipelinePassStateSnapshot[] m_RecordScratch;
        int m_RecordedInputIndex;
        int m_ActiveStepResultCount;
        readonly FixedPipelineWorkingState m_CheckpointWorking;
        readonly FixedCharacterRestoreTransaction m_CheckpointCharacterRestore;
        readonly FixedCharacterRestoreTransaction m_SourceCharacterRestore;
        readonly FixedWorldRestoreTransaction m_CheckpointWorldRestore;
        readonly FixedWorldRestoreTransaction m_SourceWorldRestore;
        readonly SimulationPipelineStateRestoreTransaction m_CheckpointPipelineRestore;
        readonly SimulationPipelineStateRestoreTransaction m_SourcePipelineRestore;
        readonly SimulationSessionRestoreTransaction m_CheckpointRestore;
        readonly SimulationSessionRestoreTransaction m_SourceRestore;

        public FixedPipelineTransactionPort(
            PipelineTransactionRuntimeServices services,
            FixedCharacterRuntime characterRuntime,
            SimulationWorldStateStore stateStore,
            ICharacterWorldSolver solver,
            ISimulationDiagnosticsSink diagnostics,
            IFixedSimulationRestoreSource restoreSource,
            IFixedSimulationSessionSnapshotCodec snapshotCodec,
            IFixedSimulationCommitter committer,
            FixedPipelineProductStore products,
            FixedWorkingStatePort workingStatePort,
            FixedCompletedStepPort completedStepPort,
            int maximumSteps,
            IFixedCommittedInputReadPort availabilityInput)
        {
            m_Services = services ?? throw new ArgumentNullException(nameof(services));
            m_CharacterRuntime = characterRuntime ?? throw new ArgumentNullException(nameof(characterRuntime));
            m_Roster = m_CharacterRuntime.Roster;
            m_AvailabilityInput = availabilityInput;
            for (int actor = 0; actor < m_Roster.Count; actor++)
                m_Roster[actor].PrepareEvaluationOutputCapacity(maximumSteps);
            m_StepSnapshots = new FixedSimulationSessionSnapshot[maximumSteps];
            int retainedCapacity = checked(maximumSteps + services.Descriptor.RecordedFrameCapacity);
            m_StepResults = new List<SimulationTickResult>(maximumSteps);
            for (int step = 0; step < maximumSteps; step++)
                m_StepResults.Add(new SimulationTickResult(m_Roster.Count));
            m_StateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
            m_StateStore.PrepareStepStorage(retainedCapacity);
            m_Solver = solver ?? throw new ArgumentNullException(nameof(solver));
            m_Solver.PrepareStepStorage(retainedCapacity);
            m_Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            m_RestoreSource = restoreSource;
            m_SnapshotCodec = snapshotCodec ?? throw new ArgumentNullException(nameof(snapshotCodec));
            GameplayEffectSessionCapacity checkpointCapacity = m_Roster[0].SessionCapacity;
            var snapshotOwner = committer as IFixedSimulationSnapshotRetentionOwner;
            int stepSnapshotCapacity = snapshotOwner == null ? 0 :
                checked(snapshotOwner.MaximumRetainedStepSnapshots + checkpointCapacity.CandidateTicks);
            m_SnapshotCodec.PrepareCheckpointStorage(services.Descriptor, m_CharacterRuntime, services.StateParticipants,
                in checkpointCapacity, m_StateStore.Current.WorldState, stepSnapshotCapacity);
            using (SimulationCharacterStatePayloadStorage.Page initialEncoding = m_Roster[0].RuntimeStatePages.EncodingStorage?.Acquire())
            {
#if UNITY_EDITOR
                SimulationSnapshotCaptureProfile initialProfile = default;
#endif
                if (initialEncoding != null)
                    for (int actor = 0; actor < m_Roster.Count; actor++)
                        FixedCharacterRuntimeStateCodec.Encode(m_StateStore.Current.Actors[actor].State, initialEncoding
#if UNITY_EDITOR
                            , ref initialProfile
#endif
                            );
            }
            if (snapshotOwner != null)
                snapshotOwner.PrepareSnapshotStorage(m_SnapshotCodec, in checkpointCapacity);
            if (services.Descriptor.CapturesState)
            {
                m_StateStore.PrepareRestoreStorage(checkpointCapacity.StatePageCount, solver.MaximumStatePayloadBytes);
                int maximumWorldPayloadBytes = WorldSimulationStateCodec.MaximumEncodedBytes(m_StateStore.Current.WorldState, solver.MaximumStatePayloadBytes);
                m_CheckpointWorking = new FixedPipelineWorkingState(m_StateStore.Current);
                m_CheckpointCharacterRestore = new FixedCharacterRestoreTransaction(m_Roster);
                m_SourceCharacterRestore = new FixedCharacterRestoreTransaction(m_Roster);
                m_CheckpointWorldRestore = new FixedWorldRestoreTransaction(m_Solver, maximumWorldPayloadBytes);
                m_SourceWorldRestore = new FixedWorldRestoreTransaction(m_Solver, maximumWorldPayloadBytes);
                m_CheckpointPipelineRestore = new SimulationPipelineStateRestoreTransaction(services.Plan, services.StateParticipants);
                m_SourcePipelineRestore = new SimulationPipelineStateRestoreTransaction(services.Plan, services.StateParticipants);
                m_CheckpointRestore = new SimulationSessionRestoreTransaction();
                m_SourceRestore = new SimulationSessionRestoreTransaction();
            }
            m_Committer = committer ?? throw new ArgumentNullException(nameof(committer));
            m_Products = products ?? throw new ArgumentNullException(nameof(products));
            m_CharacterEvaluationResults = m_Products.GetRequired<FixedExclusiveProductSlot<FixedCharacterEvaluationResultBatch>>(
                SimulationPipelineProducts.CharacterEvaluationResults);
            m_WorkingStatePort = workingStatePort ?? throw new ArgumentNullException(nameof(workingStatePort));
            m_CompletedStepPort = completedStepPort ?? throw new ArgumentNullException(nameof(completedStepPort));
            if (services.Descriptor.RecordedFrameCapacity > 0)
                PrepareRecordedFrameSources();
        }

        public string TransactionIdentityDomain => "fixed-pipeline-transaction/1";
        public bool DiagnosticsEnabled => m_Diagnostics.IsEnabled;
        internal void RestoreRecordedControlInput(ArraySegment<byte> bytes) => m_AvailabilityInput.RestoreControlInput(bytes);
        internal void RestoreRecordedControlSources(ArraySegment<byte> bytes) => m_AvailabilityInput.RestoreControlSources(bytes);

        internal void QueryAbilityAvailability(ActorId actorId, GameplayAbilityRulePhase phase,
            out GameplayAbilityAvailabilityResult result)
        {
            if (m_AvailabilityInput == null)
                throw new NotSupportedException("This Session does not publish a current input read owner for Availability.");
            var current = m_StateStore.Current;
            m_CharacterRuntime.QueryAbilityAvailability(actorId, in current, m_AvailabilityInput, phase, out result);
        }

        public ulong BaselineCompletedTick => m_StateStore.Current.LastCompletedTick;
        public WorldRevision BaselineWorldRevision => m_StateStore.Current.WorldState.WorldRevision;

        void PrepareRecordedFrameSources()
        {
            int participantCount = m_Services.StateParticipants.Count;
            m_RecordSources = new ISimulationPipelineStepStateStorage[participantCount];
            m_RecordScratch = new SimulationPipelinePassStateSnapshot[participantCount];
            m_RecordedInputIndex = -1;
            for (int i = 0; i < participantCount; i++)
            {
                m_RecordSources[i] = (ISimulationPipelineStepStateStorage)m_Services.StateParticipants[i];
                if (m_Services.StateParticipants[i].StateIdentity.StateSchemaId ==
                    StandardFixedLocalPipelinePassContracts.LocalControlInputStateSchemaId)
                    m_RecordedInputIndex = i;
            }
            if (m_RecordedInputIndex == -1)
                throw new InvalidOperationException("Frame recording requires the formal Fixed Local control input state participant.");
        }

        public void PrepareRecordedFrames(int capacity)
        {
            int declaredCapacity = m_Services.Descriptor.RecordedFrameCapacity;
            if ((uint)capacity > (uint)declaredCapacity)
                throw new GameplayEffectPreparationException(new GameplayEffectPreparationFailure(
                    GameplayEffectPreparationFailureKind.ContentRequirementExceedsCapacity,
                    GameplayEffectCapacityDimension.RecordedFrames, m_Services.Descriptor.SessionId.Value,
                    capacity, declaredCapacity));
            int participantCount = m_Services.StateParticipants.Count;
            m_StateStore.PrepareRecordedFrames(capacity, participantCount);
        }

        public ReadOnlyMemory<byte> RecordFrame(ulong presentationFrame)
        {
            int captured = 0;
            try
            {
                for (; captured < m_RecordSources.Length; captured++)
                    m_RecordScratch[captured] = m_RecordSources[captured].CaptureStepState();
                m_StateStore.RecordFrame(presentationFrame, m_RecordScratch);
                ReadOnlyMemory<byte> inputState = m_RecordScratch[m_RecordedInputIndex].Payload;
                captured = 0;
                return inputState;
            }
            finally
            {
                for (int i = 0; i < captured; i++)
                    m_RecordScratch[i].ReleaseStepStorage();
                Array.Clear(m_RecordScratch, 0, m_RecordScratch.Length);
            }
        }

        public SimulationSessionCheckpoint CaptureCheckpoint()
        {
            SimulationWorldStateSet state = m_StateStore.Current;
            if (state.LastCompletedTick == 0)
                throw new InvalidOperationException("Session checkpoint requires a completed Logic Tick.");
            return m_SnapshotCodec.CaptureCheckpoint(in state);
        }

        public SimulationSessionCheckpoint CaptureRecordedCheckpoint(ulong presentationFrame)
        {
            if (!m_StateStore.TryGetRecordedFrame(presentationFrame, out SimulationWorldStateSet state,
                    out SimulationPipelinePassStateSnapshot[] participants))
                throw new InvalidOperationException("The presentation frame has no retained execution state.");
            return m_SnapshotCodec.CaptureCheckpoint(in state, participants);
        }

        public void RestoreCheckpoint(SimulationSessionCheckpoint checkpoint)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            if (!checkpoint.SessionId.Equals(m_Services.Descriptor.SessionId))
                throw new InvalidOperationException("Checkpoint belongs to another Simulation Session.");
            FixedSimulationSessionSnapshot snapshot = m_SnapshotCodec.ReadCheckpoint(checkpoint);
            SimulationRestoreDirective directive = checkpoint.RestoreDirective;
            m_SnapshotCodec.RequireRestore(
                m_Services.Descriptor,
                m_CharacterRuntime,
                m_Solver,
                directive,
                snapshot);
            SimulationWorldStateSet restored = m_StateStore.PrepareRestore(snapshot.World);
            m_CheckpointWorking.Replace(m_StateStore.Current);
            FixedPipelineWorkingState working = m_CheckpointWorking;
            FixedCharacterRestoreTransaction character = m_CheckpointCharacterRestore.Prepare(working, restored, snapshot.World.WorldHash.Value);
            SimulationSessionRestoreTransaction restore;
            SimulationPipelineStateRestoreTransaction preparedPipeline = null;
            try
            {
                SimulationPipelineStateRestoreTransaction pipeline = m_CheckpointPipelineRestore.Prepare(snapshot.Pipeline);
                preparedPipeline = pipeline;
                FixedWorldRestoreTransaction world = m_CheckpointWorldRestore.Prepare(working, restored, snapshot.World.WorldHash.Value);
                restore = m_CheckpointRestore.Prepare(character, world, pipeline);
            }
            catch
            {
                preparedPipeline?.Dispose();
                character.Dispose();
                throw;
            }
            using (restore)
            {
                restore.ApplyAndValidate();
                m_StateStore.ReplaceValidated(restored);
                restore.CompleteAfterAtomicSessionPublish();
            }
            PublishTimelineOwners(in restored);
        }

        public void DisposeCheckpointStorage()
        {
            if (m_Committer is IFixedSimulationSnapshotRetentionOwner snapshotOwner)
                snapshotOwner.ClearSnapshotStorage();
            m_SnapshotCodec.DisposeCheckpointStorage();
        }

        public FixedPipelineWorkingState CreateWorkingState()
        {
            if (m_WorkingStateShell == null)
                m_WorkingStateShell = new FixedPipelineWorkingState(m_StateStore.Current);
            else
                m_WorkingStateShell.Replace(m_StateStore.Current);
            return m_WorkingStateShell;
        }

        public ulong GetLastCompletedTick(FixedPipelineWorkingState workingState)
        {
            return workingState.LastCompletedTick;
        }

        public void BeginOuterTransaction()
        {
            m_Products.BeginOuterTransaction();
            for (int actor = 0; actor < m_Roster.Count; actor++)
                for (int ability = 0; ability < m_Roster[actor].Invocations.Length; ability++)
                    m_Roster[actor].Invocations[ability].BeginBlackboardTransaction();
        }

        public void BeginSimulationStep(FixedPipelineWorkingState workingState, FixedSimulationStep step)
        {
            m_Products.BeginSimulationStep();
            m_WorkingStatePort.Set(workingState.Current, step);
        }

        public void SetCompletedSteps(IReadOnlyList<FixedCompletedSimulationStep> steps)
        {
            m_CompletedStepPort.Set(steps);
        }

        public void ClearTransientState()
        {
            m_WorkingStatePort.Clear();
            m_CompletedStepPort.Clear();
            for (int i = 0; i < m_ActiveStepResultCount; i++)
            {
                m_StepResults[i].ReleaseFrame();
                m_StepSnapshots[i]?.ReleaseStorage();
                m_StepSnapshots[i] = null;
            }
            m_ActiveStepResultCount = 0;
        }

        public void DiscardUnconsumedEvaluations()
        {
            if (m_CharacterEvaluationResults.HasValue)
                m_CharacterEvaluationResults.Read().DiscardUnconsumed();
        }

        public SimulationSessionExecutionPlan<FixedSimulationStep> ReadExecutionPlan()
        {
            return m_Products
                .GetRequired<FixedExclusiveProductSlot<SimulationSessionExecutionPlan<FixedSimulationStep>>>(
                    SimulationPipelineProducts.ExecutionPlan)
                .Read();
        }

        public void ValidateTargetExecutionPlan(
            SimulationSessionExecutionPlan<FixedSimulationStep> plan,
            SimulationSessionLogicTickContext outer)
        {
            _ = outer;
            if ((plan.Requirements & (SimulationSessionPlanRequirement.Snapshot | SimulationSessionPlanRequirement.StateHash)) != 0 &&
                m_Services.Descriptor.MaximumSessionCheckpointPayloadBytes <= 0)
                throw Failure("snapshot_payload_capacity_missing", "Snapshot and StateHash plans require MaximumSessionCheckpointPayloadBytes.");
            FixedCharacterRuntime characterRuntime = m_CharacterRuntime;
            IReadOnlyList<SimulationActorBinding> roster = m_Roster;
            for (int stepIndex = 0; stepIndex < plan.Steps.Count; stepIndex++)
            {
                FixedSimulationStep step = plan.Steps[stepIndex];
                if (step.Inputs.Count != roster.Count)
                    throw Failure("execution_plan_roster_mismatch", "ExecutionPlan Step input count does not match the locked roster.");
                for (int i = 0; i < step.Inputs.Count; i++)
                {
                    SimulationPipelineActorInput<FixedStepInput> input = step.Inputs[i];
                    SimulationInput value = input.Value.Input;
                    if (!input.ActorId.Equals(roster[i].ActorId) || !value.IsValid ||
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
            FixedPipelineWorkingState workingState,
            PipelineTransactionRuntimeServices services)
        {
            if (m_RestoreSource == null)
                throw Failure("restore_source_missing", "ExecutionPlan requested restore without a bound restore Source.");
            FixedSimulationSessionSnapshot snapshot = m_RestoreSource.GetRequiredSnapshot(directive) ??
                throw Failure("restore_snapshot_missing", "Restore Source returned no Session snapshot.");
            m_SnapshotCodec.RequireRestore(
                services.Descriptor,
                m_CharacterRuntime,
                m_Solver,
                directive,
                snapshot);
            SimulationWorldStateSet restored = m_StateStore.PrepareRestore(snapshot.World);
            FixedCharacterRestoreTransaction character = m_SourceCharacterRestore.Prepare(
                workingState, restored, snapshot.World.WorldHash.Value, snapshot);
            SimulationSessionRestoreTransaction transaction;
            SimulationPipelineStateRestoreTransaction preparedPipeline = null;
            try
            {
                SimulationPipelineStateRestoreTransaction pipeline = m_SourcePipelineRestore.Prepare(snapshot.Pipeline);
                preparedPipeline = pipeline;
                FixedWorldRestoreTransaction world = m_SourceWorldRestore.Prepare(workingState, restored, snapshot.World.WorldHash.Value);
                transaction = m_SourceRestore.Prepare(character, world, pipeline);
            }
            catch
            {
                preparedPipeline?.Dispose();
                character.Dispose();
                throw;
            }
            return new PipelineRestorePreparation(transaction, snapshot.Pipeline, snapshot.Tick);
        }

        public bool TryGetCoreStepStage(
            ICompiledSimulationPipelinePassRuntime pass,
            out PipelineTransactionStage stage)
        {
            SimulationPipelinePassId passId = pass.Descriptor.PassId;
            if (passId.Equals(StandardFixedPipelinePassContracts.AbilityEvaluate.PassId))
            {
                stage = PipelineTransactionStage.Evaluate;
                return true;
            }
            if (passId.Equals(StandardFixedPipelinePassContracts.WorldResolveBatch.PassId))
            {
                stage = PipelineTransactionStage.ResolveBatch;
                return true;
            }
            if (passId.Equals(StandardFixedPipelinePassContracts.AbilityFinalize.PassId))
            {
                stage = PipelineTransactionStage.Finalize;
                return true;
            }
            stage = default;
            return false;
        }

        public int FinalizedResultCount => GetFinalizedSlot().UnsealedCount;

        public FixedCompletedSimulationStep CompleteStep(
            SimulationSessionExecutionPlan<FixedSimulationStep> executionPlan,
            FixedSimulationStep step,
            int finalizedStart,
            FixedPipelineWorkingState workingState)
        {
            _ = workingState;
            FixedAppendProductSlot<SimulationActorTickResult> finalized = GetFinalizedSlot();
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
                .GetRequired<FixedExclusiveProductSlot<WorldSolveBatchResult>>(
                    SimulationPipelineProducts.WorldSolveBatchResult)
                .Read();
            ValidateWorldResult(worldResult, step.Tick);
            SimulationWorldStateSet candidateState = m_StateStore.StateStorage.CreateCandidate(
                step.Tick, actorResults, worldResult.NextWorldState);
            try
            {
                bool capture = (executionPlan.Requirements &
                    (SimulationSessionPlanRequirement.Snapshot | SimulationSessionPlanRequirement.StateHash)) != 0;
                FixedSimulationSessionSnapshot snapshot = capture
                    ? m_SnapshotCodec.CaptureStepSnapshot(in candidateState)
                    : null;
                m_StepSnapshots[m_ActiveStepResultCount - 1] = snapshot;
                SimulationWorldSnapshot worldSnapshot = snapshot?.World;
                FixedSimulationStepSnapshot stepSnapshot = capture
                    ? new FixedSimulationStepSnapshot(snapshot)
                    : default;
                tickResult.InitializeFrame(
                    m_CharacterRuntime.NumericProfile,
                    m_CharacterRuntime.GameplayContentHash,
                    step.Tick,
                    (executionPlan.Requirements & SimulationSessionPlanRequirement.Snapshot) != 0 ? worldSnapshot : null,
                    candidateState);
                return new FixedCompletedSimulationStep(step, tickResult, candidateState, stepSnapshot);
            }
            finally
            {
                candidateState.Release();
            }
        }

        public SimulationPipelineStateSnapshot GetPipelineProjection(FixedCompletedSimulationStep completedStep)
        {
            return completedStep.PipelineProjection;
        }

        public void ApplyCompletedStep(
            FixedPipelineWorkingState workingState,
            FixedCompletedSimulationStep completedStep)
        {
            workingState.Replace(completedStep.State);
        }

        public FixedSimulationCommitBatch FreezeCommitBatch(
            StableHash transactionIdentity,
            ExecutionWorkspaceBuffer<FixedCompletedSimulationStep> completedSteps,
            SessionExecutionWorkspace<
                FixedCompletedSimulationStep,
                FixedSourceEgressRecord> workspace)
        {
            SimulationPipelineOutputDispositionSet dispositions = m_Products
                .GetRequired<FixedExclusiveProductSlot<SimulationPipelineOutputDispositionSet>>(
                    SimulationPipelineProducts.OutputDispositionSet)
                .Read();
            if (!dispositions.TransactionIdentity.Equals(transactionIdentity))
                throw Failure("output_disposition_transaction_mismatch", "OutputDispositionSet belongs to another outer transaction.", SimulationSessionFailureStage.Egress);
            IReadOnlyList<FixedSourceEgressRecord> sourceEgress = ReadSourceEgress(workspace.Egress);
            return new FixedSimulationCommitBatch(
                transactionIdentity,
                completedSteps,
                dispositions,
                sourceEgress);
        }

        public void Commit(FixedSimulationCommitBatch commitBatch)
        {
            m_Committer.Commit(commitBatch);
        }

        public void CompleteStatePublish(FixedPipelineWorkingState workingState)
        {
            for (int step = 0; step < m_ActiveStepResultCount; step++)
                for (int actor = 0; actor < m_Roster.Count; actor++)
                    FixedPipelineDiagnostics.PublishOperations(m_Diagnostics,
                        m_StepResults[step].Actors[actor].TraceRecords, 0);
            for (int actor = 0; actor < m_Roster.Count; actor++)
                for (int ability = 0; ability < m_Roster[actor].Invocations.Length; ability++)
                    m_Roster[actor].Invocations[ability].PublishBlackboardResults();
            SimulationWorldStateSet state = workingState.Current;
            PublishTimelineOwners(in state);
        }

        void PublishTimelineOwners(in SimulationWorldStateSet state)
        {
            for (int index = 0; index < m_Roster.Count; index++)
                m_Roster[index].TimelineRuntime?.ReleaseUnreferencedPlaybacks(state.Actors[index].State.TimelineSnapshots, state.LastCompletedTick);
        }

        public void PublishWorkingState(FixedPipelineWorkingState workingState)
        {
            m_StateStore.ReplaceValidated(workingState.Current);
        }

        public void RestoreTimelineBaseline()
        {
            FixedGameplayStep.AdoptTimelineBaseline(m_Roster, m_StateStore.Current, true);
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
                trace.SnapshotHash
#if UNITY_EDITOR
                , trace.Kind == PipelineTransactionTraceKind.StepCompleted
                    ? m_StepSnapshots[m_ActiveStepResultCount - 1]?.CaptureProfile ?? default
                    : default
#endif
                ));
        }

        void ValidateWorldResult(WorldSolveBatchResult result, SimulationTick tick)
        {
            if (result.Tick != tick ||
                !result.SolverId.Equals(m_Solver.Descriptor.ImplementationId) ||
                !string.Equals(result.SolverVersion, m_Solver.Descriptor.Version, StringComparison.Ordinal) ||
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
                    !header.FactIdentity.Equals(new StableHash(ingress.FactIdentity)))
                {
                    throw Failure("execution_plan_ingress_mismatch", "ExecutionPlan typed ingress identity is invalid.");
                }
            }
        }

        FixedAppendProductSlot<SimulationActorTickResult> GetFinalizedSlot()
        {
            return m_Products.GetRequired<FixedAppendProductSlot<SimulationActorTickResult>>(
                SimulationPipelineProducts.FinalizedStepResult);
        }

        IReadOnlyList<FixedSourceEgressRecord> ReadSourceEgress(
            ExecutionWorkspaceBuffer<FixedSourceEgressRecord> values)
        {
            if (!m_Products.TryGet<FixedAppendProductSlot<FixedSourceEgressRecord>>(
                    SimulationPipelineProducts.SourceEgress,
                    out FixedAppendProductSlot<FixedSourceEgressRecord> slot))
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
