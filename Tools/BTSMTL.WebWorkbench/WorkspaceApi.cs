using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

public static class WorkspaceApi
{
    public static void MapWorkspaceApi(this WebApplication app, AuthoringWorkspaceDirectory workspace)
    {
        app.MapGet("/api/workspace", () => new
        {
            path = workspace.Path,
            documentFormat = AuthoringRootDocument.FormatVersion,
            source = "authoring-documents"
        });
        app.MapGet("/api/workspace/roots", (int offset, int limit) => workspace.List(offset, limit));
        app.MapGet("/api/workspace/root", (string path) =>
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
        app.MapGet("/api/workspace/references", (string rootId, int offset, int limit) =>
            workspace.Referencing(rootId, offset, limit));
    }
}
