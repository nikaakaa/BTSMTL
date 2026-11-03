using System;
using System.Runtime.CompilerServices;

namespace BTSMTL.Orchestration
{
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
            in OrchestrationBlackboardSourceContext blackboardSource)
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
            in OrchestrationBlackboardSourceContext blackboardSource)
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
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>
        {
            if (kind == OrchestrationTraceKind.Edge)
                m_Storage.TraceEdge(ref cursor, edge, selected, passed, operation);
            else
                m_Storage.TraceOperation(ref cursor, kind, operation, result, stopCause, waitReason,
                    valueSlot, in blackboardSource);
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
