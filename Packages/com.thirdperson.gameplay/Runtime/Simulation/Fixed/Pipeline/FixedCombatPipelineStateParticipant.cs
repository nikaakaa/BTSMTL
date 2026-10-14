using System;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedCombatPipelineStateParticipant : ISimulationPipelineFrameStateParticipant, ISimulationPipelineStepStateStorage
    {
        readonly FixedCombatRuntime m_Combat;
        readonly FixedCombatCheckpoint m_Checkpoint;
        FixedCombatRestore m_Restore;
        SimulationPipelinePassStateStorage m_StepStorage;

        public FixedCombatPipelineStateParticipant(SimulationPipelinePassDescriptor descriptor, FixedCombatRuntime combat)
        {
            m_Combat = combat;
            StateIdentity = new SimulationPipelineStateParticipantIdentity(descriptor.PassId,
                descriptor.ImplementationVersion, descriptor.StateOwner,
                StandardFixedPipelinePassContracts.CombatStateSchemaId, StandardFixedPipelinePassContracts.CombatStateSchemaVersion);
            m_Checkpoint = new FixedCombatCheckpoint(StateIdentity, combat);
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;

        public void PrepareStepStorage(int snapshotCapacity)
        {
            m_StepStorage = new SimulationPipelinePassStateStorage(StateIdentity, snapshotCapacity, m_Combat);
            m_Restore = new FixedCombatRestore(StateIdentity, m_Combat);
        }

        public SimulationPipelinePassStateSnapshot CaptureStepState() => m_StepStorage.Capture();

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            m_Checkpoint.Capture();
            return m_Checkpoint;
        }

        public ISimulationPipelineFrameState CreateFrameState() => new FixedCombatCheckpoint(StateIdentity, m_Combat);
        public SimulationPipelinePassStateSnapshot CaptureState() => CaptureStepState();

        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            m_Restore.Prepare(snapshot);
            return m_Restore;
        }
    }

    internal sealed class FixedCombatCheckpoint : ISimulationPipelineFrameState
    {
        readonly FixedCombatRuntime m_Combat;
        readonly IValueSnapshotCopy m_Before;

        public FixedCombatCheckpoint(SimulationPipelineStateParticipantIdentity identity, FixedCombatRuntime combat)
        {
            Participant = identity;
            m_Combat = combat;
            m_Before = combat.CreateValueSnapshot();
            m_Before.Release();
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }
        public void Capture() => m_Combat.CaptureValueSnapshot(m_Before);
        public void Restore() => m_Combat.RestoreValueSnapshot(m_Before);

        public void CopyFrom(ISimulationPipelineFrameState source) =>
            m_Before.CopyFrom(((FixedCombatCheckpoint)source).m_Before);

        public void Dispose() => m_Before.Release();
    }

    internal sealed class FixedCombatRestore : ISimulationPipelinePassRestoreTransaction
    {
        readonly FixedCombatRuntime m_Combat;
        readonly IValueResources m_Before;
        readonly IValueResources m_Current;
        IValueResources m_Replacement;

        public FixedCombatRestore(SimulationPipelineStateParticipantIdentity identity, FixedCombatRuntime combat)
        {
            Participant = identity;
            m_Combat = combat;
            m_Before = combat.CreateValueSnapshot();
            m_Before.Release();
            m_Current = combat.CreateValueSnapshot();
            m_Current.Release();
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }

        public void Prepare(SimulationPipelinePassStateSnapshot snapshot)
        {
            m_Combat.CaptureValueSnapshot(m_Before);
            m_Replacement = snapshot.State;
            m_Replacement.Retain();
        }

        public void Apply() => m_Combat.RestoreValueSnapshot(m_Replacement);

        public void ValidateApplied()
        {
            m_Combat.CaptureValueSnapshot(m_Current);
            try
            {
                if (!m_Current.Equals(m_Replacement))
                    throw new InvalidOperationException("Combat state restoration did not reproduce the captured state.");
            }
            finally
            {
                m_Current.Release();
            }
        }

        public void Rollback() => m_Combat.RestoreValueSnapshot(m_Before);

        public void Dispose()
        {
            m_Before.Release();
            m_Replacement.Release();
            m_Replacement = null;
        }
    }
}
