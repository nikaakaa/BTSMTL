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
        readonly OrchestrationProgram<TValue> m_Program;
        readonly OrchestrationExecutionWorkspace<TValue> m_Workspace;
        readonly IOrchestrationExtensionBindings<TValue, TContext> m_Bindings;
        readonly OrchestrationExecutionFrames<TValue> m_Frames;
        Span<OrchestrationOperationState> m_Operations;
        Span<TValue> m_Values;
        OrchestrationBlackboardStorageView<TValue> m_Blackboards;
        int m_FrameSlot;
        readonly OrchestrationFrameInput<TValue> m_Input;
        readonly OrchestrationIdentityOrdinalView m_Identities;
        TContext m_Context;
        readonly IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2,
            TVector3, TQuaternion, TMath, TValues, TContext> m_TimelineDispatcher;
        readonly IOrchestrationTimelineClipExitDispatcher<TContext> m_ClipExitDispatcher;
        readonly OrchestrationStateView m_TimelineState;
        OrchestrationExecutionContract m_Contract;
        int m_EntryDepth;
        int m_InputDepth;
        int m_StateDepth;
        int m_ForceStopDepth;
        int m_TraceCount;
        int m_StateFrameCount;
        int m_CurrentOperationIndex;
        int m_GraphCallDepth;
        OrchestrationObservationPhase m_ObservationPhase;
        OrchestrationTimelineInvocationFacts m_TimelineInvocation;
        TValue m_TimelineTime;

        public OrchestrationExecutionCursor(OrchestrationProgram<TValue> program,
            OrchestrationExecutionWorkspace<TValue> workspace,
            IOrchestrationExtensionBindings<TValue, TContext> bindings,
            OrchestrationExecutionFrames<TValue> frames, int frameSlot,
            Span<TValue> characterValues, Span<OrchestrationBlackboardMetadata> characterBlackboards,
            ReadOnlySpan<int> characterSlots,
            IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2,
                TVector3, TQuaternion, TMath, TValues, TContext> timelineDispatcher,
            IOrchestrationTimelineClipExitDispatcher<TContext> clipExitDispatcher,
            OrchestrationStateView timelineState,
            in OrchestrationFrameInput<TValue> input,
            in OrchestrationIdentityOrdinalView identities,
            in TContext context)
        {
            m_Program = program;
            m_Workspace = workspace;
            m_Bindings = bindings;
            m_Frames = frames;
            m_FrameSlot = frameSlot;
            m_Operations = frames.Operations(frameSlot);
            m_Values = frames.Values(frameSlot);
            m_Blackboards = new OrchestrationBlackboardStorageView<TValue>(program, frames, frameSlot,
                characterValues, characterBlackboards, characterSlots);
            m_Input = input;
            m_Identities = identities;
            m_Context = context;
            m_TimelineDispatcher = timelineDispatcher;
            m_ClipExitDispatcher = clipExitDispatcher;
            m_TimelineState = timelineState;
            m_Contract = OrchestrationExecutionContract.Resumable;
            m_EntryDepth = 0;
            m_InputDepth = 0;
            m_StateDepth = 0;
            m_ForceStopDepth = 0;
            m_TraceCount = 0;
            m_StateFrameCount = 0;
            m_CurrentOperationIndex = -1;
            m_GraphCallDepth = 0;
            m_ObservationPhase = OrchestrationObservationPhase.None;
            m_TimelineInvocation = default;
            m_TimelineTime = default;
            Array.Clear(workspace.InputObservationWritten, 0, workspace.InputObservationWritten.Length);
            Array.Clear(workspace.OutputObservationWritten, 0, workspace.OutputObservationWritten.Length);
            Array.Clear(workspace.CachedObservationWritten, 0, workspace.CachedObservationWritten.Length);
        }

        public int CurrentOperationIndex => m_CurrentOperationIndex;
        public int ExecutionFrameIndex => m_FrameSlot;
        public ReadOnlySpan<OrchestrationTraceEvent<TValue>> Trace => m_Workspace.TraceEvents.AsSpan(0, m_TraceCount);
        public ReadOnlySpan<OrchestrationStateTraceFrame> StateTraceFrames => m_Workspace.StateFrames.AsSpan(0, m_StateFrameCount);
        public bool IsPredictiveEvaluation
        {
            get
            {
                for (int i = m_StateDepth - 1; i >= 0; i--)
                    if (m_Workspace.States[i].HasRootCompletedOverride)
                        return true;
                return false;
            }
        }

        void BeginInvocation()
        {
            if (m_EntryDepth == 0)
            {
                m_Workspace.ResetOutputs();
            }
            m_EntryDepth++;
        }

        public OrchestrationOperationResult Advance(OrchestrationExecutionEntry entry)
        {
            OrchestrationExecutionContract previous = m_Contract;
            m_Contract = entry.Contract;
            BeginInvocation();
            try
            {
                OrchestrationOperationResult result = Tick(entry.OperationIndex, entry.InputPortId);
                if (entry.Contract == OrchestrationExecutionContract.Complete && result == OrchestrationOperationResult.Running)
                    throw new InvalidOperationException("同次完成的图入口不能返回跨次等待。");
                return result;
            }
            finally
            {
                m_Contract = previous;
                m_EntryDepth--;
            }
        }

        public OrchestrationOperationResult ExecuteOutput(int operationIndex, int outputPortId,
            OrchestrationExecutionContract contract)
        {
            OrchestrationExecutionContract previous = m_Contract;
            m_Contract = contract;
            BeginInvocation();
            try
            {
                OrchestrationOperationResult result = TickOutput(operationIndex, outputPortId);
                if (contract == OrchestrationExecutionContract.Complete && result == OrchestrationOperationResult.Running)
                    throw new InvalidOperationException("同次完成的图输出不能返回跨次等待。");
                return result;
            }
            finally
            {
                m_Contract = previous;
                m_EntryDepth--;
            }
        }

        public bool IsActive(int operationIndex)
        {
            if (operationIndex < 0 || !m_Program.Operation(operationIndex).IsRunnable)
                return false;
            OrchestrationOperationStatus status = ReadStatus(operationIndex);
            return status == OrchestrationOperationStatus.Running || status == OrchestrationOperationStatus.Stopping;
        }

        public bool IsRunning(int operationIndex) =>
            ReadStatus(operationIndex) == OrchestrationOperationStatus.Running;

        public bool IsStopping(int operationIndex) =>
            ReadStatus(operationIndex) == OrchestrationOperationStatus.Stopping;

        public OrchestrationOperationStatus ReadStatus(int operationIndex) =>
            operationIndex < 0 || !m_Program.Operation(operationIndex).IsRunnable
                ? OrchestrationOperationStatus.Dormant : ReadOperationState(operationIndex).Status;

        public ulong ReadGeneration(int operationIndex) =>
            m_Program.Operation(operationIndex).IsRunnable ? ReadOperationState(operationIndex).Generation : 1;

        int OperationFrame(int operationIndex) => m_Frames.OwnerFrame(m_FrameSlot,
            m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex);

        ref OrchestrationOperationState ReadOperationState(int operationIndex) =>
            ref m_Frames.Operations(OperationFrame(operationIndex))[operationIndex];

        void SelectExecutionFrame(int frameSlot)
        {
            m_FrameSlot = frameSlot;
            m_Operations = m_Frames.Operations(frameSlot);
            m_Values = m_Frames.Values(frameSlot);
            m_Blackboards.SelectFrame(frameSlot);
            m_TimelineDispatcher.SelectExecutionFrame(frameSlot, ref m_Context);
            Array.Clear(m_Workspace.CachedObservationWritten, 0, m_Workspace.CachedObservationWritten.Length);
        }

        OrchestrationOperationResult Tick(int operationIndex, int inputPortId = 0)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            if (!operation.IsRunnable)
                return Execute(operationIndex, inputPortId);
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            if (state.Status == OrchestrationOperationStatus.Stopping)
                return OrchestrationOperationResult.Running;
            bool entering = state.Status != OrchestrationOperationStatus.Running;
            if (entering)
            {
                ResetOperation(operationIndex);
                state.ChildCursor = operation.Code == OrchestrationOperationCode.Selector ? -1 : 0;
                state.Generation = checked(state.Generation + 1);
                InvokeHooks(operationIndex, OrchestrationLifecycleHook.OperationActivated);
                state.Status = OrchestrationOperationStatus.Running;
                TraceOperation(OrchestrationTraceKind.Enter, operationIndex);
            }
            else
                TraceOperation(OrchestrationTraceKind.Running, operationIndex);
            OrchestrationOperationResult result = Execute(operationIndex, inputPortId);
            if (result != OrchestrationOperationResult.Running)
            {
                state.Status = result == OrchestrationOperationResult.Success
                    ? OrchestrationOperationStatus.Success : OrchestrationOperationStatus.Failure;
                TraceOperation(OrchestrationTraceKind.Complete, operationIndex, result);
            }
            return result;
        }

        OrchestrationOperationResult TickPersistent(int operationIndex)
        {
            OrchestrationOperationStatus status = ReadStatus(operationIndex);
            if (status == OrchestrationOperationStatus.Success)
                return OrchestrationOperationResult.Success;
            if (status == OrchestrationOperationStatus.Failure)
                return OrchestrationOperationResult.Failure;
            return Tick(operationIndex);
        }

        public OrchestrationStopStatus RequestStop(int operationIndex, OrchestrationStopContext context)
        {
            BeginInvocation();
            try
            {
                m_CurrentOperationIndex = operationIndex;
                if (operationIndex < 0 || !m_Program.Operation(operationIndex).IsRunnable)
                    return OrchestrationStopStatus.Completed;
                ref OrchestrationOperationState state = ref m_Operations[operationIndex];
                if (state.Status == OrchestrationOperationStatus.Dormant ||
                    state.Status == OrchestrationOperationStatus.Success ||
                    state.Status == OrchestrationOperationStatus.Failure)
                {
                    ResetOperation(operationIndex);
                    return OrchestrationStopStatus.Completed;
                }
                if (state.Status == OrchestrationOperationStatus.Running)
                {
                    state.Status = OrchestrationOperationStatus.Stopping;
                    WriteStopContext(operationIndex, context);
                    TraceOperation(OrchestrationTraceKind.StopRequested, operationIndex,
                        stopCause: context.Cause);
                    if (TryRequestLeafStop(operationIndex, context, out OrchestrationStopStatus requested))
                    {
                        if (requested != OrchestrationStopStatus.Running)
                        {
                            TraceOperation(OrchestrationTraceKind.Stopped, operationIndex,
                                requested == OrchestrationStopStatus.Failed ? OrchestrationOperationResult.Failure : OrchestrationOperationResult.Success,
                                context.Cause);
                            ResetOperation(operationIndex);
                        }
                        return requested;
                    }
                }
                else if (context.IsValid)
                    WriteStopContext(operationIndex, context);
                return ContinueStop(operationIndex);
            }
            finally
            {
                m_EntryDepth--;
            }
        }

        public OrchestrationStopStatus ContinueStop(int operationIndex)
        {
            BeginInvocation();
            try
            {
                if (!IsStopping(operationIndex))
                    return OrchestrationStopStatus.Completed;
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                OrchestrationStopContext context = ReadStopContext(operationIndex);
                OrchestrationStopStatus status;
                switch (operation.Code)
                {
                    case OrchestrationOperationCode.State:
                        status = ContinueStateStop(operationIndex, context);
                        break;
                    case OrchestrationOperationCode.StateMachine:
                        status = ContinueStateMachineStop(operationIndex, context);
                        break;
                    case OrchestrationOperationCode.Timeline:
                        status = ContinueLeafStop(operationIndex, context);
                        break;
                    case OrchestrationOperationCode.Extension:
                        status = (m_Bindings.Contract(operation.ExtensionBindingIndex).Invocations &
                            OrchestrationExtensionInvocationKind.ContinueStop) != 0
                            ? ContinueLeafStop(operationIndex, context)
                            : ContinueDirectChildStop(operationIndex, context, OrchestrationFlowKind.Child);
                        break;
                    case OrchestrationOperationCode.SubGraph:
                        status = ContinueDirectChildStop(operationIndex, context, OrchestrationFlowKind.Enter);
                        break;
                    default:
                        status = ContinueDirectChildStop(operationIndex, context, OrchestrationFlowKind.Child);
                        break;
                }
                if (status == OrchestrationStopStatus.Running)
                    return status;
                TraceOperation(OrchestrationTraceKind.Stopped, operationIndex,
                    status == OrchestrationStopStatus.Failed ? OrchestrationOperationResult.Failure : OrchestrationOperationResult.Success,
                    context.Cause);
                ResetOperation(operationIndex);
                return status;
            }
            finally
            {
                m_EntryDepth--;
            }
        }

        public void ForceStop(int operationIndex, OrchestrationStopContext context)
        {
            BeginInvocation();
            bool ownsVisited = m_ForceStopDepth == 0;
            m_ForceStopDepth++;
            try
            {
                ForceStopCore(operationIndex, context);
            }
            finally
            {
                m_EntryDepth--;
                m_ForceStopDepth--;
                if (ownsVisited)
                    Array.Clear(m_Workspace.ForceStopVisited, 0, m_Workspace.ForceStopVisited.Length);
            }
        }

        void ForceStopCore(int operationIndex, OrchestrationStopContext context)
        {
            if (operationIndex < 0)
                return;
            int visitedIndex = checked(m_FrameSlot * m_Program.OperationCount + operationIndex);
            if (m_Workspace.ForceStopVisited[visitedIndex])
                return;
            m_Workspace.ForceStopVisited[visitedIndex] = true;
            m_CurrentOperationIndex = operationIndex;
            bool active = IsActive(operationIndex);
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            switch (operation.Code)
            {
                case OrchestrationOperationCode.Timeline:
                    if (m_Operations[operationIndex].TimelineRuntimeHandle != 0)
                        ForceStopLeaf(operationIndex, context);
                    break;
                case OrchestrationOperationCode.Extension:
                    if ((m_Bindings.Contract(operation.ExtensionBindingIndex).Invocations &
                         OrchestrationExtensionInvocationKind.ForceStop) != 0)
                        ForceStopLeaf(operationIndex, context);
                    ForceStopChildren(operationIndex, context);
                    break;
                case OrchestrationOperationCode.StateMachine:
                    ForceStopStateMachine(operationIndex, context);
                    break;
                case OrchestrationOperationCode.State:
                    ForceStopState(operationIndex, context);
                    break;
                default:
                    ForceStopChildren(operationIndex, context);
                    break;
            }
            if (active)
                TraceOperation(OrchestrationTraceKind.ForceStopped, operationIndex, stopCause: context.Cause);
            ResetOperation(operationIndex);
        }

        void ForceStopChildren(int operationIndex, OrchestrationStopContext context)
        {
            OrchestrationFlowKind kind = m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.SubGraph
                ? OrchestrationFlowKind.Enter : OrchestrationFlowKind.Child;
            foreach (int edgeIndex in m_Program.Outgoing(operationIndex, kind))
            {
                int child = m_Program.Edge(edgeIndex).TargetOperationIndex;
                if (IsActive(child))
                    ForceStopCore(child, context);
            }
        }

        OrchestrationStopStatus ContinueDirectChildStop(int operationIndex,
            OrchestrationStopContext context, OrchestrationFlowKind kind)
        {
            OrchestrationStopStatus aggregate = OrchestrationStopStatus.Completed;
            foreach (int edgeIndex in m_Program.Outgoing(operationIndex, kind))
            {
                int child = m_Program.Edge(edgeIndex).TargetOperationIndex;
                if (!IsActive(child))
                    continue;
                OrchestrationStopStatus status = RequestStop(child, context);
                if (status == OrchestrationStopStatus.Failed)
                    return status;
                if (status == OrchestrationStopStatus.Running)
                    aggregate = status;
            }
            return aggregate;
        }

        void ResetOperation(int operationIndex)
        {
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            ulong generation = state.Generation;
            state = default;
            state.Generation = generation;
            state.StopSourceOperationIndex = -1;
            state.StopReplacementOperationIndex = -1;
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            if (operation.OutputSlotOffset >= 0)
                for (int i = 0; i < operation.OutputCount; i++)
                    m_Values[operation.OutputSlotOffset + i] = m_Program.DefaultValue(operation.OutputSlotOffset + i);
            if (operation.Code == OrchestrationOperationCode.SubGraph)
            {
                OrchestrationGraphCall call = m_Program.GraphCall(operation.GraphCallIndex);
                foreach (ref readonly OrchestrationGraphParameter parameter in call.Inputs.Span)
                    m_Values[parameter.ValueSlot] = m_Program.DefaultValue(parameter.ValueSlot);
                foreach (ref readonly OrchestrationGraphParameter parameter in call.Outputs.Span)
                    m_Values[parameter.ValueSlot] = m_Program.DefaultValue(parameter.ValueSlot);
            }
        }

        OrchestrationStopContext ReadStopContext(int operationIndex)
        {
            ref readonly OrchestrationOperationState state = ref m_Operations[operationIndex];
            return new OrchestrationStopContext(state.StopCause,
                state.StopSourceOperationIndex, state.StopReplacementOperationIndex);
        }

        void WriteStopContext(int operationIndex, OrchestrationStopContext context)
        {
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            state.StopCause = context.Cause;
            state.StopSourceOperationIndex = context.SourceOperationIndex;
            state.StopReplacementOperationIndex = context.ReplacementOperationIndex;
        }

        void ClearStopContext(int operationIndex)
        {
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            state.StopCause = 0;
            state.StopSourceOperationIndex = -1;
            state.StopReplacementOperationIndex = -1;
        }

        OrchestrationOperationResult Execute(int operationIndex, int inputPortId)
        {
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            switch (operation.Code)
            {
                case OrchestrationOperationCode.Root:
                    return TickRoot(operationIndex);
                case OrchestrationOperationCode.StateOnEnter:
                case OrchestrationOperationCode.StateOnExit:
                case OrchestrationOperationCode.TimelineEnter:
                    return TickSingleChild(operationIndex, OrchestrationFlowKind.Child);
                case OrchestrationOperationCode.Loop:
                    return TickLoop(operationIndex);
                case OrchestrationOperationCode.Parallel:
                    return TickParallel(operationIndex);
                case OrchestrationOperationCode.Sequence:
                    return m_Contract == OrchestrationExecutionContract.Complete
                        ? TickChildren(operationIndex) : TickSequence(operationIndex);
                case OrchestrationOperationCode.Selector:
                    return TickSelector(operationIndex);
                case OrchestrationOperationCode.Succeed:
                    return OrchestrationOperationResult.Success;
                case OrchestrationOperationCode.SubGraph:
                    return TickGraphCall(operationIndex);
                case OrchestrationOperationCode.StateMachine:
                    return TickStateMachine(operationIndex);
                case OrchestrationOperationCode.State:
                    return TickState(operationIndex);
                case OrchestrationOperationCode.Branch:
                    return TickBranch(operationIndex);
                case OrchestrationOperationCode.Cycle:
                    return TickCycle(operationIndex, inputPortId);
                default:
                    OrchestrationOperationResult result = ExecuteCoreLeaf(operationIndex);
                    return result == OrchestrationOperationResult.Success && m_Contract == OrchestrationExecutionContract.Complete
                        ? TickChildren(operationIndex) : result;
            }
        }

        OrchestrationOperationResult TickSingleChild(int operationIndex, OrchestrationFlowKind kind)
        {
            ReadOnlySpan<int> children = m_Program.Outgoing(operationIndex, kind);
            if (children.Length > 1)
                throw new InvalidOperationException($"Single-child operation '{operationIndex}' has '{children.Length}' child edges.");
            if (children.Length == 0)
                return OrchestrationOperationResult.Success;
            int edgeIndex = children[0];
            if (!EvaluateCondition(edgeIndex))
                return OrchestrationOperationResult.Failure;
            OrchestrationOperationResult result = TickEdge(edgeIndex);
            return result == OrchestrationOperationResult.Running
                ? Wait(operationIndex, m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.SubGraph
                    ? OrchestrationWaitReason.SubgraphCompletion : OrchestrationWaitReason.ChildCompletion)
                : result;
        }

        OrchestrationOperationResult TickChildren(int operationIndex)
        {
            foreach (int edgeIndex in m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child))
            {
                OrchestrationOperationResult result = TickEdge(edgeIndex);
                if (result != OrchestrationOperationResult.Success)
                    return result;
            }
            return OrchestrationOperationResult.Success;
        }

        OrchestrationOperationResult TickOutput(int operationIndex, int portId)
        {
            foreach (int edgeIndex in m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child))
            {
                if (m_Program.Edge(edgeIndex).SourcePortId != portId)
                    continue;
                OrchestrationOperationResult result = TickEdge(edgeIndex);
                if (result != OrchestrationOperationResult.Success)
                    return result;
            }
            return OrchestrationOperationResult.Success;
        }

        OrchestrationOperationResult TickBranch(int operationIndex)
        {
            int depth = ReadInputs(operationIndex, out int count);
            bool condition = default(TValues).Boolean(in m_Workspace.Inputs[depth][0]);
            ReleaseInputs();
            OrchestrationOperationResult result = TickOutput(operationIndex,
                condition ? m_Program.Ports.BranchTrue : m_Program.Ports.BranchFalse);
            return result == OrchestrationOperationResult.Success
                ? TickOutput(operationIndex, m_Program.Ports.BranchThen) : result;
        }

        OrchestrationOperationResult TickCycle(int operationIndex, int inputPortId)
        {
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            if (inputPortId == m_Program.Ports.CycleReset)
            {
                state.ChildCursor = 0;
                return OrchestrationOperationResult.Success;
            }
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            int selected = (operation.Integer0 + state.ChildCursor) % operation.Integer1;
            foreach (int edgeIndex in m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child))
            {
                if (m_Program.Edge(edgeIndex).Priority != selected)
                    continue;
                OrchestrationOperationResult result = TickEdge(edgeIndex);
                if (result != OrchestrationOperationResult.Success)
                    return result;
            }
            state.ChildCursor = (state.ChildCursor + 1) % operation.Integer1;
            return OrchestrationOperationResult.Success;
        }

        OrchestrationOperationResult TickEdge(int edgeIndex)
        {
            TraceEdge(edgeIndex, true, true);
            ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(edgeIndex);
            return Tick(edge.TargetOperationIndex, edge.TargetPortId);
        }

        bool EvaluateCondition(int edgeIndex)
        {
            ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(edgeIndex);
            bool result = true;
            if (edge.HasCondition)
            {
                TValue value = Evaluate(edge.ConditionOperationIndex);
                result = ToBoolean(in value);
                TraceEdge(edgeIndex, false, result);
            }
            return result;
        }

        OrchestrationOperationResult Wait(int operationIndex, OrchestrationWaitReason reason)
        {
            TraceOperation(OrchestrationTraceKind.Waiting, operationIndex,
                OrchestrationOperationResult.Running, waitReason: reason);
            return OrchestrationOperationResult.Running;
        }

        void TraceOperation(OrchestrationTraceKind kind, int operationIndex,
            OrchestrationOperationResult result = default, uint stopCause = 0,
            OrchestrationWaitReason waitReason = default, int valueSlot = -1)
        {
            if (!m_Input.CaptureTrace || IsPredictiveEvaluation)
                return;
            ref OrchestrationTraceEvent<TValue> item = ref m_Workspace.TraceEvents[m_TraceCount++];
            item = default;
            item.Kind = kind;
            item.OperationIndex = operationIndex;
            item.EdgeIndex = -1;
            item.Generation = ReadGeneration(operationIndex);
            item.StateFrameIndex = m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.State
                ? CaptureStatePath(operationIndex)
                : m_StateDepth == 0 ? -1 : m_Workspace.States[m_StateDepth - 1].TraceFrameIndex;
            item.Result = result;
            item.StopCause = stopCause;
            item.WaitReason = waitReason;
            item.ValueSlot = valueSlot;
            if (kind == OrchestrationTraceKind.Blackboard)
            {
                item.Value = m_Blackboards.Value(valueSlot);
                item.Blackboard = m_Blackboards.Metadata(valueSlot);
            }
        }

        void TraceEdge(int edgeIndex, bool selected, bool passed)
        {
            if (!m_Input.CaptureTrace || IsPredictiveEvaluation)
                return;
            int operationIndex = m_Program.Edge(edgeIndex).SourceOperationIndex;
            m_Workspace.TraceEvents[m_TraceCount++] = new OrchestrationTraceEvent<TValue>
            {
                Kind = OrchestrationTraceKind.Edge,
                OperationIndex = operationIndex,
                EdgeIndex = edgeIndex,
                Generation = ReadGeneration(operationIndex),
                StateFrameIndex = m_StateDepth == 0 ? -1 : m_Workspace.States[m_StateDepth - 1].TraceFrameIndex,
                Selected = selected,
                Passed = passed,
                ValueSlot = -1
            };
        }
    }
}
