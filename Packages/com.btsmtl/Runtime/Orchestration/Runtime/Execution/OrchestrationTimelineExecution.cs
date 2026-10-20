using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
        where TDiagnostics : struct, IOrchestrationExecutionDiagnostics
        where TStorage : struct, IOrchestrationValueStorage<TValue>
    {
        OrchestrationOperationResult TickTimeline(int operationIndex, int inputDepth, int inputCount) =>
            m_TimelineDispatcher.Tick(ref this, m_TimelineState, operationIndex,
                m_Program.Payload<OrchestrationTimelinePayload>(operationIndex).ContentIdentityId, ReadGeneration(operationIndex),
                ref OperationState(operationIndex).TimelineRuntimeHandle,
                m_Workspace.Inputs[inputDepth].AsSpan(0, inputCount), m_Input.Tick,
                ref m_Context);

        OrchestrationStopStatus StopTimeline(int operationIndex,
            OrchestrationStopContext context, bool force) =>
            m_TimelineDispatcher.Stop(ref this, m_TimelineState, operationIndex,
                ref OperationState(operationIndex).TimelineRuntimeHandle, in context,
                force, m_Input.Tick, ref m_Context);

        public OrchestrationOperationResult AdvanceTimeline(
            in OrchestrationTimelineInvocationFacts invocation, in TValue time,
            in OrchestrationStopContext stopContext)
        {
            OrchestrationTimelineInvocationFacts previous = m_TimelineInvocation;
            TValue previousTime = m_TimelineTime;
            m_TimelineInvocation = invocation;
            m_TimelineTime = time;
            BeginInvocation();
            try
            {
                OrchestrationOperationResult result = Advance(invocation.Invocation.Entry);
                if (invocation.EndsExecutionFrame)
                {
                    CloseExecutionFrame(in stopContext);
                    ClearExecutionFrame(m_FrameSlot);
                }
                return result;
            }
            finally
            {
                m_TimelineInvocation = previous;
                m_TimelineTime = previousTime;
                m_EntryDepth--;
            }
        }

        OrchestrationOperationResult RequestTimelineClipExit(int operationIndex) =>
            m_ClipExitDispatcher.RequestClipExit(m_TimelineState, operationIndex,
                m_Input.Tick, ref m_Context);

        public void ExecuteTimelineBatch<TTime>(
            OrchestrationTimelineBatch<TTime> batch,
            in OrchestrationTimelineStart<OrchestrationTime> start,
            in OrchestrationStopContext stopContext) where TTime : unmanaged
        {
            BeginInvocation();
            try
            {
                ReadOnlySpan<OrchestrationTimelineInvocation<TTime>> invocations = batch.Invocations;
                for (int i = 0; i < invocations.Length; i++)
                {
                    ref readonly OrchestrationTimelineInvocation<TTime> invocation = ref invocations[i];
                    OrchestrationOperationResult result;
                    OrchestrationTimelineInvocationFacts previousInvocation = m_TimelineInvocation;
                    TValue previousTime = m_TimelineTime;
                    int previousFrame = m_FrameSlot;
                    int previousOperation = m_CurrentOperationIndex;
                    int frameSlot;
                    bool releaseFrame = false;
                    try
                    {
                        frameSlot = m_TimelineDispatcher.EnterInvocation(batch.PlaybackSlot, i, ref m_Context);
                        OrchestrationTimelineInvocationFacts facts = m_TimelineDispatcher.ReadInvocationFacts();
                        TValue time = m_TimelineDispatcher.ReadTime();
                        m_TimelineInvocation = facts;
                        m_TimelineTime = time;
                        SelectExecutionFrame(frameSlot);
                        result = Advance(invocation.Entry);
                        if (facts.EndsExecutionFrame)
                        {
                            if (!invocation.Token.Marker &&
                                m_TimelineDispatcher.ReleaseInvocation(ref m_Context) != OrchestrationOperationResult.Success)
                                result = OrchestrationOperationResult.Failure;
                            CloseExecutionFrame(in stopContext);
                            releaseFrame = true;
                        }
                    }
                    finally
                    {
                        try
                        {
                            m_TimelineDispatcher.LeaveInvocation(ref m_Context);
                        }
                        finally
                        {
                            SelectExecutionFrame(previousFrame);
                            m_CurrentOperationIndex = previousOperation;
                            m_TimelineInvocation = previousInvocation;
                            m_TimelineTime = previousTime;
                        }
                    }
                    if (releaseFrame)
                        ClearExecutionFrame(frameSlot);
                    if (result == OrchestrationOperationResult.Failure)
                        throw new InvalidOperationException($"Timeline playback '{invocation.Token.PlaybackIdentity}' clip '{invocation.Token.ClipOrdinal}' hook '{invocation.Hook}' failed.");
                }
                if (batch.Status == OrchestrationTimelinePlaybackStatus.Completed ||
                    batch.Status == OrchestrationTimelinePlaybackStatus.Stopped)
                    ReleaseTimelineFrames(in start, in stopContext);
            }
            finally
            {
                m_EntryDepth--;
            }
        }

        void ReleaseTimelineFrames(in OrchestrationTimelineStart<OrchestrationTime> start,
            in OrchestrationStopContext stopContext)
        {
            for (int frameSlot = 0; frameSlot < m_Frames.Capacity; frameSlot++)
                if (m_Frames.Header(frameSlot).SkillContentId != 0 &&
                    m_Frames.Header(frameSlot).TimelineStart.Equals(start))
                    ReleaseExecutionFrame(frameSlot, in stopContext);
        }

        void ReleaseExecutionFrame(int frameSlot, in OrchestrationStopContext stopContext)
        {
            int previousFrame = m_FrameSlot;
            int previousOperation = m_CurrentOperationIndex;
            SelectExecutionFrame(frameSlot);
            try
            {
                CloseExecutionFrame(in stopContext);
            }
            finally
            {
                SelectExecutionFrame(previousFrame);
                m_CurrentOperationIndex = previousOperation;
            }
            ClearExecutionFrame(frameSlot);
        }

        void CloseExecutionFrame(in OrchestrationStopContext stopContext)
        {
            int ownerOperationIndex = m_Frames.Header(m_FrameSlot).OwnerOperationIndex;
            for (int operationIndex = 0; operationIndex < m_Program.OperationCount; operationIndex++)
                if (m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex == ownerOperationIndex &&
                    m_Program.ExecutionStorage.OperationIndices.Span[operationIndex] !=
                        OrchestrationProgramStorageLayout.NoOperationIndex &&
                    (OperationState(operationIndex).Status == OrchestrationOperationStatus.Running ||
                     OperationState(operationIndex).Status == OrchestrationOperationStatus.Stopping ||
                     OperationState(operationIndex).TimelineRuntimeHandle != 0))
                    ForceStop(operationIndex, stopContext);
            for (int child = 0; child < m_Frames.Capacity; child++)
                if (m_Frames.Header(child).SkillContentId != 0 &&
                    m_Frames.Header(child).ParentFrameIndex == m_FrameSlot)
                    ReleaseExecutionFrame(child, in stopContext);
        }

        void ClearExecutionFrame(int frameSlot)
        {
            default(TDiagnostics).Lifecycle(ref this, OrchestrationDiagnosticLifecycle.RetireFrame, -1, frameSlot);
            m_Frames.RemoveAt(frameSlot);
            Array.Clear(m_Workspace.ForceStopVisited,
                checked(frameSlot * m_Program.OperationCount), m_Program.OperationCount);
        }
    }
}
