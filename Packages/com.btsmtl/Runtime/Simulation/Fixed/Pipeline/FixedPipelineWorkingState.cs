using System;
using System.Collections.Generic;

using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedPipelineWorkingState
    {
        SimulationWorldStateSet m_Current;

        public FixedPipelineWorkingState(SimulationWorldStateSet state)
        {
            m_Current = state;
        }

        public SimulationWorldStateSet Current => m_Current;
        public ulong LastCompletedTick => m_Current.LastCompletedTick;
        public IReadOnlyList<SimulationActorState> Actors => m_Current.Actors;
        public WorldSimulationState World => m_Current.WorldState;

        public void Replace(SimulationWorldStateSet state) => m_Current = state;
    }

    public sealed class FixedCharacterRestoreTransaction : ISimulationSessionRestoreParticipantTransaction
    {
        FixedPipelineWorkingState m_Working;
        SimulationWorldStateSet m_Previous;
        SimulationWorldStateSet m_Restored;
        readonly IReadOnlyList<SimulationActorBinding> m_Roster;
        bool m_Applied;
        bool m_Validated;
        bool m_Completed;

        StableHash m_Identity;
        FixedSimulationSessionSnapshot m_Snapshot;

        public FixedCharacterRestoreTransaction(IReadOnlyList<SimulationActorBinding> roster)
        {
            m_Roster = roster;
            m_Completed = true;
        }

        public FixedCharacterRestoreTransaction Prepare(
            FixedPipelineWorkingState working, SimulationWorldStateSet restored, StableHash identity,
            FixedSimulationSessionSnapshot snapshot = null)
        {
            m_Working = working;
            m_Previous = working.Current;
            m_Restored = restored;
            m_Identity = identity;
            m_Applied = false;
            m_Validated = false;
            m_Completed = false;
            SimulationWorldStateStore.RetainStatePages(m_Previous);
            m_Snapshot = snapshot;
            m_Snapshot?.RetainStorage();
            return this;
        }

        public SimulationSessionRestoreParticipantKind Kind => SimulationSessionRestoreParticipantKind.Character;
        public string Identity => m_Identity.ToString();

        public void Apply()
        {
            RequireOpen();
            if (m_Applied)
                throw new InvalidOperationException("Character restore transaction is already applied.");
            m_Working.Replace(m_Restored);
            m_Applied = true;
            m_Validated = false;
            RestoreTimelineSnapshots(m_Restored);
        }

        public void ValidateApplied()
        {
            RequireOpen();
            if (!m_Applied || !m_Working.Current.SharesStateStorage(m_Restored))
                throw new InvalidOperationException("Character restore transaction is not fully applied.");
            m_Validated = true;
        }

        public void CompleteAfterSessionPublish()
        {
            RequireOpen();
            if (!m_Applied || !m_Validated)
                throw new InvalidOperationException("Character restore transaction was not applied and validated before Session publish.");
            SimulationWorldStateStore.ReleaseStatePages(m_Restored);
            SimulationWorldStateStore.ReleaseStatePages(m_Previous);
            m_Completed = true;
        }

        void RestoreTimelineSnapshots(SimulationWorldStateSet state)
        {
            for (int actorIndex = 0; actorIndex < state.Actors.Count; actorIndex++)
            {
                IReadOnlyList<AbilityTimelineRuntimeSnapshot> snapshots = state.Actors[actorIndex].State.TimelineSnapshots;
                for (int index = 0; index < snapshots.Count; index++)
                    m_Roster[actorIndex].TimelineRuntime.ApplyRestore(snapshots[index], state.LastCompletedTick);
            }
        }

        public void Rollback()
        {
            if (m_Completed || !m_Applied)
                return;
            m_Working.Replace(m_Previous);
            RestoreTimelineSnapshots(m_Previous);
            for (int index = 0; index < m_Roster.Count; index++)
                m_Roster[index].TimelineRuntime?.DiscardUnpublishedPlaybacks(m_Previous.Actors[index].State.TimelineSnapshots);
            m_Applied = false;
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
                if (!m_Completed)
                {
                    SimulationWorldStateStore.ReleaseStatePages(m_Restored);
                    SimulationWorldStateStore.ReleaseStatePages(m_Previous);
                }
                m_Completed = true;
                m_Working = null;
                m_Previous = default;
                m_Restored = default;
                m_Snapshot?.ReleaseStorage();
                m_Snapshot = null;
            }
        }

        void RequireOpen()
        {
            if (m_Completed)
                throw new ObjectDisposedException(nameof(FixedCharacterRestoreTransaction));
        }
    }

    public sealed class FixedWorldRestoreTransaction : ISimulationSessionRestoreParticipantTransaction
    {
        FixedPipelineWorkingState m_Working;
        readonly ICharacterWorldSolver m_Solver;
        SimulationWorldStateSet m_Previous;
        SimulationWorldStateSet m_Restored;
        StableHash m_RestoreHash;
        bool m_Applied;
        bool m_Validated;
        bool m_Completed;

        StableHash m_Identity;
        readonly CanonicalWriter m_HashWriter;

        public FixedWorldRestoreTransaction(ICharacterWorldSolver solver, int maximumWorldPayloadBytes)
        {
            m_Solver = solver;
            m_HashWriter = new CanonicalWriter(new byte[maximumWorldPayloadBytes]);
            m_Completed = true;
        }

        public FixedWorldRestoreTransaction Prepare(
            FixedPipelineWorkingState working, SimulationWorldStateSet restored, StableHash identity)
        {
            m_Working = working;
            m_Previous = working.Current;
            m_Restored = restored;
            m_Identity = identity;
            m_Applied = false;
            m_Validated = false;
            m_Completed = false;
            m_RestoreHash = WorldSimulationStateCodec.ComputeHash(restored.WorldState, m_HashWriter);
            return this;
        }

        public SimulationSessionRestoreParticipantKind Kind => SimulationSessionRestoreParticipantKind.World;
        public string Identity => m_Identity.ToString();

        public void Apply()
        {
            RequireOpen();
            if (m_Applied)
                throw new InvalidOperationException("World restore transaction is already applied.");
            m_Solver.Restore(m_Restored.WorldState);
            m_Applied = true;
            m_Validated = false;
        }

        public void ValidateApplied()
        {
            RequireOpen();
            if (!m_Applied || !m_Working.Current.SharesStateStorage(m_Restored))
                throw new InvalidOperationException("World restore transaction is not fully applied.");
            WorldSimulationState captured = m_Solver.Capture(m_Restored.WorldState.WorldRevision);
            StableHash capturedHash = WorldSimulationStateCodec.ComputeHash(captured, m_HashWriter);
            if (!capturedHash.Equals(m_RestoreHash))
                throw new InvalidOperationException("World Solver state does not match the restored canonical World state.");
            m_Validated = true;
        }

        public void CompleteAfterSessionPublish()
        {
            RequireOpen();
            if (!m_Applied || !m_Validated)
                throw new InvalidOperationException("World restore transaction was not applied and validated before Session publish.");
            m_Completed = true;
        }

        public void Rollback()
        {
            if (m_Completed || !m_Applied)
                return;
            m_Solver.Restore(m_Previous.WorldState);
            m_Applied = false;
            m_Validated = false;
        }

        public void Dispose()
        {
            if (!m_Completed)
                Rollback();
            m_Completed = true;
            m_Working = null;
            m_Previous = default;
            m_Restored = default;
        }

        void RequireOpen()
        {
            if (m_Completed)
                throw new ObjectDisposedException(nameof(FixedWorldRestoreTransaction));
        }
    }
}
