using System;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TNumerics, THost>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where THost : struct, IOrchestrationHost<TValue>
    {
        readonly OrchestrationProgramView<TValue> m_Program;
        readonly OrchestrationExecutionScratch<TValue> m_Scratch;
        readonly Span<TValue> m_InputValues;
        readonly Span<TValue> m_OutputValues;
        readonly ReadOnlySpan<OrchestrationRange> m_InputRows;
        readonly ReadOnlySpan<OrchestrationRange> m_OutputRows;
        readonly Span<TValue> m_ScheduledValues;
        readonly Span<OrchestrationScheduledPrediction> m_ScheduledPredictions;
        readonly Span<OrchestrationGraphCallContext> m_GraphCalls;
        readonly Span<OrchestrationStateContext> m_States;
        readonly Span<OrchestrationStateAncestor> m_StateAncestors;
        readonly ReadOnlySpan<OrchestrationValueScheduleInstruction> m_ValueInstructions;
        readonly ReadOnlySpan<int> m_ValueArgumentSlots;
        readonly ReadOnlySpan<OrchestrationRange> m_ValueArgumentLists;
        readonly ReadOnlySpan<OrchestrationValueScheduleEntry> m_ValueEntries;
        readonly ReadOnlySpan<OrchestrationValueScheduleEntry> m_InputEntries;
        readonly ReadOnlySpan<OrchestrationValueScheduleEntry> m_TransitionEntries;
        readonly ReadOnlySpan<OrchestrationValueScheduleEntry> m_ChildSelectionEntries;
        readonly ReadOnlySpan<OrchestrationValueScheduleEntry> m_SelectorAbortEntries;
        readonly ReadOnlySpan<OrchestrationValueScheduleEntry> m_BooleanEntries;
        readonly ReadOnlySpan<OrchestrationValueOperation> m_ValueOperations;
        readonly ReadOnlySpan<int> m_StateContexts;
        readonly ReadOnlySpan<ushort> m_OperationIndices;
        readonly ReadOnlySpan<OrchestrationValueStorageSlot> m_ValueStorage;
        readonly OrchestrationExecutionFrames<TValue> m_Frames;
        Span<OrchestrationOperationState> m_Operations;
        Span<TValue> m_SavedOutputs;
        OrchestrationBlackboardStorageView<TValue, THost> m_Blackboards;
        int m_FrameSlot;
        readonly OrchestrationFrameInput<TValue> m_Input;
        readonly OrchestrationIdentityOrdinalView m_Identities;
        THost m_Host;
        readonly OrchestrationStateView m_TimelineState;
        OrchestrationExecutionContract m_Contract;
        int m_EntryDepth;
        int m_InputDepth;
        int m_StateDepth;
        OrchestrationForceStopVisits m_ForceStopVisits;
        int m_ForceStopDepth;
        int m_CurrentOperationIndex;
        int m_GraphCallDepth;
        int m_ScheduledPredictionDepth;
        OrchestrationTimelineInvocationFacts m_TimelineInvocation;
        TValue m_TimelineTime;

        internal OrchestrationExecutionCursor(OrchestrationProgramView<TValue> program,
            OrchestrationExecutionScratch<TValue> scratch,
            OrchestrationExecutionFrames<TValue> frames, int frameSlot,
            ReadOnlySpan<int> characterSlots,
            OrchestrationStateView timelineState,
            in OrchestrationFrameInput<TValue> input,
            OrchestrationIdentityOrdinalView identities, OrchestrationExecutionSourceBindingView sourceBinding,
            OrchestrationExecutionSource<TValue> source, THost host)
        {
            m_Program = program;
            m_Scratch = scratch;
            ref OrchestrationExecutionScratchHeader header = ref scratch.Header;
            header.Error = default;
            OrchestrationExecutionScratchLayout layout = header.Layout;
            m_InputValues = scratch.Section<TValue>(layout.InputValues);
            m_OutputValues = scratch.Section<TValue>(layout.OutputValues);
            m_InputRows = program.WorkspaceInputRows;
            m_OutputRows = program.WorkspaceOutputRows;
            m_ScheduledValues = scratch.Section<TValue>(layout.ScheduledValues);
            m_ScheduledPredictions = scratch.Section<OrchestrationScheduledPrediction>(layout.ScheduledPredictions);
            m_GraphCalls = scratch.Section<OrchestrationGraphCallContext>(layout.GraphCalls);
            m_States = scratch.Section<OrchestrationStateContext>(layout.States);
            m_StateAncestors = scratch.Section<OrchestrationStateAncestor>(layout.StateAncestors);
            m_ForceStopVisits = new OrchestrationForceStopVisits(scratch.Section<ulong>(layout.ForceStopVisited),
                scratch.Section<int>(layout.ForceStopNext), program.OperationCount);
            OrchestrationValueScheduleView schedule = program.ValueSchedule;
            m_ValueInstructions = schedule.Instructions;
            m_ValueArgumentSlots = schedule.ArgumentSlots;
            m_ValueArgumentLists = schedule.ArgumentLists;
            m_ValueEntries = schedule.ValueEntries;
            m_InputEntries = schedule.InputEntries;
            m_TransitionEntries = schedule.TransitionEntries;
            m_ChildSelectionEntries = schedule.ChildSelectionEntries;
            m_SelectorAbortEntries = schedule.SelectorAbortEntries;
            m_BooleanEntries = schedule.BooleanEntries;
            m_ValueOperations = schedule.ValueOperations;
            m_StateContexts = schedule.StateContexts;
            OrchestrationProgramStorageView storage = program.ExecutionStorage;
            m_OperationIndices = storage.OperationIndices;
            m_ValueStorage = storage.Values;
            m_Frames = frames;
            m_FrameSlot = frameSlot;
            m_Blackboards = new OrchestrationBlackboardStorageView<TValue, THost>(program, frames, frameSlot,
                characterSlots, sourceBinding, source);
            m_Operations = frames.Payload<OrchestrationOperationState>(frameSlot,
                OrchestrationStateSegmentRole.Operations, m_Blackboards.ProgramIndex);
            m_SavedOutputs = frames.Payload<TValue>(frameSlot,
                OrchestrationStateSegmentRole.SavedOutputs, m_Blackboards.ProgramIndex);
            m_Input = input;
            m_Identities = identities;
            m_Host = host;
            m_TimelineState = timelineState;
            m_Contract = OrchestrationExecutionContract.Resumable;
            m_EntryDepth = 0;
            m_InputDepth = 0;
            m_StateDepth = 0;
            m_ForceStopDepth = 0;
            m_CurrentOperationIndex = -1;
            m_GraphCallDepth = 0;
            m_ScheduledPredictionDepth = 0;
            m_TimelineInvocation = default;
            m_TimelineTime = default;
            m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.Initialize, -1, frameSlot);
        }

        public int CurrentOperationIndex => m_CurrentOperationIndex;
        public int ExecutionFrameIndex => m_FrameSlot;

        // 诊断实现读取的执行现场；诊断只经插头回调进入，不改变推进语义。
        internal OrchestrationProgramView<TValue> Program => m_Program;
        internal OrchestrationFrameInput<TValue> Input => m_Input;
        internal OrchestrationTimelineInvocationFacts TimelineInvocation => m_TimelineInvocation;
        internal int FrameCapacity => m_Frames.Capacity;
        internal ref readonly OrchestrationExecutionFrameState FrameHeader(int frameSlot) => ref m_Frames.Header(frameSlot);
        internal int CurrentStateTraceFrame => m_StateDepth == 0 ? -1 : m_States[m_StateDepth - 1].TraceFrameIndex;
        internal int ChildCursor(int operationIndex) => OperationState(operationIndex).ChildCursor;
        internal bool IsBorrowedOperation(int operationIndex) => m_Blackboards.IsBorrowedOperation(operationIndex);
        internal int ExecutionFrameOf(int operationIndex) => OperationFrame(operationIndex);
        internal void ReadBlackboardValue(int valueSlot, ref TValue destination) => m_Blackboards.ReadValue(valueSlot, ref destination);
        internal OrchestrationBlackboardMetadata ReadBlackboardMetadata(int valueSlot) => m_Blackboards.ReadMetadata(valueSlot);
        internal void SelectDiagnosticFrame(int frameSlot) => SelectExecutionFrame(frameSlot);

        // 只有调度预测会压入带 RootCompleted 覆盖的状态，且与预测深度成对进出。
        public bool IsPredictiveEvaluation => m_ScheduledPredictionDepth != 0;

        void BeginInvocation()
        {
            if (m_EntryDepth == 0)
            {
                ref OrchestrationExecutionScratchHeader header = ref m_Scratch.Header;
                header.OutputRequestCount = 0;
                header.OutputValueCount = 0;
                m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.BeginInvocation, -1, m_FrameSlot);
            }
            m_EntryDepth++;
        }

        public OrchestrationOperationResult Advance(OrchestrationExecutionEntry entry)
        {
            OrchestrationExecutionContract previous = m_Contract;
            m_Contract = entry.Contract;
            int traceFrame = m_FrameSlot;
            m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.EnterEntry,
                entry.OperationIndex, traceFrame);
            BeginInvocation();
            try
            {
                OrchestrationOperationResult result = Tick(entry.OperationIndex, entry.InputPortId);
                if (entry.Contract == OrchestrationExecutionContract.Complete && result == OrchestrationOperationResult.Running)
                    Fail(OrchestrationExecutionErrorCode.CompleteEntryWaiting, entry.OperationIndex);
                return result;
            }
            finally
            {
                m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.LeaveEntry,
                    entry.OperationIndex, traceFrame);
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
                    Fail(OrchestrationExecutionErrorCode.CompleteOutputWaiting, operationIndex);
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

        public ReadOnlySpan<OrchestrationExecutionFrameState> ReadExecutionFrame(int operationIndex) =>
            m_Blackboards.ReadExecutionFrame(operationIndex);

        public OrchestrationExecutionSourceReader<TValue> ReadExecutionSource() => m_Blackboards.Reader;

        int OperationFrame(int operationIndex) => m_Blackboards.OperationFrame(operationIndex);

        ref readonly OrchestrationOperationState ReadOperationState(int operationIndex) =>
            ref m_Blackboards.ReadOperation(operationIndex);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ref OrchestrationOperationState OperationState(int operationIndex) =>
            ref m_Operations[m_OperationIndices[operationIndex]];

        Span<TValue> SavedOutputs(in OrchestrationOperation operation) =>
            m_SavedOutputs.Slice(m_ValueStorage[operation.OutputSlotOffset].SavedOutputIndex,
                operation.OutputCount);

        void SelectExecutionFrame(int frameSlot)
        {
            m_FrameSlot = frameSlot;
            m_Operations = m_Frames.Payload<OrchestrationOperationState>(frameSlot,
                OrchestrationStateSegmentRole.Operations, m_Blackboards.ProgramIndex);
            m_SavedOutputs = m_Frames.Payload<TValue>(frameSlot,
                OrchestrationStateSegmentRole.SavedOutputs, m_Blackboards.ProgramIndex);
            m_Blackboards.SelectFrame(frameSlot);
            m_Host.SelectExecutionFrame(frameSlot);
        }

        OrchestrationOperationResult Tick(int operationIndex, int inputPortId = 0)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            if (!operation.IsRunnable)
                return Execute(operationIndex, inputPortId);
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
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
                ref OrchestrationOperationState state = ref OperationState(operationIndex);
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
                    default:
                        bool continuesStop = operation.Code == OrchestrationOperationCode.Extension &&
                            (m_Host.ExecutionContract(m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex).Invocations &
                                OrchestrationExtensionInvocationKind.ContinueStop) != 0;
                        status = OrchestrationStopRoute.OrdinaryStopUsesChildren(operation.Code, continuesStop)
                            ? ContinueDirectChildStop(operationIndex, context, OrchestrationStopRoute.ChildFlowKind(operation.Code))
                            : ContinueLeafStop(operationIndex, context);
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
                    m_ForceStopVisits.Clear();
            }
        }

        void ForceStopCore(int operationIndex, OrchestrationStopContext context)
        {
            if (operationIndex < 0)
                return;
            if (!m_ForceStopVisits.Visit(m_FrameSlot, operationIndex))
                return;
            m_CurrentOperationIndex = operationIndex;
            bool active = IsActive(operationIndex);
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            switch (operation.Code)
            {
                case OrchestrationOperationCode.Timeline:
                    if (OperationState(operationIndex).TimelineRuntimeHandle != 0)
                        ForceStopLeaf(operationIndex, context);
                    break;
                case OrchestrationOperationCode.Extension:
                    if (active && (m_Host.ExecutionContract(m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex).Invocations &
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
            if (m_OperationIndices[operationIndex] != OrchestrationProgramStorageLayout.NoOperationIndex)
                ResetOperation(operationIndex);
        }

        void ForceStopChildren(int operationIndex, OrchestrationStopContext context)
        {
            OrchestrationFlowKind kind = OrchestrationStopRoute.ChildFlowKind(m_Program.Operation(operationIndex).Code);
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
            RetireOperationOverrides(operationIndex);
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            if (state.TimelineRuntimeHandle != 0)
                m_Host.ReleasePlayback(m_TimelineState, operationIndex, ref state.TimelineRuntimeHandle);
            ulong generation = state.Generation;
            state = default;
            state.Generation = generation;
            state.StopSourceOperationIndex = -1;
            state.StopReplacementOperationIndex = -1;
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            if (operation.OutputSlotOffset >= 0)
                for (int i = 0; i < operation.OutputCount; i++)
                    m_SavedOutputs[m_ValueStorage[operation.OutputSlotOffset + i].SavedOutputIndex] =
                        m_Program.DefaultValue(operation.OutputSlotOffset + i);
            if (operation.Code == OrchestrationOperationCode.SubGraph)
            {
                OrchestrationGraphCallView call = m_Program.GraphCall(m_Program.Payload<OrchestrationGraphCallPayload>(operationIndex).CallIndex);
                foreach (ref readonly OrchestrationGraphParameter parameter in call.Inputs)
                {
                    m_Blackboards.WriteValue(parameter.ValueSlot, in m_Program.DefaultValue(parameter.ValueSlot));
                    ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(parameter.ValueSlot);
                    metadata.Revision = checked(metadata.Revision + 1);
                }
                foreach (ref readonly OrchestrationGraphParameter parameter in call.Outputs)
                {
                    m_Blackboards.WriteValue(parameter.ValueSlot, in m_Program.DefaultValue(parameter.ValueSlot));
                    ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(parameter.ValueSlot);
                    metadata.Revision = checked(metadata.Revision + 1);
                }
            }
        }

        void RetireOperationOverrides(int operationIndex)
            => m_Host.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.RetireOperation,
                operationIndex, m_FrameSlot);

        OrchestrationStopContext ReadStopContext(int operationIndex)
        {
            ref readonly OrchestrationOperationState state = ref OperationState(operationIndex);
            return new OrchestrationStopContext(state.StopCause,
                state.StopSourceOperationIndex, state.StopReplacementOperationIndex);
        }

        void WriteStopContext(int operationIndex, OrchestrationStopContext context)
        {
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            state.StopCause = context.Cause;
            state.StopSourceOperationIndex = context.SourceOperationIndex;
            state.StopReplacementOperationIndex = context.ReplacementOperationIndex;
        }

        void ClearStopContext(int operationIndex)
        {
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
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
            bool condition = default(TNumerics).Boolean(in InputRow(depth)[0]);
            ReleaseInputs();
            OrchestrationOperationResult result = TickOutput(operationIndex,
                condition ? m_Program.Ports.BranchTrue : m_Program.Ports.BranchFalse);
            return result == OrchestrationOperationResult.Success
                ? TickOutput(operationIndex, m_Program.Ports.BranchThen) : result;
        }

        OrchestrationOperationResult TickCycle(int operationIndex, int inputPortId)
        {
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            if (inputPortId == m_Program.Ports.CycleReset)
            {
                state.ChildCursor = 0;
                return OrchestrationOperationResult.Success;
            }
            OrchestrationCyclePayload cycle = m_Program.Payload<OrchestrationCyclePayload>(operationIndex);
            int selected = (cycle.Start + state.ChildCursor) % cycle.Count;
            foreach (int edgeIndex in m_Program.Outgoing(operationIndex, OrchestrationFlowKind.Child))
            {
                if (m_Program.Edge(edgeIndex).Priority != selected)
                    continue;
                OrchestrationOperationResult result = TickEdge(edgeIndex);
                if (result != OrchestrationOperationResult.Success)
                    return result;
            }
            state.ChildCursor = (state.ChildCursor + 1) % cycle.Count;
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
                result = EvaluateBoolean(edge.ConditionOperationIndex);
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

        // 工作区值行：深度来自编译期容量，宽度超出发布容量时写错误记录。
        Span<TValue> InputRow(int depth)
        {
            OrchestrationRange row = m_InputRows[depth];
            return m_InputValues.Slice(row.Offset, row.Count);
        }

        void RequireRowDepth(int operationIndex, int required)
        {
            if (required > m_InputRows.Length)
                Fail(OrchestrationExecutionErrorCode.WorkspaceRowDepth, operationIndex,
                    required: required, capacity: m_InputRows.Length);
        }

        Span<TValue> BorrowInputs(int operationIndex, int depth, int count)
        {
            OrchestrationRange row = m_InputRows[depth];
            if (count > row.Count)
                Fail(OrchestrationExecutionErrorCode.WorkspaceInputWidth, operationIndex, depth,
                    aux: m_InputRows.Length, required: count, capacity: row.Count);
            return m_InputValues.Slice(row.Offset, count);
        }

        Span<TValue> BorrowOutputs(int operationIndex, int depth, int count)
        {
            OrchestrationRange row = m_OutputRows[depth];
            if (count > row.Count)
                Fail(OrchestrationExecutionErrorCode.WorkspaceOutputWidth, operationIndex, depth,
                    aux: m_OutputRows.Length, required: count, capacity: row.Count);
            return m_OutputValues.Slice(row.Offset, count);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void Fail(OrchestrationExecutionErrorCode code, int operationIndex, int slot = 0, int detail = 0,
            int aux = 0, long required = 0, long capacity = 0, ulong identity = 0)
        {
            m_Scratch.Header.Error = new OrchestrationExecutionError
            {
                Code = code,
                OperationIndex = operationIndex,
                Slot = slot,
                Detail = detail,
                Aux = aux,
                Required = required,
                Capacity = capacity,
                Identity = identity
            };
            OrchestrationExecutionErrorText.Throw(code);
        }

        void TraceOperation(OrchestrationTraceKind kind, int operationIndex,
            OrchestrationOperationResult result = default, uint stopCause = 0,
            OrchestrationWaitReason waitReason = default, int valueSlot = -1)
            => m_Host.Trace(ref this, kind, operationIndex, -1, result,
                stopCause, waitReason, valueSlot, false, false, default);

        void TraceEdge(int edgeIndex, bool selected, bool passed, int sourceStateOperation = -1)
            => m_Host.Trace(ref this, OrchestrationTraceKind.Edge, sourceStateOperation,
                edgeIndex, default, 0, default, -1, selected, passed, default);
    }
}
