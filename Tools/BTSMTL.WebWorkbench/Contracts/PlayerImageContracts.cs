namespace BTSMTL.WebWorkbench;

public sealed record PreviewImageSourceDescription(string DisplayName, string FrameDisplayName);
public sealed record PreviewImageBuild(string BuildId, string DisplayName, PreviewField[] Fields, string RawJson);
