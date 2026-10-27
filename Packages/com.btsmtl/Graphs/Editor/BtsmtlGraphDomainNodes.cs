using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using FlowCanvas;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs.Editor
{
    // 宿主按节点能力取得编译描述：节点字段先读成文档作者值，再交给按 CompilerBindingId 登记的描述。
    public interface IBtsmtlGraphNodeCompileSource
    {
        GraphNodeCompileDescription Describe(FlowNode node);
    }

    // 领域节点的编译描述：宿主输入、编排输出与扩展操作，技能图与通用图共用。
    public static class BtsmtlGraphDomainNodes
    {
        // 扩展端口按作者端口映射到运行端口；未映射的作者端口（常量输出）保持原身份，由编译合同核对。
        public static string RuntimePort(FlowNode node, string port, OrchestrationExtensionPortDirection direction)
        {
            if (!GraphOrchestrationExtensionNodes.TryGet(node.GetType(), out GraphOrchestrationExtensionNodes.Contract extension))
                return port;
            foreach (GraphOrchestrationExtensionPortBinding binding in extension.Ports)
                if (binding.AuthoringPortId == port && binding.RuntimePort.Direction == direction)
                    return binding.RuntimePort.Id;
            return port;
        }

        public static bool TryDescribe(FlowNode node, IBtsmtlGraphNodeCompileSource nodes, out SimulationNodeEmission emission)
        {
            Type type = node.GetType();
            if (GraphOrchestrationOutputNodes.TryGetHostInputs(type, out IReadOnlyList<GraphOrchestrationHostInputPort> hostInputs))
            {
                emission = new SimulationNodeEmission(OrchestrationOperationCode.HostInput,
                    hostInputs: hostInputs.Select(value => new SemanticHostInputPort(value.OutputPortId,
                        value.InputId, OrchestrationValuePortContracts.FromOrchestrationKind(value.ValueKind), value.ValueTypeIdentity)));
                return true;
            }
            if (GraphOrchestrationOutputNodes.TryGetOutput(type, out OrchestrationOutputTemplate output))
            {
                emission = new SimulationNodeEmission(OrchestrationOperationCode.EmitOutput,
                    constants: nodes.Describe(node).Payload, output: output);
                return true;
            }
            if (GraphOrchestrationExtensionNodes.TryGet(type, out GraphOrchestrationExtensionNodes.Contract extension))
            {
                GraphNodeCompileDescription description = nodes.Describe(node);
                emission = new SimulationNodeEmission(OrchestrationOperationCode.Extension,
                    constants: description.Payload, extension: extension.Definition,
                    blackboardInputs: Blackboards(node, description.ReadBindings, OrchestrationExtensionPortDirection.Input),
                    blackboardOutputs: Blackboards(node, description.WriteBindings, OrchestrationExtensionPortDirection.Output),
                    constantInputs: Constants(node, description.ConstantInputs, OrchestrationExtensionPortDirection.Input),
                    constantOutputs: Constants(node, description.ConstantOutputs, OrchestrationExtensionPortDirection.Output),
                    derivedInputs: Derived(node, description.DerivedInputs),
                    requiredInputConnections: description.RequiredInputConnections);
                return true;
            }
            emission = default;
            return false;
        }

        static IEnumerable<SemanticBlackboardPortBinding> Blackboards(FlowNode node,
            IReadOnlyList<GraphOrchestrationBlackboardPort> ports, OrchestrationExtensionPortDirection direction) =>
            ports.Select(port => new SemanticBlackboardPortBinding(RuntimePort(node, port.PortId, direction), port.OwnerId,
                port.DeclarationId, OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));

        static IEnumerable<SemanticConstantPortDeclaration> Constants(FlowNode node,
            IReadOnlyList<GraphOrchestrationConstantPort> ports, OrchestrationExtensionPortDirection direction) =>
            ports.Select(port => new SemanticConstantPortDeclaration(RuntimePort(node, port.PortId, direction),
                port.PayloadFieldName, OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));

        static IEnumerable<SemanticDerivedInput> Derived(FlowNode node, IReadOnlyList<GraphOrchestrationDerivedInput> ports) =>
            ports.Select(port => new SemanticDerivedInput(RuntimePort(node, port.PortId, OrchestrationExtensionPortDirection.Input),
                (SemanticDerivedInputSource)port.Source, port.SourceOutputPortIndex,
                OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));
    }
}
