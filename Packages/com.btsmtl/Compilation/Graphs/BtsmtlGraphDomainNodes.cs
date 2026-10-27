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
                    constants: description.Payload, extension: extension.Definition,
                    blackboardInputs: Blackboards(declaration, description.ReadBindings, OrchestrationExtensionPortDirection.Input),
                    blackboardOutputs: Blackboards(declaration, description.WriteBindings, OrchestrationExtensionPortDirection.Output),
                    constantInputs: Constants(declaration, description.ConstantInputs, OrchestrationExtensionPortDirection.Input),
                    constantOutputs: Constants(declaration, description.ConstantOutputs, OrchestrationExtensionPortDirection.Output),
                    derivedInputs: Derived(declaration, description.DerivedInputs),
                    requiredInputConnections: description.RequiredInputConnections);
                return true;
            }
            emission = default;
            return false;
        }

        static IEnumerable<SemanticBlackboardPortBinding> Blackboards(GraphOrchestrationNodeDeclaration declaration,
            IReadOnlyList<GraphOrchestrationBlackboardPort> ports, OrchestrationExtensionPortDirection direction) =>
            ports.Select(port => new SemanticBlackboardPortBinding(declaration.RuntimePort(port.PortId, direction), port.OwnerId,
                port.DeclarationId, OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));

        static IEnumerable<SemanticConstantPortDeclaration> Constants(GraphOrchestrationNodeDeclaration declaration,
            IReadOnlyList<GraphOrchestrationConstantPort> ports, OrchestrationExtensionPortDirection direction) =>
            ports.Select(port => new SemanticConstantPortDeclaration(declaration.RuntimePort(port.PortId, direction),
                port.PayloadFieldName, OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));

        static IEnumerable<SemanticDerivedInput> Derived(GraphOrchestrationNodeDeclaration declaration,
            IReadOnlyList<GraphOrchestrationDerivedInput> ports) =>
            ports.Select(port => new SemanticDerivedInput(declaration.RuntimePort(port.PortId, OrchestrationExtensionPortDirection.Input),
                (SemanticDerivedInputSource)port.Source, port.SourceOutputPortIndex,
                OrchestrationValuePortContracts.FromOrchestrationKind(port.ValueKind), port.ValueTypeIdentity));
    }
}
