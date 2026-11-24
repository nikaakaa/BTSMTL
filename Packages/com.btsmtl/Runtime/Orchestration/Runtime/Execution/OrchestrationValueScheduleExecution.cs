using System;
using System.Runtime.CompilerServices;
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
            internal ScheduledSelection(int target)
            {
                Value = OrchestrationPreparedScheduleTarget.ReturnValue(target);
                Reason = OrchestrationPreparedScheduleTarget.ReturnReason(target);
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ScheduledSelection RunValueSchedule(in OrchestrationValueScheduleEntry entry, int excludedTarget = -1)
        {
            int target = m_Host.ScheduleEntry(in entry);
            return OrchestrationPreparedScheduleTarget.IsReturn(target)
                ? new ScheduledSelection(target) : ExecuteValueSchedule(target, excludedTarget);
        }

        ScheduledSelection ExecuteValueSchedule(int pc, int excludedTarget)
        {
#if UNITY_EDITOR
            using var workloadCost = new OrchestrationWorkloadCostScope(m_WorkloadObservation,
                OrchestrationWorkloadCost.ValueSchedule);
#endif
            Span<TValue> values = m_ScheduledValues;
            int depth = m_InputDepth++;
            do
            {
                ref readonly OrchestrationValueScheduleInstruction instruction = ref m_ValueInstructions[pc];
#if UNITY_EDITOR
                m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.ScheduleSteps);
#endif
                switch (instruction.Code)
                {
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
                        using (new OrchestrationWorkloadCostScope(m_WorkloadObservation, OrchestrationWorkloadCost.Numeric))
#endif
                        default(TNumerics).Unary((OrchestrationValueOpcode)instruction.Operand,
                            in values[instruction.Argument], in m_Identities, ref values[instruction.Destination]);
                        break;
                    case OrchestrationPreparedScheduleTarget.ComputeNumber:
#if UNITY_EDITOR
                        m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.ValueComputes);
                        m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.NumericComputes);
                        using (new OrchestrationWorkloadCostScope(m_WorkloadObservation, OrchestrationWorkloadCost.Numeric))
#endif
                        {
                            m_CurrentOperationIndex = instruction.Operation;
                            default(TNumerics).Compute((OrchestrationValueOpcode)instruction.Operand, values,
                                m_ValueArgumentSlots.Slice(instruction.Argument, instruction.Alternative),
                                in m_Identities, ref values[instruction.Destination]);
                        }
                        break;
                    case OrchestrationPreparedScheduleTarget.ReadValue:
                    {
                        int operationIndex = instruction.Operation;
#if UNITY_EDITOR
                        m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.ValueComputes);
#endif
                        m_CurrentOperationIndex = operationIndex;
                        int argument = instruction.Argument;
                        ref TValue value = ref values[instruction.Destination];
                        TNumerics numerics = default;
                        switch ((OrchestrationValueOpcode)instruction.Operand)
                        {
                            case OrchestrationValueOpcode.Copy:
                                value = values[argument];
                                break;
                            case OrchestrationValueOpcode.Constant:
                                value = m_Constants[argument];
                                break;
                            case OrchestrationValueOpcode.HostInput:
#if UNITY_EDITOR
                                m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.HostInputReads);
#endif
                                value = m_Input.HostInputs[argument];
                                break;
                            case OrchestrationValueOpcode.InputVector2Magnitude:
#if UNITY_EDITOR
                                m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.HostInputReads);
                                m_WorkloadObservation?.Count(operationIndex, OrchestrationWorkloadCounter.NumericComputes);
                                using (new OrchestrationWorkloadCostScope(m_WorkloadObservation, OrchestrationWorkloadCost.Numeric))
#endif
                                numerics.Unary(OrchestrationValueOpcode.Vector2Magnitude, in m_Input.HostInputs[argument],
                                    in m_Identities, ref value);
                                break;
                            case OrchestrationValueOpcode.InputRequest:
                                value = numerics.FromBoolean(HasRequest(m_Program.Payload<OrchestrationRequestPayload>(operationIndex).IdentityId));
                                break;
                            case OrchestrationValueOpcode.BlackboardGet:
                                ReadBlackboard(operationIndex, argument, ref value);
                                break;
                            case OrchestrationValueOpcode.GraphCallOutput:
                                ReadGraphCallOutput(operationIndex, argument, ref value);
                                break;
                            case OrchestrationValueOpcode.StateRootCompleted:
                                value = numerics.FromBoolean(CurrentStateRootCompleted());
                                break;
                            case OrchestrationValueOpcode.StateExitCause:
                                value = numerics.FromBoolean(unchecked((uint)argument) == CurrentStateExitCause());
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
                                value = EvaluateExtensionOutputs(operationIndex,
                                    ScheduledArguments(instruction.Alternative), depth)[argument];
                                break;
                        }
                        break;
                    }
                    case OrchestrationPreparedScheduleTarget.ReadExtensionOutputs:
                    {
#if UNITY_EDITOR
                        m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.ValueComputes);
#endif
                        m_CurrentOperationIndex = instruction.Operation;
                        ReadOnlySpan<TValue> outputs = EvaluateExtensionOutputs(instruction.Operation,
                            ScheduledArguments(instruction.Alternative), depth);
                        values[instruction.Destination] = outputs[instruction.Argument];
                        pc = instruction.Next;
                        for (int remaining = instruction.Operand - 1; remaining != 0; remaining--)
                        {
                            ref readonly var step = ref m_ValueInstructions[pc];
                            m_CurrentOperationIndex = step.Operation;
                            if (step.Code == OrchestrationValueScheduleCode.CopyInput)
                            {
#if UNITY_EDITOR
                                m_WorkloadObservation?.Count(step.Operation, OrchestrationWorkloadCounter.InputCopies);
#endif
                                values[step.Destination] = values[step.Operand];
                            }
                            else
                                values[step.Destination] = outputs[step.Argument];
                            pc = step.Next;
                        }
                        continue;
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
                    case OrchestrationPreparedScheduleTarget.ReadReusableBlackboard:
                        m_CurrentOperationIndex = instruction.Operation;
                        ref byte valid = ref m_ReuseFlags[instruction.Argument];
                        if (valid == 0)
                        {
#if UNITY_EDITOR
                            m_WorkloadObservation?.Count(instruction.Operation, OrchestrationWorkloadCounter.ValueComputes);
#endif
                            ReadBlackboard(instruction.Operation, instruction.Operand, ref values[instruction.Destination]);
                            valid = 1;
                        }
                        break;
                    case OrchestrationPreparedScheduleTarget.GuardReusableValue:
                        m_CurrentOperationIndex = instruction.Operation;
                        pc = m_ReuseFlags[instruction.Destination] != 0 ? instruction.Next : instruction.Alternative;
                        continue;
                    case OrchestrationPreparedScheduleTarget.MarkReusableValue:
                        m_CurrentOperationIndex = instruction.Operation;
                        m_ReuseFlags[instruction.Destination] = 1;
                        break;
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
            } while (!OrchestrationPreparedScheduleTarget.IsReturn(pc));
            m_InputDepth--;
            return new ScheduledSelection(pc);
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
