using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

// 由项目提供的能力描述完成整根领域接收；默认定义版本由项目根文档合同给出。
public sealed class AuthoringDefinitionDocumentDomain(AuthoringDefinitionCatalog definitions, string definitionVersion)
    : IAuthoringDocumentDomain
{
    public GraphAuthoringDomainId DomainId => new(definitions.Read().Domain);
    public string DefinitionVersion => definitionVersion;

    public void Receive(AuthoringDocumentSet documents) =>
        _ = new AuthoringDocumentReception(documents, new AuthoringDefinitionSet(definitions.Read()),
            reference => reference.RootId.Equals(documents.Root.Id)
                ? documents.Require<AuthoringGraphDocument>(reference.DocumentId)
                : throw new InvalidOperationException(
                    $"图引用 '{reference.RootId}/{reference.DocumentId}' 不属于当前根，整根接收不读取其他根。"));
}
