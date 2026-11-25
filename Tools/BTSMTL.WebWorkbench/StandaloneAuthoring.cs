using System.Text.Json;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Workspace;
using BTSMTL.Compilation;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

sealed class StandaloneAuthoring : IAuthoringDocumentAdapter
{
    static readonly Type[] s_FlowNodes = [typeof(BtsmtlEntryFlowNode), typeof(BtsmtlBranchFlowNode)];
    static readonly Type[] s_TimelineTypes = [typeof(TimelineData), typeof(TreeTrack), typeof(TreeClip), typeof(TimelineMarker)];
    internal const string TimelineBody = "TimelineBody";
    internal const string TimelineTrigger = "TimelineTrigger";
    static readonly (AuthoringDocumentKind Kind, string Label, Type Type)[] s_DocumentTypes =
    [
        (AuthoringDocumentKind.Graph, "图文档", typeof(BtsmtlGraphDocument)),
        (AuthoringDocumentKind.Timeline, "Timeline", typeof(TimelineData)),
        (AuthoringDocumentKind.Variables, "变量声明", typeof(BtsmtlGraphVariableDeclarations))
    ];

    public StandaloneAuthoring(string workspace) => Documents = new(workspace, this);

    public AuthoringDocumentService Documents { get; }

    public IReadOnlyList<AuthoringDocumentType> DocumentTypes { get; } = s_DocumentTypes.Select(item =>
        new AuthoringDocumentType(item.Kind.Id, item.Label, item.Type, item.Kind.Directory, item.Kind.RootFile)).ToArray();

    public Type? AuthoringType(AuthoringDocumentSnapshot document) => s_DocumentTypes.SingleOrDefault(item => item.Kind == document.Kind).Type;

    public IReadOnlyDictionary<string, string> StorageRoles(Type authoringType) => authoringType == typeof(BtsmtlGraphDocument)
        ? new Dictionary<string, string> { ["displayName"] = "name" }
        : authoringType == typeof(TimelineData) ? new Dictionary<string, string> { ["displayName"] = "m_Name" }
        : new Dictionary<string, string>();

    public IReadOnlyList<AuthoringDocumentElement> DeclaredElements(AuthoringDocumentSnapshot owner) => [];

    public string? DefaultEntry(AuthoringDocumentSnapshot document) => document.Kind == AuthoringDocumentKind.Graph ||
        document.Kind == AuthoringDocumentKind.Timeline ? document.Identity : null;

    public JToken? StorageShape(Type authoringType) => null;

    public AuthoringDocumentWrite[] PrepareSave(string workspace, AuthoringDocumentWrite[] writes,
        IReadOnlyList<AuthoringDocumentSnapshot> documents, AuthoringDocumentEditingView request) => writes.Select(write =>
    {
        if (write.IsDeletion || write.Kind != AuthoringDocumentKind.Graph && write.Kind != AuthoringDocumentKind.Timeline)
            return write;
        AuthoringDocumentSnapshot? saved = documents.FirstOrDefault(document => document.DirectoryPath == write.DirectoryPath);
        var files = new Dictionary<string, string>(write.Files, StringComparer.Ordinal);
        foreach (string file in GraphFiles(write.Kind, write.Files.Keys))
        {
            string? text = saved?.Files.GetValueOrDefault(file);
            if (text == write.Files[file]) continue;
            JObject edited = AuthoringJsonText.Parse(write.Files[file]);
            BtsmtlGraphDocument graph = BtsmtlGraphDocumentJson.Read(text is null ? edited : AuthoringJsonText.Parse(text));
            if (text is not null) graph.Replace(edited);
            files[file] = AuthoringJsonText.Text(BtsmtlGraphDocumentJson.Write(graph));
        }
        return new AuthoringDocumentWrite(write.Kind, write.DirectoryPath, write.ExpectedRevision, files);
    }).ToArray();

    public JToken VariableTypeChoices() => AuthoringVariableChoices.Describe([], []);

    public async Task<AuthoringQueryResult> Document(string path, SkillAuthoringPageQuery input)
    {
        IReadOnlyList<AuthoringDocumentSnapshot> captured = await Documents.Capture(input.EditingView);
        AuthoringDocumentSnapshot document = FindDocument(captured, path);
        JObject? layout = document.Files.ContainsKey("layout.json") ? document.Read("layout.json") : null;
        AuthoringGraphProjection[] graphs = GraphFiles(document.Kind, document.Files.Keys)
            .Select(file => ProjectGraph(document, file, layout)).ToArray();
        SkillAuthoringFile[] files = document.Files.OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => new SkillAuthoringFile(file.Key, file.Value)).ToArray();
        var source = new SkillAuthoringSnapshot(document.DocumentPath, document.Revision, files, files,
            AuthoringQueryResults.Documents(Documents, [document])[0]);
        return new(new
        {
            skill = document.DocumentPath, programId = DefaultEntry(document),
            sourceRevision = document.Revision, inputRevision = document.Revision, tickRate = StandalonePreview.TickRate,
            graphs, relations = Array.Empty<object>(), sources = new[] { source },
            elements = JsonSerializer.Deserialize<JsonElement>(AuthoringJsonText.Compact(Documents.Elements(captured, null)))
        });
    }

    public Task<AuthoringQueryResult> Types(string path, string? search, SkillAuthoringPageQuery input)
    {
        AuthoringQueryPage.Require(input.Offset, input.Limit);
        var nodes = BtsmtlNativeValueNodeCatalog.All.Select(node => (Name: node.NodeTypeName, Kind: node.Kind, ClrType: (string?)null))
            .Concat(s_FlowNodes.Select(type => (Name: type.Name, Kind: "flow", ClrType: type.FullName)))
            .Where(node => search is null || node.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(node => new JObject
            {
                ["type"] = node.Name, ["displayName"] = node.Name, ["clrType"] = node.ClrType,
                ["kind"] = node.Kind, ["family"] = "flow", ["hidden"] = false,
                ["category"] = node.ClrType is null ? "数值" : "流程", ["timelineDomains"] = null
            });
        return Task.FromResult(new AuthoringQueryResult(AuthoringQueryPage.Slice(nodes, input.Offset, input.Limit)));
    }

    public async Task<AuthoringQueryResult> Type(string path, SkillAuthoringTypeQuery input)
    {
        AuthoringDocumentSnapshot document = FindDocument(await Documents.Capture(input.EditingView), path);
        return new(DescribeType(input.Type, input.Configuration, document.Revision, input.Offset, input.Limit));
    }

    public Task<AuthoringQueryResult> Configuration(SkillAuthoringConfigurationQuery input) =>
        Task.FromResult(new AuthoringQueryResult(DescribeType(input.Type, input.Configuration, string.Empty, input.Offset, input.Limit)));

    public Task<AuthoringQueryResult> Elements(string path, AuthoringElementsQuery input) =>
        Task.FromResult(new AuthoringQueryResult(TimelineElements()));

    static JObject TimelineElements()
    {
        var track = new TreeTrack();
        var clip = new TreeClip();
        var timeline = TimelineData.CreateDefault(null);
        var marker = new TimelineMarker();
        var serializer = new Newtonsoft.Json.JsonSerializer();
        serializer.Converters.Add(new StringEnumConverter());
        JToken Field(object? value, Type _) => value is null ? JValue.CreateNull() : JToken.FromObject(value, serializer);
        JObject Element(object instance, string? kind) => new()
        {
            ["kind"] = kind, ["clrType"] = instance.GetType().FullName, ["shape"] = null,
            ["value"] = new JObject(TimelineJson.StoredFields(instance.GetType())
                .Select(field => new JProperty(field.Key, Field(field.Value.GetValue(instance), field.Value.FieldType))))
        };
        JObject typedTrack = Element(track, track.ContractKind);
        typedTrack["value"]![TimelineJson.ContractKindMember] = track.ContractKind;
        JObject typedClip = Element(clip, clip.ContractKind);
        typedClip["value"]![TimelineJson.ContractKindMember] = clip.ContractKind;
        return new JObject
        {
            ["availability"] = new JObject { ["available"] = true, ["reason"] = null },
            ["graphs"] = new JArray(GraphTemplate(TimelineBody, "Root"), GraphTemplate(TimelineTrigger, "OnEnable")),
            ["anchors"] = new JObject(), ["flowOutputs"] = new JArray(),
            ["state"] = null, ["transition"] = null, ["blackboard"] = null,
            ["referenceNodes"] = new JArray(),
            ["timeline"] = new JObject { ["kind"] = null, ["clrType"] = typeof(TimelineData).FullName,
                ["value"] = TimelineJson.Write(timeline, Field), ["shape"] = null },
            ["tracks"] = new JArray(AuthoringTimelineTemplates.Track(typedTrack, TreeTimelineDefinition.Track)),
            ["clips"] = new JArray(AuthoringTimelineTemplates.Clip(typedClip, clip, TreeTimelineDefinition.Clip, null)),
            ["marker"] = Element(marker, null),
            ["previewInput"] = null
        };
    }

    static JObject GraphTemplate(string role, string entry)
    {
        string id = Guid.NewGuid().ToString("N");
        string nodeId = Guid.NewGuid().ToString("N");
        return new JObject
        {
            ["role"] = role, ["seed"] = "Anchors", ["ownership"] = null,
            ["variables"] = BtsmtlGraphVariableDeclarationsJson.Write(new BtsmtlGraphVariableDeclarations(id, Guid.NewGuid().ToString("N"), [])),
            ["requiredContext"] = new JArray(),
            ["layout"] = new JObject { ["allNodes"] = new JObject
            {
                [nodeId] = new JObject { ["position"] = new JObject { ["x"] = 0, ["y"] = 0 } }
            } },
            ["value"] = new JObject
            {
                ["AuthoringId"] = id, ["ContentRevision"] = Guid.NewGuid().ToString("N"), ["name"] = role,
                ["m_ContentDomain"] = "graph", ["Role"] = role, ["Domains"] = new JArray(), ["VariableOwnerId"] = id,
                ["EntryDeclarations"] = new JArray(new JObject
                {
                    ["m_EntryId"] = entry, ["m_NodeId"] = nodeId, ["m_Contract"] = (int)OrchestrationExecutionContract.Complete
                }),
                ["layout"] = new JObject(),
                ["allNodes"] = new JArray(new JObject
                {
                    ["UID"] = nodeId, ["type"] = nameof(BtsmtlEntryFlowNode),
                    ["_inputPortValues"] = new JObject(), ["outConnections"] = new JArray()
                })
            }
        };
    }

    public async Task<AuthoringQueryResult> Fields(string path, SkillAuthoringNodeQuery input)
    {
        AuthoringQueryPage.Require(input.Offset, input.Limit);
        JObject node = await ReadNode(path, input);
        _ = NodePorts((string)node["type"]!);
        return new(AuthoringQueryPage.Slice([], input.Offset, input.Limit));
    }

    public async Task<AuthoringQueryResult> Ports(string path, SkillAuthoringNodeQuery input)
    {
        AuthoringQueryPage.Require(input.Offset, input.Limit);
        JObject node = await ReadNode(path, input);
        return new(AuthoringQueryPage.Slice(DescribePorts(NodePorts((string)node["type"]!),
            (JObject)node["_inputPortValues"]!), input.Offset, input.Limit));
    }

    async Task<JObject> ReadNode(string path, SkillAuthoringNodeQuery input)
    {
        AuthoringDocumentSnapshot document = FindDocument(await Documents.Capture(input.EditingView), path);
        string pathToGraph = document.Kind == AuthoringDocumentKind.Graph ? document.Kind.RootFile : $"graphs/{input.GraphId}.json";
        BtsmtlGraphDocument graph = BtsmtlGraphDocumentJson.Read(document.Read(pathToGraph));
        if (graph.AuthoringId != input.GraphId)
            throw new ArgumentException($"图 {input.GraphId} 不属于文档 {path}。");
        return graph.Node(input.NodeId).Json();
    }

    static AuthoringDocumentSnapshot FindDocument(IEnumerable<AuthoringDocumentSnapshot> documents, string path) =>
        documents.FirstOrDefault(document => document.DocumentPath == path)
        ?? throw new FileNotFoundException($"找不到作者文档 {path}。");

    static IEnumerable<string> GraphFiles(AuthoringDocumentKind kind, IEnumerable<string> files) =>
        kind == AuthoringDocumentKind.Graph ? [kind.RootFile] : kind == AuthoringDocumentKind.Timeline
            ? files.Where(file => file.StartsWith("graphs/", StringComparison.Ordinal) && file.EndsWith(".json", StringComparison.Ordinal)) : [];

    static AuthoringGraphProjection ProjectGraph(AuthoringDocumentSnapshot document, string file, JObject? layout)
    {
        JObject stored = document.Read(file);
        BtsmtlGraphDocument graph = BtsmtlGraphDocumentJson.Read(stored);
        string route = "graph:" + graph.AuthoringId;
        var projection = new AuthoringGraphProjection(AuthoringGraphProjection.DocumentActor, graph.AuthoringId,
            graph.ContentRevision, graph.AuthoringId, graph.Name, (string?)stored["Role"], [], [], [], [])
        {
            Owner = document.DocumentPath, OwnerName = (string)(document.Root[document.Kind == AuthoringDocumentKind.Timeline ? "m_Name" : "name"])!, File = file,
            AuthoringPath = route, ContentDomain = graph.ContentDomain, Domains = graph.Domains.ToArray(),
            VariableOwnerId = graph.VariableOwnerId
        };
        JToken? positions = (file == document.Kind.RootFile ? stored["layout"] : layout?[graph.AuthoringId])?["allNodes"];
        projection.AddFlow((JArray)stored["allNodes"]!, node =>
        {
            string id = (string)node["UID"]!;
            string type = (string)node["type"]!;
            JToken? position = positions?[id]?["position"];
            return new(id, (string?)node["_name"] ?? type, type, route,
                position is null ? null : new((double)position["x"]!, (double)position["y"]!), node.Path);
        }, node => NodePorts((string)node["type"]!).Select(port => new AuthoringGraphPort((string)node["UID"]!,
            port.Id, port.DisplayName, port.IsInput ? "input" : "output", port.IsFlow, PortType(port), route)),
            token => token.Path);
        return projection.Complete();
    }

    static IReadOnlyList<GraphNodePort> NodePorts(string type)
    {
        if (BtsmtlNativeValueNodeCatalog.TryGet(type, out BtsmtlNativeValueNodeDefinition definition))
            return definition.Ports;
        Type flow = s_FlowNodes.FirstOrDefault(candidate => candidate.Name == type)
            ?? throw new ArgumentException($"独立工作区的节点目录未声明类型 {type}。");
        return GraphNodePortShapes.Of(Activator.CreateInstance(flow)!);
    }

    static string PortType(GraphNodePort port) => port.IsFlow ? "Flow" :
        CompileValueTypes.ClrType(CompileValueTypes.FromId(port.ValueTypeId)).Name;

    static IEnumerable<JObject> DescribePorts(IEnumerable<GraphNodePort> ports, JObject inputs) => ports.Select(port => new JObject
    {
        ["direction"] = port.IsInput ? "input" : "output",
        ["port"] = new JObject
        {
            ["ID"] = port.Id, ["name"] = port.DisplayName, ["type"] = PortType(port),
            ["serializedValue"] = port.IsInput && !port.IsFlow ? inputs[port.Id] : null
        }
    });

    static JObject DescribeType(string type, JsonElement? edited, string revision, int offset, int limit)
    {
        AuthoringQueryPage.Require(offset, limit);
        JObject configuration = edited is { } value ? AuthoringJsonText.Parse(value.GetRawText()) : new();
        IReadOnlyList<GraphNodePort> ports = s_TimelineTypes.Any(value => value.FullName == type || value.Name == type)
            ? [] : NodePorts(type);
        if (configuration["_inputPortValues"] is not JObject inputs)
            configuration["_inputPortValues"] = inputs = new JObject();
        foreach (GraphNodePort port in ports.Where(port => port.IsInput && !port.IsFlow))
            if (!inputs.ContainsKey(port.Id))
                inputs[port.Id] = port.HasDefault ? JToken.FromObject(port.DefaultValue) :
                    BtsmtlGraphVariableDeclarationsJson.CreateDefault(CompileValueTypes.FromId(port.ValueTypeId));
        JObject describedPorts = AuthoringQueryPage.Slice(DescribePorts(ports, inputs), offset, limit);
        describedPorts["availability"] = new JObject { ["available"] = true, ["reason"] = null };
        return new()
        {
            ["type"] = type, ["sourceRevision"] = revision, ["authoringRevision"] = null,
            ["inputKind"] = null, ["configuration"] = configuration,
            ["fields"] = AuthoringQueryPage.Slice([], offset, limit), ["ports"] = describedPorts
        };
    }
}
