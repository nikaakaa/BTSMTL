using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage>
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
        where TStorage : struct, IOrchestrationValueStorage<TValue>
    {
        public void Evaluate(int operationIndex, ref TValue value, int outputPortIndex = -1)
        {
            m_CurrentOperationIndex = operationIndex;
            int slot = m_Program.ValueEvaluationSlot(operationIndex, outputPortIndex);
            ref readonly OrchestrationValueScheduleEntry entry = ref m_ValueEntries[
                ScheduleStateContext() * m_Program.ValueSchedule.ValueSlotCount + slot];
            RunValueSchedule(in entry);
            value = m_Workspace.ScheduledValues[slot];
        }

        public bool EvaluateBoolean(int operationIndex)
        {
            OrchestrationValueSchedule schedule = m_Program.ValueSchedule;
            ref readonly OrchestrationValueScheduleEntry entry = ref m_BooleanEntries[
                ScheduleStateContext() * m_Program.OperationCount + operationIndex];
            RunValueSchedule(in entry);
            return default(TValues).Boolean(in m_Workspace.ScheduledValues[schedule.BooleanSlot]);
        }

        void EvaluateCoreValue(int operationIndex, int valueOperation, ReadOnlySpan<int> arguments,
            int depth, ref TValue value)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationValueOperation typed = ref m_ValueOperations[valueOperation];
            TValues values = default;
            TMath math = default;
            switch (typed.Code)
            {
                case OrchestrationValueOpcode.Copy:
                    value = m_Workspace.ScheduledValues[arguments[typed.Argument]];
                    return;
                case OrchestrationValueOpcode.Constant:
                    value = m_Program.Constant(typed.Argument);
                    return;
                case OrchestrationValueOpcode.HostInput:
                    value = m_Input.HostInputs[typed.Argument];
                    return;
                case OrchestrationValueOpcode.InputVector2Magnitude:
                    value = values.FromScalar(math.Magnitude2(values.Vector2(in m_Input.HostInputs[typed.Argument])));
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
                    value = values.FromBoolean(typed.Argument == CurrentStateExitCause());
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
                    m_Workspace.RequireDepth("Inputs/Outputs", operationIndex, depth + 1, m_Workspace.Inputs.Length);
                    int count = CopyScheduledArguments(arguments, depth, operationIndex);
                    ReadOnlySpan<TValue> inputs = m_Workspace.Inputs[depth].AsSpan(0, count);
                    Span<TValue> outputs = m_Workspace.BorrowOutputs(operationIndex, depth, operation.OutputCount);
                    var invocation = new OrchestrationExtensionArguments<TValue>(
                        OrchestrationExtensionInvocationKind.Evaluate, operationIndex, ReadGeneration(operationIndex),
                        m_Input.Tick, 0, StateAncestors(operationIndex), ReadExecutionFrame(operationIndex),
                        inputs, outputs, in m_TimelineInvocation);
                    RequireSynchronousValueResult(operationIndex, binding,
                        m_Bindings.InvokeReadOnly(binding, in m_Context, in invocation));
                    value = outputs[typed.Argument];
                    return;
                }
                default:
                    OrchestrationTypedValueMath<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion,
                        TMath, TValues>.Compute(typed.Code, m_Workspace.ScheduledValues, arguments,
                            in m_Identities, ref value);
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
            m_Workspace.RequireDepth("Inputs/Outputs", operationIndex, depth + 1, m_Workspace.Inputs.Length);
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

        internal int FindDiagnosticOverride(int operationIndex, int slot, OrchestrationValueOverrideKind kind,
            in OrchestrationBlackboardOwnerToken blackboardOwner)
        {
            if (kind == OrchestrationValueOverrideKind.Blackboard)
            {
                OrchestrationValueOverrideAddress address = BlackboardOverrideAddress(slot, in blackboardOwner);
                return ReadBlackboardOverrideIndex(in address);
            }
            Span<OrchestrationValueOverride<TValue>> overrides = m_Input.Overrides;
            for (int index = 0; index < overrides.Length; index++)
            {
                ref OrchestrationValueOverride<TValue> supplied = ref overrides[index];
                if (supplied.Mode == OrchestrationValueOverrideMode.None || supplied.Address.Kind != kind ||
                    supplied.Address.Slot != slot || supplied.Status == OrchestrationValueOverrideStatus.OwnerRetired)
                    continue;
                OrchestrationExecutionOwnerToken owner = ReadExecutionOwner(operationIndex);
                if (!supplied.Address.Owner.Equals(in owner))
                    continue;
                if (!IsPredictiveEvaluation)
                    supplied.Status = OrchestrationValueOverrideStatus.Consumed;
                return index;
            }
            return -1;
        }

        int ReadBlackboardOverrideIndex(in OrchestrationValueOverrideAddress address)
        {
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
            default(TDiagnostics).Observe(ref this, operationIndex, inputSlot, true, in value);
        }

        void ObserveOutput(int operationIndex, int outputSlot, in TValue value)
        {
            m_CurrentOperationIndex = operationIndex;
            default(TDiagnostics).Observe(ref this, operationIndex, outputSlot, false, in value);
        }

        internal void ObserveDiagnosticPort(int operationIndex, int slot, bool input, in TValue value)
        {
            if (!m_Input.CaptureValues && m_Input.Observations == null || IsPredictiveEvaluation)
                return;
            if (m_Input.CaptureValues)
            {
                (input ? m_Workspace.InputObservations : m_Workspace.OutputObservations)[slot] = value;
                (input ? m_Workspace.InputObservationWritten : m_Workspace.OutputObservationWritten)[slot] = true;
            }
            if (m_Input.Observations != null)
            {
                OrchestrationValueOverrideAddress address = ReadPortOverrideAddress(operationIndex, slot, input);
                m_Input.Observations.Observe(in address, in value);
            }
        }

        void PrepareGraphCall(int operationIndex)
        {
            OrchestrationGraphCall call = m_Program.GraphCall(m_Program.Payload<OrchestrationGraphCallPayload>(operationIndex).CallIndex);
            if (IsActive(call.EntryOperationIndex))
                return;
            int depth = ReadInputs(operationIndex, out int count);
            try
            {
                foreach (ref readonly OrchestrationGraphParameter output in call.Outputs.Span)
                {
                    m_Blackboards.WriteValue(output.ValueSlot, in m_Program.DefaultValue(output.ValueSlot));
                    ref OrchestrationBlackboardMetadata metadata = ref m_Blackboards.WriteMetadata(output.ValueSlot);
                    metadata.Revision = checked(metadata.Revision + 1);
                }
                ReadOnlySpan<OrchestrationGraphParameter> parameters = call.Inputs.Span;
                for (int i = 0; i < parameters.Length; i++)
                {
                    m_Blackboards.WriteValue(parameters[i].ValueSlot, in m_Workspace.Inputs[depth][i]);
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
            OrchestrationGraphCall call = m_Program.GraphCall(m_Program.Payload<OrchestrationGraphCallPayload>(operationIndex).CallIndex);
            int slot = call.Outputs.Span[outputPortIndex].ValueSlot;
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
                    ushort binding = m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex;
                    if (m_Bindings.Contract(binding).Kind != OrchestrationExtensionKind.Action)
                        return EvaluateControlValue(operationIndex);
                    if (!m_Bindings.ShouldExecute(binding, in m_Context))
                        return OrchestrationOperationResult.Success;
                    break;
            }
            int depth = ReadInputs(operationIndex, out int count);
            try
            {
                m_CurrentOperationIndex = operationIndex;
                switch (operation.Code)
                {
                    case OrchestrationOperationCode.EmitOutput:
                        EmitOutput(operationIndex, m_Workspace.Inputs[depth].AsSpan(0, count));
                        return OrchestrationOperationResult.Success;
                    case OrchestrationOperationCode.BlackboardSet:
                    {
                        OrchestrationBlackboardSetPayload set = m_Program.Payload<OrchestrationBlackboardSetPayload>(operationIndex);
                        WriteBlackboard(operationIndex, set.Slot,
                            in m_Workspace.Inputs[depth][0], set.Assignment);
                        return OrchestrationOperationResult.Success;
                    }
                    case OrchestrationOperationCode.Timeline:
                        return TickTimeline(operationIndex, depth, count);
                    case OrchestrationOperationCode.TimelineClipExitRequest:
                        return default(TValues).Boolean(in m_Workspace.Inputs[depth][0])
                            ? RequestTimelineClipExit(operationIndex) : OrchestrationOperationResult.Success;
                    case OrchestrationOperationCode.Extension:
                    {
                        ushort binding = m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex;
                        Span<TValue> outputs = operation.OutputSlotOffset >= 0
                            ? SavedOutputs(in operation)
                            : m_Workspace.BorrowOutputs(operationIndex, depth, operation.OutputCount);
                        var invocation = new OrchestrationExtensionArguments<TValue>(
                            OrchestrationExtensionInvocationKind.Execute, operationIndex, ReadGeneration(operationIndex),
                            m_Input.Tick, 0, StateAncestors(operationIndex),
                            ReadExecutionFrame(operationIndex),
                            m_Workspace.Inputs[depth].AsSpan(0, count), outputs, in m_TimelineInvocation);
                        OrchestrationOperationResult result = (OrchestrationOperationResult)m_Bindings.InvokeCandidate(
                            binding, ref m_Context, in invocation);
                        foreach (ref readonly OrchestrationBlackboardOutputBinding assignment in m_Program.OutputAssignments(operationIndex))
                            WriteBlackboard(operationIndex, assignment.ValueSlot,
                                in outputs[assignment.SourceOutputPortIndex]);
                        return result;
                    }
                    default:
                        throw new InvalidOperationException($"Operation '{operationIndex}' ({operation.Code}) is not executable.");
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

        static void RequireSynchronousValueResult(int operationIndex, int bindingIndex,
            OrchestrationExtensionResult result)
        {
            if (result != OrchestrationExtensionResult.Success)
                throw new InvalidOperationException($"Read-only extension '{bindingIndex}' at operation '{operationIndex}' returned '{result}'.");
        }
    }
}
