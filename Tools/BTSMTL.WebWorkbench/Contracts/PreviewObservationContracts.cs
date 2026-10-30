namespace BTSMTL.WebWorkbench;

public sealed record PreviewField(string DisplayName, string Value);
public sealed record PreviewAuthoringLocation(string DisplayName, string DocumentId, string ElementId);
public sealed record PreviewRecord(string DisplayName, PreviewField[] Fields, string RawJson,
    PreviewAuthoringLocation[] Locations);
public sealed record PreviewSection(string DisplayName, string EmptyMessage, PreviewRecord[] Records);
public sealed record PreviewConsistency(string DisplayName, string Value);
public sealed record PreviewPose(double[] Position, double[] Rotation);
public sealed record PreviewActor(string Id, string DisplayName, PreviewPose Root, PreviewField[] Fields, string RawJson);
public sealed record PreviewShapePart(int ShapeIndex, PreviewPose LocalPose);
public sealed record PreviewShape(string Identity, string Kind, double Radius, double HalfLength,
    double[] HalfExtents, double[][] Vertices, PreviewShapePart[] Parts);
public sealed record PreviewWorldCollider(string Identity, string Kind, PreviewPose Pose,
    double Radius, double HalfLength, double[] HalfExtents, bool IsTrigger);
public sealed record PreviewTriangle(double[] A, double[] B, double[] C);
public sealed record PreviewActorCollider(string ActorId, double Radius, double HalfLength, double[] CenterOffset);
public sealed record PreviewGeometry(string RunId, string ContentRevision, PreviewWorldCollider[] World,
    PreviewTriangle[] WorldTriangles, PreviewActorCollider[] Actors, PreviewShape[] Shapes, string RawJson);
public sealed record PreviewSweepSegment(int ShapeIndex, PreviewPose Start, PreviewPose End);
public sealed record PreviewSweep(PreviewSweepSegment[] Segments, PreviewSweepSegment[] Targets);
public sealed record PreviewContact(double[] Point, double[] Normal);
public sealed record PreviewFrameGeometry(PreviewSweep[] Sweeps, PreviewContact[] Contacts);
public sealed record PreviewFrame(string HostFrame, double Seconds, PreviewActor[] Actors,
    string[] Channels, PreviewConsistency? Consistency, PreviewField[] Fields,
    PreviewSection[] Sections, PreviewFrameGeometry Geometry, string RawJson)
{
    public PreviewRouteFrame? Route { get; init; }
    public PreviewFrameSource? Source { get; init; }
    public PreviewObservationAvailability[] Availability { get; init; } = [];
}
public sealed record PreviewFrameSource(string PlayerBuildId, string ContentRevision, string SceneRevision,
    string InputSource, string? InputRevision, string SdkFrame, string GameplayTick);
public sealed record PreviewSummary(string RunId, string ContentRevision, string FrameDisplayName,
    string FirstFrame, string LastFrame, int FrameCount, string[] Channels, bool GeometryAvailable,
    PreviewField[] Fields, string RawJson)
{
    public string PlayerBuildId { get; init; } = string.Empty;
    public string SceneRevision { get; init; } = string.Empty;
    public string InputSource { get; init; } = string.Empty;
    public string? InputRevision { get; init; }
    public PreviewObservationAvailability[] Availability { get; init; } = [];
}
public sealed record PreviewPage<T>(T[] Items, int Total, int? NextOffset);
public sealed record PreviewRoutePlayback(string DocumentId, double CursorSeconds, string Status);
public sealed record PreviewRouteFrame(string HostFrame, double Seconds, PreviewRoutePlayback[] Playbacks,
    PreviewAuthoringLocation[] Active)
{
    public PreviewNodeOccurrence[] Nodes { get; init; } = [];
    public PreviewEdgeOccurrence[] Edges { get; init; } = [];
    public PreviewPortSample[] Ports { get; init; } = [];
    public PreviewObservationAvailability[] Availability { get; init; } = [];
}
public sealed record PreviewRoute(string RunId, string ContentRevision, PreviewRouteFrame[] Frames)
{
    public string Protocol => "btsmtl.preview-route/2";
    public PreviewObservationAvailability[] Availability { get; init; } = [];
}
public sealed record PreviewObservationPage(string RunId, string ContentRevision, string[] Channels,
    string FirstFrame, string LastFrame, int Offset, int Total, int? NextOffset, PreviewFrame[] Items);
public sealed record PreviewQueryField(string Name, string DisplayName, string InputType, string Placeholder);
public sealed record PreviewNumberInput(string Name, string DisplayName, string Minimum, string Step, string InitialValue);
public sealed record PreviewHostDescription(string DisplayName, string FrameDisplayName,
    PreviewQueryField[] Filters, PreviewNumberInput InputLimit)
{
    public PreviewModeAvailability[] Modes { get; init; } = [];
}
public sealed record PreviewPlaybackObservation(PreviewSummary Summary);
public sealed record PreviewPlaybackState(string State, Guid? LastCommandId, PreviewPlaybackObservation Observation);
public sealed record PreviewPlaybackView(bool Ready, object Receipt, PreviewPlaybackState? Snapshot);
public sealed record PreviewStreamEvent(string Event, string? Id, object Data);
