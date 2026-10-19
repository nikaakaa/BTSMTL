using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            sealed class TransitionConditions
            {
                internal OrchestrationTransitionCondition[] Conditions;
                internal OrchestrationTransitionSource[] Sources;
                internal int[] EdgeConditions;
                internal int CacheCount;
                internal int SnapshotCount;
            }

            TransitionConditions BuildTransitionConditions(OperationDraft[] operations,
                OrchestrationFlowEdge[] edges, OrchestrationProgramStructure structure)
            {
                var machines = new int[operations.Length];
                Array.Fill(machines, -1);
                for (int operation = 0; operation < operations.Length; operation++)
                {
                    if (operations[operation].Code == OrchestrationOperationCode.State)
                        machines[operation] = operations[operation].StateMachineOwnerOperationIndex;
                    else if (operations[operation].Code == OrchestrationOperationCode.StateMachine)
                    {
                        machines[structure.InitialStateEntries.Span[operation]] = operation;
                        int any = structure.AnyStateEntries.Span[operation];
                        if (any >= 0)
                            machines[any] = operation;
                    }
                }
                var result = new TransitionConditions { EdgeConditions = new int[edges.Length] };
                Array.Fill(result.EdgeConditions, -1);
                var conditions = new List<OrchestrationTransitionCondition>();
                var sources = new List<OrchestrationTransitionSource>();
                var dependencies = new Dictionary<(int Operation, int Output), OrchestrationTransitionSource[]>();

                OrchestrationTransitionSource[] Value(int operation, int output)
                {
                    var key = (operation, output);
                    if (dependencies.TryGetValue(key, out OrchestrationTransitionSource[] existing))
                        return existing;
                    ref readonly OrchestrationValueOperation typed = ref m_ValueOperations[
                        structure.ValueEvaluationOffsets.Span[operation] + output + 1];
                    var found = new HashSet<(OrchestrationTransitionSourceKind Kind, int Index)>();
                    void Add(OrchestrationTransitionSourceKind kind, int index) => found.Add((kind, index));
                    bool inputs = false;
                    switch (typed.Code)
                    {
                        case OrchestrationValueOpcode.Constant:
                            break;
                        case OrchestrationValueOpcode.HostInput:
                        case OrchestrationValueOpcode.InputVector2Magnitude:
                            Add(OrchestrationTransitionSourceKind.HostInput, typed.Argument);
                            break;
                        case OrchestrationValueOpcode.InputRequest:
                            Add(OrchestrationTransitionSourceKind.InputRequest, operation);
                            break;
                        case OrchestrationValueOpcode.BlackboardGet:
                            Add(OrchestrationTransitionSourceKind.Blackboard, typed.Argument);
                            break;
                        case OrchestrationValueOpcode.GraphCallOutput:
                            Add(OrchestrationTransitionSourceKind.GraphCallOutput, operation);
                            break;
                        case OrchestrationValueOpcode.StateRootCompleted:
                            Add(OrchestrationTransitionSourceKind.StateRootCompleted, -1);
                            break;
                        case OrchestrationValueOpcode.StateExitCause:
                            Add(OrchestrationTransitionSourceKind.StateExitCause, -1);
                            break;
                        case OrchestrationValueOpcode.Cycle:
                            Add(OrchestrationTransitionSourceKind.Cycle, operation);
                            break;
                        case OrchestrationValueOpcode.TimelineTime:
                            Add(OrchestrationTransitionSourceKind.TimelineTime, operation);
                            break;
                        case OrchestrationValueOpcode.TimelineSection:
                            Add(OrchestrationTransitionSourceKind.TimelineSection, operation);
                            break;
                        case OrchestrationValueOpcode.Extension:
                        {
                            bool action = m_Extensions[operations[operation].ExtensionBindingIndex].Kind ==
                                OrchestrationExtensionKind.Action;
                            Add(action ? OrchestrationTransitionSourceKind.ActionOutput
                                : OrchestrationTransitionSourceKind.Extension, operation);
                            inputs = !action;
                            break;
                        }
                        default:
                            inputs = true;
                            break;
                    }
                    if (inputs)
                    {
                        OrchestrationRange range = operations[operation].Inputs;
                        for (int i = range.Offset; i < range.Offset + range.Count; i++)
                        {
                            OrchestrationValueInputBinding input = m_Inputs[i];
                            switch (input.Source)
                            {
                                case OrchestrationValueInputSource.Operation:
                                    foreach (OrchestrationTransitionSource source in Value(input.SourceIndex, input.SourceOutputPortIndex))
                                        Add(source.Kind, source.Index);
                                    break;
                                case OrchestrationValueInputSource.Blackboard:
                                    Add(OrchestrationTransitionSourceKind.Blackboard, input.SourceIndex);
                                    break;
                                case OrchestrationValueInputSource.StateCompletionTransition:
                                    Add(OrchestrationTransitionSourceKind.Prediction, operation);
                                    break;
                            }
                        }
                    }
                    var ordered = new List<(OrchestrationTransitionSourceKind Kind, int Index)>(found);
                    ordered.Sort((left, right) => left.Kind == right.Kind
                        ? left.Index.CompareTo(right.Index) : left.Kind.CompareTo(right.Kind));
                    var value = new OrchestrationTransitionSource[ordered.Count];
                    for (int i = 0; i < value.Length; i++)
                        value[i] = new OrchestrationTransitionSource(ordered[i].Kind, ordered[i].Index);
                    dependencies.Add(key, value);
                    return value;
                }

                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    OrchestrationFlowEdge edge = edges[edgeIndex];
                    if (edge.Kind != OrchestrationFlowKind.Transition || !edge.HasCondition)
                        continue;
                    OrchestrationTransitionSource[] reads = Value(edge.ConditionOperationIndex, -1);
                    bool stable = true;
                    foreach (OrchestrationTransitionSource source in reads)
                        stable &= source.Kind is OrchestrationTransitionSourceKind.HostInput or
                            OrchestrationTransitionSourceKind.InputRequest or OrchestrationTransitionSourceKind.StateRootCompleted or
                            OrchestrationTransitionSourceKind.StateExitCause or OrchestrationTransitionSourceKind.Cycle ||
                            source.Kind == OrchestrationTransitionSourceKind.Blackboard &&
                            m_Blackboards[source.Index].ScopeKind != OrchestrationScopeKind.ActionInstance;
                    int cache = stable ? result.CacheCount++ : -1;
                    int snapshot = stable ? result.SnapshotCount : -1;
                    if (stable)
                        result.SnapshotCount = checked(result.SnapshotCount + reads.Length);
                    result.EdgeConditions[edgeIndex] = conditions.Count;
                    conditions.Add(new OrchestrationTransitionCondition(edgeIndex, machines[edge.SourceOperationIndex],
                        new OrchestrationRange(sources.Count, reads.Length), cache, snapshot));
                    sources.AddRange(reads);
                }
                result.Conditions = conditions.ToArray();
                result.Sources = sources.ToArray();
                return result;
            }
        }
    }
}
