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
            long retainedGenerations)
        {
            ExecuteInvocations = executeInvocations;
            EvaluateInvocations = evaluateInvocations;
            StopInvocations = stopInvocations;
            LifecycleInvocations = lifecycleInvocations;
            BlackboardFactsInvocations = blackboardFactsInvocations;
            RetainedGenerations = retainedGenerations;
            ExtensionInvocations = checked(executeInvocations + evaluateInvocations + stopInvocations +
                lifecycleInvocations + blackboardFactsInvocations);
        }

        public long ExecuteInvocations { get; }
        public long EvaluateInvocations { get; }
        public long StopInvocations { get; }
        public long LifecycleInvocations { get; }
        public long BlackboardFactsInvocations { get; }
        public long RetainedGenerations { get; }
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

    public sealed class OrchestrationProgramTickCapacity
    {
        internal OrchestrationProgramTickCapacity(in OrchestrationProgramTickBudget budget,
            long countedOperationEvaluations, OrchestrationOperationTickCapacity[] operations,
            OrchestrationTimelineTickCapacity[] timelines)
        {
            Budget = budget;
            CountedOperationEvaluations = countedOperationEvaluations;
            Operations = operations;
            Timelines = timelines;
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
                long execution = checked((long)budget.OuterInvocationCount * analysis.InvocationEvaluations);
                long evaluations = checked(execution +
                    (long)budget.StandaloneEvaluationCount * analysis.ValueEvaluations);
                long stopTraversals = checked(budget.OuterInvocationCount + execution);
                var operations = new OrchestrationOperationTickCapacity[program.OperationCount];
                var timelineResults = new OrchestrationTimelineTickCapacity[program.OperationCount];
                ReadOnlySpan<int> owners = executionCapacity.OwnerInstances(programIndex).Span;
                for (int operationIndex = 0; operationIndex < program.OperationCount; operationIndex++)
                {
                    ref readonly OrchestrationOperation operation = ref program.Operation(operationIndex);
                    bool extension = operation.Code == OrchestrationOperationCode.Extension;
                    bool action = extension && extensions[programIndex].Span[operation.ExtensionBindingIndex].Kind ==
                        OrchestrationExtensionKind.Action;
                    long stops = checked(stopTraversals * analysis.StopVisits[operationIndex]);
                    long hooks = checked(execution * analysis.ExecutionHooks(operationIndex) +
                        stops * analysis.ExitHooks(operationIndex));
                    long evaluate = extension && !action
                        ? checked(evaluations * checked(1 + analysis.UncountedEvaluations[operationIndex])) : 0;
                    long facts = checked(evaluations * checked(analysis.DirectFacts(operationIndex) +
                        analysis.UncountedFacts[operationIndex]));
                    operations[operationIndex] = new OrchestrationOperationTickCapacity(
                        action ? checked((long)budget.OuterInvocationCount * analysis.ExecuteVisits[operationIndex]) : 0,
                        evaluate, action ? stops : 0, hooks, facts,
                        checked(owners[operation.ExecutionFrameOwnerOperationIndex] + execution));
                    if (operation.Code != OrchestrationOperationCode.Timeline)
                        continue;
                    if (!timelines.TryGetValue((programIndex, operationIndex), out OrchestrationTimelineTickContentCapacity content))
                        throw new InvalidOperationException($"Program '{programIndex}' Timeline '{operationIndex}' lacks its prepared Tick sampling capacity.");
                    long identities = checked(owners[operation.ExecutionFrameOwnerOperationIndex] + execution);
                    long timelineStops = budget.OuterInvocationCount == 0 ? 0 : identities;
                    long graphInvocations = checked(execution * content.GraphInvocationsPerAdvance +
                        timelineStops * content.GraphInvocationsPerStop);
                    timelineResults[operationIndex] = new OrchestrationTimelineTickCapacity(execution,
                        timelineStops, execution, identities, graphInvocations,
                        checked(execution * content.LogicSampleInvocationsPerAdvance),
                        checked(execution * content.LogicSampleRowsPerAdvance));
                }
                result[programIndex] = new OrchestrationProgramTickCapacity(in budget, evaluations,
                    operations, timelineResults);
            }
            return new OrchestrationExecutionTickCapacity(result);
        }

        sealed class ProgramAnalysis<TValue> where TValue : unmanaged
        {
            readonly OrchestrationProgram<TValue> m_Program;
            readonly ReadOnlyMemory<OrchestrationExtensionReference> m_Extensions;
            readonly bool[] m_ActiveStops;
            readonly bool[] m_ActiveValues;
            readonly long[] m_StopVisits;
            readonly int m_ProgramIndex;
            readonly Dictionary<(int Program, int Operation), OrchestrationTimelineTickContentCapacity> m_Timelines;
            readonly long[][] m_BoundedVisits;
            readonly bool[][] m_ActiveVisits;
            readonly long[] m_ValueVisits;
            int m_CountTarget = -1;

            internal ProgramAnalysis(OrchestrationProgram<TValue> program,
                ReadOnlyMemory<OrchestrationExtensionReference> extensions, int programIndex,
                Dictionary<(int Program, int Operation), OrchestrationTimelineTickContentCapacity> timelines)
            {
                m_Program = program;
                m_Extensions = extensions;
                m_ActiveStops = new bool[program.OperationCount];
                m_ActiveValues = new bool[program.ValueEvaluationSlotCount];
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
                if (InvocationEvaluations > m_Program.MaximumOperationEvaluations ||
                    ValueEvaluations > m_Program.MaximumOperationEvaluations)
                    throw new InvalidOperationException($"Program '{m_ProgramIndex}' requires '{Math.Max(InvocationEvaluations, ValueEvaluations)}' finite operation evaluations; published budget is '{m_Program.MaximumOperationEvaluations}'.");
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
                    if (m_Program.Operation(target).Code == OrchestrationOperationCode.Extension &&
                        m_Extensions.Span[m_Program.Operation(target).ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action)
                    {
                        m_CountTarget = target;
                        Array.Clear(m_BoundedVisits[0], 0, m_BoundedVisits[0].Length);
                        Array.Clear(m_BoundedVisits[1], 0, m_BoundedVisits[1].Length);
                        for (int root = 0; root < m_Program.OperationCount; root++)
                            ExecuteVisits[target] = Math.Max(ExecuteVisits[target],
                                checked(BoundedVisits(root, false) + BoundedVisits(root, true)));
                    }
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
                long count = m_CountTarget >= 0 ? !stop && index == m_CountTarget ? 1 : 0 :
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
                {
                    for (int state = 0; state < m_Program.OperationCount; state++)
                        if (m_Program.Operation(state).Code == OrchestrationOperationCode.State &&
                            m_Program.Operation(state).StateMachineOwnerOperationIndex == index)
                            count = checked(count + (stop ? BoundedVisits(state, true) :
                                checked(2 * BoundedVisits(state, false) + BoundedVisits(state, true))));
                    if (!stop)
                    {
                        int initial = m_Program.InitialStateEntry(index);
                        if (initial < 0)
                            throw new InvalidOperationException($"Program '{m_ProgramIndex}' StateMachine '{index}' has no initial transition entry.");
                        count = checked(count + TransitionVisits(initial));
                        int any = m_Program.AnyStateEntry(index);
                        if (any >= 0)
                            count = checked(count + TransitionVisits(any));
                    }
                }
                else if (operation.Code == OrchestrationOperationCode.State)
                {
                    count = checked(count + StateEntryVisits(m_Program.StateOnEnterEdge(index), stop));
                    count = checked(count + StateEntryVisits(m_Program.StateRootEdge(index), stop));
                    if (stop)
                        count = checked(count + StateEntryVisits(m_Program.StateOnExitEdge(index), false));
                }
                else if (operation.Code != OrchestrationOperationCode.Succeed &&
                    (!stop || operation.Code != OrchestrationOperationCode.Extension ||
                        (m_Extensions.Span[operation.ExtensionBindingIndex].RequiredInvocations &
                            OrchestrationExtensionInvocationKind.ContinueStop) == 0))
                {
                    OrchestrationFlowKind kind = operation.Code == OrchestrationOperationCode.SubGraph
                        ? OrchestrationFlowKind.Enter : OrchestrationFlowKind.Child;
                    foreach (int edgeIndex in m_Program.Outgoing(index, kind))
                    {
                        ref readonly OrchestrationFlowEdge edge = ref m_Program.Edge(edgeIndex);
                        count = checked(count + BoundedVisits(edge.TargetOperationIndex, stop));
                        if (!stop)
                            count = checked(count + 2 * BoundedVisits(edge.TargetOperationIndex, true) +
                                (edge.HasCondition ? 2 * ValueVisits(edge.ConditionOperationIndex, -1) : 0));
                    }
                }
                if (!stop)
                    count = checked(count + TransitionVisits(index));
                m_ActiveVisits[mode][index] = false;
                m_BoundedVisits[mode][index] = checked(count + 1);
                return count;
            }

            long StateEntryVisits(int edgeIndex, bool stop) => edgeIndex < 0 ? 0 :
                BoundedVisits(m_Program.Edge(edgeIndex).TargetOperationIndex, stop);

            long TransitionVisits(int index)
            {
                if (m_CountTarget >= 0)
                    return 0;
                long count = 0;
                foreach (int edge in m_Program.Outgoing(index, OrchestrationFlowKind.Transition))
                    if (m_Program.Edge(edge).HasCondition)
                        count = checked(count + ValueVisits(m_Program.Edge(edge).ConditionOperationIndex, -1));
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
                int slot = m_Program.ValueEvaluationSlot(index, output);
                if (m_ValueVisits[slot] != 0)
                    return m_ValueVisits[slot];
                if (m_ActiveValues[slot])
                    throw new InvalidOperationException($"Program '{m_ProgramIndex}' has no finite value bound: operation '{index}/{output}' has a value call cycle.");
                m_ActiveValues[slot] = true;
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(index);
                bool action = operation.Code == OrchestrationOperationCode.Extension &&
                    m_Extensions.Span[operation.ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action;
                long count = action ? 1 : checked(1 + InputVisits(index));
                m_ActiveValues[slot] = false;
                return m_ValueVisits[slot] = count;
            }

            void VisitOrdinaryStop(int operationIndex)
            {
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                if (m_ActiveStops[operationIndex])
                    throw new InvalidOperationException($"Tick stop capacity cannot be proven finite: ordinary-stop control cycle reaches operation '{operation.IdentityId}'.");
                m_StopVisits[operationIndex] = checked(m_StopVisits[operationIndex] + 1);
                if (!operation.IsRunnable)
                    return;
                m_ActiveStops[operationIndex] = true;
                if (operation.Code == OrchestrationOperationCode.StateMachine)
                {
                    for (int state = 0; state < m_Program.OperationCount; state++)
                        if (m_Program.Operation(state).Code == OrchestrationOperationCode.State &&
                            m_Program.Operation(state).StateMachineOwnerOperationIndex == operationIndex)
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
                        (m_Extensions.Span[operation.ExtensionBindingIndex].RequiredInvocations &
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
                        VisitValue(operationIndex, output, stateContext);
                    foreach (ref readonly OrchestrationValueInputBinding input in m_Program.Inputs(operationIndex))
                        VisitInput(operationIndex, in input, stateContext);
                }
                // 普通 ReadInputs 里的预测不计预算；诊断观察里的预测已由上面的完整端口闭包计入。
                for (int operationIndex = 0; operationIndex < m_Program.OperationCount; operationIndex++)
                    foreach (ref readonly OrchestrationValueInputBinding input in m_Program.Inputs(operationIndex))
                        if (input.Source == OrchestrationValueInputSource.StateCompletionTransition)
                            VisitInput(operationIndex, in input, stateContext);
            }

            void VisitValue(int operationIndex, int outputPortIndex, int stateContext)
            {
                int slot = m_Program.ValueEvaluationSlot(operationIndex, outputPortIndex);
                // 与 EvaluateValue 的 ActiveValues 地址相同；回边在再次调用扩展前终止。
                if (m_ActiveValues[slot])
                    return;
                m_ActiveValues[slot] = true;
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                bool action = operation.Code == OrchestrationOperationCode.Extension &&
                    m_Extensions.Span[operation.ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action;
                if (!action)
                {
                    foreach (ref readonly OrchestrationValueInputBinding input in m_Program.Inputs(operationIndex))
                        VisitInput(operationIndex, in input, stateContext);
                    if (operation.Code == OrchestrationOperationCode.Extension)
                        UncountedEvaluations[operationIndex] = checked(UncountedEvaluations[operationIndex] + 1);
                    else if (operation.Code == OrchestrationOperationCode.BlackboardGet)
                        VisitFacts(operationIndex, operation.ValueSlot);
                }
                m_ActiveValues[slot] = false;
            }

            void VisitInput(int operationIndex, in OrchestrationValueInputBinding input, int stateContext)
            {
                if (input.Source == OrchestrationValueInputSource.Operation)
                    VisitValue(input.SourceIndex, input.SourceOutputPortIndex, stateContext);
                else if (input.Source == OrchestrationValueInputSource.Blackboard)
                    VisitFacts(operationIndex, input.SourceIndex);
                else if (input.Source == OrchestrationValueInputSource.StateCompletionTransition && stateContext >= 0)
                    foreach (int edge in m_Program.Outgoing(stateContext, OrchestrationFlowKind.Transition))
                        if (m_Program.Edge(edge).HasCondition)
                            VisitValue(m_Program.Edge(edge).ConditionOperationIndex, -1, stateContext);
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
                if ((operation.Code == OrchestrationOperationCode.BlackboardGet ||
                     operation.Code == OrchestrationOperationCode.BlackboardSet) &&
                    m_Program.Blackboard(operation.ValueSlot).ScopeKind == OrchestrationScopeKind.ActionInstance)
                    count++;
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
