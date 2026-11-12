using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

[JsonConverter(typeof(PlayerExecutionModeConverter))]
public enum PlayerExecutionMode { Batch, Interactive }
public sealed class PlayerExecutionModeConverter() : JsonStringEnumConverter<PlayerExecutionMode>(JsonNamingPolicy.CamelCase, false);

[JsonConverter(typeof(PlayerObservationChannelConverter))]
public enum PlayerObservationChannel { Logic, Bones, Camera, Physics, Graph, StateMachine, Timeline, Blackboard, Ports, Input, Images }
public sealed class PlayerObservationChannelConverter() : JsonStringEnumConverter<PlayerObservationChannel>(JsonNamingPolicy.CamelCase, false);

[JsonConverter(typeof(PlayerPreviewModeConverter))]
public enum PlayerPreviewMode { Off, Quick, Real }
public sealed class PlayerPreviewModeConverter() : JsonStringEnumConverter<PlayerPreviewMode>(JsonNamingPolicy.CamelCase, false);

public sealed record PlayerPreviewRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string PlayerBuildId, [property: JsonRequired] JsonElement ProjectParameters,
    [property: JsonRequired] string OutputDirectory, int Width = 1280, int Height = 720, int Quality = 75,
    PlayerObservationChannel[]? Channels = null, PlayerPreviewMode? Mode = null);

public sealed record PlayerPreviewCreationRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] JsonElement ProjectParameters, [property: JsonRequired] int Width,
    [property: JsonRequired] int Height);

public sealed record PlayerPreviewCreationOptions(Guid RequestId, JsonElement ProjectParameters,
    string OutputDirectory, int Width, int Height, int Quality, string FrameRangeBasis,
    PlayerObservationChannel[] Channels, PlayerPreviewMode Mode);

public sealed record StartLivePlayerRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string PlayerBuildId, int Width = 1280, int Height = 720, bool HideWindow = false);

public sealed record SwapLivePlayerRequest([property: JsonRequired] string PlayerBuildId);

public sealed record LoadPlayerPreviewRequest([property: JsonRequired] Guid CommandId,
    [property: JsonRequired] JsonElement Content, [property: JsonRequired] PlayerObservationChannel[] Channels,
    PlayerExecutionMode ExecutionMode = PlayerExecutionMode.Interactive, int Quality = 75);

public sealed record PlayerPreviewTarget(Guid LiveId, string PlayerBuildId, Guid LoadCommandId, JsonElement Content,
    PlayerObservationChannel[]? Channels);

public sealed record PlayerContentRefreshTarget([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] Guid LiveId, [property: JsonRequired] JsonElement ProjectParameters,
    [property: JsonRequired] PlayerObservationChannel[] Channels);

public sealed record PlayerOperationPhase(string Stage, double Milliseconds);
