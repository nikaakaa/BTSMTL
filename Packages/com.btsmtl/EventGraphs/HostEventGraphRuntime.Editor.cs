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
        EventGraphRuntimePort[] m_RuntimePorts;
        Float32GraphPortValues m_PortValues => m_Runtime.PortValues;
        public IReadOnlyList<EventGraphRuntimePort> RuntimePorts => m_RuntimePorts;
        public EventGraphRuntimePort RequireRuntimePort(string node, string port, bool input) => m_EditorPorts[(node, port, input)];
        public void QueuePortTrigger(string node, string port, bool input)
        {
            EventGraphRuntimePort target = RequireRuntimePort(node, port, input);
            m_Runtime.QueueTrigger(target.Operation, target.PortId, target.IsInput);
        }

        public void MutateRuntimeVariable(string variableId, EventGraphValue value, EventGraphVariableMutation operation)
        {
            m_Runtime.RequireIdle();
            int slot = m_Bindings.Runtime.PublishedSlots[VariableContract.Layout.Bind(variableId).Index];
            if (operation != EventGraphVariableMutation.Release)
                RequireValue(value, m_Bindings.StateTypes[slot]);
            AbilityStateValue state = operation == EventGraphVariableMutation.Release ? default :
                EventGraphCompiledValues.ToState(value, m_Bindings.Runtime.Data.DefaultStateValue(slot).Identity);
            if (operation == EventGraphVariableMutation.Assign)
                m_Runtime.WriteState(slot, state);
            m_Runtime.SetVariableOverride(slot, state, operation == EventGraphVariableMutation.Override);
        }

        public EventGraphValue ReadRuntimeVariable(string variableId)
        {
            int slot = m_Bindings.Runtime.PublishedSlots[VariableContract.Layout.Bind(variableId).Index];
            return EventGraphCompiledValues.FromState(m_Runtime.ReadState(slot), m_Bindings.StateTypes[slot]);
        }

        public bool IsRuntimeVariableLocked(string variableId) =>
            m_Runtime.IsVariableLocked(m_Bindings.Runtime.PublishedSlots[VariableContract.Layout.Bind(variableId).Index]);

        internal EventGraphValue ReadPort(EventGraphRuntimePort port, bool input)
        {
            AbilityStateValue value = input ? m_PortValues.ReadInput(port.ValueSlot) :
                m_PortValues.TryRead(port.ValueSlot, out AbilityStateValue provided) ? provided : m_PortValues.Read(port.ValueSlot);
            return value.Kind == default ? default : EventGraphCompiledValues.FromState(value, port.ValueType);
        }

        internal bool IsPortLocked(EventGraphRuntimePort port) => m_PortValues.IsLocked(port.ValueSlot);

        internal void MutatePort(EventGraphRuntimePort port, EventGraphValue value, EventGraphVariableMutation operation)
        {
            m_Runtime.RequireIdle();
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
            var maps = new Dictionary<(string Node, string Port, ProgramValuePortDirection Direction), ProgramExecutionSource>();
            foreach (ProgramExecutionSource map in m_Bindings.Runtime.Data.ExecutionSources)
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

            void Add(Port port, bool input)
            {
                bool trigger = port.type == typeof(Flow);
                ProgramValuePortDirection direction = trigger ? ProgramValuePortDirection.None :
                    input ? ProgramValuePortDirection.Input : ProgramValuePortDirection.Output;
                ProgramExecutionSource map = maps[(port.parent.UID, port.ID, direction)];
                var operation = new OperationHandle(map.TargetIndex);
                int slot = -1;
                if (!trigger)
                {
                    SimulationOperation definition = m_Bindings.Runtime.Data.Operations[operation.Value];
                    OperationValuePortContract contract = GameplayAbilityValuePortContracts.Require(definition.Code, definition.Integer0);
                    slot = input
                        ? m_Bindings.Runtime.Data.ValueLayout.InputPortSlot(operation, contract.RequireInput(map.CompiledPortId).Order)
                        : m_Bindings.Runtime.Data.ValueLayout.OutputPortSlot(operation, contract.RequireSelection(map.CompiledPortId).Order);
                }
                m_EditorPorts.Add((port.parent.UID, port.ID, input), new EventGraphRuntimePort(this, port, input, operation, slot));
            }
        }

        public void WriteRecordedFrameState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            m_Runtime.RequireIdle();
            m_Runtime.WriteRecordedLifecycle(ref writer);
            for (int i = 0; i < m_Bindings.Runtime.Data.StateSlots.Count; i++)
            {
                EventGraphCompiledValues.FromState(m_Runtime.ReadStoredState(i), m_Bindings.StateTypes[i]).WriteRecordedFrameState(ref writer);
                writer.Write(m_Runtime.IsVariableLocked(i));
                if (m_Runtime.IsVariableLocked(i))
                    EventGraphCompiledValues.FromState(m_Runtime.ReadVariableOverride(i), m_Bindings.StateTypes[i]).WriteRecordedFrameState(ref writer);
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
        }

        public void RestoreRecordedFrameState(ref RecordedRuntimeStateReader reader)
        {
            m_Runtime.RestoreRecordedLifecycle(ref reader);
            for (int i = 0; i < m_Bindings.Runtime.Data.StateSlots.Count; i++)
            {
                m_Runtime.WriteState(i, EventGraphCompiledValues.ToState(
                    EventGraphValue.ReadRecordedFrameState(ref reader, m_Bindings.StateTypes[i]),
                    m_Bindings.Runtime.Data.DefaultStateValue(i).Identity));
                bool locked = reader.Read<bool>();
                if (locked)
                {
                    AbilityStateValue value = EventGraphCompiledValues.ToState(
                        EventGraphValue.ReadRecordedFrameState(ref reader, m_Bindings.StateTypes[i]),
                        m_Bindings.Runtime.Data.DefaultStateValue(i).Identity);
                    m_Runtime.SetVariableOverride(i, value, true);
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
        }
    }
}
#endif
