#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.Authoring.Workspace
{
public abstract record AuthoringTimelineEdit;
public sealed record RenameTimeline(string DisplayName) : AuthoringTimelineEdit;
public sealed record SetTimelineLoop(bool Loop) : AuthoringTimelineEdit;
public sealed record ConfigureTimelineFields(AuthoringNamedValue[] Fields) : AuthoringTimelineEdit;
public sealed record InsertTimelineTrack(int Index, AuthoringTimelineTrackDocument Track) : AuthoringTimelineEdit;
public sealed record DeleteTimelineTrack(GraphAuthoringElementId TrackId) : AuthoringTimelineEdit;
public sealed record MoveTimelineTrack(GraphAuthoringElementId TrackId, int Index) : AuthoringTimelineEdit;
public sealed record ConfigureTimelineTrack(GraphAuthoringElementId TrackId, string DisplayName,
    AuthoringNamedValue[] Fields) : AuthoringTimelineEdit;
public sealed record InsertTimelineClip(GraphAuthoringElementId TrackId, int Index,
    AuthoringTimelineClipDocument Clip) : AuthoringTimelineEdit;
public sealed record DeleteTimelineClip(GraphAuthoringElementId TrackId, GraphAuthoringElementId ClipId) : AuthoringTimelineEdit;
public sealed record MoveTimelineClip(GraphAuthoringElementId SourceTrackId, GraphAuthoringElementId TargetTrackId,
    GraphAuthoringElementId ClipId, int Index) : AuthoringTimelineEdit;
public sealed record ConfigureTimelineClip(GraphAuthoringElementId TrackId, GraphAuthoringElementId ClipId,
    string DisplayName, AuthoringNamedValue[] Fields) : AuthoringTimelineEdit;
public sealed record SetTimelineClipRange(GraphAuthoringElementId TrackId, GraphAuthoringElementId ClipId,
    TimelineClipTimeRange Range) : AuthoringTimelineEdit;
public sealed record InsertTimelineMarker(GraphAuthoringElementId TrackId, int Index,
    AuthoringTimelineMarkerDocument Marker) : AuthoringTimelineEdit;
public sealed record DeleteTimelineMarker(GraphAuthoringElementId TrackId, GraphAuthoringElementId MarkerId) : AuthoringTimelineEdit;
public sealed record ConfigureTimelineMarker(GraphAuthoringElementId TrackId, GraphAuthoringElementId MarkerId,
    string DisplayName, OrchestrationTime Time, AuthoringNamedValue[] Fields) : AuthoringTimelineEdit;
public sealed record InsertTimelineBinding(int Index, AuthoringTimelineBindingDocument Binding) : AuthoringTimelineEdit;
public sealed record DeleteTimelineBinding(GraphAuthoringElementId BindingId) : AuthoringTimelineEdit;
public sealed record ConfigureTimelineBinding(GraphAuthoringElementId BindingId,
    AuthoringNamedValue[] Fields) : AuthoringTimelineEdit;
public sealed record AuthoringTimelineEditBatch(GraphAuthoringElementId DocumentId, AuthoringTimelineEdit[] Edits);

public static class AuthoringTimelineMutation
{
    public static AuthoringTimelineDocument Apply(AuthoringTimelineDocument source, IReadOnlyList<AuthoringTimelineEdit> edits)
    {
        string name = source.DisplayName;
        bool loop = source.Loop;
        ReadOnlyMemory<AuthoringNamedValue> fields = source.Fields;
        var tracks = source.Tracks.ToArray().ToList();
        var bindings = source.Bindings.ToArray().ToList();
        foreach (AuthoringTimelineEdit edit in edits)
        {
            switch (edit)
            {
                case RenameTimeline rename: name = rename.DisplayName; break;
                case SetTimelineLoop set: loop = set.Loop; break;
                case ConfigureTimelineFields configure: fields = configure.Fields; break;
                case InsertTimelineTrack insert: tracks.Insert(insert.Index, insert.Track); break;
                case DeleteTimelineTrack delete:
                    tracks.RemoveAt(TrackIndex(delete.TrackId));
                    break;
                case MoveTimelineTrack move:
                    AuthoringTimelineTrackDocument movedTrack = tracks[TrackIndex(move.TrackId)];
                    tracks.Remove(movedTrack);
                    tracks.Insert(move.Index, movedTrack);
                    break;
                case ConfigureTimelineTrack configure:
                    int trackIndex = TrackIndex(configure.TrackId);
                    AuthoringTimelineTrackDocument track = tracks[trackIndex];
                    tracks[trackIndex] = new(track.Id, track.ContractKind, configure.DisplayName,
                        configure.Fields, track.Clips, track.Markers);
                    break;
                case InsertTimelineClip insert:
                    EditClips(insert.TrackId, clips => clips.Insert(insert.Index, insert.Clip));
                    break;
                case DeleteTimelineClip delete:
                    EditClips(delete.TrackId, clips => clips.RemoveAt(ClipIndex(clips, delete.ClipId)));
                    break;
                case MoveTimelineClip move:
                    AuthoringTimelineClipDocument moved = tracks[TrackIndex(move.SourceTrackId)].Clips.ToArray()
                        .Single(clip => clip.Id.Equals(move.ClipId));
                    EditClips(move.SourceTrackId, clips => clips.RemoveAt(ClipIndex(clips, move.ClipId)));
                    EditClips(move.TargetTrackId, clips => clips.Insert(move.Index, moved));
                    break;
                case ConfigureTimelineClip configure:
                    EditClips(configure.TrackId, clips =>
                    {
                        int index = ClipIndex(clips, configure.ClipId);
                        AuthoringTimelineClipDocument clip = clips[index];
                        clips[index] = new(clip.Id, clip.ContractKind, configure.DisplayName, clip.TimeRange, configure.Fields);
                    });
                    break;
                case SetTimelineClipRange set:
                    EditClips(set.TrackId, clips =>
                    {
                        int index = ClipIndex(clips, set.ClipId);
                        AuthoringTimelineClipDocument clip = clips[index];
                        clips[index] = new(clip.Id, clip.ContractKind, clip.DisplayName, set.Range, clip.Fields);
                    });
                    break;
                case InsertTimelineMarker insert:
                    EditMarkers(insert.TrackId, markers => markers.Insert(insert.Index, insert.Marker));
                    break;
                case DeleteTimelineMarker delete:
                    EditMarkers(delete.TrackId, markers => markers.RemoveAt(MarkerIndex(markers, delete.MarkerId)));
                    break;
                case ConfigureTimelineMarker configure:
                    EditMarkers(configure.TrackId, markers =>
                    {
                        int index = MarkerIndex(markers, configure.MarkerId);
                        AuthoringTimelineMarkerDocument marker = markers[index];
                        markers[index] = new(marker.Id, marker.ContractKind, configure.DisplayName, configure.Time, configure.Fields);
                    });
                    break;
                case InsertTimelineBinding insert: bindings.Insert(insert.Index, insert.Binding); break;
                case DeleteTimelineBinding delete:
                    bindings.RemoveAt(bindings.FindIndex(binding => binding.Id.Equals(delete.BindingId)));
                    break;
                case ConfigureTimelineBinding configure:
                    int bindingIndex = bindings.FindIndex(binding => binding.Id.Equals(configure.BindingId));
                    bindings[bindingIndex] = new(configure.BindingId, configure.Fields);
                    break;
                default: throw new ArgumentException("没有正式 Timeline 修改：" + edit.GetType().Name, nameof(edits));
            }
        }
        return new(source.Id, source.RoleId, source.Owner, name, source.DefinitionVersion, loop,
            fields, tracks.ToArray(), bindings.ToArray());

        int TrackIndex(GraphAuthoringElementId id) => tracks.FindIndex(track => track.Id.Equals(id));
        void EditClips(GraphAuthoringElementId id, Action<List<AuthoringTimelineClipDocument>> apply)
        {
            int index = TrackIndex(id);
            AuthoringTimelineTrackDocument track = tracks[index];
            var clips = track.Clips.ToArray().ToList();
            apply(clips);
            tracks[index] = new(track.Id, track.ContractKind, track.DisplayName, track.Fields, clips.ToArray(), track.Markers);
        }
        void EditMarkers(GraphAuthoringElementId id, Action<List<AuthoringTimelineMarkerDocument>> apply)
        {
            int index = TrackIndex(id);
            AuthoringTimelineTrackDocument track = tracks[index];
            var markers = track.Markers.ToArray().ToList();
            apply(markers);
            tracks[index] = new(track.Id, track.ContractKind, track.DisplayName, track.Fields, track.Clips, markers.ToArray());
        }
    }

    static int ClipIndex(List<AuthoringTimelineClipDocument> clips, GraphAuthoringElementId id) =>
        clips.FindIndex(clip => clip.Id.Equals(id));
    static int MarkerIndex(List<AuthoringTimelineMarkerDocument> markers, GraphAuthoringElementId id) =>
        markers.FindIndex(marker => marker.Id.Equals(id));
}
}
