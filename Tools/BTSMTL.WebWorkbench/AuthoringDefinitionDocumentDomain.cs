using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

public sealed class AuthoringDefinitionDocumentDomain(AuthoringDefinitionCatalog definitions, string definitionVersion)
    : IAuthoringDocumentDomain
{
    public AuthoringDocumentAcceptance Receive(AuthoringDocumentSet documents, string? expectedDefinitionsRevision)
    {
        AuthoringDefinitionSnapshot source = definitions.Read();
        if (expectedDefinitionsRevision != null && expectedDefinitionsRevision != source.Revision)
            throw new AuthoringDefinitionsConflictException(expectedDefinitionsRevision, source.Revision);
        if (!documents.Root.DomainId.Equals(new GraphAuthoringDomainId(source.Domain)) ||
            documents.Root.DefinitionVersion != definitionVersion)
            throw new InvalidOperationException("作者根的领域或默认定义版本与当前正式合同不一致。");
        _ = new AuthoringDocumentReception(documents, new AuthoringDefinitionSet(source),
            reference => reference.RootId.Equals(documents.Root.Id)
                ? documents.Require<AuthoringGraphDocument>(reference.DocumentId)
                : throw new InvalidOperationException(
                    $"图引用 '{reference.RootId}/{reference.DocumentId}' 不属于当前根，整根接收不读取其他根。"));
        return new(documents, AuthoringDocumentAcceptanceStage.Structure, source.Revision, new[] { source.Source });
    }
}
