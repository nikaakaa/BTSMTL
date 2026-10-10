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
