using System;

namespace ThirdPersonSimulation
{
    public interface ISimulationPipelineStepStateStorage
    {
        void PrepareStepStorage(int snapshotCapacity);
        SimulationPipelinePassStateSnapshot CaptureStepState();
    }

    public interface ISimulationPipelineSnapshotRetentionCapacity
    {
        int MaximumRetainedStepSnapshots { get; }
    }

    public sealed class SimulationPipelineStepSnapshotStorage
    {
        internal sealed class Page
        {
            readonly SimulationPipelineStepSnapshotStorage m_Owner;
            readonly int m_Index;
            int m_References;
            internal int Count;

            internal Page(SimulationPipelineStepSnapshotStorage owner, int index,
                CompiledSimulationPipelinePlan plan, int participantCount)
            {
                m_Owner = owner;
                m_Index = index;
                Participants = new SimulationPipelinePassStateSnapshot[participantCount];
                Snapshot = new SimulationPipelineStateSnapshot(plan.Identity, plan.Backend, Participants, this);
            }

            internal SimulationPipelinePassStateSnapshot[] Participants { get; }
            internal SimulationPipelineStateSnapshot Snapshot { get; }
            internal void Begin() => m_References = 1;
            internal void Retain() => m_References++;

            internal void Release()
            {
                if (--m_References != 0)
                    return;
                for (int index = 0; index < Count; index++)
                    Participants[index].ReleaseStepStorage();
                Array.Clear(Participants, 0, Count);
                Count = 0;
                m_Owner.m_Free[m_Owner.m_FreeCount++] = m_Index;
            }
        }

        readonly ISimulationPipelineStateParticipant[] m_Participants;
        readonly Page[] m_Pages;
        readonly int[] m_Free;
        int m_FreeCount;

        public SimulationPipelineStepSnapshotStorage(CompiledSimulationPipelinePlan plan,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            int snapshotCapacity)
        {
            m_Participants = SimulationPipelineStateSnapshotCoordinator.PrepareStepProjection(plan, participants);
            m_Pages = new Page[snapshotCapacity];
            m_Free = new int[snapshotCapacity];
            for (int index = 0; index < m_Pages.Length; index++)
            {
                m_Pages[index] = new Page(this, index, plan, m_Participants.Length);
                m_Free[index] = index;
            }
            m_FreeCount = snapshotCapacity;
        }

        public SimulationPipelineStateSnapshot Capture(ulong tick)
        {
            if (m_FreeCount == 0)
                throw new InvalidOperationException("Pipeline Step snapshot storage exceeded its prepared retention capacity.");
            Page page = m_Pages[m_Free[--m_FreeCount]];
            page.Begin();
            try
            {
                for (int index = 0; index < m_Participants.Length; index++)
                {
                    ISimulationPipelineStateParticipant participant = m_Participants[index];
                    SimulationPipelinePassStateSnapshot snapshot = participant is ISimulationPipelineStepStateStorage storage
                        ? storage.CaptureStepState() : participant.CaptureState();
                    if (snapshot == null)
                        throw SimulationPipelineStateSnapshotCoordinator.Failure("pipeline_state_capture_missing",
                            participant.StateIdentity.PassId, "State participant returned no snapshot.");
                    page.Participants[page.Count++] = snapshot;
                    SimulationPipelineStateSnapshotCoordinator.RequireSnapshotIdentity(participant.StateIdentity, snapshot);
                }
                page.Snapshot.SetPreparedTick(tick);
                return page.Snapshot;
            }
            catch
            {
                page.Release();
                throw;
            }
        }
    }

    public sealed class SimulationPipelinePassStateStorage
    {
        internal sealed class Page
        {
            internal readonly SimulationPipelinePassStateStorage Owner;
            internal readonly int Index;
            internal int References;
            readonly SimulationPipelinePassStateSnapshot m_Snapshot;

            internal Page(SimulationPipelinePassStateStorage owner, int index,
                SimulationPipelineStateParticipantIdentity identity, IValueSnapshotSource source)
            {
                Owner = owner;
                Index = index;
                State = source.CreateValueSnapshot();
                State.Release();
                m_Snapshot = new SimulationPipelinePassStateSnapshot(identity, State, this);
            }

            internal IValueResources State { get; }
            internal SimulationPipelinePassStateSnapshot Snapshot => m_Snapshot;
        }

        readonly IValueSnapshotSource m_Source;
        readonly Page[] m_Pages;
        readonly int[] m_Free;
        int m_FreeCount;

        public SimulationPipelinePassStateStorage(SimulationPipelineStateParticipantIdentity identity,
            int snapshotCapacity, IValueSnapshotSource source)
        {
            m_Source = source;
            m_Pages = new Page[snapshotCapacity];
            m_Free = new int[snapshotCapacity];
            for (int i = 0; i < snapshotCapacity; i++)
            {
                m_Pages[i] = new Page(this, i, identity, source);
                m_Free[i] = i;
            }
            m_FreeCount = snapshotCapacity;
        }

        public SimulationPipelinePassStateSnapshot Capture()
        {
            if (m_FreeCount == 0)
                throw new InvalidOperationException("Pipeline Step snapshot storage exceeded its prepared retention capacity.");
            Page page = m_Pages[m_Free[--m_FreeCount]];
            page.References = 1;
            m_Source.CaptureValueSnapshot(page.State);
            return page.Snapshot;
        }

        internal void Retain(Page page) => page.References++;

        internal void Release(Page page)
        {
            if (--page.References == 0)
            {
                page.State.Release();
                m_Free[m_FreeCount++] = page.Index;
            }
        }
    }
}
