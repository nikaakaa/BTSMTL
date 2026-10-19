using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            OrchestrationWorkspaceCapacity BuildWorkspaceCapacity(OperationDraft[] operations,
                OrchestrationFlowEdge[] edges, OrchestrationProgramStructure structure,
                OrchestrationValueSchedule schedule, OrchestrationGraphEntry[] entries)
            {
                var analysis = new WorkspaceCapacityAnalysis(this, operations, edges, structure, schedule);
                return analysis.Build(entries);
            }

            sealed class WorkspaceCapacityCycle : Exception
            {
                internal WorkspaceCapacityCycle(int[] operations) => Operations = operations;
                internal int[] Operations { get; }
            }

            sealed class WorkspaceCapacityAnalysis
            {
                enum Phase : byte { Tick, Stop, ForceStop }

                sealed class Shape
                {
                    internal readonly List<int> Inputs = new();
                    internal readonly List<int> Outputs = new();
                    internal int States;
                    internal int GraphCalls;
                    internal int Entries;

                    internal void Row(int depth, int inputs, int outputs)
                    {
                        while (Inputs.Count <= depth)
                        {
                            Inputs.Add(0);
                            Outputs.Add(0);
                        }
                        Inputs[depth] = Math.Max(Inputs[depth], inputs);
                        Outputs[depth] = Math.Max(Outputs[depth], outputs);
                    }

                    internal void Merge(Shape other, int rows = 0, int states = 0, int graphs = 0, int entries = 0)
                    {
                        for (int i = 0; i < other.Inputs.Count; i++)
                            Row(checked(rows + i), other.Inputs[i], other.Outputs[i]);
                        States = Math.Max(States, checked(states + other.States));
                        GraphCalls = Math.Max(GraphCalls, checked(graphs + other.GraphCalls));
                        Entries = Math.Max(Entries, checked(entries + other.Entries));
                    }
                }

                readonly Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering> m_Compiler;
                readonly OperationDraft[] m_Operations;
                readonly OrchestrationFlowEdge[] m_Edges;
                readonly OrchestrationProgramStructure m_Structure;
                readonly OrchestrationValueSchedule m_Schedule;
                readonly int m_CycleResetPort;
                readonly List<int>[] m_States;
                readonly List<int>[] m_TimelineEntries;
                readonly List<int>[] m_OwnerOperations;
                readonly List<int>[] m_ForceChildren;
                readonly bool[] m_ForceCycles;
                readonly Dictionary<int, Shape> m_Schedules = new();
                readonly Dictionary<(int Operation, Phase Phase, OrchestrationExecutionContract Contract), Shape> m_Shapes = new();
                readonly List<(int Operation, Phase Phase, OrchestrationExecutionContract Contract)> m_Active = new();
                readonly HashSet<string> m_ReportedCycles = new(StringComparer.Ordinal);

                internal WorkspaceCapacityAnalysis(
                    Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering> compiler,
                    OperationDraft[] operations, OrchestrationFlowEdge[] edges,
                    OrchestrationProgramStructure structure, OrchestrationValueSchedule schedule)
                {
                    m_Compiler = compiler;
                    m_Operations = operations;
                    m_Edges = edges;
                    m_Structure = structure;
                    m_Schedule = schedule;
                    m_CycleResetPort = compiler.Bind("Reset");
                    m_States = Lists(operations.Length);
                    m_TimelineEntries = Lists(operations.Length);
                    m_OwnerOperations = Lists(operations.Length);
                    m_ForceChildren = Lists(operations.Length);
                    for (int i = 0; i < operations.Length; i++)
                    {
                        OperationDraft operation = operations[i];
                        m_OwnerOperations[operation.ExecutionFrameOwnerOperationIndex].Add(i);
                        if (operation.Code == OrchestrationOperationCode.State)
                            m_States[operation.StateMachineOwnerOperationIndex].Add(i);
                    }
                    foreach (ProgramSourceMapEntry entry in compiler.m_SourceMap)
                    {
                        ProgramExecutionSource source = entry.Source;
                        if (source.TargetKind == ProgramSourceTargetKind.GraphInvocation &&
                            source.InvocationCallerKind is ProgramInvocationCallerKind.TimelineClip or
                                ProgramInvocationCallerKind.PresentationMarker or ProgramInvocationCallerKind.PresentationTreeClip)
                            m_TimelineEntries[source.InvocationCallerOperationIndex].Add(source.InvocationEntryOperationIndex);
                    }
                    for (int i = 0; i < operations.Length; i++)
                    {
                        OrchestrationOperationCode code = operations[i].Code;
                        if (code == OrchestrationOperationCode.Timeline)
                            continue;
                        if (code == OrchestrationOperationCode.StateMachine)
                            m_ForceChildren[i].AddRange(m_States[i]);
                        else if (code == OrchestrationOperationCode.State)
                        {
                            AddForceChildren(i, OrchestrationFlowKind.Enter);
                            AddForceChildren(i, OrchestrationFlowKind.Exit);
                        }
                        else
                            AddForceChildren(i, code == OrchestrationOperationCode.SubGraph
                                ? OrchestrationFlowKind.Enter : OrchestrationFlowKind.Child);
                    }
                    m_ForceCycles = new bool[operations.Length];
                    var visited = new byte[operations.Length];
                    bool ReachesCycle(int operation)
                    {
                        if (visited[operation] == 1)
                            return true;
                        if (visited[operation] == 2)
                            return m_ForceCycles[operation];
                        visited[operation] = 1;
                        bool cycle = false;
                        foreach (int child in m_ForceChildren[operation])
                            cycle |= ReachesCycle(child);
                        visited[operation] = 2;
                        return m_ForceCycles[operation] = cycle;
                    }
                    for (int i = 0; i < operations.Length; i++)
                        ReachesCycle(i);
                }

                static List<int>[] Lists(int count)
                {
                    var result = new List<int>[count];
                    for (int i = 0; i < count; i++)
                        result[i] = new List<int>();
                    return result;
                }

                ReadOnlySpan<int> Outgoing(int operation, OrchestrationFlowKind kind)
                {
                    int segment = operation * OrchestrationProgramStructure.FlowKindCount + (int)kind - 1;
                    int start = m_Structure.OutgoingOffsets.Span[segment];
                    return m_Structure.OutgoingEdgeIndexes.Span.Slice(start,
                        m_Structure.OutgoingOffsets.Span[segment + 1] - start);
                }

                void AddForceChildren(int operation, OrchestrationFlowKind kind)
                {
                    foreach (int edge in Outgoing(operation, kind))
                        m_ForceChildren[operation].Add(m_Edges[edge].TargetOperationIndex);
                }

                internal OrchestrationWorkspaceCapacity Build(ReadOnlySpan<OrchestrationGraphEntry> entries)
                {
                    var result = new Shape();
                    void Collect(Func<Shape> build, int entryDepth = 0)
                    {
                        try
                        {
                            result.Merge(build(), entries: entryDepth);
                        }
                        catch (WorkspaceCapacityCycle cycle)
                        {
                            Array.Sort(cycle.Operations);
                            string identity = string.Join(",", cycle.Operations);
                            if (!m_ReportedCycles.Add(identity))
                                return;
                            var sources = new List<string>();
                            foreach (int operation in cycle.Operations)
                                sources.Add(m_Compiler.SourceLocation(ProgramSourceTargetKind.Operation, operation));
                            m_Compiler.Error("workspace_synchronous_call_cycle", ProgramSourceTargetKind.Operation,
                                cycle.Operations[0], $"同步控制／图调用／Timeline 调用成环，不能发布有限工作区容量：{string.Join("；", sources)}。");
                        }
                    }
                    foreach (OrchestrationGraphEntry entry in entries)
                    {
                        OrchestrationExecutionEntry execution = entry.Execution;
                        Collect(() => Visit(execution.OperationIndex, Phase.Tick, execution.Contract, execution.InputPortId), 1);
                    }
                    foreach (ProgramSourceMapEntry entry in m_Compiler.m_SourceMap)
                    {
                        ProgramExecutionSource source = entry.Source;
                        if (source.TargetKind != ProgramSourceTargetKind.OperationPort ||
                            source.ValuePortDirection != ProgramValuePortDirection.None)
                            continue;
                        int operation = source.TargetIndex;
                        int port = m_Compiler.Bind(source.CompiledPortId);
                        if (source.IsInputPort)
                            Collect(() => Visit(operation, Phase.Tick, OrchestrationExecutionContract.Complete, port), 1);
                        else
                            Collect(() => Output(operation, port));
                    }
                    for (int i = 0; i < m_Operations.Length; i++)
                    {
                        int operation = i;
                        Collect(() => Visit(operation, Phase.Stop, OrchestrationExecutionContract.Resumable));
                        Collect(() => Force(operation, OrchestrationExecutionContract.Resumable, new HashSet<int>()));
                        foreach (int entry in m_TimelineEntries[i])
                        {
                            int invocation = entry;
                            Collect(() => Visit(invocation, Phase.Tick, m_Compiler.m_Contract), 1);
                        }
                    }
                    foreach (OrchestrationValueScheduleEntry entry in m_Schedule.ValueEntries.Span)
                        if (entry.Product >= 0)
                            result.Merge(Schedule(entry.Product));
                    foreach (OrchestrationValueScheduleEntry entry in m_Schedule.BooleanEntries.Span)
                        if (entry.Product >= 0)
                            result.Merge(Schedule(entry.Product));
                    foreach (OrchestrationBlackboardSlot slot in m_Compiler.m_Blackboards)
                        if (slot.ScopeKind == OrchestrationScopeKind.ActionInstance)
                        {
                            result.Row(0, 1, 4);
                            break;
                        }
                    int ancestors = 0;
                    for (int i = 0; i < m_Operations.Length; i++)
                    {
                        int depth = 0;
                        for (int state = m_Operations[i].Code == OrchestrationOperationCode.State
                                ? i : m_Operations[i].ParentStateOperationIndex;
                             state >= 0; state = m_Operations[state].ParentStateOperationIndex)
                            depth++;
                        ancestors = Math.Max(ancestors, depth);
                    }
                    return new OrchestrationWorkspaceCapacity(result.Inputs.ToArray(), result.Outputs.ToArray(),
                        result.States, result.GraphCalls, result.Entries, ancestors);
                }

                Shape Schedule(int entry)
                {
                    if (m_Schedules.TryGetValue(entry, out Shape existing))
                        return existing;
                    var result = new Shape();
                    result.Row(0, 0, 0);
                    var visited = new HashSet<(int Instruction, int Predictions)>();
                    var pending = new Stack<(int Instruction, int Predictions)>();
                    pending.Push((entry, 0));
                    while (pending.Count != 0)
                    {
                        var current = pending.Pop();
                        if (!visited.Add(current))
                            continue;
                        OrchestrationValueScheduleInstruction instruction = m_Schedule.Instructions.Span[current.Instruction];
                        int predictions = current.Predictions;
                        switch (instruction.Code)
                        {
                            case OrchestrationValueScheduleCode.Return:
                            case OrchestrationValueScheduleCode.ReturnInputs:
                                continue;
                            case OrchestrationValueScheduleCode.Compute:
                            {
                                OrchestrationValueOperation value = m_Schedule.ValueOperations.Span[instruction.Operand];
                                int outputs = value.Code == OrchestrationValueOpcode.Extension
                                    ? m_Operations[instruction.Operation].OutputCount : 0;
                                result.Row(0, m_Schedule.ArgumentLists.Span[instruction.Argument].Count, outputs);
                                if (value.Code == OrchestrationValueOpcode.BlackboardGet)
                                    Facts(result, value.Argument, 1);
                                break;
                            }
                            case OrchestrationValueScheduleCode.ReadInputBlackboard:
                                Facts(result, instruction.Operand, 1);
                                break;
                            case OrchestrationValueScheduleCode.EnterPrediction:
                                result.States = Math.Max(result.States, ++predictions);
                                break;
                            case OrchestrationValueScheduleCode.LeavePrediction:
                                predictions--;
                                break;
                            case OrchestrationValueScheduleCode.BranchBoolean:
                            case OrchestrationValueScheduleCode.BranchExcludedTarget:
                            case OrchestrationValueScheduleCode.BranchCondition:
                                pending.Push((instruction.Alternative, predictions));
                                break;
                        }
                        pending.Push((instruction.Next, predictions));
                    }
                    m_Schedules.Add(entry, result);
                    return result;
                }

                void Schedules(Shape result, ReadOnlySpan<OrchestrationValueScheduleEntry> entries,
                    int index, int stride, int rows = 0, int states = 0)
                {
                    for (int context = 0; context * stride + index < entries.Length; context++)
                    {
                        OrchestrationValueScheduleEntry entry = entries[context * stride + index];
                        if (entry.Product >= 0)
                            result.Merge(Schedule(entry.Product), rows, states);
                    }
                }

                void Inputs(Shape result, int operation)
                {
                    result.Row(0, m_Operations[operation].Inputs.Count, 0);
                    Schedules(result, m_Schedule.InputEntries.Span, operation, m_Operations.Length, rows: 1);
                }

                void Facts(Shape result, int slot, int row)
                {
                    if (m_Compiler.m_Blackboards[slot].ScopeKind == OrchestrationScopeKind.ActionInstance)
                        result.Row(row, 1, 4);
                }

                void Hooks(Shape result, int operation)
                {
                    OrchestrationRange hooks = m_Operations[operation].LifecycleHooks;
                    for (int i = hooks.Offset; i < hooks.Offset + hooks.Count; i++)
                        result.Row(0, m_Compiler.m_LifecycleHooks[i].Inputs.Count, 0);
                }

                Shape Visit(int operation, Phase phase, OrchestrationExecutionContract contract, int inputPort = 0)
                {
                    if (phase == Phase.Tick && m_Operations[operation].Code == OrchestrationOperationCode.Cycle &&
                        inputPort == m_CycleResetPort)
                    {
                        var reset = new Shape();
                        if (m_Operations[operation].IsRunnable)
                            Hooks(reset, operation);
                        return reset;
                    }
                    var key = (operation, phase, contract);
                    if (m_Shapes.TryGetValue(key, out Shape cached))
                        return cached;
                    int cycle = m_Active.IndexOf(key);
                    if (cycle >= 0)
                    {
                        var operations = new HashSet<int>();
                        for (int i = cycle; i < m_Active.Count; i++)
                            operations.Add(m_Active[i].Operation);
                        var indexes = new int[operations.Count];
                        operations.CopyTo(indexes);
                        throw new WorkspaceCapacityCycle(indexes);
                    }
                    m_Active.Add(key);
                    try
                    {
                        Shape result = phase == Phase.Tick ? Tick(operation, contract) : Stop(operation, contract);
                        m_Shapes.Add(key, result);
                        return result;
                    }
                    finally
                    {
                        m_Active.RemoveAt(m_Active.Count - 1);
                    }
                }

                void Child(Shape result, int edge, Phase phase, OrchestrationExecutionContract contract,
                    int states = 0, int graphs = 0, bool passesPort = true)
                {
                    if (edge >= 0)
                        result.Merge(Visit(m_Edges[edge].TargetOperationIndex, phase, contract,
                            passesPort ? m_Edges[edge].TargetPortId : 0), states: states, graphs: graphs);
                }

                Shape Output(int operation, int port)
                {
                    var result = new Shape();
                    foreach (int edge in Outgoing(operation, OrchestrationFlowKind.Child))
                        if (m_Edges[edge].SourcePortId == port)
                            Child(result, edge, Phase.Tick, OrchestrationExecutionContract.Complete);
                    return result;
                }

                void Children(Shape result, int operation, OrchestrationFlowKind kind, Phase phase,
                    OrchestrationExecutionContract contract, bool conditions = false, bool stops = false,
                    int states = 0, int graphs = 0, bool first = false)
                {
                    foreach (int edgeIndex in Outgoing(operation, kind))
                    {
                        OrchestrationFlowEdge edge = m_Edges[edgeIndex];
                        if (conditions && edge.HasCondition)
                            Schedules(result, m_Schedule.BooleanEntries.Span, edge.ConditionOperationIndex,
                                m_Operations.Length, states: states);
                        Child(result, edgeIndex, phase, contract, states, graphs);
                        if (stops)
                            Child(result, edgeIndex, Phase.Stop, contract, states, graphs);
                        if (first)
                            break;
                    }
                }

                void Timeline(Shape result, int operation, int rows)
                {
                    var owners = new HashSet<int>();
                    foreach (int entry in m_TimelineEntries[operation])
                    {
                        result.Merge(Visit(entry, Phase.Tick, m_Compiler.m_Contract), rows: rows, entries: 1);
                        owners.Add(m_Operations[entry].ExecutionFrameOwnerOperationIndex);
                    }
                    foreach (int owner in owners)
                        foreach (int child in m_OwnerOperations[owner])
                            result.Merge(Force(child, m_Compiler.m_Contract, new HashSet<int>()), rows: rows);
                }

                Shape Tick(int operation, OrchestrationExecutionContract contract)
                {
                    var result = new Shape();
                    OperationDraft op = m_Operations[operation];
                    if (op.IsRunnable)
                        Hooks(result, operation);
                    switch (op.Code)
                    {
                        case OrchestrationOperationCode.Root:
                            Children(result, operation, OrchestrationFlowKind.Child, Phase.Tick, contract,
                                conditions: contract == OrchestrationExecutionContract.Resumable,
                                graphs: contract == OrchestrationExecutionContract.Complete ? 1 : 0,
                                first: contract == OrchestrationExecutionContract.Resumable);
                            if (contract == OrchestrationExecutionContract.Complete)
                                result.GraphCalls = Math.Max(result.GraphCalls, 1);
                            return result;
                        case OrchestrationOperationCode.StateOnEnter:
                        case OrchestrationOperationCode.StateOnExit:
                        case OrchestrationOperationCode.TimelineEnter:
                        case OrchestrationOperationCode.Loop:
                            Children(result, operation, OrchestrationFlowKind.Child, Phase.Tick, contract,
                                conditions: true, first: true);
                            return result;
                        case OrchestrationOperationCode.Sequence:
                        case OrchestrationOperationCode.Parallel:
                            Children(result, operation, OrchestrationFlowKind.Child, Phase.Tick, contract,
                                conditions: op.Code == OrchestrationOperationCode.Parallel || contract == OrchestrationExecutionContract.Resumable,
                                stops: op.Code == OrchestrationOperationCode.Parallel || contract == OrchestrationExecutionContract.Resumable);
                            return result;
                        case OrchestrationOperationCode.Selector:
                            foreach (int edge in Outgoing(operation, OrchestrationFlowKind.Child))
                            {
                                Schedules(result, m_Schedule.ChildSelectionEntries.Span, edge, m_Edges.Length);
                                Schedules(result, m_Schedule.SelectorAbortEntries.Span, edge, m_Edges.Length);
                                Child(result, edge, Phase.Tick, contract);
                                Child(result, edge, Phase.Stop, contract);
                            }
                            return result;
                        case OrchestrationOperationCode.Succeed:
                            return result;
                        case OrchestrationOperationCode.SubGraph:
                            Inputs(result, operation);
                            Children(result, operation, OrchestrationFlowKind.Enter, Phase.Tick, contract,
                                conditions: true, first: true);
                            return result;
                        case OrchestrationOperationCode.StateMachine:
                        {
                            int initial = m_Structure.InitialStateEntries.Span[operation];
                            Schedules(result, m_Schedule.TransitionEntries.Span, initial, m_Operations.Length);
                            int any = m_Structure.AnyStateEntries.Span[operation];
                            if (any >= 0)
                                Schedules(result, m_Schedule.TransitionEntries.Span, any, m_Operations.Length, states: 1);
                            foreach (int state in m_States[operation])
                            {
                                result.Merge(Visit(state, Phase.Tick, contract));
                                result.Merge(Visit(state, Phase.Stop, contract));
                                Schedules(result, m_Schedule.TransitionEntries.Span, state, m_Operations.Length, states: 1);
                            }
                            return result;
                        }
                        case OrchestrationOperationCode.State:
                        {
                            int enter = m_Structure.StateOnEnterEdges.Span[operation];
                            Child(result, enter, Phase.Tick, contract, states: 1, passesPort: false);
                            if (enter >= 0)
                                result.Merge(Force(m_Edges[enter].TargetOperationIndex, contract, new HashSet<int>()));
                            Child(result, m_Structure.StateRootEdges.Span[operation], Phase.Tick, contract,
                                states: 1, passesPort: false);
                            return result;
                        }
                        case OrchestrationOperationCode.Branch:
                            Inputs(result, operation);
                            break;
                        case OrchestrationOperationCode.Cycle:
                            break;
                        default:
                        {
                            bool action = op.Code == OrchestrationOperationCode.Extension &&
                                m_Compiler.m_Extensions[op.ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action;
                            if (!action && CanEvaluate(op.Code))
                                Schedules(result, m_Schedule.BooleanEntries.Span, operation, m_Operations.Length);
                            else
                            {
                                Inputs(result, operation);
                                if (action)
                                {
                                    if (op.OutputSlotOffset < 0)
                                        result.Row(0, 0, op.OutputCount);
                                    OrchestrationRange assignments = op.OutputAssignments;
                                    for (int i = assignments.Offset; i < assignments.Offset + assignments.Count; i++)
                                        Facts(result, m_Compiler.m_BlackboardOutputs[i].ValueSlot, 1);
                                }
                                if (op.Code == OrchestrationOperationCode.BlackboardSet)
                                {
                                    Facts(result, op.ValueSlot, 1);
                                }
                                if (op.Code == OrchestrationOperationCode.Timeline)
                                    Timeline(result, operation, 1);
                            }
                            if (contract != OrchestrationExecutionContract.Complete)
                                return result;
                            break;
                        }
                    }
                    Children(result, operation, OrchestrationFlowKind.Child, Phase.Tick, contract);
                    return result;
                }

                Shape Stop(int operation, OrchestrationExecutionContract contract)
                {
                    var result = new Shape();
                    OperationDraft op = m_Operations[operation];
                    if (!op.IsRunnable)
                        return result;
                    switch (op.Code)
                    {
                        case OrchestrationOperationCode.State:
                        {
                            Child(result, m_Structure.StateOnEnterEdges.Span[operation], Phase.Stop, contract, states: 1);
                            Child(result, m_Structure.StateRootEdges.Span[operation], Phase.Stop, contract, states: 1);
                            int exit = m_Structure.StateOnExitEdges.Span[operation];
                            Child(result, exit, Phase.Tick, contract, states: 1, passesPort: false);
                            if (exit >= 0)
                                result.Merge(Force(m_Edges[exit].TargetOperationIndex, contract, new HashSet<int>()));
                            return result;
                        }
                        case OrchestrationOperationCode.StateMachine:
                            Hooks(result, operation);
                            foreach (int state in m_States[operation])
                                result.Merge(Visit(state, Phase.Stop, contract));
                            return result;
                        case OrchestrationOperationCode.Timeline:
                            Timeline(result, operation, 0);
                            return result;
                        case OrchestrationOperationCode.Extension:
                            if (op.OutputSlotOffset < 0)
                                result.Row(0, 0, op.OutputCount);
                            if ((m_Compiler.m_Extensions[op.ExtensionBindingIndex].RequiredInvocations &
                                OrchestrationExtensionInvocationKind.ContinueStop) != 0)
                                return result;
                            break;
                    }
                    Children(result, operation, op.Code == OrchestrationOperationCode.SubGraph
                        ? OrchestrationFlowKind.Enter : OrchestrationFlowKind.Child, Phase.Stop, contract);
                    return result;
                }

                Shape Force(int operation, OrchestrationExecutionContract contract, HashSet<int> visited)
                {
                    if (!visited.Add(operation))
                        return new Shape();
                    var key = (operation, Phase.ForceStop, contract);
                    try
                    {
                        if (!m_ForceCycles[operation] && m_Shapes.TryGetValue(key, out Shape cached))
                            return cached;
                        int cycle = m_Active.IndexOf(key);
                        if (cycle >= 0)
                        {
                            var operations = new HashSet<int>();
                            for (int i = cycle; i < m_Active.Count; i++)
                                operations.Add(m_Active[i].Operation);
                            var indexes = new int[operations.Count];
                            operations.CopyTo(indexes);
                            throw new WorkspaceCapacityCycle(indexes);
                        }
                        m_Active.Add(key);
                        try
                        {
                            var result = new Shape();
                            OperationDraft op = m_Operations[operation];
                            if (op.Code == OrchestrationOperationCode.Timeline)
                                Timeline(result, operation, 0);
                            else
                            {
                                if (op.IsRunnable && op.Code == OrchestrationOperationCode.Extension && op.OutputSlotOffset < 0)
                                    result.Row(0, 0, op.OutputCount);
                                int states = op.Code == OrchestrationOperationCode.State ? 1 : 0;
                                result.States = states;
                                foreach (int child in m_ForceChildren[operation])
                                    result.Merge(Force(child, contract, visited), states: states);
                            }
                            if (!m_ForceCycles[operation])
                                m_Shapes.Add(key, result);
                            return result;
                        }
                        finally
                        {
                            m_Active.RemoveAt(m_Active.Count - 1);
                        }
                    }
                    finally
                    {
                        visited.Remove(operation);
                    }
                }
            }
        }
    }
}
