using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            readonly HashSet<(int Operation, int State)> m_ValueContexts = new();
            readonly HashSet<(int Operation, int State)> m_ControlContexts = new();

            void ValidateValueDependencies(OperationDraft[] operations, OrchestrationFlowEdge[] controlFlow,
                OrchestrationProgramStructure structure)
            {
                var indexes = new Dictionary<(int Operation, int State), int>();
                var nodes = new List<(int Operation, int State)>();
                var dependencies = new List<List<int>>();
                var controls = new Queue<(int Operation, int State)>();
                var visitedControls = new HashSet<(int Operation, int State)>();
                var invocationEntries = new List<int>[operations.Length];
                for (int i = 0; i < operations.Length; i++)
                    invocationEntries[i] = new List<int>();
                foreach (ProgramSourceMapEntry entry in m_SourceMap)
                {
                    ProgramExecutionSource source = entry.Source;
                    if (source.TargetKind == ProgramSourceTargetKind.GraphInvocation &&
                        source.InvocationCallerKind is ProgramInvocationCallerKind.TimelineClip or
                            ProgramInvocationCallerKind.PresentationMarker or ProgramInvocationCallerKind.PresentationTreeClip)
                        invocationEntries[source.InvocationCallerOperationIndex].Add(source.InvocationEntryOperationIndex);
                }

                int Value(int operation, int state)
                {
                    var key = (operation, state);
                    if (indexes.TryGetValue(key, out int index))
                        return index;
                    index = nodes.Count;
                    indexes.Add(key, index);
                    nodes.Add(key);
                    dependencies.Add(new List<int>());
                    return index;
                }

                void Control(int operation, int state)
                {
                    var context = (operation, state);
                    if (visitedControls.Add(context))
                        controls.Enqueue(context);
                }

                IEnumerable<OrchestrationFlowEdge> Outgoing(int operation)
                {
                    int segment = operation * OrchestrationProgramStructure.FlowKindCount;
                    int start = structure.OutgoingOffsets.Span[segment];
                    int end = structure.OutgoingOffsets.Span[segment + OrchestrationProgramStructure.FlowKindCount];
                    for (int i = start; i < end; i++)
                        yield return controlFlow[structure.OutgoingEdgeIndexes.Span[i]];
                }

                IEnumerable<int> InputDependencies(int operation, int state)
                {
                    OrchestrationRange range = operations[operation].Inputs;
                    for (int i = range.Offset; i < range.Offset + range.Count; i++)
                    {
                        OrchestrationValueInputBinding input = m_Inputs[i];
                        if (input.Source == OrchestrationValueInputSource.Operation)
                            yield return input.SourceIndex;
                        else if (input.Source == OrchestrationValueInputSource.StateCompletionTransition && state >= 0)
                            foreach (OrchestrationFlowEdge edge in Outgoing(state))
                                if (edge.Kind == OrchestrationFlowKind.Transition)
                                {
                                    if (!edge.HasCondition)
                                        break;
                                    yield return edge.ConditionOperationIndex;
                                }
                    }
                }

                // 无状态的闭包覆盖全部正式值端口；预测只由真实执行上下文补入。
                for (int i = 0; i < operations.Length; i++)
                    Value(i, -1);
                Control(m_Entry.Value, -1);
                foreach (ProgramReference reference in m_References)
                    if (!reference.HasSourceOperation && reference.Kind == ProgramReferenceKind.Operation)
                        Control(reference.TargetIndex, -1);
                while (controls.Count != 0)
                {
                    (int operation, int state) = controls.Dequeue();
                    Value(operation, state);
                    foreach (int input in InputDependencies(operation, state))
                        Value(input, state);
                    OrchestrationOperationCode code = operations[operation].Code;
                    int childState = code == OrchestrationOperationCode.State ? operation : state;
                    if (code == OrchestrationOperationCode.StateMachine)
                    {
                        int initial = structure.InitialStateEntries.Span[operation];
                        if (initial >= 0)
                            Control(initial, state);
                        continue;
                    }
                    foreach (OrchestrationFlowEdge edge in Outgoing(operation))
                    {
                        if (edge.Kind == OrchestrationFlowKind.Transition)
                            continue;
                        if (edge.HasCondition)
                            Value(edge.ConditionOperationIndex, childState);
                        Control(edge.TargetOperationIndex, childState);
                    }
                    foreach (OrchestrationFlowEdge edge in Outgoing(operation))
                    {
                        if (edge.Kind != OrchestrationFlowKind.Transition ||
                            code == OrchestrationOperationCode.StateAny && edge.TargetOperationIndex == state)
                            continue;
                        if (edge.HasCondition)
                            Value(edge.ConditionOperationIndex, childState);
                        Control(edge.TargetOperationIndex, childState);
                        if (!edge.HasCondition)
                            break;
                    }
                    if (code == OrchestrationOperationCode.State)
                    {
                        int machine = operations[operation].StateMachineOwnerOperationIndex;
                        int any = structure.AnyStateEntries.Span[machine];
                        if (any >= 0)
                            Control(any, operation);
                    }
                    foreach (int entry in invocationEntries[operation])
                        Control(entry, state);
                    if (code == OrchestrationOperationCode.SubGraph)
                        Control(m_GraphCalls[operations[operation].GraphCallIndex].EntryOperation.Value, state);
                }

                // 同一操作的所有即时输出读取同一组输入；Action 输出只读取保存槽。
                for (int i = 0; i < nodes.Count; i++)
                {
                    (int operation, int state) = nodes[i];
                    OrchestrationOperationCode code = operations[operation].Code;
                    if (code is OrchestrationOperationCode.Constant or OrchestrationOperationCode.BlackboardGet ||
                        code == OrchestrationOperationCode.Extension &&
                        m_Extensions[operations[operation].ExtensionBindingIndex].Kind == OrchestrationExtensionKind.Action)
                        continue;
                    foreach (int input in InputDependencies(operation, state))
                        dependencies[i].Add(Value(input, state));
                }

                m_ValueContexts.UnionWith(nodes);
                m_ControlContexts.UnionWith(visitedControls);
                var discovery = new int[nodes.Count];
                Array.Fill(discovery, -1);
                var lowLinks = new int[nodes.Count];
                var active = new bool[nodes.Count];
                var stack = new Stack<int>();
                var reported = new HashSet<string>(StringComparer.Ordinal);
                int next = 0;
                void Visit(int index)
                {
                    discovery[index] = lowLinks[index] = next++;
                    stack.Push(index);
                    active[index] = true;
                    foreach (int dependency in dependencies[index])
                    {
                        if (discovery[dependency] < 0)
                        {
                            Visit(dependency);
                            lowLinks[index] = Math.Min(lowLinks[index], lowLinks[dependency]);
                        }
                        else if (active[dependency])
                            lowLinks[index] = Math.Min(lowLinks[index], discovery[dependency]);
                    }
                    if (lowLinks[index] != discovery[index])
                        return;
                    var component = new List<int>();
                    int member;
                    do
                    {
                        member = stack.Pop();
                        active[member] = false;
                        component.Add(member);
                    }
                    while (member != index);
                    if (component.Count == 1 && !dependencies[index].Contains(index))
                        return;
                    var operationsInCycle = new List<int>();
                    foreach (int value in component)
                        operationsInCycle.Add(nodes[value].Operation);
                    operationsInCycle.Sort();
                    if (!reported.Add(string.Join(",", operationsInCycle)))
                        return;
                    var locations = new List<string>();
                    foreach (int operation in operationsInCycle)
                        locations.Add(SourceLocation(ProgramSourceTargetKind.Operation, operation));
                    int state = nodes[index].State;
                    string context = state < 0 ? string.Empty :
                        $"，状态上下文 '{SourceLocation(ProgramSourceTargetKind.Operation, state)}'";
                    Error("value_dependency_cycle", ProgramSourceTargetKind.Operation, operationsInCycle[0],
                        $"值依赖成环{context}：{string.Join("；", locations)}。");
                }
                for (int i = 0; i < nodes.Count; i++)
                    if (discovery[i] < 0)
                        Visit(i);
            }
        }
    }
}
