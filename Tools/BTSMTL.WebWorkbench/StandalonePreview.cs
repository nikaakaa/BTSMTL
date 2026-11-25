using BTSMTL.Authoring.Workspace;
using BTSMTL.Compilation.Analysis;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json.Nodes;

namespace BTSMTL.WebWorkbench;

sealed class StandalonePreview(AuthoringDocumentService documents) : IPreviewHost<object, object>, IPreviewQuickAdapter, IPreviewQuickEnvironment
{
    internal const int TickRate = 60;
    static readonly TimelineContractCatalog s_TimelineContracts = TimelineContractCatalog.ForAuthoring([TreeTimelineContracts.CreateAuthoringProvider()]);

    public Task<PreviewHostDescription> Describe(CancellationToken cancellation) => Task.FromResult(
        new PreviewHostDescription("BTSMTL", "Timeline 帧", [], new("rangeEndSeconds", "路线范围（秒）", "0", "1", "10"))
        {
            QuickAuthoring = true,
            Modes = [new("off", "不预览", true, "只编辑作者内容。"),
                new("quick", "快速预览", true, "按当前 Timeline 和片段图绑定计算播放路线，在时间轴上播放和定位。")]
        });

    public object Sources() => new
    {
        scenes = Array.Empty<PreviewSourceFile>(), timelines = Array.Empty<object>(), replays = Array.Empty<object>(),
        quickScenes = Array.Empty<PreviewQuickScene>(), sceneDirectory = "", replayDirectory = "", runDirectory = ""
    };

    public object Inputs(string target, string revision) => throw new NotSupportedException("作者路线预览没有项目场景输入。");

    public Task<IPreviewQuickEnvironment> Open(PreviewQuickTarget target, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (target.ScenePath is not null || target.ScenePublication is not null)
            throw new ArgumentException("独立工作区的预览入口需要作者 Timeline。");
        return Task.FromResult<IPreviewQuickEnvironment>(this);
    }

    string? IPreviewQuickEnvironment.ScenePath => null;
    string IPreviewQuickEnvironment.CarrierPath => "";
    string IPreviewQuickEnvironment.CarrierIdentity => "";
    string IPreviewQuickEnvironment.ActorId => "";
    string IPreviewQuickEnvironment.ActorScope => "";
    int IPreviewQuickEnvironment.TickRate => TickRate;
    IReadOnlyList<GraphHostInputDescriptor> IPreviewQuickEnvironment.Inputs => [];
    public IReadOnlyList<TimelineStaticInputRequest> Requests => [];
    public IReadOnlyDictionary<string, OrchestrationValueKind> InputKinds { get; } = new Dictionary<string, OrchestrationValueKind>();
    public string? SelectPlayerBuild(IEnumerable<JsonNode> builds) => null;
    public IPreviewInputKeyAdapter Keys => throw new NotSupportedException("作者路线预览没有项目按键输入。");

    async Task<PreviewQuickDocumentChoice[]> IPreviewQuickEnvironment.Documents() => Choices(await documents.Repository.Read());

    public async Task<IPreviewQuickCapture> Capture(PreviewQuickRoot root, Action<string>? mark, CancellationToken cancellation)
    {
        PreviewQuickAuthoringTarget target = root.Authoring;
        IReadOnlyList<AuthoringDocumentSnapshot> saved = await documents.Repository.Read(cancellation);
        AuthoringDocumentSnapshot previous = Find(saved, target.DocumentPath);
        if (target.SavedRevision != previous.Revision)
            throw new InvalidOperationException("Timeline 的保存修订已变化，请重新读取预览来源。");
        IReadOnlyList<AuthoringDocumentSnapshot> view = root is PreviewQuickWebRoot web
            ? documents.Repository.EditingView(saved, documents.Writes(web.EditingView)) : saved;
        AuthoringDocumentSnapshot document = Find(view, target.DocumentPath);
        if (target.Entry is not PreviewQuickTimelineEntry entry || entry.TimelineId != document.Identity)
            throw new ArgumentException("预览入口不属于所选作者 Timeline。");
        var snapshot = new PreviewQuickDocument(document.Kind.Id, document.DocumentPath, document.Identity,
            document.Revision, previous.Revision, document.Revision != previous.Revision);
        string kind = root is PreviewQuickWebRoot ? "webEditingView" : "authoringDocument";
        var identity = new PreviewQuickSource(kind, target, document.Revision, null, [snapshot]);
        return new CaptureView(document, identity, Choices(saved));
    }

    static AuthoringDocumentSnapshot Find(IReadOnlyList<AuthoringDocumentSnapshot> view, string path) => view
        .FirstOrDefault(document => document.Kind == AuthoringDocumentKind.Timeline && document.DocumentPath == path)
        ?? throw new FileNotFoundException($"找不到作者 Timeline {path}。");

    static PreviewQuickDocumentChoice[] Choices(IReadOnlyList<AuthoringDocumentSnapshot> view) => view
        .Where(document => document.Kind == AuthoringDocumentKind.Timeline).Select(document =>
        {
            string name = (string)document.Root["m_Name"]!;
            return new PreviewQuickDocumentChoice(document.Kind.Id, document.DocumentPath, document.Identity, name, document.Revision,
                [new(new PreviewQuickTimelineEntry(document.Identity), name, true)]);
        }).ToArray();

    sealed class CaptureView(AuthoringDocumentSnapshot document, PreviewQuickSource identity, PreviewQuickDocumentChoice[] choices) : IPreviewQuickCapture
    {
        public PreviewQuickSource Identity => identity;
        public string Label => (string)document.Root["m_Name"]!;
        public string ExecutionContextIdentity => document.Identity;
        public string ContentKey => document.Revision;
        public string ScopeKey => document.DocumentPath;
        public string SceneApplyUnavailableReason => "作者路线未关联项目场景。";
        public TimelineContractCatalog TimelineContracts => s_TimelineContracts;
        public IReadOnlyList<PreviewQuickSpawn> Spawns => [];
        public PreviewQuickDocumentChoice[] Documents() => choices;
        public PreviewQuickInputPlan[] InputPlans() => [];
        public string GraphOwner(string graphId) => document.DocumentPath;
        public string TimelineOwner(string timelineId) => document.DocumentPath;
        public Task<IPreviewQuickCapture> CaptureSpawn(PreviewQuickSpawn spawn, CancellationToken cancellation) =>
            throw new NotSupportedException("作者路线预览没有项目对象生成。");
        public JObject RoutePayload(TimelineStaticRoute route) => throw new NotSupportedException("作者路线预览无需发布播放器内容。");

        public TimelineData ReadTimeline(string timelineId)
        {
            var serializer = new JsonSerializer { DateParseHandling = DateParseHandling.None };
            object ReadValue(JToken token, Type type)
            {
                if (type == typeof(List<TimelineMarker>))
                    return token.Select(item => (TimelineMarker)ReadValue(item, typeof(TimelineMarker))).ToList();
                if (type != typeof(TreeClip) && type != typeof(TimelineMarker))
                    return token.ToObject(type, serializer)!;
                object instance = Activator.CreateInstance(type)!;
                foreach (var field in TimelineJson.StoredFields(type))
                    if (token[field.Key] is { } value)
                        field.Value.SetValue(instance, ReadValue(value, field.Value.FieldType));
                return instance;
            }

            TimelineData timeline = TimelineJson.Read(document.Root, ReadValue);
            timeline.Init();
            timeline.BindContentFingerprint(document.FileRevision(document.Kind.RootFile));
            return timeline;
        }

        public IPreviewQuickCompiledContent Compile()
        {
            TimelineData timeline = ReadTimeline(document.Identity);
            var references = timeline.Tracks.SelectMany(track => track.Clips
                .OfType<ITimelineTreeClipAuthoringSource>().Select(clip => (Id: clip.AssetTreeId, Role: StandaloneAuthoring.TimelineBody))
                .Concat(track.Markers.Select(marker => (Id: marker.GraphId, Role: StandaloneAuthoring.TimelineTrigger)))).Distinct();
            var sources = new Dictionary<string, IReadOnlyList<TimelineContentDependency>>(StringComparer.Ordinal);
            foreach (var (id, role) in references)
            {
                string file = $"graphs/{id}.json";
                JObject? graph = document.Files.ContainsKey(file) ? document.Read(file) : null;
                if ((string?)graph?["AuthoringId"] != id || (string?)graph?["Role"] != role)
                    throw new InvalidDataException($"Timeline「{timeline.Name}」引用的图「{id}」需要本文档内的 {role} 图。");
                sources.Add(id, [new(TimelineGraphBinding.TreeDependencyIdentity(id), TimelineGraphBinding.TreeDependencyKind,
                    Path.Combine(document.DirectoryPath, file), document.FileRevision(file))]);
            }
            TimelineContentDiscoveryResult result = TimelineContentDiscovery.Discover(timeline, TimelineContracts, new TimelineGraphSourceBindings(sources));
            if (!result.IsValid)
                throw new InvalidDataException(string.Join("\n", result.Errors));
            return new CompiledContent([result.Content]);
        }
    }

    sealed record CompiledContent(IReadOnlyList<TimelineContentUnit> Timelines) : IPreviewQuickCompiledContent
    {
        public IReadOnlyDictionary<string, OrchestrationStaticQueryInput> Graphs { get; } = new Dictionary<string, OrchestrationStaticQueryInput>();
        public IReadOnlyList<PreviewQuickProgramInput> Programs => [];
        public PreviewQuickPublication Publish(PreviewQuickPublishRequest request) =>
            throw new NotSupportedException("作者路线预览无需发布播放器内容。");
    }
}
