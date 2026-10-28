namespace BTSMTL.WebWorkbench;

public static class AuthoringGraphEditsApi
{
    public static void MapAuthoringGraphEditsApi(this WebApplication app,
        Func<GraphEditPreviewRequest, IResult> preview)
    {
        app.MapPost("/api/authoring/graphs/edits/preview", preview)
            .AddEndpointFilter(async (context, next) =>
            {
                try { return await next(context); }
                catch (Exception error) when (error is IOException or FormatException or ArgumentException or
                    Newtonsoft.Json.JsonException or InvalidOperationException or KeyNotFoundException)
                {
                    return Results.UnprocessableEntity(new { Stage = "graph-edit-preview",
                        RequestId = context.HttpContext.TraceIdentifier, Message = error.Message });
                }
            });
    }
}
