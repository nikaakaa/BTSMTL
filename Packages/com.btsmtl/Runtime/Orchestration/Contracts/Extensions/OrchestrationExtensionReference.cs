namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExtensionReference
    {
        public OrchestrationExtensionReference(OrchestrationExtensionIdentity identity,
            uint semanticVersion, int contentIndex,
            OrchestrationExtensionInvocationKind requiredInvocations, string sourcePath)
        {
            Identity = identity;
            SemanticVersion = semanticVersion;
            ContentIndex = contentIndex;
            RequiredInvocations = requiredInvocations;
            SourcePath = sourcePath;
        }

        public OrchestrationExtensionIdentity Identity { get; }
        public uint SemanticVersion { get; }
        public int ContentIndex { get; }
        public OrchestrationExtensionInvocationKind RequiredInvocations { get; }
        public string SourcePath { get; }
    }
}
