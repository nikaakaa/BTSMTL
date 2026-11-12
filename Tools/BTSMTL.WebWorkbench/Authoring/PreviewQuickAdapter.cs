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
    // 场景角色对应的作者执行范围（适配者目录 execution-scope 的值），快预览按它解析资源与映射。
    string ActorScope { get; }
    int TickRate { get; }
    IReadOnlyList<GraphHostInputDescriptor> Inputs { get; }
    IReadOnlyList<TimelineStaticInputRequest> Requests { get; }
    IReadOnlyDictionary<string, OrchestrationValueKind> InputKinds { get; }
    Task<PreviewQuickDocumentChoice[]> Documents();
    Task<IPreviewQuickCapture> Capture(PreviewQuickRoot root, Action<string>? mark, CancellationToken cancellation);
    string? SelectPlayerBuild(IEnumerable<JsonNode> builds);
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
    string? SceneApplyUnavailableReason { get; }
    PreviewQuickDocumentChoice[] Documents();
    IPreviewQuickCompiledContent Compile();
    PreviewQuickInputPlan[] InputPlans();
    string GraphOwner(string graphId);
    string TimelineOwner(string timelineId);
    TimelineData ReadTimeline(string timelineId);
    JObject RoutePayload(TimelineStaticRoute route);
    TimelineContractCatalog TimelineContracts { get; }
    // 作者图里执行时会生成另一对象的节点；被生成对象运行自己的根图，快预览把它推演成独立实例。
    IReadOnlyList<PreviewQuickSpawn> Spawns { get; }
    Task<IPreviewQuickCapture> CaptureSpawn(PreviewQuickSpawn spawn, CancellationToken cancellation);
}

// GraphId／NodeId：生成节点的作者位置；Carrier：被生成对象的载体名；DocumentPath：它运行的根图所属作者文档。
public sealed record PreviewQuickSpawn(string GraphId, string NodeId, string Carrier, string DocumentPath);

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
