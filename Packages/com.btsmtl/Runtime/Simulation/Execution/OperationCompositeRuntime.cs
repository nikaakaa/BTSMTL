using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    internal interface IOperationCompositeRuntimeHost<TTarget>
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        OperationExecutionTopology Topology { get; }
        int ReadInt32(int slotIndex);
        void WriteInt32(int slotIndex, int value);
        bool EvaluateCondition(ProgramControlFlowEdge edge);
        OperationExecutionResult TickEdge(ProgramControlFlowEdge edge);
        OperationExecutionResult Wait(OperationExecutionDescriptor operation, OperationWaitReason reason);
        bool IsActive(OperationHandle handle);
        bool IsRunning(OperationHandle handle);
        bool IsStopping(OperationHandle handle);
        OperationStopStatus RequestStop(OperationHandle handle, OperationStopContext context);
        OperationStopStatus ContinueStop(OperationHandle handle);
        OperationStopContext ReadStopContext(OperationExecutionDescriptor operation);
        void WriteStopContext(OperationExecutionDescriptor operation, OperationStopContext context);
        void ClearStopContext(OperationExecutionDescriptor operation);
        void EmitLoopTrace(OperationExecutionDescriptor operation, string code, string detail);
    }

    internal sealed class OperationCompositeRuntime<TTarget>
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        readonly IOperationCompositeRuntimeHost<TTarget> m_Host;

        public OperationCompositeRuntime(IOperationCompositeRuntimeHost<TTarget> host)
        {
            m_Host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public OperationExecutionResult TickLoop(OperationExecutionDescriptor operation)
        {
            IReadOnlyList<ProgramControlFlowEdge> children = Edges(operation.Handle, ProgramControlFlowKind.Child);
            if (children.Count != 1)
                return OperationExecutionResult.Failure;
            ProgramControlFlowEdge edge = children[0];
            if (!EvaluateCondition(edge))
                return OperationExecutionResult.Failure;
            int iterationSlot = RequireOperationSlot(operation, ProgramStateSemantic.RunnableChildCursor);
            int iteration = m_Host.ReadInt32(iterationSlot);
            if (iteration == 0 || !m_Host.IsActive(edge.Target))
            {
                m_Host.WriteInt32(iterationSlot, checked(iteration + 1));
                m_Host.EmitLoopTrace(operation, "loop_iteration_enter", string.Empty);
            }
            OperationExecutionResult child = m_Host.TickEdge(edge);
            if (child != OperationExecutionResult.Running)
                m_Host.EmitLoopTrace(operation, "loop_iteration_complete", OperationTraceText.Result(child));
            if (operation.Integer0 == 1 && child == OperationExecutionResult.Success)
                return OperationExecutionResult.Success;
            if (operation.Integer0 == 2 && child == OperationExecutionResult.Failure)
                return OperationExecutionResult.Failure;
            return m_Host.Wait(operation, child == OperationExecutionResult.Running ? OperationWaitReason.ChildCompletion : OperationWaitReason.NextIteration);
        }

        public OperationExecutionResult TickSequence(OperationExecutionDescriptor operation)
        {
            IReadOnlyList<ProgramControlFlowEdge> children = Edges(operation.Handle, ProgramControlFlowKind.Child);
            int slot = RequireOperationSlot(operation, ProgramStateSemantic.RunnableChildCursor);
            int cursor = Math.Max(0, m_Host.ReadInt32(slot));
            OperationStopContext pending = m_Host.ReadStopContext(operation);
            if (pending.IsValid && cursor < children.Count)
            {
                OperationStopStatus stop = m_Host.RequestStop(children[cursor].Target, pending);
                if (stop == OperationStopStatus.Running)
                    return m_Host.Wait(operation, OperationWaitReason.ChildStop);
                m_Host.ClearStopContext(operation);
                return OperationExecutionResult.Failure;
            }
            while (cursor < children.Count)
            {
                ProgramControlFlowEdge edge = children[cursor];
                if (!EvaluateCondition(edge))
                {
                    if (UsesSelfAbort(edge.AbortPolicy) && m_Host.IsActive(edge.Target))
                    {
                        OperationStopContext context = OperationStopContext.SelfAbort(edge.Target);
                        m_Host.WriteStopContext(operation, context);
                        OperationStopStatus stop = m_Host.RequestStop(edge.Target, context);
                        if (stop == OperationStopStatus.Running)
                            return m_Host.Wait(operation, OperationWaitReason.ChildStop);
                        m_Host.ClearStopContext(operation);
                        if (stop == OperationStopStatus.Failed)
                            return OperationExecutionResult.Failure;
                    }
                    return OperationExecutionResult.Failure;
                }
                OperationExecutionResult result = m_Host.TickEdge(edge);
                if (result == OperationExecutionResult.Running)
                {
                    m_Host.WriteInt32(slot, cursor);
                    return m_Host.Wait(operation, OperationWaitReason.ChildCompletion);
                }
                if (result == OperationExecutionResult.Failure)
                    return result;
                cursor++;
                m_Host.WriteInt32(slot, cursor);
            }
            return OperationExecutionResult.Success;
        }

        public OperationExecutionResult TickSelector(OperationExecutionDescriptor operation)
        {
            IReadOnlyList<ProgramControlFlowEdge> children = Edges(operation.Handle, ProgramControlFlowKind.Child);
            int slot = RequireOperationSlot(operation, ProgramStateSemantic.RunnableChildCursor);
            int cursor = m_Host.ReadInt32(slot);
            OperationStopContext pending = m_Host.ReadStopContext(operation);
            if (pending.IsValid && cursor >= 0 && cursor < children.Count)
            {
                OperationStopStatus stop = m_Host.RequestStop(children[cursor].Target, pending);
                if (stop == OperationStopStatus.Running)
                    return m_Host.Wait(operation, pending.Cause == OperationStopCause.LowerPriorityAbort ? OperationWaitReason.PriorityReplacement : OperationWaitReason.ChildStop);
                m_Host.ClearStopContext(operation);
                m_Host.WriteInt32(slot, -1);
                if (stop == OperationStopStatus.Failed)
                    return OperationExecutionResult.Failure;
                int replacement = FindChildIndex(children, pending.Replacement);
                return TickSelectorFrom(operation, children, slot,
                    pending.Cause == OperationStopCause.LowerPriorityAbort && replacement >= 0 ? replacement : 0);
            }
            if (cursor >= 0 && cursor < children.Count)
            {
                ProgramControlFlowEdge current = children[cursor];
                if (m_Host.IsRunning(current.Target))
                {
                    if (UsesSelfAbort(current.AbortPolicy) && !EvaluateCondition(current))
                    {
                        OperationStopContext context = OperationStopContext.SelfAbort(current.Target);
                        m_Host.WriteStopContext(operation, context);
                        OperationStopStatus stop = m_Host.RequestStop(current.Target, context);
                        if (stop == OperationStopStatus.Running)
                            return m_Host.Wait(operation, OperationWaitReason.ChildStop);
                        m_Host.ClearStopContext(operation);
                        m_Host.WriteInt32(slot, -1);
                        if (stop == OperationStopStatus.Failed)
                            return OperationExecutionResult.Failure;
                        return TickSelectorFrom(operation, children, slot, 0);
                    }
                    for (int i = 0; i < cursor; i++)
                    {
                        ProgramControlFlowEdge edge = children[i];
                        if (!UsesLowerPriorityAbort(edge.AbortPolicy) || !EvaluateCondition(edge))
                            continue;
                        OperationStopContext context = OperationStopContext.LowerPriorityAbort(current.Target, edge.Target);
                        m_Host.WriteStopContext(operation, context);
                        OperationStopStatus stop = m_Host.RequestStop(current.Target, context);
                        if (stop == OperationStopStatus.Running)
                            return m_Host.Wait(operation, OperationWaitReason.PriorityReplacement);
                        m_Host.ClearStopContext(operation);
                        m_Host.WriteInt32(slot, -1);
                        if (stop == OperationStopStatus.Failed)
                            return OperationExecutionResult.Failure;
                        return TickSelectorFrom(operation, children, slot, i);
                    }
                    OperationExecutionResult currentResult = m_Host.TickEdge(current);
                    if (currentResult != OperationExecutionResult.Failure)
                        return currentResult == OperationExecutionResult.Running ? m_Host.Wait(operation, OperationWaitReason.ChildCompletion) : currentResult;
                    cursor++;
                }
            }
            return TickSelectorFrom(operation, children, slot, cursor < 0 ? 0 : cursor);
        }

        public OperationExecutionResult TickParallel(OperationExecutionDescriptor operation)
        {
            IReadOnlyList<ProgramControlFlowEdge> children = Edges(operation.Handle, ProgramControlFlowKind.Child);
            int slot = RequireOperationSlot(operation, ProgramStateSemantic.RunnableChildCursor);
            int completedMask = m_Host.ReadInt32(slot);
            bool completesWithFirstChild = operation.Integer0 == 2;
            bool ticksCompletedChildren = operation.Integer0 == 1;
            bool running = false;
            bool stopping = false;
            for (int i = 0; i < children.Count; i++)
            {
                if (i >= 31)
                    throw new InvalidOperationException($"Parallel operation '{operation.Handle}' exceeds the portable 31-child completion mask.");
                ProgramControlFlowEdge edge = children[i];
                if (completesWithFirstChild && (completedMask & 1) != 0)
                {
                    if (i == 0)
                        continue;
                    OperationStopStatus stop = m_Host.IsStopping(edge.Target)
                        ? m_Host.ContinueStop(edge.Target)
                        : m_Host.IsActive(edge.Target)
                            ? m_Host.RequestStop(edge.Target, OperationStopContext.ParentStop(operation.Handle))
                            : OperationStopStatus.Completed;
                    if (stop == OperationStopStatus.Failed)
                        return OperationExecutionResult.Failure;
                    if (stop == OperationStopStatus.Running)
                    {
                        running = true;
                        stopping = true;
                    }
                    continue;
                }
                if (!EvaluateCondition(edge))
                {
                    if (m_Host.IsStopping(edge.Target))
                    {
                        OperationStopStatus pendingStop = m_Host.ContinueStop(edge.Target);
                        if (pendingStop == OperationStopStatus.Failed)
                            return OperationExecutionResult.Failure;
                        if (pendingStop == OperationStopStatus.Running)
                        {
                            running = true;
                            stopping = true;
                        }
                    }
                    else if (UsesSelfAbort(edge.AbortPolicy) && m_Host.IsActive(edge.Target))
                    {
                        OperationStopStatus stop = m_Host.RequestStop(edge.Target, OperationStopContext.SelfAbort(edge.Target));
                        if (stop == OperationStopStatus.Failed)
                            return OperationExecutionResult.Failure;
                        if (stop == OperationStopStatus.Running)
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
                OperationExecutionResult result = m_Host.TickEdge(edge);
                if (result == OperationExecutionResult.Running)
                    running = true;
                else if (!ticksCompletedChildren)
                {
                    completedMask |= 1 << i;
                    if (completesWithFirstChild && i == 0 && result == OperationExecutionResult.Failure)
                        completedMask |= int.MinValue;
                }
            }
            m_Host.WriteInt32(slot, completedMask);
            if (running)
                return m_Host.Wait(operation, stopping ? OperationWaitReason.ParallelStop : OperationWaitReason.ParallelCompletion);
            return completesWithFirstChild && completedMask < 0
                ? OperationExecutionResult.Failure
                : OperationExecutionResult.Success;
        }

        OperationExecutionResult TickSelectorFrom(
            OperationExecutionDescriptor operation,
            IReadOnlyList<ProgramControlFlowEdge> children,
            int cursorSlot,
            int start)
        {
            for (int i = Math.Max(0, start); i < children.Count; i++)
            {
                ProgramControlFlowEdge edge = children[i];
                if (!EvaluateCondition(edge))
                    continue;
                OperationExecutionResult result = m_Host.TickEdge(edge);
                if (result == OperationExecutionResult.Failure)
                    continue;
                m_Host.WriteInt32(cursorSlot, i);
                return result == OperationExecutionResult.Running ? m_Host.Wait(operation, OperationWaitReason.ChildCompletion) : result;
            }
            m_Host.WriteInt32(cursorSlot, -1);
            return OperationExecutionResult.Failure;
        }

        bool EvaluateCondition(ProgramControlFlowEdge edge) => m_Host.EvaluateCondition(edge);

        IReadOnlyList<ProgramControlFlowEdge> Edges(OperationHandle source, ProgramControlFlowKind kind) =>
            m_Host.Topology.Outgoing(source, kind);

        int RequireOperationSlot(OperationExecutionDescriptor operation, ProgramStateSemantic semantic) =>
            m_Host.Topology.RequireOperationStateSlot(operation.Handle, semantic);

        static int FindChildIndex(IReadOnlyList<ProgramControlFlowEdge> children, OperationHandle target)
        {
            if (!target.IsValid)
                return -1;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i].Target.Equals(target))
                    return i;
            }
            return -1;
        }

        static bool UsesSelfAbort(ProgramAbortPolicy policy)
        {
            return policy == ProgramAbortPolicy.Self || policy == ProgramAbortPolicy.Both;
        }

        static bool UsesLowerPriorityAbort(ProgramAbortPolicy policy)
        {
            return policy == ProgramAbortPolicy.LowerPriority || policy == ProgramAbortPolicy.Both;
        }
    }

    public interface ISynchronousOperationTarget
    {
        void Enter(OperationExecutionDescriptor operation, string inputPort);
        void ExecuteLeaf(OperationExecutionDescriptor operation);
        bool ReadCondition(OperationExecutionDescriptor operation);
        int ReadCycleCursor(OperationExecutionDescriptor operation);
        void WriteCycleCursor(OperationExecutionDescriptor operation, int cursor);
    }

    public static class OperationCompositeRuntime
    {
        public static void ExecuteSynchronous<TTarget>(OperationExecutionTopology topology, ref TTarget target,
            OperationHandle handle, string inputPort = "")
            where TTarget : struct, ISynchronousOperationTarget
        {
            OperationExecutionDescriptor operation = topology.Operation(handle);
            target.Enter(operation, inputPort);
            switch (operation.Code)
            {
                case SimulationOperationCode.Root:
                case SimulationOperationCode.Sequence:
                    ExecuteChildren(topology, ref target, handle);
                    break;
                case SimulationOperationCode.Branch:
                    ExecuteOutput(topology, ref target, handle, target.ReadCondition(operation) ? "True" : "False");
                    ExecuteOutput(topology, ref target, handle, "Then");
                    break;
                case SimulationOperationCode.Cycle:
                    if (inputPort == "Reset")
                        target.WriteCycleCursor(operation, 0);
                    else
                    {
                        int selected = (operation.Integer0 + target.ReadCycleCursor(operation)) % operation.Integer1;
                        IReadOnlyList<ProgramControlFlowEdge> children = topology.Outgoing(handle, ProgramControlFlowKind.Child);
                        for (int i = 0; i < children.Count; i++)
                            if (children[i].Priority == selected)
                                ExecuteSynchronous(topology, ref target, children[i].Target, children[i].TargetPort);
                        target.WriteCycleCursor(operation, (target.ReadCycleCursor(operation) + 1) % operation.Integer1);
                    }
                    break;
                default:
                    target.ExecuteLeaf(operation);
                    ExecuteChildren(topology, ref target, handle);
                    break;
            }
        }

        public static void ExecuteOutput<TTarget>(OperationExecutionTopology topology, ref TTarget target,
            OperationHandle operation, string outputPort) where TTarget : struct, ISynchronousOperationTarget
        {
            IReadOnlyList<ProgramControlFlowEdge> children = topology.Outgoing(operation, ProgramControlFlowKind.Child);
            for (int i = 0; i < children.Count; i++)
                if (string.Equals(children[i].SourcePort, outputPort, StringComparison.Ordinal))
                    ExecuteSynchronous(topology, ref target, children[i].Target, children[i].TargetPort);
        }

        static void ExecuteChildren<TTarget>(OperationExecutionTopology topology, ref TTarget target,
            OperationHandle operation) where TTarget : struct, ISynchronousOperationTarget
        {
            IReadOnlyList<ProgramControlFlowEdge> children = topology.Outgoing(operation, ProgramControlFlowKind.Child);
            for (int i = 0; i < children.Count; i++)
                ExecuteSynchronous(topology, ref target, children[i].Target, children[i].TargetPort);
        }
    }
}
