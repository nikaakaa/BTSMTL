using System;

namespace ThirdPersonSimulation
{
    internal interface IOperationStateMachineHost<TTarget>
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        OperationExecutionTopology Topology { get; }
        int ReadInt32(int slotIndex);
        void WriteInt32(int slotIndex, int value);
        CanonicalTextValue ReadIdentity(int slotIndex);
        void WriteIdentity(int slotIndex, CanonicalTextValue value);
        OperationExecutionResult Tick(OperationHandle handle);
        OperationExecutionResult TickPersistent(OperationHandle handle);
        OperationStopStatus RequestStop(OperationHandle handle, OperationStopContext context);
        void ForceStop(OperationHandle handle, OperationStopContext context);
        bool IsActive(OperationHandle handle);
        bool IsStopping(OperationHandle handle);
        ulong ReadGeneration(OperationHandle handle);
        bool EvaluateCondition(ProgramControlFlowEdge edge);
        int RequireOperationSlot(OperationExecutionDescriptor operation, ProgramStateSemantic semantic);
        void NotifyStateLifecycle(OperationExecutionDescriptor machine, OperationHandle state, OperationStateLifecyclePhase phase);
        void NotifyStateTransition(OperationExecutionDescriptor machine, OperationHandle exitingState, OperationHandle targetState);
        void TraceEdge(ProgramControlFlowEdge edge, bool selected, bool passed);
        OperationExecutionResult Wait(OperationExecutionDescriptor operation, OperationWaitReason reason);
        CanonicalTextValue CurrentStateExecutionPath { get; }
        CanonicalTextValue BuildStateExecutionPath(CanonicalTextValue parent, ulong machine, ulong state, ulong generation);
        OperationControlRuntime<TTarget>.StateExecutionScope PushStateScope(OperationHandle state, int exitCause);
        OperationControlRuntime<TTarget>.StateExecutionScope PushStateScope(OperationHandle state, int exitCause, bool hasRootCompletedOverride, bool rootCompletedOverride);
    }

    internal sealed class OperationStateMachineRuntime<TTarget>
        where TTarget : struct, IOperationControlTarget<TTarget>
    {
        readonly IOperationStateMachineHost<TTarget> m_Host;

        public OperationStateMachineRuntime(IOperationStateMachineHost<TTarget> host)
        {
            m_Host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public ProgramControlFlowEdge PredictCurrentStateRootCompletionTransition(OperationHandle state)
        {
            OperationControlRuntime<TTarget>.StateExecutionScope scope = m_Host.PushStateScope(state, -1, true, true);
            try
            {
                return SelectTransition(state);
            }
            finally
            {
                scope.Dispose();
            }
        }

        public OperationExecutionResult Tick(OperationExecutionDescriptor operation)
        {
            int activeSlot = m_Host.RequireOperationSlot(operation, ProgramStateSemantic.StateMachineActive);
            int pendingSlot = m_Host.RequireOperationSlot(operation, ProgramStateSemantic.StateMachinePending);
            int exitingSlot = m_Host.RequireOperationSlot(operation, ProgramStateSemantic.StateMachineExiting);
            int transitionSlot = m_Host.RequireOperationSlot(operation, ProgramStateSemantic.StateMachineTransition);
            OperationHandle exiting = ResolveOperationIdentity(m_Host.ReadIdentity(exitingSlot));
            if (exiting.IsValid)
                return ContinueStateTransition(operation, activeSlot, pendingSlot, exitingSlot, transitionSlot, exiting);

            OperationHandle active = ResolveOperationIdentity(m_Host.ReadIdentity(activeSlot));
            if (!active.IsValid)
            {
                OperationHandle enter = m_Host.Topology.StateMachineInitialEntry(operation.Handle);
                ProgramControlFlowEdge initial = SelectTransition(enter);
                if (initial == null || m_Host.Topology.Operation(initial.Target).Code != SimulationOperationCode.State)
                    return OperationExecutionResult.Failure;
                active = initial.Target;
                m_Host.TraceEdge(initial, true, true);
                ActivateState(operation, activeSlot, active);
            }

            OperationHandle anyState = m_Host.Topology.StateMachineAnyStateEntry(operation.Handle);
            ProgramControlFlowEdge transition = anyState.IsValid ? SelectTransition(anyState, active, active) : null;
            if (transition == null)
            {
                OperationExecutionResult stateResult = m_Host.Tick(active);
                if (stateResult == OperationExecutionResult.Failure)
                    return stateResult;
                transition = SelectTransition(active, default, active);
            }
            if (transition == null)
                return OperationExecutionResult.Running;

            m_Host.TraceEdge(transition, true, true);
            m_Host.WriteIdentity(exitingSlot, m_Host.Topology.OperationIdentity(active));
            m_Host.WriteIdentity(pendingSlot, m_Host.Topology.OperationIdentity(transition.Target));
            m_Host.WriteIdentity(transitionSlot, transition.Identity);
            return ContinueStateTransition(operation, activeSlot, pendingSlot, exitingSlot, transitionSlot, active);
        }

        OperationExecutionResult ContinueStateTransition(
            OperationExecutionDescriptor machine,
            int activeSlot,
            int pendingSlot,
            int exitingSlot,
            int transitionSlot,
            OperationHandle exiting)
        {
            OperationHandle target = ResolveOperationIdentity(m_Host.ReadIdentity(pendingSlot));
            OperationStopContext context = OperationStopContext.StateTransition(exiting, target);
            OperationStopStatus stop = m_Host.RequestStop(exiting, context);
            if (stop == OperationStopStatus.Running)
                return m_Host.Wait(machine, OperationWaitReason.StateExit);
            if (stop == OperationStopStatus.Failed)
                return OperationExecutionResult.Failure;
            m_Host.WriteIdentity(exitingSlot, string.Empty);
            m_Host.WriteIdentity(pendingSlot, string.Empty);
            m_Host.WriteIdentity(transitionSlot, string.Empty);
            if (!target.IsValid || m_Host.Topology.Operation(target).Code == SimulationOperationCode.StateExit)
            {
                m_Host.WriteIdentity(activeSlot, string.Empty);
                ClearStateMachineExecutionPath(machine);
                m_Host.NotifyStateLifecycle(machine, exiting, OperationStateLifecyclePhase.Exited);
                return OperationExecutionResult.Success;
            }
            m_Host.NotifyStateLifecycle(machine, exiting, OperationStateLifecyclePhase.Exited);
            m_Host.NotifyStateTransition(machine, exiting, target);
            ActivateState(machine, activeSlot, target);
            return m_Host.Tick(target) == OperationExecutionResult.Failure
                ? OperationExecutionResult.Failure : OperationExecutionResult.Running;
        }

        public OperationExecutionResult TickState(OperationExecutionDescriptor operation)
        {
            int cursorSlot = m_Host.RequireOperationSlot(operation, ProgramStateSemantic.RunnableChildCursor);
            int phase = m_Host.ReadInt32(cursorSlot);
            if (phase <= 0)
            {
                ProgramControlFlowEdge enter = m_Host.Topology.StateOnEnter(operation.Handle);
                if (enter != null)
                {
                    OperationExecutionResult result = TickInStateContext(operation, enter.Target, -1);
                    if (result != OperationExecutionResult.Success)
                        return result == OperationExecutionResult.Running ? m_Host.Wait(operation, OperationWaitReason.StateEnter) : result;
                    m_Host.ForceStop(enter.Target, OperationStopContext.Reset(enter.Target));
                }
                phase = 1;
                m_Host.WriteInt32(cursorSlot, phase);
            }
            ProgramControlFlowEdge root = m_Host.Topology.StateRoot(operation.Handle);
            if (root == null)
                return OperationExecutionResult.Running;
            OperationExecutionResult rootResult;
            OperationControlRuntime<TTarget>.StateExecutionScope scope = m_Host.PushStateScope(operation.Handle, -1);
            try
            {
                rootResult = m_Host.TickPersistent(root.Target);
            }
            finally
            {
                scope.Dispose();
            }
            return rootResult == OperationExecutionResult.Failure
                ? OperationExecutionResult.Failure
                : OperationExecutionResult.Running;
        }

        public OperationStopStatus ContinueStateStop(OperationExecutionDescriptor state, OperationStopContext context)
        {
            int cursorSlot = m_Host.RequireOperationSlot(state, ProgramStateSemantic.RunnableChildCursor);
            int phase = m_Host.ReadInt32(cursorSlot);
            if (phase < 2)
            {
                ProgramControlFlowEdge active = phase <= 0
                    ? m_Host.Topology.StateOnEnter(state.Handle)
                    : m_Host.Topology.StateRoot(state.Handle);
                if (active != null)
                {
                    using (m_Host.PushStateScope(state.Handle, MapExitCause(context.Cause)))
                    {
                        OperationStopStatus activeStop = m_Host.RequestStop(active.Target, context);
                        if (activeStop != OperationStopStatus.Completed)
                            return activeStop;
                    }
                }
                phase = 2;
                m_Host.WriteInt32(cursorSlot, phase);
            }

            ProgramControlFlowEdge exit = m_Host.Topology.StateOnExit(state.Handle);
            if (exit != null)
            {
                OperationExecutionResult result = TickInStateContext(state, exit.Target, MapExitCause(context.Cause));
                if (result == OperationExecutionResult.Running)
                    return OperationStopStatus.Running;
                if (result == OperationExecutionResult.Failure)
                    return OperationStopStatus.Failed;
                m_Host.ForceStop(exit.Target, OperationStopContext.Reset(exit.Target));
            }
            m_Host.WriteInt32(cursorSlot, 3);
            return OperationStopStatus.Completed;
        }

        public OperationStopStatus ContinueStateMachineStop(OperationExecutionDescriptor machine, OperationStopContext context)
        {
            int activeSlot = m_Host.RequireOperationSlot(machine, ProgramStateSemantic.StateMachineActive);
            int pendingSlot = m_Host.RequireOperationSlot(machine, ProgramStateSemantic.StateMachinePending);
            int exitingSlot = m_Host.RequireOperationSlot(machine, ProgramStateSemantic.StateMachineExiting);
            int transitionSlot = m_Host.RequireOperationSlot(machine, ProgramStateSemantic.StateMachineTransition);
            OperationHandle exiting = ResolveOperationIdentity(m_Host.ReadIdentity(exitingSlot));
            if (!exiting.IsValid)
                exiting = ResolveOperationIdentity(m_Host.ReadIdentity(activeSlot));
            m_Host.WriteIdentity(pendingSlot, string.Empty);
            m_Host.WriteIdentity(transitionSlot, string.Empty);
            if (!exiting.IsValid)
            {
                m_Host.WriteIdentity(activeSlot, string.Empty);
                ClearStateMachineExecutionPath(machine);
                return OperationStopStatus.Completed;
            }
            m_Host.WriteIdentity(exitingSlot, m_Host.Topology.OperationIdentity(exiting));
            OperationStopStatus stop = m_Host.RequestStop(exiting, context);
            if (stop != OperationStopStatus.Completed)
                return stop;
            m_Host.WriteIdentity(activeSlot, string.Empty);
            m_Host.WriteIdentity(exitingSlot, string.Empty);
            ClearStateMachineExecutionPath(machine);
            m_Host.NotifyStateLifecycle(machine, exiting, OperationStateLifecyclePhase.Exited);
            return OperationStopStatus.Completed;
        }

        public void ForceStopState(OperationExecutionDescriptor state, OperationStopContext context)
        {
            using (m_Host.PushStateScope(state.Handle, MapExitCause(context.Cause)))
            {
                var entries = m_Host.Topology.Outgoing(state.Handle, ProgramControlFlowKind.Enter);
                for (int i = 0; i < entries.Count; i++)
                    m_Host.ForceStop(entries[i].Target, context);
                var exits = m_Host.Topology.Outgoing(state.Handle, ProgramControlFlowKind.Exit);
                for (int i = 0; i < exits.Count; i++)
                    m_Host.ForceStop(exits[i].Target, context);
            }
        }

        public void ForceStopStateMachine(OperationExecutionDescriptor machine, OperationStopContext context)
        {
            int activeSlot = m_Host.Topology.FindOperationStateSlot(machine.Handle, ProgramStateSemantic.StateMachineActive);
            int exitingSlot = m_Host.Topology.FindOperationStateSlot(machine.Handle, ProgramStateSemantic.StateMachineExiting);
            OperationHandle active = activeSlot >= 0 ? ResolveOperationIdentity(m_Host.ReadIdentity(activeSlot)) : OperationHandle.Invalid;
            OperationHandle exiting = exitingSlot >= 0 ? ResolveOperationIdentity(m_Host.ReadIdentity(exitingSlot)) : OperationHandle.Invalid;
            if (active.IsValid)
                m_Host.ForceStop(active, context);
            if (exiting.IsValid && !exiting.Equals(active))
                m_Host.ForceStop(exiting, context);
            ClearStateMachineExecutionPath(machine);
        }

        void ActivateState(OperationExecutionDescriptor machine, int activeSlot, OperationHandle state)
        {
            m_Host.WriteIdentity(activeSlot, m_Host.Topology.OperationIdentity(state));
            ulong generation = checked(m_Host.ReadGeneration(state) + 1);
            if (generation == 0)
                generation = 1;
            CanonicalTextValue parent = m_Host.CurrentStateExecutionPath;
            CanonicalTextValue path = m_Host.BuildStateExecutionPath(parent, (ulong)machine.Handle.Value, (ulong)state.Value, generation);
            int pathSlot = m_Host.RequireOperationSlot(machine, ProgramStateSemantic.StateMachineExecutionPath);
            m_Host.WriteIdentity(pathSlot, path);
            m_Host.NotifyStateLifecycle(machine, state, OperationStateLifecyclePhase.Entered);
        }

        void ClearStateMachineExecutionPath(OperationExecutionDescriptor machine)
        {
            int slot = m_Host.Topology.FindOperationStateSlot(machine.Handle, ProgramStateSemantic.StateMachineExecutionPath);
            if (slot >= 0)
                m_Host.WriteIdentity(slot, string.Empty);
        }

        ProgramControlFlowEdge SelectTransition(
            OperationHandle source,
            OperationHandle excludedTarget = default,
            OperationHandle stateContext = default)
        {
            if (!source.IsValid)
                return null;
            var transitions = m_Host.Topology.Outgoing(source, ProgramControlFlowKind.Transition);
            bool hasScope = stateContext.IsValid;
            OperationControlRuntime<TTarget>.StateExecutionScope scope = hasScope
                ? m_Host.PushStateScope(stateContext, -1)
                : default;
            try
            {
                for (int i = 0; i < transitions.Count; i++)
                {
                    if (excludedTarget.IsValid && transitions[i].Target.Equals(excludedTarget))
                        continue;
                    if (m_Host.EvaluateCondition(transitions[i]))
                        return transitions[i];
                }
                return null;
            }
            finally
            {
                if (hasScope)
                    scope.Dispose();
            }
        }

        OperationExecutionResult TickInStateContext(OperationExecutionDescriptor state, OperationHandle target, int exitCause)
        {
            OperationControlRuntime<TTarget>.StateExecutionScope scope = m_Host.PushStateScope(state.Handle, exitCause);
            try
            {
                return m_Host.Tick(target);
            }
            finally
            {
                scope.Dispose();
            }
        }

        static int MapExitCause(OperationStopCause cause)
        {
            switch (cause)
            {
                case OperationStopCause.StateTransition: return 0;
                case OperationStopCause.SelfAbort: return 1;
                case OperationStopCause.LowerPriorityAbort: return 2;
                default: return 3;
            }
        }

        OperationHandle ResolveOperationIdentity(CanonicalTextValue identity)
        {
            return m_Host.Topology.TryResolveOperation(identity, out OperationHandle operation)
                ? operation
                : OperationHandle.Invalid;
        }

    }
}
