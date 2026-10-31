using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TNumerics, THost>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where THost : struct, IOrchestrationHost<TValue>
    {
        public bool CurrentStateRootCompleted()
        {
            int state;
            if (m_StateDepth == 0)
                return m_Blackboards.HasBorrowedState && m_Blackboards.BorrowedStateRootCompleted();
            else
            {
                ref readonly OrchestrationStateContext context =
                    ref m_States[m_StateDepth - 1];
                if (context.HasRootCompletedOverride)
                    return context.RootCompletedOverride;
                state = context.OperationIndex;
            }
            if (state < 0)
                return false;
            int edgeIndex = m_Program.StateRootEdge(state);
            return edgeIndex >= 0 && ReadStatus(m_Program.Edge(edgeIndex).TargetOperationIndex) ==
                OrchestrationOperationStatus.Success;
        }

        public int CurrentStateExitCause()
        {
            if (m_StateDepth != 0)
                return m_States[m_StateDepth - 1].ExitCause;
            OrchestrationStopContext source = m_Blackboards.BorrowedStopContext;
            return m_Blackboards.HasBorrowedState && source.IsValid ? MapExitCause(source.Cause) : -1;
        }

        public bool IsCurrentStateExecution(int stateOperationIndex)
        {
            if (m_Blackboards.IsBorrowedOperation(stateOperationIndex))
                return m_Blackboards.IsBorrowedStateScope(stateOperationIndex);
            int frameSlot = OperationFrame(stateOperationIndex);
            for (int i = m_StateDepth - 1; i >= 0; i--)
                if (m_States[i].ExecutionFrameIndex == frameSlot &&
                    m_States[i].OperationIndex == stateOperationIndex)
                    return true;
            return false;
        }

        OrchestrationOperationResult TickStateMachine(int operationIndex)
        {
            ref OrchestrationOperationState machine = ref OperationState(operationIndex);
            int exiting = m_Program.StateOperationIndex(machine.ExitingStateIdentityId);
            if (exiting >= 0)
                return ContinueStateTransition(operationIndex, exiting);
            int active = m_Program.StateOperationIndex(machine.ActiveStateIdentityId);
            if (active < 0)
            {
                int initial = SelectTransition(m_Program.InitialStateEntry(operationIndex));
                if (initial < 0 || m_Program.Operation(m_Program.Edge(initial).TargetOperationIndex).Code != OrchestrationOperationCode.State)
                    return OrchestrationOperationResult.Failure;
                active = m_Program.Edge(initial).TargetOperationIndex;
                TraceEdge(initial, true, true);
                ActivateState(operationIndex, active);
            }
            int anyState = m_Program.AnyStateEntry(operationIndex);
            int transition = anyState >= 0 ? SelectTransition(anyState, active, active) : -1;
            if (transition < 0)
            {
                OrchestrationOperationResult stateResult = Tick(active);
                if (stateResult == OrchestrationOperationResult.Failure)
                    return stateResult;
                transition = SelectTransition(active, -1, active);
            }
            if (transition < 0)
                return OrchestrationOperationResult.Running;
            TraceEdge(transition, true, true, active);
            ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(transition);
            machine.ExitingStateIdentityId = m_Program.Payload<OrchestrationStateIdentityPayload>(active).IdentityId;
            machine.PendingStateIdentityId = m_Program.Payload<OrchestrationStateIdentityPayload>(edge.TargetOperationIndex).IdentityId;
            machine.TransitionIdentityId = edge.IdentityId;
            return ContinueStateTransition(operationIndex, active);
        }

        OrchestrationOperationResult ContinueStateTransition(int machineOperationIndex, int exiting)
        {
            ref OrchestrationOperationState machine = ref OperationState(machineOperationIndex);
            int target = m_Program.StateOperationIndex(machine.PendingStateIdentityId);
            OrchestrationStopStatus stop = RequestStop(exiting, OrchestrationStopContext.StateTransition(exiting, target));
            if (stop == OrchestrationStopStatus.Running)
                return Wait(machineOperationIndex, OrchestrationWaitReason.StateExit);
            if (stop == OrchestrationStopStatus.Failed)
                return OrchestrationOperationResult.Failure;
            machine.ExitingStateIdentityId = 0;
            machine.PendingStateIdentityId = 0;
            machine.TransitionIdentityId = 0;
            if (target < 0 || m_Program.Operation(target).Code == OrchestrationOperationCode.StateExit)
            {
                machine.ActiveStateIdentityId = 0;
                InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateExited,
                    machineOperationIndex, exiting, exiting, target);
                return OrchestrationOperationResult.Success;
            }
            InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateExited,
                machineOperationIndex, exiting, exiting, target);
            InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateTransition,
                machineOperationIndex, target, exiting, target);
            ActivateState(machineOperationIndex, target);
            return Tick(target) == OrchestrationOperationResult.Failure
                ? OrchestrationOperationResult.Failure : OrchestrationOperationResult.Running;
        }

        OrchestrationOperationResult TickState(int operationIndex)
        {
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            if (state.ChildCursor <= 0)
            {
                int enterEdge = m_Program.StateOnEnterEdge(operationIndex);
                if (enterEdge >= 0)
                {
                    int enter = m_Program.Edge(enterEdge).TargetOperationIndex;
                    OrchestrationOperationResult result = TickInState(operationIndex, enter, -1);
                    if (result != OrchestrationOperationResult.Success)
                        return result == OrchestrationOperationResult.Running
                            ? Wait(operationIndex, OrchestrationWaitReason.StateEnter) : result;
                    ForceStop(enter, OrchestrationStopContext.Reset(enter));
                }
                state.ChildCursor = 1;
            }
            int rootEdge = m_Program.StateRootEdge(operationIndex);
            if (rootEdge < 0)
                return OrchestrationOperationResult.Running;
            OrchestrationOperationResult rootResult;
            PushState(operationIndex, -1);
            try
            {
                rootResult = TickPersistent(m_Program.Edge(rootEdge).TargetOperationIndex);
            }
            finally
            {
                PopState();
            }
            return rootResult == OrchestrationOperationResult.Failure
                ? OrchestrationOperationResult.Failure : OrchestrationOperationResult.Running;
        }

        OrchestrationStopStatus ContinueStateStop(int stateOperationIndex, OrchestrationStopContext context)
        {
            ref OrchestrationOperationState state = ref OperationState(stateOperationIndex);
            if (state.ChildCursor < 2)
            {
                int activeEdge = state.ChildCursor <= 0
                    ? m_Program.StateOnEnterEdge(stateOperationIndex) : m_Program.StateRootEdge(stateOperationIndex);
                if (activeEdge >= 0)
                {
                    PushState(stateOperationIndex, MapExitCause(context.Cause));
                    try
                    {
                        OrchestrationStopStatus activeStop = RequestStop(m_Program.Edge(activeEdge).TargetOperationIndex, context);
                        if (activeStop != OrchestrationStopStatus.Completed)
                            return activeStop;
                    }
                    finally
                    {
                        PopState();
                    }
                }
                state.ChildCursor = 2;
            }
            int exitEdge = m_Program.StateOnExitEdge(stateOperationIndex);
            if (exitEdge >= 0)
            {
                int exit = m_Program.Edge(exitEdge).TargetOperationIndex;
                OrchestrationOperationResult result = TickInState(stateOperationIndex, exit, MapExitCause(context.Cause));
                if (result == OrchestrationOperationResult.Running)
                    return OrchestrationStopStatus.Running;
                if (result == OrchestrationOperationResult.Failure)
                    return OrchestrationStopStatus.Failed;
                ForceStop(exit, OrchestrationStopContext.Reset(exit));
            }
            state.ChildCursor = 3;
            return OrchestrationStopStatus.Completed;
        }

        OrchestrationStopStatus ContinueStateMachineStop(int machineOperationIndex, OrchestrationStopContext context)
        {
            ref OrchestrationOperationState machine = ref OperationState(machineOperationIndex);
            int exiting = m_Program.StateOperationIndex(machine.ExitingStateIdentityId);
            if (exiting < 0)
                exiting = m_Program.StateOperationIndex(machine.ActiveStateIdentityId);
            machine.PendingStateIdentityId = 0;
            machine.TransitionIdentityId = 0;
            if (exiting < 0)
            {
                machine.ActiveStateIdentityId = 0;
                return OrchestrationStopStatus.Completed;
            }
            machine.ExitingStateIdentityId = m_Program.Payload<OrchestrationStateIdentityPayload>(exiting).IdentityId;
            OrchestrationStopStatus stop = RequestStop(exiting, context);
            if (stop != OrchestrationStopStatus.Completed)
                return stop;
            machine.ActiveStateIdentityId = 0;
            machine.ExitingStateIdentityId = 0;
            InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateExited,
                machineOperationIndex, exiting, exiting);
            return OrchestrationStopStatus.Completed;
        }

        void ForceStopState(int stateOperationIndex, OrchestrationStopContext context)
        {
            PushState(stateOperationIndex, MapExitCause(context.Cause));
            try
            {
                foreach (int edgeIndex in m_Program.Outgoing(stateOperationIndex, OrchestrationFlowKind.Enter))
                    ForceStop(m_Program.Edge(edgeIndex).TargetOperationIndex, context);
                foreach (int edgeIndex in m_Program.Outgoing(stateOperationIndex, OrchestrationFlowKind.Exit))
                    ForceStop(m_Program.Edge(edgeIndex).TargetOperationIndex, context);
            }
            finally
            {
                PopState();
            }
        }

        void ForceStopStateMachine(int machineOperationIndex, OrchestrationStopContext context)
        {
            ref readonly OrchestrationOperationState machine = ref OperationState(machineOperationIndex);
            int active = m_Program.StateOperationIndex(machine.ActiveStateIdentityId);
            int exiting = m_Program.StateOperationIndex(machine.ExitingStateIdentityId);
            if (active >= 0)
                ForceStop(active, context);
            if (exiting >= 0 && exiting != active)
                ForceStop(exiting, context);
        }

        void ActivateState(int machineOperationIndex, int stateOperationIndex)
        {
            OperationState(machineOperationIndex).ActiveStateIdentityId = m_Program.Payload<OrchestrationStateIdentityPayload>(stateOperationIndex).IdentityId;
            InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateEntered,
                machineOperationIndex, stateOperationIndex, targetStateOperationIndex: stateOperationIndex);
        }

        int SelectTransition(int sourceOperationIndex, int excludedTarget = -1, int stateContext = -1)
        {
            bool hasScope = stateContext >= 0;
            if (hasScope)
                PushState(stateContext, -1);
            try
            {
                ref readonly OrchestrationValueScheduleEntry entry = ref m_TransitionEntries[
                    ScheduleStateContext() * m_Program.OperationCount + sourceOperationIndex];
                return RunValueSchedule(in entry, excludedTarget).Value;
            }
            finally
            {
                if (hasScope)
                    PopState();
            }
        }

        OrchestrationOperationResult TickInState(int stateOperationIndex, int targetOperationIndex, int exitCause)
        {
            PushState(stateOperationIndex, exitCause);
            try
            {
                return Tick(targetOperationIndex);
            }
            finally
            {
                PopState();
            }
        }

        void PushState(int stateOperationIndex, int exitCause,
            bool hasRootCompletedOverride = false, bool rootCompletedOverride = false)
        {
            if (m_StateDepth + 1 > m_States.Length)
                Fail(OrchestrationExecutionErrorCode.WorkspaceStateDepth, stateOperationIndex,
                    required: m_StateDepth + 1, capacity: m_States.Length);
            int trace = m_Host.CaptureState(ref this, stateOperationIndex, hasRootCompletedOverride);
            m_States[m_StateDepth++] = new OrchestrationStateContext
            {
                ExecutionFrameIndex = OperationFrame(stateOperationIndex),
                OperationIndex = stateOperationIndex,
                ExitCause = exitCause,
                HasRootCompletedOverride = hasRootCompletedOverride,
                RootCompletedOverride = rootCompletedOverride,
                TraceFrameIndex = trace
            };
        }

        void PopState() => m_StateDepth--;

        static int MapExitCause(uint cause) => cause switch
        {
            (uint)OrchestrationStopCause.StateTransition => 0,
            (uint)OrchestrationStopCause.SelfAbort => 1,
            (uint)OrchestrationStopCause.LowerPriorityAbort => 2,
            _ => 3
        };
    }
}
