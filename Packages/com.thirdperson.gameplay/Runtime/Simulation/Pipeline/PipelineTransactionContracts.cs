using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public interface ICompiledSimulationPipelinePassRuntime : IDisposable
    {
        SimulationPipelinePassDescriptor Descriptor { get; }
        SimulationPipelinePhase Phase { get; }
        SimulationPipelinePassRuntimeState State { get; }
        ISimulationPipelineStateParticipant StateParticipant { get; }
        ISimulationPipelineReconstructiblePass Reconstructible { get; }
        void Activate();
        void Execute(SimulationPipelineIngressContext context);
        void Execute(SimulationPipelineScheduleContext context);
        void Execute(SimulationPipelineStepTransactionContext context);
        void Execute(SimulationPipelineEgressContext context);
    }

    internal enum PipelineTransactionStage : byte
    {
        Ingress = 1,
        Schedule = 2,
        Restore = 3,
        Evaluate = 4,
        ResolveBatch = 5,
        Finalize = 6,
        Egress = 7,
        Publish = 8,
        Commit = 9
    }

    internal enum PipelineTransactionTraceKind : byte
    {
        OuterTickStarted = 1,
        IngressCompleted = 2,
        ScheduleResolved = 3,
        RestorePrepared = 4,
        RestoreApplied = 5,
        StepCompleted = 6,
        EgressCompleted = 7,
        StatePublished = 8,
        CommitCompleted = 9,
        PassCompleted = 10,
        PassFailed = 11,
        SnapshotCaptured = 12,
        SnapshotRestored = 13,
        OuterTickFailed = 14
    }

    internal readonly struct PipelineTransactionTrace
    {
        public PipelineTransactionTrace(
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
            Source = source;
            CompletedTick = completedTick;
            Kind = kind;
            Success = success;
            Detail = detail ?? string.Empty;
            Phase = phase;
            PassId = passId;
            PassVersion = passVersion;
            ScheduleStatus = scheduleStatus;
            RestoreRequested = restoreRequested;
            StepCount = stepCount;
            ElapsedStopwatchTicks = elapsedStopwatchTicks;
            ProductInputs = productInputs ?? string.Empty;
            ProductOutputs = productOutputs ?? string.Empty;
            SnapshotParticipant = snapshotParticipant ?? string.Empty;
            SnapshotHash = snapshotHash;
        }

        public SimulationTickSourceIdentity Source { get; }
        public ulong CompletedTick { get; }
        public PipelineTransactionTraceKind Kind { get; }
        public bool Success { get; }
        public string Detail { get; }
        public SimulationPipelinePhase Phase { get; }
        public SimulationPipelinePassId PassId { get; }
        public SimulationPipelinePassImplementationVersion PassVersion { get; }
        public SimulationSessionExecutionPlanStatus ScheduleStatus { get; }
        public bool RestoreRequested { get; }
        public int StepCount { get; }
        public long ElapsedStopwatchTicks { get; }
        public string ProductInputs { get; }
        public string ProductOutputs { get; }
        public string SnapshotParticipant { get; }
        public StableHash SnapshotHash { get; }
    }

    internal sealed class PipelineTransactionRuntimeServices
    {
        public PipelineTransactionRuntimeServices(
            SimulationSessionCompositionDescriptor descriptor,
            CompiledSimulationPipelinePlan plan,
            IReadOnlyList<ICompiledSimulationPipelinePassRuntime> passes,
            IReadOnlyList<ISimulationPipelineStateParticipant> stateParticipants,
            IReadOnlyList<ISimulationPipelineReconstructiblePass> reconstructiblePasses)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Passes = passes ?? throw new ArgumentNullException(nameof(passes));
            StateParticipants = SimulationPipelineStateSnapshotCoordinator.CreateParticipantSet(
                Plan,
                stateParticipants ?? throw new ArgumentNullException(nameof(stateParticipants)));
            ReconstructiblePasses = reconstructiblePasses ?? throw new ArgumentNullException(nameof(reconstructiblePasses));
        }

        public SimulationSessionCompositionDescriptor Descriptor { get; }
        public CompiledSimulationPipelinePlan Plan { get; }
        public IReadOnlyList<ICompiledSimulationPipelinePassRuntime> Passes { get; }
        public IReadOnlyList<ISimulationPipelineStateParticipant> StateParticipants { get; }
        public IReadOnlyList<ISimulationPipelineReconstructiblePass> ReconstructiblePasses { get; }
    }

    internal sealed class PipelineRestorePreparation
    {
        public PipelineRestorePreparation(
            SimulationSessionRestoreTransaction transaction,
            SimulationPipelineStateSnapshot pipelineSnapshot,
            SimulationTick tick)
        {
            Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            PipelineSnapshot = pipelineSnapshot ?? throw new ArgumentNullException(nameof(pipelineSnapshot));
            if (!tick.IsValid || pipelineSnapshot.LastCompletedTick != tick.Value)
                throw new ArgumentException("Pipeline restore preparation Tick is invalid.", nameof(tick));
            Tick = tick;
        }

        public SimulationSessionRestoreTransaction Transaction { get; }
        public SimulationPipelineStateSnapshot PipelineSnapshot { get; }
        public SimulationTick Tick { get; }
    }

    internal interface IPipelineTransactionTargetPort<
        TStep,
        TWorkingState,
        TCompletedStep,
        TEgressRecord,
        TCommitBatch>
        where TStep : SimulationPipelineStep
        where TWorkingState : class
        where TCompletedStep : struct
    {
        string TransactionIdentityDomain { get; }
        bool DiagnosticsEnabled { get; }
        ulong BaselineCompletedTick { get; }
        WorldRevision BaselineWorldRevision { get; }
        TWorkingState CreateWorkingState();
        ulong GetLastCompletedTick(TWorkingState workingState);
        void BeginOuterTransaction();
        void BeginSimulationStep(TWorkingState workingState, TStep step);
        void SetCompletedSteps(IReadOnlyList<TCompletedStep> steps);
        void ClearTransientState();
        void DiscardUnconsumedEvaluations();
        SimulationSessionExecutionPlan<TStep> ReadExecutionPlan();
        void ValidateTargetExecutionPlan(
            SimulationSessionExecutionPlan<TStep> plan,
            SimulationSessionLogicTickContext outer);
        PipelineRestorePreparation PrepareRestore(
            SimulationRestoreDirective directive,
            TWorkingState workingState,
            PipelineTransactionRuntimeServices services);
        bool TryGetCoreStepStage(
            ICompiledSimulationPipelinePassRuntime pass,
            out PipelineTransactionStage stage);
        int FinalizedResultCount { get; }
        TCompletedStep CompleteStep(
            SimulationSessionExecutionPlan<TStep> executionPlan,
            TStep step,
            int finalizedStart,
            TWorkingState workingState);
        SimulationPipelineStateSnapshot GetPipelineProjection(TCompletedStep completedStep);
        void ApplyCompletedStep(TWorkingState workingState, TCompletedStep completedStep);
        TCommitBatch FreezeCommitBatch(
            StableHash transactionIdentity,
            ExecutionWorkspaceBuffer<TCompletedStep> completedSteps,
            SessionExecutionWorkspace<TCompletedStep, TEgressRecord> workspace);
        void PublishWorkingState(TWorkingState workingState);
        void CompleteStatePublish(TWorkingState workingState);
        void RestoreSolverBaseline();
        void RestoreTimelineBaseline();
        void Commit(TCommitBatch commitBatch);
        void PublishTrace(PipelineTransactionTrace trace);
    }

    internal static class PipelineTransactionFailure
    {
        public static SimulationSessionCompositionException Create(
            SimulationSessionFailureStage stage,
            string code,
            string message,
            Exception inner = null,
            string passIdentity = "")
        {
            return new SimulationSessionCompositionException(new SimulationSessionFailure(
                stage,
                code,
                message,
                passIdentity: passIdentity), inner);
        }
    }
}
