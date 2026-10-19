using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            OrchestrationProgramStructure BuildProgramStructure(OperationDraft[] operations,
                OrchestrationFlowEdge[] edges, OrchestrationCorePorts ports)
            {
                int count = operations.Length;
                int kinds = OrchestrationProgramStructure.FlowKindCount;
                var edgeIndexes = new int[edges.Length];
                var offsets = new int[checked(count * kinds + 1)];
                for (int i = 0; i < edges.Length; i++)
                {
                    edgeIndexes[i] = i;
                    ref readonly OrchestrationFlowEdge edge = ref edges[i];
                    offsets[edge.SourceOperationIndex * kinds + (int)edge.Kind]++;
                    if (operations[edge.SourceOperationIndex].ExecutionFrameOwnerOperationIndex !=
                        operations[edge.TargetOperationIndex].ExecutionFrameOwnerOperationIndex)
                        Error("control_execution_frame_mismatch", ProgramSourceTargetKind.Operation, edge.SourceOperationIndex,
                            $"控制边 '{m_Identities.Resolve(edge.IdentityId)}' 跨越执行帧归属。");
                }
                Array.Sort(edgeIndexes, (leftIndex, rightIndex) =>
                {
                    ref readonly OrchestrationFlowEdge left = ref edges[leftIndex];
                    ref readonly OrchestrationFlowEdge right = ref edges[rightIndex];
                    int source = left.SourceOperationIndex.CompareTo(right.SourceOperationIndex);
                    if (source != 0)
                        return source;
                    return left.Kind == right.Kind ? OrchestrationFlowEdge.CompareOrder(in left, in right)
                        : ((byte)left.Kind).CompareTo((byte)right.Kind);
                });
                for (int i = 1; i < offsets.Length; i++)
                    offsets[i] += offsets[i - 1];

                var initial = EmptyIndexes(count);
                var any = EmptyIndexes(count);
                var onEnter = EmptyIndexes(count);
                var roots = EmptyIndexes(count);
                var onExit = EmptyIndexes(count);
                m_EvaluationOffsets = new int[count + 1];
                int maximumStateIdentity = 0;
                for (int i = 0; i < count; i++)
                {
                    ref readonly OperationDraft operation = ref operations[i];
                    m_EvaluationOffsets[i + 1] = checked(m_EvaluationOffsets[i] + operation.OutputCount + 1);
                    if (operation.Code is OrchestrationOperationCode.State or OrchestrationOperationCode.StateExit)
                        maximumStateIdentity = Math.Max(maximumStateIdentity, operation.StateIdentityId);
                    if (operation.Code == OrchestrationOperationCode.Parallel)
                    {
                        int segment = i * kinds + (int)OrchestrationFlowKind.Child - 1;
                        int children = offsets[segment + 1] - offsets[segment];
                        if (children > 31)
                            Error("parallel_child_limit", ProgramSourceTargetKind.Operation, i,
                                $"Parallel 有 {children} 个子项，超过完成位图允许的 31 个。");
                    }
                    if (operation.Code == OrchestrationOperationCode.SubGraph)
                    {
                        int segment = i * kinds + (int)OrchestrationFlowKind.Enter - 1;
                        int entries = offsets[segment + 1] - offsets[segment];
                        if (entries != 1)
                            Error("graph_call_entry_count", ProgramSourceTargetKind.Operation, i,
                                $"GraphCall 必须有一个 Enter 调用入口，实际为 {entries} 个。");
                    }
                    if (operation.Code == OrchestrationOperationCode.StateMachine)
                    {
                        int initialCount = 0;
                        int anyCount = 0;
                        int segment = i * kinds + (int)OrchestrationFlowKind.Enter - 1;
                        for (int j = offsets[segment]; j < offsets[segment + 1]; j++)
                        {
                            ref readonly OrchestrationFlowEdge edge = ref edges[edgeIndexes[j]];
                            OrchestrationOperationCode target = operations[edge.TargetOperationIndex].Code;
                            string port = m_Identities.Resolve(edge.SourcePortId);
                            if (port == "StateMachine" && target == OrchestrationOperationCode.StateEnter)
                            {
                                initialCount++;
                                initial[i] = edge.TargetOperationIndex;
                            }
                            else if (edge.SourcePortId == ports.AnyState && target == OrchestrationOperationCode.StateAny)
                            {
                                anyCount++;
                                any[i] = edge.TargetOperationIndex;
                            }
                            else
                                Error("state_machine_entry_invalid", ProgramSourceTargetKind.Operation, i,
                                    $"状态机入口 '{port}' 指向 {target}，必须明确声明 StateMachine／StateEnter 或 AnyState／StateAny。");
                        }
                        if (initialCount != 1)
                            Error("state_machine_initial_entry_count", ProgramSourceTargetKind.Operation, i,
                                $"状态机必须有一个明确的初始入口，实际为 {initialCount} 个。");
                        if (anyCount > 1)
                            Error("state_machine_any_entry_count", ProgramSourceTargetKind.Operation, i,
                                $"状态机最多有一个 AnyState 入口，实际为 {anyCount} 个。");
                    }
                    if (operation.Code != OrchestrationOperationCode.State)
                        continue;
                    if (operations[operation.StateMachineOwnerOperationIndex].ExecutionFrameOwnerOperationIndex !=
                        operation.ExecutionFrameOwnerOperationIndex)
                        Error("state_execution_frame_mismatch", ProgramSourceTargetKind.Operation, i,
                            "State 与所属 StateMachine 跨越执行帧归属。");
                    int first = offsets[i * kinds];
                    int end = offsets[(i + 1) * kinds];
                    for (int j = first; j < end; j++)
                    {
                        int index = edgeIndexes[j];
                        ref readonly OrchestrationFlowEdge edge = ref edges[index];
                        if (edge.Kind == OrchestrationFlowKind.Enter && edge.SourcePortId == ports.StateOnEnter)
                            StateEdge(onEnter, i, index, "OnEnter");
                        else if (edge.Kind == OrchestrationFlowKind.Enter && edge.SourcePortId == ports.StateRoot)
                            StateEdge(roots, i, index, "Root");
                        else if (edge.Kind == OrchestrationFlowKind.Exit && edge.SourcePortId == ports.StateOnExit)
                            StateEdge(onExit, i, index, "OnExit");
                    }
                }
                var states = EmptyIndexes(maximumStateIdentity + 1);
                for (int i = 0; i < count; i++)
                {
                    ref readonly OperationDraft operation = ref operations[i];
                    if (operation.Code is OrchestrationOperationCode.State or OrchestrationOperationCode.StateExit)
                        states[operation.StateIdentityId] = i;
                    var ancestors = new HashSet<int>();
                    for (int parent = operation.ParentStateOperationIndex; parent >= 0;
                         parent = operations[parent].ParentStateOperationIndex)
                        if (!ancestors.Add(parent))
                        {
                            Error("state_parent_cycle", ProgramSourceTargetKind.Operation, i,
                                "状态祖先路径成环。");
                            break;
                        }
                    if (operation.Code == OrchestrationOperationCode.EmitOutput)
                    {
                        if (operation.Inputs.Count != m_OutputTemplates[operation.Integer0].Ports.Length)
                            Error("output_input_count", ProgramSourceTargetKind.Operation, i,
                                "输出节点必须为模板的每个端口提供输入。");
                    }
                }
                if (operations[m_Entry.Value].Code != OrchestrationOperationCode.Root ||
                    operations[m_Entry.Value].ExecutionFrameOwnerOperationIndex != m_Entry.Value)
                    Error("program_root_invalid", ProgramSourceTargetKind.Operation, m_Entry.Value,
                        "编排程序入口必须是持有自身执行帧的 Root。");

                var callOffsets = new int[count + 1];
                var calls = new List<int>();
                var retained = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    ref readonly OperationDraft query = ref operations[i];
                    if (query.Code == OrchestrationOperationCode.TimelineSection)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            ref readonly OperationDraft call = ref operations[j];
                            if (call.Code != OrchestrationOperationCode.Timeline ||
                                call.TimelineContentIdentityId != query.TimelineContentIdentityId ||
                                call.ExecutionFrameOwnerOperationIndex != query.ExecutionFrameOwnerOperationIndex)
                                continue;
                            calls.Add(j);
                            retained[j] = true;
                        }
                        if (calls.Count == callOffsets[i])
                            Error("timeline_section_call_missing", ProgramSourceTargetKind.Operation, i,
                                "Timeline 分段查询的内容在同一执行帧归属中没有正式播放调用。");
                    }
                    callOffsets[i + 1] = calls.Count;
                }
                foreach (ProgramGraphCallFrame frame in m_GraphCalls)
                {
                    int owner = frame.OwnerOperation.Value;
                    int frameOwner = operations[owner].ExecutionFrameOwnerOperationIndex;
                    if (operations[frame.EntryOperation.Value].ExecutionFrameOwnerOperationIndex != frameOwner)
                        Error("graph_call_execution_frame_mismatch", ProgramSourceTargetKind.Operation, owner,
                            "GraphCall 与入口跨越执行帧归属。");
                    foreach (ProgramGraphParameterBinding parameter in frame.Inputs)
                        ValidateParameterFrame(owner, parameter, frameOwner);
                    foreach (ProgramGraphParameterBinding parameter in frame.Outputs)
                        ValidateParameterFrame(owner, parameter, frameOwner);
                }
                return new OrchestrationProgramStructure(edgeIndexes, offsets, initial, any, onEnter, roots, onExit,
                    m_EvaluationOffsets, states, callOffsets, calls.ToArray(), retained);
            }

            void StateEdge(int[] entries, int operation, int edge, string port)
            {
                if (entries[operation] >= 0)
                    Error("state_control_entry_multiple", ProgramSourceTargetKind.Operation, operation,
                        $"State 的 '{port}' 入口重复声明。");
                entries[operation] = edge;
            }

            void ValidateParameterFrame(int operation, ProgramGraphParameterBinding parameter, int frameOwner)
            {
                if (m_Blackboards[ValueSlot(parameter.StateSlot)].ExecutionFrameOwnerOperationIndex != frameOwner)
                    Error("graph_parameter_execution_frame_mismatch", ProgramSourceTargetKind.Operation, operation,
                        $"图参数 '{parameter.ParameterName}' 跨越执行帧归属。");
            }
        }
    }
}
