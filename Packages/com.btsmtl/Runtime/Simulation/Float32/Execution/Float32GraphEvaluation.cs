using System;

namespace ThirdPersonSimulation
{
    public sealed class Float32GraphEvaluationWorkspace
    {
        internal readonly Float32GraphValueRuntime Values;
        public OperationHandle CurrentOperation { get; internal set; } = OperationHandle.Invalid;
#if UNITY_EDITOR
        internal readonly AbilityStateValue[] ObservedPorts;
        internal readonly bool[] WrittenPorts;
        internal readonly AbilityStateValue[] VariableOverrides;
#endif

        public Float32GraphEvaluationWorkspace(Float32GraphExecutionData data
#if UNITY_EDITOR
            , IFloat32GraphValuePortInputs ports, AbilityStateValue[] variableOverrides
#endif
            )
        {
            Values = new Float32GraphValueRuntime(data, new Float32GraphValueWorkspace(data)
#if UNITY_EDITOR
                , ports
#endif
                );
#if UNITY_EDITOR
            ObservedPorts = new AbilityStateValue[data.ValueLayout.PortSlotCount];
            WrittenPorts = new bool[data.ValueLayout.PortSlotCount];
            VariableOverrides = variableOverrides;
#endif
        }

        public void Begin()
        {
            CurrentOperation = OperationHandle.Invalid;
            Values.BeginEvaluation();
#if UNITY_EDITOR
            Array.Clear(WrittenPorts, 0, WrittenPorts.Length);
            Array.Clear(ObservedPorts, 0, ObservedPorts.Length);
#endif
        }
    }

    public static class Float32GraphEvaluation
    {
        public static void PrepareCandidate(ReadOnlySpan<AbilityStateValue> oldState,
            Span<AbilityStateValue> candidate, Float32GraphEvaluationWorkspace workspace)
        {
            oldState.CopyTo(candidate);
            workspace.Begin();
        }

        public static void Execute(Float32GraphBindings bindings, OperationHandle entry,
            AbilityStateValue[] inputs, AbilityStateValue[] candidate, Float32GraphEvaluationWorkspace workspace,
            CanonicalTextValue inputPort = default)
        {
            var target = new Target(bindings, inputs, candidate, workspace);
            OperationControlRuntime.Execute(bindings.Data.Topology, ref target,
                new GraphExecutionEntry(entry, GraphExecutionContract.Complete), inputPort);
        }

        public static void ExecuteOutput(Float32GraphBindings bindings, OperationHandle entry,
            CanonicalTextValue outputPort, AbilityStateValue[] inputs, AbilityStateValue[] candidate,
            Float32GraphEvaluationWorkspace workspace)
        {
            var target = new Target(bindings, inputs, candidate, workspace);
            OperationControlRuntime.ExecuteOutput(bindings.Data.Topology, ref target, entry, outputPort);
        }

        struct Target : ISynchronousOperationTarget, IFloat32GraphValueContext
        {
            readonly Float32GraphBindings m_Bindings;
            readonly AbilityStateValue[] m_Inputs;
            readonly AbilityStateValue[] m_Candidate;
            readonly Float32GraphEvaluationWorkspace m_Workspace;

            internal Target(Float32GraphBindings bindings, AbilityStateValue[] inputs,
                AbilityStateValue[] candidate, Float32GraphEvaluationWorkspace workspace)
            {
                m_Bindings = bindings;
                m_Inputs = inputs;
                m_Candidate = candidate;
                m_Workspace = workspace;
            }

            public bool IsReadOnlyObservation => false;
            public void Enter(OperationExecutionDescriptor operation) => RequireExecution(operation.Handle);
            public void RequireExecution(OperationHandle operation) => m_Workspace.CurrentOperation = operation;
            public void ExecuteLeaf(OperationExecutionDescriptor operation)
            {
                SimulationOperation definition = m_Bindings.Data.Operations[operation.Handle.Value];
                using Float32ValueInputLease inputs = m_Workspace.Values.ReadInputs(ref this, definition);
                int slot = m_Bindings.OperationSlots[operation.Handle.Value];
                m_Candidate[slot] = Float32GraphCalculations.Assign(ReadState(slot), inputs[0], operation.Integer1);
            }

            public bool ReadCondition(OperationExecutionDescriptor operation)
            {
                using Float32ValueInputLease inputs = m_Workspace.Values.ReadInputs(ref this,
                    m_Bindings.Data.Operations[operation.Handle.Value]);
                return inputs[0].Boolean;
            }

            public int ReadCycleCursor(OperationExecutionDescriptor operation) =>
                ReadState(m_Bindings.OperationSlots[operation.Handle.Value]).Int32;
            public void WriteCycleCursor(OperationExecutionDescriptor operation, int cursor) =>
                m_Candidate[m_Bindings.OperationSlots[operation.Handle.Value]] = AbilityStateValue.FromInt32(cursor);

            public AbilityStateValue EvaluateDomainValue(SimulationOperation operation, string outputPort,
                int outputPortIndex, Float32ValueInputLease inputs)
            {
                switch (operation.Code)
                {
                    case SimulationOperationCode.BlackboardGet:
                        return ReadState(m_Bindings.OperationSlots[operation.Handle.Value]);
                    case SimulationOperationCode.Cycle:
                        return AbilityStateValue.FromInt32((operation.Integer0 +
                            ReadState(m_Bindings.OperationSlots[operation.Handle.Value]).Int32) % operation.Integer1);
                    case SimulationOperationCode.HostInput:
                        return m_Inputs[m_Bindings.InputSlots[operation.Handle.Value]];
                    default:
                        throw new InvalidOperationException($"图值不能执行 '{operation.Code}'。");
                }
            }

            AbilityStateValue ReadState(int slot)
            {
#if UNITY_EDITOR
                if (m_Workspace.VariableOverrides[slot].Kind != default)
                    return m_Workspace.VariableOverrides[slot];
#endif
                return m_Candidate[slot];
            }

            public void TraceResult(SimulationOperation operation, string outputPort, int outputPortIndex, AbilityStateValue value)
            {
                RequireExecution(operation.Handle);
#if UNITY_EDITOR
                Observe(m_Bindings.Data.ValueLayout.OutputPortSlot(operation.Handle, outputPortIndex), value);
#endif
            }
            public void TraceInput(SimulationOperation operation, CompiledValueInputBinding input, AbilityStateValue value)
            {
                RequireExecution(operation.Handle);
#if UNITY_EDITOR
                Observe(m_Bindings.Data.ValueLayout.InputPortSlot(operation.Handle, input.TargetPortIndex), value);
#endif
            }
#if UNITY_EDITOR
            void Observe(int slot, AbilityStateValue value)
            {
                m_Workspace.ObservedPorts[slot] = value;
                m_Workspace.WrittenPorts[slot] = true;
            }
#endif
        }
    }
}
