using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    [Flags]
    public enum WorldCollisionSummary : uint
    {
        None = 0,
        Sides = 1,
        Above = 2,
        Below = 4
    }

    public enum WorldStatePersistenceMode : byte
    {
        Reconstruct = 1,
        Snapshot = 2,
        FrameOwned = 3
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct WorldSimulationStateIdentity
    {
        public WorldSimulationStateIdentity(SimulationNumericProfile numericProfile,
            SolverImplementationId solverId, string solverVersion, WorldRevision worldRevision,
            WorldStatePersistenceMode persistenceMode)
        {
            NumericProfile = numericProfile;
            SolverId = solverId;
            SolverVersion = solverVersion;
            WorldRevision = worldRevision;
            PersistenceMode = persistenceMode;
        }

        [CanonicalBound]
        public SimulationNumericProfile NumericProfile { get; }
        [CanonicalBound]
        public SolverImplementationId SolverId { get; }
        [CanonicalBound, CanonicalTextRules(Required = true)]
        public string SolverVersion { get; }
        [CanonicalBound]
        public WorldRevision WorldRevision { get; }
        public WorldStatePersistenceMode PersistenceMode { get; }
    }

    public interface ICharacterWorldBatchSolver<TDescriptor, TBatchRequest, TBatchResult, TDiagnostics> : IDisposable
    {
        TDescriptor Descriptor { get; }
        void RequireBodyBinding(ActorId actorId, string bindingId);
        TBatchResult ResolveBatch(TBatchRequest request, TDiagnostics diagnostics);
    }

    public interface ICharacterWorldSolver<TDescriptor, TBodyState, TWorldState, TBatchRequest, TBatchResult, TDiagnostics> :
        ICharacterWorldBatchSolver<TDescriptor, TBatchRequest, TBatchResult, TDiagnostics>
    {
        int MaximumStatePayloadBytes { get; }
        TWorldState Create(WorldRevision worldRevision, IReadOnlyList<TBodyState> orderedInitialBodies);
        void Reconstruct(TWorldState state);
        TWorldState Capture(WorldRevision worldRevision);
        void Restore(TWorldState state);
    }
}
