using System;

namespace BTSMTL.Orchestration
{
    public interface IOrchestrationTimelineDispatcher<TValue, TContext> : IOrchestrationTimelineClipExitDispatcher<TContext>
        where TValue : unmanaged
        where TContext : struct
    {
        OrchestrationOperationResult Tick<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        OrchestrationStopStatus Stop<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            ReadOnlySpan<OrchestrationStopContext> stopContext,
            ref TContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        OrchestrationTime ReadTime();

        void ReleasePlayback(OrchestrationStateView candidate, int operationIndex,
            ref ulong playbackIdentity, in TContext context);

        void SelectExecutionFrame(int frameSlot, ref TContext context);
        void ReleaseExecutionFrame(int frameSlot, ReadOnlySpan<OrchestrationStopContext> stopContext, ref TContext context);
    }
}
