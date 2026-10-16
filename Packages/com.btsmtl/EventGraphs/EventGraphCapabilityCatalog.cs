using System;
using System.Reflection;

namespace BTSMTL.EventGraphs
{
    public static class EventGraphCapabilityCatalog
    {
        public static bool TryGet(Type nodeType, out EventGraphCapabilityKind kind)
        {
            EventGraphCapabilityAttribute capability = nodeType.GetCustomAttribute<EventGraphCapabilityAttribute>(true);
            if (capability != null)
            {
                kind = capability.Kind;
                return true;
            }
            if (BtsmtlNativeValueNodeCatalog.TryGetNode(nodeType, out BtsmtlNativeValueNodeDefinition value))
            {
                kind = value.EventGraphCapability;
                return true;
            }
            kind = default;
            return false;
        }

        public static bool Allows(Type nodeType) => TryGet(nodeType, out _);
    }
}
