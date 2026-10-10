using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using ThirdPersonPerformance.Instrumentation;

namespace ThirdPersonSimulation
{
    internal sealed class PipelineTransactionCoordinator<
        TStep,
        TWorkingState,
        TCompletedStep,
        TEgressRecord,
        TCommitBatch>
        where TStep : SimulationPipelineStep
        where TWorkingState : class
        where TCompletedStep : struct
    {
        readonly PipelineTransactionRuntimeServices m_Services;
        readonly IPipelineTransactionTargetPort<
            TStep,
            TWorkingState,
            TCompletedStep,
            TEgressRecord,
            TCommitBatch> m_Target;
        readonly SessionExecutionWorkspace<TCompletedStep, TEgressRecord> m_Workspace =
            new SessionExecutionWorkspace<TCompletedStep, TEgressRecord>();
        readonly SimulationPipelineStateCheckpointSet m_PipelineCheckpoints;
        readonly Dictionary<SimulationPipelinePassId, PassProductTrace> m_PassProductTraces =
            new Dictionary<SimulationPipelinePassId, PassProductTrace>();

        public PipelineTransactionCoordinator(
            PipelineTransactionRuntimeServices services,
            IPipelineTransactionTargetPort<
                TStep,
                TWorkingState,
                TCompletedStep,
                TEgressRecord,
                TCommitBatch> target)
        {
            m_Services = services ?? throw new ArgumentNullException(nameof(services));
            m_Target = target ?? throw new ArgumentNullException(nameof(target));
            m_PipelineCheckpoints = new SimulationPipelineStateCheckpointSet(m_Services.StateParticipants);
            RequireStepPassLayout();
            for (int i = 0; i < m_Services.Passes.Count; i++)
            {
                SimulationPipelinePassDescriptor descriptor = m_Services.Passes[i].Descriptor;
                if (!m_PassProductTraces.TryAdd(descriptor.PassId, BuildProductTrace(descriptor)))
                    throw new InvalidOperationException($"Pipeline contains duplicate PassId '{descriptor.PassId}'.");
            }
        }

        public void Execute(SimulationSessionLogicTickContext outer)
        {
            ExecuteTransaction(outer);
        }

        void ExecuteTransaction(SimulationSessionLogicTickContext outer)
        {
            ulong beforeCompletedTick = m_Target.BaselineCompletedTick;
            TWorkingState working = m_Target.CreateWorkingState();
            StableHash transactionIdentity = ComputeTransactionIdentity(
                m_Target.TransactionIdentityDomain,
                m_Services.Descriptor.Identity.Value,
                m_Services.Plan.PlanHash,
                outer.Source.Kind,
                outer.Source.ClockId,
                outer.Source.SourceTick,
                beforeCompletedTick);
            PublishPipeline(
                outer.Source,
                beforeCompletedTick,
                PipelineTransactionTraceKind.OuterTickStarted,
                true,
                "Outer Pipeline transaction started.");
            SimulationPipelineStateCheckpointSet beforePipeline = m_PipelineCheckpoints.Capture();
            SimulationSessionRestoreTransaction restoreTransaction = null;
            ExecutionWorkspaceLease workspaceLease = default;
            bool solverTouched = false;
            bool statePublished = false;
            try
            {
                workspaceLease = m_Workspace.BeginTransaction();
                m_Target.BeginOuterTransaction();
                ExecuteIngress(outer, beforeCompletedTick);
                PublishPipeline(
                    outer.Source,
                    beforeCompletedTick,
                    PipelineTransactionTraceKind.IngressCompleted,
                    true,
                    "Ingress phase completed.");
                ExecuteSchedule(outer, beforeCompletedTick);
                SimulationSessionExecutionPlan<TStep> executionPlan = m_Target.ReadExecutionPlan();
                ValidateExecutionPlan(executionPlan, outer);
                PublishPipeline(
                    outer.Source,
                    beforeCompletedTick,
                    PipelineTransactionTraceKind.ScheduleResolved,
                    true,
                    "Schedule phase produced a validated ExecutionPlan.",
                    scheduleStatus: executionPlan.Status,
                    restoreRequested: executionPlan.Restore != null,
                    stepCount: executionPlan.Steps.Count);
                if (executionPlan.Status == SimulationSessionExecutionPlanStatus.Pending)
                {
                    RestorePipelineBefore(beforePipeline);
                    return;
                }
                if (executionPlan.Restore != null)
                {
                    PipelineRestorePreparation preparation = PrepareRestore(
                        executionPlan.Restore,
                        working);
                    restoreTransaction = preparation.Transaction;
                    PublishPipeline(
                        outer.Source,
                        beforeCompletedTick,
                        PipelineTransactionTraceKind.RestorePrepared,
                        true,
                        "Session restore transaction prepared.",
                        scheduleStatus: executionPlan.Status,
                        restoreRequested: true,
                        stepCount: executionPlan.Steps.Count);
                    solverTouched = true;
                    ApplyRestore(
                        preparation,
                        outer.Source,
                        executionPlan.Status,
                        executionPlan.Steps.Count);
                }
                ExecutionWorkspaceBuffer<TCompletedStep> completed = m_Workspace.CompletedSteps;
                completed.EnsureCapacity(executionPlan.Steps.Count);
                for (int stepIndex = 0; stepIndex < executionPlan.Steps.Count; stepIndex++)
                {
                    TStep step = executionPlan.Steps[stepIndex];
                    ulong workingTick = m_Target.GetLastCompletedTick(working);
                    if (step.Tick.Value != checked(workingTick + 1))
                    {
                        throw Failure(
                            SimulationSessionFailureStage.Step,
                            "step_tick_not_contiguous",
                            $"Simulation Step Tick '{step.Tick}' does not immediately follow working Tick '{workingTick}'.");
                    }
                    m_Target.BeginSimulationStep(working, step);
                    int finalizedStart = m_Target.FinalizedResultCount;
                    ExecuteStepPasses(
                        step,
                        stepIndex,
                        executionPlan.Steps.Count,
                        transactionIdentity,
                        ref solverTouched);
                    TCompletedStep completedStep = m_Target.CompleteStep(
                        executionPlan,
                        step,
                        finalizedStart,
                        working);
                    SimulationPipelineStateSnapshot pipelineProjection = m_Target.GetPipelineProjection(completedStep);
                    if (pipelineProjection != null)
                    {
                        PublishSnapshot(
                            step.Source,
                            pipelineProjection,
                            PipelineTransactionTraceKind.SnapshotCaptured,
                            "Simulation Step Pipeline snapshot captured.");
                    }
                    completed.Add(completedStep);
                    m_Target.ApplyCompletedStep(working, completedStep);
                    string stepCompletedDetail = m_Target.DiagnosticsEnabled
                        ? $"Simulation Step {stepIndex + 1}/{executionPlan.Steps.Count} completed."
                        : null;
                    PublishPipeline(
                        step.Source,
                        step.Tick.Value,
                        PipelineTransactionTraceKind.StepCompleted,
                        true,
                        stepCompletedDetail,
                        scheduleStatus: executionPlan.Status,
                        restoreRequested: executionPlan.Restore != null,
                        stepCount: executionPlan.Steps.Count);
                }
                m_Target.SetCompletedSteps(completed);
                ulong completedTick = m_Target.GetLastCompletedTick(working);
                ExecuteEgress(outer, completed.Count, completedTick, transactionIdentity);
                PublishPipeline(
                    outer.Source,
                    completedTick,
                    PipelineTransactionTraceKind.EgressCompleted,
                    true,
                    "Egress phase completed.",
                    scheduleStatus: executionPlan.Status,
                    restoreRequested: executionPlan.Restore != null,
                    stepCount: completed.Count);
                TCommitBatch commitBatch = FreezeCommitBatch(transactionIdentity, completed);
                PublishWorkingState(working);
                statePublished = true;
                PublishPipeline(
                    outer.Source,
                    completedTick,
                    PipelineTransactionTraceKind.StatePublished,
                    true,
                    "Character and World working state published atomically.",
                    stepCount: completed.Count);
                restoreTransaction?.CompleteAfterAtomicSessionPublish();
                m_Target.CompleteStatePublish(working);
                try
                {
                    CommitExternal(commitBatch);
                    PublishPipeline(
                        outer.Source,
                        completedTick,
                        PipelineTransactionTraceKind.CommitCompleted,
                        true,
                        "External Committer completed.",
                        stepCount: completed.Count);
                }
                catch (Exception exception)
                {
                    throw Failure(
                        SimulationSessionFailureStage.Commit,
                        "external_commit_failed",
                        "External Committer failed after atomic Gameplay state publish.",
                        exception);
                }
                return;
            }
            catch (Exception exception)
            {
                PublishPipeline(
                    outer.Source,
                    beforeCompletedTick,
                    PipelineTransactionTraceKind.OuterTickFailed,
                    false,
                    exception.Message);
                if (!statePublished)
                {
                    try
                    {
                        try
                        {
                            m_Target.DiscardUnconsumedEvaluations();
                        }
                        finally
                        {
                            restoreTransaction?.Dispose();
                        }
                    }
                    finally
                    {
                        RestoreFailedTransaction(beforePipeline, solverTouched);
                        restoreTransaction = null;
                    }
                }
                throw;
            }
            finally
            {
                try
                {
                    m_Target.DiscardUnconsumedEvaluations();
                }
                finally
                {
                    try
                    {
                        restoreTransaction?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            m_Target.ClearTransientState();
                        }
                        finally
                        {
                            try
                            {
                                if (workspaceLease.IsValid)
                                    m_Workspace.EndTransaction(workspaceLease);
                            }
                            finally
                            {
                                beforePipeline.Dispose();
                            }
                        }
                    }
                }
            }
        }

        [PerformanceProbe("simulation.pipeline.commit-freeze")]
        TCommitBatch FreezeCommitBatch(
            StableHash transactionIdentity,
            ExecutionWorkspaceBuffer<TCompletedStep> completed)
        {
            return m_Target.FreezeCommitBatch(
                transactionIdentity,
                completed,
                m_Workspace);
        }

        [PerformanceProbe("simulation.pipeline.state-publish")]
        void PublishWorkingState(TWorkingState working)
        {
            m_Target.PublishWorkingState(working);
        }

        [PerformanceProbe("simulation.pipeline.external-commit")]
        void CommitExternal(TCommitBatch commitBatch)
        {
            m_Target.Commit(commitBatch);
        }

        [PerformanceProbe("simulation.pipeline.restore")]
        PipelineRestorePreparation PrepareRestore(
            SimulationRestoreDirective directive,
            TWorkingState working)
        {
            return m_Target.PrepareRestore(
                directive,
                working,
                m_Services);
        }

        [PerformanceProbe("simulation.pipeline.restore")]
        void ApplyRestore(
            PipelineRestorePreparation preparation,
            SimulationTickSourceIdentity source,
            SimulationSessionExecutionPlanStatus scheduleStatus,
            int stepCount)
        {
            preparation.Transaction.ApplyAndValidate();
            PublishSnapshot(
                source,
                preparation.PipelineSnapshot,
                PipelineTransactionTraceKind.SnapshotRestored,
                "Pipeline snapshot restored and validated.");
            PublishPipeline(
                source,
                preparation.Tick.Value,
                PipelineTransactionTraceKind.RestoreApplied,
                true,
                "Session restore transaction applied and validated.",
                scheduleStatus: scheduleStatus,
                restoreRequested: true,
                stepCount: stepCount);
        }

        [PerformanceProbe("simulation.pipeline.ingress")]
        void ExecuteIngress(SimulationSessionLogicTickContext outer, ulong completedTick)
        {
            var context = new SimulationPipelineIngressContext(
                m_Services.Descriptor.Identity,
                m_Services.Plan.Identity,
                outer.Source,
                completedTick);
            ExecutePhase(
                context,
                SimulationPipelinePhase.Ingress,
                SimulationSessionFailureStage.Ingress,
                outer.Source,
                completedTick,
                static (pass, phaseContext) => pass.Execute(phaseContext));
        }

        [PerformanceProbe("simulation.pipeline.schedule")]
        void ExecuteSchedule(SimulationSessionLogicTickContext outer, ulong completedTick)
        {
            var context = new SimulationPipelineScheduleContext(
                m_Services.Descriptor.Identity,
                m_Services.Plan.Identity,
                outer.Source,
                completedTick);
            ExecutePhase(
                context,
                SimulationPipelinePhase.Schedule,
                SimulationSessionFailureStage.Schedule,
                outer.Source,
                completedTick,
                static (pass, phaseContext) => pass.Execute(phaseContext));
        }

        void ExecuteStepPasses(
            TStep step,
            int stepIndex,
            int stepCount,
            StableHash transactionIdentity,
            ref bool solverTouched)
        {
            var context = new SimulationPipelineStepTransactionContext(
                m_Services.Descriptor.Identity,
                m_Services.Plan.Identity,
                step.Tick,
                step.ExecutionKind,
                stepIndex,
                stepCount,
                transactionIdentity);
            for (int i = 0; i < m_Services.Passes.Count; i++)
            {
                ICompiledSimulationPipelinePassRuntime pass = m_Services.Passes[i];
                if (pass.Phase != SimulationPipelinePhase.Step)
                    continue;
                if (m_Target.TryGetCoreStepStage(pass, out PipelineTransactionStage stage) &&
                    stage == PipelineTransactionStage.ResolveBatch)
                {
                    solverTouched = true;
                }
                ExecutePass(
                    pass,
                    SimulationSessionFailureStage.Step,
                    step.Source,
                    step.Tick.Value,
                    stage,
                    context);
            }
        }

        [PerformanceProbe("simulation.pipeline.egress")]
        void ExecuteEgress(
            SimulationSessionLogicTickContext outer,
            int completedStepCount,
            ulong completedTick,
            StableHash transactionIdentity)
        {
            var context = new SimulationPipelineEgressContext(
                m_Services.Descriptor.Identity,
                m_Services.Plan.Identity,
                outer.Source,
                completedStepCount,
                transactionIdentity);
            ExecutePhase(
                context,
                SimulationPipelinePhase.Egress,
                SimulationSessionFailureStage.Egress,
                outer.Source,
                completedTick,
                static (pass, phaseContext) => pass.Execute(phaseContext));
        }

        void ExecutePhase<TContext>(
            TContext context,
            SimulationPipelinePhase phase,
            SimulationSessionFailureStage failureStage,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            Action<ICompiledSimulationPipelinePassRuntime, TContext> execute)
        {
            for (int i = 0; i < m_Services.Passes.Count; i++)
            {
                ICompiledSimulationPipelinePassRuntime pass = m_Services.Passes[i];
                if (pass.Phase == phase)
                {
                    ExecutePassCore(
                        pass,
                        failureStage,
                        source,
                        completedTick,
                        execute,
                        context);
                }
            }
        }

        void ExecutePass(
            ICompiledSimulationPipelinePassRuntime pass,
            SimulationSessionFailureStage stage,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            PipelineTransactionStage pipelineStage,
            SimulationPipelineStepTransactionContext context)
        {
            switch (pipelineStage)
            {
                case PipelineTransactionStage.Evaluate:
                    ExecuteEvaluatePass(pass, stage, source, completedTick, context);
                    return;
                case PipelineTransactionStage.ResolveBatch:
                    ExecuteWorldResolvePass(pass, stage, source, completedTick, context);
                    return;
                case PipelineTransactionStage.Finalize:
                    ExecuteFinalizePass(pass, stage, source, completedTick, context);
                    return;
                default:
                    ExecuteOtherPass(pass, stage, source, completedTick, context);
                    return;
            }
        }

        [PerformanceProbe("simulation.pipeline.evaluate")]
        void ExecuteEvaluatePass(
            ICompiledSimulationPipelinePassRuntime pass,
            SimulationSessionFailureStage stage,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            SimulationPipelineStepTransactionContext context)
        {
            ExecuteStepPassCore(pass, stage, source, completedTick, context);
        }

        [PerformanceProbe("simulation.pipeline.world-resolve")]
        void ExecuteWorldResolvePass(
            ICompiledSimulationPipelinePassRuntime pass,
            SimulationSessionFailureStage stage,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            SimulationPipelineStepTransactionContext context)
        {
            ExecuteStepPassCore(pass, stage, source, completedTick, context);
        }

        [PerformanceProbe("simulation.pipeline.finalize")]
        void ExecuteFinalizePass(
            ICompiledSimulationPipelinePassRuntime pass,
            SimulationSessionFailureStage stage,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            SimulationPipelineStepTransactionContext context)
        {
            ExecuteStepPassCore(pass, stage, source, completedTick, context);
        }

        [PerformanceProbe("simulation.pipeline.step-other")]
        void ExecuteOtherPass(
            ICompiledSimulationPipelinePassRuntime pass,
            SimulationSessionFailureStage stage,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            SimulationPipelineStepTransactionContext context)
        {
            ExecuteStepPassCore(pass, stage, source, completedTick, context);
        }

        void ExecuteStepPassCore(
            ICompiledSimulationPipelinePassRuntime pass,
            SimulationSessionFailureStage stage,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            SimulationPipelineStepTransactionContext context)
        {
            bool trace = m_Target.DiagnosticsEnabled;
            long started = trace ? Stopwatch.GetTimestamp() : 0;
            try
            {
                pass.Execute(context);
                if (trace)
                    PublishPass(pass, source, completedTick, true, "Pipeline Pass completed.", Stopwatch.GetTimestamp() - started);
            }
            catch (SimulationSessionCompositionException)
            {
                if (trace)
                    PublishPass(pass, source, completedTick, false, "Pipeline Pass reported a composition failure.", Stopwatch.GetTimestamp() - started);
                throw;
            }
            catch (Exception exception)
            {
                if (trace)
                    PublishPass(pass, source, completedTick, false, exception.Message, Stopwatch.GetTimestamp() - started);
                throw Failure(
                    stage,
                    "pipeline_pass_failed",
                    $"Pipeline Pass '{pass.Descriptor.PassId}' failed.",
                    exception,
                    pass.Descriptor.PassId.ToString());
            }
        }

        void ExecutePassCore<TContext>(
            ICompiledSimulationPipelinePassRuntime pass,
            SimulationSessionFailureStage stage,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            Action<ICompiledSimulationPipelinePassRuntime, TContext> execute,
            TContext context)
        {
            bool trace = m_Target.DiagnosticsEnabled;
            long started = trace ? Stopwatch.GetTimestamp() : 0;
            try
            {
                execute(pass, context);
                if (trace)
                    PublishPass(pass, source, completedTick, true, "Pipeline Pass completed.", Stopwatch.GetTimestamp() - started);
            }
            catch (SimulationSessionCompositionException)
            {
                if (trace)
                    PublishPass(pass, source, completedTick, false, "Pipeline Pass reported a composition failure.", Stopwatch.GetTimestamp() - started);
                throw;
            }
            catch (Exception exception)
            {
                if (trace)
                    PublishPass(pass, source, completedTick, false, exception.Message, Stopwatch.GetTimestamp() - started);
                throw Failure(
                    stage,
                    "pipeline_pass_failed",
                    $"Pipeline Pass '{pass.Descriptor.PassId}' failed.",
                    exception,
                    pass.Descriptor.PassId.ToString());
            }
        }

        void ValidateExecutionPlan(
            SimulationSessionExecutionPlan<TStep> plan,
            SimulationSessionLogicTickContext outer)
        {
            if (!plan.IsValid || !plan.OuterSource.Equals(outer.Source) ||
                !plan.GameplayContentHash.Equals(m_Services.Descriptor.GameplayContentHash) ||
                !plan.PipelineHash.Equals(m_Services.Plan.Identity.Hash) ||
                !plan.RosterHash.Equals(m_Services.Descriptor.Roster.RosterHash))
            {
                throw Failure(
                    SimulationSessionFailureStage.Schedule,
                    "execution_plan_identity_mismatch",
                    "ExecutionPlan does not match the active outer Tick, Catalog, Pipeline or roster.");
            }
            IReadOnlyList<ActorId> roster = m_Services.Descriptor.Roster.Actors;
            for (int stepIndex = 0; stepIndex < plan.Steps.Count; stepIndex++)
            {
                TStep step = plan.Steps[stepIndex];
                if (step.Actors.Count != roster.Count)
                {
                    throw Failure(
                        SimulationSessionFailureStage.Schedule,
                        "execution_plan_roster_mismatch",
                        "ExecutionPlan Step Actor count does not match the locked roster.");
                }
                for (int actorIndex = 0; actorIndex < step.Actors.Count; actorIndex++)
                {
                    if (!step.Actors[actorIndex].Equals(roster[actorIndex]))
                    {
                        throw Failure(
                            SimulationSessionFailureStage.Schedule,
                            "execution_plan_actor_order_mismatch",
                            "ExecutionPlan Step Actor order does not match the stable locked roster.");
                    }
                }
            }
            m_Target.ValidateTargetExecutionPlan(plan, outer);
        }

        void RestoreFailedTransaction(
            SimulationPipelineStateCheckpointSet beforePipeline,
            bool solverTouched)
        {
            Exception timelineFailure = null;
            try
            {
                m_Target.DiscardUnconsumedEvaluations();
                m_Target.RestoreTimelineBaseline();
            }
            catch (Exception exception)
            {
                timelineFailure = exception;
            }
            Exception solverFailure = null;
            if (solverTouched)
            {
                try
                {
                    m_Target.RestoreSolverBaseline();
                }
                catch (Exception exception)
                {
                    solverFailure = exception;
                }
            }
            RestorePipelineBefore(beforePipeline);
            if (timelineFailure != null)
                throw Failure(
                    SimulationSessionFailureStage.Runtime,
                    "timeline_rollback_failed",
                    "Timeline runtime failed to restore the outer Tick baseline.",
                    timelineFailure);
            if (solverFailure != null)
            {
                throw Failure(
                    SimulationSessionFailureStage.Runtime,
                    "solver_rollback_failed",
                    "World Solver failed to restore the outer Tick baseline.",
                    solverFailure);
            }
        }

        void RestorePipelineBefore(SimulationPipelineStateCheckpointSet beforePipeline)
        {
            beforePipeline.Restore();
            var context = new SimulationPipelineReconstructionContext(
                m_Services.Descriptor.Identity,
                m_Services.Plan.Identity,
                m_Services.Descriptor.GameplayContentHash,
                m_Services.Descriptor.Roster.RosterHash,
                m_Target.BaselineWorldRevision);
            for (int i = 0; i < m_Services.ReconstructiblePasses.Count; i++)
                m_Services.ReconstructiblePasses[i].Reconstruct(context);
        }

        void RequireStepPassLayout()
        {
            int evaluate = 0;
            int resolve = 0;
            int finalize = 0;
            int evaluateIndex = -1;
            int resolveIndex = -1;
            int finalizeIndex = -1;
            for (int i = 0; i < m_Services.Passes.Count; i++)
            {
                ICompiledSimulationPipelinePassRuntime pass = m_Services.Passes[i];
                if (pass.Phase != SimulationPipelinePhase.Step)
                    continue;
                if (!m_Target.TryGetCoreStepStage(pass, out PipelineTransactionStage stage))
                    continue;
                switch (stage)
                {
                    case PipelineTransactionStage.Evaluate:
                        evaluate++;
                        evaluateIndex = i;
                        break;
                    case PipelineTransactionStage.ResolveBatch:
                        resolve++;
                        resolveIndex = i;
                        break;
                    case PipelineTransactionStage.Finalize:
                        finalize++;
                        finalizeIndex = i;
                        break;
                    default:
                        throw new InvalidOperationException($"Pipeline core Step Pass '{pass.Descriptor.PassId}' has invalid transaction stage '{stage}'.");
                }
            }
            if (evaluate != 1 || resolve != 1 || finalize != 1)
            {
                throw new InvalidOperationException(
                    $"Pipeline transaction requires one Evaluate, ResolveBatch and Finalize Pass; found '{evaluate}/{resolve}/{finalize}'.");
            }
            if (evaluateIndex >= resolveIndex || resolveIndex >= finalizeIndex)
            {
                throw new InvalidOperationException(
                    "Pipeline core Step Passes must be ordered Evaluate, ResolveBatch and Finalize.");
            }
        }

        void PublishPass(
            ICompiledSimulationPipelinePassRuntime pass,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            bool success,
            string detail,
            long elapsedStopwatchTicks)
        {
            if (!m_PassProductTraces.TryGetValue(pass.Descriptor.PassId, out PassProductTrace products))
                throw new InvalidOperationException($"Pipeline Pass '{pass.Descriptor.PassId}' has no composed diagnostics descriptor.");
            PublishPipeline(
                source,
                completedTick,
                success ? PipelineTransactionTraceKind.PassCompleted : PipelineTransactionTraceKind.PassFailed,
                success,
                detail,
                pass.Phase,
                pass.Descriptor.PassId,
                pass.Descriptor.ImplementationVersion,
                elapsedStopwatchTicks: elapsedStopwatchTicks,
                productInputs: products.Inputs,
                productOutputs: products.Outputs);
        }

        void PublishSnapshot(
            SimulationTickSourceIdentity source,
            SimulationPipelineStateSnapshot snapshot,
            PipelineTransactionTraceKind kind,
            string detail)
        {
            if (!m_Target.DiagnosticsEnabled || snapshot == null)
                return;
            for (int i = 0; i < snapshot.Participants.Count; i++)
            {
                SimulationPipelinePassStateSnapshot participant = snapshot.Participants[i];
                string identity =
                    $"{participant.PassId}@{participant.ImplementationVersion}:" +
                    $"{participant.StateOwner}:{participant.StateSchemaId}@{participant.StateSchemaVersion}";
                PublishPipeline(
                    source,
                    snapshot.LastCompletedTick,
                    kind,
                    true,
                    detail,
                    snapshotParticipant: identity,
                    snapshotHash: participant.StateHash);
            }
        }

        void PublishPipeline(
            SimulationTickSourceIdentity source,
            ulong completedTick,
            PipelineTransactionTraceKind kind,
            bool success,
            string detail,
            SimulationPipelinePhase phase = default,
            SimulationPipelinePassId passId = default,
            SimulationPipelinePassImplementationVersion passVersion = default,
            SimulationSessionExecutionPlanStatus scheduleStatus = default,
            bool restoreRequested = false,
            int stepCount = 0,
            long elapsedStopwatchTicks = 0,
            string productInputs = "",
            string productOutputs = "",
            string snapshotParticipant = "",
            StableHash snapshotHash = default)
        {
            if (!m_Target.DiagnosticsEnabled)
                return;
            m_Target.PublishTrace(new PipelineTransactionTrace(
                source,
                completedTick,
                kind,
                success,
                detail,
                phase,
                passId,
                passVersion,
                scheduleStatus,
                restoreRequested,
                stepCount,
                elapsedStopwatchTicks,
                productInputs,
                productOutputs,
                snapshotParticipant,
                snapshotHash));
        }

        static PassProductTrace BuildProductTrace(SimulationPipelinePassDescriptor descriptor)
        {
            var inputValues = new List<string>();
            var outputValues = new List<string>();
            for (int i = 0; i < descriptor.ProductAccesses.Count; i++)
            {
                SimulationPipelineProductAccess access = descriptor.ProductAccesses[i];
                if (access.Access == SimulationPipelineProductAccessKind.ReadOnlyConsumer)
                    inputValues.Add(access.Product.VersionedIdentity);
                else
                    outputValues.Add(access.Product.VersionedIdentity);
            }
            return new PassProductTrace(
                string.Join(",", inputValues),
                string.Join(",", outputValues));
        }

        static StableHash ComputeTransactionIdentity(
            string domain,
            StableHash descriptorIdentity,
            StableHash planHash,
            SimulationTickSourceKind sourceKind,
            CanonicalTextValue clockId,
            ulong sourceTick,
            ulong beforeCompletedTick)
        {
            Span<char> descriptorIdentityText = stackalloc char[64];
            Span<char> planHashText = stackalloc char[64];
            descriptorIdentity.Format(descriptorIdentityText);
            planHash.Format(planHashText);
            Span<char> sourceKindText = stackalloc char[3];
            Span<char> sourceTickText = stackalloc char[20];
            Span<char> completedTickText = stackalloc char[20];
            if (!((int)sourceKind).TryFormat(
                    sourceKindText,
                    out int sourceKindLength,
                    default,
                    CultureInfo.CurrentCulture) ||
                !sourceTick.TryFormat(
                    sourceTickText,
                    out int sourceTickLength,
                    default,
                    CultureInfo.CurrentCulture) ||
                !beforeCompletedTick.TryFormat(
                    completedTickText,
                    out int completedTickLength,
                    default,
                    CultureInfo.CurrentCulture))
            {
                throw new InvalidOperationException(
                    "Pipeline transaction identity numeric formatting failed.");
            }
            ReadOnlySpan<char> sourceKindValue =
                sourceKindText.Slice(0, sourceKindLength);
            ReadOnlySpan<char> sourceTickValue =
                sourceTickText.Slice(0, sourceTickLength);
            ReadOnlySpan<char> completedTickValue =
                completedTickText.Slice(0, completedTickLength);
            using CanonicalTextReadLease clockLease = clockId.AcquireRead();
            ReadOnlySpan<char> clockText = clockId.AsSpan();
            Encoding utf8 = Encoding.UTF8;
            int byteCount = checked(
                6 +
                utf8.GetByteCount(domain) +
                descriptorIdentityText.Length +
                planHashText.Length +
                utf8.GetByteCount(sourceKindValue) +
                utf8.GetByteCount(clockText) +
                utf8.GetByteCount(sourceTickValue) +
                utf8.GetByteCount(completedTickValue));
            byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                int offset = 0;
                offset = AppendUtf8(utf8, buffer, offset, domain);
                buffer[offset++] = 0x1f;
                offset = AppendUtf8(utf8, buffer, offset, descriptorIdentityText);
                buffer[offset++] = 0x1f;
                offset = AppendUtf8(utf8, buffer, offset, planHashText);
                buffer[offset++] = 0x1f;
                offset = AppendUtf8(utf8, buffer, offset, sourceKindValue);
                buffer[offset++] = 0x1f;
                offset = AppendUtf8(utf8, buffer, offset, clockText);
                buffer[offset++] = 0x1f;
                offset = AppendUtf8(utf8, buffer, offset, sourceTickValue);
                buffer[offset++] = 0x1f;
                offset = AppendUtf8(utf8, buffer, offset, completedTickValue);
                return SimulationCanonicalPayloadHash.Compute(
                    new ArraySegment<byte>(buffer, 0, offset));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        static int AppendUtf8(
            Encoding encoding,
            byte[] buffer,
            int offset,
            string value) =>
            offset + encoding.GetBytes(
                value,
                0,
                value.Length,
                buffer,
                offset);

        static int AppendUtf8(
            Encoding encoding,
            byte[] buffer,
            int offset,
            ReadOnlySpan<char> value) =>
            offset + encoding.GetBytes(
                value,
                buffer.AsSpan(offset));

        readonly struct PassProductTrace
        {
            public PassProductTrace(string inputs, string outputs)
            {
                Inputs = inputs ?? string.Empty;
                Outputs = outputs ?? string.Empty;
            }

            public string Inputs { get; }
            public string Outputs { get; }
        }

        static SimulationSessionCompositionException Failure(
            SimulationSessionFailureStage stage,
            string code,
            string message,
            Exception inner = null,
            string passIdentity = "")
        {
            return PipelineTransactionFailure.Create(stage, code, message, inner, passIdentity);
        }
    }
}
