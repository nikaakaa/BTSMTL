namespace BTSMTL.WebWorkbench;

// 网页消费的预览归档查询。帧范围按宿主帧（PreviewPosition.HostFrame 的数值）给出；项目实现只负责读自己的归档。
public interface IPreviewArchive
{
    PreviewArchiveDigest Archive(Guid id);
    PreviewSummary Summary(Guid id);
    PreviewGeometry Geometry(Guid id);
    PreviewPage<PreviewRecord> SourceMap(Guid id, int offset, int limit);
    PreviewPage<PreviewFrame> Frames(Guid id, int offset, int limit);
    PreviewObservationPage Observations(Guid id, int firstFrame, int lastFrame, int offset, int limit);
    PreviewArchivePage<PreviewLogicRecord> Logic(Guid id, int firstFrame, int lastFrame, int offset, int limit);
    PreviewPoseLayout PoseLayout(Guid id);
    PreviewArchivePage<PreviewPresentationRecord> Presentation(Guid id, int firstFrame, int lastFrame,
        int offset, int limit, int? rosterIndex);
    PreviewArchivePage<PreviewCamera> Camera(Guid id, int firstFrame, int lastFrame, int offset, int limit);
    PreviewInputPage Input(Guid id, int firstFrame, int lastFrame, int offset, int limit);
    PreviewArchivePage<PreviewDataCurve> Curves(Guid id, int offset, int limit);
    PreviewDataCurveSamples CurveSamples(Guid id, string curveId, int firstFrame, int lastFrame, int offset, int limit);
    PreviewArchivePage<PreviewUnavailableRecord> Unavailable(Guid id, int firstFrame, int lastFrame, int offset, int limit);
    PreviewArchivePage<PreviewFrameImage> FrameImages(Guid id, int firstFrame, int lastFrame, int offset, int limit);
    PreviewFrameImage FrameImageMetadata(Guid id, int imageIndex);
}

public static class PreviewArchiveApi
{
    const int PageLimit = 128;

    public static void MapPreviewArchiveApi(this WebApplication app, IPreviewArchive archive)
    {
        RouteGroupBuilder routes = app.MapGroup("/api/preview/player-jobs/{id:guid}");
        routes.MapGet("/archive", (Guid id) => archive.Archive(id));
        routes.MapGet("/summary", (Guid id) => archive.Summary(id));
        routes.MapGet("/geometry", (Guid id) => archive.Geometry(id));
        routes.MapGet("/source-map", (Guid id, int? offset, int? limit) =>
            archive.SourceMap(id, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/frames", (Guid id, int? offset, int? limit) => archive.Frames(id, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/observations", (Guid id, int firstFrame, int lastFrame, int? offset, int? limit) =>
            archive.Observations(id, firstFrame, lastFrame, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/logic", (Guid id, int firstFrame, int lastFrame, int? offset, int? limit) =>
            archive.Logic(id, firstFrame, lastFrame, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/pose-layout", (Guid id) => archive.PoseLayout(id));
        routes.MapGet("/presentation", (Guid id, int firstFrame, int lastFrame, int? offset, int? limit, int? rosterIndex) =>
            archive.Presentation(id, firstFrame, lastFrame, offset ?? 0, limit ?? PageLimit, rosterIndex));
        routes.MapGet("/camera", (Guid id, int firstFrame, int lastFrame, int? offset, int? limit) =>
            archive.Camera(id, firstFrame, lastFrame, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/input", (Guid id, int firstFrame, int lastFrame, int? offset, int? limit) =>
            archive.Input(id, firstFrame, lastFrame, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/curves", (Guid id, int? offset, int? limit) => archive.Curves(id, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/curves/samples", (Guid id, string curveId, int firstFrame, int lastFrame, int? offset, int? limit) =>
            archive.CurveSamples(id, curveId, firstFrame, lastFrame, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/unavailable", (Guid id, int firstFrame, int lastFrame, int? offset, int? limit) =>
            archive.Unavailable(id, firstFrame, lastFrame, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/frame-images", (Guid id, int firstFrame, int lastFrame, int? offset, int? limit) =>
            archive.FrameImages(id, firstFrame, lastFrame, offset ?? 0, limit ?? PageLimit));
        routes.MapGet("/frame-images/{imageIndex:int}/metadata", (Guid id, int imageIndex) =>
            archive.FrameImageMetadata(id, imageIndex));
    }
}
