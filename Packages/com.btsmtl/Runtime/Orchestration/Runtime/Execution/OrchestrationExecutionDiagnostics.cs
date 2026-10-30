using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public enum OrchestrationDiagnosticLifecycle : byte
    {
        Initialize,
        BeginInvocation,
        EnterEntry,
        LeaveEntry,
        RetireOperation,
        RetireFrame
    }

    public interface IOrchestrationExecutionDiagnostics<TValue> where TValue : unmanaged
    {
        int ScheduleEntry(in OrchestrationValueScheduleEntry entry);

        void Lifecycle<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>;

        int FindOverride<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>;

        void Observe<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, int slot, bool input, in TValue value)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>;

        void Trace<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>;

        int CaptureState<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, bool rootCompletedOverride)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>;
    }

    // 正式推进插头：不持有任何存储，诊断调用全部内联为空。
    public readonly struct OrchestrationProductDiagnostics<TValue> : IOrchestrationExecutionDiagnostics<TValue>
        where TValue : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ScheduleEntry(in OrchestrationValueScheduleEntry entry) => entry.Product;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Lifecycle<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FindOverride<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> => -1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Observe<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, int slot, bool input, in TValue value)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Trace<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CaptureState<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, bool rootCompletedOverride)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> => -1;
    }

    // 工作台采集插头：Trace、值观察、覆盖退休都写入托管存储，不进入 Burst。
    public readonly struct OrchestrationWorkbenchDiagnostics<TValue> : IOrchestrationExecutionDiagnostics<TValue>
        where TValue : unmanaged
    {
        readonly OrchestrationWorkbenchDiagnosticStorage<TValue> m_Storage;

        public OrchestrationWorkbenchDiagnostics(OrchestrationWorkbenchDiagnosticStorage<TValue> storage) =>
            m_Storage = storage ?? throw new ArgumentNullException(nameof(storage));

        public OrchestrationWorkbenchDiagnosticStorage<TValue> Storage => m_Storage;

        public int ScheduleEntry(in OrchestrationValueScheduleEntry entry) => entry.Workbench;

        public void Lifecycle<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationDiagnosticLifecycle lifecycle, int operation, int frame)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> =>
            cursor.DiagnosticLifecycle(m_Storage, lifecycle, operation, frame);

        public int FindOverride<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, int slot, OrchestrationValueOverrideKind kind, in OrchestrationBlackboardOwnerToken owner)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> =>
            cursor.FindDiagnosticOverride(m_Storage, operation, slot, kind, in owner);

        public void Observe<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, int slot, bool input, in TValue value)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> =>
            cursor.ObserveDiagnosticPort(m_Storage, operation, slot, input, in value);

        public void Trace<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            OrchestrationTraceKind kind, int operation, int edge, OrchestrationOperationResult result,
            uint stopCause, OrchestrationWaitReason waitReason, int valueSlot, bool selected, bool passed,
            in OrchestrationBlackboardSourceContext blackboardSource)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
        {
            if (kind == OrchestrationTraceKind.Edge)
                cursor.TraceDiagnosticEdge(m_Storage, edge, selected, passed, operation);
            else
                cursor.TraceDiagnosticOperation(m_Storage, kind, operation, result, stopCause, waitReason,
                    valueSlot, in blackboardSource);
        }

        public int CaptureState<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>(
            ref OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost> cursor,
            int operation, bool rootCompletedOverride)
            where TScalar : unmanaged where TVector2 : unmanaged where TVector3 : unmanaged
            where TYaw : unmanaged where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TContext : struct
            where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
            where TStorage : struct, IOrchestrationValueStorage<TValue>
            where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext> =>
            cursor.CaptureDiagnosticState(m_Storage, operation, rootCompletedOverride);
    }

    // 工作台采集存储：Trace、调用作用域绑定与值观察；按需准备，采集期间复用。
    public sealed class OrchestrationWorkbenchDiagnosticStorage<TValue> where TValue : unmanaged
    {
        readonly OrchestrationProgram<TValue> m_Program;
        readonly OrchestrationGraphContent<TValue> m_Content;
        readonly int m_ExecutionFrameCapacity;
        internal TValue[] InputObservations;
        internal bool[] InputObservationWritten;
        internal TValue[] OutputObservations;
        internal bool[] OutputObservationWritten;
        internal OrchestrationTraceEvent<TValue>[] TraceEvents = Array.Empty<OrchestrationTraceEvent<TValue>>();
        internal OrchestrationStateTraceFrame[] StateFrames = Array.Empty<OrchestrationStateTraceFrame>();
        internal OrchestrationTraceContext[] TraceContexts = Array.Empty<OrchestrationTraceContext>();
        internal TraceBinding[] TraceBindings;
        internal TraceInvocation[] TraceInvocations;
        internal OrchestrationRange[] TraceInvocationGroups;
        internal int[] TraceInvocationFrameOwners;
        internal int[] TraceFrameEntries = Array.Empty<int>();
        internal int[] TraceBorrowedFrameEntries = Array.Empty<int>();
        internal TraceEntryScope[] TraceEntryScopes;
        internal int TraceCount;
        internal int StateFrameCount;
        internal int TraceContextCount;
        internal int TraceEntryDepth;

        public OrchestrationWorkbenchDiagnosticStorage(OrchestrationProgram<TValue> program,
            OrchestrationGraphContent<TValue> content, int executionFrameCapacity)
        {
            m_Program = program;
            m_Content = content;
            m_ExecutionFrameCapacity = executionFrameCapacity;
        }

        // 宿主在需要逐端口观察时设置；采集插头在写入观察值时转发。
        public IOrchestrationValueObservationSink<TValue> Observations { get; set; }
        public ReadOnlySpan<TValue> ObservedInputs => InputObservations;
        public ReadOnlySpan<bool> WrittenInputs => InputObservationWritten;
        public ReadOnlySpan<TValue> ObservedOutputs => OutputObservations;
        public ReadOnlySpan<bool> WrittenOutputs => OutputObservationWritten;
        public ReadOnlyMemory<OrchestrationGraphSource> TraceSources => m_Content.Sources;
        public ReadOnlySpan<OrchestrationTraceEvent<TValue>> Trace => TraceEvents.AsSpan(0, TraceCount);
        public ReadOnlySpan<OrchestrationStateTraceFrame> StateTraceFrames => StateFrames.AsSpan(0, StateFrameCount);
        public ReadOnlySpan<OrchestrationTraceContext> Contexts => TraceContexts.AsSpan(0, TraceContextCount);

        internal void Initialize(bool captureValues)
        {
            if (captureValues)
            {
                Array.Clear(InputObservationWritten, 0, InputObservationWritten.Length);
                Array.Clear(OutputObservationWritten, 0, OutputObservationWritten.Length);
            }
            TraceFrameEntries.AsSpan().Fill(-1);
            TraceBorrowedFrameEntries.AsSpan().Fill(-1);
            TraceEntryDepth = 0;
        }

        internal void BeginInvocation()
        {
            TraceCount = 0;
            StateFrameCount = 0;
            TraceContextCount = 0;
        }

        public void PrepareValueCapture()
        {
            if (InputObservations != null)
                return;
            InputObservations = new TValue[m_Program.InputBindingCount];
            InputObservationWritten = new bool[m_Program.InputBindingCount];
            OutputObservations = new TValue[m_Program.ValueEvaluationSlotCount];
            OutputObservationWritten = new bool[m_Program.ValueEvaluationSlotCount];
            PrepareInvocationBindings();
        }

        public void PrepareTraceCapture(in OrchestrationTraceCapacity trace)
        {
            PrepareInvocationBindings();
            if (trace.Events > TraceEvents.Length)
                TraceEvents = new OrchestrationTraceEvent<TValue>[checked((int)trace.Events)];
            if (trace.StateFrames > StateFrames.Length)
                StateFrames = new OrchestrationStateTraceFrame[checked((int)trace.StateFrames)];
            if (trace.Contexts > TraceContexts.Length)
                TraceContexts = new OrchestrationTraceContext[checked((int)trace.Contexts)];
        }

        public void PrepareInvocationBindings()
        {
            if (TraceBindings != null)
                return;
            TraceBindings = new TraceBinding[m_Program.OperationCount];
            TraceFrameEntries = new int[m_ExecutionFrameCapacity];
            TraceBorrowedFrameEntries = new int[m_ExecutionFrameCapacity];
            TraceEntryScopes = new TraceEntryScope[m_Program.Content.WorkspaceCapacity.EntryDepth];
            ReadOnlySpan<OrchestrationGraphSource> sources = m_Content.Sources.Span;
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
            TraceInvocations = new TraceInvocation[invocationCount];
            TraceInvocationGroups = new OrchestrationRange[groups.Count];
            TraceInvocationFrameOwners = new int[groups.Count];
            int cursor = 0;
            for (int group = 0; group < groups.Count; group++)
            {
                List<int> entries = groups[group];
                TraceInvocationGroups[group] = new OrchestrationRange(cursor, entries.Count);
                var entryOperations = new HashSet<int>();
                foreach (int sourceIndex in entries)
                {
                    OrchestrationGraphExecutionSource source = sources[sourceIndex].Source;
                    if (!entryOperations.Add(source.InvocationEntry.OperationIndex))
                        throw new ArgumentException($"Trace invocation '{source.SourceInvocationPath}' has multiple sources for entry '{source.InvocationEntry.OperationIndex}'.");
                    TraceInvocationFrameOwners[group] = m_Program.Operation(source.InvocationEntry.OperationIndex)
                        .ExecutionFrameOwnerOperationIndex;
                    TraceInvocations[cursor++] = new TraceInvocation(sourceIndex, source.TargetIndex,
                        source.InvocationEntry.OperationIndex, source.ParentInvocationPath.Length == 0 ? -1 :
                            groupIndexes[source.ParentInvocationPath]);
                }
            }
            for (int operation = 0; operation < m_Program.OperationCount; operation++)
            {
                if (!operationSources.TryGetValue(operation, out int sourceIndex))
                    throw new ArgumentException($"Trace capture requires an Operation Source for operation '{operation}'.");
                TraceBindings[operation] = new TraceBinding(sourceIndex,
                    groupIndexes[sources[sourceIndex].Source.GraphInvocationPath]);
            }
        }

        internal readonly struct TraceBinding
        {
            internal TraceBinding(int sourceIndex, int invocationGroupIndex)
            {
                SourceIndex = sourceIndex;
                InvocationGroupIndex = invocationGroupIndex;
            }

            internal int SourceIndex { get; }
            internal int InvocationGroupIndex { get; }
        }

        internal readonly struct TraceInvocation
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

        internal readonly struct TraceEntryScope
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
