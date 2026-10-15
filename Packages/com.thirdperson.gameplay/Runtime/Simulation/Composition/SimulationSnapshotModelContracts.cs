namespace ThirdPersonSimulation
{
    public interface ISimulationSnapshotState : IValueResources
    {
        ulong LastCompletedTick { get; }
    }

    public interface ISimulationWorldSnapshot<TState> where TState : ISimulationSnapshotState
    {
        TState State { get; }
        void SetPrepared(TState state);
        void RetainStateStorage();
        void ReleaseStateStorage();
    }

    public interface ISimulationSessionSnapshot<TWorld> : IValueResources
    {
        SimulationSessionCompositionIdentity CompositionIdentity { get; }
        SimulationTick Tick { get; }
        void SetPrepared(TWorld world, SimulationPipelineStateSnapshot pipeline);
    }
}
