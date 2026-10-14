using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public enum SimulationOutputDispositionKind : byte
    {
        Publish = 1,
        Replace = 2,
        Retire = 3,
        Suppress = 4
    }

    public readonly struct SimulationOutputDisposition
    {
        public SimulationOutputDisposition(
            EventId sourceEventId,
            ActorId actorId,
            SimulationOutputDispositionKind kind,
            EventId targetEventId = default)
        {
            if (!sourceEventId.IsValid || !actorId.IsValid)
                throw new ArgumentException("Output disposition source EventId or ActorId is invalid.");
            bool requiresTarget = kind == SimulationOutputDispositionKind.Replace || kind == SimulationOutputDispositionKind.Retire;
            if (requiresTarget != targetEventId.IsValid || targetEventId.Equals(sourceEventId))
                throw new ArgumentException("Output disposition target EventId does not match its lifecycle kind.", nameof(targetEventId));
            SourceEventId = sourceEventId;
            ActorId = actorId;
            Kind = kind;
            TargetEventId = targetEventId;
        }

        public EventId SourceEventId { get; }
        public ActorId ActorId { get; }
        public SimulationOutputDispositionKind Kind { get; }
        public EventId TargetEventId { get; }
    }

    public sealed class SimulationTickResult
    {
        static readonly Comparison<SimulationActorTickResult> s_CompareActors = CompareActors;
        readonly SimulationActorTickResult[] m_Actors;
        SimulationPipelineStateSnapshot m_PipelineProjection;
        SimulationWorldStateStore.StatePage m_CandidateStorage;
        SimulationWorldSnapshot m_WorldSnapshot;

        internal SimulationTickResult(int actorCount)
        {
            m_Actors = new SimulationActorTickResult[actorCount];
        }

        internal SimulationActorTickResult[] ActorArray => m_Actors;

        internal void InitializeFrame(
            SimulationNumericProfile numericProfile,
            GameplayContentHash gameplayContentHash,
            SimulationTick tick,
            SimulationWorldSnapshot candidateSnapshot)
        {
            NumericProfile = numericProfile;
            GameplayContentHash = gameplayContentHash;
            Tick = tick;
            CandidateSnapshot = candidateSnapshot;
        }

        internal void SetPipelineProjection(SimulationPipelineStateSnapshot projection) => m_PipelineProjection = projection;

        internal void SetCandidateStorage(SimulationWorldStateStore.StatePage storage) => m_CandidateStorage = storage;

        internal void SetWorldSnapshot(SimulationWorldSnapshot snapshot) => m_WorldSnapshot = snapshot;

        internal void ReleaseFrame()
        {
            m_PipelineProjection?.ReleaseStepStorage();
            m_PipelineProjection = null;
            m_WorldSnapshot?.ReleaseStateStorage();
            m_WorldSnapshot = null;
            m_CandidateStorage?.Release();
            m_CandidateStorage = null;
            Array.Clear(m_Actors, 0, m_Actors.Length);
            CandidateSnapshot = null;
        }

        internal static void SortByActorId(SimulationActorTickResult[] actors)
        {
            Array.Sort(actors, s_CompareActors);
        }

        static int CompareActors(SimulationActorTickResult left, SimulationActorTickResult right) =>
            left.ActorId.CompareTo(right.ActorId);

        public SimulationNumericProfile NumericProfile { get; private set; }
        public GameplayContentHash GameplayContentHash { get; private set; }
        public SimulationTick Tick { get; private set; }
        public IReadOnlyList<SimulationActorTickResult> Actors => m_Actors;
        public bool HasCandidateSnapshot => CandidateSnapshot != null;
        public SimulationWorldSnapshot CandidateSnapshot { get; private set; }
    }

    public enum SimulationBoundaryTraceKind : byte
    {
        TickStarted = 1,
        RestoreRequested = 2,
        RestoreApplied = 3,
        EvaluateStarted = 4,
        EvaluateCompleted = 5,
        WorldBatchStarted = 6,
        WorldBatchCompleted = 7,
        FinalizeStarted = 8,
        FinalizeCompleted = 9,
        OutputPlanValidated = 10,
        StatePublished = 11,
        CommitStarted = 12,
        CommitCompleted = 13,
        TickFailed = 14
    }

    public readonly struct SimulationBoundaryTraceRecord
    {
        public SimulationBoundaryTraceRecord(
            SimulationNumericProfile numericProfile,
            SimulationTick tick,
            SimulationTickSourceIdentity source,
            SimulationBoundaryTraceKind kind,
            ActorId actorId,
            ProgramId programId,
            SolverImplementationId solverId,
            WorldCapability solverCapabilities,
            bool success,
            string detail,
            CharacterStateHash characterStateHash = default,
            SimulationWorldHash worldHash = default,
            bool deterministicValidity = false,
            string snapshotIdentity = "")
        {
            if (!numericProfile.IsValid || !tick.IsValid ||
                CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0 ||
                kind is not (SimulationBoundaryTraceKind.TickStarted or
                    SimulationBoundaryTraceKind.RestoreRequested or
                    SimulationBoundaryTraceKind.RestoreApplied or
                    SimulationBoundaryTraceKind.EvaluateStarted or
                    SimulationBoundaryTraceKind.EvaluateCompleted or
                    SimulationBoundaryTraceKind.WorldBatchStarted or
                    SimulationBoundaryTraceKind.WorldBatchCompleted or
                    SimulationBoundaryTraceKind.FinalizeStarted or
                    SimulationBoundaryTraceKind.FinalizeCompleted or
                    SimulationBoundaryTraceKind.OutputPlanValidated or
                    SimulationBoundaryTraceKind.StatePublished or
                    SimulationBoundaryTraceKind.CommitStarted or
                    SimulationBoundaryTraceKind.CommitCompleted or
                    SimulationBoundaryTraceKind.TickFailed))
                throw new ArgumentException("Simulation boundary trace identity is incomplete.");
            NumericProfile = numericProfile;
            Tick = tick;
            Source = source;
            Kind = kind;
            ActorId = actorId;
            ProgramId = programId;
            SolverId = solverId;
            SolverCapabilities = solverCapabilities;
            Success = success;
            Detail = detail ?? string.Empty;
            CharacterStateHash = characterStateHash;
            WorldHash = worldHash;
            DeterministicValidity = deterministicValidity;
            SnapshotIdentity = snapshotIdentity ?? string.Empty;
        }

        public SimulationNumericProfile NumericProfile { get; }
        public SimulationTick Tick { get; }
        public SimulationTickSourceIdentity Source { get; }
        public SimulationBoundaryTraceKind Kind { get; }
        public ActorId ActorId { get; }
        public ProgramId ProgramId { get; }
        public SolverImplementationId SolverId { get; }
        public WorldCapability SolverCapabilities { get; }
        public bool Success { get; }
        public string Detail { get; }
        public CharacterStateHash CharacterStateHash { get; }
        public SimulationWorldHash WorldHash { get; }
        public bool DeterministicValidity { get; }
        public string SnapshotIdentity { get; }
    }

    public enum SimulationPipelineTraceKind : byte
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

    public readonly struct SimulationPipelineTraceRecord
    {
        public SimulationPipelineTraceRecord(
            SimulationSessionCompositionIdentity session,
            SimulationPipelineIdentity pipeline,
            SimulationTickSourceIdentity source,
            ulong completedTick,
            SimulationPipelineTraceKind kind,
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
            if (!session.IsValid || !pipeline.IsValid || CanonicalTextValue.IsNullOrEmpty(source.ClockId) || source.SourceTick == 0 ||
                kind is not (SimulationPipelineTraceKind.OuterTickStarted or
                    SimulationPipelineTraceKind.IngressCompleted or
                    SimulationPipelineTraceKind.ScheduleResolved or
                    SimulationPipelineTraceKind.RestorePrepared or
                    SimulationPipelineTraceKind.RestoreApplied or
                    SimulationPipelineTraceKind.StepCompleted or
                    SimulationPipelineTraceKind.EgressCompleted or
                    SimulationPipelineTraceKind.StatePublished or
                    SimulationPipelineTraceKind.CommitCompleted or
                    SimulationPipelineTraceKind.PassCompleted or
                    SimulationPipelineTraceKind.PassFailed or
                    SimulationPipelineTraceKind.SnapshotCaptured or
                    SimulationPipelineTraceKind.SnapshotRestored or
                    SimulationPipelineTraceKind.OuterTickFailed) || stepCount < 0 || elapsedStopwatchTicks < 0)
            {
                throw new ArgumentException("Pipeline trace identity is incomplete.");
            }
            bool passTrace = kind == SimulationPipelineTraceKind.PassCompleted || kind == SimulationPipelineTraceKind.PassFailed;
            if (passTrace && (phase is not (SimulationPipelinePhase.Ingress or
                    SimulationPipelinePhase.Schedule or
                    SimulationPipelinePhase.Step or
                    SimulationPipelinePhase.Egress) || !passId.IsValid || !passVersion.IsValid))
                throw new ArgumentException("Pipeline Pass trace identity is incomplete.");
            bool snapshotTrace = kind == SimulationPipelineTraceKind.SnapshotCaptured || kind == SimulationPipelineTraceKind.SnapshotRestored;
            if (snapshotTrace && (CanonicalTextValue.IsNullOrWhiteSpace(snapshotParticipant) || !snapshotHash.IsValid))
                throw new ArgumentException("Pipeline snapshot trace identity is incomplete.");
            Session = session;
            Pipeline = pipeline;
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

        public SimulationSessionCompositionIdentity Session { get; }
        public SimulationPipelineIdentity Pipeline { get; }
        public SimulationTickSourceIdentity Source { get; }
        public ulong CompletedTick { get; }
        public SimulationPipelineTraceKind Kind { get; }
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

    public enum SimulationModelTraceKind : byte
    {
        Identity = 1,
        Transport = 2,
        Queue = 3,
        Correction = 4,
        OutputDisposition = 5,
        Failure = 6
    }

    public readonly struct SimulationModelTraceRecord
    {
        public SimulationModelTraceRecord(
            SimulationModelTraceKind kind,
            string code,
            string detail,
            ActorId actorId = default,
            ulong localSourceTick = 0,
            ulong authorityTick = 0,
            ulong inputSequence = 0,
            ulong ackSequence = 0,
            int queueDepth = 0,
            int replayCount = 0,
            float primaryValue = 0f,
            float secondaryValue = 0f,
            bool success = true,
            ulong snapshotSequence = 0)
        {
            if (kind is not (SimulationModelTraceKind.Identity or
                    SimulationModelTraceKind.Transport or
                    SimulationModelTraceKind.Queue or
                    SimulationModelTraceKind.Correction or
                    SimulationModelTraceKind.OutputDisposition or
                    SimulationModelTraceKind.Failure) || CanonicalTextValue.IsNullOrWhiteSpace(code) ||
                queueDepth < 0 || replayCount < 0 || float.IsNaN(primaryValue) || float.IsInfinity(primaryValue) ||
                float.IsNaN(secondaryValue) || float.IsInfinity(secondaryValue))
            {
                throw new ArgumentException("Simulation model trace is invalid.");
            }
            Kind = kind;
            Code = code.Trim();
            Detail = detail ?? string.Empty;
            ActorId = actorId;
            LocalSourceTick = localSourceTick;
            AuthorityTick = authorityTick;
            InputSequence = inputSequence;
            AckSequence = ackSequence;
            QueueDepth = queueDepth;
            ReplayCount = replayCount;
            PrimaryValue = primaryValue;
            SecondaryValue = secondaryValue;
            Success = success;
            SnapshotSequence = snapshotSequence;
        }

        public SimulationModelTraceKind Kind { get; }
        public string Code { get; }
        public string Detail { get; }
        public ActorId ActorId { get; }
        public ulong LocalSourceTick { get; }
        public ulong AuthorityTick { get; }
        public ulong InputSequence { get; }
        public ulong AckSequence { get; }
        public int QueueDepth { get; }
        public int ReplayCount { get; }
        public float PrimaryValue { get; }
        public float SecondaryValue { get; }
        public bool Success { get; }
        public ulong SnapshotSequence { get; }
    }

    public enum SimulationWorldTraceKind : byte
    {
        Query = 1,
        Projection = 2,
        Collision = 3,
        Failure = 4
    }

    public readonly struct SimulationWorldTraceRecord
    {
        public SimulationWorldTraceRecord(
            SimulationWorldTraceKind kind,
            string code,
            string detail,
            SimulationTick tick,
            ActorId actorId,
            SolverImplementationId solverId,
            string solverVersion,
            long sourceReference = 0,
            long resultReference = 0,
            int region = 0,
            int traversalCount = 0,
            int includeMask = 0,
            int excludeMask = 0,
            uint localizationStatus = 0,
            uint resolveStatus = 0,
            uint projectionStatus = 0,
            long elapsedStopwatchTicks = 0,
            Float32Vector3 requestedDisplacement = default,
            Float32Vector3 appliedDisplacement = default,
            string disposition = "",
            bool success = true)
        {
            if (kind is not (SimulationWorldTraceKind.Query or
                    SimulationWorldTraceKind.Projection or
                    SimulationWorldTraceKind.Collision or
                    SimulationWorldTraceKind.Failure) || CanonicalTextValue.IsNullOrWhiteSpace(code) ||
                !tick.IsValid || !actorId.IsValid || CanonicalTextValue.IsNullOrWhiteSpace(solverId.Value) ||
                CanonicalTextValue.IsNullOrWhiteSpace(solverVersion) || traversalCount < 0 || elapsedStopwatchTicks < 0)
            {
                throw new ArgumentException("Simulation World trace is invalid.");
            }
            Kind = kind;
            Code = code.Trim();
            Detail = detail ?? string.Empty;
            Tick = tick;
            ActorId = actorId;
            SolverId = solverId;
            SolverVersion = solverVersion.Trim();
            SourceReference = sourceReference;
            ResultReference = resultReference;
            Region = region;
            TraversalCount = traversalCount;
            IncludeMask = includeMask;
            ExcludeMask = excludeMask;
            LocalizationStatus = localizationStatus;
            ResolveStatus = resolveStatus;
            ProjectionStatus = projectionStatus;
            ElapsedStopwatchTicks = elapsedStopwatchTicks;
            RequestedDisplacement = requestedDisplacement;
            AppliedDisplacement = appliedDisplacement;
            Disposition = disposition ?? string.Empty;
            Success = success;
        }

        public SimulationWorldTraceKind Kind { get; }
        public string Code { get; }
        public string Detail { get; }
        public SimulationTick Tick { get; }
        public ActorId ActorId { get; }
        public SolverImplementationId SolverId { get; }
        public string SolverVersion { get; }
        public long SourceReference { get; }
        public long ResultReference { get; }
        public int Region { get; }
        public int TraversalCount { get; }
        public int IncludeMask { get; }
        public int ExcludeMask { get; }
        public uint LocalizationStatus { get; }
        public uint ResolveStatus { get; }
        public uint ProjectionStatus { get; }
        public long ElapsedStopwatchTicks { get; }
        public Float32Vector3 RequestedDisplacement { get; }
        public Float32Vector3 AppliedDisplacement { get; }
        public string Disposition { get; }
        public bool Success { get; }
    }

    public interface ISimulationDiagnosticsSink
    {
        bool IsEnabled { get; }
        bool IsActorEnabled(ActorId actorId);
        void PublishBoundary(SimulationBoundaryTraceRecord record);
        void PublishPipeline(SimulationPipelineTraceRecord record);
        void PublishOperation(SimulationTraceRecord record);
        void PublishModel(SimulationModelTraceRecord record);
        void PublishWorld(SimulationWorldTraceRecord record);
    }

    public sealed class NullSimulationDiagnosticsSink : ISimulationDiagnosticsSink
    {
        public static readonly NullSimulationDiagnosticsSink Instance = new NullSimulationDiagnosticsSink();
        NullSimulationDiagnosticsSink() { }
        public bool IsEnabled => false;
        public bool IsActorEnabled(ActorId actorId) => false;
        public void PublishBoundary(SimulationBoundaryTraceRecord record) { }
        public void PublishPipeline(SimulationPipelineTraceRecord record) { }
        public void PublishOperation(SimulationTraceRecord record) { }
        public void PublishModel(SimulationModelTraceRecord record) { }
        public void PublishWorld(SimulationWorldTraceRecord record) { }
    }

}
