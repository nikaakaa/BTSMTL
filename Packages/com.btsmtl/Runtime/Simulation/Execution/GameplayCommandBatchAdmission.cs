namespace ThirdPersonSimulation
{
    public enum GameplayCommandBatchAdmissionKind : byte
    {
        Accepted = 1,
        CapacityExceeded = 2
    }

    public readonly struct GameplayCommandBatchAdmission
    {
        public GameplayCommandBatchAdmission(ActorId actorId, ulong tick, long requestedEntries,
            long requestedCommands, int availableEntries, int availableCommands, SimulationExecutionSource source)
        {
            ActorId = actorId;
            Source = source;
            Tick = tick;
            RequestedEntries = requestedEntries;
            RequestedCommands = requestedCommands;
            AvailableEntries = availableEntries;
            AvailableCommands = availableCommands;
            Kind = requestedEntries <= availableEntries && requestedCommands <= availableCommands
                ? GameplayCommandBatchAdmissionKind.Accepted : GameplayCommandBatchAdmissionKind.CapacityExceeded;
        }

        public GameplayCommandBatchAdmissionKind Kind { get; }
        public ActorId ActorId { get; }
        public SimulationExecutionSource Source { get; }
        public ulong Tick { get; }
        public long RequestedEntries { get; }
        public long RequestedCommands { get; }
        public int AvailableEntries { get; }
        public int AvailableCommands { get; }
        public bool Accepted => Kind == GameplayCommandBatchAdmissionKind.Accepted;
        public GameplayEffectCapacityDimension Dimension => RequestedCommands > AvailableCommands
            ? GameplayEffectCapacityDimension.CommandsPerTick : GameplayEffectCapacityDimension.SourceIngressEntries;
        public long Required => RequestedCommands > AvailableCommands ? RequestedCommands : RequestedEntries;
        public int Available => RequestedCommands > AvailableCommands ? AvailableCommands : AvailableEntries;
    }
}
