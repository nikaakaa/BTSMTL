using System.Net.WebSockets;

namespace BTSMTL.WebWorkbench;

public sealed record PreviewImageSourceDescription(string DisplayName, string FrameDisplayName);
public sealed record PreviewImageMetadata(string RunId, string HostFrame, string? ContentRevision,
    string Alignment, PreviewField[] Fields, string RawJson);
public sealed record PreviewImageFile(string Path, string MediaType, PreviewImageMetadata Metadata);
public sealed record PreviewImageBuild(string BuildId, string DisplayName, PreviewField[] Fields, string RawJson);

public interface IPreviewImageSource
{
    PreviewImageSourceDescription Describe();
    PreviewImageBuild[] Builds();
    PreviewImageFile Read(Guid runId, Guid sourceId, string hostFrame);
    void RequireLive(Guid id);
    Task View(Guid id, WebSocket socket);
}

public static class PreviewImageApi
{
    public static void MapPreviewImageApi(this WebApplication app, IPreviewImageSource? source)
    {
        app.MapGet("/api/preview/images", () => new { Available = source != null, Description = source?.Describe() });
        if (source == null) return;
        app.MapGet("/api/preview/players", () => new { Items = source.Builds() });
        app.MapGet("/api/preview/runs/{runId:guid}/player/{sourceId:guid}/frames/{hostFrame}/metadata",
            (Guid runId, Guid sourceId, string hostFrame) => source.Read(runId, sourceId, hostFrame).Metadata);
        app.MapGet("/api/preview/runs/{runId:guid}/player/{sourceId:guid}/frames/{hostFrame}",
            (Guid runId, Guid sourceId, string hostFrame) =>
            {
                PreviewImageFile image = source.Read(runId, sourceId, hostFrame);
                return Results.File(image.Path, image.MediaType);
            });
        app.Map("/api/preview/live/{id:guid}/view", async (Guid id, HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) return Results.BadRequest();
            source.RequireLive(id);
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
            await source.View(id, socket);
            return Results.Empty;
        });
    }
}
