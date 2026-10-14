#if UNITY_EDITOR
using System.Collections.Generic;
using BTSMTL.EventGraphs;

namespace ThirdPersonSimulation
{
    public sealed partial class Float32GraphEventRuntime
    {
        readonly Float32GraphPortValues m_PortValues;
        readonly AbilityStateValue[] m_VariableOverrides;
        readonly List<(OperationHandle Operation, CanonicalTextValue Port, bool Input)> m_Triggers;

        public Float32GraphPortValues PortValues => m_PortValues;
        public AbilityStateValue ReadStoredState(int slot) => m_State[slot];
        public AbilityStateValue ReadVariableOverride(int slot) => m_VariableOverrides[slot];
        public bool IsVariableLocked(int slot) => m_VariableOverrides[slot].Kind != default;

        public void SetVariableOverride(int slot, AbilityStateValue value, bool locked)
        {
            if (locked) value.Retain();
            m_VariableOverrides[slot].Release();
            m_VariableOverrides[slot] = locked ? value : default;
        }

        public void QueueTrigger(OperationHandle operation, CanonicalTextValue port, bool input)
        {
            RequireIdle();
            port.Retain();
            m_Triggers.Add((operation, port, input));
        }

        void ExecuteTriggers()
        {
            for (int i = 0; i < m_Triggers.Count; i++)
            {
                var trigger = m_Triggers[i];
                if (trigger.Input)
                    Float32GraphEvaluation.Execute(m_Bindings, trigger.Operation, m_Inputs, m_Candidate, m_Workspace, trigger.Port);
                else
                    Float32GraphEvaluation.ExecuteOutput(m_Bindings, trigger.Operation, trigger.Port, m_Inputs, m_Candidate, m_Workspace);
            }
        }

        void AdoptEditor()
        {
            for (int i = 0; i < m_Workspace.WrittenPorts.Length; i++)
                if (m_Workspace.WrittenPorts[i])
                    m_PortValues.Observe(i, m_Workspace.ObservedPorts[i]);
            m_PortValues.CompleteEvaluation();
            ClearTriggers();
        }

        void ResetEditor()
        {
            m_PortValues.Reset();
            ClearTriggers();
            for (int i = 0; i < m_VariableOverrides.Length; i++)
            {
                m_VariableOverrides[i].Release();
                m_VariableOverrides[i] = default;
            }
        }

        void ClearTriggers()
        {
            for (int i = 0; i < m_Triggers.Count; i++)
                m_Triggers[i].Port.Release();
            m_Triggers.Clear();
        }

        public void WriteRecordedLifecycle<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(m_ResetGeneration);
            writer.Write(m_Started);
            writer.Write(m_Faulted);
            writer.WriteText(m_LastInvocation.SourceId);
            writer.Write(m_LastInvocation.Sequence);
            if (m_Faulted)
            {
                writer.WriteText(m_LastFailure.GraphId);
                writer.WriteText(m_LastFailure.EventId);
                writer.WriteText(m_LastFailure.NodeId);
                writer.WriteText(m_LastFailure.Message);
                writer.WriteText(m_LastFailure.ExceptionType);
            }
            writer.Write(m_Triggers.Count);
            for (int i = 0; i < m_Triggers.Count; i++)
            {
                var trigger = m_Triggers[i];
                writer.Write(trigger.Operation.Value);
                writer.WriteText(trigger.Port);
                writer.Write(trigger.Input);
            }
        }

        public void RestoreRecordedLifecycle(ref RecordedRuntimeStateReader reader)
        {
            RequireIdle();
            ResetEditor();
            m_Output.Invalidate();
            m_ResetGeneration = reader.Read<ulong>();
            m_Started = reader.Read<bool>();
            m_Faulted = reader.Read<bool>();
            CanonicalTextValue invocationSource = reader.ReadText();
            ulong sequence = reader.Read<ulong>();
            m_LastInvocation.Release();
            m_LastInvocation = sequence == 0 ? default : new EventGraphInvocationIdentity(invocationSource, sequence);
            m_LastInvocation.Retain();
            m_LastFailure = m_Faulted ? new EventGraphExecutionFailure(reader.ReadText().ToString(), reader.ReadText().ToString(),
                reader.ReadText().ToString(), reader.ReadText().ToString(), null, reader.ReadText().ToString()) : null;
            int triggerCount = reader.Read<int>();
            for (int i = 0; i < triggerCount; i++)
                QueueTrigger(new OperationHandle(reader.Read<int>()), reader.ReadText(), reader.Read<bool>());
        }
    }
}
#endif
