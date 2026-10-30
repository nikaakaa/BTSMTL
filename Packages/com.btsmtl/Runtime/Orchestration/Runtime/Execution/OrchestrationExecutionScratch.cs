using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    // 一次推进的草稿区布局，准备期按程序与容量算出；宿主按 SizeBytes 提供内存并调用 Initialize。
    public readonly struct OrchestrationExecutionScratchLayout
    {
        const int Alignment = 16;

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
            int offset = OrchestrationMemoryImageWriter.SizeOf<OrchestrationExecutionScratchHeader>();
            int forceStopWords = (int)((checked((long)frameCapacity * program.OperationCount) + 63L) / 64);
            OrchestrationMemorySection inputValues = Add<TValue>(ref offset, program.WorkspaceInputValueCount);
            OrchestrationMemorySection outputValues = Add<TValue>(ref offset, program.WorkspaceOutputValueCount);
            OrchestrationMemorySection scheduledValues = Add<TValue>(ref offset, program.ScheduleScratchSlotCount);
            OrchestrationMemorySection scheduledPredictions = Add<OrchestrationScheduledPrediction>(ref offset,
                program.SchedulePredictionDepth);
            OrchestrationMemorySection graphCalls = Add<OrchestrationGraphCallContext>(ref offset,
                program.WorkspaceGraphCallDepth);
            OrchestrationMemorySection states = Add<OrchestrationStateContext>(ref offset, program.WorkspaceStateDepth);
            OrchestrationMemorySection stateAncestors = Add<OrchestrationStateAncestor>(ref offset,
                program.WorkspaceStateAncestorCount);
            OrchestrationMemorySection forceStopVisited = Add<ulong>(ref offset, forceStopWords);
            OrchestrationMemorySection forceStopNext = Add<int>(ref offset, forceStopWords);
            OrchestrationMemorySection outputRequests = Add<OrchestrationOutputRequest>(ref offset, outputs.RequestCount);
            OrchestrationMemorySection emittedValues = Add<TValue>(ref offset, outputs.ValueCount);
            return new OrchestrationExecutionScratchLayout(Align(offset), inputValues, outputValues, scheduledValues,
                scheduledPredictions, graphCalls, states, stateAncestors, forceStopVisited, forceStopNext,
                outputRequests, emittedValues);
        }

        // 强制停止位图与链表要求初值为零，其余区段先写后读。
        public void Initialize(Span<byte> memory)
        {
            if (memory.Length < SizeBytes)
                throw new ArgumentException("编排草稿内存小于准备布局。", nameof(memory));
            memory.Slice(0, SizeBytes).Clear();
            SpanReinterpret.Cast<byte, OrchestrationExecutionScratchHeader>(memory.Slice(0,
                OrchestrationMemoryImageWriter.SizeOf<OrchestrationExecutionScratchHeader>()))[0] =
                new OrchestrationExecutionScratchHeader { Layout = this };
        }

        static OrchestrationMemorySection Add<T>(ref int offset, int count) where T : unmanaged
        {
            int start = Align(offset);
            offset = checked(start + count * OrchestrationMemoryImageWriter.SizeOf<T>());
            return new OrchestrationMemorySection(start, count);
        }

        static int Align(int value) => checked(value + Alignment - 1) & ~(Alignment - 1);
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
        internal int ExitCause;
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
            ref SpanReinterpret.Cast<byte, OrchestrationExecutionScratchHeader>(m_Memory.Slice(0,
                OrchestrationMemoryImageWriter.SizeOf<OrchestrationExecutionScratchHeader>()))[0];

        internal Span<T> Section<T>(OrchestrationMemorySection section) where T : unmanaged =>
            SpanReinterpret.Cast<byte, T>(m_Memory.Slice(section.OffsetBytes,
                section.Count * OrchestrationMemoryImageWriter.SizeOf<T>()));

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
