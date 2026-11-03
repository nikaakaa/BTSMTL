using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TNumerics, THost>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where THost : struct, IOrchestrationHost<TValue>
    {
        OrchestrationOperationResult TickLoop(int operationIndex)
        {
            ReadOnlySpan<int> children = m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child);
            if (children.Length != 1)
                return OrchestrationOperationResult.Failure;
            int edgeIndex = children[0];
            if (!EvaluateCondition(edgeIndex))
                return OrchestrationOperationResult.Failure;
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            if (state.ChildCursor == 0 || !IsActive(m_Program.Edge(edgeIndex).TargetOperationIndex))
            {
                state.ChildCursor = checked(state.ChildCursor + 1);
                TraceOperation(OrchestrationTraceKind.LoopIterationEnter, operationIndex);
            }
            OrchestrationOperationResult child;
            int loopFrame = m_FrameSlot;
            m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.EnterLoopIteration, operationIndex, loopFrame);
            try
            {
                child = TickEdge(edgeIndex);
            }
            finally
            {
                m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.LeaveLoopIteration, operationIndex, loopFrame);
            }
            if (child != OrchestrationOperationResult.Running)
                TraceOperation(OrchestrationTraceKind.LoopIterationComplete, operationIndex, child);
            int mode = m_Program.Payload<OrchestrationModePayload>(operationIndex).Mode;
            if (mode == 1 && child == OrchestrationOperationResult.Success)
                return OrchestrationOperationResult.Success;
            if (mode == 2 && child == OrchestrationOperationResult.Failure)
                return OrchestrationOperationResult.Failure;
            return Wait(operationIndex, child == OrchestrationOperationResult.Running
                ? OrchestrationWaitReason.ChildCompletion : OrchestrationWaitReason.NextIteration);
        }

        OrchestrationOperationResult TickSequence(int operationIndex)
        {
            ReadOnlySpan<int> children = m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child);
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            int cursor = Math.Max(0, state.ChildCursor);
            ref readonly OrchestrationStopContext pending = ref ReadStopContext(operationIndex);
            if (pending.IsValid && cursor < children.Length)
            {
                OrchestrationStopStatus stop = RequestStop(m_Program.Edge(children[cursor]).TargetOperationIndex, in pending);
                if (stop == OrchestrationStopStatus.Running)
                    return Wait(operationIndex, OrchestrationWaitReason.ChildStop);
                ClearStopContext(operationIndex);
                return OrchestrationOperationResult.Failure;
            }
            while (cursor < children.Length)
            {
                int edgeIndex = children[cursor];
                ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(edgeIndex);
                if (!EvaluateCondition(edgeIndex))
                {
                    if (UsesSelfAbort(edge.AbortPolicy) && IsActive(edge.TargetOperationIndex))
                    {
                        OrchestrationStopContext context = OrchestrationStopContext.SelfAbort(operationIndex, m_FrameSlot, m_Input.Tick);
                        WriteStopContext(operationIndex, context);
                        OrchestrationStopStatus stop = RequestStop(edge.TargetOperationIndex, context);
                        if (stop == OrchestrationStopStatus.Running)
                            return Wait(operationIndex, OrchestrationWaitReason.ChildStop);
                        ClearStopContext(operationIndex);
                        if (stop == OrchestrationStopStatus.Failed)
                            return OrchestrationOperationResult.Failure;
                    }
                    return OrchestrationOperationResult.Failure;
                }
                OrchestrationOperationResult result = TickEdge(edgeIndex);
                if (result == OrchestrationOperationResult.Running)
                {
                    state.ChildCursor = cursor;
                    return Wait(operationIndex, OrchestrationWaitReason.ChildCompletion);
                }
                if (result == OrchestrationOperationResult.Failure)
                    return result;
                state.ChildCursor = ++cursor;
            }
            return OrchestrationOperationResult.Success;
        }

        OrchestrationOperationResult TickSelector(int operationIndex)
        {
            ReadOnlySpan<int> children = m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child);
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            int cursor = state.ChildCursor;
            ref readonly OrchestrationStopContext pending = ref ReadStopContext(operationIndex);
            if (pending.IsValid && cursor >= 0 && cursor < children.Length)
            {
                OrchestrationStopStatus stop = RequestStop(m_Program.Edge(children[cursor]).TargetOperationIndex, in pending);
                if (stop == OrchestrationStopStatus.Running)
                    return Wait(operationIndex, pending.Cause == (uint)OrchestrationStopCause.LowerPriorityAbort
                        ? OrchestrationWaitReason.PriorityReplacement : OrchestrationWaitReason.ChildStop);
                ClearStopContext(operationIndex);
                state.ChildCursor = -1;
                if (stop == OrchestrationStopStatus.Failed)
                    return OrchestrationOperationResult.Failure;
                return TickSelectorFrom(operationIndex, children, 0);
            }
            if (cursor >= 0 && cursor < children.Length)
            {
                int currentIndex = children[cursor];
                ref readonly OrchestrationFlowEdge current = ref m_Program.Edge(currentIndex);
                if (IsRunning(current.TargetOperationIndex))
                {
                    ref readonly OrchestrationValueScheduleEntry abortEntry = ref m_SelectorAbortEntries[
                        ScheduleStateContext() * m_Program.EdgeCount + currentIndex];
                    ScheduledSelection selection = RunValueSchedule(in abortEntry);
                    if (selection.Value >= 0)
                    {
                        bool self = selection.Reason == OrchestrationValueSelectionReason.SelfAbort;
                        OrchestrationStopContext context = self
                            ? OrchestrationStopContext.SelfAbort(operationIndex, m_FrameSlot, m_Input.Tick)
                            : OrchestrationStopContext.LowerPriorityAbort(operationIndex, m_FrameSlot, m_Input.Tick,
                                m_Program.Edge(children[selection.Value]).TargetOperationIndex, children[selection.Value]);
                        WriteStopContext(operationIndex, context);
                        OrchestrationStopStatus stop = RequestStop(current.TargetOperationIndex, context);
                        if (stop == OrchestrationStopStatus.Running)
                            return Wait(operationIndex, self ? OrchestrationWaitReason.ChildStop : OrchestrationWaitReason.PriorityReplacement);
                        ClearStopContext(operationIndex);
                        state.ChildCursor = -1;
                        if (stop == OrchestrationStopStatus.Failed)
                            return OrchestrationOperationResult.Failure;
                        return TickSelectorFrom(operationIndex, children, 0);
                    }
                    OrchestrationOperationResult currentResult = TickEdge(currentIndex);
                    if (currentResult != OrchestrationOperationResult.Failure)
                        return currentResult == OrchestrationOperationResult.Running
                            ? Wait(operationIndex, OrchestrationWaitReason.ChildCompletion) : currentResult;
                    cursor++;
                }
            }
            return TickSelectorFrom(operationIndex, children, cursor < 0 ? 0 : cursor);
        }

        OrchestrationOperationResult TickParallel(int operationIndex)
        {
            ReadOnlySpan<int> children = m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child);
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            int completedMask = state.ChildCursor;
            int mode = m_Program.Payload<OrchestrationModePayload>(operationIndex).Mode;
            bool completesWithFirstChild = mode == 2;
            bool ticksCompletedChildren = mode == 1;
            bool running = false;
            bool stopping = false;
            for (int i = 0; i < children.Length; i++)
            {
                int edgeIndex = children[i];
                ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(edgeIndex);
                if (IsStopping(edge.TargetOperationIndex))
                {
                    OrchestrationStopStatus stop = ContinueStop(edge.TargetOperationIndex);
                    if (stop == OrchestrationStopStatus.Failed)
                        return OrchestrationOperationResult.Failure;
                    if (stop == OrchestrationStopStatus.Running)
                    {
                        running = true;
                        stopping = true;
                        continue;
                    }
                    completedMask &= ~(1 << i);
                }
                if (completesWithFirstChild && (completedMask & 1) != 0)
                {
                    if (i == 0)
                        continue;
                    OrchestrationStopStatus stop = IsActive(edge.TargetOperationIndex)
                        ? RequestStop(edge.TargetOperationIndex, OrchestrationStopContext.ParentStop(operationIndex, m_FrameSlot, m_Input.Tick))
                        : OrchestrationStopStatus.Completed;
                    if (stop == OrchestrationStopStatus.Failed)
                        return OrchestrationOperationResult.Failure;
                    if (stop == OrchestrationStopStatus.Running)
                    {
                        running = true;
                        stopping = true;
                    }
                    continue;
                }
                if (!EvaluateCondition(edgeIndex))
                {
                    if (UsesSelfAbort(edge.AbortPolicy) && IsActive(edge.TargetOperationIndex))
                    {
                        OrchestrationStopStatus stop = RequestStop(edge.TargetOperationIndex,
                            OrchestrationStopContext.SelfAbort(operationIndex, m_FrameSlot, m_Input.Tick));
                        if (stop == OrchestrationStopStatus.Failed)
                            return OrchestrationOperationResult.Failure;
                        if (stop == OrchestrationStopStatus.Running)
                        {
                            running = true;
                            stopping = true;
                        }
                    }
                    completedMask &= ~(1 << i);
                    continue;
                }
                if (!ticksCompletedChildren && (completedMask & (1 << i)) != 0)
                    continue;
                OrchestrationOperationResult result = TickEdge(edgeIndex);
                if (result == OrchestrationOperationResult.Running)
                    running = true;
                else if (!ticksCompletedChildren)
                {
                    completedMask |= 1 << i;
                    if (completesWithFirstChild && i == 0 && result == OrchestrationOperationResult.Failure)
                        completedMask |= int.MinValue;
                }
            }
            state.ChildCursor = completedMask;
            if (running)
                return Wait(operationIndex, stopping
                    ? OrchestrationWaitReason.ParallelStop : OrchestrationWaitReason.ParallelCompletion);
            return completesWithFirstChild && completedMask < 0
                ? OrchestrationOperationResult.Failure : OrchestrationOperationResult.Success;
        }

        OrchestrationOperationResult TickSelectorFrom(int operationIndex, ReadOnlySpan<int> children, int start)
        {
            for (int i = start; i < children.Length; i++)
            {
                ref readonly OrchestrationValueScheduleEntry entry = ref m_ChildSelectionEntries[
                    ScheduleStateContext() * m_Program.EdgeCount + children[i]];
                i = RunValueSchedule(in entry).Value;
                if (i < 0)
                    break;
                int edgeIndex = children[i];
                OrchestrationOperationResult result = TickEdge(edgeIndex);
                if (result == OrchestrationOperationResult.Failure)
                    continue;
                OperationState(operationIndex).ChildCursor = i;
                return result == OrchestrationOperationResult.Running
                    ? Wait(operationIndex, OrchestrationWaitReason.ChildCompletion) : result;
            }
            OperationState(operationIndex).ChildCursor = -1;
            return OrchestrationOperationResult.Failure;
        }

        static bool UsesSelfAbort(OrchestrationAbortPolicy policy) =>
            policy == OrchestrationAbortPolicy.Self || policy == OrchestrationAbortPolicy.Both;

    }
}
