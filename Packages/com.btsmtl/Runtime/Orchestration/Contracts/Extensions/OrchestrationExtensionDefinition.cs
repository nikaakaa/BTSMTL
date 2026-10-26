namespace BTSMTL.Orchestration
{
    public interface IOrchestrationExtensionDefinition
    {
        OrchestrationExtensionDefinition Declaration { get; }
    }

    public sealed class OrchestrationExtensionDefinition<TOperation> : IOrchestrationExtensionDefinition
        where TOperation : struct
    {
        readonly OrchestrationExtensionDefinition m_Declaration;

        public OrchestrationExtensionDefinition(OrchestrationExtensionIdentity identity,
            uint semanticVersion, OrchestrationExtensionContract contract)
        {
            m_Declaration = new OrchestrationExtensionDefinition(identity, semanticVersion, contract);
        }

        public OrchestrationExtensionDefinition Declaration => m_Declaration;
        public OrchestrationExtensionIdentity Identity => m_Declaration.Identity;
        public uint SemanticVersion => m_Declaration.SemanticVersion;
        public OrchestrationExtensionContract Contract => m_Declaration.Contract;

        public OrchestrationExtensionReference CreateReference(int contentIndex, string sourcePath) =>
            m_Declaration.CreateReference(contentIndex, sourcePath);
    }

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
                Contract.Kind, Contract.Access, Contract.Ports, Contract.Fields, Contract.Invocations,
                Contract.Completion, sourcePath);
    }
}
