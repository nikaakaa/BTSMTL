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
        void InvokeHooks(int operationIndex, OrchestrationLifecycleHook phase,
            int machineOperationIndex = -1, int stateOperationIndex = -1,
            int exitingStateOperationIndex = -1, int targetStateOperationIndex = -1)
        {
            foreach (ref readonly OrchestrationHookBinding hook in m_Program.Hooks(operationIndex))
            {
                if (hook.Hook != phase)
                    continue;
                int depth = m_InputDepth++;
                ReadOnlySpan<OrchestrationHookInput> inputs = m_Program.HookInputs(in hook);
                try
                {
                    for (int i = 0; i < inputs.Length; i++)
                        m_Workspace.Inputs[depth][i] = HookValue(in inputs[i], operationIndex, phase,
                            machineOperationIndex, stateOperationIndex, exitingStateOperationIndex, targetStateOperationIndex);
                    int pathState = phase == OrchestrationLifecycleHook.StateEntered ? stateOperationIndex
                        : phase == OrchestrationLifecycleHook.StateExited || phase == OrchestrationLifecycleHook.StateTransition
                            ? exitingStateOperationIndex : -1;
                    var invocation = new OrchestrationExtensionInvocation<TValue>(
                        OrchestrationExtensionInvocationKind.Execute, operationIndex, ReadGeneration(operationIndex),
                        m_Input.Tick, 0, StateAncestors(operationIndex, pathState, phase == OrchestrationLifecycleHook.StateEntered),
                        m_Workspace.Inputs[depth].AsSpan(0, inputs.Length), Span<TValue>.Empty);
                    m_CurrentOperationIndex = operationIndex;
                    OrchestrationExtensionResult result = m_Bindings.InvokeCandidate(
                        hook.ExtensionBindingIndex, ref m_Context, in invocation);
                    if (result != OrchestrationExtensionResult.Success)
                        throw new InvalidOperationException($"Lifecycle hook '{phase}' at operation '{operationIndex}' returned '{result}'.");
                }
                finally
                {
                    ReleaseInputs();
                }
            }
        }

        TValue HookValue(in OrchestrationHookInput input, int operationIndex, OrchestrationLifecycleHook phase,
            int machineOperationIndex, int stateOperationIndex, int exitingStateOperationIndex, int targetStateOperationIndex)
        {
            TValues values = default;
            switch (input.Source)
            {
                case OrchestrationHookInputSource.Constant:
                    return m_Program.Constant(input.ConstantIndex);
                case OrchestrationHookInputSource.OperationIndex:
                    return values.FromInt32(operationIndex);
                case OrchestrationHookInputSource.OperationGeneration:
                    return values.FromUInt64(ReadGeneration(operationIndex));
                case OrchestrationHookInputSource.MachineOperationIndex:
                    return values.FromInt32(machineOperationIndex);
                case OrchestrationHookInputSource.StateOperationIndex:
                    return values.FromInt32(stateOperationIndex);
                case OrchestrationHookInputSource.ExitingStateOperationIndex:
                    return values.FromInt32(exitingStateOperationIndex);
                case OrchestrationHookInputSource.TargetStateOperationIndex:
                    return values.FromInt32(targetStateOperationIndex);
                case OrchestrationHookInputSource.Tick:
                    return values.FromUInt64(m_Input.Tick);
                case OrchestrationHookInputSource.StateGeneration:
                    return values.FromUInt64(phase == OrchestrationLifecycleHook.StateEntered
                        ? checked(ReadGeneration(stateOperationIndex) + 1) : ReadGeneration(stateOperationIndex));
                case OrchestrationHookInputSource.ExitingStateGeneration:
                    return values.FromUInt64(ReadGeneration(exitingStateOperationIndex));
                case OrchestrationHookInputSource.TargetStateGeneration:
                    return values.FromUInt64(checked(ReadGeneration(targetStateOperationIndex) + 1));
                default:
                    throw new InvalidOperationException($"Lifecycle hook at operation '{operationIndex}' has unsupported input '{input.Source}'.");
            }
        }

        bool TryRequestLeafStop(int operationIndex, OrchestrationStopContext context,
            out OrchestrationStopStatus status)
        {
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            if (operation.Code == OrchestrationOperationCode.Extension &&
                (m_Bindings.Contract(operation.ExtensionBindingIndex).Invocations &
                    OrchestrationExtensionInvocationKind.RequestStop) != 0)
            {
                status = InvokeExtensionStop(operationIndex, context, OrchestrationExtensionInvocationKind.RequestStop);
                return true;
            }
            status = default;
            return false;
        }

        OrchestrationStopStatus ContinueLeafStop(int operationIndex, OrchestrationStopContext context)
        {
            if (m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.Timeline)
                return StopTimeline(operationIndex, context, false);
            return InvokeExtensionStop(operationIndex, context, OrchestrationExtensionInvocationKind.ContinueStop);
        }

        void ForceStopLeaf(int operationIndex, OrchestrationStopContext context)
        {
            if (m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.Timeline)
            {
                OrchestrationStopStatus status = StopTimeline(operationIndex, context, true);
                if (status != OrchestrationStopStatus.Completed)
                    throw new InvalidOperationException($"Timeline operation '{operationIndex}' force stop returned '{status}'.");
                return;
            }
            OrchestrationStopStatus result = InvokeExtensionStop(operationIndex, context,
                OrchestrationExtensionInvocationKind.ForceStop);
            if (result != OrchestrationStopStatus.Completed)
                throw new InvalidOperationException($"Action operation '{operationIndex}' force stop returned '{result}'.");
        }

        OrchestrationStopStatus InvokeExtensionStop(int operationIndex, OrchestrationStopContext context,
            OrchestrationExtensionInvocationKind kind)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            Span<TValue> outputs = operation.OutputSlotOffset >= 0
                ? m_Values.Slice(operation.OutputSlotOffset, operation.OutputCount)
                : m_Workspace.Outputs[m_InputDepth].AsSpan(0, operation.OutputCount);
            var invocation = new OrchestrationExtensionInvocation<TValue>(kind,
                operationIndex, ReadGeneration(operationIndex), m_Input.Tick, context.Cause,
                StateAncestors(operationIndex), ReadOnlySpan<TValue>.Empty, outputs);
            OrchestrationExtensionResult result = m_Bindings.InvokeCandidate(operation.ExtensionBindingIndex,
                ref m_Context, in invocation);
            return result switch
            {
                OrchestrationExtensionResult.Running => OrchestrationStopStatus.Running,
                OrchestrationExtensionResult.Success => OrchestrationStopStatus.Completed,
                OrchestrationExtensionResult.Failure => OrchestrationStopStatus.Failed,
                _ => throw new InvalidOperationException($"Action operation '{operationIndex}' returned an invalid stop result.")
            };
        }

        ReadOnlySpan<OrchestrationStateAncestor> StateAncestors(int operationIndex,
            int stateOperationIndex = -1, bool entering = false)
        {
            if (stateOperationIndex < 0)
                stateOperationIndex = m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.State
                    ? operationIndex : m_StateDepth > 0 ? m_Workspace.States[m_StateDepth - 1].OperationIndex
                    : m_Program.Operation(operationIndex).ParentStateOperationIndex;
            int count = 0;
            for (int state = stateOperationIndex; state >= 0; state = m_Program.Operation(state).ParentStateOperationIndex)
                count++;
            int index = count;
            for (int state = stateOperationIndex; state >= 0; state = m_Program.Operation(state).ParentStateOperationIndex)
            {
                ulong generation = ReadGeneration(state);
                if (entering && state == stateOperationIndex)
                    generation = checked(generation + 1);
                m_Workspace.StateAncestors[--index] = new OrchestrationStateAncestor(state, generation);
            }
            return m_Workspace.StateAncestors.AsSpan(0, count);
        }
    }
}
