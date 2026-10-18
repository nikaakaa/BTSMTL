using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExecutionWorkspace<TValue> where TValue : unmanaged
    {
        internal readonly TValue[][] Inputs;
        internal readonly TValue[][] Outputs;
        internal readonly bool[] ActiveValues;
        internal readonly bool[] ForceStopVisited;
        internal readonly GraphCallContext[] GraphCallEntries;
        internal readonly StateContext[] States;
        internal readonly OrchestrationStateAncestor[] StateAncestors;
        internal readonly TValue[] InputObservations;
        internal readonly bool[] InputObservationWritten;
        internal readonly TValue[] OutputObservations;
        internal readonly bool[] OutputObservationWritten;
        readonly OrchestrationProgram<TValue> m_Program;
        readonly OrchestrationGraphContent<TValue> m_Content;
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
        internal readonly OrchestrationOutputRequest[] OutputRequests;
        internal readonly TValue[] OutputValues;
        internal int OutputRequestCount;
        internal int OutputValueCount;

        public OrchestrationExecutionWorkspace(OrchestrationProgram<TValue> program, OrchestrationGraphContent<TValue> content,
            OrchestrationExecutionCapacity capacity, int programIndex, OrchestrationProgramTickCapacity tickCapacity)
        {
            m_Program = program;
            m_Content = content;
            int executionFrameCapacity = capacity.State.FrameCount;
            // 同步调用栈沿 Timeline 父子链复用，不按并存执行帧重复准备。
            int depth = checked(capacity.SourceDepth(programIndex) * checked(program.OperationCount + 1));
            int maximumInputs = 4;
            int maximumOutputs = 4;
            for (int i = 0; i < program.OperationCount; i++)
            {
                ref readonly OrchestrationOperation operation = ref program.Operation(i);
                maximumInputs = Math.Max(maximumInputs, operation.Inputs.Count);
                maximumOutputs = Math.Max(maximumOutputs, operation.OutputCount);
                foreach (ref readonly OrchestrationHookBinding hook in program.Hooks(i))
                    maximumInputs = Math.Max(maximumInputs, hook.Inputs.Count);
            }
            Inputs = new TValue[depth][];
            Outputs = new TValue[depth][];
            for (int i = 0; i < depth; i++)
            {
                Inputs[i] = new TValue[maximumInputs];
                Outputs[i] = new TValue[maximumOutputs];
            }
            ActiveValues = new bool[program.ValueEvaluationSlotCount];
            ForceStopVisited = new bool[checked(executionFrameCapacity * program.OperationCount)];
            GraphCallEntries = new GraphCallContext[depth];
            States = new StateContext[depth];
            StateAncestors = new OrchestrationStateAncestor[program.OperationCount];
            InputObservations = new TValue[program.InputBindingCount];
            InputObservationWritten = new bool[program.InputBindingCount];
            OutputObservations = new TValue[program.ValueEvaluationSlotCount];
            OutputObservationWritten = new bool[program.ValueEvaluationSlotCount];
            OrchestrationProgramOutputCapacity output = tickCapacity.CursorOutputs;
            OutputRequests = new OrchestrationOutputRequest[output.RequestCount];
            OutputValues = new TValue[output.ValueCount];
            PrepareInvocationBindings(executionFrameCapacity);
        }

        public ReadOnlySpan<TValue> ObservedInputs => InputObservations;
        public ReadOnlySpan<bool> WrittenInputs => InputObservationWritten;
        public ReadOnlySpan<TValue> ObservedOutputs => OutputObservations;
        public ReadOnlySpan<bool> WrittenOutputs => OutputObservationWritten;
        public int OutputRequestCapacity => OutputRequests.Length;
        public int OutputValueCapacity => OutputValues.Length;
        public ReadOnlyMemory<OrchestrationGraphSource> TraceSources => m_Content.Sources;

        public void PrepareTraceCapture(OrchestrationProgramTickCapacity capacity)
        {
            OrchestrationTraceCapacity trace = capacity.CursorTrace;
            TraceEvents = new OrchestrationTraceEvent<TValue>[checked((int)trace.Events)];
            StateFrames = new OrchestrationStateTraceFrame[checked((int)trace.StateFrames)];
            TraceContexts = new OrchestrationTraceContext[checked((int)trace.Contexts)];
        }

        void PrepareInvocationBindings(int frames)
        {
            TraceBindings = new TraceBinding[m_Program.OperationCount];
            TraceFrameEntries = new int[frames];
            TraceBorrowedFrameEntries = new int[frames];
            TraceEntryScopes = new TraceEntryScope[States.Length];
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

        // 宿主采用候选后消费；下一次最外层入口会重置本批输出。
        public ReadOnlySpan<OrchestrationOutputRequest> EmittedOutputRequests => OutputRequests.AsSpan(0, OutputRequestCount);
        public ReadOnlySpan<TValue> EmittedOutputValues => OutputValues.AsSpan(0, OutputValueCount);

        internal void ResetOutputs()
        {
            OutputRequestCount = 0;
            OutputValueCount = 0;
        }

        internal struct StateContext
        {
            internal int ExecutionFrameIndex;
            internal int OperationIndex;
            internal int ExitCause;
            internal bool HasRootCompletedOverride;
            internal bool RootCompletedOverride;
            internal int TraceFrameIndex;
        }

        internal struct GraphCallContext
        {
            internal int ExecutionFrameIndex;
            internal int OperationIndex;
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
        readonly OrchestrationTraceEvent<TValue>[] m_Events;
        readonly OrchestrationStateTraceFrame[] m_StateFrames;
        readonly OrchestrationTraceContext[] m_Contexts;
        readonly OrchestrationTraceBatch[] m_Batches;
        int m_EventCount;
        int m_StateFrameCount;
        int m_ContextCount;
        int m_BatchCount;

        public OrchestrationExecutionTraceBuffer(OrchestrationProgramTickCapacity capacity)
        {
            OrchestrationTraceCapacity trace = capacity.TickTrace;
            m_Events = new OrchestrationTraceEvent<TValue>[checked((int)trace.Events)];
            m_StateFrames = new OrchestrationStateTraceFrame[checked((int)trace.StateFrames)];
            m_Contexts = new OrchestrationTraceContext[checked((int)trace.Contexts)];
            m_Batches = new OrchestrationTraceBatch[capacity.Budget.OuterInvocationCount];
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
