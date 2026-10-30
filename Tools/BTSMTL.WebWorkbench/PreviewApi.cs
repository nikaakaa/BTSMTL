namespace BTSMTL.WebWorkbench;

public interface IPreviewHost<out TSourceCatalog, out TInputCatalog>
{
    PreviewHostDescription Describe();
    TSourceCatalog Sources();
    TInputCatalog Inputs(string target, string revision);
}

public static class PreviewApi
{
    public static void MapPreviewApi(this WebApplication app) =>
        app.MapGet("/api/preview/host", () => new { Available = false, Description = (PreviewHostDescription?)null });

    public static void MapPreviewApi<TSourceCatalog, TInputCatalog>(this WebApplication app,
        IPreviewHost<TSourceCatalog, TInputCatalog> host)
    {
        RouteGroupBuilder routes = app.MapGroup("/api/preview");
        routes.MapGet("/host", () => new { Available = true, Description = host.Describe() });
        routes.MapGet("/sources", host.Sources);
        routes.MapGet("/inputs", (string scene, string revision) => host.Inputs(scene, revision));
    }
}
