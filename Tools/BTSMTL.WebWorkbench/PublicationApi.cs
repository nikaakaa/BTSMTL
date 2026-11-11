namespace BTSMTL.WebWorkbench;

public static class PublicationApi
{
    public static void MapPublicationApi(this WebApplication app, IAuthoringPublicationAdapter adapter)
    {
        app.MapGet("/api/authoring/documents/creation-options", (HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, null, adapter.CreationOptions));
        app.MapPost("/api/authoring/documents/create", (AuthoringDocumentCreationRequest input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { input.Type }, () => adapter.Create(input)));
        app.MapPost("/api/authoring/documents/delete", (string documentPath, AuthoringDocumentDeleteRequest input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { documentPath }, () => adapter.Delete(documentPath, input)));
        app.MapGet("/api/authoring/publication/scopes", (HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, null, adapter.Scopes));
        app.MapPost("/api/authoring/documents/exports/{kind}", (string documentPath, string kind, AuthoringDocumentExportRequest input,
            HttpContext context, CancellationToken cancellation) =>
            AuthoringQueryResults.ReadAsync(context, new { documentPath, kind }, () => adapter.Export(documentPath, kind, input, cancellation)));
        app.MapGet("/api/authoring/documents/exports/files/{kind}/{key}/{**path}",
            (string documentPath, string kind, string key, string path, string? name) =>
            {
                AuthoringExportDownload file = adapter.Download(documentPath, kind, key, path, name);
                return Results.File(file.Path, file.ContentType, file.Name, enableRangeProcessing: true);
            });
    }
}
