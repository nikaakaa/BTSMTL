namespace BTSMTL.Compilation
{
    public readonly struct SimulationSourceLocation
    {
        public SimulationSourceLocation(
            string sourceType,
            string graphId,
            string nodeId,
            string edgeId,
            string timelineId,
            string clipId,
            string displayPath,
            string trackId = "",
            string declarationId = "",
            string portId = "",
            string contentHash = "")
        {
            SourceType = sourceType ?? string.Empty;
            GraphId = graphId ?? string.Empty;
            NodeId = nodeId ?? string.Empty;
            PortId = portId ?? string.Empty;
            EdgeId = edgeId ?? string.Empty;
            DeclarationId = declarationId ?? string.Empty;
            TimelineId = timelineId ?? string.Empty;
            TrackId = trackId ?? string.Empty;
            ClipId = clipId ?? string.Empty;
            DisplayPath = displayPath ?? string.Empty;
            ContentHash = contentHash ?? string.Empty;
        }
        public string SourceType { get; }
        public string GraphId { get; }
        public string NodeId { get; }
        public string PortId { get; }
        public string EdgeId { get; }
        public string DeclarationId { get; }
        public string TimelineId { get; }
        public string TrackId { get; }
        public string ClipId { get; }
        public string DisplayPath { get; }
        public string ContentHash { get; }
        public string TemplateIdentity => !string.IsNullOrEmpty(ClipId)
            ? $"timeline:{TimelineId}/clip:{ClipId}"
            : !string.IsNullOrEmpty(TrackId)
                ? $"timeline:{TimelineId}/track:{TrackId}"
                : !string.IsNullOrEmpty(TimelineId)
                    ? $"timeline:{TimelineId}"
                    : !string.IsNullOrEmpty(NodeId)
                        ? $"graph:{GraphId}/node:{NodeId}"
                        : !string.IsNullOrEmpty(EdgeId)
                            ? $"graph:{GraphId}/edge:{EdgeId}"
                            : !string.IsNullOrEmpty(DeclarationId)
                                ? $"graph:{GraphId}/declaration:{DeclarationId}"
                                : !string.IsNullOrEmpty(GraphId)
                                    ? $"graph:{GraphId}"
                                    : SourceType;
        public string ImmutableDataIdentity => !string.IsNullOrEmpty(PortId)
            ? $"{TemplateIdentity}/port:{PortId}"
            : TemplateIdentity;
        public string Identity => !string.IsNullOrEmpty(DisplayPath)
            ? DisplayPath
            : !string.IsNullOrEmpty(NodeId)
                ? $"{GraphId}/{NodeId}"
                : !string.IsNullOrEmpty(ClipId)
                    ? $"{TimelineId}/{ClipId}"
                    : GraphId;
    }
}
