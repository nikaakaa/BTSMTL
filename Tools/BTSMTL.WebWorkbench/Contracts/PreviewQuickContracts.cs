using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

public sealed record PreviewQuickRoot([property: JsonRequired] string Kind, [property: JsonRequired] string Path,
    string? ElementId = null, int Machine = 0);
public sealed record PreviewQuickTarget([property: JsonRequired] string ScenePath, PreviewQuickRoot? Root = null);
public sealed record PreviewQuickInputPoint(string InputId, double Time, JsonElement Value);
public sealed record PreviewQuickConditionPoint(string Key, double Time, bool Satisfied, bool RestoreDefault);
public sealed record PreviewQuickInput([property: JsonRequired] int Scheme, [property: JsonRequired] string Revision,
    PreviewQuickInputPoint[]? Points = null, PreviewQuickConditionPoint[]? Conditions = null);
public sealed record PreviewQuickRequest([property: JsonRequired] string Mode,
    [property: JsonRequired] PreviewQuickTarget Target, [property: JsonRequired] string SourceRevision,
    [property: JsonRequired] PreviewQuickInput Input, [property: JsonRequired] string OutputDirectory,
    double? RangeEndSeconds = null, int TimeoutSeconds = 180);

public sealed record PreviewQuickReply<T>(bool Success, bool Pending, string? Code, string? Message, T? Data, string RawJson);
public sealed record PreviewQuickDocument(string Path, string? SavedRevision, bool HasUnsavedChanges);
public sealed record PreviewQuickSource(string Kind, string Identity, string Path, string Revision,
    string SceneRevision, PreviewQuickDocument[] Documents);
public sealed record PreviewQuickSkill(string Directory, string DisplayName, string Identity);
public sealed record PreviewQuickScene(PreviewSourceFile Publication, string ScenePath, string SceneGuid, string SceneRevision);
public sealed record PreviewQuickTimeline(string Path, string ElementId, string Name, string Identity);
public sealed record PreviewQuickMachine(int Index, int Operation, string? EntryTimeline, string? EntryTimelineId);
public sealed record PreviewQuickScheme(int Index, string Name, string Revision, string? TimelineId);
public sealed record PreviewQuickInputDeclaration(string InputId, string Kind, string? Type);
public sealed record PreviewQuickInputRequest(string InputId, double BufferSeconds);
public sealed record PreviewQuickRootDescription(string Owner, string Label, PreviewQuickMachine[] Machines,
    PreviewQuickScheme[] InputSchemes, int CurrentInputScheme, string InputSchemeSource);
public sealed record PreviewQuickDescription(string ScenePath, string CarrierPath, string CarrierIdentity,
    int TickRate, PreviewQuickSkill[] Skills, PreviewQuickTimeline[] Timelines, PreviewQuickInputDeclaration[] Inputs,
    PreviewQuickInputRequest[] ActionRequests, PreviewQuickRootDescription? Root, PreviewQuickSource? Source, string Note);

public sealed record PreviewQuickLocation(string GraphId, string NodeId, string EdgeId, string DisplayPath);
public sealed record PreviewQuickSegment(string? Timeline, string? TimelineId, string? AssetPath, string State,
    PreviewQuickLocation? StateSource, double Start, double End, double LocalStart, double LocalEnd,
    int Playback, int Cycle, int Occurrence, bool PlaybackCompleted, int CallerPlayback, string CallerClipId);
public sealed record PreviewQuickCondition(string Key, string Label, string Source, int OperationIndex, bool Undeterminable);
public sealed record PreviewQuickMissingCondition(string Key, string Label);
public sealed record PreviewQuickSuggestion(string FromState, string ToState, string Transition,
    PreviewQuickLocation? FromSource, PreviewQuickLocation? ToSource, PreviewQuickLocation TransitionSource,
    double Start, double End, PreviewQuickMissingCondition[] Missing);
public sealed record PreviewQuickClock(string Unit, double Origin, string Owner, int TickRate);
public sealed record PreviewQuickPlan(string Root, string SourceType, PreviewQuickClock Clock,
    int InputScheme, string InputSchemeRevision, string InputRevision, PreviewQuickConditionPoint[] ConditionsUsed,
    string[] Assumptions, double Duration, string? StopReason, string? StopDetail, string Status,
    PreviewQuickSegment[] Segments, PreviewQuickCondition[] Conditions, PreviewQuickSuggestion[] Suggestions,
    PreviewQuickInputDeclaration[] DeclaredInputs, PreviewQuickInputPoint[] InputsUsed, string InputSource);
public sealed record PreviewQuickStatus(string JobId, string Stage, bool Completed, bool CancellationRequested,
    string NativeMode, string? AdoptedMode, PreviewQuickSource? Source, int SamplesDone, int SamplesTotal,
    string? OutputDirectory, string? SummaryPath, PreviewQuickPlan? Plan, string? Error,
    string[] CleanupErrors, string[] Notes, string[] LogErrors);
