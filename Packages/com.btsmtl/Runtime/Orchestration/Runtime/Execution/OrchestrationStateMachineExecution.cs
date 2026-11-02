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

        public OrchestrationStopContext CurrentStopContext()
        {
            if (m_StateDepth != 0)
                return m_States[m_StateDepth - 1].ExitContext;
            return m_InvocationStopContext.IsValid ? m_InvocationStopContext : m_Blackboards.BorrowedStopContext;
        }

        public OrchestrationStateExitContext CurrentStateExitContext() => CurrentStopContext().StateExit;

        public uint CurrentStateExitCause() => CurrentStateExitContext().Cause;

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
            {
                RetargetStateTransition(operationIndex, exiting);
                return ContinueStateTransition(operationIndex, exiting);
            }
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
                {
                    PushState(active, in ReadStopContext(active));
                    try
                    {
                        InvokeHooks(operationIndex, OrchestrationLifecycleHook.StateExited, active, active);
                    }
                    finally
                    {
                        PopState();
                    }
                    machine.ActiveStateIdentityId = 0;
                    return stateResult;
                }
                transition = SelectTransition(active, -1, active);
            }
            if (transition < 0)
                return OrchestrationOperationResult.Running;
            TraceEdge(transition, true, true, active);
            ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(transition);
            machine.ExitingStateIdentityId = m_Program.Payload<OrchestrationStateIdentityPayload>(active).IdentityId;
            machine.StopContext = OrchestrationStopContext.StateTransition(operationIndex, m_FrameSlot,
                m_Input.Tick, edge.TargetOperationIndex, transition).ForStateExit(active, m_FrameSlot,
                edge.TargetOperationIndex, transition);
            return ContinueStateTransition(operationIndex, active);
        }

        void RetargetStateTransition(int machineOperationIndex, int exiting)
        {
            ref OrchestrationOperationState machine = ref OperationState(machineOperationIndex);
            int pending = machine.StopContext.ReplacementEdgeIndex;
            int anyState = m_Program.AnyStateEntry(machineOperationIndex);
            int selected = anyState >= 0 ? SelectTransition(anyState, exiting, exiting) : -1;
            int source = SelectTransition(exiting, -1, exiting);
            if (source >= 0 && (selected < 0 || m_Program.Edge(source).Priority < m_Program.Edge(selected).Priority))
                selected = source;
            if (selected < 0 || m_Program.Edge(selected).Priority >= m_Program.Edge(pending).Priority)
                return;
            int target = m_Program.Edge(selected).TargetOperationIndex;
            machine.StopContext = machine.StopContext.WithReplacement(target, selected)
                .ForStateExit(exiting, m_FrameSlot, target, selected);
            TraceEdge(selected, true, true, exiting);
        }

        OrchestrationOperationResult ContinueStateTransition(int machineOperationIndex, int exiting)
        {
            ref OrchestrationOperationState machine = ref OperationState(machineOperationIndex);
            machine.StopContext = machine.StopContext.AtTick(m_Input.Tick);
            int target = machine.StopContext.ReplacementOperationIndex;
            OrchestrationStopStatus stop = RequestStop(exiting, in machine.StopContext);
            if (stop == OrchestrationStopStatus.Running)
                return Wait(machineOperationIndex, OrchestrationWaitReason.StateExit);
            if (stop == OrchestrationStopStatus.Failed)
                return OrchestrationOperationResult.Failure;
            machine.ExitingStateIdentityId = 0;
            PushState(exiting, in machine.StopContext);
            try
            {
                InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateExited,
                    exiting, exiting);
                if (target >= 0 && m_Program.Operation(target).Code != OrchestrationOperationCode.StateExit)
                    InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateTransition,
                        target, exiting);
            }
            finally
            {
                PopState();
            }
            ClearStopContext(machineOperationIndex);
            if (target < 0 || m_Program.Operation(target).Code == OrchestrationOperationCode.StateExit)
            {
                machine.ActiveStateIdentityId = 0;
                return OrchestrationOperationResult.Success;
            }
            ActivateState(machineOperationIndex, target);
            return Tick(target) == OrchestrationOperationResult.Failure
                ? OrchestrationOperationResult.Failure : OrchestrationOperationResult.Running;
        }

        OrchestrationOperationResult TickState(int operationIndex)
        {
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            if (state.StopContext.IsValid)
                return ContinueNaturalStateExit(operationIndex);
            if (state.ChildCursor <= 0)
            {
                state.ChildCursor = -1;
                int enterEdge = m_Program.StateOnEnterEdge(operationIndex);
                if (enterEdge >= 0)
                {
                    int enter = m_Program.Edge(enterEdge).TargetOperationIndex;
                    OrchestrationOperationResult result = TickInState(operationIndex, enter, default);
                    if (result != OrchestrationOperationResult.Success)
                        return result == OrchestrationOperationResult.Running
                            ? Wait(operationIndex, OrchestrationWaitReason.StateEnter) : BeginNaturalStateExit(operationIndex);
                    ResetOperation(enter);
                }
                state.ChildCursor = 1;
            }
            int rootEdge = m_Program.StateRootEdge(operationIndex);
            if (rootEdge < 0)
                return OrchestrationOperationResult.Running;
            OrchestrationOperationResult rootResult;
            PushState(operationIndex, default);
            try
            {
                rootResult = TickPersistent(m_Program.Edge(rootEdge).TargetOperationIndex);
            }
            finally
            {
                PopState();
            }
            return rootResult == OrchestrationOperationResult.Failure
                ? BeginNaturalStateExit(operationIndex) : OrchestrationOperationResult.Running;
        }

        OrchestrationOperationResult BeginNaturalStateExit(int stateOperationIndex)
        {
            OperationState(stateOperationIndex).StopContext = new OrchestrationStopContext(
                (uint)OrchestrationStopCause.None, OrchestrationStopMode.Request, m_Input.Tick,
                stateOperationIndex, m_FrameSlot).ForStateExit(stateOperationIndex, m_FrameSlot);
            return ContinueNaturalStateExit(stateOperationIndex);
        }

        OrchestrationOperationResult ContinueNaturalStateExit(int stateOperationIndex) =>
            ContinueStateStop(stateOperationIndex, in ReadStopContext(stateOperationIndex)) == OrchestrationStopStatus.Running
                ? Wait(stateOperationIndex, OrchestrationWaitReason.StateExit) : OrchestrationOperationResult.Failure;

        OrchestrationStopStatus ContinueStateStop(int stateOperationIndex, in OrchestrationStopContext context)
        {
            ref OrchestrationOperationState state = ref OperationState(stateOperationIndex);
            if (state.ChildCursor == 0 || state.ChildCursor == 3)
                return OrchestrationStopStatus.Completed;
            OrchestrationStopContext exitContext = StateExitContext(stateOperationIndex, in context);
            WriteStopContext(stateOperationIndex, in exitContext);
            if (state.ChildCursor < 2)
            {
                int activeEdge = state.ChildCursor <= 0
                    ? m_Program.StateOnEnterEdge(stateOperationIndex) : m_Program.StateRootEdge(stateOperationIndex);
                if (activeEdge >= 0)
                {
                    PushState(stateOperationIndex, in exitContext);
                    try
                    {
                        OrchestrationStopStatus activeStop = RequestStop(m_Program.Edge(activeEdge).TargetOperationIndex, in exitContext);
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
            CompleteStateExit(stateOperationIndex, in exitContext);
            return OrchestrationStopStatus.Completed;
        }

        void CompleteStateExit(int stateOperationIndex, in OrchestrationStopContext context)
        {
            OperationState(stateOperationIndex).ChildCursor = 3;
            int exitEdge = m_Program.StateOnExitEdge(stateOperationIndex);
            if (exitEdge >= 0)
            {
                int exit = m_Program.Edge(exitEdge).TargetOperationIndex;
                PushState(stateOperationIndex, in context);
                try
                {
                    if (Advance(new OrchestrationExecutionEntry(exit, OrchestrationExecutionContract.Complete)) ==
                        OrchestrationOperationResult.Failure)
                        Fail(OrchestrationExecutionErrorCode.StateExitFailed, exit, stateOperationIndex);
                    ResetOperation(exit);
                }
                finally
                {
                    PopState();
                }
            }
        }

        OrchestrationStopStatus ContinueStateMachineStop(int machineOperationIndex, in OrchestrationStopContext context)
        {
            ref OrchestrationOperationState machine = ref OperationState(machineOperationIndex);
            int exiting = m_Program.StateOperationIndex(machine.ExitingStateIdentityId);
            if (exiting < 0)
                exiting = m_Program.StateOperationIndex(machine.ActiveStateIdentityId);
            if (exiting < 0)
            {
                machine.ActiveStateIdentityId = 0;
                return OrchestrationStopStatus.Completed;
            }
            machine.ExitingStateIdentityId = m_Program.Payload<OrchestrationStateIdentityPayload>(exiting).IdentityId;
            OrchestrationStopContext exitContext = context.ForStateExit(exiting, m_FrameSlot)
                .AtOperation(exiting, m_FrameSlot, m_Input.Tick);
            OrchestrationStopStatus stop = RequestStop(exiting, in exitContext);
            if (stop != OrchestrationStopStatus.Completed)
                return stop;
            machine.ActiveStateIdentityId = 0;
            machine.ExitingStateIdentityId = 0;
            PushState(exiting, in exitContext);
            try
            {
                InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateExited, exiting, exiting);
            }
            finally
            {
                PopState();
            }
            return OrchestrationStopStatus.Completed;
        }

        void ForceStopState(int stateOperationIndex, in OrchestrationStopContext context)
        {
            ref OrchestrationOperationState state = ref OperationState(stateOperationIndex);
            if (state.ChildCursor == 0 || state.ChildCursor == 3)
                return;
            OrchestrationStopContext exitContext = StateExitContext(stateOperationIndex, in context);
            PushState(stateOperationIndex, in exitContext);
            try
            {
                foreach (int edgeIndex in m_Program.Outgoing(stateOperationIndex, OrchestrationFlowKind.Enter))
                {
                    int child = m_Program.Edge(edgeIndex).TargetOperationIndex;
                    if (IsActive(child))
                        ForceStop(child, in exitContext);
                }
                if (context.Cause != (uint)OrchestrationStopCause.Shutdown)
                    CompleteStateExit(stateOperationIndex, in exitContext);
            }
            finally
            {
                PopState();
            }
        }

        void ForceStopStateMachine(int machineOperationIndex, in OrchestrationStopContext context)
        {
            ref readonly OrchestrationOperationState machine = ref OperationState(machineOperationIndex);
            int active = m_Program.StateOperationIndex(machine.ActiveStateIdentityId);
            int exiting = m_Program.StateOperationIndex(machine.ExitingStateIdentityId);
            if (active >= 0)
                ForceStopMachineState(machineOperationIndex, active, in context);
            if (exiting >= 0 && exiting != active)
                ForceStopMachineState(machineOperationIndex, exiting, in context);
        }

        void ForceStopMachineState(int machineOperationIndex, int stateOperationIndex,
            in OrchestrationStopContext context)
        {
            int phase = OperationState(stateOperationIndex).ChildCursor;
            bool entered = phase != 0 && phase != 3;
            OrchestrationStopContext exitContext = context.ForStateExit(stateOperationIndex, m_FrameSlot)
                .AtOperation(stateOperationIndex, m_FrameSlot, m_Input.Tick);
            ForceStop(stateOperationIndex, in exitContext);
            if (entered && context.Cause != (uint)OrchestrationStopCause.Shutdown)
            {
                PushState(stateOperationIndex, in exitContext);
                try
                {
                    InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateExited,
                        stateOperationIndex, stateOperationIndex);
                }
                finally
                {
                    PopState();
                }
            }
        }

        OrchestrationStopContext StateExitContext(int stateOperationIndex, in OrchestrationStopContext context) =>
            context.StateExit.IsValid && context.StateExit.SourceStateOperationIndex == stateOperationIndex &&
                context.StateExit.SourceStateExecutionFrameIndex == m_FrameSlot
                ? context : context.ForStateExit(stateOperationIndex, m_FrameSlot);

        void ActivateState(int machineOperationIndex, int stateOperationIndex)
        {
            OperationState(machineOperationIndex).ActiveStateIdentityId = m_Program.Payload<OrchestrationStateIdentityPayload>(stateOperationIndex).IdentityId;
            InvokeHooks(machineOperationIndex, OrchestrationLifecycleHook.StateEntered,
                stateOperationIndex);
        }

        int SelectTransition(int sourceOperationIndex, int excludedTarget = -1, int stateContext = -1)
        {
            bool hasScope = stateContext >= 0;
            if (hasScope)
                PushState(stateContext, in ReadStopContext(stateContext));
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

        OrchestrationOperationResult TickInState(int stateOperationIndex, int targetOperationIndex,
            in OrchestrationStopContext context)
        {
            PushState(stateOperationIndex, in context);
            try
            {
                return Tick(targetOperationIndex);
            }
            finally
            {
                PopState();
            }
        }

        void PushState(int stateOperationIndex, in OrchestrationStopContext context,
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
                ExitContext = context,
                HasRootCompletedOverride = hasRootCompletedOverride,
                RootCompletedOverride = rootCompletedOverride,
                TraceFrameIndex = trace
            };
        }

        void PopState() => m_StateDepth--;

    }
}
