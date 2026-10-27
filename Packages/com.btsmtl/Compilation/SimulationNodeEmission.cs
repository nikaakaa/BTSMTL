using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public readonly struct SimulationNodeEmission
    {
        public SimulationNodeEmission(
            OrchestrationOperationCode code,
            int integer0 = 0,
            int integer1 = 0,
            ulong unsigned0 = 0,
            string text0 = null,
            uint flags = 0,
            IEnumerable<KeyValuePair<string, object>> constants = null,
            OrchestrationExtensionDefinition? extension = null,
            OrchestrationOutputTemplate? output = null,
            IEnumerable<SemanticHostInputPort> hostInputs = null,
            IEnumerable<SemanticBlackboardPortBinding> blackboardInputs = null,
            IEnumerable<SemanticBlackboardPortBinding> blackboardOutputs = null,
            IEnumerable<SemanticConstantPortDeclaration> constantInputs = null,
            IEnumerable<SemanticConstantPortDeclaration> constantOutputs = null,
            IEnumerable<SemanticDerivedInput> derivedInputs = null,
            IEnumerable<string> requiredInputConnections = null)
        {
            Code = code;
            Integer0 = integer0;
            Integer1 = integer1;
            Unsigned0 = unsigned0;
            Text0 = text0 ?? string.Empty;
            Flags = flags;
            Extension = extension;
            Output = output;
            HostInputs = hostInputs == null ? Array.Empty<SemanticHostInputPort>() : hostInputs.ToArray();
            BlackboardInputs = blackboardInputs == null ? Array.Empty<SemanticBlackboardPortBinding>() : blackboardInputs.ToArray();
            BlackboardOutputs = blackboardOutputs == null ? Array.Empty<SemanticBlackboardPortBinding>() : blackboardOutputs.ToArray();
            ConstantInputs = constantInputs == null ? Array.Empty<SemanticConstantPortDeclaration>() : constantInputs.ToArray();
            ConstantOutputs = constantOutputs == null ? Array.Empty<SemanticConstantPortDeclaration>() : constantOutputs.ToArray();
            DerivedInputs = derivedInputs == null ? Array.Empty<SemanticDerivedInput>() : derivedInputs.ToArray();
            RequiredInputConnections = requiredInputConnections == null ? Array.Empty<string>() : requiredInputConnections.ToArray();
            Constants = constants == null
                ? Array.Empty<KeyValuePair<string, object>>()
                : extension.HasValue
                    ? SimulationNodeEmissionFields.ExtensionFields(extension.Value, constants)
                    : constants.ToArray();
        }

        public OrchestrationOperationCode Code { get; }
        public OrchestrationExtensionDefinition? Extension { get; }
        public OrchestrationOutputTemplate? Output { get; }
        public IReadOnlyList<SemanticHostInputPort> HostInputs { get; }
        public IReadOnlyList<SemanticBlackboardPortBinding> BlackboardInputs { get; }
        public IReadOnlyList<SemanticBlackboardPortBinding> BlackboardOutputs { get; }
        public IReadOnlyList<SemanticConstantPortDeclaration> ConstantInputs { get; }
        public IReadOnlyList<SemanticConstantPortDeclaration> ConstantOutputs { get; }
        public IReadOnlyList<SemanticDerivedInput> DerivedInputs { get; }
        public IReadOnlyList<string> RequiredInputConnections { get; }
        public int Integer0 { get; }
        public int Integer1 { get; }
        public ulong Unsigned0 { get; }
        public string Text0 { get; }
        public uint Flags { get; }
        public IReadOnlyList<KeyValuePair<string, object>> Constants { get; }
    }
}
