using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TNumerics, THost>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where THost : struct, IOrchestrationHost<TValue>
    {
        readonly struct ScheduledSelection
        {
            internal ScheduledSelection(int value, OrchestrationValueSelectionReason reason)
            {
                Value = value;
                Reason = reason;
            }

            internal int Value { get; }
            internal OrchestrationValueSelectionReason Reason { get; }
        }

        int CurrentStateOperationIndex => m_StateDepth == 0 ? m_Blackboards.BorrowedStateOperationIndex :
            m_States[m_StateDepth - 1].OperationIndex;

        int ScheduleStateContext() => m_StateContexts[CurrentStateOperationIndex + 1];

        ReadOnlySpan<int> ScheduledArguments(int argumentList)
        {
            OrchestrationRange range = m_ValueArgumentLists[argumentList];
            return m_ValueArgumentSlots.Slice(range.Offset, range.Count);
        }

        int CopyScheduledArguments(ReadOnlySpan<int> slots, int depth, int operationIndex)
        {
            Span<TValue> inputs = BorrowInputs(operationIndex, depth, slots.Length);
            for (int i = 0; i < slots.Length; i++)
                inputs[i] = m_ScheduledValues[slots[i]];
            return slots.Length;
        }

        ScheduledSelection RunValueSchedule(in OrchestrationValueScheduleEntry entry, int excludedTarget = -1)
        {
            int pc = m_Host.ScheduleEntry(in entry);
            Span<TValue> values = m_ScheduledValues;
            int depth = m_InputDepth++;
            while (true)
            {
                ref readonly OrchestrationValueScheduleInstruction instruction = ref m_ValueInstructions[pc];
#if UNITY_EDITOR
                m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.ScheduleSteps);
#endif
                switch (instruction.Code)
                {
                    case OrchestrationValueScheduleCode.Return:
                    case OrchestrationValueScheduleCode.ReturnInputs:
                        m_InputDepth--;
                        return new ScheduledSelection(instruction.Argument, (OrchestrationValueSelectionReason)instruction.Operand);
                    case OrchestrationValueScheduleCode.ReadInputConstant:
#if UNITY_EDITOR
                        m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.InputConstants);
#endif
                        m_CurrentOperationIndex = instruction.Operation;
                        values[instruction.Destination] = m_Constants[instruction.Operand];
                        break;
                    case OrchestrationValueScheduleCode.ReadInputBlackboard:
                        ReadBlackboard(instruction.Operation, instruction.Operand, ref values[instruction.Destination]);
                        break;
                    case OrchestrationValueScheduleCode.CopyInput:
#if UNITY_EDITOR
                        m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.InputCopies);
#endif
                        m_CurrentOperationIndex = instruction.Operation;
                        values[instruction.Destination] = values[instruction.Operand];
                        break;
                    case OrchestrationValueScheduleCode.ConvertInput:
#if UNITY_EDITOR
                        m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.InputConversions);
#endif
                        default(TNumerics).Unary((OrchestrationValueOpcode)instruction.Operand,
                            in values[instruction.Argument], in m_Identities, ref values[instruction.Destination]);
                        break;
                    case OrchestrationValueScheduleCode.Compute:
                    {
                        int operationIndex = instruction.Operation;
#if UNITY_EDITOR
                        m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.ValueComputes);
#endif
                        m_CurrentOperationIndex = operationIndex;
                        ref readonly OrchestrationValueOperation typed = ref m_ValueOperations[instruction.Operand];
                        ref TValue value = ref values[instruction.Destination];
                        TNumerics numerics = default;
                        switch (typed.Code)
                        {
                            case OrchestrationValueOpcode.Copy:
                                value = values[m_ValueArgumentSlots[m_ValueArgumentLists[instruction.Argument].Offset + typed.Argument]];
                                break;
                            case OrchestrationValueOpcode.Constant:
                                value = m_Constants[typed.Argument];
                                break;
                            case OrchestrationValueOpcode.HostInput:
#if UNITY_EDITOR
                                m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.HostInputReads);
#endif
                                value = m_Input.HostInputs[typed.Argument];
                                break;
                            case OrchestrationValueOpcode.InputVector2Magnitude:
#if UNITY_EDITOR
                                m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.HostInputReads);
                                m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.NumericComputes);
#endif
                                numerics.Unary(OrchestrationValueOpcode.Vector2Magnitude, in m_Input.HostInputs[typed.Argument],
                                    in m_Identities, ref value);
                                break;
                            case OrchestrationValueOpcode.InputRequest:
                                value = numerics.FromBoolean(HasRequest(m_Program.Payload<OrchestrationRequestPayload>(operationIndex).IdentityId));
                                break;
                            case OrchestrationValueOpcode.BlackboardGet:
                                ReadBlackboard(operationIndex, typed.Argument, ref value);
                                break;
                            case OrchestrationValueOpcode.GraphCallOutput:
                                ReadGraphCallOutput(operationIndex, typed.Argument, ref value);
                                break;
                            case OrchestrationValueOpcode.StateRootCompleted:
                                value = numerics.FromBoolean(CurrentStateRootCompleted());
                                break;
                            case OrchestrationValueOpcode.StateExitCause:
                                value = numerics.FromBoolean(unchecked((uint)typed.Argument) == CurrentStateExitCause());
                                break;
                            case OrchestrationValueOpcode.Cycle:
                            {
                                OrchestrationCyclePayload cycle = m_Program.Payload<OrchestrationCyclePayload>(operationIndex);
                                value = numerics.FromInt32((cycle.Start + ReadOperationState(operationIndex).ChildCursor) % cycle.Count);
                                break;
                            }
                            case OrchestrationValueOpcode.TimelineTime:
                                value = m_TimelineTime;
                                break;
                            case OrchestrationValueOpcode.Extension:
                                EvaluateExtensionValue(operationIndex, typed.Argument,
                                    ScheduledArguments(instruction.Argument), depth, ref value);
                                break;
                            default:
#if UNITY_EDITOR
                                m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.NumericComputes);
#endif
                                numerics.Compute(typed.Code, values, ScheduledArguments(instruction.Argument), in m_Identities, ref value);
                                break;
                        }
                        break;
                    }
                    case OrchestrationValueScheduleCode.ReadActionOutput:
                    {
#if UNITY_EDITOR
                        m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.SavedOutputReads);
#endif
                        m_CurrentOperationIndex = instruction.Operation;
                        ref readonly OrchestrationOperation operation = ref m_Program.Operation(instruction.Operation);
                        values[instruction.Destination] = m_Blackboards.ReadOutputValue(instruction.Operation,
                            operation.OutputSlotOffset + (instruction.Operand < 0 ? 0 : instruction.Operand));
                        break;
                    }
                    case OrchestrationValueScheduleCode.OverrideInput:
                    case OrchestrationValueScheduleCode.OverrideOutput:
                    {
                        int supplied = m_Host.FindOverride(ref this, instruction.Operation, instruction.Operand,
                            instruction.Code == OrchestrationValueScheduleCode.OverrideInput
                                ? OrchestrationValueOverrideKind.Input : OrchestrationValueOverrideKind.Output, default);
                        if (supplied >= 0)
                            values[instruction.Destination] = m_Input.Overrides[supplied].Value;
                        pc = supplied >= 0 ? instruction.Next : instruction.Alternative;
                        continue;
                    }
                    case OrchestrationValueScheduleCode.ObserveInput:
                        ObserveInput(instruction.Operation, instruction.Operand, in values[instruction.Destination]);
                        break;
                    case OrchestrationValueScheduleCode.ObserveOutput:
                        ObserveOutput(instruction.Operation, instruction.Operand, in values[instruction.Destination]);
                        break;
                    case OrchestrationValueScheduleCode.BranchBoolean:
                        m_CurrentOperationIndex = instruction.Operation;
                        pc = default(TNumerics).Boolean(in values[instruction.Destination]) ? instruction.Next : instruction.Alternative;
                        continue;
                    case OrchestrationValueScheduleCode.BranchExcludedTarget:
                        pc = instruction.Operand == excludedTarget ? instruction.Next : instruction.Alternative;
                        continue;
                    case OrchestrationValueScheduleCode.BranchCondition:
                    {
                        bool passed = default(TNumerics).Boolean(in values[instruction.Destination]);
                        TraceEdge(instruction.Operand, false, passed);
                        pc = passed ? instruction.Next : instruction.Alternative;
                        continue;
                    }
                    case OrchestrationValueScheduleCode.EnterPrediction:
                        EnterScheduledPrediction(instruction.Operation);
                        break;
                    case OrchestrationValueScheduleCode.LeavePrediction:
                        LeaveScheduledPrediction();
                        break;
                    case OrchestrationValueScheduleCode.WritePrediction:
                        m_CurrentOperationIndex = instruction.Operation;
                        values[instruction.Destination] = default(TNumerics).FromInt32(instruction.Operand < 0
                            ? -1 : m_Program.Edge(instruction.Operand).TargetOperationIndex);
                        break;
                    default:
                        Fail(OrchestrationExecutionErrorCode.ValueScheduleInstruction, instruction.Operation,
                            detail: (int)instruction.Code);
                        break;
                }
                pc = instruction.Next;
            }
        }

        void EnterScheduledPrediction(int state)
        {
            int previousFrame = m_FrameSlot;
            bool selectFrame = m_StateDepth != 0 && !m_Blackboards.IsBorrowedOperation(state) &&
                m_States[m_StateDepth - 1].ExecutionFrameIndex != previousFrame;
            m_ScheduledPredictions[m_ScheduledPredictionDepth++] =
                new OrchestrationScheduledPrediction
                {
                    PreviousFrame = previousFrame,
                    PreviousOperation = m_CurrentOperationIndex,
                    SelectedFrame = selectFrame
                };
            if (selectFrame)
                SelectExecutionFrame(m_States[m_StateDepth - 1].ExecutionFrameIndex);
            PushState(state, default, true, true);
        }

        void LeaveScheduledPrediction()
        {
            PopState();
            ref readonly OrchestrationScheduledPrediction previous =
                ref m_ScheduledPredictions[--m_ScheduledPredictionDepth];
            if (previous.SelectedFrame)
            {
                SelectExecutionFrame(previous.PreviousFrame);
                m_CurrentOperationIndex = previous.PreviousOperation;
            }
        }
    }
}
