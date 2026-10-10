#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FlowCanvas;
using NodeCanvas.Framework;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    public sealed partial class HostEventGraphRuntime
    {
        readonly Dictionary<(string Node, string Port, bool Input), EventGraphRuntimePort> m_EditorPorts = new();
        readonly List<EventGraphRuntimePort> m_EditorTriggers = new();
        EventGraphRuntimePort[] m_RuntimePorts;
        Float32GraphPortValues m_PortValues;
        AbilityStateValue[] m_VariableOverrides;
        bool[] m_VariableLocks;
        public IReadOnlyList<EventGraphRuntimePort> RuntimePorts => m_RuntimePorts;
        public EventGraphRuntimePort RequireRuntimePort(string node, string port, bool input) => m_EditorPorts[(node, port, input)];
        public void QueuePortTrigger(string node, string port, bool input) =>
            m_EditorTriggers.Add(RequireRuntimePort(node, port, input));

        public void MutateRuntimeVariable(string variableId, EventGraphValue value, EventGraphVariableMutation operation)
        {
            int slot = m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index];
            if (operation != EventGraphVariableMutation.Release)
                RequireValue(value, m_Bindings.StateTypes[slot]);
            AbilityStateValue state = operation == EventGraphVariableMutation.Release ? default :
                EventGraphCompiledValues.ToState(value, m_Bindings.Data.DefaultStateValue(slot).Identity);
            if (operation == EventGraphVariableMutation.Assign)
                WriteState(slot, state);
            else if (operation == EventGraphVariableMutation.Override)
                state.Retain();
            m_VariableOverrides[slot].Release();
            m_VariableOverrides[slot] = operation == EventGraphVariableMutation.Override ? state : default;
            m_VariableLocks[slot] = operation == EventGraphVariableMutation.Override;
        }

        public EventGraphValue ReadRuntimeVariable(string variableId)
        {
            int slot = m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index];
            return EventGraphCompiledValues.FromState(ReadState(slot), m_Bindings.StateTypes[slot]);
        }

        public bool IsRuntimeVariableLocked(string variableId) =>
            m_VariableLocks[m_Bindings.PublishedSlots[VariableContract.Layout.Bind(variableId).Index]];

        internal EventGraphValue ReadPort(EventGraphRuntimePort port, bool input)
        {
            AbilityStateValue value = input ? m_PortValues.ReadInput(port.ValueSlot) :
                m_PortValues.TryRead(port.ValueSlot, out AbilityStateValue provided) ? provided : m_PortValues.Read(port.ValueSlot);
            return value.Kind == default ? default : EventGraphCompiledValues.FromState(value, port.ValueType);
        }

        internal bool IsPortLocked(EventGraphRuntimePort port) => m_PortValues.IsLocked(port.ValueSlot);

        internal void MutatePort(EventGraphRuntimePort port, EventGraphValue value, EventGraphVariableMutation operation)
        {
            if (operation == EventGraphVariableMutation.Release)
                m_PortValues.Release(port.ValueSlot);
            else
            {
                RequireValue(value, port.ValueType);
                m_PortValues.Set(port.ValueSlot, EventGraphCompiledValues.ToState(value, port.EnumIdentity),
                    operation == EventGraphVariableMutation.Override);
            }
        }

        static void RequireValue(EventGraphValue value, Type type)
        {
            EventGraphValueKinds.TryGet(type, out EventGraphValueKind expected);
            if (value.Kind != expected || expected == EventGraphValueKind.Enum && value.EnumType != type)
                throw new InvalidOperationException($"运行编辑值与声明类型 '{type.FullName}' 不匹配。");
        }

        void PrepareEditor()
        {
            m_PortValues = new Float32GraphPortValues(m_Bindings.Data.ValueLayout);
            m_VariableOverrides = new AbilityStateValue[m_State.Length];
            m_VariableLocks = new bool[m_State.Length];
            m_EditorPorts.Clear();
            var maps = new Dictionary<(string Node, string Port, ProgramValuePortDirection Direction), ProgramSourceMapEntry>();
            foreach (ProgramSourceMapEntry map in m_Bindings.Data.SourceMap)
                if (map.GraphId == Source.AuthoringId && map.TargetKind == ProgramSourceTargetKind.OperationPort)
                    maps.Add((map.NodeId, map.PortId, map.ValuePortDirection), map);
            foreach (Node node in Source.allNodes)
            {
                var flow = (FlowNode)node;
                foreach (Port port in flow.GetInputPorts())
                    Add(port, true);
                foreach (Port port in flow.GetOutputPorts())
                    Add(port, false);
            }
            m_RuntimePorts = new EventGraphRuntimePort[m_EditorPorts.Count];
            m_EditorPorts.Values.CopyTo(m_RuntimePorts, 0);
            m_EditorTriggers.Capacity = m_RuntimePorts.Length;

            void Add(Port port, bool input)
            {
                bool trigger = port.type == typeof(Flow);
                ProgramValuePortDirection direction = trigger ? ProgramValuePortDirection.None :
                    input ? ProgramValuePortDirection.Input : ProgramValuePortDirection.Output;
                ProgramSourceMapEntry map = maps[(port.parent.UID, port.ID, direction)];
                var operation = new OperationHandle(map.TargetIndex);
                int slot = -1;
                if (!trigger)
                {
                    SimulationOperation definition = m_Bindings.Data.Operations[operation.Value];
                    OperationValuePortContract contract = GameplayAbilityValuePortContracts.Require(definition.Code, definition.Integer0);
                    slot = input
                        ? m_Bindings.Data.ValueLayout.InputPortSlot(operation, contract.RequireInput(map.CompiledPortId).Order)
                        : m_Bindings.Data.ValueLayout.OutputPortSlot(operation, contract.RequireSelection(map.CompiledPortId).Order);
                }
                m_EditorPorts.Add((port.parent.UID, port.ID, input), new EventGraphRuntimePort(this, port, input, operation, slot));
            }
        }

        void ExecuteTriggers(ref Context context)
        {
            for (int i = 0; i < m_EditorTriggers.Count; i++)
            {
                EventGraphRuntimePort port = m_EditorTriggers[i];
                if (port.IsInput)
                    OperationCompositeRuntime.ExecuteSynchronous(m_Bindings.Data.Topology, ref context, port.Operation, port.PortId);
                else
                    OperationCompositeRuntime.ExecuteOutput(m_Bindings.Data.Topology, ref context, port.Operation, port.PortId);
            }
            m_EditorTriggers.Clear();
        }

        void ResetEditor()
        {
            m_PortValues.Reset();
            m_EditorTriggers.Clear();
            for (int i = 0; i < m_VariableOverrides.Length; i++)
            {
                m_VariableOverrides[i].Release();
                m_VariableOverrides[i] = default;
                m_VariableLocks[i] = false;
            }
        }

        public void WriteRecordedFrameState<TWriter>(ref TWriter writer)
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
            for (int i = 0; i < m_State.Length; i++)
            {
                EventGraphCompiledValues.FromState(m_State[i], m_Bindings.StateTypes[i]).WriteRecordedFrameState(ref writer);
                writer.Write(m_VariableLocks[i]);
                if (m_VariableLocks[i])
                    EventGraphCompiledValues.FromState(m_VariableOverrides[i], m_Bindings.StateTypes[i]).WriteRecordedFrameState(ref writer);
            }
            for (int i = 0; i < m_RuntimePorts.Length; i++)
            {
                EventGraphRuntimePort port = m_RuntimePorts[i];
                if (port.IsTrigger)
                    continue;
                port.WriteRecordedFrameState(ref writer);
                AbilityStateValue observed = m_PortValues.Read(port.ValueSlot);
                writer.Write(observed.Kind != default);
                if (observed.Kind != default)
                    EventGraphCompiledValues.FromState(observed, port.ValueType).WriteRecordedFrameState(ref writer);
            }
            writer.Write(m_EditorTriggers.Count);
            for (int i = 0; i < m_EditorTriggers.Count; i++)
                writer.Write(Array.IndexOf(m_RuntimePorts, m_EditorTriggers[i]));
        }

        public void RestoreRecordedFrameState(ref RecordedRuntimeStateReader reader)
        {
            ResetEditor();
            m_Output.Invalidate();
            m_ResetGeneration = reader.Read<ulong>();
            m_Started = reader.Read<bool>();
            m_Faulted = reader.Read<bool>();
            string invocationSource = reader.ReadText();
            ulong sequence = reader.Read<ulong>();
            m_LastInvocation = sequence == 0 ? default : new EventGraphInvocationIdentity(invocationSource, sequence);
            m_LastFailure = m_Faulted ? new EventGraphExecutionFailure(reader.ReadText(), reader.ReadText(),
                reader.ReadText(), reader.ReadText(), null, reader.ReadText()) : null;
            for (int i = 0; i < m_State.Length; i++)
            {
                WriteState(i, EventGraphCompiledValues.ToState(
                    EventGraphValue.ReadRecordedFrameState(ref reader, m_Bindings.StateTypes[i]),
                    m_Bindings.Data.DefaultStateValue(i).Identity));
                m_VariableLocks[i] = reader.Read<bool>();
                if (m_VariableLocks[i])
                {
                    AbilityStateValue value = EventGraphCompiledValues.ToState(
                        EventGraphValue.ReadRecordedFrameState(ref reader, m_Bindings.StateTypes[i]),
                        m_Bindings.Data.DefaultStateValue(i).Identity);
                    value.Retain();
                    m_VariableOverrides[i] = value;
                }
            }
            for (int i = 0; i < m_RuntimePorts.Length; i++)
            {
                EventGraphRuntimePort port = m_RuntimePorts[i];
                if (port.IsTrigger)
                    continue;
                port.RestoreRecordedFrameState(ref reader);
                if (reader.Read<bool>())
                    m_PortValues.Observe(port.ValueSlot, EventGraphCompiledValues.ToState(
                        EventGraphValue.ReadRecordedFrameState(ref reader, port.ValueType), port.EnumIdentity));
            }
            int triggerCount = reader.Read<int>();
            for (int i = 0; i < triggerCount; i++)
                m_EditorTriggers.Add(m_RuntimePorts[reader.Read<int>()]);
        }
    }
}
#endif
