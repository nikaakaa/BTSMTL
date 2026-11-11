namespace BTSMTL.WebWorkbench;

public static class PreviewQuickApi
{
    public static QuickPreviewService MapPreviewQuickApi(this WebApplication app, IPreviewQuickAdapter adapter,
        PlayerPreviewJobs players, PlayerLiveSessions live)
    {
        var quick = new QuickPreviewService(adapter, players, live);
        RouteGroupBuilder routes = app.MapGroup("/api/preview/quick");
        routes.MapPost("/describe", async (PreviewQuickTarget target, CancellationToken cancellation) =>
            Result(await quick.DescribeQuick(target, cancellation)));
        routes.MapPost("/plans", async (PreviewQuickRequest request, HttpContext context, CancellationToken cancellation) =>
            Result(await quick.PlanQuick(request, context.Connection.LocalPort, cancellation)));
        routes.MapGet("/plans/{jobId}", async (string jobId, string? stage, CancellationToken cancellation) =>
            Result(await quick.QuickStatus(jobId, stage, cancellation)));
        routes.MapPost("/plans/{jobId}/seek", async (string jobId, PreviewQuickSeekRequest request, CancellationToken cancellation) =>
            Result(await quick.SeekQuick(jobId, request, cancellation)));
        routes.MapPost("/plans/{jobId}/retime", async (string jobId, PreviewQuickRetimeRequest request, CancellationToken cancellation) =>
            Result(await quick.RetimeQuick(jobId, request, cancellation)));
        routes.MapPost("/plans/{jobId}/cancel", async (string jobId, CancellationToken cancellation) =>
            Result(await quick.CancelQuick(jobId, cancellation)));
        return quick;
    }

    static IResult Result<T>(PreviewQuickReply<T> reply) => Results.Json(reply,
        statusCode: reply.Success ? reply.Pending ? StatusCodes.Status202Accepted : StatusCodes.Status200OK : StatusCodes.Status409Conflict);
}
