using System;

namespace ThirdPersonSimulation
{
    public interface ISimulationPipelineStepStateStorage
    {
        int MaximumPayloadBytes { get; }
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
        readonly CanonicalWriter m_HashWriter;
        int m_FreeCount;

        public SimulationPipelineStepSnapshotStorage(CompiledSimulationPipelinePlan plan,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            int snapshotCapacity, int maximumPayloadBytes)
        {
            m_Participants = SimulationPipelineStateSnapshotCoordinator.PrepareStepProjection(plan, participants);
            m_Pages = new Page[snapshotCapacity];
            m_Free = new int[snapshotCapacity];
            m_HashWriter = new CanonicalWriter(new byte[maximumPayloadBytes]);
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
                page.Snapshot.SetPreparedTick(tick, m_HashWriter);
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
        public sealed class Page : IDisposable
        {
            internal readonly SimulationPipelinePassStateStorage Owner;
            internal readonly int Index;
            internal int References;
            readonly SimulationPipelinePassStateSnapshot m_Snapshot;

            internal Page(SimulationPipelinePassStateStorage owner, int index,
                SimulationPipelineStateParticipantIdentity identity, int payloadCapacity, int maximumTextValues)
            {
                Owner = owner;
                Index = index;
                var payload = new byte[payloadCapacity];
                Writer = new CanonicalWriter(payload);
                Text = new CanonicalTextReferences(maximumTextValues, owner.TextStorage);
                Writer.SetTextCapture(Text);
                m_Snapshot = SimulationPipelinePassStateSnapshot.FromStepStorage(identity, payload, this);
            }

            public CanonicalWriter Writer { get; }
            public CanonicalTextReferences Text { get; }
            public void Release() => Owner.Release(this);

            public SimulationPipelinePassStateSnapshot Commit()
            {
                m_Snapshot.CommitStepPayload(checked((int)Writer.Length));
                Owner.Retain(this);
                return m_Snapshot;
            }

            public void Dispose() => Owner.Release(this);
        }

        readonly Page[] m_Pages;
        public CanonicalTextStorage TextStorage { get; }
        readonly int[] m_Free;
        int m_FreeCount;

        public SimulationPipelinePassStateStorage(SimulationPipelineStateParticipantIdentity identity,
            int snapshotCapacity, int payloadCapacity, int maximumTextValues)
            : this(identity, snapshotCapacity, payloadCapacity, maximumTextValues,
                new CanonicalTextStorage(checked(snapshotCapacity * maximumTextValues),
                    checked(snapshotCapacity * (payloadCapacity / sizeof(char)))))
        {
        }

        public SimulationPipelinePassStateStorage(SimulationPipelineStateParticipantIdentity identity,
            int snapshotCapacity, int payloadCapacity, int maximumTextValues, CanonicalTextStorage textStorage)
        {
            TextStorage = textStorage;
            m_Pages = new Page[snapshotCapacity];
            m_Free = new int[snapshotCapacity];
            for (int i = 0; i < snapshotCapacity; i++)
            {
                m_Pages[i] = new Page(this, i, identity, payloadCapacity, maximumTextValues);
                m_Free[i] = i;
            }
            m_FreeCount = snapshotCapacity;
        }

        public Page BeginWrite()
        {
            if (m_FreeCount == 0)
                throw new InvalidOperationException("Pipeline Step snapshot storage exceeded its prepared retention capacity.");
            Page page = m_Pages[m_Free[--m_FreeCount]];
            page.References = 1;
            page.Writer.Reset();
            return page;
        }

        internal void Retain(Page page) => page.References++;

        internal void Release(Page page)
        {
            if (--page.References == 0)
            {
                page.Text.Reset();
                m_Free[m_FreeCount++] = page.Index;
            }
        }
    }
}
