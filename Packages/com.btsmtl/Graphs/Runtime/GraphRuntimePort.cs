#if UNITY_EDITOR
using System;
using FlowCanvas;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    public sealed class GraphRuntimePort
    {
        Func<GraphHostValue> m_Read;
        Func<GraphHostValue> m_ReadInput;
        Action<GraphHostValue, GraphVariableMutation> m_Mutate;
        Func<bool> m_IsLocked;
        readonly Action m_Trigger;
        readonly Func<GraphRuntimePort, bool> m_HasPendingTrigger;
        internal int Operation { get; }
        internal int ValueSlot { get; }
        internal int EnumTypeContentId { get; }
        internal int CompiledPortId { get; }
        public Port Port { get; }
        public bool IsInput { get; }
        public string GraphId { get; }
        public Type ValueType { get; }
        public string NodeId { get; }
        public string PortId { get; }
        public bool IsTrigger { get; }

        internal GraphRuntimePort(string graph, Port port, bool input, int operation, int slot,
            int compiledPortId, int enumTypeContentId, Func<GraphRuntimePort, bool, GraphHostValue> read,
            Action<GraphRuntimePort, GraphHostValue, GraphVariableMutation> mutate,
            Func<GraphRuntimePort, bool> isLocked, Action trigger,
            Func<GraphRuntimePort, bool> hasPendingTrigger)
        {
            GraphId = graph;
            ValueType = port.type;
            Port = port;
            NodeId = port.parent.UID;
            PortId = port.ID;
            IsTrigger = port.type == typeof(Flow);
            IsInput = input;
            Operation = operation;
            ValueSlot = slot;
            CompiledPortId = compiledPortId;
            EnumTypeContentId = enumTypeContentId;
            m_Read = () => read(this, false);
            m_ReadInput = () => read(this, true);
            m_Mutate = (value, mutation) => mutate(this, value, mutation);
            m_IsLocked = () => isLocked(this);
            m_Trigger = trigger;
            m_HasPendingTrigger = hasPendingTrigger;
        }

        public GraphRuntimePort(string graph, Port port, bool input, Type valueType, Func<GraphHostValue> read,
            Func<GraphHostValue> readInput, Action<GraphHostValue, GraphVariableMutation> mutate, Func<bool> isLocked)
            : this(graph, port.parent.UID, port.ID, input, valueType, read, readInput, mutate, isLocked)
        {
            Port = port;
            IsTrigger = port.type == typeof(Flow);
        }

        public GraphRuntimePort(string graph, string nodeId, string portId, bool input, Type valueType,
            Func<GraphHostValue> read, Func<GraphHostValue> readInput,
            Action<GraphHostValue, GraphVariableMutation> mutate, Func<bool> isLocked)
        {
            GraphId = graph; NodeId = nodeId; PortId = portId; IsInput = input; ValueType = valueType;
            m_Read = read; m_ReadInput = readInput; m_Mutate = mutate;
            m_IsLocked = isLocked;
        }

        public GraphHostValue Read() => m_Read();
        public GraphHostValue ReadInput() => m_ReadInput();
        public bool IsLocked => m_IsLocked();

        public void WriteRecordedFrameState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter =>
            WriteRecordedFrameState(ref writer, false);

        internal void WriteRecordedFrameState<TWriter>(ref TWriter writer, bool maximumCapacity)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            GraphHostValue value = maximumCapacity ? default : m_ReadInput();
            bool provided = maximumCapacity || value.Kind != default;
            writer.Write(provided);
            if (provided)
            {
                writer.Write(maximumCapacity || m_IsLocked());
                if (maximumCapacity)
                    GraphHostValue.WriteRecordedStateCapacity(ref writer, ValueType);
                else
                    value.WriteRecordedFrameState(ref writer);
            }
        }

        public void RestoreRecordedFrameState(ref RecordedRuntimeStateReader reader)
        {
            GraphVariableMutation operation = ReadRecordedMutation(ref reader, ValueType, out GraphHostValue value);
            m_Mutate(value, operation);
        }

        internal static GraphVariableMutation ReadRecordedMutation(ref RecordedRuntimeStateReader reader,
            Type valueType, out GraphHostValue value)
        {
            if (reader.Read<bool>())
            {
                bool locked = reader.Read<bool>();
                value = GraphHostValue.ReadRecordedFrameState(ref reader, valueType);
                return locked ? GraphVariableMutation.Override : GraphVariableMutation.Assign;
            }
            value = default;
            return GraphVariableMutation.Release;
        }

        public void Mutate(GraphHostValue value, GraphVariableMutation operation) => m_Mutate(value, operation);
        public void Trigger() => m_Trigger();
        public bool HasPendingTrigger => m_HasPendingTrigger(this);
    }
}
#endif
