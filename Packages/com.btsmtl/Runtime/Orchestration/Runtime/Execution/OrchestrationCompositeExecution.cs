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
        OrchestrationOperationResult TickLoop(int operationIndex)
        {
            ReadOnlySpan<int> children = m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child);
            if (children.Length != 1)
                return OrchestrationOperationResult.Failure;
            int edgeIndex = children[0];
            if (!EvaluateCondition(edgeIndex))
                return OrchestrationOperationResult.Failure;
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            if (state.ChildCursor == 0 || !IsActive(m_Program.Edge(edgeIndex).TargetOperationIndex))
            {
                state.ChildCursor = checked(state.ChildCursor + 1);
                TraceOperation(OrchestrationTraceKind.LoopIterationEnter, operationIndex);
            }
            OrchestrationOperationResult child = TickEdge(edgeIndex);
            if (child != OrchestrationOperationResult.Running)
                TraceOperation(OrchestrationTraceKind.LoopIterationComplete, operationIndex, child);
            int mode = m_Program.Operation(operationIndex).Integer0;
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
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            int cursor = Math.Max(0, state.ChildCursor);
            OrchestrationStopContext pending = ReadStopContext(operationIndex);
            if (pending.IsValid && cursor < children.Length)
            {
                OrchestrationStopStatus stop = RequestStop(m_Program.Edge(children[cursor]).TargetOperationIndex, pending);
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
                        OrchestrationStopContext context = OrchestrationStopContext.SelfAbort(edge.TargetOperationIndex);
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
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            int cursor = state.ChildCursor;
            OrchestrationStopContext pending = ReadStopContext(operationIndex);
            if (pending.IsValid && cursor >= 0 && cursor < children.Length)
            {
                OrchestrationStopStatus stop = RequestStop(m_Program.Edge(children[cursor]).TargetOperationIndex, pending);
                if (stop == OrchestrationStopStatus.Running)
                    return Wait(operationIndex, pending.Cause == (uint)OrchestrationStopCause.LowerPriorityAbort
                        ? OrchestrationWaitReason.PriorityReplacement : OrchestrationWaitReason.ChildStop);
                ClearStopContext(operationIndex);
                state.ChildCursor = -1;
                if (stop == OrchestrationStopStatus.Failed)
                    return OrchestrationOperationResult.Failure;
                int replacement = FindChildIndex(children, pending.ReplacementOperationIndex);
                return TickSelectorFrom(operationIndex, children,
                    pending.Cause == (uint)OrchestrationStopCause.LowerPriorityAbort && replacement >= 0 ? replacement : 0);
            }
            if (cursor >= 0 && cursor < children.Length)
            {
                int currentIndex = children[cursor];
                ref readonly OrchestrationFlowEdge current = ref m_Program.Edge(currentIndex);
                if (IsRunning(current.TargetOperationIndex))
                {
                    if (UsesSelfAbort(current.AbortPolicy) && !EvaluateCondition(currentIndex))
                    {
                        OrchestrationStopContext context = OrchestrationStopContext.SelfAbort(current.TargetOperationIndex);
                        WriteStopContext(operationIndex, context);
                        OrchestrationStopStatus stop = RequestStop(current.TargetOperationIndex, context);
                        if (stop == OrchestrationStopStatus.Running)
                            return Wait(operationIndex, OrchestrationWaitReason.ChildStop);
                        ClearStopContext(operationIndex);
                        state.ChildCursor = -1;
                        if (stop == OrchestrationStopStatus.Failed)
                            return OrchestrationOperationResult.Failure;
                        return TickSelectorFrom(operationIndex, children, 0);
                    }
                    for (int i = 0; i < cursor; i++)
                    {
                        int edgeIndex = children[i];
                        ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(edgeIndex);
                        if (!UsesLowerPriorityAbort(edge.AbortPolicy) || !EvaluateCondition(edgeIndex))
                            continue;
                        OrchestrationStopContext context = OrchestrationStopContext.LowerPriorityAbort(
                            current.TargetOperationIndex, edge.TargetOperationIndex);
                        WriteStopContext(operationIndex, context);
                        OrchestrationStopStatus stop = RequestStop(current.TargetOperationIndex, context);
                        if (stop == OrchestrationStopStatus.Running)
                            return Wait(operationIndex, OrchestrationWaitReason.PriorityReplacement);
                        ClearStopContext(operationIndex);
                        state.ChildCursor = -1;
                        if (stop == OrchestrationStopStatus.Failed)
                            return OrchestrationOperationResult.Failure;
                        return TickSelectorFrom(operationIndex, children, i);
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
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            int completedMask = state.ChildCursor;
            int mode = m_Program.Operation(operationIndex).Integer0;
            bool completesWithFirstChild = mode == 2;
            bool ticksCompletedChildren = mode == 1;
            bool running = false;
            bool stopping = false;
            for (int i = 0; i < children.Length; i++)
            {
                int edgeIndex = children[i];
                ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(edgeIndex);
                if (completesWithFirstChild && (completedMask & 1) != 0)
                {
                    if (i == 0)
                        continue;
                    OrchestrationStopStatus stop = IsStopping(edge.TargetOperationIndex)
                        ? ContinueStop(edge.TargetOperationIndex)
                        : IsActive(edge.TargetOperationIndex)
                            ? RequestStop(edge.TargetOperationIndex, OrchestrationStopContext.ParentStop(operationIndex))
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
                    if (IsStopping(edge.TargetOperationIndex))
                    {
                        OrchestrationStopStatus stop = ContinueStop(edge.TargetOperationIndex);
                        if (stop == OrchestrationStopStatus.Failed)
                            return OrchestrationOperationResult.Failure;
                        if (stop == OrchestrationStopStatus.Running)
                        {
                            running = true;
                            stopping = true;
                        }
                    }
                    else if (UsesSelfAbort(edge.AbortPolicy) && IsActive(edge.TargetOperationIndex))
                    {
                        OrchestrationStopStatus stop = RequestStop(edge.TargetOperationIndex,
                            OrchestrationStopContext.SelfAbort(edge.TargetOperationIndex));
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
            for (int i = Math.Max(0, start); i < children.Length; i++)
            {
                int edgeIndex = children[i];
                if (!EvaluateCondition(edgeIndex))
                    continue;
                OrchestrationOperationResult result = TickEdge(edgeIndex);
                if (result == OrchestrationOperationResult.Failure)
                    continue;
                m_Operations[operationIndex].ChildCursor = i;
                return result == OrchestrationOperationResult.Running
                    ? Wait(operationIndex, OrchestrationWaitReason.ChildCompletion) : result;
            }
            m_Operations[operationIndex].ChildCursor = -1;
            return OrchestrationOperationResult.Failure;
        }

        int FindChildIndex(ReadOnlySpan<int> children, int operationIndex)
        {
            if (operationIndex < 0)
                return -1;
            for (int i = 0; i < children.Length; i++)
                if (m_Program.Edge(children[i]).TargetOperationIndex == operationIndex)
                    return i;
            return -1;
        }

        static bool UsesSelfAbort(OrchestrationAbortPolicy policy) =>
            policy == OrchestrationAbortPolicy.Self || policy == OrchestrationAbortPolicy.Both;

        static bool UsesLowerPriorityAbort(OrchestrationAbortPolicy policy) =>
            policy == OrchestrationAbortPolicy.LowerPriority || policy == OrchestrationAbortPolicy.Both;
    }
}
