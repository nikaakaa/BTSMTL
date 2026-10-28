using System.Text.Json.Serialization;
using System.Text.Json;

namespace BTSMTL.WebWorkbench;

public interface IDocumentReceiveRequest
{
    Guid RequestId { get; }
    string DefinitionsRevision { get; }
}

public sealed record DocumentTextRequest([property: JsonRequired] string Path, [property: JsonRequired] string Text);
public sealed record DocumentCandidateRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string RootPath, [property: JsonRequired] string RootRevision,
    [property: JsonRequired] string DefinitionsRevision, [property: JsonRequired] DocumentTextRequest[] Files,
    string? BaseCandidateId = null) : IDocumentReceiveRequest;
public sealed record DocumentInverseRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string RootPath, [property: JsonRequired] string RootRevision,
    [property: JsonRequired] string DefinitionsRevision, [property: JsonRequired] string AppliedCandidateId,
    string? BaseCandidateId = null) : IDocumentReceiveRequest;

public sealed record DocumentEditPreviewRequest([property: JsonRequired] Guid RequestId,
    [property: JsonRequired] string RootPath, [property: JsonRequired] string RootRevision,
    [property: JsonRequired] string DefinitionsRevision, [property: JsonRequired] GraphEditBatchRequest[] GraphBatches,
    [property: JsonRequired] TimelineEditBatchRequest[] TimelineBatches, string? BaseCandidateId = null) : IDocumentReceiveRequest;
public sealed record TimelineEditBatchRequest([property: JsonRequired] string DocumentId,
    [property: JsonRequired] TimelineEditRequest[] Edits);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RenameTimelineRequest), "rename-timeline")]
[JsonDerivedType(typeof(SetTimelineLoopRequest), "set-timeline-loop")]
[JsonDerivedType(typeof(ConfigureTimelineFieldsRequest), "configure-timeline-fields")]
[JsonDerivedType(typeof(InsertTimelineTrackRequest), "insert-track")]
[JsonDerivedType(typeof(DeleteTimelineTrackRequest), "delete-track")]
[JsonDerivedType(typeof(MoveTimelineTrackRequest), "move-track")]
[JsonDerivedType(typeof(ConfigureTimelineTrackRequest), "configure-track")]
[JsonDerivedType(typeof(InsertTimelineClipRequest), "insert-clip")]
[JsonDerivedType(typeof(DeleteTimelineClipRequest), "delete-clip")]
[JsonDerivedType(typeof(MoveTimelineClipRequest), "move-clip")]
[JsonDerivedType(typeof(ConfigureTimelineClipRequest), "configure-clip")]
[JsonDerivedType(typeof(SetTimelineClipRangeRequest), "set-clip-range")]
[JsonDerivedType(typeof(InsertTimelineMarkerRequest), "insert-marker")]
[JsonDerivedType(typeof(DeleteTimelineMarkerRequest), "delete-marker")]
[JsonDerivedType(typeof(ConfigureTimelineMarkerRequest), "configure-marker")]
[JsonDerivedType(typeof(InsertTimelineBindingRequest), "insert-binding")]
[JsonDerivedType(typeof(DeleteTimelineBindingRequest), "delete-binding")]
[JsonDerivedType(typeof(ConfigureTimelineBindingRequest), "configure-binding")]
public abstract record TimelineEditRequest;
public sealed record RenameTimelineRequest([property: JsonRequired] string DisplayName) : TimelineEditRequest;
public sealed record SetTimelineLoopRequest([property: JsonRequired] bool Loop) : TimelineEditRequest;
public sealed record ConfigureTimelineFieldsRequest([property: JsonRequired] Dictionary<string, JsonElement> Fields) : TimelineEditRequest;
public sealed record InsertTimelineTrackRequest([property: JsonRequired] int Index,
    [property: JsonRequired] TimelineTrackRequest Track) : TimelineEditRequest;
public sealed record DeleteTimelineTrackRequest([property: JsonRequired] string TrackId) : TimelineEditRequest;
public sealed record MoveTimelineTrackRequest([property: JsonRequired] string TrackId,
    [property: JsonRequired] int Index) : TimelineEditRequest;
public sealed record ConfigureTimelineTrackRequest([property: JsonRequired] string TrackId,
    [property: JsonRequired] string DisplayName, [property: JsonRequired] Dictionary<string, JsonElement> Fields) : TimelineEditRequest;
public sealed record InsertTimelineClipRequest([property: JsonRequired] string TrackId, [property: JsonRequired] int Index,
    [property: JsonRequired] TimelineClipRequest Clip) : TimelineEditRequest;
public sealed record DeleteTimelineClipRequest([property: JsonRequired] string TrackId,
    [property: JsonRequired] string ClipId) : TimelineEditRequest;
public sealed record MoveTimelineClipRequest([property: JsonRequired] string SourceTrackId,
    [property: JsonRequired] string TargetTrackId, [property: JsonRequired] string ClipId,
    [property: JsonRequired] int Index) : TimelineEditRequest;
public sealed record ConfigureTimelineClipRequest([property: JsonRequired] string TrackId,
    [property: JsonRequired] string ClipId, [property: JsonRequired] string DisplayName,
    [property: JsonRequired] Dictionary<string, JsonElement> Fields) : TimelineEditRequest;
public sealed record SetTimelineClipRangeRequest([property: JsonRequired] string TrackId,
    [property: JsonRequired] string ClipId, [property: JsonRequired] TimelineRangeRequest Range) : TimelineEditRequest;
public sealed record InsertTimelineMarkerRequest([property: JsonRequired] string TrackId,
    [property: JsonRequired] int Index, [property: JsonRequired] TimelineMarkerRequest Marker) : TimelineEditRequest;
public sealed record DeleteTimelineMarkerRequest([property: JsonRequired] string TrackId,
    [property: JsonRequired] string MarkerId) : TimelineEditRequest;
public sealed record ConfigureTimelineMarkerRequest([property: JsonRequired] string TrackId,
    [property: JsonRequired] string MarkerId, [property: JsonRequired] string DisplayName,
    [property: JsonRequired] string TimeRaw, [property: JsonRequired] Dictionary<string, JsonElement> Fields) : TimelineEditRequest;
public sealed record InsertTimelineBindingRequest([property: JsonRequired] int Index,
    [property: JsonRequired] TimelineBindingRequest Binding) : TimelineEditRequest;
public sealed record DeleteTimelineBindingRequest([property: JsonRequired] string BindingId) : TimelineEditRequest;
public sealed record ConfigureTimelineBindingRequest([property: JsonRequired] string BindingId,
    [property: JsonRequired] Dictionary<string, JsonElement> Fields) : TimelineEditRequest;

public sealed record TimelineRangeRequest([property: JsonRequired] string Start, [property: JsonRequired] string End,
    [property: JsonRequired] string EaseIn, [property: JsonRequired] string EaseOut, [property: JsonRequired] string ClipIn,
    [property: JsonRequired] float SourceStart, [property: JsonRequired] float SourceEnd,
    [property: JsonRequired] string OtherEaseIn, [property: JsonRequired] string OtherEaseOut);
public sealed record TimelineTrackRequest([property: JsonRequired] string Id, [property: JsonRequired] string ContractKind,
    [property: JsonRequired] string DisplayName, [property: JsonRequired] Dictionary<string, JsonElement> Fields,
    [property: JsonRequired] TimelineClipRequest[] Clips, [property: JsonRequired] TimelineMarkerRequest[] Markers);
public sealed record TimelineClipRequest([property: JsonRequired] string Id, [property: JsonRequired] string ContractKind,
    [property: JsonRequired] string DisplayName, [property: JsonRequired] TimelineRangeRequest Range,
    [property: JsonRequired] Dictionary<string, JsonElement> Fields);
public sealed record TimelineMarkerRequest([property: JsonRequired] string Id, [property: JsonRequired] string ContractKind,
    [property: JsonRequired] string DisplayName, [property: JsonRequired] string TimeRaw,
    [property: JsonRequired] Dictionary<string, JsonElement> Fields);
public sealed record TimelineBindingRequest([property: JsonRequired] string Id,
    [property: JsonRequired] Dictionary<string, JsonElement> Fields);
