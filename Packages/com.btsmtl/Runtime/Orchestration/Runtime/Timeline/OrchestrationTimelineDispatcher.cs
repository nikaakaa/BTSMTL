using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public interface IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2,
        TVector3, TQuaternion, TMath, TValues, TContext> : IOrchestrationTimelineClipExitDispatcher<TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
        where TContext : struct
    {
        OrchestrationOperationResult Tick(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TQuaternion, TMath, TValues, TContext> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context);

        OrchestrationStopStatus Stop(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TQuaternion, TMath, TValues, TContext> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext,
            bool force, ulong tick, ref TContext context);

        TValue ReadTime();

        void EnterInvocation(int playbackSlot, int invocationIndex, ref TContext context);

        OrchestrationOperationResult ReleaseInvocation(ref TContext context);

        void LeaveInvocation(ref TContext context);
    }
}
