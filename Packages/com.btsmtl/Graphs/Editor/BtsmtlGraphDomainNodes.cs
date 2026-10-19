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
    // 领域节点的编译描述：宿主输入、编排输出与扩展操作，技能图与通用图共用。
    public static class BtsmtlGraphDomainNodes
    {
        public static string RuntimePort(FlowNode node, string port, OrchestrationExtensionPortDirection direction)
        {
            if (direction == OrchestrationExtensionPortDirection.Output &&
                node is IGraphOrchestrationConstantPortAuthoring constants &&
                constants.ConstantOutputs.Any(output => output.PortId == port))
                return port;
            if (GraphOrchestrationExtensionNodes.TryGet(node.GetType(), out GraphOrchestrationExtensionNodes.Contract extension))
                return extension.Ports.Single(binding =>
                    binding.AuthoringPortId == port && binding.RuntimePort.Direction == direction).RuntimePort.Id;
            return port;
        }

        public static bool TryDescribe(FlowNode node, out SimulationNodeEmission emission)
        {
            if (node is IGraphOrchestrationHostInputAuthoring host)
            {
                emission = new SimulationNodeEmission(OrchestrationOperationCode.HostInput,
                    hostInputs: host.HostInputs.Select(value => new SemanticHostInputPort(value.OutputPortId,
                        value.InputId, OrchestrationValuePortContracts.FromOrchestrationKind(value.ValueKind), value.ValueTypeIdentity)));
                return true;
            }
            if (node is IGraphOrchestrationOutputAuthoring output)
            {
                emission = new SimulationNodeEmission(OrchestrationOperationCode.EmitOutput,
                    constants: output.DescribeOrchestrationPayload(), output: output.OutputTemplate);
                return true;
            }
            if (GraphOrchestrationExtensionNodes.TryGet(node.GetType(), out GraphOrchestrationExtensionNodes.Contract extension))
            {
                var payload = (IGraphOrchestrationPayloadAuthoring)node;
                var blackboards = node as IGraphOrchestrationBlackboardAuthoring;
                var constants = node as IGraphOrchestrationConstantPortAuthoring;
                var derived = node as IGraphOrchestrationDerivedInputAuthoring;
                emission = new SimulationNodeEmission(OrchestrationOperationCode.Extension,
                    constants: payload.DescribeOrchestrationPayload(), extension: extension.Definition,
                    blackboardInputs: Blackboards(node, blackboards?.ReadBindings, OrchestrationExtensionPortDirection.Input),
                    blackboardOutputs: Blackboards(node, blackboards?.WriteBindings, OrchestrationExtensionPortDirection.Output),
                    constantInputs: Constants(node, constants?.ConstantInputs, OrchestrationExtensionPortDirection.Input),
                    constantOutputs: Constants(node, constants?.ConstantOutputs, OrchestrationExtensionPortDirection.Output),
                    derivedInputs: Derived(node, derived?.DerivedInputs));
                return true;
            }
            emission = default;
            return false;
        }

        static IEnumerable<SemanticBlackboardPortBinding> Blackboards(FlowNode node,
            IReadOnlyList<GraphOrchestrationBlackboardPort> ports, OrchestrationExtensionPortDirection direction) =>
            ports == null ? Array.Empty<SemanticBlackboardPortBinding>() : ports.Select(port =>
                new SemanticBlackboardPortBinding(RuntimePort(node, port.PortId, direction), port.OwnerId, port.DeclarationId,
                    OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));

        static IEnumerable<SemanticConstantPortDeclaration> Constants(FlowNode node,
            IReadOnlyList<GraphOrchestrationConstantPort> ports, OrchestrationExtensionPortDirection direction) =>
            ports == null ? Array.Empty<SemanticConstantPortDeclaration>() : ports.Select(port =>
                new SemanticConstantPortDeclaration(RuntimePort(node, port.PortId, direction), port.PayloadFieldName,
                    OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));

        static IEnumerable<SemanticDerivedInput> Derived(FlowNode node, IReadOnlyList<GraphOrchestrationDerivedInput> ports) =>
            ports == null ? Array.Empty<SemanticDerivedInput>() : ports.Select(port =>
                new SemanticDerivedInput(RuntimePort(node, port.PortId, OrchestrationExtensionPortDirection.Input),
                    (SemanticDerivedInputSource)port.Source, port.SourceOutputPortIndex,
                    OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));
    }
}
