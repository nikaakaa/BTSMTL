using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    internal sealed class ServerAuthoritativePassStateRestoreTransaction : ISimulationPipelinePassRestoreTransaction
    {
        enum Stage : byte { Released, Prepared, Applied, Published }

        readonly ISimulationPipelineStateParticipant m_Owner;
        readonly Action<CanonicalReader, CanonicalReader, CanonicalWriter> m_Read;
        readonly SimulationPipelinePassStateStorage m_Storage;
        readonly CanonicalReader m_Reader = new CanonicalReader(Array.Empty<byte>());
        readonly CanonicalReader m_InputReader = new CanonicalReader(Array.Empty<byte>());
        readonly CanonicalWriter m_CanonicalScratch;
        readonly SimulationPipelinePassStateCheckpoint m_Checkpoint;
        SimulationPipelinePassStateSnapshot m_CheckpointState;
        SimulationPipelinePassStateSnapshot m_Previous;
        SimulationPipelinePassStateSnapshot m_Next;
        Stage m_Stage;

        internal ServerAuthoritativePassStateRestoreTransaction(ISimulationPipelineStateParticipant owner,
            int maximumPayloadBytes, int maximumTextValues, int retainedSnapshots,
            Action<CanonicalReader, CanonicalReader, CanonicalWriter> read)
        {
            if (maximumPayloadBytes <= 0)
                throw new InvalidOperationException("ServerAuthoritative participant requires MaximumSessionCheckpointPayloadBytes.");
            m_Owner = owner;
            m_Read = read;
            int pages = checked(retainedSnapshots + 3);
            int texts = SimulationSessionCheckpointPayloadLayout.MaximumNonEmptyTextValues(maximumPayloadBytes,
                maximumTextValues, 0, 0);
            m_Storage = new SimulationPipelinePassStateStorage(owner.StateIdentity, pages,
                maximumPayloadBytes, texts, new CanonicalTextStorage(checked(texts * (pages + 1)),
                    checked((maximumPayloadBytes / sizeof(char)) * (pages + 1))));
            m_CanonicalScratch = new CanonicalWriter(new byte[maximumPayloadBytes]);
            m_Checkpoint = new SimulationPipelinePassStateCheckpoint(owner.StateIdentity, RestoreCheckpoint, ReleaseCheckpoint);
        }

        public SimulationPipelineStateParticipantIdentity Participant => m_Owner.StateIdentity;

        internal SimulationPipelinePassStateSnapshot CaptureState()
        {
            using SimulationPipelinePassStateStorage.Page page = m_Storage.BeginWrite();
            m_Owner.WriteCheckpointState(page.Writer);
            return page.Commit();
        }

        internal ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            m_Checkpoint.Begin();
            try { m_CheckpointState = CaptureState(); return m_Checkpoint; }
            catch { m_Checkpoint.Dispose(); throw; }
        }

        void RestoreCheckpoint() => Read(m_CheckpointState);
        void ReleaseCheckpoint()
        {
            m_CheckpointState?.ReleaseStepStorage();
            m_CheckpointState = null;
        }

        internal ServerAuthoritativePassStateRestoreTransaction Prepare(SimulationPipelinePassStateSnapshot snapshot)
        {
            if (m_Stage != Stage.Released)
                throw new InvalidOperationException("Participant restore is still held by a Session transaction.");
            m_Previous = CaptureState();
            try
            {
                if (snapshot.TextResolver != null)
                {
                    m_Next = snapshot;
                    snapshot.RetainStepStorage();
                }
                else
                {
                    using SimulationPipelinePassStateStorage.Page page = m_Storage.BeginWrite();
                    page.Writer.WriteRawBytes(snapshot.Payload.Span);
                    m_Next = page.Commit();
                    m_Next.SetPreparedTextResolver(page.Text.ReceptionResolver);
                }
                m_Stage = Stage.Prepared;
                return this;
            }
            catch
            {
                m_Previous.ReleaseStepStorage();
                m_Previous = null;
                throw;
            }
        }

        void Read(SimulationPipelinePassStateSnapshot snapshot)
        {
            m_Reader.Reset(snapshot.Payload);
            m_Reader.SetTextResolver(snapshot.TextResolver);
            try { m_Read(m_Reader, m_InputReader, m_CanonicalScratch); m_Reader.RequireComplete(); }
            finally
            {
                m_Reader.Reset(Array.Empty<byte>());
                m_Reader.SetTextResolver(null);
                m_InputReader.Reset(Array.Empty<byte>());
                m_InputReader.SetTextResolver(null);
            }
        }

        public void Apply()
        {
            if (m_Stage != Stage.Prepared)
                throw new InvalidOperationException("Participant restore is not prepared.");
            m_Stage = Stage.Applied;
            Read(m_Next);
        }

        public void ValidateApplied()
        {
            if (m_Stage != Stage.Applied)
                throw new InvalidOperationException("Participant restore was not applied.");
        }

        public void CompleteAfterSessionPublish()
        {
            if (m_Stage != Stage.Applied)
                throw new InvalidOperationException("Participant restore was not applied before Session publish.");
            m_Stage = Stage.Published;
        }

        public void Rollback()
        {
            if (m_Stage != Stage.Applied) return;
            Read(m_Previous);
            m_Stage = Stage.Prepared;
        }

        public void Dispose()
        {
            if (m_Stage == Stage.Applied) Rollback();
            m_Next?.ReleaseStepStorage();
            m_Previous?.ReleaseStepStorage();
            m_Next = null;
            m_Previous = null;
            m_Stage = Stage.Released;
        }

        internal void ReleaseStorage()
        {
            Dispose();
            m_Checkpoint.Dispose();
            m_CanonicalScratch.Dispose();
        }
    }
}
