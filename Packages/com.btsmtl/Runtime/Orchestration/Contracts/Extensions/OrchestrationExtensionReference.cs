using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationExtensionReference
    {
        public OrchestrationExtensionReference(OrchestrationExtensionIdentity identity,
            uint semanticVersion, int contentIndex,
            OrchestrationExtensionKind kind, OrchestrationExtensionAccess access,
            ReadOnlyMemory<OrchestrationExtensionPort> ports,
            OrchestrationExtensionInvocationKind requiredInvocations, string sourcePath)
        {
            Identity = identity;
            SemanticVersion = semanticVersion;
            ContentIndex = contentIndex;
            Kind = kind;
            Access = access;
            Ports = ports;
            RequiredInvocations = requiredInvocations;
            SourcePath = sourcePath;
        }

        public OrchestrationExtensionIdentity Identity { get; }
        public uint SemanticVersion { get; }
        public int ContentIndex { get; }
        public OrchestrationExtensionKind Kind { get; }
        public OrchestrationExtensionAccess Access { get; }
        public ReadOnlyMemory<OrchestrationExtensionPort> Ports { get; }
        public OrchestrationExtensionInvocationKind RequiredInvocations { get; }
        public string SourcePath { get; }
    }
}
