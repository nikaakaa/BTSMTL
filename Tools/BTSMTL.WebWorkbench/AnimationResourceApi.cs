namespace BTSMTL.WebWorkbench;

public static class AnimationResourceApi
{
    public static void MapAnimationResourceApi(this WebApplication app, string? manifestPath)
    {
        WebAnimationLibrary? library = manifestPath == null ? null : new WebAnimationLibrary(manifestPath);
        app.MapGet("/api/resources/animations", () =>
        {
            if (library == null)
                return Results.Conflict(new { code = "web_animation_library_missing",
                    message = "请用 --AnimationLibrary 指定完整导出的网页动画清单。" });
            try { return Results.Content(library.Read().Json, "application/json", System.Text.Encoding.UTF8); }
            catch (Exception error) when (error is IOException or System.Text.Json.JsonException)
            {
                return Results.UnprocessableEntity(new { code = "web_animation_library_invalid", message = error.Message });
            }
        });
        app.MapGet("/api/resources/animations/file/{hash}", (string hash) =>
        {
            if (library == null)
                return Results.Conflict(new { code = "web_animation_library_missing",
                    message = "当前服务未装配网页动画清单。" });
            try
            {
                WebAnimationLibrary.Snapshot source = library.Version(hash);
                return Results.File(source.FilePath, "model/gltf-binary", enableRangeProcessing: true,
                    entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue('"' + source.Hash + '"'));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { code = "web_animation_version_unknown",
                    message = "该资源版本尚未被当前服务读取，请先读取对应清单。", hash });
            }
        });
    }
}
