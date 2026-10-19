using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
        where TDiagnostics : struct, IOrchestrationExecutionDiagnostics
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
            m_Workspace.States[m_StateDepth - 1].OperationIndex;

        int ScheduleStateContext() => m_Program.ValueSchedule.StateContexts.Span[CurrentStateOperationIndex + 1];

        int CopyScheduledArguments(int argumentList, int depth)
        {
            OrchestrationValueSchedule schedule = m_Program.ValueSchedule;
            OrchestrationRange range = schedule.ArgumentLists.Span[argumentList];
            ReadOnlySpan<int> slots = schedule.ArgumentSlots.Span.Slice(range.Offset, range.Count);
            for (int i = 0; i < slots.Length; i++)
                m_Workspace.Inputs[depth][i] = m_Workspace.ScheduledValues[slots[i]];
            return slots.Length;
        }

        ScheduledSelection RunValueSchedule(in OrchestrationValueScheduleEntry entry, int excludedTarget = -1)
        {
            int pc = default(TDiagnostics).ScheduleEntry(in entry);
            OrchestrationValueSchedule schedule = m_Program.ValueSchedule;
            TValue[] values = m_Workspace.ScheduledValues;
            int depth = m_InputDepth++;
            int predictions = m_ScheduledPredictionDepth;
            try
            {
                while (true)
                {
                    ref readonly OrchestrationValueScheduleInstruction instruction = ref schedule.Instructions.Span[pc];
                    switch (instruction.Code)
                    {
                        case OrchestrationValueScheduleCode.Return:
                        case OrchestrationValueScheduleCode.ReturnInputs:
                            return new ScheduledSelection(instruction.Argument, (OrchestrationValueSelectionReason)instruction.Operand);
                        case OrchestrationValueScheduleCode.ReadInputConstant:
                            m_CurrentOperationIndex = instruction.Operation;
                            values[instruction.Destination] = m_Program.Constant(instruction.Operand);
                            break;
                        case OrchestrationValueScheduleCode.ReadInputBlackboard:
                            values[instruction.Destination] = ReadBlackboard(instruction.Operation, instruction.Operand);
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
                        {
                            int count = CopyScheduledArguments(instruction.Argument, depth);
                            EvaluateCoreValue(instruction.Operation, instruction.Operand, depth, count,
                                ref values[instruction.Destination]);
                            break;
                        }
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
                            int supplied = default(TDiagnostics).FindOverride(ref this, instruction.Operation, instruction.Operand,
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
                        case OrchestrationValueScheduleCode.ReadTransitionCache:
                            pc = ReadTransitionCache(instruction.Operand, ref values[instruction.Destination])
                                ? instruction.Next : instruction.Alternative;
                            continue;
                        case OrchestrationValueScheduleCode.WriteTransitionCache:
                            WriteTransitionCache(instruction.Operand, in values[instruction.Destination]);
                            break;
                        default:
                            throw new InvalidOperationException($"未知的值调度指令 '{instruction.Code}'。");
                    }
                    pc = instruction.Next;
                }
            }
            finally
            {
                while (m_ScheduledPredictionDepth > predictions)
                    LeaveScheduledPrediction();
                m_InputDepth--;
            }
        }

        void EnterScheduledPrediction(int state)
        {
            int previousFrame = m_FrameSlot;
            bool selectFrame = m_StateDepth != 0 && !m_Blackboards.IsBorrowedOperation(state) &&
                m_Workspace.States[m_StateDepth - 1].ExecutionFrameIndex != previousFrame;
            m_Workspace.ScheduledPredictions[m_ScheduledPredictionDepth++] =
                new OrchestrationExecutionWorkspace<TValue>.ScheduledPrediction
                {
                    PreviousFrame = previousFrame,
                    PreviousOperation = m_CurrentOperationIndex,
                    SelectedFrame = selectFrame
                };
            if (selectFrame)
                SelectExecutionFrame(m_Workspace.States[m_StateDepth - 1].ExecutionFrameIndex);
            PushState(state, -1, true, true);
        }

        void LeaveScheduledPrediction()
        {
            PopState();
            ref readonly OrchestrationExecutionWorkspace<TValue>.ScheduledPrediction previous =
                ref m_Workspace.ScheduledPredictions[--m_ScheduledPredictionDepth];
            if (previous.SelectedFrame)
            {
                SelectExecutionFrame(previous.PreviousFrame);
                m_CurrentOperationIndex = previous.PreviousOperation;
            }
        }
    }
}
