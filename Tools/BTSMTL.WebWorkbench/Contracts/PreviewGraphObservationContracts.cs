namespace BTSMTL.WebWorkbench;

public sealed record PreviewModeAvailability(string Id, string DisplayName, bool Available, string Reason)
{
    public bool CreatesRun { get; init; }
    public string[] DefaultChannels { get; init; } = [];
}
public sealed record PreviewObservationAvailability(string Channel, bool Available, bool Complete, string Reason)
{
    public PreviewDiagnosticCoverage[]? Coverage { get; init; }
}
public sealed record PreviewDiagnosticCoverage(int ObjectSlot, string? FirstFrame, string? LastFrame,
    int LogicFrameCount, int PresentationEvaluationCount, bool StartedBeforeExecution,
    string PublishedEventCount, string ArchivedEventCount, string FrameMappedEventCount,
    string DiscardedTraceEventCount, string EvictedStateCount);
public sealed record PreviewExecutionCall(string? ActorId, string ProgramId, string ProgramRevision,
    string InvocationPath, string? ParentInvocationPath, string ActionId, string ExecutionGeneration,
    string InvocationGeneration, string? ParentInvocationGeneration, string PlaybackId)
{
    public int? ObjectSlot { get; init; }
    public string? ObjectHandle { get; init; }
    public string? ObjectKind { get; init; }
    public int? DefinitionIndex { get; init; }
    public int? ProgramIndex { get; init; }
    public string? ExecutionBranchId { get; init; }
    public string? RuntimeEpoch { get; init; }
    public PreviewObservationAvailability? BranchAvailability { get; init; }
    public PreviewObservationAvailability? ObjectAvailability { get; init; }
}
public sealed record PreviewNodeOccurrence(string Sequence, string GraphId, string NodeId,
    string Kind, string? Generation, string? Result, PreviewExecutionCall? Call)
{
    public int? LoopIteration { get; init; }
    public PreviewLoopScope? LoopScope { get; init; }
    public PreviewObservationAvailability? ActivationAvailability { get; init; }
    public PreviewObservationAvailability? LoopScopeAvailability { get; init; }
    public uint? StopCause { get; init; }
    public string? StopMode { get; init; }
}
public sealed record PreviewLoopScope(string GraphId, string NodeId, string? Generation, int Iteration,
    PreviewExecutionCall Call)
{
    public PreviewObservationAvailability? ActivationAvailability { get; init; }
}
public sealed record PreviewEdgeOccurrence(string Sequence, string GraphId, string EdgeId,
    bool Selected, bool Passed, PreviewExecutionCall? Call)
{
    public string? SourceNodeId { get; init; }
    public string? Generation { get; init; }
    public PreviewLoopScope? LoopScope { get; init; }
    public PreviewObservationAvailability? ActivationAvailability { get; init; }
    public PreviewObservationAvailability? LoopScopeAvailability { get; init; }
}
public sealed record PreviewPortSample(string Sequence, string GraphId, string NodeId, string PortId,
    string Direction, string ValueType, string Value, string RawJson, PreviewExecutionCall? Call)
{
    public bool SourceAvailable { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? Generation { get; init; }
    public string? ObservationSequence { get; init; }
    public PreviewLoopScope[]? LoopScopes { get; init; }
    public PreviewObservationAvailability? ActivationAvailability { get; init; }
    public PreviewObservationAvailability? LoopScopeAvailability { get; init; }
}
public sealed record PreviewRoutePage(string RunId, string ContentRevision, string Protocol,
    PreviewObservationAvailability[] Availability, string FirstFrame, string LastFrame,
    int Offset, int Total, int? NextOffset, PreviewRouteFrame[] Items);
