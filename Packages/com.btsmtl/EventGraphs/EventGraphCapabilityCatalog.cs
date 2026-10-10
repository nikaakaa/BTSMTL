using System;
using FlowCanvas.Macros;
using FlowCanvas.Nodes;

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

    public static class EventGraphCapabilityCatalog
    {
        public static bool TryGet(Type nodeType, out EventGraphCapabilityKind kind)
        {
            if (nodeType == typeof(StartEvent))
                kind = EventGraphCapabilityKind.InitializationEvent;
            else if (nodeType == typeof(UpdateEvent))
                kind = EventGraphCapabilityKind.UpdateEvent;
            else if (nodeType == typeof(SwitchBool))
                kind = EventGraphCapabilityKind.Switch;
            else if (nodeType == typeof(Split))
                kind = EventGraphCapabilityKind.InstantSplit;
            else if (nodeType == typeof(Sequence))
                kind = EventGraphCapabilityKind.Sequence;
            else if (nodeType == typeof(MacroNodeWrapper) ||
                     nodeType == typeof(MacroInputNode) ||
                     nodeType == typeof(MacroOutputNode))
                kind = EventGraphCapabilityKind.Macro;
            else if (typeof(EventGraphHostInputNodeMarker).IsAssignableFrom(nodeType))
                kind = EventGraphCapabilityKind.HostInput;
            else if (nodeType.IsGenericType && nodeType.GetGenericTypeDefinition() == typeof(GetVariable<>))
                kind = EventGraphCapabilityKind.VariableGet;
            else if (nodeType.IsGenericType && nodeType.GetGenericTypeDefinition() == typeof(SetVariable<>))
                kind = EventGraphCapabilityKind.VariableSet;
            else
            {
                if (!BtsmtlNativeValueNodeCatalog.TryGetNode(nodeType, out _))
                {
                    kind = default;
                    return false;
                }
                kind = EventGraphCapabilityKind.PureValue;
            }
            return true;
        }

        public static bool Allows(Type nodeType) => TryGet(nodeType, out _);
    }
}
