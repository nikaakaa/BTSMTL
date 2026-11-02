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
            in OrchestrationStopContext context) =>
            m_Host.Stop(ref this, m_TimelineState, operationIndex,
                ref OperationState(operationIndex).TimelineRuntimeHandle, in context);

        public OrchestrationOperationResult AdvanceTimeline(
            in OrchestrationTimelineInvocationFacts invocation, in TValue time,
            in OrchestrationStopContext stopContext, bool captureContext,
            out OrchestrationTraceContext context)
        {
            context = default;
            OrchestrationTimelineInvocationFacts previous = m_TimelineInvocation;
            TValue previousTime = m_TimelineTime;
            ReadOnlySpan<OrchestrationStopContext> previousStopContext = m_InvocationStopContext;
            m_TimelineInvocation = invocation;
            m_TimelineTime = time;
            m_InvocationStopContext = OrchestrationStopContext.Borrow(in stopContext);
            BeginInvocation();
            try
            {
                bool exits = invocation.Invocation.Hook == OrchestrationTimelineHook.Disable ||
                    invocation.Invocation.Hook == OrchestrationTimelineHook.Destroy;
                OrchestrationExecutionEntry entry = invocation.Invocation.Entry;
                if (exits)
                {
                    CloseExecutionFrame(in stopContext);
                    entry = new OrchestrationExecutionEntry(entry.OperationIndex,
                        OrchestrationExecutionContract.Complete, entry.InputPortId);
                }
                OrchestrationOperationResult result = Advance(entry);
                if (exits && result == OrchestrationOperationResult.Failure)
                    Fail(OrchestrationExecutionErrorCode.TimelineInvocation, entry.OperationIndex,
                        invocation.Invocation.Token.ClipOrdinal, (int)invocation.Invocation.Hook,
                        identity: invocation.Invocation.Token.PlaybackIdentity);
                if (captureContext)
                {
                    m_Host.ReadEntryContext(ref this, in entry, out context);
                }
                return result;
            }
            finally
            {
                m_TimelineInvocation = previous;
                m_TimelineTime = previousTime;
                m_InvocationStopContext = previousStopContext;
                m_EntryDepth--;
            }
        }

        OrchestrationOperationResult RequestTimelineClipExit(int operationIndex) =>
            m_Host.RequestClipExit(m_TimelineState, operationIndex, m_Input.Tick);

        public void ReleaseExecutionFrame(int frameSlot, in OrchestrationStopContext stopContext)
        {
            BeginInvocation();
            try
            {
                ReleaseFrame(frameSlot, in stopContext);
            }
            finally
            {
                m_EntryDepth--;
            }
        }

        public void CompleteTimelineInvocation(in OrchestrationStopContext stopContext)
        {
            m_EntryDepth++;
            try
            {
                ReleaseFrame(m_FrameSlot, in stopContext);
            }
            finally
            {
                m_EntryDepth--;
            }
        }

        void ReleaseFrame(int frameSlot, in OrchestrationStopContext stopContext)
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
                {
                    if (m_Frames.Header(child).SkillContentId == m_Frames.Header(m_FrameSlot).SkillContentId)
                        ReleaseFrame(child, in stopContext);
                    else
                        m_Host.ReleaseExecutionFrame(child, in stopContext);
                }
        }

        void ClearExecutionFrame(int frameSlot)
        {
            m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.RetireFrame, -1, frameSlot);
            m_Frames.RemoveAt(frameSlot);
            m_ForceStopVisits.ClearFrame(frameSlot);
        }
    }
}
