namespace BTSMTL.WebWorkbench;

public interface IPreviewHost<out TSourceCatalog, out TInputCatalog>
{
    Task<PreviewHostDescription> Describe(CancellationToken cancellation);
    TSourceCatalog Sources();
    TInputCatalog Inputs(string target, string revision);
    Task<PreviewQuickReply<PreviewQuickDescription>> DescribeQuick(PreviewQuickTarget target, CancellationToken cancellation);
    Task<PreviewQuickReply<PreviewQuickStatus>> PlanQuick(PreviewQuickRequest request, int localPort, CancellationToken cancellation);
    // stage 非空时等到作业离开该阶段或结束再答复。
    Task<PreviewQuickReply<PreviewQuickStatus>> QuickStatus(string jobId, string? stage, CancellationToken cancellation);
    Task<PreviewQuickReply<PreviewQuickStatus>> SeekQuick(string jobId, PreviewQuickSeekRequest request, CancellationToken cancellation);
    Task<PreviewQuickReply<PreviewQuickStatus>> RetimeQuick(string jobId, PreviewQuickRetimeRequest request, CancellationToken cancellation);
    Task<PreviewQuickReply<PreviewQuickStatus>> CancelQuick(string jobId, CancellationToken cancellation);
}

public static class PreviewApi
{
    public static void MapPreviewApi(this WebApplication app) =>
        app.MapGet("/api/preview/host", () => new { Available = false, Description = (PreviewHostDescription?)null });

    public static void MapPreviewApi<TSourceCatalog, TInputCatalog>(this WebApplication app,
        IPreviewHost<TSourceCatalog, TInputCatalog> host)
    {
        RouteGroupBuilder routes = app.MapGroup("/api/preview");
        routes.MapGet("/host", async (CancellationToken cancellation) =>
            new { Available = true, Description = await host.Describe(cancellation) });
        routes.MapGet("/sources", host.Sources);
        routes.MapGet("/inputs", (string scene, string revision) => host.Inputs(scene, revision));
        routes.MapPost("/quick/describe", async (PreviewQuickTarget target, CancellationToken cancellation) =>
            QuickResult(await host.DescribeQuick(target, cancellation)));
        routes.MapPost("/quick/plans", async (PreviewQuickRequest request, HttpContext context, CancellationToken cancellation) =>
            QuickResult(await host.PlanQuick(request, context.Connection.LocalPort, cancellation)));
        routes.MapGet("/quick/plans/{jobId}", async (string jobId, string? stage, CancellationToken cancellation) =>
            QuickResult(await host.QuickStatus(jobId, stage, cancellation)));
        routes.MapPost("/quick/plans/{jobId}/seek", async (string jobId, PreviewQuickSeekRequest request, CancellationToken cancellation) =>
            QuickResult(await host.SeekQuick(jobId, request, cancellation)));
        routes.MapPost("/quick/plans/{jobId}/retime", async (string jobId, PreviewQuickRetimeRequest request, CancellationToken cancellation) =>
            QuickResult(await host.RetimeQuick(jobId, request, cancellation)));
        routes.MapPost("/quick/plans/{jobId}/cancel", async (string jobId, CancellationToken cancellation) =>
            QuickResult(await host.CancelQuick(jobId, cancellation)));
    }

    static IResult QuickResult<T>(PreviewQuickReply<T> reply) => Results.Json(reply,
        statusCode: reply.Success ? reply.Pending ? StatusCodes.Status202Accepted : StatusCodes.Status200OK :
            StatusCodes.Status409Conflict);
}
