using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas.Nodes;

namespace BTSMTL
{
    public static class BtsmtlNativeValueNodes
    {
        static readonly IReadOnlyDictionary<string, Type> s_SourceTypes =
            new[] { typeof(SimplexNode).Assembly, typeof(BtsmtlNativeValueNodes).Assembly }.Distinct()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => BtsmtlNativeValueNodeCatalog.TryGet(type.Name, out _))
                .ToDictionary(type => type.Name, StringComparer.Ordinal);

        public static Type SourceType(BtsmtlNativeValueNodeDefinition definition) => s_SourceTypes[definition.NodeTypeName];

        public static bool TryGet(Type nodeType, out BtsmtlNativeValueNodeDefinition definition) =>
            BtsmtlNativeValueNodeCatalog.TryGet(nodeType.Name, out definition) && SourceType(definition) == nodeType;

        public static bool TryGetNode(Type nodeType, out BtsmtlNativeValueNodeDefinition definition)
        {
            for (Type type = nodeType; type != null; type = type.BaseType)
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SimplexNodeWrapper<>))
                    return TryGet(type.GetGenericArguments()[0], out definition);
            definition = null;
            return false;
        }
    }
}
