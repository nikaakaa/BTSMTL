namespace BTSMTL.WebWorkbench;

// 常驻播放器上报的播放状态，由宿主把项目时钟投影为通用来源与定位；State 取播放控制的
// idle／loading／paused／playing／stepping／seeking／failed。Transport 为空时 TransportUnavailableReason 说明原因。
public sealed record PreviewLiveStatus(string State, string? Error, PreviewFrameSource? Source, string? AdoptedMode,
    PreviewTransportAction[]? Transport, string? TransportUnavailableReason, string? HostFrame, string? LastFrame,
    string? InputMode, string? CameraMode, PreviewField[] Fields, string RawJson);
