using System.Text.Json;
using System.Text.Json.Nodes;

namespace BTSMTL.WebWorkbench;

public sealed record PlayerJobParameters(JsonElement Value, string FrameRangeBasis);
public sealed record PlayerArchiveResult(bool Completed, string? AdoptedMode);
public sealed record PlayerMessageProjection(JsonObject Json, PreviewContentIdentity? Content, PreviewClock? Clock);

public interface IPlayerAdapter
{
    string PlayersDirectory { get; }
    string RunsDirectory { get; }
    string TemporaryDirectory { get; }
    int PoolLimit { get; }
    string ObservationProtocol { get; }
    IReadOnlySet<string> Commands { get; }
    string RecipeDirectory(string baseBuildId);
    string[] LaunchArguments(string connection, string directory, int width, int height, bool hideWindow);
    PlayerJobParameters CreationParameters(JsonElement parameters);
    void ValidateJob(PlayerPreviewRequest request);
    LoadPlayerPreviewRequest JobLoad(PlayerPreviewRequest request);
    PlayerPreviewRequest ResumeRequest(Guid jobId, PlayerPreviewTarget target, JsonObject command);
    PlayerArchiveResult ValidateArchive(string path, PlayerPreviewRequest request);
    PreviewArchiveDigest ArchiveDigest(string path);
    JsonObject PrepareContent(JsonElement content);
    JsonElement SelectedContent(JsonObject prepared);
    JsonElement ContentIdentity(JsonObject content);
    JsonObject ResolveCommand(JsonObject command, JsonElement? content);
    PreviewFrameSource FrameSource(Guid liveId, ReadOnlySpan<byte> packet, JsonNode acknowledgement);
    PreviewLiveStatus Status(Guid liveId, JsonObject status);
    PlayerMessageProjection Message(Guid liveId, JsonObject message);
    byte[] ViewerMessage(Guid liveId, ReadOnlySpan<byte> message);
    PreviewImageSourceDescription ImageDescription { get; }
    PreviewImageBuild DescribeBuild(JsonNode build);
}
