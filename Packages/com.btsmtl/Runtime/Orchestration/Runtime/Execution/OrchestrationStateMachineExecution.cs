using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics>
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
    {
        public bool CurrentStateRootCompleted()
        {
            int state;
            if (m_StateDepth == 0)
                state = m_Blackboards.BorrowedStateOperationIndex;
            else
            {
                ref readonly OrchestrationExecutionWorkspace<TValue>.StateContext context =
                    ref m_Workspace.States[m_StateDepth - 1];
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
                return m_Workspace.States[m_StateDepth - 1].ExitCause;
            OrchestrationStopContext source = m_Blackboards.BorrowedStopContext;
            return m_Blackboards.BorrowedStateOperationIndex >= 0 && source.IsValid ? MapExitCause(source.Cause) : -1;
        }

        public bool IsCurrentStateExecution(int stateOperationIndex)
        {
            if (m_Blackboards.IsBorrowedOperation(stateOperationIndex))
                return m_Blackboards.IsBorrowedStateScope(stateOperationIndex);
            int frameSlot = OperationFrame(stateOperationIndex);
            for (int i = m_StateDepth - 1; i >= 0; i--)
                if (m_Workspace.States[i].ExecutionFrameIndex == frameSlot &&
                    m_Workspace.States[i].OperationIndex == stateOperationIndex)
                    return true;
            return false;
        }

        OrchestrationOperationResult TickStateMachine(int operationIndex)
        {
            ref OrchestrationOperationState machine = ref m_Operations[operationIndex];
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
            ref OrchestrationOperationState machine = ref m_Operations[machineOperationIndex];
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
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
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
            ref OrchestrationOperationState state = ref m_Operations[stateOperationIndex];
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
            ReleaseCompletedStateTimelines(stateOperationIndex);
            state.ChildCursor = 3;
            return OrchestrationStopStatus.Completed;
        }

        OrchestrationStopStatus ContinueStateMachineStop(int machineOperationIndex, OrchestrationStopContext context)
        {
            ref OrchestrationOperationState machine = ref m_Operations[machineOperationIndex];
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
            ReleaseCompletedStateTimelines(stateOperationIndex);
        }

        void ForceStopStateMachine(int machineOperationIndex, OrchestrationStopContext context)
        {
            ref readonly OrchestrationOperationState machine = ref m_Operations[machineOperationIndex];
            int active = m_Program.StateOperationIndex(machine.ActiveStateIdentityId);
            int exiting = m_Program.StateOperationIndex(machine.ExitingStateIdentityId);
            if (active >= 0)
                ForceStop(active, context);
            if (exiting >= 0 && exiting != active)
                ForceStop(exiting, context);
        }

        void ActivateState(int machineOperationIndex, int stateOperationIndex)
        {
            m_Operations[machineOperationIndex].ActiveStateIdentityId = m_Program.Payload<OrchestrationStateIdentityPayload>(stateOperationIndex).IdentityId;
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
                ref readonly OrchestrationValueScheduleEntry entry = ref m_Program.ValueSchedule.TransitionEntries.Span[
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
            int trace = default(TDiagnostics).CaptureState(ref this, stateOperationIndex, hasRootCompletedOverride);
            m_Workspace.States[m_StateDepth++] = new OrchestrationExecutionWorkspace<TValue>.StateContext
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

        internal int CaptureDiagnosticState(int operationIndex, bool rootCompletedOverride) =>
            m_Input.CaptureTrace && !rootCompletedOverride && !IsPredictiveEvaluation
                ? CaptureStatePath(operationIndex) : -1;

        int CaptureStatePath(int stateOperationIndex, bool lifecycleOwner = false)
        {
            int parentState = m_Program.Operation(stateOperationIndex).ParentStateOperationIndex;
            int parent = parentState >= 0 ? CaptureStatePath(parentState) : -1;
            int index = m_StateFrameCount++;
            m_Workspace.StateFrames[index] = new OrchestrationStateTraceFrame
            {
                ParentIndex = parent,
                StateOperationIndex = stateOperationIndex,
                Generation = ReadGeneration(stateOperationIndex),
                ContextIndex = CaptureTraceContext(stateOperationIndex, lifecycleOwner)
            };
            return index;
        }

        static int MapExitCause(uint cause) => cause switch
        {
            (uint)OrchestrationStopCause.StateTransition => 0,
            (uint)OrchestrationStopCause.SelfAbort => 1,
            (uint)OrchestrationStopCause.LowerPriorityAbort => 2,
            _ => 3
        };
    }
}
