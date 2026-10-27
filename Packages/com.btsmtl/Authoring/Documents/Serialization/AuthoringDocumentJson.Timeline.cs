using System;
using System.Globalization;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Documents
{
    public static partial class AuthoringDocumentJson
    {
        public static string WriteTimeline(AuthoringTimelineDocument timeline) => Write(new JObject
        {
            ["formatVersion"] = AuthoringRootDocument.FormatVersion,
            ["id"] = timeline.Id.Value,
            ["role"] = timeline.RoleId.Value,
            ["owner"] = WriteReference(timeline.Owner),
            ["displayName"] = timeline.DisplayName,
            ["definitionVersion"] = timeline.DefinitionVersion,
            ["loop"] = timeline.Loop,
            ["fields"] = WriteFields(timeline.Fields),
            ["tracks"] = new JArray(timeline.Tracks.ToArray().Select(track => new JObject
            {
                ["id"] = track.Id.Value,
                ["contract"] = track.ContractKind,
                ["displayName"] = track.DisplayName,
                ["fields"] = WriteFields(track.Fields),
                ["clips"] = new JArray(track.Clips.ToArray().Select(clip => new JObject
                {
                    ["id"] = clip.Id.Value,
                    ["contract"] = clip.ContractKind,
                    ["displayName"] = clip.DisplayName,
                    ["timeRange"] = WriteTimeRange(clip.TimeRange),
                    ["fields"] = WriteFields(clip.Fields)
                })),
                ["markers"] = new JArray(track.Markers.ToArray().Select(marker => new JObject
                {
                    ["id"] = marker.Id.Value,
                    ["contract"] = marker.ContractKind,
                    ["displayName"] = marker.DisplayName,
                    ["timeRaw"] = marker.Time.RawValue.ToString(CultureInfo.InvariantCulture),
                    ["fields"] = WriteFields(marker.Fields)
                }))
            })),
            ["bindings"] = new JArray(timeline.Bindings.ToArray().Select(binding => new JObject
            {
                ["id"] = binding.Id.Value,
                ["fields"] = WriteFields(binding.Fields)
            }))
        });

        public static AuthoringTimelineDocument ReadTimeline(string text, string sourcePath)
        {
            JObject timeline = ReadDocument(text, sourcePath);
            return new AuthoringTimelineDocument(Id(timeline, "id", sourcePath),
                new GraphAuthoringDocumentRoleId(Text(timeline, "role", sourcePath)),
                ReadReference(Required(timeline, "owner", sourcePath), sourcePath),
                Text(timeline, "displayName", sourcePath), Text(timeline, "definitionVersion", sourcePath),
                Required(timeline, "loop", sourcePath).Value<bool>(),
                ReadFields(Required(timeline, "fields", sourcePath), sourcePath),
                Array(timeline, "tracks", sourcePath).Select(track => new AuthoringTimelineTrackDocument(
                    Id(track, "id", sourcePath), Text(track, "contract", sourcePath), Text(track, "displayName", sourcePath),
                    ReadFields(Required(track, "fields", sourcePath), sourcePath),
                    Array(track, "clips", sourcePath).Select(clip => new AuthoringTimelineClipDocument(
                        Id(clip, "id", sourcePath), Text(clip, "contract", sourcePath),
                        Text(clip, "displayName", sourcePath), ReadTimeRange(Required(clip, "timeRange", sourcePath), sourcePath),
                        ReadFields(Required(clip, "fields", sourcePath), sourcePath))).ToArray(),
                    Array(track, "markers", sourcePath).Select(marker => new AuthoringTimelineMarkerDocument(
                        Id(marker, "id", sourcePath), Text(marker, "contract", sourcePath),
                        Text(marker, "displayName", sourcePath), ReadTime(marker, "timeRaw", sourcePath),
                        ReadFields(Required(marker, "fields", sourcePath), sourcePath))).ToArray())).ToArray(),
                Array(timeline, "bindings", sourcePath).Select(binding => new AuthoringTimelineBindingDocument(
                    Id(binding, "id", sourcePath), ReadFields(Required(binding, "fields", sourcePath), sourcePath))).ToArray());
        }

        static JObject WriteTimeRange(TimelineClipTimeRange range) => new JObject
        {
            ["startRaw"] = range.Start.RawValue.ToString(CultureInfo.InvariantCulture),
            ["endRaw"] = range.End.RawValue.ToString(CultureInfo.InvariantCulture),
            ["easeInRaw"] = range.EaseIn.RawValue.ToString(CultureInfo.InvariantCulture),
            ["easeOutRaw"] = range.EaseOut.RawValue.ToString(CultureInfo.InvariantCulture),
            ["clipInRaw"] = range.ClipIn.RawValue.ToString(CultureInfo.InvariantCulture),
            ["sourceStart"] = range.SourceStart.ToString("R", CultureInfo.InvariantCulture),
            ["sourceEnd"] = range.SourceEnd.ToString("R", CultureInfo.InvariantCulture),
            ["otherEaseInRaw"] = range.OtherEaseIn.RawValue.ToString(CultureInfo.InvariantCulture),
            ["otherEaseOutRaw"] = range.OtherEaseOut.RawValue.ToString(CultureInfo.InvariantCulture)
        };

        static TimelineClipTimeRange ReadTimeRange(JToken range, string sourcePath) => new TimelineClipTimeRange(
            ReadTime(range, "startRaw", sourcePath), ReadTime(range, "endRaw", sourcePath),
            ReadTime(range, "easeInRaw", sourcePath), ReadTime(range, "easeOutRaw", sourcePath),
            ReadTime(range, "clipInRaw", sourcePath),
            float.Parse(Text(range, "sourceStart", sourcePath), CultureInfo.InvariantCulture),
            float.Parse(Text(range, "sourceEnd", sourcePath), CultureInfo.InvariantCulture),
            ReadTime(range, "otherEaseInRaw", sourcePath), ReadTime(range, "otherEaseOutRaw", sourcePath));

        static OrchestrationTime ReadTime(JToken value, string key, string sourcePath) =>
            OrchestrationTime.FromRaw(long.Parse(Text(value, key, sourcePath), CultureInfo.InvariantCulture));
    }
}
