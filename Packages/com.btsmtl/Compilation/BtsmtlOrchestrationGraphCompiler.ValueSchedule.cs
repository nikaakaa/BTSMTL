using System;
using System.Collections.Generic;
using System.Numerics;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            OrchestrationValueSchedule BuildValueSchedule(OperationDraft[] operations,
                OrchestrationFlowEdge[] edges, OrchestrationProgramStructure structure)
            {
                TransitionConditions transitionConditions = BuildTransitionConditions(operations, edges, structure);
                var states = new List<int> { -1 };
                var contexts = new int[operations.Length + 1];
                for (int i = 0; i < operations.Length; i++)
                    if (operations[i].Code == OrchestrationOperationCode.State)
                    {
                        contexts[i + 1] = states.Count;
                        states.Add(i);
                    }
                int values = structure.ValueEvaluationOffsets.Span[operations.Length];
                var valueEntries = new OrchestrationValueScheduleEntry[checked(states.Count * values)];
                var inputEntries = new OrchestrationValueScheduleEntry[checked(states.Count * operations.Length)];
                var transitions = new OrchestrationValueScheduleEntry[inputEntries.Length];
                var booleans = new OrchestrationValueScheduleEntry[inputEntries.Length];
                var children = new OrchestrationValueScheduleEntry[checked(states.Count * edges.Length)];
                var aborts = new OrchestrationValueScheduleEntry[children.Length];
                Array.Fill(valueEntries, new OrchestrationValueScheduleEntry(-1, -1));
                Array.Fill(inputEntries, new OrchestrationValueScheduleEntry(-1, -1));
                Array.Fill(transitions, new OrchestrationValueScheduleEntry(-1, -1));
                Array.Fill(booleans, new OrchestrationValueScheduleEntry(-1, -1));
                Array.Fill(children, new OrchestrationValueScheduleEntry(-1, -1));
                Array.Fill(aborts, new OrchestrationValueScheduleEntry(-1, -1));
                var instructions = new List<OrchestrationValueScheduleInstruction>();
                var argumentSlots = new List<int>();
                var argumentLists = new List<OrchestrationRange>();
                var prediction = new Dictionary<int, bool>();
                bool InputPrediction(int operation)
                {
                    OrchestrationRange range = operations[operation].Inputs;
                    for (int i = range.Offset; i < range.Offset + range.Count; i++)
                    {
                        OrchestrationValueInputBinding input = m_Inputs[i];
                        if (input.Source == OrchestrationValueInputSource.StateCompletionTransition ||
                            input.Source == OrchestrationValueInputSource.Operation && ValuePrediction(input.SourceIndex))
                            return true;
                    }
                    return false;
                }
                bool ValuePrediction(int operation)
                {
                    if (prediction.TryGetValue(operation, out bool result))
                        return result;
                    var pending = new Stack<(int Operation, bool Complete)>();
                    pending.Push((operation, false));
                    while (pending.Count != 0)
                    {
                        var current = pending.Pop();
                        if (prediction.ContainsKey(current.Operation))
                            continue;
                        ref readonly OperationDraft op = ref operations[current.Operation];
                        if (op.Code == OrchestrationOperationCode.Constant ||
                            op.Code == OrchestrationOperationCode.BlackboardGet ||
                            op.Code == OrchestrationOperationCode.Extension &&
                            m_Extensions[op.ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action)
                        {
                            prediction.Add(current.Operation, false);
                            continue;
                        }
                        if (!current.Complete)
                        {
                            pending.Push((current.Operation, true));
                            for (int i = op.Inputs.Offset; i < op.Inputs.Offset + op.Inputs.Count; i++)
                            {
                                OrchestrationValueInputBinding input = m_Inputs[i];
                                if (input.Source == OrchestrationValueInputSource.Operation)
                                    pending.Push((input.SourceIndex, false));
                            }
                            continue;
                        }
                        result = false;
                        for (int i = op.Inputs.Offset; i < op.Inputs.Offset + op.Inputs.Count; i++)
                        {
                            OrchestrationValueInputBinding input = m_Inputs[i];
                            if (input.Source == OrchestrationValueInputSource.StateCompletionTransition ||
                                input.Source == OrchestrationValueInputSource.Operation && prediction[input.SourceIndex])
                            {
                                result = true;
                                break;
                            }
                        }
                        prediction.Add(current.Operation, result);
                    }
                    return prediction[operation];
                }
                ReadOnlySpan<int> Outgoing(int operation, OrchestrationFlowKind kind)
                {
                    int segment = operation * OrchestrationProgramStructure.FlowKindCount + (int)kind - 1;
                    int start = structure.OutgoingOffsets.Span[segment];
                    return structure.OutgoingEdgeIndexes.Span.Slice(start, structure.OutgoingOffsets.Span[segment + 1] - start);
                }
                bool SelectionPrediction(int operation, OrchestrationFlowKind kind)
                {
                    foreach (int edge in Outgoing(operation, kind))
                    {
                        if (!edges[edge].HasCondition)
                        {
                            if (kind == OrchestrationFlowKind.Transition && operations[operation].Code != OrchestrationOperationCode.StateAny)
                                break;
                            continue;
                        }
                        if (ValuePrediction(edges[edge].ConditionOperationIndex))
                            return true;
                    }
                    return false;
                }
                for (int context = 0; context < states.Count; context++)
                {
                    var product = new ValueScheduleBuilder(this, operations, edges, structure, states[context], false,
                        instructions, argumentSlots, argumentLists, transitionConditions);
                    var workbench = new ValueScheduleBuilder(this, operations, edges, structure, states[context], true,
                        instructions, argumentSlots, argumentLists, transitionConditions);
                    for (int operation = 0; operation < operations.Length; operation++)
                    {
                        OperationDraft op = operations[operation];
                        int valueOffset = structure.ValueEvaluationOffsets.Span[operation];
                        if (CanEvaluate(op.Code))
                            for (int output = -1; output < op.OutputCount; output++)
                            {
                                int slot = valueOffset + output + 1;
                                int target = context * values + slot;
                                if (context != 0 && !ValuePrediction(operation))
                                    valueEntries[target] = valueEntries[slot];
                                else if (context == 0 || m_ValueContexts.Contains((operation, states[context])))
                                    valueEntries[target] = new OrchestrationValueScheduleEntry(
                                        product.ValueEntry(operation, output), workbench.ValueEntry(operation, output));
                            }
                        int entry = context * operations.Length + operation;
                        if (CanEvaluate(op.Code) && BooleanKind(m_ValueKinds[valueOffset]))
                        {
                            if (context != 0 && !ValuePrediction(operation))
                                booleans[entry] = booleans[operation];
                            else if (context == 0 || m_ValueContexts.Contains((operation, states[context])))
                                booleans[entry] = new OrchestrationValueScheduleEntry(
                                    product.BooleanEntry(operation), workbench.BooleanEntry(operation));
                        }
                        if (context != 0 && !InputPrediction(operation))
                            inputEntries[entry] = inputEntries[operation];
                        else if (context == 0 || m_ValueContexts.Contains((operation, states[context])) ||
                            m_ControlContexts.Contains((operation, states[context])))
                            inputEntries[entry] = new OrchestrationValueScheduleEntry(
                                product.InputEntry(operation), workbench.InputEntry(operation));
                        if (op.Code == OrchestrationOperationCode.Selector)
                        {
                            ReadOnlySpan<int> childEdges = Outgoing(operation, OrchestrationFlowKind.Child);
                            for (int position = 0; position < childEdges.Length; position++)
                            {
                                int edge = childEdges[position];
                                int target = context * edges.Length + edge;
                                if (context != 0 && !SelectionPrediction(operation, OrchestrationFlowKind.Child))
                                {
                                    children[target] = children[edge];
                                    aborts[target] = aborts[edge];
                                }
                                else if (context == 0 || m_ControlContexts.Contains((operation, states[context])))
                                {
                                    children[target] = new OrchestrationValueScheduleEntry(
                                        product.ChildEntry(operation, position), workbench.ChildEntry(operation, position));
                                    aborts[target] = new OrchestrationValueScheduleEntry(
                                        product.AbortEntry(operation, position), workbench.AbortEntry(operation, position));
                                }
                            }
                        }
                        if (op.Code is not (OrchestrationOperationCode.State or OrchestrationOperationCode.StateAny or
                            OrchestrationOperationCode.StateEnter))
                            continue;
                        if (context != 0 && op.Code != OrchestrationOperationCode.StateAny &&
                            !SelectionPrediction(operation, OrchestrationFlowKind.Transition))
                            transitions[entry] = transitions[operation];
                        else if (context == 0 || operation == states[context] ||
                            m_ControlContexts.Contains((operation, states[context])))
                            transitions[entry] = new OrchestrationValueScheduleEntry(
                                product.TransitionEntry(operation), workbench.TransitionEntry(operation));
                    }
                }
                bool predicts = false;
                foreach (OrchestrationValueInputBinding input in m_Inputs)
                    predicts |= input.Source == OrchestrationValueInputSource.StateCompletionTransition;
                int Reverse(int index) => index < 0 ? -1 : instructions.Count - index - 1;
                var ordered = new OrchestrationValueScheduleInstruction[instructions.Count];
                for (int i = 0; i < ordered.Length; i++)
                {
                    OrchestrationValueScheduleInstruction value = instructions[i];
                    ordered[Reverse(i)] = new OrchestrationValueScheduleInstruction(value.Code, value.Operation,
                        value.Operand, value.Destination, value.Argument, Reverse(value.Next), Reverse(value.Alternative));
                }
                void ReverseEntries(OrchestrationValueScheduleEntry[] entries)
                {
                    for (int i = 0; i < entries.Length; i++)
                        entries[i] = new OrchestrationValueScheduleEntry(Reverse(entries[i].Product), Reverse(entries[i].Workbench));
                }
                ReverseEntries(valueEntries);
                ReverseEntries(inputEntries);
                ReverseEntries(transitions);
                ReverseEntries(children);
                ReverseEntries(aborts);
                ReverseEntries(booleans);
                return new OrchestrationValueSchedule(ordered, argumentSlots.ToArray(), argumentLists.ToArray(),
                    valueEntries, inputEntries, transitions, children, aborts, booleans, m_ValueOperations,
                    contexts, values, m_Inputs.Count,
                    checked((values + m_Inputs.Count + 1) * (predicts ? 2 : 1)), predicts ? 1 : 0,
                    transitionConditions.Conditions, transitionConditions.Sources,
                    transitionConditions.CacheCount, transitionConditions.SnapshotCount);
            }

            static bool CanEvaluate(OrchestrationOperationCode code) => code is
                OrchestrationOperationCode.ConditionResult or OrchestrationOperationCode.And or OrchestrationOperationCode.Or or
                OrchestrationOperationCode.Not or OrchestrationOperationCode.Compare or OrchestrationOperationCode.Calculate or
                OrchestrationOperationCode.IdentityValue or OrchestrationOperationCode.SelectValue or OrchestrationOperationCode.Constant or
                OrchestrationOperationCode.InputBoolean or OrchestrationOperationCode.InputScalar or OrchestrationOperationCode.InputVector2 or
                OrchestrationOperationCode.HostInput or OrchestrationOperationCode.InputVector2Magnitude or OrchestrationOperationCode.InputRequest or
                OrchestrationOperationCode.BlackboardGet or OrchestrationOperationCode.SubGraph or OrchestrationOperationCode.StateRootCompleted or
                OrchestrationOperationCode.StateExitCause or OrchestrationOperationCode.Cycle or OrchestrationOperationCode.TimelineTime or
                OrchestrationOperationCode.TimelineSection or OrchestrationOperationCode.Extension;

            sealed class ValueScheduleBuilder
            {
                enum WorkKind : byte { Value, BooleanValue, Input, Inputs, CompleteValue, CompleteInput, Select, SelectChild, TransitionCondition, Emit, EnterPrediction, LeavePrediction }

                enum CompileStepKind : byte { Expand, Alias, Emit }

                readonly struct CompileStep
                {
                    internal readonly (int Work, BigInteger Available) Key;
                    internal readonly CompileStepKind Kind;
                    internal readonly OrchestrationValueScheduleInstruction Data;
                    internal readonly BigInteger NextAvailable;
                    internal readonly BigInteger AlternativeAvailable;

                    internal CompileStep((int Work, BigInteger Available) key,
                        CompileStepKind kind = CompileStepKind.Expand,
                        OrchestrationValueScheduleInstruction data = default,
                        BigInteger nextAvailable = default, BigInteger alternativeAvailable = default)
                    {
                        Key = key;
                        Kind = kind;
                        Data = data;
                        NextAvailable = nextAvailable;
                        AlternativeAvailable = alternativeAvailable;
                    }
                }

                readonly Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering> m_Compiler;
                readonly OperationDraft[] m_Operations;
                readonly OrchestrationFlowEdge[] m_Edges;
                readonly OrchestrationProgramStructure m_Structure;
                readonly int m_State;
                readonly bool m_Diagnostics;
                readonly int m_ValueCount;
                readonly int m_BankSize;
                readonly List<OrchestrationValueScheduleInstruction> m_Instructions;
                readonly List<int> m_ArgumentSlots;
                readonly List<OrchestrationRange> m_ArgumentLists;
                readonly TransitionConditions m_TransitionConditions;
                readonly List<(WorkKind Kind, OrchestrationValueScheduleInstruction Data)> m_Work = new();
                readonly Dictionary<(WorkKind Kind, OrchestrationValueScheduleCode Code, int Op, int Operand, int Dest, int Arg, int Next, int Alt), int> m_WorkIndexes = new();
                readonly Dictionary<(int Work, BigInteger Available), int> m_Compiled = new();
                readonly List<BigInteger> m_Liveness = new();
                readonly Dictionary<(int Operation, int Output, int Bank), BigInteger> m_ValueDependencies = new();
                readonly Dictionary<(int Operation, int Bank, int Selected), int> m_Arguments = new();

                internal ValueScheduleBuilder(
                    Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering> compiler,
                    OperationDraft[] operations, OrchestrationFlowEdge[] edges, OrchestrationProgramStructure structure,
                    int state, bool diagnostics, List<OrchestrationValueScheduleInstruction> instructions,
                    List<int> argumentSlots, List<OrchestrationRange> argumentLists,
                    TransitionConditions transitionConditions)
                {
                    m_Compiler = compiler;
                    m_Operations = operations;
                    m_Edges = edges;
                    m_Structure = structure;
                    m_State = state;
                    m_Diagnostics = diagnostics;
                    m_ValueCount = structure.ValueEvaluationOffsets.Span[operations.Length];
                    m_BankSize = m_ValueCount + compiler.m_Inputs.Count + 1;
                    m_Instructions = instructions;
                    m_ArgumentSlots = argumentSlots;
                    m_ArgumentLists = argumentLists;
                    m_TransitionConditions = transitionConditions;
                }

                int Work(WorkKind kind, int op = -1, int operand = -1, int destination = -1, int argument = 0,
                    int next = -1, int alternative = -1, OrchestrationValueScheduleCode code = default)
                {
                    var key = (kind, code, op, operand, destination, argument, next, alternative);
                    if (m_WorkIndexes.TryGetValue(key, out int index))
                        return index;
                    index = m_Work.Count;
                    m_WorkIndexes.Add(key, index);
                    m_Work.Add((kind, new OrchestrationValueScheduleInstruction(code, op, operand, destination,
                        argument, next, alternative)));
                    return index;
                }

                int Emit(OrchestrationValueScheduleCode code, int op = -1, int operand = -1, int destination = -1,
                    int argument = 0, int next = -1, int alternative = -1) =>
                    Work(WorkKind.Emit, op, operand, destination, argument, next, alternative, code);

                int ValueSlot(int operation, int output, int bank) =>
                    bank * m_BankSize + m_Structure.ValueEvaluationOffsets.Span[operation] + output + 1;

                int InputSlot(int input, int bank) => bank * m_BankSize + m_ValueCount + input;

                int BooleanSlot(int bank) => bank * m_BankSize + m_BankSize - 1;

                internal int ValueEntry(int operation, int output) => Compile(
                    Work(WorkKind.Value, operation, output, argument: 0,
                        next: Emit(OrchestrationValueScheduleCode.Return,
                            operand: (int)OrchestrationValueSelectionReason.None)), BigInteger.Zero);

                internal int InputEntry(int operation) => Compile(
                    Work(WorkKind.Inputs, operation, argument: 0,
                        next: Emit(OrchestrationValueScheduleCode.ReturnInputs,
                            operand: (int)OrchestrationValueSelectionReason.None)), BigInteger.Zero);

                internal int TransitionEntry(int operation) => Compile(
                    Work(WorkKind.Select, operation, 0,
                        destination: Emit(OrchestrationValueScheduleCode.Return, operand: (int)OrchestrationValueSelectionReason.None), argument: 0), BigInteger.Zero);

                internal int BooleanEntry(int operation) => Compile(
                    Work(WorkKind.BooleanValue, operation, -1, argument: 0,
                        next: Emit(OrchestrationValueScheduleCode.Return,
                            operand: (int)OrchestrationValueSelectionReason.None)), BigInteger.Zero);

                internal int ChildEntry(int operation, int position) => Compile(
                    Work(WorkKind.SelectChild, operation, position,
                        destination: Emit(OrchestrationValueScheduleCode.Return, operand: (int)OrchestrationValueSelectionReason.None),
                        argument: 0), BigInteger.Zero);

                internal int AbortEntry(int operation, int position)
                {
                    int segment = operation * OrchestrationProgramStructure.FlowKindCount + (int)OrchestrationFlowKind.Child - 1;
                    int start = m_Structure.OutgoingOffsets.Span[segment];
                    int next = Emit(OrchestrationValueScheduleCode.Return,
                        operand: (int)OrchestrationValueSelectionReason.None, argument: -1);
                    for (int i = position - 1; i >= 0; i--)
                    {
                        int index = m_Structure.OutgoingEdgeIndexes.Span[start + i];
                        OrchestrationFlowEdge edge = m_Edges[index];
                        if (edge.AbortPolicy is not (OrchestrationAbortPolicy.LowerPriority or OrchestrationAbortPolicy.Both))
                            continue;
                        int accepted = Emit(OrchestrationValueScheduleCode.Return,
                            operand: (int)OrchestrationValueSelectionReason.None, argument: i);
                        next = edge.HasCondition
                            ? Work(WorkKind.BooleanValue, edge.ConditionOperationIndex, -1, argument: 0,
                                next: Emit(OrchestrationValueScheduleCode.BranchCondition, operation, index,
                                    BooleanSlot(0), next: accepted, alternative: next))
                            : accepted;
                    }
                    int currentIndex = m_Structure.OutgoingEdgeIndexes.Span[start + position];
                    OrchestrationFlowEdge current = m_Edges[currentIndex];
                    if (current.HasCondition && current.AbortPolicy is OrchestrationAbortPolicy.Self or OrchestrationAbortPolicy.Both)
                        next = Work(WorkKind.BooleanValue, current.ConditionOperationIndex, -1, argument: 0,
                            next: Emit(OrchestrationValueScheduleCode.BranchCondition, operation, currentIndex,
                                BooleanSlot(0), next: next,
                                alternative: Emit(OrchestrationValueScheduleCode.Return,
                                    operand: (int)OrchestrationValueSelectionReason.SelfAbort, argument: position)));
                    return Compile(next, BigInteger.Zero);
                }

                int Arguments(int operation, int bank, int selected)
                {
                    if (m_Arguments.TryGetValue((operation, bank, selected), out int existing))
                        return existing;
                    OrchestrationRange inputs = m_Operations[operation].Inputs;
                    int start = m_ArgumentSlots.Count;
                    int count = selected == -2 ? 1 : selected >= 0 ? 2 : inputs.Count;
                    for (int i = 0; i < count; i++)
                        m_ArgumentSlots.Add(InputSlot(inputs.Offset + (i == 1 && selected >= 0 ? selected : i), bank));
                    int index = m_ArgumentLists.Count;
                    m_ArgumentLists.Add(new OrchestrationRange(start, count));
                    m_Arguments.Add((operation, bank, selected), index);
                    return index;
                }

                BigInteger InputDependencies(int operation, int inputIndex, int bank)
                {
                    OrchestrationValueInputBinding input = m_Compiler.m_Inputs[inputIndex];
                    BigInteger result = BigInteger.One << InputSlot(inputIndex, bank);
                    if (input.Source == OrchestrationValueInputSource.Operation)
                        result |= ValueDependencies(input.SourceIndex, input.SourceOutputPortIndex, bank);
                    return result;
                }

                BigInteger AllInputs(int operation, int bank)
                {
                    OrchestrationRange inputs = m_Operations[operation].Inputs;
                    BigInteger result = BigInteger.Zero;
                    for (int i = inputs.Offset; i < inputs.Offset + inputs.Count; i++)
                        result |= InputDependencies(operation, i, bank);
                    return result;
                }

                BigInteger ValueDependencies(int operation, int output, int bank)
                {
                    if (m_ValueDependencies.TryGetValue((operation, output, bank), out BigInteger result))
                        return result;
                    var pending = new Stack<(int Operation, int Output, bool Complete)>();
                    pending.Push((operation, output, false));
                    while (pending.Count != 0)
                    {
                        var current = pending.Pop();
                        var key = (current.Operation, current.Output, bank);
                        if (m_ValueDependencies.ContainsKey(key))
                            continue;
                        ref readonly OperationDraft op = ref m_Operations[current.Operation];
                        bool action = op.Code == OrchestrationOperationCode.Extension &&
                            m_Compiler.m_Extensions[op.ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action;
                        if (!action && !current.Complete)
                        {
                            pending.Push((current.Operation, current.Output, true));
                            for (int i = op.Inputs.Offset; i < op.Inputs.Offset + op.Inputs.Count; i++)
                            {
                                OrchestrationValueInputBinding input = m_Compiler.m_Inputs[i];
                                if (input.Source == OrchestrationValueInputSource.Operation)
                                    pending.Push((input.SourceIndex, input.SourceOutputPortIndex, false));
                            }
                            continue;
                        }
                        result = BigInteger.One << ValueSlot(current.Operation, current.Output, bank);
                        if (!action)
                            for (int i = op.Inputs.Offset; i < op.Inputs.Offset + op.Inputs.Count; i++)
                            {
                                result |= BigInteger.One << InputSlot(i, bank);
                                OrchestrationValueInputBinding input = m_Compiler.m_Inputs[i];
                                if (input.Source == OrchestrationValueInputSource.Operation)
                                    result |= m_ValueDependencies[(input.SourceIndex, input.SourceOutputPortIndex, bank)];
                            }
                        m_ValueDependencies.Add(key, result);
                    }
                    return m_ValueDependencies[(operation, output, bank)];
                }

                BigInteger Live(int index)
                {
                    if (index < 0)
                        return BigInteger.Zero;
                    BigInteger Previous(int work) => work < 0 ? BigInteger.Zero : m_Liveness[work];
                    // Work 的后继均已先创建，活跃集合按同一追加顺序计算。
                    for (int current = m_Liveness.Count; current <= index; current++)
                    {
                        var work = m_Work[current];
                        OrchestrationValueScheduleInstruction d = work.Data;
                        BigInteger result;
                        switch (work.Kind)
                        {
                            case WorkKind.Value:
                            case WorkKind.BooleanValue:
                                result = ValueDependencies(d.Operation, d.Operand, d.Argument) | Previous(d.Next);
                                break;
                            case WorkKind.Input:
                                result = InputDependencies(d.Operation, m_Operations[d.Operation].Inputs.Offset + d.Operand, d.Argument) | Previous(d.Next);
                                break;
                            case WorkKind.Inputs:
                                result = AllInputs(d.Operation, d.Argument) | Previous(d.Next);
                                break;
                            case WorkKind.TransitionCondition:
                                result = ValueDependencies(m_Edges[d.Operand].ConditionOperationIndex, -1, d.Argument) |
                                    Previous(d.Next);
                                break;
                            case WorkKind.Select:
                            case WorkKind.SelectChild:
                            {
                                result = Previous(d.Destination);
                                OrchestrationFlowKind kind = work.Kind == WorkKind.Select ? OrchestrationFlowKind.Transition : OrchestrationFlowKind.Child;
                                int segment = d.Operation * OrchestrationProgramStructure.FlowKindCount + (int)kind - 1;
                                int start = m_Structure.OutgoingOffsets.Span[segment];
                                int end = m_Structure.OutgoingOffsets.Span[segment + 1];
                                for (int i = start + d.Operand; i < end; i++)
                                {
                                    OrchestrationFlowEdge edge = m_Edges[m_Structure.OutgoingEdgeIndexes.Span[i]];
                                    bool any = m_Operations[d.Operation].Code == OrchestrationOperationCode.StateAny;
                                    if (any && m_State >= 0 && edge.TargetOperationIndex == m_State)
                                        continue;
                                    if (!edge.HasCondition)
                                    {
                                        if (any && m_State < 0)
                                            continue;
                                        break;
                                    }
                                    result |= ValueDependencies(edge.ConditionOperationIndex, -1, d.Argument);
                                }
                                break;
                            }
                            case WorkKind.EnterPrediction:
                            case WorkKind.LeavePrediction:
                                result = BigInteger.Zero;
                                break;
                            default:
                                result = Previous(d.Next);
                                if (d.Code is OrchestrationValueScheduleCode.OverrideInput or OrchestrationValueScheduleCode.OverrideOutput or
                                    OrchestrationValueScheduleCode.BranchBoolean or OrchestrationValueScheduleCode.BranchExcludedTarget or OrchestrationValueScheduleCode.BranchCondition)
                                    result |= Previous(d.Alternative);
                                break;
                        }
                        m_Liveness.Add(result);
                    }
                    return m_Liveness[index];
                }

                int Inputs(int operation, int bank, int next)
                {
                    OperationDraft op = m_Operations[operation];
                    int Finish(int selected)
                    {
                        var continuation = m_Work[next];
                        OrchestrationValueScheduleInstruction data = continuation.Data;
                        if (selected == -2 && continuation.Kind == WorkKind.Emit && data.Code == OrchestrationValueScheduleCode.Compute)
                            return Emit(OrchestrationValueScheduleCode.CopyInput, operation,
                                InputSlot(op.Inputs.Offset, bank), data.Destination, next: data.Next);
                        return Work(continuation.Kind, data.Operation, data.Operand, data.Destination,
                            Arguments(operation, bank, selected), data.Next, data.Alternative, data.Code);
                    }
                    int Read(int index, int continuation) => Work(WorkKind.Input, operation, index, argument: bank, next: continuation);
                    int normal = Finish(-1);
                    if (op.Inputs.Count == 0)
                        return normal;
                    if (op.Code is OrchestrationOperationCode.And or OrchestrationOperationCode.Or)
                    {
                        int complete = normal;
                        for (int i = op.Inputs.Count - 1; i >= 1; i--)
                            complete = Read(i, complete);
                        int shortCircuit = Finish(-2);
                        int branch = Emit(OrchestrationValueScheduleCode.BranchBoolean, operation,
                            destination: InputSlot(op.Inputs.Offset, bank),
                            next: op.Code == OrchestrationOperationCode.And ? complete : shortCircuit,
                            alternative: op.Code == OrchestrationOperationCode.And ? shortCircuit : complete);
                        return Read(0, branch);
                    }
                    if (op.Code == OrchestrationOperationCode.SelectValue)
                    {
                        int branch = Emit(OrchestrationValueScheduleCode.BranchBoolean, operation,
                            destination: InputSlot(op.Inputs.Offset, bank),
                            next: Read(1, Finish(1)), alternative: Read(2, Finish(2)));
                        return Read(0, branch);
                    }
                    for (int i = op.Inputs.Count - 1; i >= 0; i--)
                        normal = Read(i, normal);
                    return normal;
                }

                int Compile(int index, BigInteger available)
                {
                    var key = (index, available & Live(index));
                    var pending = new Stack<CompileStep>();
                    pending.Push(new CompileStep(key));
                    while (pending.Count != 0)
                    {
                        CompileStep step = pending.Pop();
                        if (m_Compiled.ContainsKey(step.Key))
                            continue;
                        if (step.Kind == CompileStepKind.Expand)
                        {
                            step = Reduce(step.Key);
                            pending.Push(step);
                            if (step.Data.Alternative >= 0)
                                pending.Push(new CompileStep((step.Data.Alternative, step.AlternativeAvailable)));
                            if (step.Data.Next >= 0)
                                pending.Push(new CompileStep((step.Data.Next, step.NextAvailable)));
                            continue;
                        }
                        OrchestrationValueScheduleInstruction data = step.Data;
                        int result = step.Kind == CompileStepKind.Alias
                            ? m_Compiled[(data.Next, step.NextAvailable)]
                            : Instruction(new OrchestrationValueScheduleInstruction(data.Code, data.Operation,
                                data.Operand, data.Destination, data.Argument,
                                data.Next < 0 ? -1 : m_Compiled[(data.Next, step.NextAvailable)],
                                data.Alternative < 0 ? -1 : m_Compiled[(data.Alternative, step.AlternativeAvailable)]));
                        m_Compiled.Add(step.Key, result);
                    }
                    return m_Compiled[key];
                }

                CompileStep Reduce((int Work, BigInteger Available) key)
                {
                    BigInteger available = key.Available;
                    CompileStep Alias(int next, BigInteger nextAvailable) =>
                        new(key, CompileStepKind.Alias,
                            new OrchestrationValueScheduleInstruction(default, -1, -1, -1, 0, next, -1),
                            nextAvailable & Live(next));
                    CompileStep EmitStep(OrchestrationValueScheduleInstruction data,
                        BigInteger nextAvailable, BigInteger alternativeAvailable) =>
                        new(key, CompileStepKind.Emit, data,
                            nextAvailable & Live(data.Next), alternativeAvailable & Live(data.Alternative));

                    var work = m_Work[key.Work];
                    OrchestrationValueScheduleInstruction d = work.Data;
                    int op = d.Operation;
                    switch (work.Kind)
                    {
                        case WorkKind.BooleanValue:
                        {
                            int slot = ValueSlot(op, d.Operand, d.Argument);
                            OrchestrationValueOpcode conversion = BooleanConversion(m_Compiler.m_ValueKinds[
                                m_Structure.ValueEvaluationOffsets.Span[op] + d.Operand + 1]);
                            return Alias(Work(WorkKind.Value, op, d.Operand, argument: d.Argument,
                                next: Emit(OrchestrationValueScheduleCode.ConvertInput, op, (int)conversion,
                                    BooleanSlot(d.Argument), slot, d.Next)), available);
                        }
                        case WorkKind.Value:
                        {
                            int slot = ValueSlot(op, d.Operand, d.Argument);
                            if ((available & (BigInteger.One << slot)) != 0)
                                return Alias(d.Next, available);
                            int done = Work(WorkKind.CompleteValue, op, d.Operand, slot, next: d.Next);
                            ref readonly OperationDraft operation = ref m_Operations[op];
                            bool action = operation.Code == OrchestrationOperationCode.Extension &&
                                m_Compiler.m_Extensions[operation.ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action;
                            int compute = action
                                ? Emit(OrchestrationValueScheduleCode.ReadActionOutput, op, d.Operand, slot, next: done)
                                : Work(WorkKind.Inputs, op, argument: d.Argument,
                                    next: Emit(OrchestrationValueScheduleCode.Compute, op,
                                        m_Structure.ValueEvaluationOffsets.Span[op] + d.Operand + 1, slot, next: done));
                            return Alias(m_Diagnostics
                                ? Emit(OrchestrationValueScheduleCode.OverrideOutput, op,
                                    m_Structure.ValueEvaluationOffsets.Span[op] + d.Operand + 1, slot,
                                    next: done, alternative: compute)
                                : compute, available);
                        }
                        case WorkKind.Inputs:
                            return Alias(Inputs(op, d.Argument, d.Next), available);
                        case WorkKind.TransitionCondition:
                        {
                            int condition = m_TransitionConditions.EdgeConditions[d.Operand];
                            int computed = Work(WorkKind.BooleanValue, m_Edges[d.Operand].ConditionOperationIndex,
                                -1, argument: d.Argument, next: Emit(OrchestrationValueScheduleCode.WriteTransitionCache,
                                    op, condition, BooleanSlot(d.Argument), next: d.Next));
                            return EmitStep(new OrchestrationValueScheduleInstruction(
                                OrchestrationValueScheduleCode.ReadTransitionCache, op, condition, BooleanSlot(d.Argument),
                                0, d.Next, computed), available, available);
                        }
                        case WorkKind.Input:
                        {
                            int inputIndex = m_Operations[op].Inputs.Offset + d.Operand;
                            int slot = InputSlot(inputIndex, d.Argument);
                            if ((available & (BigInteger.One << slot)) != 0)
                                return Alias(d.Next, available);
                            OrchestrationValueInputBinding input = m_Compiler.m_Inputs[inputIndex];
                            int done = Work(WorkKind.CompleteInput, op, inputIndex, slot, next: d.Next);
                            int read;
                            switch (input.Source)
                            {
                                case OrchestrationValueInputSource.Operation:
                                    read = Work(WorkKind.Value, input.SourceIndex, input.SourceOutputPortIndex, argument: d.Argument,
                                        next: Emit(OrchestrationValueScheduleCode.CopyInput, op,
                                            ValueSlot(input.SourceIndex, input.SourceOutputPortIndex, d.Argument), slot, inputIndex, done));
                                    break;
                                case OrchestrationValueInputSource.Constant:
                                    read = Emit(OrchestrationValueScheduleCode.ReadInputConstant, op, input.SourceIndex, slot, inputIndex, done);
                                    break;
                                case OrchestrationValueInputSource.Blackboard:
                                    read = Emit(OrchestrationValueScheduleCode.ReadInputBlackboard, op, input.SourceIndex, slot, inputIndex, done);
                                    break;
                                case OrchestrationValueInputSource.StateCompletionTransition:
                                {
                                    if (m_State < 0)
                                        read = Emit(OrchestrationValueScheduleCode.WritePrediction, op, -1, slot, next: done);
                                    else
                                    {
                                        int leave = Work(WorkKind.LeavePrediction, next: done);
                                        int finish = Emit(OrchestrationValueScheduleCode.WritePrediction, op, -1, slot, next: leave);
                                        read = Work(WorkKind.EnterPrediction, m_State,
                                            next: Work(WorkKind.Select, m_State, 0, finish, 1));
                                    }
                                    break;
                                }
                                default:
                                    throw new InvalidOperationException($"不支持的正式值输入 '{input.Source}'。");
                            }
                            return Alias(m_Diagnostics
                                ? Emit(OrchestrationValueScheduleCode.OverrideInput, op, inputIndex, slot,
                                    next: done, alternative: read)
                                : read, available);
                        }
                        case WorkKind.CompleteValue:
                        case WorkKind.CompleteInput:
                        {
                            int next = d.Next;
                            if (work.Kind == WorkKind.CompleteInput)
                            {
                                OrchestrationValueOpcode conversion = m_Compiler.InputConversion(m_Operations[op], d.Operand);
                                if (conversion != OrchestrationValueOpcode.Copy)
                                    next = Emit(OrchestrationValueScheduleCode.ConvertInput, op, (int)conversion,
                                        d.Destination, d.Destination, next);
                            }
                            if (m_Diagnostics)
                                next = Emit(work.Kind == WorkKind.CompleteValue
                                        ? OrchestrationValueScheduleCode.ObserveOutput : OrchestrationValueScheduleCode.ObserveInput,
                                    op, work.Kind == WorkKind.CompleteValue
                                        ? m_Structure.ValueEvaluationOffsets.Span[op] + d.Operand + 1 : d.Operand,
                                    d.Destination, next: next);
                            return Alias(next, available | (BigInteger.One << d.Destination));
                        }
                        case WorkKind.Select:
                        case WorkKind.SelectChild:
                        {
                            OrchestrationFlowKind kind = work.Kind == WorkKind.Select ? OrchestrationFlowKind.Transition : OrchestrationFlowKind.Child;
                            int segment = op * OrchestrationProgramStructure.FlowKindCount + (int)kind - 1;
                            int start = m_Structure.OutgoingOffsets.Span[segment];
                            int count = m_Structure.OutgoingOffsets.Span[segment + 1] - start;
                            int Finish(int edge)
                            {
                                OrchestrationValueScheduleInstruction finish = m_Work[d.Destination].Data;
                                return Emit(finish.Code, finish.Operation,
                                    finish.Code == OrchestrationValueScheduleCode.Return ? finish.Operand : edge, finish.Destination,
                                    finish.Code == OrchestrationValueScheduleCode.Return
                                        ? edge < 0 ? -1 : work.Kind == WorkKind.SelectChild ? d.Operand : edge
                                        : finish.Argument, finish.Next, finish.Alternative);
                            }
                            if (d.Operand == count)
                                return Alias(Finish(-1), available);
                            int edgeIndex = m_Structure.OutgoingEdgeIndexes.Span[start + d.Operand];
                            OrchestrationFlowEdge edge = m_Edges[edgeIndex];
                            int nextEdge = Work(work.Kind, op, d.Operand + 1, d.Destination, d.Argument);
                            if (m_Operations[op].Code == OrchestrationOperationCode.StateAny && m_State >= 0 &&
                                edge.TargetOperationIndex == m_State)
                                return Alias(nextEdge, available);
                            int selection = Finish(edgeIndex);
                            if (edge.HasCondition)
                            {
                                int branch = Emit(OrchestrationValueScheduleCode.BranchCondition, op, edgeIndex,
                                    BooleanSlot(d.Argument), next: selection, alternative: nextEdge);
                                int condition = m_TransitionConditions.EdgeConditions[edgeIndex];
                                selection = work.Kind == WorkKind.Select && d.Argument == 0 && !m_Diagnostics &&
                                    m_TransitionConditions.Conditions[condition].CacheIndex >= 0
                                    ? Work(WorkKind.TransitionCondition, op, edgeIndex, argument: d.Argument, next: branch)
                                    : Work(WorkKind.BooleanValue, edge.ConditionOperationIndex, -1, argument: d.Argument, next: branch);
                            }
                            if (m_Operations[op].Code == OrchestrationOperationCode.StateAny && m_State < 0)
                                selection = Emit(OrchestrationValueScheduleCode.BranchExcludedTarget, op, edge.TargetOperationIndex,
                                    next: nextEdge, alternative: selection);
                            return Alias(selection, available);
                        }
                        case WorkKind.EnterPrediction:
                        case WorkKind.LeavePrediction:
                            return EmitStep(new OrchestrationValueScheduleInstruction(
                                work.Kind == WorkKind.EnterPrediction ? OrchestrationValueScheduleCode.EnterPrediction : OrchestrationValueScheduleCode.LeavePrediction,
                                op, -1, -1, 0, d.Next, -1), BigInteger.Zero, BigInteger.Zero);
                        default:
                        {
                            bool terminal = d.Code is OrchestrationValueScheduleCode.Return or OrchestrationValueScheduleCode.ReturnInputs;
                            bool branch = d.Code is OrchestrationValueScheduleCode.OverrideInput or OrchestrationValueScheduleCode.OverrideOutput or
                                OrchestrationValueScheduleCode.BranchBoolean or OrchestrationValueScheduleCode.BranchExcludedTarget or OrchestrationValueScheduleCode.BranchCondition;
                            return EmitStep(new OrchestrationValueScheduleInstruction(d.Code, op, d.Operand, d.Destination, d.Argument,
                                terminal ? -1 : d.Next, branch ? d.Alternative : -1), available, available);
                        }
                    }
                }

                int Instruction(OrchestrationValueScheduleInstruction instruction)
                {
                    int result = m_Instructions.Count;
                    m_Instructions.Add(instruction);
                    return result;
                }
            }
        }
    }
}
