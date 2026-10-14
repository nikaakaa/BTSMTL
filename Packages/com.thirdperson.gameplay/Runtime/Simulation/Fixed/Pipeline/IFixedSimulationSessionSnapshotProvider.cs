using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public interface IFixedSimulationSessionSnapshotProvider
    {
        SimulationComponentIdentity Identity { get; }
        SimulationWorldStateSet ReadWorldSnapshot(SimulationWorldSnapshot snapshot);
        void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, FixedCharacterRuntime runtime,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in GameplayEffectSessionCapacity capacity, int stepSnapshotCapacity);
        SimulationSessionCheckpoint CaptureCheckpoint(in SimulationWorldStateSet state,
            SimulationPipelinePassStateSnapshot[] recordedParticipants = null);
        FixedSimulationSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint);
        void DisposeCheckpointStorage();
        FixedSimulationSessionSnapshot CaptureStepSnapshot(in SimulationWorldStateSet state);
        void RequireRestore(
            SimulationSessionCompositionDescriptor descriptor,
            SimulationRestoreDirective directive,
            FixedSimulationSessionSnapshot snapshot);
    }
}
