using System.Text.Json;

namespace BTSMTL.WebWorkbench;

public interface IPreviewHost
{
    PreviewHostDescription Describe();
    object Sources();
    object Inputs(string target, string revision);
    object ListRuns();
    object Read(Guid id);
    Task<object> Start(JsonElement request);
    object Cancel(Guid id);
    PreviewPlaybackView Playback(Guid id);
    Task<object> Control(Guid id, JsonElement request);
    object ReadControl(Guid id, Guid commandId);
    PreviewSummary Summary(Guid id);
    PreviewPage<PreviewFrame> Frames(Guid id, int offset, int limit);
    PreviewRoute Route(Guid id);
    PreviewRoutePage RouteFrames(Guid id, IReadOnlyDictionary<string, string> query);
    PreviewGraphPage Graphs(Guid id, int? rosterIndex, string? graphId, int offset, int limit);
    object Compare(Guid left, Guid right);
    PreviewObservationPage Observations(Guid id, IReadOnlyDictionary<string, string> query);
    IResult Geometry(Guid id);
    object SourceMap(Guid id, int offset, int limit);
    IAsyncEnumerable<PreviewStreamEvent> Stream(Guid id, IReadOnlyDictionary<string, string> query,
        string resume, CancellationToken cancellation);
}

public static class PreviewApi
{
    static readonly JsonSerializerOptions s_StreamJson = new(JsonSerializerDefaults.Web);

    public static void MapPreviewApi(this WebApplication app, IPreviewHost? host)
    {
        RouteGroupBuilder routes = app.MapGroup("/api/preview");
        routes.MapGet("/host", () => new { Available = host != null, Description = host?.Describe() });
        if (host == null) return;
        routes.MapGet("/sources", host.Sources);
        routes.MapGet("/inputs", (string scene, string revision) => host.Inputs(scene, revision));
        routes.MapGet("/runs", host.ListRuns);
        routes.MapGet("/runs/{id:guid}", (Guid id) => host.Read(id));
        routes.MapPost("/runs", (JsonElement request) => host.Start(request));
        routes.MapPost("/runs/{id:guid}/cancel", (Guid id) => host.Cancel(id));
        routes.MapGet("/runs/{id:guid}/playback", (Guid id) => host.Playback(id));
        routes.MapPost("/runs/{id:guid}/controls", (Guid id, JsonElement request) => host.Control(id, request));
        routes.MapGet("/runs/{id:guid}/controls/{commandId:guid}", (Guid id, Guid commandId) => host.ReadControl(id, commandId));
        routes.MapGet("/runs/{id:guid}/summary", (Guid id) => host.Summary(id));
        routes.MapGet("/runs/{id:guid}/frames", (Guid id, int offset, int limit) => host.Frames(id, offset, limit));
        routes.MapGet("/runs/{id:guid}/route", (Guid id) => host.Route(id));
        routes.MapGet("/runs/{id:guid}/route/frames", (Guid id, HttpRequest request) => host.RouteFrames(id, Query(request)));
        routes.MapGet("/runs/{id:guid}/graphs", (Guid id, int? rosterIndex, string? graphId, int offset, int limit) =>
            host.Graphs(id, rosterIndex, graphId, offset, limit));
        routes.MapGet("/runs/{left:guid}/compare/{right:guid}", (Guid left, Guid right) => host.Compare(left, right));
        routes.MapGet("/runs/{id:guid}/observations", (Guid id, HttpRequest request) => host.Observations(id, Query(request)));
        routes.MapGet("/runs/{id:guid}/geometry", (Guid id) => host.Geometry(id));
        routes.MapGet("/runs/{id:guid}/source-map", (Guid id, int offset, int limit) => host.SourceMap(id, offset, limit));
        routes.MapGet("/runs/{id:guid}/observations/stream", async (Guid id, HttpRequest request,
            HttpResponse response, CancellationToken cancellation) =>
        {
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            await foreach (PreviewStreamEvent item in host.Stream(id, Query(request),
                               request.Headers["Last-Event-ID"].ToString(), cancellation))
            {
                if (item.Id != null) await response.WriteAsync($"id: {item.Id}\n", cancellation);
                await response.WriteAsync($"event: {item.Event}\ndata: " +
                    JsonSerializer.Serialize(item.Data, s_StreamJson) + "\n\n", cancellation);
                await response.Body.FlushAsync(cancellation);
            }
        });
    }

    static IReadOnlyDictionary<string, string> Query(HttpRequest request) =>
        request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
}
