using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TNumerics, THost>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where THost : struct, IOrchestrationHost<TValue>
    {
        public void Evaluate(int operationIndex, ref TValue value, int outputPortIndex = -1)
        {
            m_CurrentOperationIndex = operationIndex;
            int slot = m_Program.ValueEvaluationSlot(operationIndex, outputPortIndex);
            ref readonly OrchestrationValueScheduleEntry entry = ref m_ValueEntries[
                ScheduleStateContext() * m_Program.ScheduleValueSlotCount + slot];
            RunValueSchedule(in entry);
            value = m_ScheduledValues[slot];
        }

        public bool EvaluateBoolean(int operationIndex)
        {
            ref readonly OrchestrationValueScheduleEntry entry = ref m_BooleanEntries[
                ScheduleStateContext() * m_Program.OperationCount + operationIndex];
            RunValueSchedule(in entry);
            return default(TNumerics).Boolean(in m_ScheduledValues[m_Program.ScheduleBooleanSlot]);
        }

        void EvaluateCoreValue(int operationIndex, int valueOperation, ReadOnlySpan<int> arguments,
            int depth, ref TValue value)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationValueOperation typed = ref m_ValueOperations[valueOperation];
            TNumerics values = default;
            switch (typed.Code)
            {
                case OrchestrationValueOpcode.Copy:
                    value = m_ScheduledValues[arguments[typed.Argument]];
                    return;
                case OrchestrationValueOpcode.Constant:
                    value = m_Program.Constant(typed.Argument);
                    return;
                case OrchestrationValueOpcode.HostInput:
                    value = m_Input.HostInputs[typed.Argument];
                    return;
                case OrchestrationValueOpcode.InputVector2Magnitude:
                    values.Unary(OrchestrationValueOpcode.Vector2Magnitude, in m_Input.HostInputs[typed.Argument],
                        in m_Identities, ref value);
                    return;
                case OrchestrationValueOpcode.InputRequest:
                    value = values.FromBoolean(HasRequest(m_Program.Payload<OrchestrationRequestPayload>(operationIndex).IdentityId));
                    return;
                case OrchestrationValueOpcode.BlackboardGet:
                    ReadBlackboard(operationIndex, typed.Argument, ref value);
                    return;
                case OrchestrationValueOpcode.GraphCallOutput:
                    ReadGraphCallOutput(operationIndex, typed.Argument, ref value);
                    return;
                case OrchestrationValueOpcode.StateRootCompleted:
                    value = values.FromBoolean(CurrentStateRootCompleted());
                    return;
                case OrchestrationValueOpcode.StateExitCause:
                    value = values.FromBoolean(unchecked((uint)typed.Argument) == CurrentStateExitCause());
                    return;
                case OrchestrationValueOpcode.Cycle:
                {
                    OrchestrationCyclePayload cycle = m_Program.Payload<OrchestrationCyclePayload>(operationIndex);
                    value = values.FromInt32((cycle.Start + ReadOperationState(operationIndex).ChildCursor) % cycle.Count);
                    return;
                }
                case OrchestrationValueOpcode.TimelineTime:
                    value = m_TimelineTime;
                    return;
                case OrchestrationValueOpcode.Extension:
                {
                    ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                    ushort binding = m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex;
                    RequireRowDepth(operationIndex, depth + 1);
                    int count = CopyScheduledArguments(arguments, depth, operationIndex);
                    ReadOnlySpan<TValue> inputs = InputRow(depth).Slice(0, count);
                    Span<TValue> outputs = BorrowOutputs(operationIndex, depth, operation.OutputCount);
                    var invocation = new OrchestrationExtensionArguments<TValue>(
                        OrchestrationExtensionInvocationKind.Evaluate, operationIndex, ReadGeneration(operationIndex),
                        m_Input.Tick, CurrentStopContextStorage(), StateAncestors(operationIndex), ReadExecutionFrame(operationIndex),
                        inputs, outputs, in m_TimelineInvocation);
                    RequireSynchronousValueResult(operationIndex, binding,
                        m_Host.InvokeReadOnly(binding, in invocation));
                    value = outputs[typed.Argument];
                    return;
                }
                default:
                    values.Compute(typed.Code, m_ScheduledValues, arguments, in m_Identities, ref value);
                    return;
            }
        }

        int ReadInputs(int operationIndex, out int count)
        {
            int depth = EnterInputRow(operationIndex);
            ref readonly OrchestrationValueScheduleEntry entry = ref m_InputEntries[
                ScheduleStateContext() * m_Program.OperationCount + operationIndex];
            int arguments = RunValueSchedule(in entry).Value;
            count = CopyScheduledArguments(ScheduledArguments(arguments), depth, operationIndex);
            return depth;
        }

        int EnterInputRow(int operationIndex)
        {
            int depth = m_InputDepth;
            RequireRowDepth(operationIndex, depth + 1);
            m_InputDepth++;
            return depth;
        }

        void ReleaseInputs() => m_InputDepth--;

        bool HasRequest(int identityId)
        {
            foreach (int request in m_Input.RequestIdentityIds)
                if (request == identityId)
                    return true;
            return false;
        }

        internal int ConsumeBlackboardOverride(int valueSlot, in OrchestrationBlackboardOwnerToken owner)
        {
            OrchestrationValueOverrideAddress address = BlackboardOverrideAddress(valueSlot, in owner);
            int index = OrchestrationValueOverride<TValue>.Find(m_Input.Overrides, in address);
            if (index < 0)
                return -1;
            ref OrchestrationValueOverride<TValue> supplied = ref m_Input.Overrides[index];
            if (!IsPredictiveEvaluation)
                supplied.Status = OrchestrationValueOverrideStatus.Consumed;
            return index;
        }

        void ObserveInput(int operationIndex, int inputSlot, in TValue value)
        {
            m_CurrentOperationIndex = operationIndex;
            m_Host.Observe(ref this, operationIndex, inputSlot, true, in value);
        }

        void ObserveOutput(int operationIndex, int outputSlot, in TValue value)
        {
            m_CurrentOperationIndex = operationIndex;
            m_Host.Observe(ref this, operationIndex, outputSlot, false, in value);
        }

        void PrepareGraphCall(int operationIndex)
        {
            OrchestrationGraphCallView call = m_Program.GraphCall(m_Program.Payload<OrchestrationGraphCallPayload>(operationIndex).CallIndex);
            if (IsActive(call.EntryOperationIndex))
                return;
            int depth = ReadInputs(operationIndex, out int count);
            try
            {
                foreach (ref readonly OrchestrationGraphParameter output in call.Outputs)
                {
                    m_Blackboards.WriteValue(output.ValueSlot, in m_Program.DefaultValue(output.ValueSlot));
                    ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(output.ValueSlot);
                    metadata.Revision = checked(metadata.Revision + 1);
                }
                ReadOnlySpan<OrchestrationGraphParameter> parameters = call.Inputs;
                for (int i = 0; i < parameters.Length; i++)
                {
                    m_Blackboards.WriteValue(parameters[i].ValueSlot, in InputRow(depth)[i]);
                    ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(parameters[i].ValueSlot);
                    metadata.Revision = checked(metadata.Revision + 1);
                }
            }
            finally
            {
                ReleaseInputs();
            }
        }

        void ReadGraphCallOutput(int operationIndex, int outputPortIndex, ref TValue destination)
        {
            OrchestrationGraphCallView call = m_Program.GraphCall(m_Program.Payload<OrchestrationGraphCallPayload>(operationIndex).CallIndex);
            int slot = call.Outputs[outputPortIndex].ValueSlot;
            OrchestrationOperationStatus status = m_Program.Operation(operationIndex).IsRunnable
                ? ReadStatus(call.EntryOperationIndex) : ReadOperationState(operationIndex).Status;
            if (status == OrchestrationOperationStatus.Success)
                m_Blackboards.ReadValue(slot, ref destination);
            else
                destination = m_Program.DefaultValue(slot);
        }

        OrchestrationOperationResult ExecuteCoreLeaf(int operationIndex)
        {
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            switch (operation.Code)
            {
                case OrchestrationOperationCode.StateRootCompleted:
                case OrchestrationOperationCode.StateExitCause:
                case OrchestrationOperationCode.BlackboardGet:
                case OrchestrationOperationCode.InputBoolean:
                case OrchestrationOperationCode.InputScalar:
                case OrchestrationOperationCode.InputVector2:
                case OrchestrationOperationCode.InputVector2Magnitude:
                case OrchestrationOperationCode.InputRequest:
                case OrchestrationOperationCode.ConditionResult:
                case OrchestrationOperationCode.Compare:
                case OrchestrationOperationCode.And:
                case OrchestrationOperationCode.Or:
                case OrchestrationOperationCode.Not:
                case OrchestrationOperationCode.Constant:
                case OrchestrationOperationCode.Calculate:
                case OrchestrationOperationCode.SelectValue:
                case OrchestrationOperationCode.HostInput:
                case OrchestrationOperationCode.IdentityValue:
                case OrchestrationOperationCode.TimelineTime:
                    return EvaluateControlValue(operationIndex);
                case OrchestrationOperationCode.Extension:
                    if (m_Host.ExecutionContract(m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex).Kind !=
                        OrchestrationExtensionKind.Action)
                        return EvaluateControlValue(operationIndex);
                    break;
            }
            int depth = ReadInputs(operationIndex, out int count);
            try
            {
                m_CurrentOperationIndex = operationIndex;
                switch (operation.Code)
                {
                    case OrchestrationOperationCode.EmitOutput:
                        EmitOutput(operationIndex, InputRow(depth).Slice(0, count));
                        return OrchestrationOperationResult.Success;
                    case OrchestrationOperationCode.BlackboardSet:
                    {
                        OrchestrationBlackboardSetPayload set = m_Program.Payload<OrchestrationBlackboardSetPayload>(operationIndex);
                        WriteBlackboard(operationIndex, set.Slot,
                            in InputRow(depth)[0], set.Assignment);
                        return OrchestrationOperationResult.Success;
                    }
                    case OrchestrationOperationCode.Timeline:
                        return TickTimeline(operationIndex, depth, count);
                    case OrchestrationOperationCode.TimelineClipExitRequest:
                        return default(TNumerics).Boolean(in InputRow(depth)[0])
                            ? RequestTimelineClipExit(operationIndex) : OrchestrationOperationResult.Success;
                    case OrchestrationOperationCode.Extension:
                    {
                        ushort binding = m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex;
                        Span<TValue> outputs = operation.OutputSlotOffset >= 0
                            ? SavedOutputs(in operation)
                            : BorrowOutputs(operationIndex, depth, operation.OutputCount);
                        var invocation = new OrchestrationExtensionArguments<TValue>(
                            OrchestrationExtensionInvocationKind.Execute, operationIndex, ReadGeneration(operationIndex),
                            m_Input.Tick, CurrentStopContextStorage(), StateAncestors(operationIndex),
                            ReadExecutionFrame(operationIndex),
                            InputRow(depth).Slice(0, count), outputs, in m_TimelineInvocation);
                        OrchestrationOperationResult result = (OrchestrationOperationResult)InvokeCandidate(
                            binding, in invocation);
                        foreach (ref readonly OrchestrationBlackboardOutputBinding assignment in m_Program.OutputAssignments(operationIndex))
                            WriteBlackboard(operationIndex, assignment.ValueSlot,
                                in outputs[assignment.SourceOutputPortIndex]);
                        return result;
                    }
                    default:
                        Fail(OrchestrationExecutionErrorCode.OperationNotExecutable, operationIndex,
                            detail: (int)operation.Code);
                        return OrchestrationOperationResult.Failure;
                }
            }
            finally
            {
                ReleaseInputs();
            }
        }

        OrchestrationOperationResult EvaluateControlValue(int operationIndex)
        {
            return EvaluateBoolean(operationIndex) ? OrchestrationOperationResult.Success : OrchestrationOperationResult.Failure;
        }

        void RequireSynchronousValueResult(int operationIndex, int bindingIndex,
            OrchestrationExtensionResult result)
        {
            if (result != OrchestrationExtensionResult.Success)
                Fail(OrchestrationExecutionErrorCode.ReadOnlyExtension, operationIndex, bindingIndex, (int)result);
        }
    }
}
