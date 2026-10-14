using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public interface IFPSimulationSessionSnapshotProvider
    {
        SimulationComponentIdentity Identity { get; }
        SimulationWorldStateSet ReadWorldSnapshot(SimulationWorldSnapshot snapshot);
        void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, FPCharacterRuntime runtime,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in GameplayEffectSessionCapacity capacity, int stepSnapshotCapacity);
        SimulationSessionCheckpoint CaptureCheckpoint(in SimulationWorldStateSet state,
            SimulationPipelinePassStateSnapshot[] recordedParticipants = null);
        FPSimulationSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint);
        void DisposeCheckpointStorage();
        FPSimulationSessionSnapshot CaptureStepSnapshot(in SimulationWorldStateSet state);
        void RequireRestore(
            SimulationSessionCompositionDescriptor descriptor,
            SimulationRestoreDirective directive,
            FPSimulationSessionSnapshot snapshot);
    }
}
