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
            TVector3, TYaw, TQuaternion, TMath, TValues, TContext> m_TimelineDispatcher;
        readonly IOrchestrationTimelineClipExitDispatcher<TContext> m_ClipExitDispatcher;
        readonly OrchestrationStateView m_TimelineState;
        OrchestrationExecutionContract m_Contract;
        int m_EntryDepth;
        int m_InputDepth;
        int m_StateDepth;
        int m_ForceStopDepth;
        int m_TraceCount;
        int m_StateFrameCount;
        int m_TraceContextCount;
        int m_TraceEntryDepth;
        int m_CurrentOperationIndex;
        int m_GraphCallDepth;
        int m_ScheduledPredictionDepth;
        OrchestrationTimelineInvocationFacts m_TimelineInvocation;
        TValue m_TimelineTime;

        internal OrchestrationExecutionCursor(OrchestrationProgram<TValue> program,
            OrchestrationExecutionWorkspace<TValue> workspace,
            IOrchestrationExtensionBindings<TValue, TContext> bindings,
            OrchestrationExecutionFrames<TValue> frames, int frameSlot,
            Span<TValue> characterValues, Span<OrchestrationBlackboardMetadata> characterBlackboards,
            ReadOnlySpan<int> characterSlots,
            IOrchestrationTimelineDispatcher<TValue, TScalar, TVector2,
                TVector3, TYaw, TQuaternion, TMath, TValues, TContext> timelineDispatcher,
            IOrchestrationTimelineClipExitDispatcher<TContext> clipExitDispatcher,
            OrchestrationStateView timelineState,
            in OrchestrationFrameInput<TValue> input,
            in OrchestrationIdentityOrdinalView identities,
            in TContext context, OrchestrationExecutionSourceBinding<TValue> sourceBinding,
            OrchestrationExecutionSource<TValue> source)
        {
            m_Program = program;
            m_Workspace = workspace;
            m_Bindings = bindings;
            m_Frames = frames;
            m_FrameSlot = frameSlot;
            m_Operations = frames.Operations(frameSlot);
            m_Values = frames.Values(frameSlot);
            m_Blackboards = new OrchestrationBlackboardStorageView<TValue>(program, frames, frameSlot,
                characterValues, characterBlackboards, characterSlots, sourceBinding, source);
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
            m_TraceContextCount = 0;
            m_TraceEntryDepth = 0;
            m_CurrentOperationIndex = -1;
            m_GraphCallDepth = 0;
            m_ScheduledPredictionDepth = 0;
            m_TimelineInvocation = default;
            m_TimelineTime = default;
            default(TDiagnostics).Lifecycle(ref this, OrchestrationDiagnosticLifecycle.Initialize, -1, frameSlot);
        }

        internal void DiagnosticLifecycle(OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
        {
            switch (lifecycle)
            {
                case OrchestrationDiagnosticLifecycle.Initialize:
                    if (m_Input.CaptureValues)
                    {
                        Array.Clear(m_Workspace.InputObservationWritten, 0, m_Workspace.InputObservationWritten.Length);
                        Array.Clear(m_Workspace.OutputObservationWritten, 0, m_Workspace.OutputObservationWritten.Length);
                    }
                    m_Workspace.TraceFrameEntries.AsSpan().Fill(-1);
                    m_Workspace.TraceBorrowedFrameEntries.AsSpan().Fill(-1);
                    break;
                case OrchestrationDiagnosticLifecycle.BeginInvocation:
                    m_TraceCount = 0;
                    m_StateFrameCount = 0;
                    m_TraceContextCount = 0;
                    break;
                case OrchestrationDiagnosticLifecycle.EnterEntry:
                    m_Workspace.TraceFrameEntries[frame] = operation;
                    m_Workspace.TraceEntryScopes[m_TraceEntryDepth++] =
                        new OrchestrationExecutionWorkspace<TValue>.TraceEntryScope(frame, operation);
                    break;
                case OrchestrationDiagnosticLifecycle.LeaveEntry:
                    m_TraceEntryDepth--;
                    m_Workspace.TraceFrameEntries[frame] = operation;
                    break;
                case OrchestrationDiagnosticLifecycle.RetireOperation:
                    RetireDiagnosticOperationOverrides(operation);
                    break;
                case OrchestrationDiagnosticLifecycle.RetireFrame:
                    OrchestrationValueOverride<TValue>.RetireExecutionFrame(m_Input.Overrides, in m_Frames.Header(frame));
                    break;
            }
        }

        public int CurrentOperationIndex => m_CurrentOperationIndex;
        public int ExecutionFrameIndex => m_FrameSlot;
        public ReadOnlySpan<OrchestrationTraceEvent<TValue>> Trace => m_Workspace.TraceEvents.AsSpan(0, m_TraceCount);
        public ReadOnlySpan<OrchestrationStateTraceFrame> StateTraceFrames => m_Workspace.StateFrames.AsSpan(0, m_StateFrameCount);
        public ReadOnlySpan<OrchestrationTraceContext> TraceContexts => m_Workspace.TraceContexts.AsSpan(0, m_TraceContextCount);

        public void BindTraceInvocationScope(int operationIndex, OrchestrationExecutionEntry entry)
        {
            int frame = OperationFrame(operationIndex);
            int[] entries = m_Blackboards.IsBorrowedOperation(operationIndex)
                ? m_Workspace.TraceBorrowedFrameEntries : m_Workspace.TraceFrameEntries;
            entries[frame] = entry.OperationIndex;
        }
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
                default(TDiagnostics).Lifecycle(ref this, OrchestrationDiagnosticLifecycle.BeginInvocation, -1, m_FrameSlot);
            }
            m_EntryDepth++;
        }

        public OrchestrationOperationResult Advance(OrchestrationExecutionEntry entry)
        {
            OrchestrationExecutionContract previous = m_Contract;
            m_Contract = entry.Contract;
            int traceFrame = m_FrameSlot;
            default(TDiagnostics).Lifecycle(ref this, OrchestrationDiagnosticLifecycle.EnterEntry,
                entry.OperationIndex, traceFrame);
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
                default(TDiagnostics).Lifecycle(ref this, OrchestrationDiagnosticLifecycle.LeaveEntry,
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

        public ReadOnlySpan<OrchestrationExecutionFrameState> ReadExecutionFrame(int operationIndex) =>
            m_Blackboards.ReadExecutionFrame(operationIndex);

        public OrchestrationExecutionSourceReader<TValue> ReadExecutionSource() => m_Blackboards.Reader;

        public OrchestrationExecutionOwnerToken ReadExecutionOwner(int operationIndex)
        {
            int invocationIndex = ResolveTraceInvocation(
                m_Workspace.TraceBindings[operationIndex].InvocationGroupIndex, operationIndex, false);
            int invocationOperation = m_Workspace.TraceInvocations[invocationIndex].OwnerOperationIndex;
            int stateOperation = m_Program.Operation(operationIndex).ParentStateOperationIndex;
            ulong generation = ReadInvocationGeneration(invocationIndex, operationIndex, out int generationOwner);
            return OrchestrationExecutionOwnerToken.Create(in ReadExecutionFrame(operationIndex)[0],
                invocationOperation, generation, stateOperation,
                stateOperation < 0 ? 0 : ReadGeneration(stateOperation), generationOwner);
        }

        ulong ReadInvocationGeneration(int invocationIndex, int operationIndex, out int generationOwner)
        {
            while (true)
            {
                ref readonly OrchestrationExecutionWorkspace<TValue>.TraceInvocation invocation =
                    ref m_Workspace.TraceInvocations[invocationIndex];
                if (m_Program.Operation(invocation.OwnerOperationIndex).IsRunnable)
                {
                    generationOwner = invocation.OwnerOperationIndex;
                    return ReadGeneration(invocation.OwnerOperationIndex);
                }
                if (invocation.ParentGroupIndex < 0)
                {
                    generationOwner = -1;
                    return ReadExecutionFrame(operationIndex)[0].Generation;
                }
                invocationIndex = ResolveTraceInvocation(invocation.ParentGroupIndex,
                    invocation.OwnerOperationIndex, false);
            }
        }

        public OrchestrationValueOverrideAddress ReadPortOverrideAddress(int operationIndex,
            int slot, bool input) => new OrchestrationValueOverrideAddress
        {
            Kind = input ? OrchestrationValueOverrideKind.Input : OrchestrationValueOverrideKind.Output,
            Slot = slot,
            Owner = ReadExecutionOwner(operationIndex)
        };

        public bool TrySelectExecutionOwner(int operationIndex, in OrchestrationExecutionOwnerToken owner)
        {
            OrchestrationRange group = m_Workspace.TraceInvocationGroups[
                m_Workspace.TraceBindings[operationIndex].InvocationGroupIndex];
            int invocationIndex = -1;
            for (int index = group.Offset; index < group.Offset + group.Count; index++)
                if (m_Workspace.TraceInvocations[index].OwnerOperationIndex == owner.InvocationOperationIndex)
                {
                    invocationIndex = index;
                    break;
                }
            if (invocationIndex < 0 || !IsInvocationGenerationOwner(invocationIndex, owner.GenerationOwnerOperationIndex))
                return false;
            int stateOperation = m_Program.Operation(operationIndex).ParentStateOperationIndex;
            if (stateOperation != owner.StateOperationIndex)
                return false;
            for (int frame = 0; frame < m_Frames.Capacity; frame++)
            {
                if (!owner.MatchesFrame(in m_Frames.Header(frame)))
                    continue;
                if (frame != m_FrameSlot)
                    SelectExecutionFrame(frame);
                m_Workspace.TraceFrameEntries[frame] =
                    m_Workspace.TraceInvocations[invocationIndex].EntryOperationIndex;
                ulong generation = owner.GenerationOwnerOperationIndex < 0
                    ? ReadExecutionFrame(operationIndex)[0].Generation : ReadGeneration(owner.GenerationOwnerOperationIndex);
                if (generation != owner.InvocationGeneration ||
                    stateOperation >= 0 && ReadGeneration(stateOperation) != owner.StateGeneration)
                    return false;
                return true;
            }
            return false;
        }

        bool IsInvocationGenerationOwner(int invocationIndex, int generationOwner)
        {
            ref readonly OrchestrationExecutionWorkspace<TValue>.TraceInvocation invocation =
                ref m_Workspace.TraceInvocations[invocationIndex];
            if (m_Program.Operation(invocation.OwnerOperationIndex).IsRunnable)
                return invocation.OwnerOperationIndex == generationOwner;
            if (invocation.ParentGroupIndex < 0)
                return generationOwner < 0;
            OrchestrationRange parents = m_Workspace.TraceInvocationGroups[invocation.ParentGroupIndex];
            for (int index = parents.Offset; index < parents.Offset + parents.Count; index++)
                if (IsInvocationGenerationOwner(index, generationOwner))
                    return true;
            return false;
        }

        int OperationFrame(int operationIndex) => m_Blackboards.OperationFrame(operationIndex);

        ref readonly OrchestrationOperationState ReadOperationState(int operationIndex) =>
            ref m_Blackboards.ReadOperation(operationIndex);

        void SelectExecutionFrame(int frameSlot)
        {
            m_FrameSlot = frameSlot;
            m_Operations = m_Frames.Operations(frameSlot);
            m_Values = m_Frames.Values(frameSlot);
            m_Blackboards.SelectFrame(frameSlot);
            m_TimelineDispatcher.SelectExecutionFrame(frameSlot, ref m_Context);
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
                        status = (m_Bindings.Contract(m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex).Invocations &
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
                    if (active && (m_Bindings.Contract(m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex).Invocations &
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
            RetireOperationOverrides(operationIndex);
            ref OrchestrationOperationState state = ref m_Operations[operationIndex];
            if (state.TimelineRuntimeHandle != 0)
                m_TimelineDispatcher.ReleasePlayback(m_TimelineState, operationIndex,
                    ref state.TimelineRuntimeHandle, in m_Context);
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
                OrchestrationGraphCall call = m_Program.GraphCall(m_Program.Payload<OrchestrationGraphCallPayload>(operationIndex).CallIndex);
                foreach (ref readonly OrchestrationGraphParameter parameter in call.Inputs.Span)
                {
                    m_Values[parameter.ValueSlot] = m_Program.DefaultValue(parameter.ValueSlot);
                    ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(parameter.ValueSlot);
                    metadata.Revision = checked(metadata.Revision + 1);
                }
                foreach (ref readonly OrchestrationGraphParameter parameter in call.Outputs.Span)
                {
                    m_Values[parameter.ValueSlot] = m_Program.DefaultValue(parameter.ValueSlot);
                    ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(parameter.ValueSlot);
                    metadata.Revision = checked(metadata.Revision + 1);
                }
            }
        }

        void RetireOperationOverrides(int operationIndex)
            => default(TDiagnostics).Lifecycle(ref this, OrchestrationDiagnosticLifecycle.RetireOperation,
                operationIndex, m_FrameSlot);

        void RetireDiagnosticOperationOverrides(int operationIndex)
        {
            Span<OrchestrationValueOverride<TValue>> overrides = m_Input.Overrides;
            for (int index = 0; index < overrides.Length; index++)
            {
                ref OrchestrationValueOverride<TValue> value = ref overrides[index];
                if (value.Mode == OrchestrationValueOverrideMode.None ||
                    !value.Address.Owner.MatchesFrame(in m_Frames.Header(m_FrameSlot)))
                    continue;
                ref readonly OrchestrationExecutionOwnerToken owner = ref value.Address.Owner;
                if (value.Address.Kind == OrchestrationValueOverrideKind.Blackboard)
                {
                    if (owner.InvocationOperationIndex != operationIndex ||
                        m_Program.Blackboard(value.Address.Slot).Lifetime == OrchestrationBlackboardLifetime.GraphInstance &&
                        !m_Program.Operation(operationIndex).IsRunnable)
                        continue;
                }
                else if (owner.GenerationOwnerOperationIndex != operationIndex && owner.StateOperationIndex != operationIndex)
                    continue;
                value.Retire();
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

        void TraceOperation(OrchestrationTraceKind kind, int operationIndex,
            OrchestrationOperationResult result = default, uint stopCause = 0,
            OrchestrationWaitReason waitReason = default, int valueSlot = -1)
            => default(TDiagnostics).Trace(ref this, kind, operationIndex, -1, result,
                stopCause, waitReason, valueSlot, false, false);

        internal void TraceDiagnosticOperation(OrchestrationTraceKind kind, int operationIndex,
            OrchestrationOperationResult result, uint stopCause,
            OrchestrationWaitReason waitReason, int valueSlot)
        {
            if (!m_Input.CaptureTrace || IsPredictiveEvaluation)
                return;
            ref OrchestrationTraceEvent<TValue> item = ref m_Workspace.TraceEvents[m_TraceCount++];
            item = default;
            item.Kind = kind;
            item.OperationIndex = operationIndex;
            item.EdgeIndex = -1;
            item.Generation = ReadGeneration(operationIndex);
            bool lifecycleOwner = kind is OrchestrationTraceKind.Enter or OrchestrationTraceKind.Running or
                OrchestrationTraceKind.Complete or OrchestrationTraceKind.StopRequested or
                OrchestrationTraceKind.Stopped or OrchestrationTraceKind.ForceStopped;
            item.ContextIndex = CaptureTraceContext(operationIndex, lifecycleOwner);
            item.StateFrameIndex = m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.State
                ? CaptureStatePath(operationIndex, lifecycleOwner)
                : m_StateDepth == 0 ? -1 : m_Workspace.States[m_StateDepth - 1].TraceFrameIndex;
            item.Result = result;
            item.StopCause = stopCause;
            item.WaitReason = waitReason;
            item.ValueSlot = valueSlot;
            if (kind == OrchestrationTraceKind.Blackboard)
            {
                item.Value = m_Blackboards.ReadValue(valueSlot);
                item.Blackboard = m_Blackboards.ReadMetadata(valueSlot);
            }
        }

        void TraceEdge(int edgeIndex, bool selected, bool passed, int sourceStateOperation = -1)
            => default(TDiagnostics).Trace(ref this, OrchestrationTraceKind.Edge, sourceStateOperation,
                edgeIndex, default, 0, default, -1, selected, passed);

        internal void TraceDiagnosticEdge(int edgeIndex, bool selected, bool passed, int sourceStateOperation)
        {
            if (!m_Input.CaptureTrace || IsPredictiveEvaluation)
                return;
            int operationIndex = sourceStateOperation >= 0 ? sourceStateOperation : m_Program.Edge(edgeIndex).SourceOperationIndex;
            ref OrchestrationTraceEvent<TValue> item = ref m_Workspace.TraceEvents[m_TraceCount++];
            item = default;
            item.Kind = OrchestrationTraceKind.Edge;
            item.OperationIndex = operationIndex;
            item.EdgeIndex = edgeIndex;
            item.Generation = ReadGeneration(operationIndex);
            item.ContextIndex = CaptureTraceContext(operationIndex);
            item.StateFrameIndex = m_StateDepth == 0 ? -1 : m_Workspace.States[m_StateDepth - 1].TraceFrameIndex;
            item.Selected = selected;
            item.Passed = passed;
            item.ValueSlot = -1;
        }

        int CaptureTraceContext(int operationIndex, bool lifecycleOwner = false)
        {
            ref readonly OrchestrationExecutionWorkspace<TValue>.TraceBinding binding =
                ref m_Workspace.TraceBindings[operationIndex];
            int invocationIndex = ResolveTraceInvocation(binding.InvocationGroupIndex, operationIndex, lifecycleOwner);
            ref readonly OrchestrationExecutionWorkspace<TValue>.TraceInvocation invocation =
                ref m_Workspace.TraceInvocations[invocationIndex];
            int parentIndex = invocation.ParentGroupIndex < 0 ? -1 :
                ResolveTraceInvocation(invocation.ParentGroupIndex, invocation.OwnerOperationIndex, false);
            ref readonly OrchestrationExecutionFrameState frame = ref ReadExecutionFrame(operationIndex)[0];
            int index = m_TraceContextCount++;
            ref OrchestrationTraceContext context = ref m_Workspace.TraceContexts[index];
            context.SourceIndex = binding.SourceIndex;
            context.InvocationSourceIndex = invocation.SourceIndex;
            context.ParentInvocationSourceIndex = parentIndex < 0 ? -1 :
                m_Workspace.TraceInvocations[parentIndex].SourceIndex;
            context.InvocationOperationIndex = invocation.OwnerOperationIndex;
            context.ParentInvocationOperationIndex = parentIndex < 0 ? -1 :
                m_Workspace.TraceInvocations[parentIndex].OwnerOperationIndex;
            context.ExecutionFrameOwnerOperationIndex = frame.OwnerOperationIndex;
            context.ActionInstanceId = frame.ActionInstanceId;
            context.SkillExecutionGeneration = frame.Generation;
            context.GraphInvocationGeneration = ReadGeneration(invocation.OwnerOperationIndex);
            context.ParentInvocationGeneration = parentIndex < 0 ? 0 :
                ReadGeneration(m_Workspace.TraceInvocations[parentIndex].OwnerOperationIndex);
            context.TimelineStart = frame.TimelineStart;
            context.ClipOrdinal = frame.ClipOrdinal;
            context.Cycle = frame.Cycle;
            context.Marker = frame.Marker;
            return index;
        }

        int ResolveTraceInvocation(int groupIndex, int operationIndex, bool lifecycleOwner)
        {
            OrchestrationRange group = m_Workspace.TraceInvocationGroups[groupIndex];
            if (group.Count == 1)
                return group.Offset;
            if (lifecycleOwner)
            {
                int owner = -1;
                for (int index = group.Offset; index < group.Offset + group.Count; index++)
                    if (m_Workspace.TraceInvocations[index].OwnerOperationIndex == operationIndex)
                    {
                        if (owner >= 0)
                        {
                            owner = -1;
                            break;
                        }
                        owner = index;
                    }
                if (owner >= 0)
                    return owner;
            }
            int frameOwner = m_Workspace.TraceInvocationFrameOwners[groupIndex];
            bool borrowed = m_Blackboards.IsBorrowedOperation(frameOwner);
            int frame = OperationFrame(frameOwner);
            if (!borrowed)
                for (int index = m_TraceEntryDepth - 1; index >= 0; index--)
                {
                    ref readonly OrchestrationExecutionWorkspace<TValue>.TraceEntryScope scope =
                        ref m_Workspace.TraceEntryScopes[index];
                    if (scope.FrameIndex != frame)
                        continue;
                    int selected = FindTraceInvocation(in group, scope.EntryOperationIndex);
                    if (selected >= 0)
                        return selected;
                }
            int entry = (borrowed ? m_Workspace.TraceBorrowedFrameEntries :
                m_Workspace.TraceFrameEntries)[frame];
            int invocation = FindTraceInvocation(in group, entry);
            if (invocation < 0)
                throw new InvalidOperationException($"Trace operation '{operationIndex}' requires its actual invocation entry scope for execution frame '{frame}'.");
            return invocation;
        }

        int FindTraceInvocation(in OrchestrationRange group, int entryOperationIndex)
        {
            for (int index = group.Offset; index < group.Offset + group.Count; index++)
                if (m_Workspace.TraceInvocations[index].EntryOperationIndex == entryOperationIndex)
                    return index;
            return -1;
        }
    }
}
