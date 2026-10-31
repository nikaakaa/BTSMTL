using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TNumerics, THost>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where THost : struct, IOrchestrationHost<TValue>
    {
        OrchestrationOperationResult TickTimeline(int operationIndex, int inputDepth, int inputCount) =>
            m_Host.Tick(ref this, m_TimelineState, operationIndex,
                m_Program.Payload<OrchestrationTimelinePayload>(operationIndex).ContentIdentityId, ReadGeneration(operationIndex),
                ref OperationState(operationIndex).TimelineRuntimeHandle,
                InputRow(inputDepth).Slice(0, inputCount), m_Input.Tick);

        OrchestrationStopStatus StopTimeline(int operationIndex,
            OrchestrationStopContext context, bool force) =>
            m_Host.Stop(ref this, m_TimelineState, operationIndex,
                ref OperationState(operationIndex).TimelineRuntimeHandle, in context,
                force, m_Input.Tick);

        public OrchestrationOperationResult AdvanceTimeline(
            in OrchestrationTimelineInvocationFacts invocation, in TValue time,
            in OrchestrationStopContext stopContext, bool captureContext,
            out OrchestrationTraceContext context)
        {
            context = default;
            OrchestrationTimelineInvocationFacts previous = m_TimelineInvocation;
            TValue previousTime = m_TimelineTime;
            m_TimelineInvocation = invocation;
            m_TimelineTime = time;
            BeginInvocation();
            try
            {
                OrchestrationOperationResult result = Advance(invocation.Invocation.Entry);
                if (captureContext)
                {
                    OrchestrationExecutionEntry entry = invocation.Invocation.Entry;
                    m_Host.ReadEntryContext(ref this, in entry, out context);
                }
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
            m_Host.RequestClipExit(m_TimelineState, operationIndex, m_Input.Tick);

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
                        frameSlot = m_Host.EnterInvocation(batch.PlaybackSlot, i);
                        OrchestrationTimelineInvocationFacts facts = m_Host.ReadInvocationFacts();
                        TValue time = default(TNumerics).FromTime(m_Host.ReadTime());
                        m_TimelineInvocation = facts;
                        m_TimelineTime = time;
                        SelectExecutionFrame(frameSlot);
                        result = Advance(invocation.Entry);
                        if (facts.EndsExecutionFrame)
                        {
                            if (!invocation.Token.Marker &&
                                m_Host.ReleaseInvocation() != OrchestrationOperationResult.Success)
                                result = OrchestrationOperationResult.Failure;
                            CloseExecutionFrame(in stopContext);
                            releaseFrame = true;
                        }
                    }
                    finally
                    {
                        try
                        {
                            m_Host.LeaveInvocation();
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
                        Fail(OrchestrationExecutionErrorCode.TimelineInvocation, invocation.Entry.OperationIndex,
                            invocation.Token.ClipOrdinal, (int)invocation.Hook, identity: invocation.Token.PlaybackIdentity);
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
                    m_OperationIndices[operationIndex] != OrchestrationProgramStorageLayout.NoOperationIndex &&
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
            m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.RetireFrame, -1, frameSlot);
            m_Frames.RemoveAt(frameSlot);
            m_ForceStopVisits.ClearFrame(frameSlot);
        }
    }
}
