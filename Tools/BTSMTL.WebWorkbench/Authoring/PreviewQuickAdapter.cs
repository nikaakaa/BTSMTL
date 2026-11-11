using System.Text.Json;
using System.Text.Json.Nodes;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public interface IPreviewQuickAdapter
{
    Task<IPreviewQuickEnvironment> Open(PreviewQuickTarget target, CancellationToken cancellation);
}

public interface IPreviewQuickEnvironment
{
    string ScenePath { get; }
    string CarrierPath { get; }
    string CarrierIdentity { get; }
    string ActorId { get; }
    int TickRate { get; }
    IReadOnlyList<GraphHostInputDescriptor> Inputs { get; }
    IReadOnlyList<TimelineStaticInputRequest> Requests { get; }
    IReadOnlyDictionary<string, OrchestrationValueKind> InputKinds { get; }
    Task<PreviewQuickDocumentChoice[]> Documents();
    Task<IPreviewQuickCapture> Capture(PreviewQuickRoot root, Action<string>? mark, CancellationToken cancellation);
    string SelectPlayerBuild(IEnumerable<JsonNode> builds);
    IPreviewInputKeyAdapter Keys { get; }
}

public interface IPreviewInputKeyAdapter
{
    PreviewQuickKeyWrite? ReadKey(JObject node, float time, string element);
    PreviewQuickInputPoint[] ConvertKeys(IReadOnlyList<PreviewQuickKeyWrite> writes);
}

public interface IPreviewQuickCapture
{
    PreviewQuickSource Identity { get; }
    string Label { get; }
    string ExecutionContextIdentity { get; }
    string ContentKey { get; }
    string ScopeKey { get; }
    PreviewQuickDocumentChoice[] Documents();
    IPreviewQuickCompiledContent Compile();
    PreviewQuickInputPlan[] InputPlans();
    string GraphOwner(string graphId);
    string TimelineOwner(string timelineId);
    TimelineData ReadTimeline(string timelineId);
    JObject RoutePayload(TimelineStaticRoute route);
    TimelineContractCatalog TimelineContracts { get; }
}

public sealed record PreviewQuickInputPlan(string DocumentPath, string SavedRevision, string Id,
    string Revision, JObject Value, Func<string, JObject> Timeline, Func<string, JObject> Graph, bool AllowsConditions,
    bool IsRootDefault);
public sealed record PreviewQuickKeyWrite(float Time, string Key, JsonElement State, string Element);
public sealed record PreviewQuickInvocation(int EntryOperation, int TargetOperation);
public sealed record PreviewQuickProgramInput(string GraphId, JObject Parameters, int[] ConditionOperations,
    PreviewQuickInvocation[] Invocations);
public sealed record PreviewQuickPublication(string ManifestPath, string Revision, JsonElement Content);
public sealed record PreviewQuickPublishRequest(string OutputDirectory, PreviewQuickSource Source, string InputRevision,
    TimelineStaticRoute Route, JObject Input, JArray Programs, Func<string, string, bool> ReuseMember);

public interface IPreviewQuickCompiledContent
{
    IReadOnlyList<TimelineContentUnit> Timelines { get; }
    IReadOnlyDictionary<string, OrchestrationStaticQueryInput> Graphs { get; }
    IReadOnlyList<PreviewQuickProgramInput> Programs { get; }
    PreviewQuickPublication Publish(PreviewQuickPublishRequest request);
}
