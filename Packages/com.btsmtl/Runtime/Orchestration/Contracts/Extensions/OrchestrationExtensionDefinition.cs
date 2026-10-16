namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExtensionDefinition
    {
        public OrchestrationExtensionDefinition(OrchestrationExtensionIdentity identity,
            uint semanticVersion, OrchestrationExtensionContract contract)
        {
            Identity = identity;
            SemanticVersion = semanticVersion;
            Contract = contract;
        }

        public OrchestrationExtensionIdentity Identity { get; }
        public uint SemanticVersion { get; }
        public OrchestrationExtensionContract Contract { get; }

        public OrchestrationExtensionReference CreateReference(int contentIndex, string sourcePath) =>
            new OrchestrationExtensionReference(Identity, SemanticVersion, contentIndex,
                Contract.Kind, Contract.Access, Contract.Ports, Contract.Fields, Contract.Invocations, sourcePath);
    }
}
