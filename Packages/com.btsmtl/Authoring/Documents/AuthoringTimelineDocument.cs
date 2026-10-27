using System;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.Authoring.Documents
{
    public sealed class AuthoringTimelineDocument
    {
        public AuthoringTimelineDocument(GraphAuthoringElementId id, GraphAuthoringDocumentRoleId roleId,
            AuthoringContentReference owner, string displayName, string definitionVersion, bool loop,
            ReadOnlyMemory<AuthoringNamedValue> fields, ReadOnlyMemory<AuthoringTimelineTrackDocument> tracks,
            ReadOnlyMemory<AuthoringTimelineBindingDocument> bindings)
        {
            Id = id;
            RoleId = roleId;
            Owner = owner;
            DisplayName = displayName;
            DefinitionVersion = definitionVersion;
            Loop = loop;
            Fields = fields;
            Tracks = tracks;
            Bindings = bindings;
        }

        public GraphAuthoringElementId Id { get; }
        public GraphAuthoringDocumentRoleId RoleId { get; }
        public AuthoringContentReference Owner { get; }
        public string DisplayName { get; }
        public string DefinitionVersion { get; }
        public bool Loop { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
        public ReadOnlyMemory<AuthoringTimelineTrackDocument> Tracks { get; }
        public ReadOnlyMemory<AuthoringTimelineBindingDocument> Bindings { get; }
    }

    public sealed class AuthoringTimelineTrackDocument
    {
        public AuthoringTimelineTrackDocument(GraphAuthoringElementId id, string contractKind, string displayName,
            ReadOnlyMemory<AuthoringNamedValue> fields, ReadOnlyMemory<AuthoringTimelineClipDocument> clips,
            ReadOnlyMemory<AuthoringTimelineMarkerDocument> markers)
        {
            Id = id;
            ContractKind = contractKind;
            DisplayName = displayName;
            Fields = fields;
            Clips = clips;
            Markers = markers;
        }

        public GraphAuthoringElementId Id { get; }
        public string ContractKind { get; }
        public string DisplayName { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
        public ReadOnlyMemory<AuthoringTimelineClipDocument> Clips { get; }
        public ReadOnlyMemory<AuthoringTimelineMarkerDocument> Markers { get; }
    }

    public sealed class AuthoringTimelineClipDocument
    {
        public AuthoringTimelineClipDocument(GraphAuthoringElementId id, string contractKind,
            string displayName, TimelineClipTimeRange timeRange, ReadOnlyMemory<AuthoringNamedValue> fields)
        {
            Id = id;
            ContractKind = contractKind;
            DisplayName = displayName;
            TimeRange = timeRange;
            Fields = fields;
        }

        public GraphAuthoringElementId Id { get; }
        public string ContractKind { get; }
        public string DisplayName { get; }
        public TimelineClipTimeRange TimeRange { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
    }

    public sealed class AuthoringTimelineMarkerDocument
    {
        public AuthoringTimelineMarkerDocument(GraphAuthoringElementId id, string contractKind,
            string displayName, OrchestrationTime time, ReadOnlyMemory<AuthoringNamedValue> fields)
        {
            Id = id;
            ContractKind = contractKind;
            DisplayName = displayName;
            Time = time;
            Fields = fields;
        }

        public GraphAuthoringElementId Id { get; }
        public string ContractKind { get; }
        public string DisplayName { get; }
        public OrchestrationTime Time { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
    }

    public sealed class AuthoringTimelineBindingDocument
    {
        public AuthoringTimelineBindingDocument(GraphAuthoringElementId id, ReadOnlyMemory<AuthoringNamedValue> fields)
        {
            Id = id;
            Fields = fields;
        }

        public GraphAuthoringElementId Id { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
    }
}
