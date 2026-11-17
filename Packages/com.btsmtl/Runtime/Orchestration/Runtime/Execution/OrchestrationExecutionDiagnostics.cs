using System;
using System.Runtime.CompilerServices;
#if UNITY_EDITOR
using System.Diagnostics;
#endif

namespace BTSMTL.Orchestration
{
#if UNITY_EDITOR
    public enum OrchestrationWorkloadCost : byte
    {
        Control,
        ValueSchedule,
        Numeric,
        BlackboardGet,
        BlackboardSet,
        Host,
        Count,
        None = byte.MaxValue
    }

    public readonly struct OrchestrationWorkloadCostScope : IDisposable
    {
        readonly OrchestrationWorkloadObservation m_Observation;
        readonly OrchestrationWorkloadCost m_Previous;

        public OrchestrationWorkloadCostScope(OrchestrationWorkloadObservation observation,
            OrchestrationWorkloadCost cost)
        {
            m_Observation = observation;
            m_Previous = observation == null ? OrchestrationWorkloadCost.None : observation.EnterCost(cost);
        }

        public void Dispose() => m_Observation?.LeaveCost(m_Previous);
    }

    public enum OrchestrationWorkloadCounter : byte
    {
        HostInputBindingReads,
        HostInputBindingWrites,
        EntryAdvances,
        OutputAdvances,
        ControlTicks,
        ControlExecutions,
        ScheduleSteps,
        InputSchedules,
        InputSlotRequests,
        InputConstants,
        InputCopies,
        InputConversions,
        HostInputReads,
        ValueComputes,
        NumericComputes,
        NumericAssignments,
        BlackboardReads,
        BlackboardWrites,
        GraphCallOutputReads,
        SavedOutputReads,
        ReadOnlyExtensions,
        CandidateExtensions,
        InputScheduleReevaluations,
        ValueReevaluations,
        NumericRecomputes,
        Count
    }

    public enum OrchestrationWorkloadCallStatus : byte
    {
        Prepared,
        Computed,
        Failed,
        Discarded
    }

    public enum OrchestrationWorkloadStopReason : byte
    {
        None,
        CallLimit,
        ExplicitStop
    }

    public struct OrchestrationWorkloadCall
    {
        public BTSMTL.Graphs.GraphInvocationIdentity Invocation { get; internal set; }
        public ulong ResetGeneration { get; internal set; }
        public bool StartsPending { get; internal set; }
        public bool RunsStart { get; internal set; }
        public bool ComputeEntered { get; internal set; }
        public bool HasDiagnostics { get; internal set; }
        public OrchestrationWorkloadCallStatus Status { get; internal set; }
        public int LastOperationIndex { get; internal set; }
        public long ExecutionElapsedTicks { get; internal set; }
    }

    public sealed class OrchestrationWorkloadObservation
    {
        const int CounterCount = (int)OrchestrationWorkloadCounter.Count;
        const int CostCount = (int)OrchestrationWorkloadCost.Count;
        public const int ClockCalibrationPairs = 64;
        readonly OrchestrationWorkloadCall[] m_Calls;
        readonly int[] m_CallOperationCounters;
        readonly long[] m_OperationCounters;
        readonly long[] m_CallCostTicks;
        readonly int[] m_CallCostScopes;
        OrchestrationWorkloadCost m_CurrentCost = OrchestrationWorkloadCost.None;
        long m_CostSegmentStarted;
        long m_ExecutionCostStarted;
        int m_CallCount;

        public OrchestrationWorkloadObservation(ThirdPersonSimulation.GraphExecutionIdentity content,
            OrchestrationProgramContent program, OrchestrationIdentityTable identities, int maximumCalls)
        {
            if (maximumCalls <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumCalls));
            Content = content;
            Program = program;
            Identities = identities;
            m_Calls = new OrchestrationWorkloadCall[maximumCalls];
            m_OperationCounters = new long[checked((OperationCount + 1) * CounterCount)];
            m_CallOperationCounters = new int[checked(maximumCalls * m_OperationCounters.Length)];
            m_CallCostTicks = new long[checked(maximumCalls * CostCount)];
            m_CallCostScopes = new int[m_CallCostTicks.Length];
            long minimum = long.MaxValue;
            long maximum = 0;
            long total = 0;
            for (int i = 0; i < ClockCalibrationPairs; i++)
            {
                long started = Stopwatch.GetTimestamp();
                long elapsed = Stopwatch.GetTimestamp() - started;
                minimum = Math.Min(minimum, elapsed);
                maximum = Math.Max(maximum, elapsed);
                total += elapsed;
            }
            ClockPairMinimumTicks = minimum;
            ClockPairMaximumTicks = maximum;
            ClockPairMeanTicks = (double)total / ClockCalibrationPairs;
        }

        public ThirdPersonSimulation.GraphExecutionIdentity Content { get; }
        public OrchestrationProgramContent Program { get; }
        public OrchestrationIdentityTable Identities { get; }
        public int OperationCount => Program.Operations.Length;
        public int CallCapacity => m_Calls.Length;
        public int CallCount => m_CallCount;
        public bool IsEnabled => StopReason == OrchestrationWorkloadStopReason.None;
        public OrchestrationWorkloadStopReason StopReason { get; private set; }
        public ReadOnlySpan<OrchestrationWorkloadCall> Calls => m_Calls.AsSpan(0, m_CallCount);
        public long ClockFrequency => Stopwatch.Frequency;
        public bool ClockIsHighResolution => Stopwatch.IsHighResolution;
        public long ClockPairMinimumTicks { get; }
        public long ClockPairMaximumTicks { get; }
        public double ClockPairMeanTicks { get; }
        public long CostStorageBytes =>
            (long)m_CallCostTicks.Length * sizeof(long) +
            (long)m_CallCostScopes.Length * sizeof(int);
        public long CounterStorageBytes =>
            (long)m_OperationCounters.Length * sizeof(long) +
            (long)m_CallOperationCounters.Length * sizeof(int);

        public long OperationCounter(int operationIndex, OrchestrationWorkloadCounter counter) =>
            m_OperationCounters[(operationIndex + 1) * CounterCount + (int)counter];

        public int CallOperationCounter(int callIndex, int operationIndex, OrchestrationWorkloadCounter counter) =>
            m_CallOperationCounters[callIndex * m_OperationCounters.Length +
                (operationIndex + 1) * CounterCount + (int)counter];

        public long CallCostTicks(int callIndex, OrchestrationWorkloadCost cost) =>
            m_CallCostTicks[callIndex * CostCount + (int)cost];

        public int CallCostScopes(int callIndex, OrchestrationWorkloadCost cost) =>
            m_CallCostScopes[callIndex * CostCount + (int)cost];

        public long CallAttributedCostTicks(int callIndex)
        {
            long total = 0;
            int start = callIndex * CostCount;
            for (int i = start; i < start + CostCount; i++)
                total += m_CallCostTicks[i];
            return total;
        }

        public long CallUnattributedCostTicks(int callIndex) =>
            m_Calls[callIndex].ExecutionElapsedTicks - CallAttributedCostTicks(callIndex);

        internal OrchestrationWorkloadCost EnterCost(OrchestrationWorkloadCost cost)
        {
            long boundary = Stopwatch.GetTimestamp();
            OrchestrationWorkloadCost previous = m_CurrentCost;
            int start = (m_CallCount - 1) * CostCount;
            if (previous == OrchestrationWorkloadCost.None)
                m_ExecutionCostStarted = boundary;
            else
                m_CallCostTicks[start + (int)previous] += boundary - m_CostSegmentStarted;
            m_CallCostScopes[start + (int)cost]++;
            m_CurrentCost = cost;
            m_CostSegmentStarted = Stopwatch.GetTimestamp();
            return previous;
        }

        internal void LeaveCost(OrchestrationWorkloadCost previous)
        {
            long boundary = Stopwatch.GetTimestamp();
            m_CallCostTicks[(m_CallCount - 1) * CostCount + (int)m_CurrentCost] += boundary - m_CostSegmentStarted;
            if (previous == OrchestrationWorkloadCost.None)
                m_Calls[m_CallCount - 1].ExecutionElapsedTicks += boundary - m_ExecutionCostStarted;
            m_CurrentCost = previous;
            m_CostSegmentStarted = Stopwatch.GetTimestamp();
        }

        public long CallCounter(int callIndex, OrchestrationWorkloadCounter counter)
        {
            long total = 0;
            int start = callIndex * m_OperationCounters.Length;
            int end = start + m_OperationCounters.Length;
            for (int i = start + (int)counter; i < end; i += CounterCount)
                total += m_CallOperationCounters[i];
            return total;
        }

        public OrchestrationWorkloadObservation BeginInvocation(
            BTSMTL.Graphs.GraphInvocationIdentity invocation, ulong resetGeneration, bool runsStart)
        {
            if (!IsEnabled)
                return null;
            m_Calls[m_CallCount++] = new OrchestrationWorkloadCall
            {
                Invocation = invocation,
                ResetGeneration = resetGeneration,
                StartsPending = runsStart,
                Status = OrchestrationWorkloadCallStatus.Prepared,
                LastOperationIndex = -1
            };
            return this;
        }

        public void EnterCompute(bool hasDiagnostics)
        {
            ref OrchestrationWorkloadCall call = ref m_Calls[m_CallCount - 1];
            call.ComputeEntered = true;
            call.HasDiagnostics = hasDiagnostics;
        }

        public void EnterStart() => m_Calls[m_CallCount - 1].RunsStart = true;

        public void EndInvocation(OrchestrationWorkloadCallStatus status, int lastOperationIndex)
        {
            ref OrchestrationWorkloadCall call = ref m_Calls[m_CallCount - 1];
            call.Status = status;
            call.LastOperationIndex = lastOperationIndex;
            if (m_CallCount == m_Calls.Length)
                StopReason = OrchestrationWorkloadStopReason.CallLimit;
        }

        public void Stop()
        {
            if (IsEnabled)
                StopReason = OrchestrationWorkloadStopReason.ExplicitStop;
        }

        public void Count(int operationIndex, OrchestrationWorkloadCounter counter, int amount = 1)
        {
            int previous = Add(operationIndex, counter, amount);
            if (previous == 0)
                return;
            OrchestrationWorkloadCounter repeated;
            switch (counter)
            {
                case OrchestrationWorkloadCounter.InputSchedules:
                    repeated = OrchestrationWorkloadCounter.InputScheduleReevaluations;
                    break;
                case OrchestrationWorkloadCounter.ValueComputes:
                    repeated = OrchestrationWorkloadCounter.ValueReevaluations;
                    break;
                case OrchestrationWorkloadCounter.NumericComputes:
                    repeated = OrchestrationWorkloadCounter.NumericRecomputes;
                    break;
                default:
                    return;
            }
            Add(operationIndex, repeated, 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        int Add(int operationIndex, OrchestrationWorkloadCounter counter, int amount)
        {
            int index = (operationIndex + 1) * CounterCount + (int)counter;
            ref int callCounter = ref m_CallOperationCounters[(m_CallCount - 1) * m_OperationCounters.Length + index];
            int previous = callCounter;
            callCounter += amount;
            m_OperationCounters[index] += amount;
            return previous;
        }
    }
#endif

    public enum OrchestrationDiagnosticLifecycle : byte
    {
        Initialize,
        BeginInvocation,
        EnterEntry,
        LeaveEntry,
        RetireOperation,
        RetireFrame,
        EnterLoopIteration,
        LeaveLoopIteration
    }

    // 诊断策略：由执行环境插头按值持有，生产与工作台在编译期区分。
    public interface IOrchestrationExecutionDiagnostics<TValue> where TValue : unmanaged
    {
        int ScheduleEntry(in OrchestrationValueScheduleEntry entry);

        void Lifecycle<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        int FindOverride<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        void Observe<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, int slot, bool input, in TValue value)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        void Trace<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource, OrchestrationStopMode stopMode = default)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        int CaptureState<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, bool rootCompletedOverride)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        void ReadEntryContext<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            in OrchestrationExecutionEntry entry, out OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;
    }

    // 正式推进：不持有任何存储，诊断调用全部内联为空。
    public readonly struct OrchestrationProductDiagnostics<TValue> : IOrchestrationExecutionDiagnostics<TValue>
        where TValue : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ScheduleEntry(in OrchestrationValueScheduleEntry entry) => entry.Product;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Lifecycle<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FindOverride<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> => -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Observe<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, int slot, bool input, in TValue value)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Trace<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource, OrchestrationStopMode stopMode = default)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CaptureState<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, bool rootCompletedOverride)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> => -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadEntryContext<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            in OrchestrationExecutionEntry entry, out OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> => context = default;
    }

    // 工作台采集：Trace、值观察、覆盖退休都写入托管存储，不进入 Burst。
    public readonly struct OrchestrationWorkbenchDiagnostics<TValue> : IOrchestrationExecutionDiagnostics<TValue>
        where TValue : unmanaged
    {
        readonly OrchestrationWorkbenchDiagnosticStorage<TValue> m_Storage;

        public OrchestrationWorkbenchDiagnostics(OrchestrationWorkbenchDiagnosticStorage<TValue> storage) =>
            m_Storage = storage ?? throw new ArgumentNullException(nameof(storage));

        public OrchestrationWorkbenchDiagnosticStorage<TValue> Storage => m_Storage;

        public int ScheduleEntry(in OrchestrationValueScheduleEntry entry) => entry.Workbench;

        public void Lifecycle<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Storage.Lifecycle(ref cursor, lifecycle, operation, frame);

        public int FindOverride<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Storage.FindOverride(ref cursor, operation, slot, kind, in owner);

        public void Observe<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, int slot, bool input, in TValue value)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Storage.Observe(ref cursor, operation, slot, input, in value);

        public void Trace<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource, OrchestrationStopMode stopMode = default)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            if (kind == OrchestrationTraceKind.Edge)
                m_Storage.TraceEdge(ref cursor, edge, selected, passed, operation);
            else
                m_Storage.TraceOperation(ref cursor, kind, operation, result, stopCause, waitReason,
                    valueSlot, in blackboardSource, stopMode);
        }

        public int CaptureState<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, bool rootCompletedOverride)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Storage.CaptureState(ref cursor, operation, rootCompletedOverride);

        public void ReadEntryContext<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            in OrchestrationExecutionEntry entry, out OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Storage.ReadEntryContext(ref cursor, in entry, out context);
    }

    public sealed class OrchestrationExecutionTraceBuffer<TValue> where TValue : unmanaged
    {
        OrchestrationTraceEvent<TValue>[] m_Events;
        OrchestrationStateTraceFrame[] m_StateFrames;
        OrchestrationTraceContext[] m_Contexts;
        OrchestrationTraceBatch[] m_Batches;
        int m_EventCount;
        int m_StateFrameCount;
        int m_ContextCount;
        int m_BatchCount;

        public OrchestrationExecutionTraceBuffer(in OrchestrationTraceCapacity trace, int batchCount)
        {
            m_Events = new OrchestrationTraceEvent<TValue>[checked((int)trace.Events)];
            m_StateFrames = new OrchestrationStateTraceFrame[checked((int)trace.StateFrames)];
            m_Contexts = new OrchestrationTraceContext[checked((int)trace.Contexts)];
            m_Batches = new OrchestrationTraceBatch[batchCount];
        }

        public void Reserve(in OrchestrationTraceCapacity trace, int batchCount)
        {
            if (trace.Events > m_Events.Length)
                Array.Resize(ref m_Events, checked((int)trace.Events));
            if (trace.StateFrames > m_StateFrames.Length)
                Array.Resize(ref m_StateFrames, checked((int)trace.StateFrames));
            if (trace.Contexts > m_Contexts.Length)
                Array.Resize(ref m_Contexts, checked((int)trace.Contexts));
            if (batchCount > m_Batches.Length)
                Array.Resize(ref m_Batches, batchCount);
        }

        public ReadOnlySpan<OrchestrationTraceEvent<TValue>> Events => m_Events.AsSpan(0, m_EventCount);
        public ReadOnlySpan<OrchestrationStateTraceFrame> StateFrames => m_StateFrames.AsSpan(0, m_StateFrameCount);
        public ReadOnlySpan<OrchestrationTraceContext> Contexts => m_Contexts.AsSpan(0, m_ContextCount);
        // 事件和路径里的索引属于各自 batch；消费者先按这些范围切片。
        public ReadOnlySpan<OrchestrationTraceBatch> Batches => m_Batches.AsSpan(0, m_BatchCount);

        public void BeginTick()
        {
            m_EventCount = 0;
            m_StateFrameCount = 0;
            m_ContextCount = 0;
            m_BatchCount = 0;
        }

        public void Append(ReadOnlySpan<OrchestrationTraceEvent<TValue>> events,
            ReadOnlySpan<OrchestrationStateTraceFrame> stateFrames,
            ReadOnlySpan<OrchestrationTraceContext> contexts)
        {
            events.CopyTo(m_Events.AsSpan(m_EventCount));
            stateFrames.CopyTo(m_StateFrames.AsSpan(m_StateFrameCount));
            contexts.CopyTo(m_Contexts.AsSpan(m_ContextCount));
            m_Batches[m_BatchCount++] = new OrchestrationTraceBatch(
                new OrchestrationRange(m_EventCount, events.Length),
                new OrchestrationRange(m_StateFrameCount, stateFrames.Length),
                new OrchestrationRange(m_ContextCount, contexts.Length));
            m_EventCount += events.Length;
            m_StateFrameCount += stateFrames.Length;
            m_ContextCount += contexts.Length;
        }
    }
}
