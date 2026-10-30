using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
        where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
        where TStorage : struct, IOrchestrationValueStorage<TValue>
        where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
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
            int pc = m_Diagnostics.ScheduleEntry(in entry);
            Span<TValue> values = m_ScheduledValues;
            int depth = m_InputDepth++;
            while (true)
            {
                ref readonly OrchestrationValueScheduleInstruction instruction = ref m_ValueInstructions[pc];
                switch (instruction.Code)
                {
                    case OrchestrationValueScheduleCode.Return:
                    case OrchestrationValueScheduleCode.ReturnInputs:
                        m_InputDepth--;
                        return new ScheduledSelection(instruction.Argument, (OrchestrationValueSelectionReason)instruction.Operand);
                    case OrchestrationValueScheduleCode.ReadInputConstant:
                        m_CurrentOperationIndex = instruction.Operation;
                        values[instruction.Destination] = m_Program.Constant(instruction.Operand);
                        break;
                    case OrchestrationValueScheduleCode.ReadInputBlackboard:
                        ReadBlackboard(instruction.Operation, instruction.Operand, ref values[instruction.Destination]);
                        break;
                    case OrchestrationValueScheduleCode.CopyInput:
                        m_CurrentOperationIndex = instruction.Operation;
                        values[instruction.Destination] = values[instruction.Operand];
                        break;
                    case OrchestrationValueScheduleCode.ConvertInput:
                        OrchestrationTypedValueMath<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion,
                            TMath, TValues>.Unary((OrchestrationValueOpcode)instruction.Operand,
                                in values[instruction.Argument], in m_Identities, ref values[instruction.Destination]);
                        break;
                    case OrchestrationValueScheduleCode.Compute:
                        EvaluateCoreValue(instruction.Operation, instruction.Operand,
                            ScheduledArguments(instruction.Argument), depth, ref values[instruction.Destination]);
                        break;
                    case OrchestrationValueScheduleCode.ReadActionOutput:
                    {
                        m_CurrentOperationIndex = instruction.Operation;
                        ref readonly OrchestrationOperation operation = ref m_Program.Operation(instruction.Operation);
                        values[instruction.Destination] = m_Blackboards.ReadOutputValue(instruction.Operation,
                            operation.OutputSlotOffset + (instruction.Operand < 0 ? 0 : instruction.Operand));
                        break;
                    }
                    case OrchestrationValueScheduleCode.OverrideInput:
                    case OrchestrationValueScheduleCode.OverrideOutput:
                    {
                        int supplied = m_Diagnostics.FindOverride(ref this, instruction.Operation, instruction.Operand,
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
                        pc = default(TValues).Boolean(in values[instruction.Destination]) ? instruction.Next : instruction.Alternative;
                        continue;
                    case OrchestrationValueScheduleCode.BranchExcludedTarget:
                        pc = instruction.Operand == excludedTarget ? instruction.Next : instruction.Alternative;
                        continue;
                    case OrchestrationValueScheduleCode.BranchCondition:
                    {
                        bool passed = default(TValues).Boolean(in values[instruction.Destination]);
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
                        values[instruction.Destination] = default(TValues).FromInt32(instruction.Operand < 0
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
            PushState(state, -1, true, true);
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
