using System;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public sealed class AuthoringTimelineDocument
    {
        public AuthoringTimelineDocument(GraphAuthoringElementId id, GraphAuthoringDocumentRoleId roleId,
            AuthoringContentReference owner, string displayName, string definitionVersion,
            ReadOnlyMemory<AuthoringNamedValue> fields, ReadOnlyMemory<AuthoringTimelineTrackDocument> tracks,
            ReadOnlyMemory<AuthoringTimelineBindingDocument> bindings)
        {
            Id = id;
            RoleId = roleId;
            Owner = owner;
            DisplayName = displayName;
            DefinitionVersion = definitionVersion;
            Fields = fields;
            Tracks = tracks;
            Bindings = bindings;
        }

        public GraphAuthoringElementId Id { get; }
        public GraphAuthoringDocumentRoleId RoleId { get; }
        public AuthoringContentReference Owner { get; }
        public string DisplayName { get; }
        public string DefinitionVersion { get; }
        public ReadOnlyMemory<AuthoringNamedValue> Fields { get; }
        public ReadOnlyMemory<AuthoringTimelineTrackDocument> Tracks { get; }
        public ReadOnlyMemory<AuthoringTimelineBindingDocument> Bindings { get; }
    }

    public sealed class AuthoringTimelineTrackDocument
    {
        public AuthoringTimelineTrackDocument(GraphAuthoringElementId id, string contractKind, string displayName,
            ReadOnlyMemory<AuthoringNamedValue> fields, ReadOnlyMemory<AuthoringTimelineElementDocument> clips,
            ReadOnlyMemory<AuthoringTimelineElementDocument> markers)
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
        public ReadOnlyMemory<AuthoringTimelineElementDocument> Clips { get; }
        public ReadOnlyMemory<AuthoringTimelineElementDocument> Markers { get; }
    }

    public sealed class AuthoringTimelineElementDocument
    {
        public AuthoringTimelineElementDocument(GraphAuthoringElementId id, string contractKind,
            string displayName, ReadOnlyMemory<AuthoringNamedValue> fields)
        {
            Id = id;
            ContractKind = contractKind;
            DisplayName = displayName;
            Fields = fields;
        }

        public GraphAuthoringElementId Id { get; }
        public string ContractKind { get; }
        public string DisplayName { get; }
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
