using System;
using System.Linq;
using BTSMTL.Authoring.Graph;
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
            ["fields"] = WriteFields(timeline.Fields),
            ["tracks"] = new JArray(timeline.Tracks.ToArray().Select(track => new JObject
            {
                ["id"] = track.Id.Value,
                ["contract"] = track.ContractKind,
                ["displayName"] = track.DisplayName,
                ["fields"] = WriteFields(track.Fields),
                ["clips"] = WriteElements(track.Clips),
                ["markers"] = WriteElements(track.Markers)
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
                ReadFields(Required(timeline, "fields", sourcePath), sourcePath),
                Array(timeline, "tracks", sourcePath).Select(track => new AuthoringTimelineTrackDocument(
                    Id(track, "id", sourcePath), Text(track, "contract", sourcePath), Text(track, "displayName", sourcePath),
                    ReadFields(Required(track, "fields", sourcePath), sourcePath),
                    ReadElements(Array(track, "clips", sourcePath), sourcePath),
                    ReadElements(Array(track, "markers", sourcePath), sourcePath))).ToArray(),
                Array(timeline, "bindings", sourcePath).Select(binding => new AuthoringTimelineBindingDocument(
                    Id(binding, "id", sourcePath), ReadFields(Required(binding, "fields", sourcePath), sourcePath))).ToArray());
        }

        static JArray WriteElements(ReadOnlyMemory<AuthoringTimelineElementDocument> elements) =>
            new JArray(elements.ToArray().Select(element => new JObject
            {
                ["id"] = element.Id.Value,
                ["contract"] = element.ContractKind,
                ["displayName"] = element.DisplayName,
                ["fields"] = WriteFields(element.Fields)
            }));

        static AuthoringTimelineElementDocument[] ReadElements(JArray elements, string sourcePath) =>
            elements.Select(element => new AuthoringTimelineElementDocument(Id(element, "id", sourcePath),
                Text(element, "contract", sourcePath), Text(element, "displayName", sourcePath),
                ReadFields(Required(element, "fields", sourcePath), sourcePath))).ToArray();
    }
}
