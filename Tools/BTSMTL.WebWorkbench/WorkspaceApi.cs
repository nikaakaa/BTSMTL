using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

public static class WorkspaceApi
{
    public static void MapAuthoringReadApi(this WebApplication app, AuthoringWorkspaceDirectory workspace,
        string route, string source)
    {
        RouteGroupBuilder routes = app.MapGroup(route);
        routes.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception error) when (error is IOException or FormatException or
                Newtonsoft.Json.JsonException or ArgumentException)
            {
                return Results.UnprocessableEntity(new
                {
                    success = false,
                    stage = "source-read",
                    requestId = context.HttpContext.TraceIdentifier,
                    diagnostics = new[] { new AuthoringSourceProblem(
                        context.HttpContext.Request.Query["path"].ToString(), "authoring_input_invalid", error.Message) }
                });
            }
        });
        routes.MapGet("", () => new
        {
            path = workspace.Path,
            documentFormat = AuthoringRootDocument.FormatVersion,
            source
        });
        routes.MapGet("/roots", (int offset, int limit) => workspace.List(offset, limit));
        routes.MapGet("/source", (string path) => workspace.ReadSource(path));
        routes.MapGet("/root", (string path) =>
        {
            AuthoringRootFiles source = workspace.Read(path);
            string directory = Path.GetDirectoryName(source.Files[0].Path)!;
            return new
            {
                root = AuthoringDocumentJson.WriteRoot(source.Root),
                source.Revision,
                files = source.Files.Select(file => new
                {
                    path = Path.GetRelativePath(directory, file.Path).Replace('\\', '/'),
                    file.Revision,
                    content = file.Text
                })
            };
        });
        routes.MapGet("/references", (string rootId, int offset, int limit) =>
            workspace.Referencing(rootId, offset, limit));
    }
}
