using System;
using System.Collections.Generic;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Graphs
{
    public sealed partial class BtsmtlMacroGraph
    {
        public static (float X, float Y) DefaultInputPosition => (200, 200);
        public static (float X, float Y) DefaultOutputPosition => (800, 200);

        public static IReadOnlyList<GraphNodePort> CreateDefaultInputInterface() => new[]
        {
            new GraphNodePort(GraphAuthoringPortDirection.Input, true, Guid.NewGuid().ToString("D"),
                "执行", GraphNodePort.Flow, 0)
        };
    }

    public sealed partial class BtsmtlMacroInputNode
#if BTSMTL_AUTHORING && !UNITY_EDITOR
        : FlowCanvas.Macros.MacroInputNode
#endif
    { }

    public sealed partial class BtsmtlMacroOutputNode
#if BTSMTL_AUTHORING && !UNITY_EDITOR
        : FlowCanvas.Macros.MacroOutputNode
#endif
    { }
}
