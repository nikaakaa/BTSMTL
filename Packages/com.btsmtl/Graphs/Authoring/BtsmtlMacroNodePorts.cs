using System;
using System.Collections.Generic;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Graphs
{
    // 子图默认出入口、执行入口与端口方向；作者 JSON 与引擎外编译只认这里，Unity 编辑壳另行派生 FlowCanvas 节点。
    public static class BtsmtlMacroDefaults
    {
        public static (float X, float Y) DefaultInputPosition => (200, 200);
        public static (float X, float Y) DefaultOutputPosition => (800, 200);

        public static IReadOnlyList<GraphNodePort> CreateDefaultInputInterface() => new[]
        {
            new GraphNodePort(GraphAuthoringPortDirection.Input, true, Guid.NewGuid().ToString("D"),
                "执行", GraphNodePort.Flow, 0)
        };

        // 调用节点：子图输入是本节点输入，子图输出是本节点输出。
        public static IEnumerable<(T Definition, bool IsInput)> CallPorts<T>(IEnumerable<T> inputs, IEnumerable<T> outputs)
        {
            foreach (T definition in inputs)
                yield return (definition, true);
            foreach (T definition in outputs)
                yield return (definition, false);
        }

        // 子图内输入锚点把参数作为输出交给图内节点，输出锚点反之。
        public static IEnumerable<(T Definition, bool IsInput)> InputAnchorPorts<T>(IEnumerable<T> inputs)
        {
            foreach (T definition in inputs)
                yield return (definition, false);
        }

        public static IEnumerable<(T Definition, bool IsInput)> OutputAnchorPorts<T>(IEnumerable<T> outputs)
        {
            foreach (T definition in outputs)
                yield return (definition, true);
        }
    }

    public sealed partial class BtsmtlMacroCallNode
    {
#if !UNITY_5_3_OR_NEWER
        string m_Subgraph;

        [GraphAuthoringField("graphId", GraphAuthoringFieldValueKind.IdentityReference, PickerKind = "graph", NonEmpty = true,
            CompilationImpact = GraphAuthoringFieldCompilationImpact.Structure)]
        public string Subgraph { get => m_Subgraph; set => m_Subgraph = value; }
#endif
    }

    public sealed partial class BtsmtlMacroInputNode { }

    public sealed partial class BtsmtlMacroOutputNode { }
}
