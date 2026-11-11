using System.Text.Json.Nodes;
using System.Text.Json;

namespace BTSMTL.WebWorkbench;

public sealed partial class PlayerPreviewJobs
{
    readonly string m_Players;
    readonly string m_Runs;
    readonly IPlayerAdapter m_Adapter;
    readonly int m_PoolLimit;

    public PlayerPreviewJobs(IPlayerAdapter adapter)
    {
        int poolLimit = adapter.PoolLimit;
        if (poolLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(poolLimit), "常驻预览播放器上限必须大于零。");
        m_PoolLimit = poolLimit;
        m_Adapter = adapter;
        m_Players = Path.TrimEndingDirectorySeparator(Path.GetFullPath(adapter.PlayersDirectory));
        if (!string.Equals(Path.GetPathRoot(m_Players), "D:\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("预览播放器构建目录必须使用 D 盘绝对路径。");
        m_Runs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(adapter.RunsDirectory));
        _ = Dispatch();
    }

    public PlayerPreviewCreationOptions CreationOptions(PlayerPreviewCreationRequest input)
    {
        PlayerJobParameters parameters = m_Adapter.CreationParameters(input.ProjectParameters);
        return new(input.RequestId, parameters.Value,
            Path.Combine(m_Runs, "PlayerArchives", input.RequestId.ToString("N")), input.Width, input.Height, 75,
            parameters.FrameRangeBasis, Enum.GetValues<PlayerObservationChannel>(), PlayerPreviewMode.Real);
    }

    public string? UnavailableReason => PublishedBuilds().Any()
        ? null : $"预览播放器目录 '{m_Players}' 中没有可用构建。";

    public object Builds() => new
    {
        Directory = m_Players,
        Items = PublishedBuilds().OrderByDescending(build => (string?)build["builtAt"], StringComparer.Ordinal).ToArray()
    };

    internal string Executable(string buildId)
    {
        if (!Guid.TryParseExact(buildId, "N", out _))
            throw new ArgumentException("需要明确的预览播放器构建身份。");
        JsonNode build = ReadPublishedBuild(buildId)
            ?? throw new FileNotFoundException($"没有预览播放器构建 '{buildId}'。");
        string executable = Path.Combine(m_Players, buildId, (string)build["executable"]!);
        return File.Exists(executable) ? executable : throw new FileNotFoundException($"预览播放器 '{executable}' 不存在。");
    }

    public IEnumerable<JsonNode> PublishedBuilds() => Directory.Exists(m_Players)
        ? Directory.EnumerateDirectories(m_Players)
            .Select(Path.GetFileName)
            .Where(buildId => Guid.TryParseExact(buildId, "N", out _))
            .Select(buildId => ReadPublishedBuild(buildId!)).OfType<JsonNode>()
        : [];

    JsonNode? ReadPublishedBuild(string buildId)
    {
        string manifest = Path.Combine(m_Players, buildId, "preview-player.json");
        if (!File.Exists(manifest))
            return null;
        JsonNode build = JsonNode.Parse(File.ReadAllBytes(manifest))!;
        if ((string?)build["buildId"] != buildId)
            throw new InvalidDataException($"预览播放器构建清单 '{manifest}' 的身份不符。");
        return build;
    }

}

public static class PlayerPreviewApi
{
    public static void MapPlayerPreviewApi(this WebApplication app, PlayerPreviewJobs jobs, PlayerLiveSessions live)
    {
        app.Lifetime.ApplicationStopping.Register(jobs.Stop);
        RouteGroupBuilder routes = app.MapGroup("/api/preview");
        routes.MapPost("/player-jobs", (PlayerPreviewRequest request, HttpContext context) =>
            jobs.Start(request, live, context.Connection.LocalPort));
        routes.MapPost("/player-jobs/creation-options", (PlayerPreviewCreationRequest request) => jobs.CreationOptions(request));
        routes.MapGet("/player-jobs", jobs.ListJobs);
        routes.MapGet("/player-jobs/{jobId:guid}", (Guid jobId) => jobs.ReadJob(jobId));
        routes.MapPost("/player-jobs/{jobId:guid}/cancel", (Guid jobId) => jobs.Cancel(jobId));
        routes.MapGet("/player-jobs/{jobId:guid}/files/{**path}", (Guid jobId, string path) =>
            Results.File(jobs.ArchiveFile(jobId, path), Path.GetExtension(path).ToLowerInvariant() switch
            { ".json" => "application/json", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream" }));
    }
}
