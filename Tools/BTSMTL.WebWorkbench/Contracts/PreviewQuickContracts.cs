using System.Text.Json;
using System.Text.Json.Serialization;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.WebWorkbench;

public static class PreviewQuickProtocol
{
    public const string Identity = "btsmtl.native-timeline-preview/4";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PreviewQuickGraphEntry), "graph")]
[JsonDerivedType(typeof(PreviewQuickTimelineEntry), "timeline")]
public abstract record PreviewQuickEntry;
public sealed record PreviewQuickGraphEntry([property: JsonRequired] string GraphId, int Machine = 0) : PreviewQuickEntry;
public sealed record PreviewQuickTimelineEntry([property: JsonRequired] string TimelineId) : PreviewQuickEntry;
public sealed record PreviewQuickAuthoringTarget([property: JsonRequired] string DocumentPath,
    [property: JsonRequired] string SavedRevision, [property: JsonRequired] PreviewQuickEntry Entry);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PreviewQuickNativeRoot), "nativeDocument")]
[JsonDerivedType(typeof(PreviewQuickWebRoot), "webEditingView")]
[JsonDerivedType(typeof(PreviewQuickDocumentRoot), "authoringDocument")]
public abstract record PreviewQuickRoot([property: JsonRequired] PreviewQuickAuthoringTarget Authoring);
public sealed record PreviewQuickNativeRoot(PreviewQuickAuthoringTarget Authoring) : PreviewQuickRoot(Authoring);
public sealed record PreviewQuickWebRoot(PreviewQuickAuthoringTarget Authoring,
    [property: JsonRequired] AuthoringDocumentEditingView EditingView) : PreviewQuickRoot(Authoring);
public sealed record PreviewQuickDocumentRoot(PreviewQuickAuthoringTarget Authoring) : PreviewQuickRoot(Authoring);

public sealed record PreviewQuickTarget([property: JsonRequired] string ScenePath, PreviewQuickRoot? Root = null,
    PreviewSourceFile? ScenePublication = null);
public sealed record PreviewQuickInputPoint(string InputId, double Time, JsonElement Value);
public sealed record PreviewQuickConditionPoint(string Key, double Time, bool Satisfied, bool RestoreDefault);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PreviewQuickDocumentInput), "documentInput")]
[JsonDerivedType(typeof(PreviewQuickRootInputs), "rootDefaults")]
public abstract record PreviewQuickInputSource;
public sealed record PreviewQuickDocumentInput([property: JsonRequired] string DocumentPath,
    [property: JsonRequired] string SavedRevision, [property: JsonRequired] string InputId,
    [property: JsonRequired] string SchemeId) : PreviewQuickInputSource;
public sealed record PreviewQuickRootInputs : PreviewQuickInputSource;
// SourceRevision 与 Input.Revision 为 null 表示跟随当前内容：宿主只捕获一次，按本次捕获的来源与方案推导，
// 并在作业状态里交回实际修订和完整来源描述；非 null 时须与本次捕获一致。
public sealed record PreviewQuickInput([property: JsonRequired] PreviewQuickInputSource Source,
    string? Revision, PreviewQuickInputPoint[]? Points = null,
    PreviewQuickConditionPoint[]? Conditions = null);
public sealed record PreviewQuickRequest([property: JsonRequired] string Mode,
    [property: JsonRequired] PreviewQuickTarget Target, string? SourceRevision,
    [property: JsonRequired] PreviewQuickInput Input, [property: JsonRequired] string OutputDirectory,
    double? RangeEndSeconds = null, int TimeoutSeconds = 180);
public sealed record PreviewQuickSeekRequest([property: JsonRequired] string RequestId,
    [property: JsonRequired] string SourceRevision, [property: JsonRequired] string InputRevision,
    [property: JsonRequired] double Seconds);

public sealed record PreviewQuickReply<T>(bool Success, bool Pending, string? Code, string? Message, T? Data, string RawJson);
public sealed record PreviewQuickDocument(string Kind, string DocumentPath, string Identity, string Revision,
    string SavedRevision, bool HasUnsavedChanges);
public sealed record PreviewQuickSource(string Kind, PreviewQuickAuthoringTarget Authoring, string Revision,
    string SceneRevision, PreviewQuickDocument[] Documents);
public sealed record PreviewQuickEntryChoice(PreviewQuickEntry Entry, string DisplayName);
public sealed record PreviewQuickDocumentChoice(string Kind, string DocumentPath, string Identity,
    string DisplayName, string SavedRevision, PreviewQuickEntryChoice[] Entries);
public sealed record PreviewQuickScene(PreviewSourceFile Publication, string ScenePath, string SceneGuid, string SceneRevision);
public sealed record PreviewQuickMachine(PreviewQuickAuthoringTarget Authoring, int Operation,
    string? EntryTimeline, string? EntryTimelineId);
public sealed record PreviewQuickScheme(PreviewQuickInputSource Source, string Name, string Revision, string? TimelineId);
public sealed record PreviewQuickInputDeclaration(string InputId, string Kind, string? Type);
public sealed record PreviewQuickInputRequest(string InputId, double BufferSeconds);
public sealed record PreviewQuickRootDescription(PreviewQuickAuthoringTarget Authoring, string Label,
    PreviewQuickMachine[] Machines, PreviewQuickScheme[] InputSchemes, PreviewQuickInputSource CurrentInputScheme);
public sealed record PreviewQuickDescription(string ScenePath, string CarrierPath, string CarrierIdentity,
    int TickRate, PreviewQuickDocumentChoice[] Documents, PreviewQuickInputDeclaration[] Inputs,
    PreviewQuickInputRequest[] ActionRequests, PreviewQuickRootDescription? Root, PreviewQuickSource? Source, string Note);

public sealed record PreviewQuickLocation(string DocumentPath, string GraphId, string NodeId, string EdgeId, string DisplayPath);
public sealed record PreviewQuickSegment(string? Timeline, string? TimelineId, string? DocumentPath, string State,
    PreviewQuickLocation? StateSource, double Start, double End, double LocalStart, double LocalEnd,
    int Playback, int Cycle, int Occurrence, bool PlaybackCompleted, int CallerPlayback, string CallerClipId);
public sealed record PreviewQuickCondition(string Key, string Label, string Source, int OperationIndex, bool Undeterminable);
public sealed record PreviewQuickMissingCondition(string Key, string Label);
public sealed record PreviewQuickSuggestion(string FromState, string ToState, string Transition,
    PreviewQuickLocation? FromSource, PreviewQuickLocation? ToSource, PreviewQuickLocation TransitionSource,
    double Start, double End, PreviewQuickMissingCondition[] Missing);
public sealed record PreviewQuickClock(string Unit, double Origin, string Owner, int TickRate);
public sealed record PreviewQuickPlan(string Root, string SourceType, PreviewQuickClock Clock,
    PreviewQuickInputSource Input, string InputSchemeRevision, string InputRevision, PreviewQuickConditionPoint[] ConditionsUsed,
    string[] Assumptions, double Duration, string? StopReason, string? StopDetail, string Status,
    PreviewQuickSegment[] Segments, PreviewQuickCondition[] Conditions, PreviewQuickSuggestion[] Suggestions,
    PreviewQuickInputDeclaration[] DeclaredInputs, PreviewQuickInputPoint[] InputsUsed, string InputSource);
public sealed record PreviewQuickStatus(string JobId, string Stage, bool Completed, bool CancellationRequested,
    string NativeMode, string? AdoptedMode, PreviewQuickSource? Source, int SamplesDone, int SamplesTotal,
    string? OutputDirectory, string? SummaryPath, PreviewQuickPlan? Plan, string? Error,
    string[] CleanupErrors, string[] Notes, string[] LogErrors, PreviewQuickFrame? Frame, PreviewQuickDescription? Description);
public sealed record PreviewQuickFrame(string JobId, string RequestId, string SourceRevision, string InputRevision,
    double RequestedSeconds, double SampledSeconds, string SampledRevision, string MediaType, string ImageBase64,
    [property: JsonRequired] bool FlipY);
