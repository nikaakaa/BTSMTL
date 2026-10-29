namespace BTSMTL.WebWorkbench;

public sealed record PreviewModeAvailability(string Id, string DisplayName, bool Available, string Reason);
public sealed record PreviewObservationAvailability(string Channel, bool Available, bool Complete, string Reason);
public sealed record PreviewExecutionCall(string ActorId, string ProgramId, string ProgramRevision,
    string InvocationPath, string ParentInvocationPath, string ActionId, string ExecutionGeneration,
    string InvocationGeneration, string ParentInvocationGeneration, string PlaybackId);
public sealed record PreviewNodeOccurrence(string Sequence, string GraphId, string NodeId,
    string Kind, string Generation, string Result, PreviewExecutionCall Call);
public sealed record PreviewEdgeOccurrence(string Sequence, string GraphId, string EdgeId,
    bool Selected, bool Passed, PreviewExecutionCall Call);
public sealed record PreviewPortSample(string Sequence, string GraphId, string NodeId, string PortId,
    string Direction, string ValueType, string Value, string RawJson, PreviewExecutionCall Call)
{
    public bool SourceAvailable { get; init; }
    public string Reason { get; init; } = string.Empty;
}
public sealed record PreviewGraphEndpoint(string GraphId, string NodeId, string PortId);
public sealed record PreviewGraphPosition(double X, double Y);
public sealed record PreviewGraphNode(string Id, string? DisplayName, string InvocationPath)
{
    public PreviewGraphPosition? Position { get; init; }
}
public sealed record PreviewGraphPort(string NodeId, string Id, string DisplayName, string Direction,
    bool IsTrigger, string InvocationPath);
public sealed record PreviewGraphEdge(string Id, string Kind, string InvocationPath,
    PreviewGraphEndpoint? Source, PreviewGraphEndpoint? Target, bool Available, string Reason);
public sealed record PreviewGraphDefinition(string ActorId, string ProgramId, string ProgramRevision,
    string Id, string? DisplayName, PreviewGraphNode[] Nodes, PreviewGraphPort[] Ports, PreviewGraphEdge[] Edges,
    PreviewObservationAvailability[] Availability);
public sealed record PreviewGraphPage(string RunId, string ContentRevision, string Protocol,
    PreviewObservationAvailability[] Availability, int Offset, int Total, int? NextOffset,
    PreviewGraphDefinition[] Items);
public sealed record PreviewRoutePage(string RunId, string ContentRevision, string Protocol,
    PreviewObservationAvailability[] Availability, string FirstFrame, string LastFrame,
    int Offset, int Total, int? NextOffset, PreviewRouteFrame[] Items);
