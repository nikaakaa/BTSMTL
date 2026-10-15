using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
        where TContext : struct
    {
        OrchestrationOperationResult TickTimeline(int operationIndex, int inputDepth, int inputCount) =>
            m_TimelineDispatcher.Tick(ref this, m_TimelineState, operationIndex,
                m_Program.Operation(operationIndex).TimelineContentIdentityId, ReadGeneration(operationIndex),
                ref m_Operations[operationIndex].TimelineRuntimeHandle,
                m_Workspace.Inputs[inputDepth].AsSpan(0, inputCount), m_Input.Tick,
                ref m_Context);

        OrchestrationStopStatus StopTimeline(int operationIndex,
            OrchestrationStopContext context, bool force) =>
            m_TimelineDispatcher.Stop(ref this, m_TimelineState, operationIndex,
                ref m_Operations[operationIndex].TimelineRuntimeHandle, in context,
                force, m_Input.Tick, ref m_Context);

        TValue ReadTimelineTime() => m_TimelineDispatcher.ReadTime();

        OrchestrationOperationResult RequestTimelineClipExit(int operationIndex) =>
            m_ClipExitDispatcher.RequestClipExit(m_TimelineState, operationIndex,
                m_Input.Tick, ref m_Context);

        public void ExecuteTimelineBatch<TTime>(
            OrchestrationTimelineBatch<TTime> batch) where TTime : unmanaged
        {
            ReadOnlySpan<OrchestrationTimelineInvocation<TTime>> invocations = batch.Invocations;
            for (int i = 0; i < invocations.Length; i++)
            {
                ref readonly OrchestrationTimelineInvocation<TTime> invocation = ref invocations[i];
                OrchestrationOperationResult result;
                OrchestrationTimelineInvocationToken previousToken = m_OutputTimelineToken;
                OrchestrationTimelineHook previousHook = m_OutputTimelineHook;
                bool hadTimelineInvocation = m_HasOutputTimelineInvocation;
                try
                {
                    m_OutputTimelineToken = invocation.Token;
                    m_OutputTimelineHook = invocation.Hook;
                    m_HasOutputTimelineInvocation = true;
                    m_TimelineDispatcher.EnterInvocation(batch.PlaybackSlot, i, ref m_Context);
                    result = Advance(invocation.Entry);
                    if ((invocation.Hook == OrchestrationTimelineHook.Disable ||
                         invocation.Hook == OrchestrationTimelineHook.Destroy) &&
                        m_TimelineDispatcher.ReleaseInvocation(ref m_Context) != OrchestrationOperationResult.Success)
                        result = OrchestrationOperationResult.Failure;
                }
                finally
                {
                    try
                    {
                        m_TimelineDispatcher.LeaveInvocation(ref m_Context);
                    }
                    finally
                    {
                        m_OutputTimelineToken = previousToken;
                        m_OutputTimelineHook = previousHook;
                        m_HasOutputTimelineInvocation = hadTimelineInvocation;
                    }
                }
                if (result == OrchestrationOperationResult.Failure)
                    throw new InvalidOperationException($"Timeline playback '{invocation.Token.PlaybackIdentity}' clip '{invocation.Token.ClipOrdinal}' hook '{invocation.Hook}' failed.");
            }
        }
    }
}
