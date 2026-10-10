using System;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    public sealed partial class HostEventGraphRuntime : IDisposable
    {
        HostEventGraph m_Source;
        EventGraphHostContract m_Contract;
        EventGraphCompiledBindings m_Bindings;
        AbilityStateValue[] m_State;
        Float32GraphValueRuntime m_Values;
        EventGraphVariableBuffer m_Output;
        IEventGraphHostContext m_Host;
        OperationHandle m_CurrentOperation = OperationHandle.Invalid;
        EventGraphExecutionFailure m_LastFailure;
        EventGraphInvocationIdentity m_LastInvocation;
        ulong m_ResetGeneration;
        bool m_Started;
        bool m_Faulted;
        bool m_Disposed;

        public HostEventGraphRuntime(HostEventGraph source, EventGraphHostContract contract) =>
            ReplaceSource(source, contract);

        public HostEventGraph Source => m_Source;
        public EventGraphHostContract Contract => m_Contract;
        public EventGraphVariableContract VariableContract => m_Bindings.Variables;
        public ulong ResetGeneration => m_ResetGeneration;
        public bool IsStarted => m_Started;
        public bool IsFaulted => m_Faulted;
        public EventGraphExecutionFailure LastFailure => m_LastFailure;

        public EventGraphExecutionResult Execute(EventGraphInvocationContext invocation, IEventGraphHostContext hostContext)
        {
            RequireAlive();
            if (!invocation.Identity.Equals(hostContext.Invocation.Identity) ||
                invocation.EventId != hostContext.Invocation.EventId ||
                invocation.DeltaSeconds != hostContext.Invocation.DeltaSeconds)
                return Fail(invocation, "Event graph host context does not match the invocation.");
            if (invocation.EventId != m_Contract.UpdateEventId)
                return Fail(invocation, $"Event graph event '{invocation.EventId}' is not declared by host contract '{m_Contract.ContractId}'.");
            if (m_Faulted)
                return EventGraphExecutionResult.Failed(m_LastFailure);
            if (m_LastInvocation.IsValid && (m_LastInvocation.SourceId != invocation.Identity.SourceId ||
                invocation.Identity.Sequence <= m_LastInvocation.Sequence))
                return Fail(invocation, "Event graph invocation identity is not increasing.");
            m_Host = hostContext;
            m_CurrentOperation = OperationHandle.Invalid;
            try
            {
                m_Values.BeginEvaluation();
                var context = new Context(this);
                if (!m_Started)
                {
                    if (m_Bindings.Start.IsValid)
                        OperationCompositeRuntime.ExecuteSynchronous(m_Bindings.Data.Topology, ref context, m_Bindings.Start);
                    m_Started = true;
                }
#if UNITY_EDITOR
                ExecuteTriggers(ref context);
#endif
                OperationCompositeRuntime.ExecuteSynchronous(m_Bindings.Data.Topology, ref context, m_Bindings.Update);
                m_Output.BeginWrite();
                for (int i = 0; i < m_Bindings.PublishedSlots.Length; i++)
                {
                    int slot = m_Bindings.PublishedSlots[i];
                    EventGraphValue value = EventGraphCompiledValues.FromState(ReadState(slot), m_Bindings.StateTypes[slot]);
                    m_Output.Write(i, value);
                }
                EventGraphVariableFrame frame = m_Output.Publish(VariableContract, invocation.Identity, m_ResetGeneration);
                m_LastInvocation = invocation.Identity;
#if UNITY_EDITOR
                m_PortValues.CompleteEvaluation();
#endif
                return EventGraphExecutionResult.Success(frame);
            }
            catch (Exception exception)
            {
                return Fail(invocation, exception.Message, exception);
            }
            finally
            {
                m_Host = null;
            }
        }

        public void Reset()
        {
            RequireAlive();
            ResetState();
            m_LastFailure = null;
            m_LastInvocation = default;
            m_Faulted = false;
            m_ResetGeneration = checked(m_ResetGeneration + 1);
        }

        public void ReplaceSource(HostEventGraph source, EventGraphHostContract contract)
        {
            RequireAlive();
            var bindings = new EventGraphCompiledBindings(source, contract);
            if (m_State != null)
                ReleaseState();
            m_Source = source;
            m_Contract = contract;
            m_Bindings = bindings;
            m_State = new AbilityStateValue[bindings.Data.StateSlots.Count];
            m_Output = new EventGraphVariableBuffer(bindings.PublishedSlots.Length);
#if UNITY_EDITOR
            PrepareEditor();
#endif
            m_Values = new Float32GraphValueRuntime(bindings.Data, new Float32GraphValueWorkspace(bindings.Data)
#if UNITY_EDITOR
                , m_PortValues
#endif
                );
            Reset();
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            ReleaseState();
            m_Disposed = true;
        }

        AbilityStateValue ReadState(int slot)
        {
#if UNITY_EDITOR
            if (m_VariableLocks[slot])
                return m_VariableOverrides[slot];
#endif
            return m_State[slot];
        }

        void WriteState(int slot, AbilityStateValue value)
        {
            value.Retain();
            m_State[slot].Release();
            m_State[slot] = value;
        }

        void ResetState()
        {
            m_Output.Invalidate();
            for (int i = 0; i < m_State.Length; i++)
                WriteState(i, m_Bindings.Data.DefaultStateValue(i));
#if UNITY_EDITOR
            ResetEditor();
#endif
            m_Started = false;
        }

        void ReleaseState()
        {
            m_Output.Invalidate();
            for (int i = 0; i < m_State.Length; i++)
                m_State[i].Release();
#if UNITY_EDITOR
            ResetEditor();
#endif
        }

        EventGraphExecutionResult Fail(EventGraphInvocationContext invocation, string message, Exception exception = null)
        {
            string node = m_CurrentOperation.IsValid ? m_Bindings.OperationNodes[m_CurrentOperation.Value] : string.Empty;
            m_LastFailure = new EventGraphExecutionFailure(m_Source.AuthoringId, invocation.EventId, node, message, exception);
            m_Faulted = true;
            ResetState();
            return EventGraphExecutionResult.Failed(m_LastFailure);
        }

        void RequireAlive()
        {
            if (m_Disposed)
                throw new ObjectDisposedException(nameof(HostEventGraphRuntime));
        }

        struct Context : ISynchronousOperationTarget, IFloat32GraphValueContext
        {
            readonly HostEventGraphRuntime m_Runtime;
            public Context(HostEventGraphRuntime runtime) => m_Runtime = runtime;
            public void Enter(OperationExecutionDescriptor operation, string inputPort) => RequireExecution(operation.Handle);
            public void RequireExecution(OperationHandle operation) => m_Runtime.m_CurrentOperation = operation;

            public void ExecuteLeaf(OperationExecutionDescriptor operation)
            {
                SimulationOperation definition = m_Runtime.m_Bindings.Data.Operations[operation.Handle.Value];
                if (operation.Code != SimulationOperationCode.BlackboardSet)
                    throw new InvalidOperationException($"事件流不能执行 '{operation.Code}'。");
                using Float32ValueInputLease inputs = m_Runtime.m_Values.ReadInputs(ref this, definition);
                int slot = m_Runtime.m_Bindings.OperationSlots[operation.Handle.Value];
                m_Runtime.WriteState(slot, Float32GraphCalculations.Assign(
                    m_Runtime.ReadState(slot), inputs[0], operation.Integer1));
            }

            public bool ReadCondition(OperationExecutionDescriptor operation)
            {
                using Float32ValueInputLease inputs = m_Runtime.m_Values.ReadInputs(
                    ref this, m_Runtime.m_Bindings.Data.Operations[operation.Handle.Value]);
                return inputs[0].Boolean;
            }

            public int ReadCycleCursor(OperationExecutionDescriptor operation) =>
                m_Runtime.ReadState(m_Runtime.m_Bindings.OperationSlots[operation.Handle.Value]).Int32;
            public void WriteCycleCursor(OperationExecutionDescriptor operation, int cursor) =>
                m_Runtime.WriteState(m_Runtime.m_Bindings.OperationSlots[operation.Handle.Value], AbilityStateValue.FromInt32(cursor));

            public AbilityStateValue EvaluateDomainValue(SimulationOperation operation, string outputPort,
                int outputPortIndex, Float32ValueInputLease inputs)
            {
                switch (operation.Code)
                {
                    case SimulationOperationCode.BlackboardGet:
                        return m_Runtime.ReadState(m_Runtime.m_Bindings.OperationSlots[operation.Handle.Value]);
                    case SimulationOperationCode.Cycle:
                        return AbilityStateValue.FromInt32((operation.Integer0 +
                            m_Runtime.ReadState(m_Runtime.m_Bindings.OperationSlots[operation.Handle.Value]).Int32) % operation.Integer1);
                    case SimulationOperationCode.HostInput:
                        EventGraphInputDescriptor input = m_Runtime.m_Bindings.Inputs[operation.Handle.Value];
                        if (!m_Runtime.m_Host.TryRead(input.InputId, input.ValueKind, out EventGraphValue value) ||
                            value.Kind != input.ValueKind || value.Kind == EventGraphValueKind.Enum && value.EnumType != input.ValueType)
                            throw new InvalidOperationException($"宿主没有提供 '{input.InputId}' 的声明类型值。");
                        return EventGraphCompiledValues.ToState(value, m_Runtime.m_Bindings.InputEnumIdentities[operation.Handle.Value]);
                    default:
                        throw new InvalidOperationException($"事件值不能执行 '{operation.Code}'。");
                }
            }

            public void TraceResult(SimulationOperation operation, string outputPort, int outputPortIndex, AbilityStateValue value)
            {
                RequireExecution(operation.Handle);
#if UNITY_EDITOR
                m_Runtime.m_PortValues.Observe(m_Runtime.m_Bindings.Data.ValueLayout.OutputPortSlot(operation.Handle, outputPortIndex), value);
#endif
            }

            public void TraceInput(SimulationOperation operation, CompiledValueInputBinding input, AbilityStateValue value)
            {
                RequireExecution(operation.Handle);
#if UNITY_EDITOR
                m_Runtime.m_PortValues.Observe(m_Runtime.m_Bindings.Data.ValueLayout.InputPortSlot(operation.Handle, input.TargetPortIndex), value);
#endif
            }
        }
    }
}
