using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public interface IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext> : IOrchestrationTimelineClipExitDispatcher<TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
    {
        OrchestrationOperationResult Tick<TDiagnostics>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs,
            ulong tick, ref TContext context) where TDiagnostics : struct, IOrchestrationExecutionDiagnostics;

        OrchestrationStopStatus Stop<TDiagnostics>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext,
            bool force, ulong tick, ref TContext context) where TDiagnostics : struct, IOrchestrationExecutionDiagnostics;

        TValue ReadTime();

        int ReadSection(OrchestrationStateView candidate, int operationIndex,
            ulong playbackIdentity, in TContext context);

        void ReleasePlayback(OrchestrationStateView candidate, int operationIndex,
            ref ulong playbackIdentity, in TContext context);

        int EnterInvocation(int playbackSlot, int invocationIndex, ref TContext context);

        OrchestrationTimelineInvocationFacts ReadInvocationFacts();

        void SelectExecutionFrame(int frameSlot, ref TContext context);

        OrchestrationOperationResult ReleaseInvocation(ref TContext context);

        void LeaveInvocation(ref TContext context);
    }
}
