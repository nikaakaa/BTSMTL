using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    // 工作台采集存储：Trace、调用作用域绑定、值观察与覆盖退休；按需准备，采集期间复用。
    // 只由工作台诊断插头经执行器回调写入，执行器本身不认识本类型。
    public sealed partial class OrchestrationWorkbenchDiagnosticStorage<TValue> where TValue : unmanaged
    {
        readonly OrchestrationProgram<TValue> m_Program;
        readonly ReadOnlyMemory<OrchestrationGraphSource> m_Sources;
        readonly int m_ExecutionFrameCapacity;
        TValue[] m_InputObservations;
        bool[] m_InputObservationWritten;
        TValue[] m_OutputObservations;
        bool[] m_OutputObservationWritten;
        OrchestrationTraceEvent<TValue>[] m_TraceEvents = Array.Empty<OrchestrationTraceEvent<TValue>>();
        OrchestrationStateTraceFrame[] m_StateFrames = Array.Empty<OrchestrationStateTraceFrame>();
        OrchestrationTraceContext[] m_TraceContexts = Array.Empty<OrchestrationTraceContext>();
        TraceBinding[] m_TraceBindings;
        TraceInvocation[] m_TraceInvocations;
        OrchestrationRange[] m_TraceInvocationGroups;
        int[] m_TraceInvocationFrameOwners;
        int[] m_TraceFrameEntries = Array.Empty<int>();
        int[] m_TraceBorrowedFrameEntries = Array.Empty<int>();
        TraceEntryScope[] m_TraceEntryScopes;
        OrchestrationTraceLoopScope[] m_LoopScopes;
        OrchestrationTraceContext m_ObservationContext;
        int m_LoopDepth;
        ulong m_ObservationSequence;
        int m_TraceCount;
        int m_StateFrameCount;
        int m_TraceContextCount;
        int m_TraceEntryDepth;

        public OrchestrationWorkbenchDiagnosticStorage(OrchestrationProgram<TValue> program,
            OrchestrationGraphContent<TValue> content, int executionFrameCapacity)
        {
            m_Program = program;
            m_Sources = content.Sources.Bind(content.Structure.Sources.Span);
            m_ExecutionFrameCapacity = executionFrameCapacity;
        }

        // 宿主在需要逐端口观察时设置；采集插头在写入观察值时转发。
        public IOrchestrationValueObservationSink<TValue> Observations { get; set; }
        public ReadOnlySpan<TValue> ObservedInputs => m_InputObservations;
        public ReadOnlySpan<bool> WrittenInputs => m_InputObservationWritten;
        public ReadOnlySpan<TValue> ObservedOutputs => m_OutputObservations;
        public ReadOnlySpan<bool> WrittenOutputs => m_OutputObservationWritten;
        public ReadOnlyMemory<OrchestrationGraphSource> TraceSources => m_Sources;
        public ReadOnlySpan<OrchestrationTraceEvent<TValue>> Trace => m_TraceEvents.AsSpan(0, m_TraceCount);
        public ReadOnlySpan<OrchestrationStateTraceFrame> StateTraceFrames => m_StateFrames.AsSpan(0, m_StateFrameCount);
        public ReadOnlySpan<OrchestrationTraceContext> Contexts => m_TraceContexts.AsSpan(0, m_TraceContextCount);

        public void PrepareValueCapture()
        {
            if (m_InputObservations != null)
                return;
            m_InputObservations = new TValue[m_Program.InputBindingCount];
            m_InputObservationWritten = new bool[m_Program.InputBindingCount];
            m_OutputObservations = new TValue[m_Program.ValueEvaluationSlotCount];
            m_OutputObservationWritten = new bool[m_Program.ValueEvaluationSlotCount];
            PrepareInvocationBindings();
        }

        public void PrepareTraceCapture(in OrchestrationTraceCapacity trace)
        {
            PrepareInvocationBindings();
            if (trace.Events > m_TraceEvents.Length)
                m_TraceEvents = new OrchestrationTraceEvent<TValue>[checked((int)trace.Events)];
            if (trace.StateFrames > m_StateFrames.Length)
                m_StateFrames = new OrchestrationStateTraceFrame[checked((int)trace.StateFrames)];
            if (trace.Contexts > m_TraceContexts.Length)
                m_TraceContexts = new OrchestrationTraceContext[checked((int)trace.Contexts)];
        }

        public void PrepareInvocationBindings()
        {
            if (m_TraceBindings != null)
                return;
            m_TraceBindings = new TraceBinding[m_Program.OperationCount];
            m_TraceFrameEntries = new int[m_ExecutionFrameCapacity];
            m_TraceBorrowedFrameEntries = new int[m_ExecutionFrameCapacity];
            m_TraceEntryScopes = new TraceEntryScope[m_Program.Content.WorkspaceCapacity.EntryDepth];
            int loopCount = 0;
            for (int operation = 0; operation < m_Program.OperationCount; operation++)
                if (m_Program.Operation(operation).Code == OrchestrationOperationCode.Loop)
                    loopCount++;
            m_LoopScopes = new OrchestrationTraceLoopScope[checked(loopCount * m_TraceEntryScopes.Length)];
            ReadOnlySpan<OrchestrationGraphSource> sources = m_Sources.Span;
            var groupIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var groups = new List<List<int>>();
            var operationSources = new Dictionary<int, int>();
            int invocationCount = 0;
            for (int index = 0; index < sources.Length; index++)
            {
                OrchestrationGraphExecutionSource source = sources[index].Source;
                if (source.TargetKind == OrchestrationGraphSourceTargetKind.Operation)
                    operationSources.Add(source.TargetIndex, index);
                else if (source.TargetKind == OrchestrationGraphSourceTargetKind.GraphInvocation)
                {
                    if (!groupIndexes.TryGetValue(source.SourceInvocationPath, out int group))
                    {
                        group = groups.Count;
                        groupIndexes.Add(source.SourceInvocationPath, group);
                        groups.Add(new List<int>());
                    }
                    groups[group].Add(index);
                    invocationCount++;
                }
            }
            m_TraceInvocations = new TraceInvocation[invocationCount];
            m_TraceInvocationGroups = new OrchestrationRange[groups.Count];
            m_TraceInvocationFrameOwners = new int[groups.Count];
            int cursor = 0;
            for (int group = 0; group < groups.Count; group++)
            {
                List<int> entries = groups[group];
                m_TraceInvocationGroups[group] = new OrchestrationRange(cursor, entries.Count);
                var entryOperations = new HashSet<int>();
                foreach (int sourceIndex in entries)
                {
                    OrchestrationGraphExecutionSource source = sources[sourceIndex].Source;
                    if (!entryOperations.Add(source.InvocationEntry.OperationIndex))
                        throw new ArgumentException($"Trace invocation '{source.SourceInvocationPath}' has multiple sources for entry '{source.InvocationEntry.OperationIndex}'.");
                    m_TraceInvocationFrameOwners[group] = m_Program.Operation(source.InvocationEntry.OperationIndex)
                        .ExecutionFrameOwnerOperationIndex;
                    m_TraceInvocations[cursor++] = new TraceInvocation(sourceIndex, source.TargetIndex,
                        source.InvocationEntry.OperationIndex, source.ParentInvocationPath.Length == 0 ? -1 :
                            groupIndexes[source.ParentInvocationPath]);
                }
            }
            for (int operation = 0; operation < m_Program.OperationCount; operation++)
            {
                if (!operationSources.TryGetValue(operation, out int sourceIndex))
                    throw new ArgumentException($"Trace capture requires an Operation Source for operation '{operation}'.");
                m_TraceBindings[operation] = new TraceBinding(sourceIndex,
                    groupIndexes[sources[sourceIndex].Source.GraphInvocationPath]);
            }
        }

        internal void Lifecycle<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            switch (lifecycle)
            {
                case OrchestrationDiagnosticLifecycle.Initialize:
                    if (cursor.Input.CaptureValues)
                    {
                        Array.Clear(m_InputObservationWritten, 0, m_InputObservationWritten.Length);
                        Array.Clear(m_OutputObservationWritten, 0, m_OutputObservationWritten.Length);
                    }
                    m_TraceFrameEntries.AsSpan().Fill(-1);
                    m_TraceBorrowedFrameEntries.AsSpan().Fill(-1);
                    m_TraceEntryDepth = 0;
                    m_LoopDepth = 0;
                    break;
                case OrchestrationDiagnosticLifecycle.BeginInvocation:
                    m_TraceCount = 0;
                    m_StateFrameCount = 0;
                    m_TraceContextCount = 0;
                    break;
                case OrchestrationDiagnosticLifecycle.EnterEntry:
                    m_TraceFrameEntries[frame] = operation;
                    m_TraceEntryScopes[m_TraceEntryDepth++] = new TraceEntryScope(frame, operation);
                    break;
                case OrchestrationDiagnosticLifecycle.LeaveEntry:
                    m_TraceEntryDepth--;
                    m_TraceFrameEntries[frame] = operation;
                    break;
                case OrchestrationDiagnosticLifecycle.RetireOperation:
                    RetireOperationOverrides(ref cursor, operation);
                    break;
                case OrchestrationDiagnosticLifecycle.RetireFrame:
                    OrchestrationValueOverride<TValue>.RetireExecutionFrame(cursor.Input.Overrides, in cursor.FrameHeader(frame));
                    break;
                case OrchestrationDiagnosticLifecycle.EnterLoopIteration:
                    if (cursor.Input.CaptureTrace || Observations != null)
                        WriteLoopScope(ref cursor, operation, out m_LoopScopes[m_LoopDepth++]);
                    break;
                case OrchestrationDiagnosticLifecycle.LeaveLoopIteration:
                    if (cursor.Input.CaptureTrace || Observations != null)
                        m_LoopDepth--;
                    break;
            }
        }

        static void RetireOperationOverrides<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            OrchestrationProgramView<TValue> program = cursor.Program;
            Span<OrchestrationValueOverride<TValue>> overrides = cursor.Input.Overrides;
            for (int index = 0; index < overrides.Length; index++)
            {
                ref OrchestrationValueOverride<TValue> value = ref overrides[index];
                if (value.Mode == OrchestrationValueOverrideMode.None ||
                    !value.Address.Owner.MatchesFrame(in cursor.FrameHeader(cursor.ExecutionFrameIndex)))
                    continue;
                ref readonly OrchestrationExecutionOwnerToken owner = ref value.Address.Owner;
                if (value.Address.Kind == OrchestrationValueOverrideKind.Blackboard)
                {
                    if (owner.InvocationOperationIndex != operationIndex ||
                        program.Blackboard(value.Address.Slot).Lifetime == OrchestrationBlackboardLifetime.GraphInstance &&
                        !program.Operation(operationIndex).IsRunnable)
                        continue;
                }
                else if (owner.GenerationOwnerOperationIndex != operationIndex && owner.StateOperationIndex != operationIndex)
                    continue;
                value.Retire();
            }
        }

        internal int FindOverride<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, int slot, OrchestrationValueOverrideKind kind,
            in OrchestrationBlackboardOwnerToken blackboardOwner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            if (kind == OrchestrationValueOverrideKind.Blackboard)
                return cursor.ConsumeBlackboardOverride(slot, in blackboardOwner);
            Span<OrchestrationValueOverride<TValue>> overrides = cursor.Input.Overrides;
            for (int index = 0; index < overrides.Length; index++)
            {
                ref OrchestrationValueOverride<TValue> supplied = ref overrides[index];
                if (supplied.Mode == OrchestrationValueOverrideMode.None || supplied.Address.Kind != kind ||
                    supplied.Address.Slot != slot || supplied.Status == OrchestrationValueOverrideStatus.OwnerRetired)
                    continue;
                OrchestrationExecutionOwnerToken owner = ReadExecutionOwner(ref cursor, operationIndex);
                if (!supplied.Address.Owner.Equals(in owner))
                    continue;
                if (!cursor.IsPredictiveEvaluation)
                    supplied.Status = OrchestrationValueOverrideStatus.Consumed;
                return index;
            }
            return -1;
        }

        internal void Observe<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, int slot, bool input, in TValue value)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            IOrchestrationValueObservationSink<TValue> observations = Observations;
            bool captureValues = cursor.Input.CaptureValues;
            if (!captureValues && observations == null || cursor.IsPredictiveEvaluation)
                return;
            if (captureValues)
            {
                (input ? m_InputObservations : m_OutputObservations)[slot] = value;
                (input ? m_InputObservationWritten : m_OutputObservationWritten)[slot] = true;
            }
            if (observations != null)
            {
                int invocationIndex = ResolveTraceInvocation(ref cursor, operationIndex);
                OrchestrationValueOverrideAddress address = ReadPortOverrideAddress(ref cursor, operationIndex, slot,
                    input, invocationIndex);
                WriteTraceContext(ref cursor, operationIndex, invocationIndex, out m_ObservationContext);
                m_ObservationContext.ObservationSequence = checked(++m_ObservationSequence);
                observations.Observe(in address, in value, in m_ObservationContext, m_LoopScopes.AsSpan(0, m_LoopDepth));
            }
        }

        internal void TraceOperation<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationTraceKind kind, int operationIndex, OrchestrationOperationResult result, uint stopCause,
            OrchestrationWaitReason waitReason, int valueSlot, in OrchestrationBlackboardSourceContext blackboardSource,
            OrchestrationStopMode stopMode)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            if (cursor.IsPredictiveEvaluation)
                return;
            if (cursor.Input.CaptureTrace || Observations != null)
                ObserveCall(ref cursor, operationIndex, kind);
            if (!cursor.Input.CaptureTrace)
                return;
            ref OrchestrationTraceEvent<TValue> item = ref m_TraceEvents[m_TraceCount++];
            item = default;
            item.Kind = kind;
            item.OperationIndex = operationIndex;
            item.EdgeIndex = -1;
            bool lifecycleOwner = kind is OrchestrationTraceKind.Enter or OrchestrationTraceKind.Running or
                OrchestrationTraceKind.Complete or OrchestrationTraceKind.StopRequested or
                OrchestrationTraceKind.Stopped or OrchestrationTraceKind.ForceStopped;
            item.ContextIndex = CaptureTraceContext(ref cursor, operationIndex, lifecycleOwner);
            item.Generation = m_TraceContexts[item.ContextIndex].NodeGeneration;
            item.StateFrameIndex = cursor.Program.Operation(operationIndex).Code == OrchestrationOperationCode.State
                ? CaptureStatePath(ref cursor, operationIndex, lifecycleOwner)
                : cursor.CurrentStateTraceFrame;
            item.Result = result;
            item.StopCause = stopCause;
            item.StopMode = stopMode;
            item.WaitReason = waitReason;
            item.DetailIndex = kind != OrchestrationTraceKind.Blackboard &&
                cursor.Program.Operation(operationIndex).Code == OrchestrationOperationCode.Loop
                ? cursor.ChildCursor(operationIndex) : valueSlot;
            if (kind == OrchestrationTraceKind.Blackboard)
            {
                cursor.ReadBlackboardValue(valueSlot, ref item.Value);
                item.Blackboard = cursor.ReadBlackboardMetadata(valueSlot);
                item.BlackboardLifetime = cursor.Program.Blackboard(valueSlot).Lifetime;
                OrchestrationTimelineInvocationFacts timeline = cursor.TimelineInvocation;
                item.BlackboardWriteStamp = new OrchestrationBlackboardWriteStamp
                {
                    SourceOperationIndex = operationIndex,
                    LogicTick = cursor.Input.Tick,
                    SourceContext = blackboardSource,
                    TimelineOperationIndex = timeline.HasInvocation
                        ? cursor.FrameHeader(cursor.ExecutionFrameIndex).ParentTimelineOperationIndex : -1,
                    ClipOperationIndex = timeline.HasInvocation
                        ? cursor.FrameHeader(cursor.ExecutionFrameIndex).OwnerOperationIndex : -1,
                    Cycle = timeline.HasInvocation ? timeline.Invocation.Token.Cycle : 0,
                    CommandSequence = timeline.HasInvocation ? timeline.Invocation.Token.Sequence : 0
                };
            }
        }

        internal void TraceEdge<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int edgeIndex, bool selected, bool passed, int sourceStateOperation)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            if (!cursor.Input.CaptureTrace || cursor.IsPredictiveEvaluation)
                return;
            int operationIndex = sourceStateOperation >= 0 ? sourceStateOperation : cursor.Program.Edge(edgeIndex).SourceOperationIndex;
            ref OrchestrationTraceEvent<TValue> item = ref m_TraceEvents[m_TraceCount++];
            item = default;
            item.Kind = OrchestrationTraceKind.Edge;
            item.OperationIndex = operationIndex;
            item.EdgeIndex = edgeIndex;
            item.ContextIndex = CaptureTraceContext(ref cursor, operationIndex);
            item.Generation = m_TraceContexts[item.ContextIndex].NodeGeneration;
            item.StateFrameIndex = cursor.CurrentStateTraceFrame;
            item.Selected = selected;
            item.Passed = passed;
            item.DetailIndex = -1;
        }

        internal int CaptureState<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, bool rootCompletedOverride)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            cursor.Input.CaptureTrace && !rootCompletedOverride && !cursor.IsPredictiveEvaluation
                ? CaptureStatePath(ref cursor, operationIndex) : -1;

        int CaptureStatePath<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int stateOperationIndex, bool lifecycleOwner = false)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            int parentState = cursor.Program.Operation(stateOperationIndex).ParentStateOperationIndex;
            int parent = parentState >= 0 ? CaptureStatePath(ref cursor, parentState) : -1;
            int index = m_StateFrameCount++;
            m_StateFrames[index] = new OrchestrationStateTraceFrame
            {
                ParentIndex = parent,
                StateOperationIndex = stateOperationIndex,
                Generation = cursor.ReadGeneration(stateOperationIndex),
                ContextIndex = CaptureTraceContext(ref cursor, stateOperationIndex, lifecycleOwner)
            };
            return index;
        }

        internal void ReadEntryContext<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            in OrchestrationExecutionEntry entry, out OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            BindTraceInvocationScope(ref cursor, entry.OperationIndex, entry);
            ReadTraceContext(ref cursor, entry.OperationIndex, out context);
        }

        public void BindTraceInvocationScope<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, OrchestrationExecutionEntry entry)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            int frame = cursor.ExecutionFrameOf(operationIndex);
            int[] entries = cursor.IsBorrowedOperation(operationIndex) ? m_TraceBorrowedFrameEntries : m_TraceFrameEntries;
            entries[frame] = entry.OperationIndex;
        }

        public void ReadTraceContext<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, out OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            int invocationIndex = ResolveTraceInvocation(ref cursor, operationIndex);
            WriteTraceContext(ref cursor, operationIndex, invocationIndex, out context);
        }

        public OrchestrationExecutionOwnerToken ReadExecutionOwner<TNumerics, THost>(
            ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor, int operationIndex)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            int invocationIndex = ResolveTraceInvocation(ref cursor, operationIndex);
            return ReadExecutionOwner(ref cursor, operationIndex, invocationIndex);
        }

        public OrchestrationValueOverrideAddress ReadPortOverrideAddress<TNumerics, THost>(
            ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor, int operationIndex, int slot, bool input)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            int invocationIndex = ResolveTraceInvocation(ref cursor, operationIndex);
            return ReadPortOverrideAddress(ref cursor, operationIndex, slot, input, invocationIndex);
        }

        public bool TrySelectExecutionOwner<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, in OrchestrationExecutionOwnerToken owner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            OrchestrationRange group = m_TraceInvocationGroups[m_TraceBindings[operationIndex].InvocationGroupIndex];
            int invocationIndex = -1;
            for (int index = group.Offset; index < group.Offset + group.Count; index++)
                if (m_TraceInvocations[index].OwnerOperationIndex == owner.InvocationOperationIndex)
                {
                    invocationIndex = index;
                    break;
                }
            if (invocationIndex < 0 || !IsInvocationGenerationOwner(ref cursor, invocationIndex, owner.GenerationOwnerOperationIndex))
                return false;
            int stateOperation = cursor.Program.Operation(operationIndex).ParentStateOperationIndex;
            if (stateOperation != owner.StateOperationIndex)
                return false;
            for (int frame = 0; frame < cursor.FrameCapacity; frame++)
            {
                if (!owner.MatchesFrame(in cursor.FrameHeader(frame)))
                    continue;
                if (frame != cursor.ExecutionFrameIndex)
                    cursor.SelectDiagnosticFrame(frame);
                m_TraceFrameEntries[frame] = m_TraceInvocations[invocationIndex].EntryOperationIndex;
                ulong generation = owner.GenerationOwnerOperationIndex < 0
                    ? cursor.ReadExecutionFrame(operationIndex)[0].Generation
                    : cursor.ReadGeneration(owner.GenerationOwnerOperationIndex);
                if (generation != owner.InvocationGeneration ||
                    stateOperation >= 0 && cursor.ReadGeneration(stateOperation) != owner.StateGeneration)
                    return false;
                return true;
            }
            return false;
        }

        OrchestrationExecutionOwnerToken ReadExecutionOwner<TNumerics, THost>(
            ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor, int operationIndex, int invocationIndex)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            int invocationOperation = m_TraceInvocations[invocationIndex].OwnerOperationIndex;
            int stateOperation = cursor.Program.Operation(operationIndex).ParentStateOperationIndex;
            ulong generation = ReadInvocationGeneration(ref cursor, invocationIndex, operationIndex, out int generationOwner);
            return OrchestrationExecutionOwnerToken.Create(in cursor.ReadExecutionFrame(operationIndex)[0],
                invocationOperation, generation, stateOperation,
                stateOperation < 0 ? 0 : cursor.ReadGeneration(stateOperation), generationOwner);
        }

        ulong ReadInvocationGeneration<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int invocationIndex, int operationIndex, out int generationOwner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            while (true)
            {
                ref readonly TraceInvocation invocation = ref m_TraceInvocations[invocationIndex];
                if (cursor.Program.Operation(invocation.OwnerOperationIndex).IsRunnable)
                {
                    generationOwner = invocation.OwnerOperationIndex;
                    return cursor.ReadGeneration(invocation.OwnerOperationIndex);
                }
                if (invocation.ParentGroupIndex < 0)
                {
                    generationOwner = -1;
                    return cursor.ReadExecutionFrame(operationIndex)[0].Generation;
                }
                invocationIndex = ResolveTraceInvocation(ref cursor, invocation.ParentGroupIndex,
                    invocation.OwnerOperationIndex, false);
            }
        }

        OrchestrationValueOverrideAddress ReadPortOverrideAddress<TNumerics, THost>(
            ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor, int operationIndex,
            int slot, bool input, int invocationIndex)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> => new OrchestrationValueOverrideAddress
        {
            Kind = input ? OrchestrationValueOverrideKind.Input : OrchestrationValueOverrideKind.Output,
            Slot = slot,
            Owner = ReadExecutionOwner(ref cursor, operationIndex, invocationIndex)
        };

        bool IsInvocationGenerationOwner<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int invocationIndex, int generationOwner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            ref readonly TraceInvocation invocation = ref m_TraceInvocations[invocationIndex];
            if (cursor.Program.Operation(invocation.OwnerOperationIndex).IsRunnable)
                return invocation.OwnerOperationIndex == generationOwner;
            if (invocation.ParentGroupIndex < 0)
                return generationOwner < 0;
            OrchestrationRange parents = m_TraceInvocationGroups[invocation.ParentGroupIndex];
            for (int index = parents.Offset; index < parents.Offset + parents.Count; index++)
                if (IsInvocationGenerationOwner(ref cursor, index, generationOwner))
                    return true;
            return false;
        }

        int CaptureTraceContext<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, bool lifecycleOwner = false)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            int invocationIndex = ResolveTraceInvocation(ref cursor, operationIndex, lifecycleOwner);
            int index = m_TraceContextCount++;
            WriteTraceContext(ref cursor, operationIndex, invocationIndex, out m_TraceContexts[index]);
            return index;
        }

        void WriteTraceContext<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, int invocationIndex, out OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            ref readonly TraceBinding binding = ref m_TraceBindings[operationIndex];
            ref readonly TraceInvocation invocation = ref m_TraceInvocations[invocationIndex];
            int parentIndex = invocation.ParentGroupIndex < 0 ? -1 :
                ResolveTraceInvocation(ref cursor, invocation.ParentGroupIndex, invocation.OwnerOperationIndex, false);
            ref readonly OrchestrationExecutionFrameState frame = ref cursor.ReadExecutionFrame(operationIndex)[0];
            context.ExecutionObject = cursor.Input.ExecutionObject;
            context.CharacterRuntimeId = cursor.Input.CharacterRuntimeId;
            context.OperationIndex = operationIndex;
            bool runnable = cursor.Program.Operation(operationIndex).IsRunnable;
            context.NodeGeneration = runnable ? cursor.ReadGeneration(operationIndex) : 0;
            context.NodeActivation = !runnable ? OrchestrationTraceAvailability.NotApplicable :
                context.NodeGeneration == 0 ? OrchestrationTraceAvailability.Unavailable : OrchestrationTraceAvailability.Available;
            context.ObservationSequence = 0;
            context.LoopScopeAvailability = cursor.HasExecutionScope
                ? OrchestrationTraceAvailability.Available : OrchestrationTraceAvailability.Unavailable;
            context.LoopScope = m_LoopDepth == 0 ? default : m_LoopScopes[m_LoopDepth - 1];
            context.SourceIndex = binding.SourceIndex;
            context.InvocationSourceIndex = invocation.SourceIndex;
            context.ParentInvocationSourceIndex = parentIndex < 0 ? -1 : m_TraceInvocations[parentIndex].SourceIndex;
            context.InvocationOperationIndex = invocation.OwnerOperationIndex;
            context.ParentInvocationOperationIndex = parentIndex < 0 ? -1 :
                m_TraceInvocations[parentIndex].OwnerOperationIndex;
            context.ExecutionFrameOwnerOperationIndex = frame.OwnerOperationIndex;
            context.ActionInstanceId = frame.ActionInstanceId;
            context.SkillExecutionGeneration = frame.Generation;
            context.GraphInvocationGeneration = ReadInvocationGeneration(ref cursor, invocationIndex, operationIndex, out _);
            context.ParentInvocationGeneration = parentIndex < 0 ? 0 :
                ReadInvocationGeneration(ref cursor, parentIndex, invocation.OwnerOperationIndex, out _);
            context.TimelineStart = frame.TimelineStart;
            context.ClipOrdinal = frame.ClipOrdinal;
            context.Cycle = frame.Cycle;
            context.Marker = frame.Marker;
            ReadCall(ref cursor, operationIndex, invocationIndex, parentIndex, ref context);
        }

        void WriteLoopScope<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, out OrchestrationTraceLoopScope scope)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            int invocationIndex = ResolveTraceInvocation(ref cursor, operationIndex);
            ref readonly TraceInvocation invocation = ref m_TraceInvocations[invocationIndex];
            int parentIndex = invocation.ParentGroupIndex < 0 ? -1 :
                ResolveTraceInvocation(ref cursor, invocation.ParentGroupIndex, invocation.OwnerOperationIndex, false);
            ref readonly OrchestrationExecutionFrameState frame = ref cursor.ReadExecutionFrame(operationIndex)[0];
            scope.OperationIndex = operationIndex;
            scope.InvocationSourceIndex = invocation.SourceIndex;
            scope.ParentInvocationSourceIndex = parentIndex < 0 ? -1 : m_TraceInvocations[parentIndex].SourceIndex;
            scope.Iteration = cursor.ChildCursor(operationIndex);
            bool runnable = cursor.Program.Operation(operationIndex).IsRunnable;
            scope.Generation = runnable ? cursor.ReadGeneration(operationIndex) : 0;
            scope.Activation = runnable ? OrchestrationTraceAvailability.Available : OrchestrationTraceAvailability.NotApplicable;
            scope.ActionInstanceId = frame.ActionInstanceId;
            scope.ExecutionGeneration = frame.Generation;
            scope.InvocationGeneration = ReadInvocationGeneration(ref cursor, invocationIndex, operationIndex, out _);
            scope.ParentInvocationGeneration = parentIndex < 0 ? 0 :
                ReadInvocationGeneration(ref cursor, parentIndex, invocation.OwnerOperationIndex, out _);
            scope.PlaybackIdentity = frame.TimelineStart.PlaybackIdentity;
            scope.CallSequence = ReadObservedCall(ref cursor, operationIndex);
        }

        int ResolveTraceInvocation<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operationIndex, bool lifecycleOwner = false)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            ResolveTraceInvocation(ref cursor, m_TraceBindings[operationIndex].InvocationGroupIndex,
                operationIndex, lifecycleOwner);

        int ResolveTraceInvocation<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int groupIndex, int operationIndex, bool lifecycleOwner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            OrchestrationRange group = m_TraceInvocationGroups[groupIndex];
            if (group.Count == 1)
                return group.Offset;
            if (lifecycleOwner)
            {
                int owner = -1;
                for (int index = group.Offset; index < group.Offset + group.Count; index++)
                    if (m_TraceInvocations[index].OwnerOperationIndex == operationIndex)
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
            int frameOwner = m_TraceInvocationFrameOwners[groupIndex];
            bool borrowed = cursor.IsBorrowedOperation(frameOwner);
            int frame = cursor.ExecutionFrameOf(frameOwner);
            if (!borrowed)
                for (int index = m_TraceEntryDepth - 1; index >= 0; index--)
                {
                    ref readonly TraceEntryScope scope = ref m_TraceEntryScopes[index];
                    if (scope.FrameIndex != frame)
                        continue;
                    int selected = FindTraceInvocation(in group, scope.EntryOperationIndex);
                    if (selected >= 0)
                        return selected;
                }
            int entry = (borrowed ? m_TraceBorrowedFrameEntries : m_TraceFrameEntries)[frame];
            int invocation = FindTraceInvocation(in group, entry);
            if (invocation < 0)
                throw new InvalidOperationException($"Trace operation '{operationIndex}' requires its actual invocation entry scope for execution frame '{frame}'.");
            return invocation;
        }

        int FindTraceInvocation(in OrchestrationRange group, int entryOperationIndex)
        {
            for (int index = group.Offset; index < group.Offset + group.Count; index++)
                if (m_TraceInvocations[index].EntryOperationIndex == entryOperationIndex)
                    return index;
            return -1;
        }

        readonly struct TraceBinding
        {
            internal TraceBinding(int sourceIndex, int invocationGroupIndex)
            {
                SourceIndex = sourceIndex;
                InvocationGroupIndex = invocationGroupIndex;
            }

            internal int SourceIndex { get; }
            internal int InvocationGroupIndex { get; }
        }

        readonly struct TraceInvocation
        {
            internal TraceInvocation(int sourceIndex, int ownerOperationIndex, int entryOperationIndex,
                int parentGroupIndex)
            {
                SourceIndex = sourceIndex;
                OwnerOperationIndex = ownerOperationIndex;
                EntryOperationIndex = entryOperationIndex;
                ParentGroupIndex = parentGroupIndex;
            }

            internal int SourceIndex { get; }
            internal int OwnerOperationIndex { get; }
            internal int EntryOperationIndex { get; }
            internal int ParentGroupIndex { get; }
        }

        readonly struct TraceEntryScope
        {
            internal TraceEntryScope(int frameIndex, int entryOperationIndex)
            {
                FrameIndex = frameIndex;
                EntryOperationIndex = entryOperationIndex;
            }

            internal int FrameIndex { get; }
            internal int EntryOperationIndex { get; }
        }
    }
}
