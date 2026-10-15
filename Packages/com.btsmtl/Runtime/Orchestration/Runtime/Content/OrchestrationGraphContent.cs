using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationGraphContent<TValue> where TValue : unmanaged
    {
        public OrchestrationGraphContent(GraphExecutionIdentity identity,
            SimulationNumericProfile numericProfile, OrchestrationIdentityTable identities,
            OrchestrationProgramContent<TValue> program,
            ReadOnlyMemory<OrchestrationExtensionReference> extensions,
            ReadOnlyMemory<OrchestrationOperationContent> operationContents,
            ReadOnlyMemory<OrchestrationGraphEntry> entries,
            ReadOnlyMemory<OrchestrationGraphVariable> variables,
            ReadOnlyMemory<OrchestrationGraphBlackboardBinding> generatedBlackboards,
            ReadOnlyMemory<OrchestrationGraphHostInput> hostInputs,
            ReadOnlyMemory<OrchestrationGraphPort> ports,
            ReadOnlyMemory<OrchestrationGraphSource> sources)
        {
            Identity = identity;
            NumericProfile = numericProfile;
            Identities = identities;
            Program = program;
            Extensions = extensions;
            OperationContents = operationContents;
            Entries = entries;
            Variables = variables;
            GeneratedBlackboards = generatedBlackboards;
            HostInputs = hostInputs;
            Ports = ports;
            Sources = sources;
        }

        public GraphExecutionIdentity Identity { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public OrchestrationIdentityTable Identities { get; }
        public OrchestrationProgramContent<TValue> Program { get; }
        public ReadOnlyMemory<OrchestrationExtensionReference> Extensions { get; }
        public ReadOnlyMemory<OrchestrationOperationContent> OperationContents { get; }
        public ReadOnlyMemory<OrchestrationGraphEntry> Entries { get; }
        public ReadOnlyMemory<OrchestrationGraphVariable> Variables { get; }
        public ReadOnlyMemory<OrchestrationGraphBlackboardBinding> GeneratedBlackboards { get; }
        public ReadOnlyMemory<OrchestrationGraphHostInput> HostInputs { get; }
        public ReadOnlyMemory<OrchestrationGraphPort> Ports { get; }
        public ReadOnlyMemory<OrchestrationGraphSource> Sources { get; }
    }
}
