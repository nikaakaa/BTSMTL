using System;

namespace BTSMTL.EventGraphs
{
    public enum EventGraphCapabilityKind : byte
    {
        InitializationEvent = 1,
        UpdateEvent = 2,
        VariableGet = 3,
        VariableSet = 4,
        PureValue = 5,
        Switch = 6,
        InstantSplit = 7,
        Sequence = 8,
        Macro = 9,
        HostInput = 10
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class EventGraphCapabilityAttribute : Attribute
    {
        public EventGraphCapabilityAttribute(EventGraphCapabilityKind kind) => Kind = kind;

        public EventGraphCapabilityKind Kind { get; }
    }
}
