using System;

namespace BTSMTL.Orchestration
{
    // 一次推进的草稿区布局，准备期按程序与容量算出；宿主按 SizeBytes 提供内存并调用 Initialize。
    public readonly struct OrchestrationExecutionScratchLayout
    {
        OrchestrationExecutionScratchLayout(int sizeBytes, OrchestrationMemorySection inputValues,
            OrchestrationMemorySection outputValues, OrchestrationMemorySection scheduledValues,
            OrchestrationMemorySection scheduledPredictions, OrchestrationMemorySection graphCalls,
            OrchestrationMemorySection states, OrchestrationMemorySection stateAncestors,
            OrchestrationMemorySection forceStopVisited, OrchestrationMemorySection forceStopNext,
            OrchestrationMemorySection outputRequests, OrchestrationMemorySection emittedValues)
        {
            SizeBytes = sizeBytes;
            InputValues = inputValues;
            OutputValues = outputValues;
            ScheduledValues = scheduledValues;
            ScheduledPredictions = scheduledPredictions;
            GraphCalls = graphCalls;
            States = states;
            StateAncestors = stateAncestors;
            ForceStopVisited = forceStopVisited;
            ForceStopNext = forceStopNext;
            OutputRequests = outputRequests;
            EmittedValues = emittedValues;
        }

        public int SizeBytes { get; }
        internal OrchestrationMemorySection InputValues { get; }
        internal OrchestrationMemorySection OutputValues { get; }
        internal OrchestrationMemorySection ScheduledValues { get; }
        internal OrchestrationMemorySection ScheduledPredictions { get; }
        internal OrchestrationMemorySection GraphCalls { get; }
        internal OrchestrationMemorySection States { get; }
        internal OrchestrationMemorySection StateAncestors { get; }
        internal OrchestrationMemorySection ForceStopVisited { get; }
        internal OrchestrationMemorySection ForceStopNext { get; }
        internal OrchestrationMemorySection OutputRequests { get; }
        internal OrchestrationMemorySection EmittedValues { get; }

        public static OrchestrationExecutionScratchLayout Prepare<TValue>(in OrchestrationProgramView<TValue> program,
            int frameCapacity, in OrchestrationProgramOutputCapacity outputs) where TValue : unmanaged
        {
            var builder = new OrchestrationMemoryLayoutBuilder(
                OrchestrationMemoryImageWriter.SizeOf<OrchestrationExecutionScratchHeader>());
            int forceStopWords = (int)((checked((long)frameCapacity * program.OperationCount) + 63L) / 64);
            OrchestrationMemorySection inputValues = builder.Add<TValue>(program.WorkspaceInputValueCount);
            OrchestrationMemorySection outputValues = builder.Add<TValue>(program.WorkspaceOutputValueCount);
            OrchestrationMemorySection scheduledValues = builder.Add<TValue>(program.ScheduleScratchSlotCount);
            OrchestrationMemorySection scheduledPredictions = builder.Add<OrchestrationScheduledPrediction>(
                program.SchedulePredictionDepth);
            OrchestrationMemorySection graphCalls = builder.Add<OrchestrationGraphCallContext>(
                program.WorkspaceGraphCallDepth);
            OrchestrationMemorySection states = builder.Add<OrchestrationStateContext>(program.WorkspaceStateDepth);
            OrchestrationMemorySection stateAncestors = builder.Add<OrchestrationStateAncestor>(
                program.WorkspaceStateAncestorCount);
            OrchestrationMemorySection forceStopVisited = builder.Add<ulong>(forceStopWords);
            OrchestrationMemorySection forceStopNext = builder.Add<int>(forceStopWords);
            OrchestrationMemorySection outputRequests = builder.Add<OrchestrationOutputRequest>(outputs.RequestCount);
            OrchestrationMemorySection emittedValues = builder.Add<TValue>(outputs.ValueCount);
            return new OrchestrationExecutionScratchLayout(builder.SizeBytes, inputValues, outputValues, scheduledValues,
                scheduledPredictions, graphCalls, states, stateAncestors, forceStopVisited, forceStopNext,
                outputRequests, emittedValues);
        }

        // 强制停止位图与链表要求初值为零，其余区段先写后读。
        public void Initialize(Span<byte> memory)
        {
            if (memory.Length < SizeBytes)
                throw new ArgumentException("编排草稿内存小于准备布局。", nameof(memory));
            memory.Slice(0, SizeBytes).Clear();
            OrchestrationMemoryImageWriter.Header<OrchestrationExecutionScratchHeader>(memory) =
                new OrchestrationExecutionScratchHeader { Layout = this };
        }
    }

    internal struct OrchestrationExecutionScratchHeader
    {
        internal OrchestrationExecutionScratchLayout Layout;
        internal int OutputRequestCount;
        internal int OutputValueCount;
        internal OrchestrationExecutionError Error;
    }

    internal struct OrchestrationScheduledPrediction
    {
        internal int PreviousFrame;
        internal int PreviousOperation;
        internal bool SelectedFrame;
    }

    internal struct OrchestrationStateContext
    {
        internal int ExecutionFrameIndex;
        internal int OperationIndex;
        internal OrchestrationStopContext ExitContext;
        internal bool HasRootCompletedOverride;
        internal bool RootCompletedOverride;
        internal int TraceFrameIndex;
    }

    internal struct OrchestrationGraphCallContext
    {
        internal int ExecutionFrameIndex;
        internal int OperationIndex;
    }

    // 推进草稿视图，只持有宿主内存；表头在偏移 0，记录布局、输出计数与错误槽。
    public readonly ref struct OrchestrationExecutionScratch<TValue> where TValue : unmanaged
    {
        readonly Span<byte> m_Memory;

        public OrchestrationExecutionScratch(Span<byte> memory) => m_Memory = memory;

        internal ref OrchestrationExecutionScratchHeader Header =>
            ref OrchestrationMemoryImageWriter.Header<OrchestrationExecutionScratchHeader>(m_Memory);

        internal Span<T> Section<T>(OrchestrationMemorySection section) where T : unmanaged =>
            OrchestrationMemoryImageWriter.Section<T>(m_Memory, section);

        public ref readonly OrchestrationExecutionError Error => ref Header.Error;
        public int OutputRequestCapacity => Header.Layout.OutputRequests.Count;
        public int OutputValueCapacity => Header.Layout.EmittedValues.Count;

        // 宿主采用候选后消费；下一次最外层入口会重置本批输出。
        public ReadOnlySpan<OrchestrationOutputRequest> EmittedOutputRequests
        {
            get
            {
                ref OrchestrationExecutionScratchHeader header = ref Header;
                return Section<OrchestrationOutputRequest>(header.Layout.OutputRequests).Slice(0, header.OutputRequestCount);
            }
        }

        public ReadOnlySpan<TValue> EmittedOutputValues
        {
            get
            {
                ref OrchestrationExecutionScratchHeader header = ref Header;
                return Section<TValue>(header.Layout.EmittedValues).Slice(0, header.OutputValueCount);
            }
        }
    }

    // 托管宿主持有的草稿内存。
    public sealed class OrchestrationExecutionScratchMemory<TValue> where TValue : unmanaged
    {
        readonly byte[] m_Memory;

        public OrchestrationExecutionScratchMemory(in OrchestrationExecutionScratchLayout layout)
        {
            m_Memory = new byte[layout.SizeBytes];
            layout.Initialize(m_Memory);
        }

        public OrchestrationExecutionScratch<TValue> View => new OrchestrationExecutionScratch<TValue>(m_Memory);
        public ref readonly OrchestrationExecutionError Error => ref View.Error;
        public int OutputRequestCapacity => View.OutputRequestCapacity;
        public int OutputValueCapacity => View.OutputValueCapacity;
        public ReadOnlySpan<OrchestrationOutputRequest> EmittedOutputRequests => View.EmittedOutputRequests;
        public ReadOnlySpan<TValue> EmittedOutputValues => View.EmittedOutputValues;
    }
}
