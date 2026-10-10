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
        readonly HostEventGraphRuntime m_Runtime;
        internal OperationHandle Operation { get; }
        internal int ValueSlot { get; }
        internal CanonicalTextValue EnumIdentity { get; }
        public Port Port { get; }
        public bool IsInput { get; }
        public string GraphId { get; }
        public Type ValueType { get; }
        public string NodeId => Port.parent.UID;
        public string PortId => Port.ID;
        public bool IsTrigger => Port.type == typeof(Flow);

        internal EventGraphRuntimePort(HostEventGraphRuntime runtime, Port port, bool input, OperationHandle operation, int slot)
        {
            m_Runtime = runtime;
            GraphId = runtime.Source.AuthoringId;
            ValueType = port.type;
            Port = port;
            IsInput = input;
            Operation = operation;
            ValueSlot = slot;
            if (ValueType.IsEnum)
                EnumIdentity = CanonicalTextValue.FromString(ValueType.AssemblyQualifiedName);
            if (!IsTrigger)
            {
                m_Read = () => runtime.ReadPort(this, false);
                m_ReadInput = () => runtime.ReadPort(this, true);
                m_Mutate = (value, mutation) => runtime.MutatePort(this, value, mutation);
                m_IsLocked = () => runtime.IsPortLocked(this);
            }
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
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            EventGraphValue value = m_ReadInput();
            bool provided = value.Kind != default;
            writer.Write(provided);
            if (provided)
            {
                writer.Write(m_IsLocked());
                value.WriteRecordedFrameState(ref writer);
            }
        }

        public void RestoreRecordedFrameState(ref RecordedRuntimeStateReader reader)
        {
            if (reader.Read<bool>())
            {
                bool locked = reader.Read<bool>();
                EventGraphValue value = EventGraphValue.ReadRecordedFrameState(ref reader, ValueType);
                m_Mutate(value, locked ? EventGraphVariableMutation.Override : EventGraphVariableMutation.Assign);
            }
            else
                m_Mutate(default, EventGraphVariableMutation.Release);
        }

        public void Mutate(EventGraphValue value, EventGraphVariableMutation operation) => m_Mutate(value, operation);
        public void Trigger() => m_Runtime.QueuePortTrigger(NodeId, PortId, IsInput);
    }
}
#endif
