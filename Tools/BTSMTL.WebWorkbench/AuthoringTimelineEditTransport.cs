using System.Globalization;
using System.Text.Json;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Workspace;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.WebWorkbench;

public static class AuthoringTimelineEditTransport
{
    public static AuthoringTimelineEdit Read(TimelineEditRequest edit) => edit switch
    {
        RenameTimelineRequest value => new RenameTimeline(value.DisplayName),
        SetTimelineLoopRequest value => new SetTimelineLoop(value.Loop),
        ConfigureTimelineFieldsRequest value => new ConfigureTimelineFields(Fields(value.Fields)),
        InsertTimelineTrackRequest value => new InsertTimelineTrack(value.Index, Track(value.Track)),
        DeleteTimelineTrackRequest value => new DeleteTimelineTrack(new(value.TrackId)),
        MoveTimelineTrackRequest value => new MoveTimelineTrack(new(value.TrackId), value.Index),
        ConfigureTimelineTrackRequest value => new ConfigureTimelineTrack(new(value.TrackId), value.DisplayName, Fields(value.Fields)),
        InsertTimelineClipRequest value => new InsertTimelineClip(new(value.TrackId), value.Index, Clip(value.Clip)),
        DeleteTimelineClipRequest value => new DeleteTimelineClip(new(value.TrackId), new(value.ClipId)),
        MoveTimelineClipRequest value => new MoveTimelineClip(new(value.SourceTrackId), new(value.TargetTrackId), new(value.ClipId), value.Index),
        ConfigureTimelineClipRequest value => new ConfigureTimelineClip(new(value.TrackId), new(value.ClipId), value.DisplayName, Fields(value.Fields)),
        SetTimelineClipRangeRequest value => new SetTimelineClipRange(new(value.TrackId), new(value.ClipId), Range(value.Range)),
        InsertTimelineMarkerRequest value => new InsertTimelineMarker(new(value.TrackId), value.Index, Marker(value.Marker)),
        DeleteTimelineMarkerRequest value => new DeleteTimelineMarker(new(value.TrackId), new(value.MarkerId)),
        ConfigureTimelineMarkerRequest value => new ConfigureTimelineMarker(new(value.TrackId), new(value.MarkerId), value.DisplayName, Time(value.TimeRaw), Fields(value.Fields)),
        InsertTimelineBindingRequest value => new InsertTimelineBinding(value.Index, new(new(value.Binding.Id), Fields(value.Binding.Fields))),
        DeleteTimelineBindingRequest value => new DeleteTimelineBinding(new(value.BindingId)),
        ConfigureTimelineBindingRequest value => new ConfigureTimelineBinding(new(value.BindingId), Fields(value.Fields)),
        _ => throw new ArgumentException($"时间轴修改 '{edit.GetType().Name}' 没有共同操作映射。")
    };

    static AuthoringTimelineTrackDocument Track(TimelineTrackRequest value) => new(new(value.Id), value.ContractKind,
        value.DisplayName, Fields(value.Fields), value.Clips.Select(Clip).ToArray(), value.Markers.Select(Marker).ToArray());
    static AuthoringTimelineClipDocument Clip(TimelineClipRequest value) => new(new(value.Id), value.ContractKind,
        value.DisplayName, Range(value.Range), Fields(value.Fields));
    static AuthoringTimelineMarkerDocument Marker(TimelineMarkerRequest value) => new(new(value.Id), value.ContractKind,
        value.DisplayName, Time(value.TimeRaw), Fields(value.Fields));
    static TimelineClipTimeRange Range(TimelineRangeRequest value) => new(Time(value.Start), Time(value.End),
        Time(value.EaseIn), Time(value.EaseOut), Time(value.ClipIn), value.SourceStart, value.SourceEnd,
        Time(value.OtherEaseIn), Time(value.OtherEaseOut));
    static OrchestrationTime Time(string value) => OrchestrationTime.FromRaw(long.Parse(value, CultureInfo.InvariantCulture));
    static AuthoringNamedValue[] Fields(Dictionary<string, JsonElement> fields) => fields.Select(field =>
        new AuthoringNamedValue(new(field.Key), AuthoringDocumentJson.ReadValue(field.Value.GetRawText(), "timeline-edit-request"))).ToArray();
}
