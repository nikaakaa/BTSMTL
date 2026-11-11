namespace BTSMTL.WebWorkbench;

public interface IAuthoringPublicationAdapter
{
    Task<AuthoringQueryResult> CreationOptions();
    Task<AuthoringQueryResult> Create(AuthoringDocumentCreationRequest input);
    Task<AuthoringQueryResult> Delete(string documentPath, AuthoringDocumentDeleteRequest input);
    Task<AuthoringQueryResult> Scopes();
    Task<AuthoringQueryResult> Export(string documentPath, string kind, AuthoringDocumentExportRequest input,
        CancellationToken cancellation);
    AuthoringExportDownload Download(string documentPath, string kind, string key, string path, string? name);
}
