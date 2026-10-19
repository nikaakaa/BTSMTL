using System;
using System.Collections.Generic;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationProgramTickBudget
    {
        public OrchestrationProgramTickBudget(int outerInvocationCount, int standaloneEvaluationCount)
        {
            OuterInvocationCount = outerInvocationCount;
            StandaloneEvaluationCount = standaloneEvaluationCount;
        }

        public int OuterInvocationCount { get; }
        public int StandaloneEvaluationCount { get; }
    }

    public readonly struct OrchestrationTimelineEntryTickCapacity
    {
        public OrchestrationTimelineEntryTickCapacity(int operationIndex, int advanceInvocations, int stopInvocations)
        {
            OperationIndex = operationIndex;
            AdvanceInvocations = advanceInvocations;
            StopInvocations = stopInvocations;
        }

        public int OperationIndex { get; }
        public int AdvanceInvocations { get; }
        public int StopInvocations { get; }
    }

    public readonly struct OrchestrationTimelineTickContentCapacity
    {
        public OrchestrationTimelineTickContentCapacity(int programIndex, int operationIndex,
            int graphInvocationsPerAdvance, int graphInvocationsPerStop,
            int logicSampleInvocationsPerAdvance, int logicSampleRowsPerAdvance,
            ReadOnlyMemory<OrchestrationTimelineEntryTickCapacity> graphEntries)
        {
            ProgramIndex = programIndex;
            OperationIndex = operationIndex;
            GraphInvocationsPerAdvance = graphInvocationsPerAdvance;
            GraphInvocationsPerStop = graphInvocationsPerStop;
            LogicSampleInvocationsPerAdvance = logicSampleInvocationsPerAdvance;
            LogicSampleRowsPerAdvance = logicSampleRowsPerAdvance;
            GraphEntries = graphEntries;
        }

        public int ProgramIndex { get; }
        public int OperationIndex { get; }
        public int GraphInvocationsPerAdvance { get; }
        public int GraphInvocationsPerStop { get; }
        public int LogicSampleInvocationsPerAdvance { get; }
        public int LogicSampleRowsPerAdvance { get; }
        public ReadOnlyMemory<OrchestrationTimelineEntryTickCapacity> GraphEntries { get; }
    }

    public readonly struct OrchestrationOperationTickCapacity
    {
        internal OrchestrationOperationTickCapacity(long executeInvocations, long evaluateInvocations,
            long stopInvocations, long lifecycleInvocations, long blackboardFactsInvocations,
            long retainedGenerations, long outputInvocations)
        {
            ExecuteInvocations = executeInvocations;
            EvaluateInvocations = evaluateInvocations;
            StopInvocations = stopInvocations;
            LifecycleInvocations = lifecycleInvocations;
            BlackboardFactsInvocations = blackboardFactsInvocations;
            RetainedGenerations = retainedGenerations;
            OutputInvocations = outputInvocations;
            ExtensionInvocations = checked(executeInvocations + evaluateInvocations + stopInvocations +
                lifecycleInvocations + blackboardFactsInvocations);
        }

        public long ExecuteInvocations { get; }
        public long EvaluateInvocations { get; }
        public long StopInvocations { get; }
        public long LifecycleInvocations { get; }
        public long BlackboardFactsInvocations { get; }
        public long RetainedGenerations { get; }
        public long OutputInvocations { get; }
        public long ExtensionInvocations { get; }
    }

    public readonly struct OrchestrationTimelineTickCapacity
    {
        internal OrchestrationTimelineTickCapacity(long advanceCount, long stopCount,
            long playbackCreationCount, long heldPlaybackIdentityCount,
            long graphInvocationCount, long logicSampleInvocationCount, long logicSampleRowCount)
        {
            AdvanceCount = advanceCount;
            StopCount = stopCount;
            PlaybackCreationCount = playbackCreationCount;
            HeldPlaybackIdentityCount = heldPlaybackIdentityCount;
            GraphInvocationCount = graphInvocationCount;
            LogicSampleInvocationCount = logicSampleInvocationCount;
            LogicSampleRowCount = logicSampleRowCount;
        }

        public long AdvanceCount { get; }
        public long StopCount { get; }
        public long PlaybackCreationCount { get; }
        public long HeldPlaybackIdentityCount { get; }
        public long GraphInvocationCount { get; }
        public long LogicSampleInvocationCount { get; }
        public long LogicSampleRowCount { get; }
    }

    public readonly struct OrchestrationTraceCapacity
    {
        public OrchestrationTraceCapacity(long events, long stateFrames)
        {
            Events = events;
            StateFrames = stateFrames;
        }

        public long Events { get; }
        public long StateFrames { get; }
        public long Contexts => checked(Events + StateFrames);
    }

    public sealed class OrchestrationProgramTickCapacity
    {
        internal OrchestrationProgramTickCapacity(in OrchestrationProgramTickBudget budget,
            long countedOperationEvaluations, OrchestrationOperationTickCapacity[] operations,
            OrchestrationTimelineTickCapacity[] timelines, in OrchestrationProgramOutputCapacity cursorOutputs,
            in OrchestrationTraceCapacity cursorTrace)
        {
            Budget = budget;
            CountedOperationEvaluations = countedOperationEvaluations;
            Operations = operations;
            Timelines = timelines;
            CursorOutputs = cursorOutputs;
            CursorTrace = cursorTrace;
            TickTrace = new OrchestrationTraceCapacity(
                checked(cursorTrace.Events * budget.OuterInvocationCount),
                checked(cursorTrace.StateFrames * budget.OuterInvocationCount));
            long extensions = 0;
            long samples = 0;
            long rows = 0;
            for (int operation = 0; operation < operations.Length; operation++)
            {
                extensions = checked(extensions + operations[operation].ExtensionInvocations);
                samples = checked(samples + timelines[operation].LogicSampleInvocationCount);
                rows = checked(rows + timelines[operation].LogicSampleRowCount);
            }
            ExtensionInvocations = checked(extensions + samples);
            LogicSampleInvocations = samples;
            LogicSampleRows = rows;
        }

        public OrchestrationProgramTickBudget Budget { get; }
        public long CountedOperationEvaluations { get; }
        public ReadOnlyMemory<OrchestrationOperationTickCapacity> Operations { get; }
        public ReadOnlyMemory<OrchestrationTimelineTickCapacity> Timelines { get; }
        public long ExtensionInvocations { get; }
        public long LogicSampleInvocations { get; }
        public long LogicSampleRows { get; }
        public OrchestrationProgramOutputCapacity CursorOutputs { get; }
        public OrchestrationTraceCapacity CursorTrace { get; }
        public OrchestrationTraceCapacity TickTrace { get; }
    }

    public sealed class OrchestrationExecutionTickCapacity
    {
        OrchestrationExecutionTickCapacity(OrchestrationProgramTickCapacity[] programs)
        {
            Programs = programs;
        }

        public ReadOnlyMemory<OrchestrationProgramTickCapacity> Programs { get; }

        public static OrchestrationExecutionTickCapacity Prepare<TValue>(
            ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<ReadOnlyMemory<OrchestrationExtensionReference>> extensions,
            ReadOnlySpan<OrchestrationProgramTickBudget> budgets,
            OrchestrationExecutionCapacity executionCapacity,
            ReadOnlySpan<OrchestrationTimelineTickContentCapacity> timelineContents) where TValue : unmanaged
        {
            if (extensions.Length != programs.Length || budgets.Length != programs.Length ||
                executionCapacity.ProgramCount != programs.Length)
                throw new ArgumentException("Tick capacity inputs must use the prepared program index space.");
            var timelines = new Dictionary<(int Program, int Operation), OrchestrationTimelineTickContentCapacity>();
            foreach (ref readonly OrchestrationTimelineTickContentCapacity content in timelineContents)
            {
                if ((uint)content.ProgramIndex >= (uint)programs.Length ||
                    (uint)content.OperationIndex >= (uint)programs[content.ProgramIndex].OperationCount ||
                    programs[content.ProgramIndex].Operation(content.OperationIndex).Code != OrchestrationOperationCode.Timeline ||
                    content.GraphInvocationsPerAdvance < 0 || content.GraphInvocationsPerStop < 0 ||
                    content.LogicSampleInvocationsPerAdvance < 0 || content.LogicSampleRowsPerAdvance < 0)
                    throw new ArgumentException("A Timeline Tick capacity does not describe a prepared Timeline operation.");
                timelines.Add((content.ProgramIndex, content.OperationIndex), content);
            }
            var result = new OrchestrationProgramTickCapacity[programs.Length];
            for (int programIndex = 0; programIndex < programs.Length; programIndex++)
            {
                ref readonly OrchestrationProgramTickBudget budget = ref budgets[programIndex];
                if (budget.OuterInvocationCount < 0 || budget.StandaloneEvaluationCount < 0)
                    throw new ArgumentException($"Program '{programIndex}' has a negative Tick invocation budget.");
                OrchestrationProgram<TValue> program = programs[programIndex];
                var analysis = new ProgramAnalysis<TValue>(program, extensions[programIndex], programIndex, timelines);
                analysis.Analyze(budget.OuterInvocationCount > 0);
                OrchestrationTraceCapacity trace = analysis.AnalyzeTrace(budget.OuterInvocationCount > 0);
                long execution = checked((long)budget.OuterInvocationCount * analysis.InvocationEvaluations);
                long evaluations = checked(execution +
                    (long)budget.StandaloneEvaluationCount * analysis.ValueEvaluations);
                long stopTraversals = checked(budget.OuterInvocationCount + execution);
                var operations = new OrchestrationOperationTickCapacity[program.OperationCount];
                var timelineResults = new OrchestrationTimelineTickCapacity[program.OperationCount];
                ReadOnlySpan<int> owners = executionCapacity.OwnerInstances(programIndex).Span;
                int outputRequests = 0;
                int outputValues = 0;
                for (int operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
                {
                    ref readonly OrchestrationOperation operation = ref program.Operation(operationIndex);
                    bool extension = operation.Code == OrchestrationOperationCode.Extension;
                    bool action = extension && extensions[programIndex].Span[program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex].Kind ==
                        OrchestrationExtensionKind.Action;
                    long stops = checked(stopTraversals * analysis.StopVisits[operationIndex]);
                    long hooks = checked(execution * analysis.ExecutionHooks(operationIndex) +
                        stops * analysis.ExitHooks(operationIndex));
                    long evaluate = extension && !action
                        ? checked(evaluations * checked(1 + analysis.UncountedEvaluations[operationIndex])) : 0;
                    long facts = checked(evaluations * checked(analysis.DirectFacts(operationIndex) +
                        analysis.UncountedFacts[operationIndex]));
                    long outputInvocations = operation.Code == OrchestrationOperationCode.EmitOutput
                        ? analysis.ExecuteVisits[operationIndex] : 0;
                    if (operation.Code == OrchestrationOperationCode.EmitOutput &&
                        owners[operation.ExecutionFrameOwnerOperationIndex] != 0)
                    {
                        outputRequests = checked(outputRequests + (int)outputInvocations);
                        outputValues = checked(outputValues + (int)(outputInvocations *
                            program.OutputTemplate(program.Payload<OrchestrationOutputPayload>(operationIndex).TemplateIndex).Ports.Length));
                    }
                    operations[operationIndex] = new OrchestrationOperationTickCapacity(
                        action ? checked((long)budget.OuterInvocationCount * analysis.ExecuteVisits[operationIndex]) : 0,
                        evaluate, action ? stops : 0, hooks, facts,
                        checked(owners[operation.ExecutionFrameOwnerOperationIndex] + execution),
                        checked((long)budget.OuterInvocationCount * outputInvocations));
                    if (operation.Code != OrchestrationOperationCode.Timeline)
                        continue;
                    if (!timelines.TryGetValue((programIndex, operationIndex), out OrchestrationTimelineTickContentCapacity content))
                        throw new InvalidOperationException($"Program '{programIndex}' Timeline '{operationIndex}' lacks its prepared Tick sampling capacity.");
                    long timelineExecutions = checked((long)budget.OuterInvocationCount *
                        analysis.ExecuteVisits[operationIndex]);
                    long identities = checked(owners[operation.ExecutionFrameOwnerOperationIndex] + timelineExecutions);
                    long timelineStops = budget.OuterInvocationCount == 0 ? 0 : identities;
                    long graphInvocations = checked(timelineExecutions * content.GraphInvocationsPerAdvance +
                        timelineStops * content.GraphInvocationsPerStop);
                    timelineResults[operationIndex] = new OrchestrationTimelineTickCapacity(timelineExecutions,
                        timelineStops, timelineExecutions, identities, graphInvocations,
                        checked(timelineExecutions * content.LogicSampleInvocationsPerAdvance),
                        checked(timelineExecutions * content.LogicSampleRowsPerAdvance));
                }
                var cursorOutputs = new OrchestrationProgramOutputCapacity(outputRequests, outputValues);
                result[programIndex] = new OrchestrationProgramTickCapacity(in budget, evaluations,
                    operations, timelineResults, in cursorOutputs, in trace);
            }
            return new OrchestrationExecutionTickCapacity(result);
        }

        sealed class ProgramAnalysis<TValue> where TValue : unmanaged
        {
            readonly OrchestrationProgram<TValue> m_Program;
            readonly ReadOnlyMemory<OrchestrationExtensionReference> m_Extensions;
            readonly bool[] m_ActiveStops;
            readonly long[] m_StopVisits;
            readonly int m_ProgramIndex;
            readonly Dictionary<(int Program, int Operation), OrchestrationTimelineTickContentCapacity> m_Timelines;
            readonly long[][] m_BoundedVisits;
            readonly bool[][] m_ActiveVisits;
            readonly long[] m_ValueVisits;
            int m_CountTarget = -1;
            CapacityMetric m_Metric;

            enum CapacityMetric { Evaluations, TraceEvents, StateFrames }

            internal ProgramAnalysis(OrchestrationProgram<TValue> program,
                ReadOnlyMemory<OrchestrationExtensionReference> extensions, int programIndex,
                Dictionary<(int Program, int Operation), OrchestrationTimelineTickContentCapacity> timelines)
            {
                m_Program = program;
                m_Extensions = extensions;
                m_ActiveStops = new bool[program.OperationCount];
                m_StopVisits = new long[program.OperationCount];
                m_ProgramIndex = programIndex;
                m_Timelines = timelines;
                m_BoundedVisits = new[] { new long[program.OperationCount], new long[program.OperationCount] };
                m_ActiveVisits = new[] { new bool[program.OperationCount], new bool[program.OperationCount] };
                m_ValueVisits = new long[program.ValueEvaluationSlotCount];
                StopVisits = new long[program.OperationCount];
                UncountedEvaluations = new long[program.OperationCount];
                UncountedFacts = new long[program.OperationCount];
                ExecuteVisits = new long[program.OperationCount];
            }

            internal long[] StopVisits { get; }
            internal long[] UncountedEvaluations { get; }
            internal long[] UncountedFacts { get; }
            internal long[] ExecuteVisits { get; }
            internal long InvocationEvaluations { get; private set; }
            internal long ValueEvaluations { get; private set; }

            internal void Analyze(bool includeStops)
            {
                for (int operation = 0; operation < m_Program.OperationCount; operation++)
                {
                    for (int output = -1; output < m_Program.Operation(operation).OutputCount; output++)
                        ValueEvaluations = Math.Max(ValueEvaluations, ValueVisits(operation, output));
                    if (includeStops)
                        InvocationEvaluations = Math.Max(InvocationEvaluations,
                            BoundedVisits(operation, false) + BoundedVisits(operation, true));
                }
                // 每个操作都作为根，普通停止的上界同时覆盖 ForceStop 单次 visited 上界 1。
                for (int root = 0; includeStops && root < m_Program.OperationCount; root++)
                {
                    Array.Clear(m_StopVisits, 0, m_StopVisits.Length);
                    VisitOrdinaryStop(root);
                    for (int operation = 0; operation < StopVisits.Length; operation++)
                        StopVisits[operation] = Math.Max(StopVisits[operation], m_StopVisits[operation]);
                }
                AnalyzeValues(-1);
                for (int state = 0; state < m_Program.OperationCount; state++)
                    if (m_Program.Operation(state).Code == OrchestrationOperationCode.State)
                        AnalyzeValues(state);
                for (int target = 0; includeStops && target < m_Program.OperationCount; target++)
                    if (m_Program.Operation(target).Code == OrchestrationOperationCode.Timeline ||
                        m_Program.Operation(target).Code == OrchestrationOperationCode.EmitOutput ||
                        m_Program.Operation(target).Code == OrchestrationOperationCode.Extension &&
                        m_Extensions.Span[m_Program.Payload<OrchestrationExtensionPayload>(target).BindingIndex].Kind == OrchestrationExtensionKind.Action)
                    {
                        m_CountTarget = target;
                        Array.Clear(m_BoundedVisits[0], 0, m_BoundedVisits[0].Length);
                        Array.Clear(m_BoundedVisits[1], 0, m_BoundedVisits[1].Length);
                        for (int root = 0; root < m_Program.OperationCount; root++)
                            ExecuteVisits[target] = Math.Max(ExecuteVisits[target],
                                checked(BoundedVisits(root, false) + BoundedVisits(root, true)));
                    }
            }

            internal OrchestrationTraceCapacity AnalyzeTrace(bool enabled)
            {
                long events = enabled ? TraceMetric(CapacityMetric.TraceEvents) : 0;
                long states = enabled ? TraceMetric(CapacityMetric.StateFrames) : 0;
                return new OrchestrationTraceCapacity(events, states);
            }

            long TraceMetric(CapacityMetric metric)
            {
                m_CountTarget = -1;
                m_Metric = metric;
                Array.Clear(m_BoundedVisits[0], 0, m_BoundedVisits[0].Length);
                Array.Clear(m_BoundedVisits[1], 0, m_BoundedVisits[1].Length);
                long maximum = 0;
                for (int root = 0; root < m_Program.OperationCount; root++)
                    maximum = Math.Max(maximum, checked(BoundedVisits(root, false) + BoundedVisits(root, true)));
                return maximum;
            }

            long LocalTraceCost(int index, bool stop)
            {
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(index);
                if (m_Metric == CapacityMetric.StateFrames)
                {
                    if (operation.Code == OrchestrationOperationCode.State)
                        // 两次生命周期事件和两次主体／进入退出作用域各保存一条状态路径。
                        return checked(4L * StateDepth(index));
                    if (operation.Code != OrchestrationOperationCode.StateMachine || stop)
                        return 0;
                    int depth = 0;
                    for (int state = 0; state < m_Program.OperationCount; state++)
                        if (m_Program.Operation(state).Code == OrchestrationOperationCode.State &&
                            m_Program.Payload<OrchestrationStatePayload>(state).MachineIndex == index)
                            depth = Math.Max(depth, StateDepth(state));
                    // 当前状态始终选择转移；AnyState 只在正式入口存在时参与。
                    return checked((m_Program.AnyStateEntry(index) >= 0 ? 2L : 1L) * depth);
                }
                long count = operation.IsRunnable ? 2 : 0;
                if (stop)
                    return count;
                if (operation.Code is OrchestrationOperationCode.Root or OrchestrationOperationCode.StateOnEnter or
                    OrchestrationOperationCode.StateOnExit or OrchestrationOperationCode.TimelineEnter or
                    OrchestrationOperationCode.Loop or OrchestrationOperationCode.Sequence or
                    OrchestrationOperationCode.Selector or OrchestrationOperationCode.Parallel or
                    OrchestrationOperationCode.SubGraph or OrchestrationOperationCode.State or
                    OrchestrationOperationCode.StateMachine)
                    count++;
                if (operation.Code == OrchestrationOperationCode.Loop)
                    count = checked(count + 2);
                if (operation.Code == OrchestrationOperationCode.StateMachine)
                    count = checked(count + 2);
                if (operation.Code == OrchestrationOperationCode.BlackboardSet)
                    count++;
                if (operation.Code == OrchestrationOperationCode.Extension)
                    count = checked(count + m_Program.OutputAssignments(index).Length);
                foreach (int edge in m_Program.Outgoing(index, OrchestrationFlowKind.Child))
                    count = checked(count + 1 + (m_Program.Edge(edge).HasCondition ? 2 : 0));
                foreach (int edge in m_Program.Outgoing(index, OrchestrationFlowKind.Enter))
                    count = checked(count + 1 + (m_Program.Edge(edge).HasCondition ? 2 : 0));
                return count;
            }

            int StateDepth(int index)
            {
                int count = 0;
                for (int state = index; state >= 0; state = m_Program.Operation(state).ParentStateOperationIndex)
                    count++;
                return count;
            }

            long BoundedVisits(int index, bool stop)
            {
                int mode = stop ? 1 : 0;
                if (m_BoundedVisits[mode][index] != 0)
                    return m_BoundedVisits[mode][index] - 1;
                if (m_ActiveVisits[mode][index])
                    throw new InvalidOperationException($"Program '{m_ProgramIndex}' has no finite invocation bound: operation '{index}' has a {(stop ? "stop" : "execution")} call cycle.");
                m_ActiveVisits[mode][index] = true;
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(index);
                long count = m_Metric != CapacityMetric.Evaluations ? LocalTraceCost(index, stop) :
                    m_CountTarget >= 0 ? !stop && index == m_CountTarget ? 1 : 0 :
                    stop ? 1 : checked(1 + ValueVisits(index, -1));
                count = checked(count + 2 * InputVisits(index));
                if (operation.Code == OrchestrationOperationCode.Timeline)
                {
                    if (!m_Timelines.TryGetValue((m_ProgramIndex, index), out OrchestrationTimelineTickContentCapacity timeline))
                        throw new InvalidOperationException($"Program '{m_ProgramIndex}' Timeline '{index}' lacks its prepared invocation bound.");
                    foreach (ref readonly OrchestrationTimelineEntryTickCapacity entry in timeline.GraphEntries.Span)
                    {
                        long graph = checked(BoundedVisits(entry.OperationIndex, false) +
                            BoundedVisits(entry.OperationIndex, true));
                        count = checked(count + graph * (stop ? entry.StopInvocations :
                            checked(entry.AdvanceInvocations + entry.StopInvocations)));
                    }
                }
                else if (operation.Code == OrchestrationOperationCode.StateMachine)
                    count = checked(count + StateMachineVisits(index, stop));
                else if (operation.Code == OrchestrationOperationCode.State)
                {
                    long enter = StateEntryVisits(m_Program.StateOnEnterEdge(index), stop);
                    long root = StateEntryVisits(m_Program.StateRootEdge(index), stop);
                    count = checked(count + (stop ? Math.Max(enter, root) : checked(enter + root)));
                    if (stop)
                        count = checked(count + StateEntryVisits(m_Program.StateOnExitEdge(index), false));
                }
                else if (operation.Code != OrchestrationOperationCode.Succeed &&
                    (!stop || operation.Code != OrchestrationOperationCode.Extension ||
                        (m_Extensions.Span[m_Program.Payload<OrchestrationExtensionPayload>(index).BindingIndex].RequiredInvocations &
                            OrchestrationExtensionInvocationKind.ContinueStop) == 0))
                {
                    OrchestrationFlowKind kind = operation.Code == OrchestrationOperationCode.SubGraph
                        ? OrchestrationFlowKind.Enter : OrchestrationFlowKind.Child;
                    bool singleStop = operation.Code == OrchestrationOperationCode.Sequence ||
                        operation.Code == OrchestrationOperationCode.Selector;
                    long stoppedChild = 0;
                    foreach (int edgeIndex in m_Program.Outgoing(index, kind))
                    {
                        ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(edgeIndex);
                        long child = BoundedVisits(edge.TargetOperationIndex, stop);
                        if (stop)
                            count = checked(count + child);
                        else
                        {
                            long stopped = BoundedVisits(edge.TargetOperationIndex, true);
                            if (singleStop)
                                stoppedChild = Math.Max(stoppedChild, stopped);
                            else if (operation.Code == OrchestrationOperationCode.Parallel)
                                child = Math.Max(child, stopped);
                            else
                                child = checked(child + 2 * stopped);
                            count = checked(count + child +
                                (m_CountTarget < 0 && edge.HasCondition
                                    ? 2 * ValueVisits(edge.ConditionOperationIndex, -1) : 0));
                        }
                    }
                    count = checked(count + stoppedChild);
                }
                if (!stop)
                    count = checked(count + TransitionVisits(index));
                m_ActiveVisits[mode][index] = false;
                m_BoundedVisits[mode][index] = checked(count + 1);
                return count;
            }

            long StateMachineVisits(int index, bool stop)
            {
                long maximum = 0;
                int any = m_Program.AnyStateEntry(index);
                for (int state = 0; state < m_Program.OperationCount; state++)
                {
                    if (m_Program.Operation(state).Code != OrchestrationOperationCode.State ||
                        m_Program.Payload<OrchestrationStatePayload>(state).MachineIndex != index)
                        continue;
                    long stopped = BoundedVisits(state, true);
                    if (stop)
                        maximum = Math.Max(maximum, stopped);
                    else
                    {
                        long executed = BoundedVisits(state, false);
                        long target = TransitionTargetVisits(state);
                        if (any >= 0)
                            target = Math.Max(target, TransitionTargetVisits(any));
                        maximum = Math.Max(maximum, checked(executed + stopped + target));
                    }
                }
                if (!stop)
                {
                    int initial = m_Program.InitialStateEntry(index);
                    if (initial < 0)
                        throw new InvalidOperationException($"Program '{m_ProgramIndex}' StateMachine '{index}' has no initial transition entry.");
                    maximum = checked(maximum + TransitionVisits(initial));
                    if (any >= 0)
                        maximum = checked(maximum + TransitionVisits(any));
                }
                return maximum;
            }

            long TransitionTargetVisits(int index)
            {
                long maximum = 0;
                foreach (int edge in m_Program.Outgoing(index, OrchestrationFlowKind.Transition))
                {
                    int target = m_Program.Edge(edge).TargetOperationIndex;
                    if (m_Program.Operation(target).Code == OrchestrationOperationCode.State)
                        maximum = Math.Max(maximum, BoundedVisits(target, false));
                }
                return maximum;
            }

            long StateEntryVisits(int edgeIndex, bool stop)
            {
                if (edgeIndex < 0)
                    return 0;
                int target = m_Program.Edge(edgeIndex).TargetOperationIndex;
                long count = BoundedVisits(target, stop);
                return m_Metric != CapacityMetric.Evaluations && !stop
                    ? checked(count + BoundedVisits(target, true)) : count;
            }

            long TransitionVisits(int index)
            {
                if (m_CountTarget >= 0)
                    return 0;
                long count = 0;
                foreach (int edge in m_Program.Outgoing(index, OrchestrationFlowKind.Transition))
                    if (m_Program.Edge(edge).HasCondition)
                        count = checked(count + (m_Metric == CapacityMetric.TraceEvents ? 1 :
                            ValueVisits(m_Program.Edge(edge).ConditionOperationIndex, -1)));
                return count;
            }

            long InputVisits(int index)
            {
                if (m_CountTarget >= 0)
                    return 0;
                long count = 0;
                foreach (ref readonly OrchestrationValueInputBinding input in m_Program.Inputs(index))
                    if (input.Source == OrchestrationValueInputSource.Operation)
                        count = checked(count + ValueVisits(input.SourceIndex, input.SourceOutputPortIndex));
                return count;
            }

            long ValueVisits(int index, int output)
            {
                if (m_Metric != CapacityMetric.Evaluations)
                    return 0;
                int slot = m_Program.ValueEvaluationSlot(index, output);
                if (m_ValueVisits[slot] != 0)
                    return m_ValueVisits[slot];
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(index);
                bool action = operation.Code == OrchestrationOperationCode.Extension &&
                    m_Extensions.Span[m_Program.Payload<OrchestrationExtensionPayload>(index).BindingIndex].Kind == OrchestrationExtensionKind.Action;
                long count = action ? 1 : checked(1 + InputVisits(index));
                return m_ValueVisits[slot] = count;
            }

            void VisitOrdinaryStop(int operationIndex)
            {
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                if (m_ActiveStops[operationIndex])
                    throw new InvalidOperationException($"Tick stop capacity cannot be proven finite: ordinary-stop control cycle reaches operation '{m_Program.ColdOperation(operationIndex).IdentityId}'.");
                m_StopVisits[operationIndex] = checked(m_StopVisits[operationIndex] + 1);
                if (!operation.IsRunnable)
                    return;
                m_ActiveStops[operationIndex] = true;
                if (operation.Code == OrchestrationOperationCode.StateMachine)
                {
                    for (int state = 0; state < m_Program.OperationCount; state++)
                        if (m_Program.Operation(state).Code == OrchestrationOperationCode.State &&
                            m_Program.Payload<OrchestrationStatePayload>(state).MachineIndex == operationIndex)
                            VisitOrdinaryStop(state);
                }
                else if (operation.Code == OrchestrationOperationCode.State)
                {
                    int enter = m_Program.StateOnEnterEdge(operationIndex);
                    if (enter >= 0)
                        VisitOrdinaryStop(m_Program.Edge(enter).TargetOperationIndex);
                    int root = m_Program.StateRootEdge(operationIndex);
                    if (root >= 0)
                        VisitOrdinaryStop(m_Program.Edge(root).TargetOperationIndex);
                }
                else if (operation.Code == OrchestrationOperationCode.SubGraph)
                    VisitOrdinaryStopChildren(operationIndex, OrchestrationFlowKind.Enter);
                else if (operation.Code != OrchestrationOperationCode.Timeline &&
                    (operation.Code != OrchestrationOperationCode.Extension ||
                        (m_Extensions.Span[m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex].RequiredInvocations &
                            OrchestrationExtensionInvocationKind.ContinueStop) == 0))
                    VisitOrdinaryStopChildren(operationIndex, OrchestrationFlowKind.Child);
                m_ActiveStops[operationIndex] = false;
            }

            void VisitOrdinaryStopChildren(int operationIndex, OrchestrationFlowKind kind)
            {
                foreach (int edge in m_Program.Outgoing(operationIndex, kind))
                    VisitOrdinaryStop(m_Program.Edge(edge).TargetOperationIndex);
            }

            void AnalyzeValues(int stateContext)
            {
                for (int operationIndex = 0; operationIndex < m_Program.OperationCount; operationIndex++)
                {
                    ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                    for (int output = -1; output < operation.OutputCount; output++)
                        VisitValue(operationIndex, stateContext);
                    foreach (ref readonly OrchestrationValueInputBinding input in m_Program.Inputs(operationIndex))
                        VisitInput(operationIndex, in input, stateContext);
                }
                // 普通 ReadInputs 里的预测不计预算；诊断观察里的预测已由上面的完整端口闭包计入。
                for (int operationIndex = 0; operationIndex < m_Program.OperationCount; operationIndex++)
                    foreach (ref readonly OrchestrationValueInputBinding input in m_Program.Inputs(operationIndex))
                        if (input.Source == OrchestrationValueInputSource.StateCompletionTransition)
                            VisitInput(operationIndex, in input, stateContext);
            }

            void VisitValue(int operationIndex, int stateContext)
            {
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                bool action = operation.Code == OrchestrationOperationCode.Extension &&
                    m_Extensions.Span[m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex].Kind == OrchestrationExtensionKind.Action;
                if (!action)
                {
                    foreach (ref readonly OrchestrationValueInputBinding input in m_Program.Inputs(operationIndex))
                        VisitInput(operationIndex, in input, stateContext);
                    if (operation.Code == OrchestrationOperationCode.Extension)
                        UncountedEvaluations[operationIndex] = checked(UncountedEvaluations[operationIndex] + 1);
                    else if (operation.Code == OrchestrationOperationCode.BlackboardGet)
                        VisitFacts(operationIndex, m_Program.ValueOperation(operationIndex).Argument);
                }
            }

            void VisitInput(int operationIndex, in OrchestrationValueInputBinding input, int stateContext)
            {
                if (input.Source == OrchestrationValueInputSource.Operation)
                    VisitValue(input.SourceIndex, stateContext);
                else if (input.Source == OrchestrationValueInputSource.Blackboard)
                    VisitFacts(operationIndex, input.SourceIndex);
                else if (input.Source == OrchestrationValueInputSource.StateCompletionTransition && stateContext >= 0)
                    foreach (int edge in m_Program.Outgoing(stateContext, OrchestrationFlowKind.Transition))
                        if (m_Program.Edge(edge).HasCondition)
                            // 可执行的预测条件已由编译边界排除再次依赖当前预测，只需统计直接值闭包。
                            VisitValue(m_Program.Edge(edge).ConditionOperationIndex, -1);
            }

            void VisitFacts(int operationIndex, int valueSlot)
            {
                if (m_Program.Blackboard(valueSlot).ScopeKind == OrchestrationScopeKind.ActionInstance)
                    UncountedFacts[operationIndex] = checked(UncountedFacts[operationIndex] + 1);
            }

            internal int DirectFacts(int operationIndex)
            {
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                int count = 0;
                foreach (ref readonly OrchestrationValueInputBinding input in m_Program.Inputs(operationIndex))
                    if (input.Source == OrchestrationValueInputSource.Blackboard &&
                        m_Program.Blackboard(input.SourceIndex).ScopeKind == OrchestrationScopeKind.ActionInstance)
                        count++;
                if (operation.Code == OrchestrationOperationCode.BlackboardGet &&
                        m_Program.Blackboard(m_Program.ValueOperation(operationIndex).Argument).ScopeKind == OrchestrationScopeKind.ActionInstance ||
                    operation.Code == OrchestrationOperationCode.BlackboardSet &&
                        m_Program.Blackboard(m_Program.Payload<OrchestrationBlackboardSetPayload>(operationIndex).Slot).ScopeKind == OrchestrationScopeKind.ActionInstance)
                    count++;
                if (operation.Code == OrchestrationOperationCode.Extension)
                    foreach (ref readonly OrchestrationBlackboardOutputBinding assignment in m_Program.OutputAssignments(operationIndex))
                        if (m_Program.Blackboard(assignment.ValueSlot).ScopeKind == OrchestrationScopeKind.ActionInstance)
                            count++;
                return count;
            }

            internal int ExecutionHooks(int operationIndex)
            {
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                int count = 0;
                foreach (ref readonly OrchestrationHookBinding hook in m_Program.Hooks(operationIndex))
                    if (hook.Hook == OrchestrationLifecycleHook.OperationActivated && operation.IsRunnable)
                        count++;
                    else if (operation.Code == OrchestrationOperationCode.StateMachine)
                    {
                        if (hook.Hook == OrchestrationLifecycleHook.StateEntered)
                            count = checked(count + 2);
                        else if (hook.Hook == OrchestrationLifecycleHook.StateExited ||
                                 hook.Hook == OrchestrationLifecycleHook.StateTransition)
                            count++;
                    }
                return count;
            }

            internal int ExitHooks(int operationIndex)
            {
                if (m_Program.Operation(operationIndex).Code != OrchestrationOperationCode.StateMachine)
                    return 0;
                int count = 0;
                foreach (ref readonly OrchestrationHookBinding hook in m_Program.Hooks(operationIndex))
                    if (hook.Hook == OrchestrationLifecycleHook.StateExited)
                        count++;
                return count;
            }
        }
    }
}
