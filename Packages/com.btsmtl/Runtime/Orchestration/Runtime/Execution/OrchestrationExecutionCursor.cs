using System;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
        where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
        where TStorage : struct, IOrchestrationValueStorage<TValue>
        where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
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
        readonly Span<ulong> m_ForceStopVisited;
        readonly Span<int> m_ForceStopNext;
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
        OrchestrationBlackboardStorageView<TValue, TStorage> m_Blackboards;
        int m_FrameSlot;
        readonly OrchestrationFrameInput<TValue> m_Input;
        readonly OrchestrationIdentityOrdinalView m_Identities;
        TContext m_Context;
        THost m_Host;
        TDiagnostics m_Diagnostics;
        readonly OrchestrationStateView m_TimelineState;
        OrchestrationExecutionContract m_Contract;
        int m_EntryDepth;
        int m_InputDepth;
        int m_StateDepth;
        int m_ForceStopDepth;
        int m_ForceStopVisitedWord;
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
            OrchestrationIdentityOrdinalView identities,
            in TContext context, OrchestrationExecutionSourceBindingView sourceBinding,
            OrchestrationExecutionSource<TValue> source, THost host, TDiagnostics diagnostics)
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
            m_ForceStopVisited = scratch.Section<ulong>(layout.ForceStopVisited);
            m_ForceStopNext = scratch.Section<int>(layout.ForceStopNext);
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
            m_Blackboards = new OrchestrationBlackboardStorageView<TValue, TStorage>(program, frames, frameSlot,
                characterSlots, sourceBinding, source);
            m_Operations = frames.Payload<OrchestrationOperationState>(frameSlot,
                OrchestrationStateSegmentRole.Operations, m_Blackboards.ProgramIndex);
            m_SavedOutputs = frames.Payload<TValue>(frameSlot,
                OrchestrationStateSegmentRole.SavedOutputs, m_Blackboards.ProgramIndex);
            m_Input = input;
            m_Identities = identities;
            m_Context = context;
            m_Host = host;
            m_Diagnostics = diagnostics;
            m_TimelineState = timelineState;
            m_Contract = OrchestrationExecutionContract.Resumable;
            m_EntryDepth = 0;
            m_InputDepth = 0;
            m_StateDepth = 0;
            m_ForceStopDepth = 0;
            m_ForceStopVisitedWord = -1;
            m_CurrentOperationIndex = -1;
            m_GraphCallDepth = 0;
            m_ScheduledPredictionDepth = 0;
            m_TimelineInvocation = default;
            m_TimelineTime = default;
            m_Diagnostics.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.Initialize, -1, frameSlot);
        }

        internal void DiagnosticLifecycle(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
        {
            switch (lifecycle)
            {
                case OrchestrationDiagnosticLifecycle.Initialize:
                    storage.Initialize(m_Input.CaptureValues);
                    break;
                case OrchestrationDiagnosticLifecycle.BeginInvocation:
                    storage.BeginInvocation();
                    break;
                case OrchestrationDiagnosticLifecycle.EnterEntry:
                    storage.TraceFrameEntries[frame] = operation;
                    storage.TraceEntryScopes[storage.TraceEntryDepth++] =
                        new OrchestrationWorkbenchDiagnosticStorage<TValue>.TraceEntryScope(frame, operation);
                    break;
                case OrchestrationDiagnosticLifecycle.LeaveEntry:
                    storage.TraceEntryDepth--;
                    storage.TraceFrameEntries[frame] = operation;
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

        public void BindTraceInvocationScope(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            int operationIndex, OrchestrationExecutionEntry entry)
        {
            int frame = OperationFrame(operationIndex);
            int[] entries = m_Blackboards.IsBorrowedOperation(operationIndex)
                ? storage.TraceBorrowedFrameEntries : storage.TraceFrameEntries;
            entries[frame] = entry.OperationIndex;
        }

        public bool IsPredictiveEvaluation
        {
            get
            {
                for (int i = m_StateDepth - 1; i >= 0; i--)
                    if (m_States[i].HasRootCompletedOverride)
                        return true;
                return false;
            }
        }

        void BeginInvocation()
        {
            if (m_EntryDepth == 0)
            {
                ref OrchestrationExecutionScratchHeader header = ref m_Scratch.Header;
                header.OutputRequestCount = 0;
                header.OutputValueCount = 0;
                m_Diagnostics.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.BeginInvocation, -1, m_FrameSlot);
            }
            m_EntryDepth++;
        }

        public OrchestrationOperationResult Advance(OrchestrationExecutionEntry entry)
        {
            OrchestrationExecutionContract previous = m_Contract;
            m_Contract = entry.Contract;
            int traceFrame = m_FrameSlot;
            m_Diagnostics.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.EnterEntry,
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
                m_Diagnostics.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.LeaveEntry,
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

        public OrchestrationExecutionOwnerToken ReadExecutionOwner(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            int operationIndex)
        {
            int invocationIndex = ResolveTraceInvocation(storage,
                storage.TraceBindings[operationIndex].InvocationGroupIndex, operationIndex, false);
            int invocationOperation = storage.TraceInvocations[invocationIndex].OwnerOperationIndex;
            int stateOperation = m_Program.Operation(operationIndex).ParentStateOperationIndex;
            ulong generation = ReadInvocationGeneration(storage, invocationIndex, operationIndex, out int generationOwner);
            return OrchestrationExecutionOwnerToken.Create(in ReadExecutionFrame(operationIndex)[0],
                invocationOperation, generation, stateOperation,
                stateOperation < 0 ? 0 : ReadGeneration(stateOperation), generationOwner);
        }

        ulong ReadInvocationGeneration(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            int invocationIndex, int operationIndex, out int generationOwner)
        {
            while (true)
            {
                ref readonly OrchestrationWorkbenchDiagnosticStorage<TValue>.TraceInvocation invocation =
                    ref storage.TraceInvocations[invocationIndex];
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
                invocationIndex = ResolveTraceInvocation(storage, invocation.ParentGroupIndex,
                    invocation.OwnerOperationIndex, false);
            }
        }

        public OrchestrationValueOverrideAddress ReadPortOverrideAddress(
            OrchestrationWorkbenchDiagnosticStorage<TValue> storage, int operationIndex,
            int slot, bool input) => new OrchestrationValueOverrideAddress
        {
            Kind = input ? OrchestrationValueOverrideKind.Input : OrchestrationValueOverrideKind.Output,
            Slot = slot,
            Owner = ReadExecutionOwner(storage, operationIndex)
        };

        public bool TrySelectExecutionOwner(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            int operationIndex, in OrchestrationExecutionOwnerToken owner)
        {
            OrchestrationRange group = storage.TraceInvocationGroups[
                storage.TraceBindings[operationIndex].InvocationGroupIndex];
            int invocationIndex = -1;
            for (int index = group.Offset; index < group.Offset + group.Count; index++)
                if (storage.TraceInvocations[index].OwnerOperationIndex == owner.InvocationOperationIndex)
                {
                    invocationIndex = index;
                    break;
                }
            if (invocationIndex < 0 || !IsInvocationGenerationOwner(storage, invocationIndex, owner.GenerationOwnerOperationIndex))
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
                storage.TraceFrameEntries[frame] =
                    storage.TraceInvocations[invocationIndex].EntryOperationIndex;
                ulong generation = owner.GenerationOwnerOperationIndex < 0
                    ? ReadExecutionFrame(operationIndex)[0].Generation : ReadGeneration(owner.GenerationOwnerOperationIndex);
                if (generation != owner.InvocationGeneration ||
                    stateOperation >= 0 && ReadGeneration(stateOperation) != owner.StateGeneration)
                    return false;
                return true;
            }
            return false;
        }

        bool IsInvocationGenerationOwner(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            int invocationIndex, int generationOwner)
        {
            ref readonly OrchestrationWorkbenchDiagnosticStorage<TValue>.TraceInvocation invocation =
                ref storage.TraceInvocations[invocationIndex];
            if (m_Program.Operation(invocation.OwnerOperationIndex).IsRunnable)
                return invocation.OwnerOperationIndex == generationOwner;
            if (invocation.ParentGroupIndex < 0)
                return generationOwner < 0;
            OrchestrationRange parents = storage.TraceInvocationGroups[invocation.ParentGroupIndex];
            for (int index = parents.Offset; index < parents.Offset + parents.Count; index++)
                if (IsInvocationGenerationOwner(storage, index, generationOwner))
                    return true;
            return false;
        }

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
            m_Host.SelectExecutionFrame(frameSlot, ref m_Context);
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
                    case OrchestrationOperationCode.Timeline:
                        status = ContinueLeafStop(operationIndex, context);
                        break;
                    case OrchestrationOperationCode.Extension:
                        status = (m_Host.ExecutionContract(m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex).Invocations &
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
                    ClearForceStopVisits();
            }
        }

        // 访问位按字记账；链表值存 next + 2，零表示未入链，使草稿初值全零即可。
        const int ForceStopListEnd = 1;

        bool VisitForceStop(int frameSlot, int operationIndex)
        {
            int index = checked(frameSlot * m_Program.OperationCount + operationIndex);
            int word = index >> 6;
            ulong mask = 1UL << (index & 63);
            if ((m_ForceStopVisited[word] & mask) != 0)
                return false;
            if (m_ForceStopNext[word] == 0)
            {
                m_ForceStopNext[word] = m_ForceStopVisitedWord < 0 ? ForceStopListEnd : m_ForceStopVisitedWord + 2;
                m_ForceStopVisitedWord = word;
            }
            m_ForceStopVisited[word] |= mask;
            return true;
        }

        void ClearForceStopVisits()
        {
            while (m_ForceStopVisitedWord >= 0)
            {
                int word = m_ForceStopVisitedWord;
                int next = m_ForceStopNext[word];
                m_ForceStopVisitedWord = next == ForceStopListEnd ? -1 : next - 2;
                m_ForceStopNext[word] = 0;
                m_ForceStopVisited[word] = 0;
            }
        }

        void ClearForceStopFrame(int frameSlot)
        {
            int index = checked(frameSlot * m_Program.OperationCount);
            int remaining = m_Program.OperationCount;
            int word = index >> 6;
            int offset = index & 63;
            while (remaining > 0)
            {
                int count = Math.Min(64 - offset, remaining);
                ulong mask = (ulong.MaxValue >> (64 - count)) << offset;
                m_ForceStopVisited[word++] &= ~mask;
                remaining -= count;
                offset = 0;
            }
            // 帧复用只清访问位，块的链表归属保留到最外层停止结束，避免同一块重复入链。
        }

        void ForceStopCore(int operationIndex, OrchestrationStopContext context)
        {
            if (operationIndex < 0)
                return;
            if (!VisitForceStop(m_FrameSlot, operationIndex))
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
            ref OrchestrationOperationState state = ref OperationState(operationIndex);
            if (state.TimelineRuntimeHandle != 0)
                m_Host.ReleasePlayback(m_TimelineState, operationIndex,
                    ref state.TimelineRuntimeHandle, in m_Context);
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
            => m_Diagnostics.Lifecycle(ref this, OrchestrationDiagnosticLifecycle.RetireOperation,
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
            bool condition = default(TValues).Boolean(in InputRow(depth)[0]);
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
            ThrowError(code);
        }

        // Burst 只接受字面量异常文字；完整信息由宿主读取错误记录后组织。
        static void ThrowError(OrchestrationExecutionErrorCode code)
        {
            switch (code)
            {
                case OrchestrationExecutionErrorCode.WorkspaceRowDepth:
                case OrchestrationExecutionErrorCode.WorkspaceInputWidth:
                case OrchestrationExecutionErrorCode.WorkspaceOutputWidth:
                case OrchestrationExecutionErrorCode.WorkspaceStateDepth:
                case OrchestrationExecutionErrorCode.WorkspaceGraphCallDepth:
                    throw new InvalidOperationException("编排工作区容量不足。");
                case OrchestrationExecutionErrorCode.CompleteEntryWaiting:
                    throw new InvalidOperationException("同次完成的图入口不能返回跨次等待。");
                case OrchestrationExecutionErrorCode.CompleteOutputWaiting:
                    throw new InvalidOperationException("同次完成的图输出不能返回跨次等待。");
                case OrchestrationExecutionErrorCode.BlackboardReadOnly:
                    throw new InvalidOperationException("黑板槽是只读配置。");
                case OrchestrationExecutionErrorCode.BlackboardScope:
                    throw new InvalidOperationException("黑板槽声明了不支持的作用域。");
                case OrchestrationExecutionErrorCode.HookFailed:
                    throw new InvalidOperationException("生命周期钩子返回失败。");
                case OrchestrationExecutionErrorCode.HookInput:
                    throw new InvalidOperationException("生命周期钩子输入来源不受支持。");
                case OrchestrationExecutionErrorCode.TimelineForceStop:
                    throw new InvalidOperationException("Timeline 强制停止未完成。");
                case OrchestrationExecutionErrorCode.ActionForceStop:
                    throw new InvalidOperationException("动作强制停止未完成。");
                case OrchestrationExecutionErrorCode.ActionStopResult:
                    throw new InvalidOperationException("动作返回了无效的停止结果。");
                case OrchestrationExecutionErrorCode.OutputBudget:
                    throw new InvalidOperationException("编排输出超出发布预算。");
                case OrchestrationExecutionErrorCode.TimelineInvocation:
                    throw new InvalidOperationException("Timeline 调用失败。");
                case OrchestrationExecutionErrorCode.OperationNotExecutable:
                    throw new InvalidOperationException("操作不可执行。");
                case OrchestrationExecutionErrorCode.ReadOnlyExtension:
                    throw new InvalidOperationException("只读扩展返回失败。");
                case OrchestrationExecutionErrorCode.ValueScheduleInstruction:
                    throw new InvalidOperationException("未知的值调度指令。");
                default:
                    throw new InvalidOperationException("编排推进失败。");
            }
        }

        void TraceOperation(OrchestrationTraceKind kind, int operationIndex,
            OrchestrationOperationResult result = default, uint stopCause = 0,
            OrchestrationWaitReason waitReason = default, int valueSlot = -1)
            => m_Diagnostics.Trace(ref this, kind, operationIndex, -1, result,
                stopCause, waitReason, valueSlot, false, false, default);

        internal void TraceDiagnosticOperation(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            OrchestrationTraceKind kind, int operationIndex,
            OrchestrationOperationResult result, uint stopCause,
            OrchestrationWaitReason waitReason, int valueSlot, in OrchestrationBlackboardSourceContext blackboardSource)
        {
            if (!m_Input.CaptureTrace || IsPredictiveEvaluation)
                return;
            ref OrchestrationTraceEvent<TValue> item = ref storage.TraceEvents[storage.TraceCount++];
            item = default;
            item.Kind = kind;
            item.OperationIndex = operationIndex;
            item.EdgeIndex = -1;
            item.Generation = ReadGeneration(operationIndex);
            bool lifecycleOwner = kind is OrchestrationTraceKind.Enter or OrchestrationTraceKind.Running or
                OrchestrationTraceKind.Complete or OrchestrationTraceKind.StopRequested or
                OrchestrationTraceKind.Stopped or OrchestrationTraceKind.ForceStopped;
            item.ContextIndex = CaptureTraceContext(storage, operationIndex, lifecycleOwner);
            item.StateFrameIndex = m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.State
                ? CaptureStatePath(storage, operationIndex, lifecycleOwner)
                : m_StateDepth == 0 ? -1 : m_States[m_StateDepth - 1].TraceFrameIndex;
            item.Result = result;
            item.StopCause = stopCause;
            item.WaitReason = waitReason;
            item.DetailIndex = kind is OrchestrationTraceKind.LoopIterationEnter or OrchestrationTraceKind.LoopIterationComplete
                ? OperationState(operationIndex).ChildCursor : valueSlot;
            if (kind == OrchestrationTraceKind.Blackboard)
            {
                m_Blackboards.ReadValue(valueSlot, ref item.Value);
                item.Blackboard = m_Blackboards.ReadMetadata(valueSlot);
                item.BlackboardLifetime = m_Program.Blackboard(valueSlot).Lifetime;
                item.BlackboardWriteStamp = new OrchestrationBlackboardWriteStamp
                {
                    SourceOperationIndex = operationIndex,
                    LogicTick = m_Input.Tick,
                    SourceContext = blackboardSource,
                    TimelineOperationIndex = m_TimelineInvocation.HasInvocation
                        ? m_Frames.Header(m_FrameSlot).ParentTimelineOperationIndex : -1,
                    ClipOperationIndex = m_TimelineInvocation.HasInvocation
                        ? m_Frames.Header(m_FrameSlot).OwnerOperationIndex : -1,
                    Cycle = m_TimelineInvocation.HasInvocation ? m_TimelineInvocation.Invocation.Token.Cycle : 0,
                    CommandSequence = m_TimelineInvocation.HasInvocation ? m_TimelineInvocation.Invocation.Token.Sequence : 0
                };
            }
        }

        void TraceEdge(int edgeIndex, bool selected, bool passed, int sourceStateOperation = -1)
            => m_Diagnostics.Trace(ref this, OrchestrationTraceKind.Edge, sourceStateOperation,
                edgeIndex, default, 0, default, -1, selected, passed, default);

        internal void TraceDiagnosticEdge(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            int edgeIndex, bool selected, bool passed, int sourceStateOperation)
        {
            if (!m_Input.CaptureTrace || IsPredictiveEvaluation)
                return;
            int operationIndex = sourceStateOperation >= 0 ? sourceStateOperation : m_Program.Edge(edgeIndex).SourceOperationIndex;
            ref OrchestrationTraceEvent<TValue> item = ref storage.TraceEvents[storage.TraceCount++];
            item = default;
            item.Kind = OrchestrationTraceKind.Edge;
            item.OperationIndex = operationIndex;
            item.EdgeIndex = edgeIndex;
            item.Generation = ReadGeneration(operationIndex);
            item.ContextIndex = CaptureTraceContext(storage, operationIndex);
            item.StateFrameIndex = m_StateDepth == 0 ? -1 : m_States[m_StateDepth - 1].TraceFrameIndex;
            item.Selected = selected;
            item.Passed = passed;
            item.DetailIndex = -1;
        }

        int CaptureTraceContext(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            int operationIndex, bool lifecycleOwner = false)
        {
            ref readonly OrchestrationWorkbenchDiagnosticStorage<TValue>.TraceBinding binding =
                ref storage.TraceBindings[operationIndex];
            int invocationIndex = ResolveTraceInvocation(storage, binding.InvocationGroupIndex, operationIndex, lifecycleOwner);
            ref readonly OrchestrationWorkbenchDiagnosticStorage<TValue>.TraceInvocation invocation =
                ref storage.TraceInvocations[invocationIndex];
            int parentIndex = invocation.ParentGroupIndex < 0 ? -1 :
                ResolveTraceInvocation(storage, invocation.ParentGroupIndex, invocation.OwnerOperationIndex, false);
            ref readonly OrchestrationExecutionFrameState frame = ref ReadExecutionFrame(operationIndex)[0];
            int index = storage.TraceContextCount++;
            ref OrchestrationTraceContext context = ref storage.TraceContexts[index];
            context.SourceIndex = binding.SourceIndex;
            context.InvocationSourceIndex = invocation.SourceIndex;
            context.ParentInvocationSourceIndex = parentIndex < 0 ? -1 :
                storage.TraceInvocations[parentIndex].SourceIndex;
            context.InvocationOperationIndex = invocation.OwnerOperationIndex;
            context.ParentInvocationOperationIndex = parentIndex < 0 ? -1 :
                storage.TraceInvocations[parentIndex].OwnerOperationIndex;
            context.ExecutionFrameOwnerOperationIndex = frame.OwnerOperationIndex;
            context.ActionInstanceId = frame.ActionInstanceId;
            context.SkillExecutionGeneration = frame.Generation;
            context.GraphInvocationGeneration = ReadGeneration(invocation.OwnerOperationIndex);
            context.ParentInvocationGeneration = parentIndex < 0 ? 0 :
                ReadGeneration(storage.TraceInvocations[parentIndex].OwnerOperationIndex);
            context.TimelineStart = frame.TimelineStart;
            context.ClipOrdinal = frame.ClipOrdinal;
            context.Cycle = frame.Cycle;
            context.Marker = frame.Marker;
            return index;
        }

        int ResolveTraceInvocation(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            int groupIndex, int operationIndex, bool lifecycleOwner)
        {
            OrchestrationRange group = storage.TraceInvocationGroups[groupIndex];
            if (group.Count == 1)
                return group.Offset;
            if (lifecycleOwner)
            {
                int owner = -1;
                for (int index = group.Offset; index < group.Offset + group.Count; index++)
                    if (storage.TraceInvocations[index].OwnerOperationIndex == operationIndex)
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
            int frameOwner = storage.TraceInvocationFrameOwners[groupIndex];
            bool borrowed = m_Blackboards.IsBorrowedOperation(frameOwner);
            int frame = OperationFrame(frameOwner);
            if (!borrowed)
                for (int index = storage.TraceEntryDepth - 1; index >= 0; index--)
                {
                    ref readonly OrchestrationWorkbenchDiagnosticStorage<TValue>.TraceEntryScope scope =
                        ref storage.TraceEntryScopes[index];
                    if (scope.FrameIndex != frame)
                        continue;
                    int selected = FindTraceInvocation(storage, in group, scope.EntryOperationIndex);
                    if (selected >= 0)
                        return selected;
                }
            int entry = (borrowed ? storage.TraceBorrowedFrameEntries :
                storage.TraceFrameEntries)[frame];
            int invocation = FindTraceInvocation(storage, in group, entry);
            if (invocation < 0)
                throw new InvalidOperationException($"Trace operation '{operationIndex}' requires its actual invocation entry scope for execution frame '{frame}'.");
            return invocation;
        }

        static int FindTraceInvocation(OrchestrationWorkbenchDiagnosticStorage<TValue> storage,
            in OrchestrationRange group, int entryOperationIndex)
        {
            for (int index = group.Offset; index < group.Offset + group.Count; index++)
                if (storage.TraceInvocations[index].EntryOperationIndex == entryOperationIndex)
                    return index;
            return -1;
        }
    }
}
