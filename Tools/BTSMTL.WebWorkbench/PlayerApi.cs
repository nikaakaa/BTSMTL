namespace BTSMTL.WebWorkbench;

public sealed record PlayerServiceOptions(string? CompilationProject, string RefreshDirectory);
public sealed record PlayerServices(PlayerPreviewJobs Jobs, PlayerLiveSessions Live, PlayerContentRefreshes Refreshes);

public static class PlayerApi
{
    public static PlayerServices MapPlayerApi(this WebApplication app, IPlayerAdapter adapter, PlayerServiceOptions options)
    {
        var jobs = new PlayerPreviewJobs(adapter);
        var live = new PlayerLiveSessions(adapter, jobs);
        var refreshes = new PlayerContentRefreshes(live, options.RefreshDirectory);
        app.UseWebSockets();
        app.MapPlayerPreviewApi(jobs, live);
        app.MapPlayerLiveApi(live);
        app.MapGet("/api/preview/refreshes/{id:guid}", (Guid id) => refreshes.Read(id));
        if (options.CompilationProject is { } project)
            app.MapPlayerCodeUpdateApi(new PlayerCodeUpdates(adapter.PlayersDirectory,
                new PlayerScriptCompiler(project, adapter.TemporaryDirectory),
                new PlayerBurstCompiler(project, adapter.TemporaryDirectory)));
        else
            app.MapPost("/api/preview/players/code-updates", () => Results.Json(new
            {
                success = false, available = false, code = "player-project-unavailable",
                message = "未配置播放器编译工程，无法更新预览播放器代码。"
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        return new(jobs, live, refreshes);
    }
}
