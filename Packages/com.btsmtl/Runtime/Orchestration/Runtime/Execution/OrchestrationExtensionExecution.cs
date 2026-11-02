using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TNumerics, THost>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
        where THost : struct, IOrchestrationHost<TValue>
    {
        void InvokeHooks(int operationIndex, OrchestrationLifecycleHook phase,
            int stateOperationIndex = -1, int exitingStateOperationIndex = -1)
        {
            foreach (ref readonly OrchestrationHookBinding hook in m_Program.Hooks(operationIndex))
            {
                if (hook.Hook != phase)
                    continue;
                int pathState = phase == OrchestrationLifecycleHook.StateEntered ? stateOperationIndex
                    : phase == OrchestrationLifecycleHook.StateExited || phase == OrchestrationLifecycleHook.StateTransition
                        ? exitingStateOperationIndex : -1;
                var invocation = new OrchestrationExtensionArguments<TValue>(
                    OrchestrationExtensionInvocationKind.Execute, operationIndex, ReadGeneration(operationIndex),
                    m_Input.Tick, CurrentStopContextStorage(), StateAncestors(operationIndex, pathState, phase == OrchestrationLifecycleHook.StateEntered),
                    ReadExecutionFrame(operationIndex),
                    Span<TValue>.Empty, Span<TValue>.Empty, in m_TimelineInvocation);
                m_CurrentOperationIndex = operationIndex;
                OrchestrationExtensionResult result = InvokeCandidate(ref m_Host, ref m_Scratch.Header.Error,
                    hook.ExtensionBindingIndex, in invocation);
                if (result != OrchestrationExtensionResult.Success)
                    Fail(OrchestrationExecutionErrorCode.HookFailed, operationIndex, hook.ExtensionBindingIndex,
                        (int)phase, (int)result);
            }
        }

        static OrchestrationExtensionResult InvokeCandidate(ref THost host, ref OrchestrationExecutionError error,
            int bindingIndex,
            in OrchestrationExtensionArguments<TValue> invocation)
        {
            OrchestrationExtensionResult result = host.InvokeCandidate(bindingIndex, in invocation);
            if (result == OrchestrationExtensionResult.Running &&
                host.ExecutionContract(bindingIndex).Completion == OrchestrationExtensionCompletion.Immediate)
            {
                error = new OrchestrationExecutionError
                {
                    Code = OrchestrationExecutionErrorCode.ImmediateExtensionWaiting,
                    OperationIndex = invocation.OperationIndex,
                    Slot = bindingIndex
                };
                OrchestrationExecutionErrorText.Throw(OrchestrationExecutionErrorCode.ImmediateExtensionWaiting);
            }
            return result;
        }

        bool TryRequestLeafStop(int operationIndex,
            out OrchestrationStopStatus status)
        {
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            if (operation.Code == OrchestrationOperationCode.Extension &&
                (m_Host.ExecutionContract(m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex).Invocations &
                    OrchestrationExtensionInvocationKind.RequestStop) != 0)
            {
                status = InvokeExtensionStop(operationIndex, OrchestrationExtensionInvocationKind.RequestStop);
                return true;
            }
            status = default;
            return false;
        }

        OrchestrationStopStatus ContinueLeafStop(int operationIndex)
        {
            if (m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.Timeline)
                return StopTimeline(operationIndex);
            return InvokeExtensionStop(operationIndex, OrchestrationExtensionInvocationKind.ContinueStop);
        }

        void ForceStopLeaf(int operationIndex, in OrchestrationStopContext context)
        {
            WriteStopContext(operationIndex, in context);
            if (m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.Timeline)
            {
                OrchestrationStopStatus status = StopTimeline(operationIndex);
                if (status != OrchestrationStopStatus.Completed)
                    Fail(OrchestrationExecutionErrorCode.TimelineForceStop, operationIndex, detail: (int)status);
                return;
            }
            OrchestrationStopStatus result = InvokeExtensionStop(operationIndex,
                OrchestrationExtensionInvocationKind.ForceStop);
            if (result != OrchestrationStopStatus.Completed)
                Fail(OrchestrationExecutionErrorCode.ActionForceStop, operationIndex, detail: (int)result);
        }

        OrchestrationStopStatus InvokeExtensionStop(int operationIndex,
            OrchestrationExtensionInvocationKind kind)
        {
            m_CurrentOperationIndex = operationIndex;
            ref readonly OrchestrationOperation operation = ref m_Program.Operation(operationIndex);
            Span<TValue> outputs;
            if (operation.OutputSlotOffset >= 0)
                outputs = SavedOutputs(in operation);
            else
            {
                RequireRowDepth(operationIndex, m_InputDepth + 1);
                outputs = BorrowOutputs(operationIndex, m_InputDepth, operation.OutputCount);
            }
            var invocation = new OrchestrationExtensionArguments<TValue>(kind,
                operationIndex, ReadGeneration(operationIndex), m_Input.Tick, StopContextStorage(operationIndex),
                StateAncestors(operationIndex), ReadExecutionFrame(operationIndex),
                ReadOnlySpan<TValue>.Empty, outputs, in m_TimelineInvocation);
            OrchestrationExtensionResult result = InvokeCandidate(ref m_Host, ref m_Scratch.Header.Error,
                m_Program.Payload<OrchestrationExtensionPayload>(operationIndex).BindingIndex, in invocation);
            switch (result)
            {
                case OrchestrationExtensionResult.Running: return OrchestrationStopStatus.Running;
                case OrchestrationExtensionResult.Success: return OrchestrationStopStatus.Completed;
                case OrchestrationExtensionResult.Failure: return OrchestrationStopStatus.Failed;
                default:
                    Fail(OrchestrationExecutionErrorCode.ActionStopResult, operationIndex, detail: (int)result);
                    return OrchestrationStopStatus.Failed;
            }
        }

        public ReadOnlySpan<OrchestrationStateAncestor> StateAncestors(int operationIndex,
            int stateOperationIndex = -1, bool entering = false)
        {
            if (stateOperationIndex < 0)
                stateOperationIndex = m_Program.Operation(operationIndex).Code == OrchestrationOperationCode.State
                    ? operationIndex : m_StateDepth > 0 ? m_States[m_StateDepth - 1].OperationIndex
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
                m_StateAncestors[--index] = new OrchestrationStateAncestor(state, generation);
            }
            return m_StateAncestors.Slice(0, count);
        }
    }
}
