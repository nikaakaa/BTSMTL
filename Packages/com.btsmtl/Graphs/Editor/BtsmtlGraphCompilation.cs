using BTSMTL.Compilation;
using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL;
using BTSMTL.Authoring.Graph;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using FlowCanvas;
using FlowCanvas.Nodes;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs.Editor
{
    public sealed class BtsmtlGraphCompilation
    {
        static readonly global::Unity.Profiling.ProfilerMarker s_EmitOperation = new("Btsmtl.Graph.Compile.EmitOperation");

        readonly BtsmtlGraphSemanticBuilder m_Builder;
        readonly SimulationOperationEmitter m_Emitter;
        readonly IBtsmtlCanvasCompilationBindings m_Bindings;

        public BtsmtlGraphCompilation(BtsmtlGraphSemanticBuilder builder,
            IBtsmtlCanvasCompilationBindings bindings = null)
        {
            m_Builder = builder;
            m_Bindings = bindings;
            m_Emitter = new SimulationOperationEmitter(builder, bindings);
        }

        public static FlowNode[] ReadNodes(FlowGraph graph) =>
            graph.allNodes.Cast<FlowNode>().OrderBy(node => node.UID, StringComparer.Ordinal).ToArray();

        public OperationHandle EmitLeaf(FlowNode node, string graphId, string route, string hash,
            SimulationNodeEmission emission, Func<FlowNode, SimulationCompileReport, SimulationNodeEmission> describe)
        {
            BtsmtlNativeValueNodeCatalog.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition native);
            OperationValuePortContract contract = PortContract(emission);
            ValueInput[] inputs = node.GetInputValuePorts().ToArray();
            ValueOutput[] outputs = node.GetOutputValuePorts().ToArray();
            bool nativeSet = node is IBtsmtlVariableFlowNode { Writes: true };
            var declaredInputs = new HashSet<string>(inputs.Select(port => MapPort(node, port.ID, native, emission.Code, false)), StringComparer.Ordinal);
            declaredInputs.UnionWith(emission.BlackboardInputs.Select(port => port.PortId));
            declaredInputs.UnionWith(emission.ConstantInputs.Select(port => port.PortId));
            declaredInputs.UnionWith(emission.DerivedInputs.Select(port => port.PortId));
            if (emission.Output.HasValue)
                declaredInputs.UnionWith(emission.Constants.Select(field => field.Key));
            var declaredOutputs = new HashSet<string>(outputs.Select(port => MapPort(node, port.ID, native, emission.Code, true)), StringComparer.Ordinal);
            declaredOutputs.UnionWith(emission.BlackboardOutputs.Select(port => port.PortId));
            foreach (SemanticDerivedInput input in emission.DerivedInputs)
                if (input.Source == SemanticDerivedInputSource.SelfOutput)
                    declaredOutputs.Add(contract.Outputs[input.SourceOutputPortIndex].Identity);
            if (!declaredInputs.SetEquals(contract.Inputs.Select(port => port.Identity)) ||
                !nativeSet && !declaredOutputs.SetEquals(contract.Outputs.Select(port => port.Identity)))
                throw new InvalidOperationException($"图节点 '{node.UID}' 的值端口与编译合同不匹配。");
            var required = node is IGraphOrchestrationRequiredInputAuthoring connections
                ? new HashSet<string>(connections.RequiredInputConnections, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var constants = new List<SimulationConstantInput>();
            foreach (ValueInput port in inputs.OrderBy(value => value.ID, StringComparer.Ordinal))
            {
                string compiled = MapPort(node, port.ID, native, emission.Code, false);
                OperationValuePortDefinition definition = contract.RequireInput(compiled);
                SemanticValueKind kind = AuthoringKind(port.type, definition, emission);
                if (!definition.Accepts(kind))
                    throw new InvalidOperationException($"图节点 '{node.UID}' 端口 '{port.ID}' 的类型不匹配。");
                if (port.isConnected)
                    continue;
                if (required.Contains(port.ID))
                    throw new InvalidOperationException($"图节点 '{node.UID}' 必须连接目标输入 '{port.ID}'。");
                constants.Add(new SimulationConstantInput(compiled, kind,
                    BtsmtlGraphValueTypes.AuthoringValue(port.type, port.serializedValue),
                    Source(graphId, route, hash, node, port.ID)));
            }
            if (!nativeSet)
                foreach (ValueOutput port in outputs)
                {
                    OperationValuePortDefinition definition = contract.RequireSelection(MapPort(node, port.ID, native, emission.Code, true));
                    if (!definition.Accepts(AuthoringKind(port.type, definition, emission)))
                        throw new InvalidOperationException($"图节点 '{node.UID}' 输出 '{port.ID}' 的类型不匹配。");
                }
            using (s_EmitOperation.Auto())
                return m_Emitter.Emit(Source(graphId, route, hash, node), emission, constants, describe);
        }

        static OperationValuePortContract PortContract(SimulationNodeEmission emission)
        {
            if (emission.Extension is OrchestrationExtensionDefinition extension)
                return OrchestrationValuePortContracts.ForExtension(extension.Contract.Kind, extension.Contract.Ports.Span,
                    emission.ConstantOutputs.Select((port, index) => new OperationValuePortDefinition(port.PortId,
                        index, OperationValuePortConstraint.Fixed, port.ValueKind)).ToArray());
            if (emission.Output is OrchestrationOutputTemplate output)
                return OrchestrationValuePortContracts.ForOutput(output);
            if (emission.Code == OrchestrationOperationCode.HostInput)
                return OrchestrationValuePortContracts.ForHostInputs(emission.HostInputs);
            return OrchestrationValuePortContracts.Require(emission.Code, emission.Integer0);
        }

        static SemanticValueKind AuthoringKind(Type type, OperationValuePortDefinition definition,
            SimulationNodeEmission emission) => type == typeof(float) &&
            definition.FixedKind == SemanticValueKind.Yaw &&
            (emission.Extension.HasValue || emission.HostInputs.Count != 0)
                ? SemanticValueKind.Yaw : BtsmtlGraphValueTypes.Kind(type);

        public void BindDefaultInput(OperationHandle operation, string compiledPort, ValueInput port,
            SimulationSourceLocation source)
        {
            int constant = m_Builder.DeclareConstant(source, "default-value",
                BtsmtlGraphValueTypes.AuthoringValue(port.type, port.serializedValue));
            if (constant >= 0)
            {
                m_Builder.DeclareConstantInputBinding(operation, compiledPort, constant,
                    BtsmtlGraphValueTypes.Kind(port.type), source);
                m_Bindings?.BindInput(source.GraphId, source.NodeId, port.ID, constant);
            }
        }

        public void PublishPortSources(string graphId, string route, string hash, IReadOnlyList<FlowNode> nodes,
            BtsmtlGraphOperationBindings operations)
        {
            foreach (FlowNode node in nodes)
            {
                foreach (FlowInput port in node.GetInputFlowPorts())
                    m_Builder.DeclareOperationPortSource(operations.FlowInput(node.UID, port.ID),
                        Source(graphId, route, hash, node, port.ID), string.Empty, ProgramValuePortDirection.None, isInputPort: true);
                foreach (FlowOutput port in node.GetOutputFlowPorts())
                    m_Builder.DeclareOperationPortSource(operations.FlowOutput(node.UID, port.ID),
                        Source(graphId, route, hash, node, port.ID), string.Empty, ProgramValuePortDirection.None, isInputPort: false);
                foreach (ValueInput port in node.GetInputValuePorts())
                {
                    BtsmtlGraphValuePortBinding binding = operations.Input(node.UID, port.ID);
                    m_Builder.DeclareOperationPortSource(binding.Operation, Source(graphId, route, hash, node, port.ID),
                        binding.PortId, ProgramValuePortDirection.Input);
                }
                foreach (ValueOutput port in node.GetOutputValuePorts())
                {
                    BtsmtlGraphValuePortBinding binding = operations.Output(node.UID, port.ID);
                    m_Builder.DeclareOperationPortSource(binding.Operation, Source(graphId, route, hash, node, port.ID),
                        binding.PortId, ProgramValuePortDirection.Output);
                }
            }
        }

        public void EmitEdge(string graphId, string route, string hash, BinderConnection edge,
            BtsmtlGraphOperationBindings operations, ProgramControlFlowKind flowKind, int order, int priority,
            OrchestrationAbortPolicy abortPolicy, OperationHandle condition)
        {
            bool value = edge.sourcePort is ValueOutput;
            if (value && (edge.sourcePort.type.IsEnum || edge.targetPort.type.IsEnum) &&
                edge.sourcePort.type != edge.targetPort.type)
                throw new InvalidOperationException($"枚举连线 '{edge.UID}' 必须连接同一枚举类型。");
            BtsmtlGraphValuePortBinding from = value ? operations.Output(edge.sourceNode.UID, edge.sourcePortID) : default;
            BtsmtlGraphValuePortBinding to = value ? operations.Input(edge.targetNode.UID, edge.targetPortID) : default;
            m_Builder.DeclareControlFlow(route,
                value ? from.Operation : operations.FlowOutput(edge.sourceNode.UID, edge.sourcePortID),
                value ? to.Operation : operations.FlowInput(edge.targetNode.UID, edge.targetPortID),
                value ? from.PortId : edge.sourcePortID, value ? to.PortId : edge.targetPortID,
                value ? ProgramControlFlowKind.Value : flowKind, order, priority, abortPolicy,
                condition.IsValid, condition,
                new SimulationSourceLocation(edge.GetType().FullName, graphId, string.Empty,
                    edge.UID, string.Empty, string.Empty, route, contentHash: hash));
        }

        public static string Input(FlowNode node, string port, BtsmtlNativeValueNodeDefinition native) =>
            native != null ? native.Input(port) : node is IBtsmtlVariableFlowNode ? "m_Value"
                : BtsmtlGraphDomainNodes.RuntimePort(node, port, OrchestrationExtensionPortDirection.Input);

        public static string Output(FlowNode node, string port, BtsmtlNativeValueNodeDefinition native,
            OrchestrationOperationCode code) => native != null ? native.Output(port)
                : node is IBtsmtlVariableFlowNode || code == OrchestrationOperationCode.HostInput &&
                    node is not IGraphOrchestrationHostInputAuthoring ? "m_Output"
                    : BtsmtlGraphDomainNodes.RuntimePort(node, port, OrchestrationExtensionPortDirection.Output);

        string MapPort(FlowNode node, string port, BtsmtlNativeValueNodeDefinition native,
            OrchestrationOperationCode code, bool output) => native != null || m_Bindings == null
                ? output ? Output(node, port, native, code) : Input(node, port, native)
                : m_Bindings.MapPort(node, port, output
                    ? OrchestrationExtensionPortDirection.Output : OrchestrationExtensionPortDirection.Input);

        public static SimulationSourceLocation Source(string graphId, string route, string hash,
            FlowNode node, string portId = "") =>
            new(node.GetType().FullName, graphId, node.UID, string.Empty, string.Empty, string.Empty,
                string.IsNullOrEmpty(portId) ? $"{route}/node:{node.UID}" : $"{route}/node:{node.UID}/port:{portId}",
                portId: portId, contentHash: hash);
    }
}
