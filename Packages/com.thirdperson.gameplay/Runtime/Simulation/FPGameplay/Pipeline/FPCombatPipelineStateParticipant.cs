using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPCombatPipelineStateParticipant : ISimulationPipelineFrameStateParticipant, ISimulationPipelineStepStateStorage
    {
        readonly FPCombatRuntime m_Combat;
        readonly FPCombatCheckpoint m_Checkpoint;
        FPCombatRestore m_Restore;
        SimulationPipelinePassStateStorage m_StepStorage;

        public FPCombatPipelineStateParticipant(SimulationPipelinePassDescriptor descriptor, FPCombatRuntime combat)
        {
            m_Combat = combat;
            StateIdentity = new SimulationPipelineStateParticipantIdentity(descriptor.PassId,
                descriptor.ImplementationVersion, descriptor.StateOwner,
                StandardFPPipelinePassContracts.CombatStateSchemaId, StandardFPPipelinePassContracts.CombatStateSchemaVersion);
            m_Checkpoint = new FPCombatCheckpoint(StateIdentity, combat);
        }

        public SimulationPipelineStateParticipantIdentity StateIdentity { get; }
        public SimulationPipelineStepProjectionMode StepProjectionMode => SimulationPipelineStepProjectionMode.Include;

        public void PrepareStepStorage(int snapshotCapacity)
        {
            m_StepStorage = new SimulationPipelinePassStateStorage(StateIdentity, snapshotCapacity, m_Combat);
            m_Restore = new FPCombatRestore(StateIdentity, m_Combat);
        }

        public SimulationPipelinePassStateSnapshot CaptureStepState() => m_StepStorage.Capture();

        public ISimulationPipelinePassStateCheckpoint CaptureCheckpoint()
        {
            m_Checkpoint.Capture();
            return m_Checkpoint;
        }

        public ISimulationPipelineFrameState CreateFrameState() => new FPCombatCheckpoint(StateIdentity, m_Combat);
        public SimulationPipelinePassStateSnapshot CaptureState() => CaptureStepState();

        public ISimulationPipelinePassRestoreTransaction PrepareRestore(SimulationPipelinePassStateSnapshot snapshot)
        {
            m_Restore.Prepare(snapshot);
            return m_Restore;
        }
    }

    internal sealed class FPCombatCheckpoint : ISimulationPipelineFrameState
    {
        readonly FPCombatRuntime m_Combat;
        readonly IValueSnapshotCopy m_Before;

        public FPCombatCheckpoint(SimulationPipelineStateParticipantIdentity identity, FPCombatRuntime combat)
        {
            Participant = identity;
            m_Combat = combat;
            m_Before = combat.CreateValueSnapshot();
            m_Before.Release();
        }

        public SimulationPipelineStateParticipantIdentity Participant { get; }
        public IValueSnapshotCopy State => m_Before;
        public void Capture() => m_Combat.CaptureValueSnapshot(m_Before);
        public void Restore() => m_Combat.RestoreValueSnapshot(m_Before);

        public void CopyFrom(ISimulationPipelineFrameState source) =>
            m_Before.CopyFrom(((FPCombatCheckpoint)source).m_Before);

        public void Dispose() => m_Before.Release();
    }

    internal sealed class FPCombatRestore : ISimulationPipelinePassRestoreTransaction
    {
        readonly FPCombatRuntime m_Combat;
        readonly IValueResources m_Before;
        readonly IValueResources m_Current;
        IValueResources m_Replacement;

        public FPCombatRestore(SimulationPipelineStateParticipantIdentity identity, FPCombatRuntime combat)
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
