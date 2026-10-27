using System;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public enum AuthoringDocumentKind
    {
        Graph,
        Timeline,
        InputPlan,
        Layout,
        References
    }

    public sealed class AuthoringRootDocument
    {
        public const int FormatVersion = 2;

        public AuthoringRootDocument(GraphAuthoringElementId id, GraphAuthoringDomainId domainId,
            string kind, string definitionVersion, string displayName,
            ReadOnlyMemory<AuthoringNamedValue> fields, ReadOnlyMemory<AuthoringDocumentFile> files,
            ReadOnlyMemory<AuthoringContentReference> externalReferences,
            ReadOnlyMemory<AuthoringUnityOutput> unityOutputs)
        {
            Id = id;
            DomainId = domainId;
            Kind = kind;
            DefinitionVersion = definitionVersion;
            DisplayName = displayName;
            Fields = fields;
            Files = files;
            ExternalReferences = externalReferences;
            UnityOutputs = unityOutputs;
        }

        public GraphAuthoringElementId Id { get; }
        public GraphAuthoringDomainId DomainId { get; }
        public string Kind { get; }
        public string DefinitionVersion { get; }
        public string DisplayName { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
        public ReadOnlyMemory<AuthoringDocumentFile> Files { get; }
        public ReadOnlyMemory<AuthoringContentReference> ExternalReferences { get; }
        public ReadOnlyMemory<AuthoringUnityOutput> UnityOutputs { get; }
    }

    public readonly struct AuthoringDocumentFile
    {
        public AuthoringDocumentFile(GraphAuthoringElementId id, AuthoringDocumentKind kind, string path)
        {
            Id = id;
            Kind = kind;
            Path = path;
        }

        public GraphAuthoringElementId Id { get; }
        public AuthoringDocumentKind Kind { get; }
        public string Path { get; }
    }

    public readonly struct AuthoringUnityOutput
    {
        public AuthoringUnityOutput(GraphAuthoringElementId documentId, GraphAuthoringElementId elementId,
            string assetPath, string guid, long localFileId)
        {
            DocumentId = documentId;
            ElementId = elementId;
            AssetPath = assetPath;
            Guid = guid;
            LocalFileId = localFileId;
        }

        public GraphAuthoringElementId DocumentId { get; }
        public GraphAuthoringElementId ElementId { get; }
        public string AssetPath { get; }
        public string Guid { get; }
        public long LocalFileId { get; }
    }
}
