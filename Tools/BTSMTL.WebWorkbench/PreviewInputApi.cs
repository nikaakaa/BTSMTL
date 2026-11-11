using System.Text.Json;

namespace BTSMTL.WebWorkbench;

public static class PreviewInputApi
{
    public static void MapPreviewInputApi(this WebApplication app, IPreviewInputAdapter adapter)
    {
        app.MapPost("/api/preview/inputs/fragments", (JsonElement input, HttpContext context) =>
            AuthoringQueryResults.Read(context, null, () => adapter.Fragment(input)));
        app.MapPost("/api/preview/inputs/plans", (string documentPath, JsonElement input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { documentPath }, () => adapter.Create(documentPath, input)));
        app.MapPost("/api/preview/inputs/recordings", (string documentPath, JsonElement input, HttpContext context) =>
            AuthoringQueryResults.ReadAsync(context, new { documentPath }, () => adapter.Import(documentPath, input)));
    }
}
