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

        SimulationRestoreDirective m_Directive;
        FixedSimulationSessionSnapshot m_Snapshot;

        public FixedCharacterRestoreTransaction(IReadOnlyList<SimulationActorBinding> roster)
        {
            m_Roster = roster;
            m_Completed = true;
        }

        public FixedCharacterRestoreTransaction Prepare(
            FixedPipelineWorkingState working, SimulationWorldStateSet restored, SimulationRestoreDirective directive,
            FixedSimulationSessionSnapshot snapshot = null)
        {
            m_Working = working;
            m_Previous = working.Current;
            m_Restored = restored;
            m_Directive = directive;
            m_Applied = false;
            m_Validated = false;
            m_Completed = false;
            m_Previous.Retain();
            m_Snapshot = snapshot;
            m_Snapshot?.RetainStorage();
            return this;
        }

        public SimulationSessionRestoreParticipantKind Kind => SimulationSessionRestoreParticipantKind.Character;
        public string Identity => m_Directive.SnapshotId;

        public void Apply()
        {
            RequireOpen();
            if (m_Applied)
                throw new InvalidOperationException("Character restore transaction is already applied.");
            m_Working.Replace(m_Restored);
            m_Applied = true;
            m_Validated = false;
            FixedGameplayStep.AdoptTimelineBaseline(m_Roster, in m_Restored, false);
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
            m_Restored.Release();
            m_Previous.Release();
            m_Completed = true;
        }

        public void Rollback()
        {
            if (m_Completed || !m_Applied)
                return;
            m_Working.Replace(m_Previous);
            FixedGameplayStep.AdoptTimelineBaseline(m_Roster, in m_Previous, true);
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
                    m_Restored.Release();
                    m_Previous.Release();
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
        bool m_Applied;
        bool m_Validated;
        bool m_Completed;

        SimulationRestoreDirective m_Directive;

        public FixedWorldRestoreTransaction(ICharacterWorldSolver solver)
        {
            m_Solver = solver;
            m_Completed = true;
        }

        public FixedWorldRestoreTransaction Prepare(
            FixedPipelineWorkingState working, SimulationWorldStateSet restored, SimulationRestoreDirective directive)
        {
            m_Working = working;
            m_Previous = working.Current;
            m_Restored = restored;
            m_Directive = directive;
            m_Applied = false;
            m_Validated = false;
            m_Completed = false;
            return this;
        }

        public SimulationSessionRestoreParticipantKind Kind => SimulationSessionRestoreParticipantKind.World;
        public string Identity => m_Directive.SnapshotId;

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
            if (!captured.Equals(m_Restored.WorldState))
                throw new InvalidOperationException("World Solver state does not match the restored World state.");
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
