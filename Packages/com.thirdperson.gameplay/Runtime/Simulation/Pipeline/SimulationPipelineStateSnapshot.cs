using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Text;

namespace ThirdPersonSimulation
{

    public readonly partial struct SimulationPipelineStateParticipantIdentity : IEquatable<SimulationPipelineStateParticipantIdentity>
    {
        public SimulationPipelineStateParticipantIdentity(
            SimulationPipelinePassId passId,
            SimulationPipelinePassImplementationVersion implementationVersion,
            string stateOwner,
            string stateSchemaId,
            int stateSchemaVersion)
        {
            if (!passId.IsValid || !implementationVersion.IsValid || stateSchemaVersion <= 0)
                throw new ArgumentException("Pipeline state participant identity is incomplete.");
            PassId = passId;
            ImplementationVersion = implementationVersion;
            StateOwner = SimulationIdentity.Require(stateOwner, nameof(stateOwner));
            StateSchemaId = SimulationIdentity.Require(stateSchemaId, nameof(stateSchemaId));
            StateSchemaVersion = stateSchemaVersion;
        }

        public SimulationPipelinePassId PassId { get; }
        public SimulationPipelinePassImplementationVersion ImplementationVersion { get; }
        [ValueTextRules(Required = true)]
        public string StateOwner { get; }
        [ValueTextRules(Required = true)]
        public string StateSchemaId { get; }
        public int StateSchemaVersion { get; }

        public bool Equals(SimulationPipelineStateParticipantIdentity other)
        {
            return PassId.Equals(other.PassId) && ImplementationVersion.Equals(other.ImplementationVersion) &&
                   string.Equals(StateOwner, other.StateOwner, StringComparison.Ordinal) &&
                   string.Equals(StateSchemaId, other.StateSchemaId, StringComparison.Ordinal) &&
                   StateSchemaVersion == other.StateSchemaVersion;
        }

        public override bool Equals(object obj) => obj is SimulationPipelineStateParticipantIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(PassId, ImplementationVersion, StateOwner, StateSchemaId, StateSchemaVersion);

        [ValueValidation]
        void RequireRecord()
        {
            if (StateSchemaVersion <= 0)
                throw new ArgumentException("Pipeline state participant identity is incomplete.");
        }
    }

    [ValueOperations(Resources = false)]
    public sealed partial class SimulationPipelineStateSnapshot : IDisposable, IEquatable<SimulationPipelineStateSnapshot>
    {
        readonly IReadOnlyList<SimulationPipelinePassStateSnapshot> m_Participants;
        [ValueEqualityIgnore]
        readonly SimulationPipelineStepSnapshotStorage.Page m_StepStorage;


        private SimulationPipelineStateSnapshot(
            SimulationPipelineIdentity pipeline,
            SimulationComponentIdentity backend,
            ulong lastCompletedTick,
            SimulationPipelinePassStateSnapshot[] participants)
        {
            if (!pipeline.IsValid || !backend.IsValid || backend.Role != SimulationComponentRole.ExecutionBackend)
                throw new ArgumentException("Pipeline state snapshot identity is incomplete.");
            SimulationPipelinePassStateSnapshot[] values = participants ??
                Array.Empty<SimulationPipelinePassStateSnapshot>();
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null)
                    throw new ArgumentException("Pipeline state snapshot contains a missing participant.", nameof(participants));
            }
            Array.Sort(values, (left, right) => left.PassId.CompareTo(right.PassId));
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0 && values[i - 1].PassId.Equals(values[i].PassId))
                    throw new ArgumentException("Pipeline state snapshot contains a missing or duplicate participant.", nameof(participants));
            }
            Pipeline = pipeline;
            Backend = backend;
            LastCompletedTick = lastCompletedTick;
            m_Participants = values;
        }

        public SimulationPipelineIdentity Pipeline { get; }
        public SimulationComponentIdentity Backend { get; }
        public ulong LastCompletedTick { get; private set; }
        public IReadOnlyList<SimulationPipelinePassStateSnapshot> Participants => m_Participants;

        public static SimulationPipelineStateSnapshot FromOwnedParticipants(
            SimulationPipelineIdentity pipeline,
            SimulationComponentIdentity backend,
            ulong lastCompletedTick,
            SimulationPipelinePassStateSnapshot[] ownedParticipants) =>
            new SimulationPipelineStateSnapshot(pipeline, backend, lastCompletedTick, ownedParticipants);

        public void RetainStepStorage()
        {
            if (m_StepStorage != null)
            {
                m_StepStorage.Retain();
                return;
            }
            for (int i = 0; i < m_Participants.Count; i++)
                m_Participants[i].RetainStepStorage();
        }

        public void ReleaseStepStorage()
        {
            if (m_StepStorage != null)
            {
                m_StepStorage.Release();
                return;
            }
            for (int i = 0; i < m_Participants.Count; i++)
                m_Participants[i].ReleaseStepStorage();
        }

        public void Dispose() => ReleaseStepStorage();

        public SimulationPipelineStateSnapshot(
            SimulationPipelineIdentity pipeline, SimulationComponentIdentity backend,
            SimulationPipelinePassStateSnapshot[] preparedParticipants)
            : this(pipeline, backend, preparedParticipants, null)
        {
        }

        internal SimulationPipelineStateSnapshot(
            SimulationPipelineIdentity pipeline, SimulationComponentIdentity backend,
            SimulationPipelinePassStateSnapshot[] preparedParticipants,
            SimulationPipelineStepSnapshotStorage.Page stepStorage)
        {
            Pipeline = pipeline;
            Backend = backend;
            m_Participants = preparedParticipants;
            m_StepStorage = stepStorage;
        }

        public void SetPreparedTick(ulong tick) => LastCompletedTick = tick;

    }

    public interface ISimulationPipelinePassRestoreTransaction : IDisposable
    {
        SimulationPipelineStateParticipantIdentity Participant { get; }
        void Apply();
        void ValidateApplied();
        void Rollback();
    }

    public interface ISimulationPipelinePassRestoreCommit
    {
        void CompleteAfterSessionPublish();
    }

    public interface ISimulationPipelineStateParticipant
    {
        SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        SimulationPipelineStepProjectionMode StepProjectionMode { get; }
        ISimulationPipelinePassStateCheckpoint CaptureCheckpoint();
        SimulationPipelinePassStateSnapshot CaptureState();
        ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot);
    }

    public interface ISimulationPipelineFrameStateParticipant : ISimulationPipelineStateParticipant
    {
        ISimulationPipelineFrameState CreateFrameState();
    }

    public interface ISimulationPipelineFrameState : ISimulationPipelinePassStateCheckpoint
    {
        IValueSnapshotCopy State { get; }
        void Capture();
        void CopyFrom(ISimulationPipelineFrameState source);
    }

    internal sealed class SimulationPipelineStateParticipantSet : IReadOnlyList<ISimulationPipelineStateParticipant>
    {
        public SimulationPipelineStateParticipantSet(
            CompiledSimulationPipelinePlan plan,
            ISimulationPipelineStateParticipant[] values)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            Values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public CompiledSimulationPipelinePlan Plan { get; }
        internal ISimulationPipelineStateParticipant[] Values { get; }
        public int Count => Values.Length;
        public ISimulationPipelineStateParticipant this[int index] => Values[index];
        public IEnumerator<ISimulationPipelineStateParticipant> GetEnumerator() =>
            ((IEnumerable<ISimulationPipelineStateParticipant>)Values).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Values.GetEnumerator();
    }

    public enum SimulationPipelineStepProjectionMode : byte
    {
        Include = 1,
        ReconstructForRestore = 2
    }

    public interface ISimulationPipelinePassStateCheckpoint : IDisposable
    {
        SimulationPipelineStateParticipantIdentity Participant { get; }
        void Restore();
    }

    public sealed class SimulationPipelinePassStateCheckpoint : ISimulationPipelinePassStateCheckpoint
    {
        Action m_Restore;
        readonly Action m_PreparedRestore;
        readonly Action m_Release;
        readonly IDisposable m_CapturedState;
        bool m_Restored;

        public SimulationPipelinePassStateCheckpoint(
            SimulationPipelineStateParticipantIdentity participant,
            Action restore)
        {
            Participant = participant;
            m_Restore = restore ?? throw new ArgumentNullException(nameof(restore));
        }

        public SimulationPipelinePassStateCheckpoint(
            SimulationPipelineStateParticipantIdentity participant, Action restore, IDisposable capturedState)
            : this(participant, restore)
        {
            m_CapturedState = capturedState;
        }

        public SimulationPipelinePassStateCheckpoint(
            SimulationPipelineStateParticipantIdentity participant, Action restore, Action release)
        {
            Participant = participant;
            m_PreparedRestore = restore;
            m_Release = release;
        }

        public void Begin()
        {
            if (m_Restore != null)
                throw new InvalidOperationException("Pipeline participant checkpoint is still held by a transaction.");
            m_Restore = m_PreparedRestore;
            m_Restored = false;
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }

        public void Restore()
        {
            if (m_Restore == null)
                throw new ObjectDisposedException(nameof(SimulationPipelinePassStateCheckpoint));
            if (m_Restored)
                return;
            m_Restore();
            m_Restored = true;
        }

        public void Dispose()
        {
            if (m_Restore == null)
                return;
            m_Restore = null;
            m_Release?.Invoke();
            m_CapturedState?.Dispose();
        }
    }

    public sealed class SimulationPipelineStateCheckpointSet : IDisposable
    {
        enum CheckpointStage : byte
        {
            Released,
            Capturing,
            Captured,
            Releasing
        }

        readonly IReadOnlyList<ISimulationPipelineStateParticipant> m_Participants;
        readonly ISimulationPipelinePassStateCheckpoint[] m_Checkpoints;
        CheckpointStage m_Stage;
        int m_CheckpointCount;

        internal SimulationPipelineStateCheckpointSet(
            IReadOnlyList<ISimulationPipelineStateParticipant> participants)
        {
            m_Participants = participants;
            m_Checkpoints = participants.Count == 0
                ? Array.Empty<ISimulationPipelinePassStateCheckpoint>()
                : new ISimulationPipelinePassStateCheckpoint[participants.Count];
        }

        internal SimulationPipelineStateCheckpointSet Capture()
        {
            if (m_Stage != CheckpointStage.Released)
                throw new InvalidOperationException("Pipeline state checkpoints are already held by a transaction.");
            m_Stage = CheckpointStage.Capturing;
            try
            {
                for (int i = 0; i < m_Participants.Count; i++)
                {
                    ISimulationPipelineStateParticipant participant = m_Participants[i];
                    ISimulationPipelinePassStateCheckpoint checkpoint = participant.CaptureCheckpoint() ??
                        throw SimulationPipelineStateSnapshotCoordinator.Failure(
                            "pipeline_state_checkpoint_missing", participant.StateIdentity.PassId,
                            "State participant returned no transaction checkpoint.");
                    m_Checkpoints[m_CheckpointCount++] = checkpoint;
                    if (!checkpoint.Participant.Equals(participant.StateIdentity))
                        throw SimulationPipelineStateSnapshotCoordinator.Failure(
                            "pipeline_state_checkpoint_identity_mismatch", participant.StateIdentity.PassId,
                            "State checkpoint identity does not match its participant.");
                }
                m_Stage = CheckpointStage.Captured;
                return this;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Restore()
        {
            if (m_Stage != CheckpointStage.Captured)
                throw new ObjectDisposedException(nameof(SimulationPipelineStateCheckpointSet));
            Exception failure = null;
            for (int i = m_CheckpointCount - 1; i >= 0; i--)
            {
                try
                {
                    m_Checkpoints[i].Restore();
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
            if (failure != null)
                throw new InvalidOperationException("Pipeline state checkpoint restore failed.", failure);
        }

        public void Dispose()
        {
            if (m_Stage == CheckpointStage.Released || m_Stage == CheckpointStage.Releasing)
                return;
            m_Stage = CheckpointStage.Releasing;
            int checkpointCount = m_CheckpointCount;
            m_CheckpointCount = 0;
            try
            {
                SimulationPipelineStateSnapshotCoordinator.ReleaseOwnedTransactions(m_Checkpoints, checkpointCount);
            }
            finally
            {
                m_Stage = CheckpointStage.Released;
            }
        }
    }

    public enum SimulationSessionRestoreParticipantKind : byte
    {
        Character = 1,
        World = 2,
        Pipeline = 3
    }

    public interface ISimulationSessionRestoreParticipantTransaction : IDisposable
    {
        SimulationSessionRestoreParticipantKind Kind { get; }
        string Identity { get; }
        void Apply();
        void ValidateApplied();
        void CompleteAfterSessionPublish();
        void Rollback();
    }

    public sealed class SimulationPipelineStateRestoreTransaction : ISimulationSessionRestoreParticipantTransaction
    {
        readonly ISimulationPipelinePassRestoreTransaction[] m_Participants;
        int m_AppliedCount;
        bool m_Validated;
        bool m_Completed;

        readonly CompiledSimulationPipelinePlan m_Plan;
        readonly IReadOnlyList<ISimulationPipelineStateParticipant> m_StateParticipants;

        public SimulationPipelineStateRestoreTransaction(
            CompiledSimulationPipelinePlan plan, IReadOnlyList<ISimulationPipelineStateParticipant> participants)
        {
            m_Plan = plan;
            m_StateParticipants = SimulationPipelineStateSnapshotCoordinator.CreateParticipantSet(plan, participants);
            m_Participants = new ISimulationPipelinePassRestoreTransaction[m_StateParticipants.Count];
            m_Completed = true;
        }

        public SimulationPipelineStateRestoreTransaction Prepare(SimulationPipelineStateSnapshot snapshot)
        {
            Snapshot = snapshot;
            m_AppliedCount = 0;
            m_Validated = false;
            m_Completed = false;
            try
            {
                SimulationPipelineStateSnapshotCoordinator.PrepareRestoreInto(m_Plan, snapshot, m_StateParticipants, m_Participants);
                return this;
            }
            catch
            {
                Snapshot = null;
                m_Completed = true;
                throw;
            }
        }

        public SimulationPipelineStateSnapshot Snapshot { get; private set; }
        public SimulationSessionRestoreParticipantKind Kind => SimulationSessionRestoreParticipantKind.Pipeline;
        public string Identity => $"{Snapshot.Pipeline.Id}/tick/{Snapshot.LastCompletedTick}";

        public void Apply()
        {
            RequireOpen();
            if (m_AppliedCount != 0)
                throw new InvalidOperationException("Pipeline restore transaction is already applied.");
            m_Validated = false;
            try
            {
                for (int i = 0; i < m_Participants.Length; i++)
                {
                    m_AppliedCount++;
                    m_Participants[i].Apply();
                }
            }
            catch (Exception restoreFailure)
            {
                try
                {
                    Rollback();
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("Pipeline restore and its rollback both failed.", restoreFailure, rollbackFailure);
                }
                throw;
            }
        }

        public void ValidateApplied()
        {
            RequireOpen();
            if (m_AppliedCount != m_Participants.Length)
                throw new InvalidOperationException("Pipeline restore transaction is not fully applied.");
            for (int i = 0; i < m_Participants.Length; i++)
                m_Participants[i].ValidateApplied();
            m_Validated = true;
        }

        public void CompleteAfterSessionPublish()
        {
            RequireOpen();
            if (m_AppliedCount != m_Participants.Length || !m_Validated)
                throw new InvalidOperationException("Pipeline restore transaction was not applied and validated before Session publish.");
            for (int i = 0; i < m_Participants.Length; i++)
                if (m_Participants[i] is ISimulationPipelinePassRestoreCommit commit)
                    commit.CompleteAfterSessionPublish();
            m_Completed = true;
        }

        public void Rollback()
        {
            if (m_Completed)
                return;
            for (int i = m_AppliedCount - 1; i >= 0; i--)
                m_Participants[i].Rollback();
            m_AppliedCount = 0;
            m_Validated = false;
        }

        public void Dispose()
        {
            try
            {
                if (!m_Completed)
                    Rollback();
            }
            finally
            {
                Snapshot = null;
                m_Completed = true;
                SimulationPipelineStateSnapshotCoordinator.ReleaseOwnedTransactions(m_Participants, m_Participants.Length);
            }
        }

        void RequireOpen()
        {
            if (m_Completed)
                throw new ObjectDisposedException(nameof(SimulationPipelineStateRestoreTransaction));
        }

    }

    public sealed class SimulationSessionRestoreTransaction : IDisposable
    {
        readonly ISimulationSessionRestoreParticipantTransaction[] m_Participants;
        int m_AppliedCount;
        bool m_Validated;
        bool m_Completed;

        public SimulationSessionRestoreTransaction()
        {
            m_Participants = new ISimulationSessionRestoreParticipantTransaction[3];
            m_Completed = true;
        }

        public SimulationSessionRestoreTransaction Prepare(
            ISimulationSessionRestoreParticipantTransaction character,
            ISimulationSessionRestoreParticipantTransaction world,
            ISimulationSessionRestoreParticipantTransaction pipeline)
        {
            m_Participants[0] = character;
            m_Participants[1] = world;
            m_Participants[2] = pipeline;
            m_AppliedCount = 0;
            m_Validated = false;
            m_Completed = false;
            return this;
        }

        public void ApplyAndValidate()
        {
            RequireOpen();
            if (m_AppliedCount != 0)
                throw new InvalidOperationException("Session restore transaction is already applied.");
            m_Validated = false;
            try
            {
                for (int i = 0; i < m_Participants.Length; i++)
                {
                    m_AppliedCount++;
                    m_Participants[i].Apply();
                }
                for (int i = 0; i < m_Participants.Length; i++)
                    m_Participants[i].ValidateApplied();
                m_Validated = true;
            }
            catch (Exception restoreFailure)
            {
                try
                {
                    Rollback();
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("Session restore and its rollback both failed.", restoreFailure, rollbackFailure);
                }
                throw;
            }
        }

        public void CompleteAfterAtomicSessionPublish()
        {
            RequireOpen();
            if (m_AppliedCount != m_Participants.Length || !m_Validated)
                throw new InvalidOperationException("Session restore transaction is not fully applied and validated.");
            for (int i = 0; i < m_Participants.Length; i++)
                m_Participants[i].CompleteAfterSessionPublish();
            m_Completed = true;
        }

        public void Rollback()
        {
            if (m_Completed)
                return;
            for (int i = m_AppliedCount - 1; i >= 0; i--)
                m_Participants[i].Rollback();
            m_AppliedCount = 0;
            m_Validated = false;
        }

        public void Dispose()
        {
            try
            {
                if (!m_Completed)
                    Rollback();
            }
            finally
            {
                m_Completed = true;
                SimulationPipelineStateSnapshotCoordinator.ReleaseOwnedTransactions(m_Participants, m_Participants.Length);
            }
        }

        void RequireOpen()
        {
            if (m_Completed)
                throw new ObjectDisposedException(nameof(SimulationSessionRestoreTransaction));
        }

        public static SimulationSessionRestoreTransaction FromOwnedTransactions(
            ISimulationSessionRestoreParticipantTransaction[] ownedParticipants) =>
            new SimulationSessionRestoreTransaction().Prepare(ownedParticipants[0], ownedParticipants[1], ownedParticipants[2]);
    }

    public static class SimulationPipelineStateSnapshotCoordinator
    {
        internal static void ReleaseOwnedTransactions<T>(T[] transactions, int count) where T : class, IDisposable
        {
            Exception failure = null;
            for (int index = count - 1; index >= 0; index--)
            {
                T transaction = transactions[index];
                transactions[index] = null;
                try
                {
                    transaction?.Dispose();
                }
                catch (Exception exception)
                {
                    failure = failure == null ? exception : new AggregateException(
                        "Releasing owned state transactions failed.", failure, exception);
                }
            }
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        internal static IReadOnlyList<ISimulationPipelineStateParticipant> CreateParticipantSet(
            CompiledSimulationPipelinePlan plan,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            return new SimulationPipelineStateParticipantSet(
                plan,
                ValidateParticipantSet(plan, participants));
        }

        public static SimulationPipelineStateSnapshot Capture(
            CompiledSimulationPipelinePlan plan,
            ulong lastCompletedTick,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            ISimulationPipelineStateParticipant[] values = ValidateParticipantSet(plan, participants);
            var snapshots = values.Length == 0
                ? Array.Empty<SimulationPipelinePassStateSnapshot>()
                : new SimulationPipelinePassStateSnapshot[values.Length];
            int capturedCount = 0;
            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    SimulationPipelinePassStateSnapshot snapshot = values[i].CaptureState() ??
                        throw Failure("pipeline_state_capture_missing", values[i].StateIdentity.PassId, "State participant returned no snapshot.");
                    snapshots[capturedCount++] = snapshot;
                    RequireSnapshotIdentity(values[i].StateIdentity, snapshot);
                }
                return SimulationPipelineStateSnapshot.FromOwnedParticipants(plan.Identity, plan.Backend, lastCompletedTick, snapshots);
            }
            catch
            {
                for (int i = 0; i < capturedCount; i++)
                    snapshots[i].ReleaseStepStorage();
                throw;
            }
        }

        internal static ISimulationPipelineStateParticipant[] PrepareStepProjection(
            CompiledSimulationPipelinePlan plan,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            ISimulationPipelineStateParticipant[] values = ValidateParticipantSet(plan, participants);
            int snapshotCount = 0;
            for (int i = 0; i < values.Length; i++)
            {
                SimulationPipelineStepProjectionMode projectionMode = values[i].StepProjectionMode;
                if ((byte)projectionMode < (byte)SimulationPipelineStepProjectionMode.Include ||
                    (byte)projectionMode > (byte)SimulationPipelineStepProjectionMode.ReconstructForRestore)
                {
                    throw Failure("pipeline_step_projection_mode_invalid", values[i].StateIdentity.PassId, "State participant Step projection mode is invalid.");
                }
                if (projectionMode == SimulationPipelineStepProjectionMode.Include)
                    snapshotCount++;
            }
            var projected = new ISimulationPipelineStateParticipant[snapshotCount];
            int snapshotIndex = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].StepProjectionMode == SimulationPipelineStepProjectionMode.Include)
                    projected[snapshotIndex++] = values[i];
            }
            return projected;
        }

        public static SimulationPipelineStateRestoreTransaction PrepareRestore(
            CompiledSimulationPipelinePlan plan, SimulationPipelineStateSnapshot snapshot,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants) =>
            new SimulationPipelineStateRestoreTransaction(plan, participants).Prepare(snapshot);

        internal static void PrepareRestoreInto(
            CompiledSimulationPipelinePlan plan, SimulationPipelineStateSnapshot snapshot,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            ISimulationPipelinePassRestoreTransaction[] transactions)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (!snapshot.Pipeline.Equals(plan.Identity) || !snapshot.Backend.Equals(plan.Backend))
                throw Failure("pipeline_snapshot_identity_mismatch", default, "Pipeline snapshot identity does not match the compiled plan and Backend.");
            ISimulationPipelineStateParticipant[] values = ValidateParticipantSet(plan, participants);
            if (values.Length != snapshot.Participants.Count)
                throw Failure("pipeline_snapshot_participant_count_mismatch", default, "Pipeline snapshot participant count does not match the compiled plan.");
            int preparedCount = 0;
            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    SimulationPipelinePassStateSnapshot state = snapshot.Participants[i];
                    RequireSnapshotIdentity(values[i].StateIdentity, state);
                    ISimulationPipelinePassRestoreTransaction transaction = values[i].PrepareRestore(state) ??
                        throw Failure("pipeline_state_restore_prepare_missing", values[i].StateIdentity.PassId, "State participant returned no restore transaction.");
                    transactions[preparedCount++] = transaction;
                    if (!transaction.Participant.Equals(values[i].StateIdentity))
                        throw Failure("pipeline_state_restore_participant_mismatch", values[i].StateIdentity.PassId, "Prepared restore transaction identity does not match its participant.");
                }

            }
            catch
            {
                ReleaseOwnedTransactions(transactions, preparedCount);
                throw;
            }
        }

        static ISimulationPipelineStateParticipant[] ValidateParticipantSet(
            CompiledSimulationPipelinePlan plan,
            IReadOnlyList<ISimulationPipelineStateParticipant> participants)
        {
            if (participants is SimulationPipelineStateParticipantSet participantSet)
            {
                if (!ReferenceEquals(participantSet.Plan, plan))
                    throw Failure("pipeline_state_participant_plan_mismatch", default, "Runtime Pipeline state participant set belongs to another compiled plan.");
                return participantSet.Values;
            }
            int expectedCount = 0;
            for (int i = 0; i < plan.Passes.Count; i++)
            {
                if (plan.Passes[i].Descriptor.StateClass == SimulationPipelinePassStateClass.SnapshotParticipant)
                    expectedCount++;
            }
            var expected = expectedCount == 0
                ? Array.Empty<CompiledSimulationPipelinePass>()
                : new CompiledSimulationPipelinePass[expectedCount];
            int expectedIndex = 0;
            for (int i = 0; i < plan.Passes.Count; i++)
            {
                if (plan.Passes[i].Descriptor.StateClass == SimulationPipelinePassStateClass.SnapshotParticipant)
                    expected[expectedIndex++] = plan.Passes[i];
            }
            var values = participants == null || participants.Count == 0
                ? Array.Empty<ISimulationPipelineStateParticipant>()
                : new ISimulationPipelineStateParticipant[participants.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = participants[i];
                if (values[i] == null)
                    throw Failure("pipeline_state_participant_missing", default, "Runtime Pipeline state participant is missing.");
            }
            Array.Sort(values, (left, right) => left.StateIdentity.PassId.CompareTo(right.StateIdentity.PassId));
            Array.Sort(expected, (left, right) => left.Descriptor.PassId.CompareTo(right.Descriptor.PassId));
            if (values.Length != expected.Length)
                throw Failure("pipeline_state_participant_count_mismatch", default, "Runtime Pipeline state participant count does not match the compiled plan.");
            for (int i = 0; i < values.Length; i++)
            {
                SimulationPipelineStateParticipantIdentity actual = values[i].StateIdentity;
                CompiledSimulationPipelinePass required = expected[i];
                if (!actual.PassId.Equals(required.Descriptor.PassId) ||
                    !actual.ImplementationVersion.Equals(required.Descriptor.ImplementationVersion) ||
                    !string.Equals(actual.StateOwner, required.Descriptor.StateOwner, StringComparison.Ordinal) ||
                    !string.Equals(actual.StateSchemaId, required.Factory.StateSchemaId, StringComparison.Ordinal) ||
                    actual.StateSchemaVersion != required.Factory.StateSchemaVersion)
                {
                    throw Failure("pipeline_state_participant_identity_mismatch", required.Descriptor.PassId, "Runtime Pipeline state participant version or schema does not match the compiled plan.");
                }
                if (i > 0 && values[i - 1].StateIdentity.PassId.Equals(actual.PassId))
                    throw Failure("pipeline_state_participant_duplicate", actual.PassId, "Runtime Pipeline state participant is duplicated.");
            }
            return values;
        }

        internal static void RequireSnapshotIdentity(
            SimulationPipelineStateParticipantIdentity expected,
            SimulationPipelinePassStateSnapshot snapshot)
        {
            if (!snapshot.PassId.Equals(expected.PassId) ||
                !snapshot.ImplementationVersion.Equals(expected.ImplementationVersion) ||
                !string.Equals(snapshot.StateOwner, expected.StateOwner, StringComparison.Ordinal) ||
                !string.Equals(snapshot.StateSchemaId, expected.StateSchemaId, StringComparison.Ordinal) ||
                snapshot.StateSchemaVersion != expected.StateSchemaVersion)
            {
                throw Failure("pipeline_state_snapshot_version_mismatch", expected.PassId, "Pipeline state snapshot participant version or schema does not match.");
            }
        }

        internal static SimulationSessionCompositionException Failure(string code, SimulationPipelinePassId passId, string message)
        {
            return new SimulationSessionCompositionException(new SimulationSessionFailure(
                SimulationSessionFailureStage.Runtime,
                code,
                message,
                passIdentity: passId.ToString()));
        }
    }
}
