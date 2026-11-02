using System;

namespace BTSMTL.Orchestration
{
    // 推进入口唯一的执行环境插头：扩展调用、Timeline 派发、片段退出、值存储与诊断由同一个结构体提供。
    // 调用上下文由插头自己持有；Burst 宿主用纯数据结构体实现，普通宿主使用 ManagedOrchestrationHost。
    public interface IOrchestrationHost<TValue> : IOrchestrationValueStorage<TValue>
        where TValue : unmanaged
    {
        OrchestrationExtensionExecutionContract ExecutionContract(int bindingIndex);
        OrchestrationExtensionResult InvokeReadOnly(int bindingIndex, in OrchestrationExtensionArguments<TValue> invocation);
        OrchestrationExtensionResult InvokeCandidate(int bindingIndex, in OrchestrationExtensionArguments<TValue> invocation);

        OrchestrationOperationResult Tick<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs, ulong tick)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        OrchestrationStopStatus Stop<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue>;

        OrchestrationTime ReadTime();
        void ReleasePlayback(OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity);
        void SelectExecutionFrame(int frameSlot);
        void ReleaseExecutionFrame(int frameSlot, in OrchestrationStopContext stopContext);
        OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate, int operationIndex, ulong tick);

        // 生产与工作台的区别由诊断类型在编译期决定，执行器不做逐节点开关判断。
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

    // 普通托管宿主：上下文、扩展、Timeline 调度对象与诊断由宿主组合；存储与诊断仍按值类型泛型实现。
    public struct ManagedOrchestrationHost<TValue, TContext, TStorage, TDiagnostics> : IOrchestrationHost<TValue>
        where TValue : unmanaged
        where TContext : struct
        where TStorage : struct, IOrchestrationValueStorage<TValue>
        where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
    {
        readonly IOrchestrationExtensionBindings<TValue, TContext> m_Extensions;
        readonly IOrchestrationTimelineDispatcher<TValue, TContext> m_Timeline;
        readonly IOrchestrationTimelineClipExitDispatcher<TContext> m_ClipExit;
        readonly TDiagnostics m_Diagnostics;
        TContext m_Context;

        public ManagedOrchestrationHost(in TContext context, TDiagnostics diagnostics,
            IOrchestrationExtensionBindings<TValue, TContext> extensions,
            IOrchestrationTimelineDispatcher<TValue, TContext> timeline = null,
            IOrchestrationTimelineClipExitDispatcher<TContext> clipExit = null)
        {
            m_Context = context;
            m_Diagnostics = diagnostics;
            m_Extensions = extensions;
            m_Timeline = timeline;
            m_ClipExit = clipExit ?? timeline;
        }

        public void Read(ReadOnlySpan<byte> memory, in OrchestrationValueAddress address, ref TValue destination) =>
            default(TStorage).Read(memory, in address, ref destination);

        public void Write(Span<byte> memory, in OrchestrationValueAddress address, in TValue value) =>
            default(TStorage).Write(memory, in address, in value);

        public OrchestrationExtensionExecutionContract ExecutionContract(int bindingIndex) =>
            m_Extensions.ExecutionContract(bindingIndex);

        public OrchestrationExtensionResult InvokeReadOnly(int bindingIndex,
            in OrchestrationExtensionArguments<TValue> invocation) =>
            m_Extensions.InvokeReadOnly(bindingIndex, in m_Context, in invocation);

        public OrchestrationExtensionResult InvokeCandidate(int bindingIndex,
            in OrchestrationExtensionArguments<TValue> invocation) =>
            m_Extensions.InvokeCandidate(bindingIndex, ref m_Context, in invocation);

        public OrchestrationOperationResult Tick<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, int contentIdentityId,
            ulong operationGeneration, ref ulong playbackIdentity, ReadOnlySpan<TValue> inputs, ulong tick)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Timeline.Tick(ref cursor, candidate, operationIndex, contentIdentityId, operationGeneration,
                ref playbackIdentity, inputs, tick, ref m_Context);

        public OrchestrationStopStatus Stop<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity,
            in OrchestrationStopContext stopContext)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Timeline.Stop(ref cursor, candidate, operationIndex, ref playbackIdentity, in stopContext,
                ref m_Context);

        public OrchestrationTime ReadTime() => m_Timeline.ReadTime();

        public void ReleasePlayback(OrchestrationStateView candidate, int operationIndex, ref ulong playbackIdentity) =>
            m_Timeline.ReleasePlayback(candidate, operationIndex, ref playbackIdentity, in m_Context);

        public void SelectExecutionFrame(int frameSlot) => m_Timeline?.SelectExecutionFrame(frameSlot, ref m_Context);

        public void ReleaseExecutionFrame(int frameSlot, in OrchestrationStopContext stopContext) =>
            m_Timeline.ReleaseExecutionFrame(frameSlot, in stopContext, ref m_Context);

        public OrchestrationOperationResult RequestClipExit(OrchestrationStateView candidate, int operationIndex, ulong tick) =>
            m_ClipExit.RequestClipExit(candidate, operationIndex, tick, ref m_Context);

        public int ScheduleEntry(in OrchestrationValueScheduleEntry entry) => m_Diagnostics.ScheduleEntry(in entry);

        public void Lifecycle<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Diagnostics.Lifecycle(ref cursor, lifecycle, operation, frame);

        public int FindOverride<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Diagnostics.FindOverride(ref cursor, operation, slot, kind, in owner);

        public void Observe<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, int slot, bool input, in TValue value)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Diagnostics.Observe(ref cursor, operation, slot, input, in value);

        public void Trace<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Diagnostics.Trace(ref cursor, kind, operation, edge, result, stopCause, waitReason, valueSlot,
                selected, passed, in blackboardSource);

        public int CaptureState<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            int operation, bool rootCompletedOverride)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Diagnostics.CaptureState(ref cursor, operation, rootCompletedOverride);

        public void ReadEntryContext<TNumerics, THost>(ref OrchestrationExecutionCursor<TValue, TNumerics, THost> cursor,
            in OrchestrationExecutionEntry entry, out OrchestrationTraceContext context)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            m_Diagnostics.ReadEntryContext(ref cursor, in entry, out context);

        public OrchestrationExecutionCursor<TValue, TNumerics, ManagedOrchestrationHost<TValue, TContext, TStorage, TDiagnostics>>
            CreateCursor<TNumerics>(OrchestrationCandidateState<TValue> state,
                OrchestrationProgramView<TValue> program, OrchestrationExecutionScratch<TValue> scratch, int frameSlot,
                ReadOnlySpan<int> characterSlots, in OrchestrationFrameInput<TValue> input,
                OrchestrationIdentityOrdinalView identities)
            where TNumerics : struct, IOrchestrationNumerics<TValue> =>
            state.CreateCursor<TNumerics, ManagedOrchestrationHost<TValue, TContext, TStorage, TDiagnostics>>(
                program, scratch, frameSlot, characterSlots, in input, identities, this);

        public OrchestrationExecutionCursor<TValue, TNumerics, ManagedOrchestrationHost<TValue, TContext, TStorage, TDiagnostics>>
            CreateCursor<TNumerics>(OrchestrationCandidateState<TValue> state,
                OrchestrationProgramView<TValue> program, OrchestrationExecutionSourceBindingView sourceBinding,
                OrchestrationExecutionScratch<TValue> scratch, int frameSlot, OrchestrationExecutionSource<TValue> source,
                ReadOnlySpan<int> characterSlots, in OrchestrationFrameInput<TValue> input,
                OrchestrationIdentityOrdinalView identities)
            where TNumerics : struct, IOrchestrationNumerics<TValue> =>
            state.CreateCursor<TNumerics, ManagedOrchestrationHost<TValue, TContext, TStorage, TDiagnostics>>(
                program, sourceBinding, scratch, frameSlot, source, characterSlots, in input, identities, this);
    }
}
