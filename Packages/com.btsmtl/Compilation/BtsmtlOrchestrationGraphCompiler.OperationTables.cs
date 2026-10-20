using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        readonly struct OperationDraft
        {
            internal OperationDraft(OrchestrationOperationCode code, bool isRunnable,
                int identityId, int integer0, int integer1, int constantIndex, int inputSlot,
                int requestIdentityId, int valueSlot, int timelineContentIdentityId,
                int stateIdentityId, int stateMachineOwnerOperationIndex,
                int extensionBindingIndex, int graphCallIndex, OrchestrationRange inputs,
                int outputCount, int outputSlotOffset, OrchestrationRange lifecycleHooks,
                int blackboardFactsBindingIndex, int parentStateOperationIndex,
                OrchestrationRange outputAssignments, int executionFrameOwnerOperationIndex)
            {
                Code = code;
                IsRunnable = isRunnable;
                IdentityId = identityId;
                Integer0 = integer0;
                Integer1 = integer1;
                ConstantIndex = constantIndex;
                InputSlot = inputSlot;
                RequestIdentityId = requestIdentityId;
                ValueSlot = valueSlot;
                TimelineContentIdentityId = timelineContentIdentityId;
                StateIdentityId = stateIdentityId;
                StateMachineOwnerOperationIndex = stateMachineOwnerOperationIndex;
                ExtensionBindingIndex = extensionBindingIndex;
                GraphCallIndex = graphCallIndex;
                Inputs = inputs;
                OutputCount = outputCount;
                OutputSlotOffset = outputSlotOffset;
                LifecycleHooks = lifecycleHooks;
                BlackboardFactsBindingIndex = blackboardFactsBindingIndex;
                ParentStateOperationIndex = parentStateOperationIndex;
                OutputAssignments = outputAssignments;
                ExecutionFrameOwnerOperationIndex = executionFrameOwnerOperationIndex;
            }

            internal readonly OrchestrationOperationCode Code;
            internal readonly bool IsRunnable;
            internal readonly int IdentityId;
            internal readonly int Integer0;
            internal readonly int Integer1;
            internal readonly int ConstantIndex;
            internal readonly int InputSlot;
            internal readonly int RequestIdentityId;
            internal readonly int ValueSlot;
            internal readonly int TimelineContentIdentityId;
            internal readonly int StateIdentityId;
            internal readonly int StateMachineOwnerOperationIndex;
            internal readonly int ExtensionBindingIndex;
            internal readonly int GraphCallIndex;
            internal readonly OrchestrationRange Inputs;
            internal readonly int OutputCount;
            internal readonly int OutputSlotOffset;
            internal readonly OrchestrationRange LifecycleHooks;
            internal readonly int BlackboardFactsBindingIndex;
            internal readonly int ParentStateOperationIndex;
            internal readonly OrchestrationRange OutputAssignments;
            internal readonly int ExecutionFrameOwnerOperationIndex;
            internal OrchestrationComparisonKind Comparison => (OrchestrationComparisonKind)Integer0;
            internal OrchestrationCalculationKind Calculation => (OrchestrationCalculationKind)Integer0;
            internal OrchestrationAssignmentKind Assignment => (OrchestrationAssignmentKind)Integer1;
        }

        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            ushort CompactIndex(int value, int operation, string field)
            {
                if ((uint)value <= ushort.MaxValue)
                    return (ushort)value;
                Error("operation_index_width", ProgramSourceTargetKind.Operation, operation,
                    $"字段 '{field}' 的值 {value} 超出已发布 IR 的 ushort 宽度。");
                return 0;
            }

            OrchestrationIndexRange CompactRange(OrchestrationRange range, int operation, string field) =>
                new(CompactIndex(range.Offset, operation, field + ".Offset"),
                    CompactIndex(range.Count, operation, field + ".Count"));

            void BuildOperationTables(OperationDraft[] drafts, OrchestrationAssignmentOpcode[] assignments,
                out OrchestrationOperation[] operations, out byte[] payloads,
                out OrchestrationOperationColdData[] cold)
            {
                operations = new OrchestrationOperation[drafts.Length];
                cold = new OrchestrationOperationColdData[drafts.Length];
                var bytes = new List<byte>();
                var order = new int[drafts.Length];
                for (int i = 0; i < order.Length; i++)
                    order[i] = i;
                Array.Sort(order, (a, b) => drafts[a].Code == drafts[b].Code
                    ? a.CompareTo(b) : drafts[a].Code.CompareTo(drafts[b].Code));
                CompactIndex(drafts.Length, m_Entry.Value, "OperationCount");
                foreach (int i in order)
                {
                    OperationDraft d = drafts[i];
                    ushort payload = CompactIndex(bytes.Count, i, "PayloadOffset");
                    switch (d.Code)
                    {
                        case OrchestrationOperationCode.Loop:
                        case OrchestrationOperationCode.Parallel:
                            Append(new OrchestrationModePayload(d.Integer0));
                            break;
                        case OrchestrationOperationCode.Cycle:
                            Append(new OrchestrationCyclePayload(d.Integer0, d.Integer1));
                            break;
                        case OrchestrationOperationCode.SubGraph:
                            Append(new OrchestrationGraphCallPayload(CompactIndex(d.GraphCallIndex, i, "GraphCallIndex")));
                            break;
                        case OrchestrationOperationCode.State:
                            Append(new OrchestrationStatePayload(d.StateIdentityId,
                                CompactIndex(d.StateMachineOwnerOperationIndex, i, "StateMachineOwnerOperationIndex")));
                            break;
                        case OrchestrationOperationCode.StateExit:
                            Append(new OrchestrationStateIdentityPayload(d.StateIdentityId));
                            break;
                        case OrchestrationOperationCode.Timeline:
                            Append(new OrchestrationTimelinePayload(d.TimelineContentIdentityId, d.Integer0));
                            break;
                        case OrchestrationOperationCode.InputRequest:
                            Append(new OrchestrationRequestPayload(d.RequestIdentityId));
                            break;
                        case OrchestrationOperationCode.BlackboardSet:
                            Append(new OrchestrationBlackboardSetPayload(CompactIndex(d.ValueSlot, i, "ValueSlot"), assignments[i]));
                            break;
                        case OrchestrationOperationCode.EmitOutput:
                            Append(new OrchestrationOutputPayload(CompactIndex(d.Integer0, i, "OutputTemplateIndex"), d.IdentityId));
                            break;
                        case OrchestrationOperationCode.Extension:
                            Append(new OrchestrationExtensionPayload(CompactIndex(d.ExtensionBindingIndex, i, "ExtensionBindingIndex"),
                                CompactRange(d.OutputAssignments, i, "OutputAssignments")));
                            break;
                    }
                    operations[i] = new OrchestrationOperation(d.Code, d.IsRunnable, payload,
                        CompactRange(d.Inputs, i, "Inputs"), CompactIndex(d.OutputCount, i, "OutputCount"),
                        CompactIndex(d.OutputSlotOffset + 1, i, "OutputSlotOffset"), CompactRange(d.LifecycleHooks, i, "LifecycleHooks"),
                        CompactIndex(d.BlackboardFactsBindingIndex + 1, i, "BlackboardFactsBindingIndex"),
                        CompactIndex(d.ParentStateOperationIndex + 1, i, "ParentStateOperationIndex"),
                        CompactIndex(d.ExecutionFrameOwnerOperationIndex, i, "ExecutionFrameOwnerOperationIndex"));
                    cold[i] = new OrchestrationOperationColdData(d.IdentityId,
                        d.Code is OrchestrationOperationCode.Compare or OrchestrationOperationCode.Calculate ? d.Integer0 : 0);
                }
                payloads = bytes.ToArray();

                void Append<T>(T data) where T : unmanaged
                {
                    ReadOnlySpan<byte> source = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref data, 1));
                    foreach (byte value in source)
                        bytes.Add(value);
                }
            }
        }
    }
}
