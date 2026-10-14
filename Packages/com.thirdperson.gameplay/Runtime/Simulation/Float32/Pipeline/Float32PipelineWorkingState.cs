using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public sealed class Float32PipelineWorkingState
    {
        SimulationWorldStateSet m_Current;

        public Float32PipelineWorkingState(SimulationWorldStateSet state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            m_Current = state ?? throw new ArgumentNullException(nameof(state));
        }

        public SimulationWorldStateSet Current => m_Current;
        public ulong LastCompletedTick => m_Current.LastCompletedTick;
        public IReadOnlyList<SimulationActorState> Actors => m_Current.Actors;
        public WorldSimulationState World => m_Current.WorldState;

        public void Replace(SimulationWorldStateSet state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            ReplaceCandidate(state);
        }

        void ReplaceCandidate(SimulationWorldStateSet candidate)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            if (candidate.Actors.Count != m_Current.Actors.Count)
                throw new InvalidOperationException("Working state candidate changed the locked Actor roster.");
            for (int i = 0; i < candidate.Actors.Count; i++)
            {
                if (candidate.Actors[i].ActorId != m_Current.Actors[i].ActorId)
                    throw new InvalidOperationException("Working state candidate changed the locked Actor order.");
            }
            m_Current = candidate;
        }
    }

    public sealed class Float32CharacterRestoreTransaction : ISimulationSessionRestoreParticipantTransaction
    {
        readonly Float32PipelineWorkingState m_Working;
        SimulationWorldStateSet m_Previous;
        SimulationWorldStateSet m_Restored;
        readonly IReadOnlyList<SimulationActorBinding> m_Roster;
        StableHash m_Identity;
        string m_IdentityText;
        bool m_Applied;
        bool m_Validated;
        bool m_Completed;

        public Float32CharacterRestoreTransaction(
            Float32PipelineWorkingState working,
            SimulationWorldStateSet restored,
            IReadOnlyList<SimulationActorBinding> roster,
            string identity)
            : this(working, roster)
        {
            if (restored == null)
                throw new ArgumentNullException(nameof(restored));
            m_IdentityText = SimulationIdentity.Require(identity, nameof(identity));
            BeginRestore(restored);
        }

        internal Float32CharacterRestoreTransaction(Float32PipelineWorkingState working,
            IReadOnlyList<SimulationActorBinding> roster)
        {
            m_Working = working;
            m_Roster = roster;
            m_Completed = true;
        }

        internal Float32CharacterRestoreTransaction Prepare(SimulationWorldStateSet restored, StableHash identity)
        {
            m_Identity = identity;
            m_IdentityText = null;
            BeginRestore(restored);
            return this;
        }

        void BeginRestore(SimulationWorldStateSet restored)
        {
            m_Previous = m_Working.Current;
            m_Restored = restored;
            m_Applied = false;
            m_Validated = false;
            m_Completed = false;
            SimulationWorldStateStore.RetainStatePages(m_Previous);
        }

        public SimulationSessionRestoreParticipantKind Kind => SimulationSessionRestoreParticipantKind.Character;
        public string Identity => m_IdentityText ??= m_Identity.ToString();

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
            if (!m_Applied || !ReferenceEquals(m_Working.Current, m_Restored))
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
            if (!m_Completed)
            {
                Rollback();
                SimulationWorldStateStore.ReleaseStatePages(m_Restored);
                SimulationWorldStateStore.ReleaseStatePages(m_Previous);
            }
            m_Completed = true;
            m_Previous = null;
            m_Restored = null;
        }

        void RequireOpen()
        {
            if (m_Completed)
                throw new ObjectDisposedException(nameof(Float32CharacterRestoreTransaction));
        }
    }

    public sealed class Float32WorldRestoreTransaction : ISimulationSessionRestoreParticipantTransaction
    {
        readonly Float32PipelineWorkingState m_Working;
        readonly ICharacterWorldSolver m_Solver;
        SimulationWorldStateSet m_Previous;
        SimulationWorldStateSet m_Restored;
        StableHash m_RestoreHash;
        readonly CanonicalWriter m_HashWriter;
        StableHash m_Identity;
        string m_IdentityText;
        bool m_Applied;
        bool m_Validated;
        bool m_Completed;

        public Float32WorldRestoreTransaction(
            Float32PipelineWorkingState working,
            ICharacterWorldSolver solver,
            SimulationWorldStateSet restored,
            string identity)
            : this(working, solver, new CanonicalWriter())
        {
            if (restored == null)
                throw new ArgumentNullException(nameof(restored));
            m_IdentityText = SimulationIdentity.Require(identity, nameof(identity));
            BeginRestore(restored);
        }

        internal Float32WorldRestoreTransaction(Float32PipelineWorkingState working,
            ICharacterWorldSolver solver, CanonicalWriter hashWriter)
        {
            m_Working = working;
            m_Solver = solver;
            m_HashWriter = hashWriter;
            m_Completed = true;
        }

        internal Float32WorldRestoreTransaction Prepare(SimulationWorldStateSet restored, StableHash identity)
        {
            m_Identity = identity;
            m_IdentityText = null;
            BeginRestore(restored);
            return this;
        }

        void BeginRestore(SimulationWorldStateSet restored)
        {
            m_Previous = m_Working.Current;
            m_Restored = restored;
            m_RestoreHash = WorldSimulationStateCodec.ComputeHash(restored.WorldState, m_HashWriter);
            m_Applied = false;
            m_Validated = false;
            m_Completed = false;
        }

        public SimulationSessionRestoreParticipantKind Kind => SimulationSessionRestoreParticipantKind.World;
        public string Identity => m_IdentityText ??= m_Identity.ToString();

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
            if (!m_Applied || !ReferenceEquals(m_Working.Current, m_Restored))
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
            m_Previous = null;
            m_Restored = null;
        }

        void RequireOpen()
        {
            if (m_Completed)
                throw new ObjectDisposedException(nameof(Float32WorldRestoreTransaction));
        }
    }
}
