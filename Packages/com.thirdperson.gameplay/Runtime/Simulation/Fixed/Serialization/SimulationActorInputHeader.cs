using System;

namespace ThirdPersonSimulation.DeterministicRollback
{
    public enum RollbackInputProvenance : byte
    {
        LocalExplicit = 1,
        RelayedExplicit = 2,
        PredictedContinuous = 3,
        PredictedNeutral = 4,
        CanonicalExplicit = 5,
        ConfirmedExplicit = 6
    }

    [CanonicalContract(MaterializedRead = true)]
    public readonly partial struct RollbackActorInputHeader
    {
        public RollbackActorInputHeader(ActorId actorId, SimulationTick tick)
        {
            ActorId = actorId;
            Tick = tick;
            RequireIdentity();
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }

        [CanonicalValidation]
        void RequireIdentity()
        {
            if (!ActorId.IsValid || !Tick.IsValid)
                throw new ArgumentException("Rollback Actor input identity is incomplete.");
        }
    }
}
