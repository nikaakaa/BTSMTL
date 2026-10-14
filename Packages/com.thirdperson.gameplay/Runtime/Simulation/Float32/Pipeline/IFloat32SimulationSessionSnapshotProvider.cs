namespace ThirdPersonSimulation
{
    public interface IFloat32SimulationSessionSnapshotProvider
    {
        SimulationComponentIdentity Identity { get; }
        SimulationWorldStateSet ReadWorldSnapshot(SimulationWorldSnapshot snapshot);
        void PrepareCheckpointStorage(SimulationSessionCompositionDescriptor descriptor, Float32CharacterRuntime runtime,
            System.Collections.Generic.IReadOnlyList<ISimulationPipelineStateParticipant> participants,
            in GameplayEffectSessionCapacity capacity, int stepSnapshotCapacity);
        SimulationWorldSnapshot CaptureStepWorldSnapshot(in SimulationWorldStateSet state);
        SimulationSessionCheckpoint CaptureCheckpoint(in SimulationWorldStateSet state,
            SimulationPipelinePassStateSnapshot[] recordedParticipants = null);
        Float32SimulationSessionSnapshot ReadCheckpoint(SimulationSessionCheckpoint checkpoint);
        void DisposeCheckpointStorage();
        void RequireRestore(
            SimulationSessionCompositionDescriptor descriptor,
            SimulationRestoreDirective directive,
            Float32SimulationSessionSnapshot snapshot);
    }
}
