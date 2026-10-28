using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using FlowCanvas;
using FlowCanvas.Macros;
using ParadoxNotion;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs.Editor
{
    // 宿主按节点能力取得编译描述，字段直接取自平台作者对象。
    public interface IBtsmtlGraphNodeCompileSource
    {
        GraphNodeCompileDescription Describe(FlowNode node);
    }

    // 节点的平台补充信息由宿主按节点能力给出。
    public readonly struct BtsmtlFlowCompileNodeInfo
    {
        public BtsmtlFlowCompileNodeInfo(string kind, Func<GraphNodeFields> fields, Func<CompileNode, SimulationNodeEmission> describe)
        {
            Kind = kind;
            Fields = fields;
            Describe = describe;
        }

        public string Kind { get; }
        public Func<GraphNodeFields> Fields { get; }
        public Func<CompileNode, SimulationNodeEmission> Describe { get; }
    }

    // Unity 作者图到编译视图：端口按 FlowCanvas 注册顺序，出边按 outConnections 顺序，值端口给出编译身份。
    public sealed class BtsmtlFlowCompileGraphs
    {
        readonly Func<FlowNode, BtsmtlFlowCompileNodeInfo> m_Info;
        readonly Func<FlowGraph, string> m_GraphId;
        readonly Dictionary<FlowGraph, CompileGraph> m_Graphs = new();
        readonly Dictionary<CompileNode, FlowNode> m_Nodes = new();

        public BtsmtlFlowCompileGraphs(Func<FlowNode, BtsmtlFlowCompileNodeInfo> info, Func<FlowGraph, string> graphId)
        {
            m_Info = info ?? throw new ArgumentNullException(nameof(info));
            m_GraphId = graphId ?? throw new ArgumentNullException(nameof(graphId));
        }

        public FlowNode Source(CompileNode node) => m_Nodes[node];

        public static FlowNode[] ReadNodes(FlowGraph graph) =>
            graph.allNodes.Cast<FlowNode>().OrderBy(node => node.UID, StringComparer.Ordinal).ToArray();

        public CompileGraph Read(FlowGraph graph)
        {
            if (m_Graphs.TryGetValue(graph, out CompileGraph existing))
                return existing;
            IReadOnlyList<CompileGraphParameter> inputs = null, outputs = null;
            if (graph is Macro macro)
            {
                BtsmtlMacroInterface.Validate(macro);
                inputs = macro.inputDefinitions.Select(Parameter).ToArray();
                outputs = macro.outputDefinitions.Select(Parameter).ToArray();
            }
            var view = new CompileGraph(m_GraphId(graph), graph.GetType().FullName, inputs, outputs);
            m_Graphs.Add(graph, view);
            var nodes = new Dictionary<FlowNode, CompileNode>();
            foreach (FlowNode node in graph.allNodes.Cast<FlowNode>())
                nodes.Add(node, Node(node));
            view.Complete(nodes.Values, compiled =>
            {
                FlowNode node = m_Nodes[compiled];
                return node.outConnections.Cast<BinderConnection>().Select(edge => new CompileEdge(edge.UID,
                    edge.GetType().FullName, compiled, edge.sourcePortID, edge.targetNode.UID, edge.targetPortID,
                    edge.sourcePort is ValueOutput)).ToArray();
            });
            return view;
        }

        CompileNode Node(FlowNode node)
        {
            BtsmtlFlowCompileNodeInfo info = m_Info(node);
            BtsmtlNativeValueNodeCatalog.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition native);
            GraphOrchestrationNodeDeclaration declaration = GraphOrchestrationNodeDeclaration.Of(node.GetType());
            bool hostOutput = node is IBtsmtlHostInputFlowNode && declaration.HostInputs == null;
            var inputs = node.GetInputValuePorts().Select(port => new CompileValuePort(port.ID,
                native != null ? native.Input(port.ID) : node is IBtsmtlVariableFlowNode ? "m_Value"
                    : declaration.RuntimePort(port.ID, OrchestrationExtensionPortDirection.Input),
                ValueType(port.type), port.isConnected, port.isRequired, SemanticValue(port.serializedValue))).ToArray();
            var outputs = node.GetOutputValuePorts().Select(port => new CompileValuePort(port.ID,
                native != null ? native.Output(port.ID) : node is IBtsmtlVariableFlowNode || hostOutput ? "m_Output"
                    : declaration.RuntimePort(port.ID, OrchestrationExtensionPortDirection.Output),
                ValueType(port.type))).ToArray();
            CompileNodeRole role = node switch
            {
                MacroInputNode => CompileNodeRole.MacroInput,
                MacroOutputNode => CompileNodeRole.MacroOutput,
                MacroNodeWrapper => CompileNodeRole.MacroCall,
                IBtsmtlVariableFlowNode => CompileNodeRole.Variable,
                _ => CompileNodeRole.Leaf
            };
            var compiled = new CompileNode(node.UID, node.GetType().FullName, info.Kind, role,
                node.GetInputFlowPorts().Select(port => port.ID).ToArray(),
                node.GetOutputFlowPorts().Select(port => port.ID).ToArray(),
                inputs, outputs, info.Fields, info.Describe);
            m_Nodes.Add(compiled, node);
            if (node is MacroNodeWrapper call)
                compiled.WithCallee(Read((FlowGraph)call.macro));
            if (node is IBtsmtlVariableFlowNode variable)
                compiled.WithVariable(variable.OwnerId, variable.VariableId, variable.Writes);
            return compiled;
        }

        static CompileGraphParameter Parameter(DynamicParameterDefinition parameter)
        {
            if (parameter.type == typeof(Flow))
                return new CompileGraphParameter(parameter.ID, parameter.name, null, true);
            BtsmtlMacroInterface.RequireValueType(parameter.type);
            return new CompileGraphParameter(parameter.ID, parameter.name, ValueType(parameter.type), false);
        }

        public static CompileValueType ValueType(Type type) => new(BtsmtlGraphValueTypes.Kind(type), type.FullName,
            type == typeof(string) ? string.Empty : SemanticValue(type == typeof(Quaternion) ? Quaternion.identity : Activator.CreateInstance(type)),
            type.IsEnum ? type.AssemblyQualifiedName : null);

        // 引擎向量与旋转转为通用语义值，编译视图不携带引擎类型。
        public static object SemanticValue(object value) => value switch
        {
            Vector2 vector => new SemanticVector2Value(vector.x, vector.y),
            Vector3 vector => new SemanticVector3Value(vector.x, vector.y, vector.z),
            Quaternion rotation => new SemanticQuaternionValue(rotation.x, rotation.y, rotation.z, rotation.w),
            AnimationCurve curve => Curve(curve),
            _ => value
        };

        public static GameplayAbilityCurve<double> Curve(AnimationCurve source)
        {
            if (source == null) return null;
            Keyframe[] sourceKeys = source.keys;
            var keys = new GameplayAbilityCurveKey<double>[sourceKeys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = sourceKeys[i];
                keys[i] = new GameplayAbilityCurveKey<double>(key.time, key.value,
                    float.IsInfinity(key.inTangent) ? 0f : key.inTangent,
                    float.IsInfinity(key.outTangent) ? 0f : key.outTangent,
                    key.inWeight, key.outWeight, (int)key.weightedMode,
                    i + 1 < keys.Length && (float.IsInfinity(key.outTangent) || float.IsInfinity(sourceKeys[i + 1].inTangent)));
            }
            return new GameplayAbilityCurve<double>((int)source.preWrapMode, (int)source.postWrapMode, keys);
        }
    }
}
