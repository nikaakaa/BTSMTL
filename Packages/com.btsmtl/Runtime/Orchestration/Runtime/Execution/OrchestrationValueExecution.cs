using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
        where TContext : struct
    {
        TValue EvaluateValue(int operationIndex, int outputPortIndex)
        {
            m_CurrentOperationIndex = operationIndex;
            int slot = m_Program.ValueEvaluationSlot(operationIndex, outputPortIndex);
            if (m_Input.UseOverrides && default(TValues).Kind(in m_Input.OutputOverrides[slot]) != 0)
            {
                TValue value = m_Input.OutputOverrides[slot];
                ObserveOutput(operationIndex, slot, in value);
                return value;
            }
            if (m_Workspace.ActiveValues[slot])
                throw new InvalidOperationException($"Value operation cycle reached '{operationIndex}/{outputPortIndex}'.");
            if (m_ObservationPhase == OrchestrationObservationPhase.Observing && m_Workspace.CachedObservationWritten[slot])
                return m_Workspace.OutputObservations[slot];
            m_Workspace.ActiveValues[slot] = true;
            try
            {
                ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
                TValue result;
                if (operation.Code == OrchestrationOperationCode.Extension &&
                    m_Bindings.Contract(operation.ExtensionBindingIndex).Kind == OrchestrationExtensionKind.Action)
                {
                    result = ReadValueSlot(operationIndex,
                        operation.OutputSlotOffset + (outputPortIndex < 0 ? 0 : outputPortIndex));
                }
                else
                {
                    int depth = ReadInputs(operationIndex, out int count);
                    try
                    {
                        result = EvaluateCoreValue(operationIndex, outputPortIndex, depth, count);
                    }
                    finally
                    {
                        ReleaseInputs();
                    }
                }
                ObserveOutput(operationIndex, slot, in result);
                if (m_ObservationPhase != OrchestrationObservationPhase.None)
                {
                    m_Workspace.OutputObservations[slot] = result;
                    m_Workspace.CachedObservationWritten[slot] = true;
                }
                return result;
            }
            finally
            {
                m_Workspace.ActiveValues[slot] = false;
            }
        }

        void ClearObservedValues() =>
            Array.Clear(m_Workspace.CachedObservationWritten, 0, m_Workspace.CachedObservationWritten.Length);

        TValue EvaluateCoreValue(int operationIndex, int outputPortIndex, int depth, int count)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            ReadOnlySpan<TValue> inputs = m_Workspace.Inputs[depth].AsSpan(0, count);
            TValues values = default;
            TMath math = default;
            switch (operation.Code)
            {
                case OrchestrationOperationCode.ConditionResult:
                    return values.FromBoolean(count > 0 && ToBoolean(in inputs[0]));
                case OrchestrationOperationCode.And:
                    return values.FromBoolean(count >= 2 && ToBoolean(in inputs[0]) && ToBoolean(in inputs[1]));
                case OrchestrationOperationCode.Or:
                    return values.FromBoolean(count > 0 && (ToBoolean(in inputs[0]) || count > 1 && ToBoolean(in inputs[1])));
                case OrchestrationOperationCode.Not:
                    return values.FromBoolean(count == 0 || !ToBoolean(in inputs[0]));
                case OrchestrationOperationCode.Compare:
                    return values.FromBoolean(count >= 2 && OrchestrationValueMath<TValue, TScalar, TVector2,
                        TVector3, TQuaternion, TMath, TValues>.Compare(operation.Comparison, inputs));
                case OrchestrationOperationCode.Calculate:
                    return OrchestrationValueMath<TValue, TScalar, TVector2, TVector3, TQuaternion,
                        TMath, TValues>.Calculate(operation.Calculation, inputs, in m_Identities);
                case OrchestrationOperationCode.IdentityValue:
                    return inputs[0];
                case OrchestrationOperationCode.SelectValue:
                    return inputs[1];
                case OrchestrationOperationCode.Constant:
                    return operation.ConstantIndex < 0 ? values.FromBoolean(false) : m_Program.Constant(operation.ConstantIndex);
                case OrchestrationOperationCode.InputBoolean:
                case OrchestrationOperationCode.InputScalar:
                case OrchestrationOperationCode.InputVector2:
                case OrchestrationOperationCode.HostInput:
                    return m_Input.HostInputs[operation.InputSlot + (outputPortIndex < 0 ? 0 : outputPortIndex)];
                case OrchestrationOperationCode.InputVector2Magnitude:
                    return values.FromScalar(math.Magnitude2(values.Vector2(in m_Input.HostInputs[operation.InputSlot])));
                case OrchestrationOperationCode.InputRequest:
                    return values.FromBoolean(HasRequest(operation.RequestIdentityId));
                case OrchestrationOperationCode.BlackboardGet:
                    return ReadBlackboard(operationIndex, operation.ValueSlot);
                case OrchestrationOperationCode.SubGraph:
                    return ReadGraphCallOutput(operationIndex, outputPortIndex);
                case OrchestrationOperationCode.StateRootCompleted:
                    return values.FromBoolean(CurrentStateRootCompleted());
                case OrchestrationOperationCode.StateExitCause:
                    return values.FromBoolean(operation.Integer0 == CurrentStateExitCause());
                case OrchestrationOperationCode.Cycle:
                    return values.FromInt32((operation.Integer0 + ReadOperationState(operationIndex).ChildCursor) % operation.Integer1);
                case OrchestrationOperationCode.TimelineTime:
                    return ReadTimelineTime();
                case OrchestrationOperationCode.Extension:
                {
                    Span<TValue> outputs = m_Workspace.Outputs[depth].AsSpan(0, operation.OutputCount);
                    var invocation = new OrchestrationExtensionArguments<TValue>(
                        OrchestrationExtensionInvocationKind.Evaluate, operationIndex, ReadGeneration(operationIndex),
                        m_Input.Tick, 0, StateAncestors(operationIndex), ReadExecutionFrame(operationIndex),
                        inputs, outputs, in m_TimelineInvocation);
                    RequireSynchronousValueResult(operationIndex, operation.ExtensionBindingIndex,
                        m_Bindings.InvokeReadOnly(operation.ExtensionBindingIndex, in m_Context, in invocation));
                    return outputs[outputPortIndex < 0 ? 0 : outputPortIndex];
                }
                default:
                    throw new InvalidOperationException($"Operation '{operationIndex}' ({operation.Code}) does not provide a value.");
            }
        }

        int ReadInputs(int operationIndex, out int count)
        {
            int depth = m_InputDepth++;
            TValue[] buffer = m_Workspace.Inputs[depth];
            ReadOnlySpan<OrchestrationValueInputBinding> inputs = m_Program.Inputs(operationIndex);
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            count = 0;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (operation.Code == OrchestrationOperationCode.SelectValue && i > 0)
                    i = ToBoolean(in buffer[0]) ? 1 : 2;
                ref readonly OrchestrationValueInputBinding input = ref inputs[i];
                int inputSlot = operation.Inputs.Offset + i;
                TValue value = ReadInput(operationIndex, inputSlot, in input);
                buffer[count++] = value;
                ObserveInput(operationIndex, inputSlot, in value);
                if (operation.Code == OrchestrationOperationCode.SelectValue && i > 0)
                    break;
                if (i == 0 && (operation.Code == OrchestrationOperationCode.And && !ToBoolean(in value) ||
                    operation.Code == OrchestrationOperationCode.Or && ToBoolean(in value)))
                    break;
            }
            return depth;
        }

        TValue ReadInput(int operationIndex, int inputSlot, in OrchestrationValueInputBinding input)
        {
            if (m_Input.UseOverrides && default(TValues).Kind(in m_Input.InputOverrides[inputSlot]) != 0)
                return m_Input.InputOverrides[inputSlot];
            TValue value;
            switch (input.Source)
            {
                case OrchestrationValueInputSource.Operation:
                    value = Evaluate(input.SourceIndex, input.SourceOutputPortIndex);
                    break;
                case OrchestrationValueInputSource.Constant:
                    value = m_Program.Constant(input.SourceIndex);
                    break;
                case OrchestrationValueInputSource.StateCompletionTransition:
                    int transition = PredictCurrentStateRootCompletionTransition();
                    value = default(TValues).FromInt32(transition < 0 ? -1 : m_Program.Edge(transition).TargetOperationIndex);
                    break;
                case OrchestrationValueInputSource.Blackboard:
                    value = ReadBlackboard(operationIndex, input.SourceIndex);
                    break;
                default:
                    throw new InvalidOperationException($"Operation '{operationIndex}' has an unsupported input source.");
            }
            return OrchestrationValueMath<TValue, TScalar, TVector2, TVector3,
                TQuaternion, TMath, TValues>.Convert(in value, input.ValueKind);
        }

        void ReleaseInputs() => m_InputDepth--;

        bool ToBoolean(in TValue value) => OrchestrationValueMath<TValue, TScalar,
            TVector2, TVector3, TQuaternion, TMath, TValues>.ToBoolean(in value, in m_Identities);

        bool HasRequest(int identityId)
        {
            foreach (int request in m_Input.RequestIdentityIds)
                if (request == identityId)
                    return true;
            return false;
        }

        TValue ReadValueSlot(int ownerOperationIndex, int valueSlot) =>
            ReadValueSlot(valueSlot, in m_Blackboards.ReadOutputValue(ownerOperationIndex, valueSlot));

        TValue ReadValueSlot(int valueSlot, in TValue value) => m_Input.UseOverrides &&
            default(TValues).Kind(in m_Input.ValueOverrides[valueSlot]) != 0
                ? m_Input.ValueOverrides[valueSlot] : value;

        void ObserveInput(int operationIndex, int inputSlot, in TValue value)
        {
            m_CurrentOperationIndex = operationIndex;
            if (!m_Input.CaptureValues || IsPredictiveEvaluation)
                return;
            m_Workspace.InputObservations[inputSlot] = value;
            m_Workspace.InputObservationWritten[inputSlot] = true;
        }

        void ObserveOutput(int operationIndex, int outputSlot, in TValue value)
        {
            m_CurrentOperationIndex = operationIndex;
            if (!m_Input.CaptureValues || IsPredictiveEvaluation)
                return;
            m_Workspace.OutputObservations[outputSlot] = value;
            m_Workspace.OutputObservationWritten[outputSlot] = true;
        }

        void PrepareGraphCall(int operationIndex)
        {
            OrchestrationGraphCall call = m_Program.GraphCall(m_Program.Operation(operationIndex).GraphCallIndex);
            if (IsActive(call.EntryOperationIndex))
                return;
            int depth = ReadInputs(operationIndex, out int count);
            try
            {
                foreach (ref readonly OrchestrationGraphParameter output in call.Outputs.Span)
                    m_Values[output.ValueSlot] = m_Program.DefaultValue(output.ValueSlot);
                ReadOnlySpan<OrchestrationGraphParameter> parameters = call.Inputs.Span;
                for (int i = 0; i < parameters.Length; i++)
                    m_Values[parameters[i].ValueSlot] = m_Workspace.Inputs[depth][i];
            }
            finally
            {
                ReleaseInputs();
            }
        }

        TValue ReadGraphCallOutput(int operationIndex, int outputPortIndex)
        {
            OrchestrationGraphCall call = m_Program.GraphCall(m_Program.Operation(operationIndex).GraphCallIndex);
            ReadOnlySpan<OrchestrationGraphParameter> outputs = call.Outputs.Span;
            for (int i = 0; i < outputs.Length; i++)
            {
                if (outputs[i].PortIndex != outputPortIndex)
                    continue;
                OrchestrationOperationStatus status = m_Program.Operation(operationIndex).IsRunnable
                    ? ReadStatus(call.EntryOperationIndex) : ReadOperationState(operationIndex).Status;
                return status == OrchestrationOperationStatus.Success
                    ? ReadValueSlot(operationIndex, outputs[i].ValueSlot) : m_Program.DefaultValue(outputs[i].ValueSlot);
            }
            throw new InvalidOperationException($"Graph call '{operationIndex}' does not declare output '{outputPortIndex}'.");
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
                    if (m_Bindings.Contract(operation.ExtensionBindingIndex).Kind != OrchestrationExtensionKind.Action)
                        return EvaluateControlValue(operationIndex);
                    if (!m_Bindings.ShouldExecute(operation.ExtensionBindingIndex, in m_Context))
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
                        WriteBlackboard(operationIndex, operation.ValueSlot,
                            in m_Workspace.Inputs[depth][0], operation.Assignment);
                        return OrchestrationOperationResult.Success;
                    }
                    case OrchestrationOperationCode.Timeline:
                        return TickTimeline(operationIndex, depth, count);
                    case OrchestrationOperationCode.TimelineClipExitRequest:
                        return default(TValues).Boolean(in m_Workspace.Inputs[depth][0])
                            ? RequestTimelineClipExit(operationIndex) : OrchestrationOperationResult.Success;
                    case OrchestrationOperationCode.Extension:
                    {
                        Span<TValue> outputs = operation.OutputSlotOffset >= 0
                            ? m_Values.Slice(operation.OutputSlotOffset, operation.OutputCount)
                            : m_Workspace.Outputs[depth].AsSpan(0, operation.OutputCount);
                        var invocation = new OrchestrationExtensionArguments<TValue>(
                            OrchestrationExtensionInvocationKind.Execute, operationIndex, ReadGeneration(operationIndex),
                            m_Input.Tick, 0, StateAncestors(operationIndex),
                            ReadExecutionFrame(operationIndex),
                            m_Workspace.Inputs[depth].AsSpan(0, count), outputs, in m_TimelineInvocation);
                        OrchestrationOperationResult result = (OrchestrationOperationResult)m_Bindings.InvokeCandidate(
                            operation.ExtensionBindingIndex, ref m_Context, in invocation);
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
            TValue value = Evaluate(operationIndex);
            return ToBoolean(in value) ? OrchestrationOperationResult.Success : OrchestrationOperationResult.Failure;
        }

        static void RequireSynchronousValueResult(int operationIndex, int bindingIndex,
            OrchestrationExtensionResult result)
        {
            if (result != OrchestrationExtensionResult.Success)
                throw new InvalidOperationException($"Read-only extension '{bindingIndex}' at operation '{operationIndex}' returned '{result}'.");
        }
    }
}
