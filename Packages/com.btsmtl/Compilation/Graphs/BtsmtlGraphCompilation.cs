using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 图叶子、连线与端口来源的共同发射：技能图与通用图共用，只读编译视图。
    public sealed class BtsmtlGraphCompilation
    {
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

        public OperationHandle EmitLeaf(CompileNode node, string graphId, string route, string hash,
            SimulationNodeEmission emission)
        {
            OperationValuePortContract contract = PortContract(emission);
            IReadOnlyList<CompileValuePort> inputs = node.ValueInputs;
            IReadOnlyList<CompileValuePort> outputs = node.ValueOutputs;
            bool nativeSet = node.Role == CompileNodeRole.Variable && node.VariableWrites;
            var declaredInputs = new HashSet<string>(inputs.Select(port => port.CompiledId), StringComparer.Ordinal);
            declaredInputs.UnionWith(emission.BlackboardInputs.Select(port => port.PortId));
            declaredInputs.UnionWith(emission.ConstantInputs.Select(port => port.PortId));
            declaredInputs.UnionWith(emission.DerivedInputs.Select(port => port.PortId));
            if (emission.Output.HasValue)
                declaredInputs.UnionWith(emission.Constants.Select(field => field.Key));
            var declaredOutputs = new HashSet<string>(outputs.Select(port => port.CompiledId), StringComparer.Ordinal);
            declaredOutputs.UnionWith(emission.BlackboardOutputs.Select(port => port.PortId));
            foreach (SemanticDerivedInput input in emission.DerivedInputs)
                if (input.Source == SemanticDerivedInputSource.SelfOutput)
                    declaredOutputs.Add(contract.Outputs[input.SourceOutputPortIndex].Identity);
            if (!declaredInputs.SetEquals(contract.Inputs.Select(port => port.Identity)) ||
                !nativeSet && !declaredOutputs.SetEquals(contract.Outputs.Select(port => port.Identity)))
                throw new InvalidOperationException($"图节点 '{node.Id}' 的值端口与编译合同不匹配。");
            var required = new HashSet<string>(emission.RequiredInputConnections, StringComparer.Ordinal);
            var constants = new List<SimulationConstantInput>();
            foreach (CompileValuePort port in inputs.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                OperationValuePortDefinition definition = contract.RequireInput(port.CompiledId);
                SemanticValueKind kind = AuthoringKind(port.Type, definition, emission);
                if (!definition.Accepts(kind))
                    throw new InvalidOperationException($"图节点 '{node.Id}' 端口 '{port.Id}' 的类型不匹配。");
                if (port.IsConnected)
                    continue;
                if (required.Contains(port.Id))
                    throw new InvalidOperationException($"图节点 '{node.Id}' 必须连接目标输入 '{port.Id}'。");
                constants.Add(new SimulationConstantInput(port.CompiledId, kind,
                    port.Type.AuthoringValue(port.DefaultValue), Source(graphId, route, hash, node, port.Id)));
            }
            if (!nativeSet)
                foreach (CompileValuePort port in outputs)
                {
                    OperationValuePortDefinition definition = contract.RequireSelection(port.CompiledId);
                    if (!definition.Accepts(AuthoringKind(port.Type, definition, emission)))
                        throw new InvalidOperationException($"图节点 '{node.Id}' 输出 '{port.Id}' 的类型不匹配。");
                }
            return m_Emitter.Emit(Source(graphId, route, hash, node), emission, constants, true);
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

        static SemanticValueKind AuthoringKind(CompileValueType type, OperationValuePortDefinition definition,
            SimulationNodeEmission emission) => type.FullName == "System.Single" &&
            definition.FixedKind == SemanticValueKind.Yaw &&
            (emission.Extension.HasValue || emission.HostInputs.Count != 0)
                ? SemanticValueKind.Yaw : type.Kind;

        public void BindDefaultInput(OperationHandle operation, string compiledPort, CompileValuePort port,
            SimulationSourceLocation source)
        {
            int constant = m_Builder.DeclareConstant(source, "default-value", port.Type.AuthoringValue(port.DefaultValue));
            if (constant >= 0)
            {
                m_Builder.DeclareConstantInputBinding(operation, compiledPort, constant, port.Type.Kind, source);
                m_Bindings?.BindInput(source.GraphId, source.NodeId, port.Id, constant);
            }
        }

        public void PublishPortSources(string graphId, string route, string hash, IReadOnlyList<CompileNode> nodes,
            BtsmtlGraphOperationBindings operations)
        {
            foreach (CompileNode node in nodes)
            {
                foreach (string port in node.FlowInputs)
                    m_Builder.DeclareOperationPortSource(operations.FlowInput(node.Id, port),
                        Source(graphId, route, hash, node, port), string.Empty, ProgramValuePortDirection.None, isInputPort: true);
                foreach (string port in node.FlowOutputs)
                    m_Builder.DeclareOperationPortSource(operations.FlowOutput(node.Id, port),
                        Source(graphId, route, hash, node, port), string.Empty, ProgramValuePortDirection.None, isInputPort: false);
                foreach (CompileValuePort port in node.ValueInputs)
                {
                    BtsmtlGraphValuePortBinding binding = operations.Input(node.Id, port.Id);
                    m_Builder.DeclareOperationPortSource(binding.Operation, Source(graphId, route, hash, node, port.Id),
                        binding.PortId, ProgramValuePortDirection.Input);
                }
                foreach (CompileValuePort port in node.ValueOutputs)
                {
                    BtsmtlGraphValuePortBinding binding = operations.Output(node.Id, port.Id);
                    m_Builder.DeclareOperationPortSource(binding.Operation, Source(graphId, route, hash, node, port.Id),
                        binding.PortId, ProgramValuePortDirection.Output);
                }
            }
        }

        public void EmitEdge(string graphId, string route, string hash, CompileEdge edge,
            BtsmtlGraphOperationBindings operations, ProgramControlFlowKind flowKind, int order, int priority,
            OrchestrationAbortPolicy abortPolicy, OperationHandle condition)
        {
            bool value = edge.IsValue;
            if (value)
            {
                CompileValueType source = edge.Source.ValueOutputs.Single(port => port.Id == edge.SourcePortId).Type;
                CompileValueType target = edge.Target.Input(edge.TargetPortId).Type;
                if ((source.IsEnum || target.IsEnum) && !source.SameType(target))
                    throw new InvalidOperationException($"枚举连线 '{edge.Id}' 必须连接同一枚举类型。");
            }
            BtsmtlGraphValuePortBinding from = value ? operations.Output(edge.Source.Id, edge.SourcePortId) : default;
            BtsmtlGraphValuePortBinding to = value ? operations.Input(edge.TargetNodeId, edge.TargetPortId) : default;
            m_Builder.DeclareControlFlow(route,
                value ? from.Operation : operations.FlowOutput(edge.Source.Id, edge.SourcePortId),
                value ? to.Operation : operations.FlowInput(edge.TargetNodeId, edge.TargetPortId),
                value ? from.PortId : edge.SourcePortId, value ? to.PortId : edge.TargetPortId,
                value ? ProgramControlFlowKind.Value : flowKind, order, priority, abortPolicy,
                condition.IsValid, condition,
                new SimulationSourceLocation(edge.SourceType, graphId, string.Empty,
                    edge.Id, string.Empty, string.Empty, route, contentHash: hash));
        }

        public static SimulationSourceLocation Source(string graphId, string route, string hash,
            CompileNode node, string portId = "") =>
            new(node.SourceType, graphId, node.Id, string.Empty, string.Empty, string.Empty,
                string.IsNullOrEmpty(portId) ? $"{route}/node:{node.Id}" : $"{route}/node:{node.Id}/port:{portId}",
                portId: portId, contentHash: hash);
    }
}
