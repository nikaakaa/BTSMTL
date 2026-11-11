using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

public sealed record AuthoringDocumentApplyRequest([property: JsonRequired] AuthoringDocumentEditingView EditingView,
    JsonElement? Preview = null, JsonElement? Job = null, bool InputsOnly = false);
public sealed record AuthoringDocumentDeleteRequest([property: JsonRequired] string ExpectedRevision,
    [property: JsonRequired] AuthoringDocumentEditingView EditingView);
public sealed record AuthoringDocumentExportRequest([property: JsonRequired] AuthoringDocumentEditingView EditingView);
public sealed record AuthoringDocumentCreationRequest([property: JsonRequired] string Type,
    [property: JsonRequired] JsonElement Parameters);
public sealed record AuthoringExportFile(string Path, string DownloadUrl);
public sealed record AuthoringExportDownload(string Path, string ContentType, string Name);
