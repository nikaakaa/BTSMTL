using System;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedCombatPipelineStateParticipant : ISimulationPipelineFrameStateParticipant, ISimulationPipelineStepStateStorage
    {
        readonly FixedCombatRuntime m_Combat;
        readonly CanonicalWriter m_Writer;
        readonly FixedCombatCheckpoint m_Checkpoint;
        readonly FixedCombatRestore m_Restore;
        SimulationPipelinePassStateStorage m_StepStorage;

        public FixedCombatPipelineStateParticipant(SimulationPipelinePassDescriptor descriptor, FixedCombatRuntime combat, int maximumSessionCheckpointPayloadBytes)
        {
            m_Combat = combat;
            MaximumPayloadBytes = combat.GetMaximumStatePayloadBytes(maximumSessionCheckpointPayloadBytes);
            MaximumTextValues = m_Combat.StateTextCapacity;
            m_Writer = new CanonicalWriter(new byte[MaximumPayloadBytes]);
            StateIdentity = new SimulationPipelineStateParticipantIdentity(descriptor.PassId,
                descriptor.ImplementationVersion, descriptor.StateOwner,
                StandardFixedPipelinePassContracts.CombatStateSchemaId, StandardFixedPipelinePassContracts.CombatStateSchemaVersion);
            m_Checkpoint = new FixedCombatCheckpoint(StateIdentity, combat);
            m_Restore = new FixedCombatRestore(StateIdentity, combat, MaximumPayloadBytes);
        }
        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public int MaximumPayloadBytes { get; }
        public int MaximumTextValues { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;
        public void PrepareStepStorage(int snapshotCapacity) =>
            m_StepStorage = new SimulationPipelinePassStateStorage(StateIdentity, snapshotCapacity, MaximumPayloadBytes, MaximumTextValues, null);

        public SimulationPipelinePassStateSnapshot CaptureStepState()
        {
            using SimulationPipelinePassStateStorage.Page page = m_StepStorage.BeginWrite();
            m_Combat.WriteState(page.Writer);
            return page.Commit();
        }

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            m_Checkpoint.Capture();
            return m_Checkpoint;
        }
        public ISimulationPipelineFrameState CreateFrameState() => new FixedCombatCheckpoint(StateIdentity, m_Combat);
        public void WriteCheckpointState(CanonicalWriter writer) => m_Combat.WriteState(writer);

        public SimulationPipelinePassStateSnapshot CaptureState()
        {
            m_Writer.Reset();
            m_Combat.WriteState(m_Writer);
            return SimulationPipelinePassStateSnapshot.FromOwnedPayload(StateIdentity.PassId,
                StateIdentity.ImplementationVersion, StateIdentity.StateOwner,
                StateIdentity.StateSchemaId, StateIdentity.StateSchemaVersion, m_Writer.ToArray());
        }
        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            m_Restore.Prepare(snapshot);
            return m_Restore;
        }
    }

    internal sealed class FixedCombatCheckpoint : ISimulationPipelineFrameState
    {
        readonly FixedCombatRuntime m_Combat;
        readonly FixedCombatStateBank m_Before;
        public FixedCombatCheckpoint(SimulationPipelineStateParticipantIdentity identity, FixedCombatRuntime combat)
        {
            Participant = identity;
            m_Combat = combat;
            m_Before = combat.CreateStateBank();
        }
        public SimulationPipelineStateParticipantIdentity Participant { get; }
        public void Capture() => m_Combat.CaptureTo(m_Before);
        public void Restore() => m_Combat.RestoreFrom(m_Before);
        public void CopyFrom(ISimulationPipelineFrameState source) =>
            FixedCombatStateBank.CanonicalCopyInto(((FixedCombatCheckpoint)source).m_Before, m_Before);
        public void Write(CanonicalWriter writer) => FixedCombatRuntime.WriteState(writer, m_Before);
        public void Read(CanonicalReader reader) => m_Combat.ReadState(reader, m_Before);
        public void Dispose() => m_Combat.ReleaseStateBank(m_Before);
    }

    internal sealed class FixedCombatRestore : ISimulationPipelinePassRestoreTransaction
    {
        readonly FixedCombatRuntime m_Combat;
        readonly FixedCombatStateBank m_Before;
        readonly FixedCombatStateBank m_Replacement;
        readonly CanonicalWriter m_Writer;
        readonly CanonicalReader m_Reader = new CanonicalReader(Array.Empty<byte>());
        SimulationPipelinePassStateSnapshot m_Snapshot;
        public FixedCombatRestore(SimulationPipelineStateParticipantIdentity identity, FixedCombatRuntime combat,
            int payloadCapacity)
        {
            Participant = identity;
            m_Combat = combat;
            m_Writer = new CanonicalWriter(new byte[payloadCapacity]);
            m_Before = combat.CreateStateBank();
            m_Replacement = combat.CreateStateBank();
        }
        public SimulationPipelineStateParticipantIdentity Participant { get; }
        public void Prepare(SimulationPipelinePassStateSnapshot snapshot)
        {
            m_Snapshot = snapshot;
            m_Combat.CaptureTo(m_Before);
            m_Reader.Reset(snapshot.Payload);
            m_Reader.SetTextResolver(snapshot.TextResolver);
            try
            {
                m_Combat.ReadState(m_Reader, m_Replacement);
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        public void Apply() => m_Combat.RestoreFrom(m_Replacement);
        public void ValidateApplied()
        {
            m_Writer.Reset();
            m_Combat.WriteState(m_Writer);
            if (!SimulationCanonicalPayloadHash.Compute(m_Writer.WrittenSpan).Equals(m_Snapshot.StateHash))
                throw new InvalidOperationException("Combat state restoration did not reproduce its canonical snapshot.");
        }
        public void CompleteAfterSessionPublish() { }
        public void Rollback() => m_Combat.RestoreFrom(m_Before);
        public void Dispose()
        {
            m_Combat.ReleaseStateBank(m_Before);
            m_Combat.ReleaseStateBank(m_Replacement);
            m_Snapshot = null;
            m_Reader.SetTextResolver(null);
            m_Reader.Reset(Array.Empty<byte>());
        }
    }
}
