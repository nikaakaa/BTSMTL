using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 领域节点的编译描述：宿主输入、编排输出与扩展操作，技能图与通用图共用。
    // 编排声明来自节点能力，载荷与端口描述来自按能力登记的节点编译描述。
    public static class BtsmtlGraphDomainNodes
    {
        public static bool TryDescribe(GraphOrchestrationNodeDeclaration declaration,
            Func<GraphNodeCompileDescription> describe, out SimulationNodeEmission emission)
        {
            if (declaration.HostInputs != null)
            {
                emission = new SimulationNodeEmission(OrchestrationOperationCode.HostInput,
                    hostInputs: declaration.HostInputs.Select(value => new SemanticHostInputPort(value.OutputPortId,
                        value.InputId, OrchestrationValuePortContracts.FromOrchestrationKind(value.ValueKind), value.ValueTypeIdentity)));
                return true;
            }
            if (declaration.Output is OrchestrationOutputTemplate output)
            {
                emission = new SimulationNodeEmission(OrchestrationOperationCode.EmitOutput,
                    constants: describe().Payload, output: output);
                return true;
            }
            if (declaration.Extension is GraphOrchestrationExtensionContract extension)
            {
                GraphNodeCompileDescription description = describe();
                emission = new SimulationNodeEmission(OrchestrationOperationCode.Extension,
                    constants: description.Payload, extension: extension.Definition.WithAdditionalPorts(description.AdditionalPorts),
                    blackboardInputs: Blackboards(description.ReadBindings),
                    blackboardOutputs: Blackboards(description.WriteBindings),
                    constantInputs: Constants(description.ConstantInputs),
                    constantOutputs: Constants(description.ConstantOutputs),
                    derivedInputs: Derived(description.DerivedInputs),
                    requiredInputConnections: description.RequiredInputConnections);
                return true;
            }
            emission = default;
            return false;
        }

        static IEnumerable<SemanticBlackboardPortBinding> Blackboards(IReadOnlyList<GraphOrchestrationBlackboardPort> ports) =>
            ports.Select(port => new SemanticBlackboardPortBinding(port.PortId, port.OwnerId,
                port.DeclarationId, OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));

        static IEnumerable<SemanticConstantPortDeclaration> Constants(IReadOnlyList<GraphOrchestrationConstantPort> ports) =>
            ports.Select(port => new SemanticConstantPortDeclaration(port.PortId,
                port.PayloadFieldName, OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));

        static IEnumerable<SemanticDerivedInput> Derived(IReadOnlyList<GraphOrchestrationDerivedInput> ports) =>
            ports.Select(port => new SemanticDerivedInput(port.PortId,
                (SemanticDerivedInputSource)port.Source, port.SourceOutputPortIndex,
                OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));
    }
}
