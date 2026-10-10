using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public interface ICharacterWorldSolver<TDescriptor, TBodyState, TWorldState, TBatchRequest, TBatchResult, TDiagnostics> : IDisposable
    {
        TDescriptor Descriptor { get; }
        int MaximumStatePayloadBytes { get; }
        void RequireBodyBinding(ActorId actorId, string bindingId);
        TWorldState Create(WorldRevision worldRevision, IReadOnlyList<TBodyState> orderedInitialBodies);
        void Reconstruct(TWorldState state);
        TWorldState Capture(WorldRevision worldRevision);
        void Restore(TWorldState state);
        TBatchResult ResolveBatch(TBatchRequest request, TDiagnostics diagnostics);
    }
}
