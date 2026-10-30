namespace BTSMTL.WebWorkbench;

public sealed record PreviewModeAvailability(string Id, string DisplayName, bool Available, string Reason)
{
    public bool CreatesRun { get; init; }
    public string[] DefaultChannels { get; init; } = [];
}
public sealed record PreviewObservationAvailability(string Channel, bool Available, bool Complete, string Reason);
public sealed record PreviewExecutionCall(string ActorId, string ProgramId, string ProgramRevision,
    string InvocationPath, string? ParentInvocationPath, string ActionId, string ExecutionGeneration,
    string InvocationGeneration, string? ParentInvocationGeneration, string PlaybackId);
public sealed record PreviewNodeOccurrence(string Sequence, string GraphId, string NodeId,
    string Kind, string Generation, string? Result, PreviewExecutionCall? Call);
public sealed record PreviewEdgeOccurrence(string Sequence, string GraphId, string EdgeId,
    bool Selected, bool Passed, PreviewExecutionCall? Call);
public sealed record PreviewPortSample(string Sequence, string GraphId, string NodeId, string PortId,
    string Direction, string ValueType, string Value, string RawJson, PreviewExecutionCall? Call)
{
    public bool SourceAvailable { get; init; }
    public string Reason { get; init; } = string.Empty;
}
public sealed record PreviewRoutePage(string RunId, string ContentRevision, string Protocol,
    PreviewObservationAvailability[] Availability, string FirstFrame, string LastFrame,
    int Offset, int Total, int? NextOffset, PreviewRouteFrame[] Items);
