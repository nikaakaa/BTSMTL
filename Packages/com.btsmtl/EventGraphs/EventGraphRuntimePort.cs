#if UNITY_EDITOR
using System;
using FlowCanvas;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    public sealed class EventGraphRuntimePort
    {
        Func<EventGraphValue> m_Read;
        Func<EventGraphValue> m_ReadInput;
        Action<EventGraphValue, EventGraphVariableMutation> m_Mutate;
        Func<bool> m_IsLocked;
        readonly Action m_Trigger;
        internal int Operation { get; }
        internal int ValueSlot { get; }
        internal int EnumTypeContentId { get; }
        internal int CompiledPortId { get; }
        public Port Port { get; }
        public bool IsInput { get; }
        public string GraphId { get; }
        public Type ValueType { get; }
        public string NodeId => Port.parent.UID;
        public string PortId => Port.ID;
        public bool IsTrigger => Port.type == typeof(Flow);

        internal EventGraphRuntimePort(string graph, Port port, bool input, int operation, int slot,
            int compiledPortId, int enumTypeContentId, Func<EventGraphRuntimePort, bool, EventGraphValue> read,
            Action<EventGraphRuntimePort, EventGraphValue, EventGraphVariableMutation> mutate,
            Func<EventGraphRuntimePort, bool> isLocked, Action trigger)
        {
            GraphId = graph;
            ValueType = port.type;
            Port = port;
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
        }

        public EventGraphRuntimePort(string graph, Port port, bool input, Type valueType, Func<EventGraphValue> read,
            Func<EventGraphValue> readInput, Action<EventGraphValue, EventGraphVariableMutation> mutate, Func<bool> isLocked)
        {
            GraphId = graph; Port = port; IsInput = input; ValueType = valueType;
            m_Read = read; m_ReadInput = readInput; m_Mutate = mutate;
            m_IsLocked = isLocked;
        }

        public EventGraphValue Read() => m_Read();
        public EventGraphValue ReadInput() => m_ReadInput();
        public bool IsLocked => m_IsLocked();

        public void WriteRecordedFrameState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter =>
            WriteRecordedFrameState(ref writer, false);

        internal void WriteRecordedFrameState<TWriter>(ref TWriter writer, bool maximumCapacity)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            EventGraphValue value = maximumCapacity ? default : m_ReadInput();
            bool provided = maximumCapacity || value.Kind != default;
            writer.Write(provided);
            if (provided)
            {
                writer.Write(maximumCapacity || m_IsLocked());
                if (maximumCapacity)
                    EventGraphValue.WriteRecordedStateCapacity(ref writer, ValueType);
                else
                    value.WriteRecordedFrameState(ref writer);
            }
        }

        public void RestoreRecordedFrameState(ref RecordedRuntimeStateReader reader)
        {
            EventGraphVariableMutation operation = ReadRecordedMutation(ref reader, ValueType, out EventGraphValue value);
            m_Mutate(value, operation);
        }

        internal static EventGraphVariableMutation ReadRecordedMutation(ref RecordedRuntimeStateReader reader,
            Type valueType, out EventGraphValue value)
        {
            if (reader.Read<bool>())
            {
                bool locked = reader.Read<bool>();
                value = EventGraphValue.ReadRecordedFrameState(ref reader, valueType);
                return locked ? EventGraphVariableMutation.Override : EventGraphVariableMutation.Assign;
            }
            value = default;
            return EventGraphVariableMutation.Release;
        }

        public void Mutate(EventGraphValue value, EventGraphVariableMutation operation) => m_Mutate(value, operation);
        public void Trigger() => m_Trigger();
    }
}
#endif
