using System.Text.Json;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Storage;

namespace BTSMTL.WebWorkbench;

public sealed class AuthoringDefinitionCatalog(string path) : IAuthoringDefinitionQueries
{
    readonly string m_Path = Path.GetFullPath(path);

    public string CurrentRevision() => Read().Revision;

    Catalog Read()
    {
        byte[] bytes = File.ReadAllBytes(m_Path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement source = document.RootElement;
        if (source.GetProperty("format").GetString() != "character-workbench.authoring-definitions/2")
            throw new InvalidDataException("能力描述格式不属于当前正式导出协议。");
        return new(AuthoringFileRevision.Compute(bytes).ToLowerInvariant(), source.GetProperty("domain").GetString()!,
            source.GetProperty("capabilities").EnumerateArray().Select(value => value.Clone()).ToArray(), source.Clone());
    }

    public object Info()
    {
        Catalog source = Read();
        return new { Path = m_Path, source.Revision, source.Domain,
            Count = source.Capabilities.Length, Source = "native-authoring-definitions" };
    }

    public object Document()
    {
        Catalog source = Read();
        return new { source.Revision, source.Domain, Definition = source.Document };
    }

    public object List(int offset, int limit, string? role)
    {
        if (offset < 0 || limit < 1 || limit > 200)
            throw new ArgumentException("能力查询要求 offset ≥ 0，limit 在 1～200 之间。");
        Catalog source = Read();
        JsonElement[] selected = source.Capabilities.Where(capability => role == null ||
            capability.GetProperty("roles").EnumerateArray().Any(value => value.GetString() == role)).ToArray();
        return new { source.Revision, source.Domain, Items = selected.Skip(offset).Take(limit),
            Offset = offset, Total = selected.Length, NextOffset = offset + limit < selected.Length ? (int?)(offset + limit) : null };
    }

    public object Require(string id)
    {
        Catalog source = Read();
        JsonElement capability = source.Capabilities.SingleOrDefault(value => value.GetProperty("id").GetString() == id);
        if (capability.ValueKind == JsonValueKind.Undefined)
            throw new KeyNotFoundException($"原能力目录没有 '{id}'。");
        return new { source.Revision, source.Domain, Capability = capability };
    }

    public object ProjectPorts(AuthoringGraphPortRequest request)
    {
        Catalog source = Read();
        if (source.Revision != request.DefinitionsRevision)
            throw new InvalidOperationException($"端口投影的定义修订已变化：请求 {request.DefinitionsRevision}，当前 {source.Revision}。");
        AuthoringGraphDocument graph = AuthoringDocumentJson.ReadGraph(request.Content, "graph-port-request");
        var definitions = source.Capabilities.ToDictionary(value => value.GetProperty("id").GetString()!);
        var nodes = new List<AuthoringProjectedNodePorts>();
        foreach (AuthoringNodeDocument node in graph.Nodes.Span)
            nodes.Add(definitions.TryGetValue(node.CapabilityId.Value, out JsonElement definition)
                ? AuthoringPortProjection.Project(definition, node)
                : new(node.Id.Value, Array.Empty<AuthoringProjectedPort>(),
                    new("capability_missing", $"原能力目录没有 '{node.CapabilityId.Value}'。")));
        return new { source.Revision, source.Domain, DocumentId = graph.Id.Value, Nodes = nodes };
    }

    public object ValidateStateMachine(AuthoringStateMachineValidationRequest request)
    {
        Catalog source = Read();
        if (source.Revision != request.DefinitionsRevision)
            throw new InvalidOperationException($"状态机接收的定义修订已变化：请求 {request.DefinitionsRevision}，当前 {source.Revision}。");
        AuthoringGraphDocument machine = AuthoringDocumentJson.ReadGraph(request.Content, "state-machine-request");
        if (!source.Document.TryGetProperty("stateMachine", out JsonElement contract))
            throw new InvalidOperationException("当前能力导出尚未包含 FSM 合同，须从已采用新合同的目标 Editor 重新导出。");
        if (machine.RoleId.Value != contract.GetProperty("role").GetString())
            throw new ArgumentException("此接收入口要求正式 StateMachine 文档。");
        AuthoringGraphDocument[] related = request.RelatedGraphs.Select((content, index) =>
            AuthoringDocumentJson.ReadGraph(content, "state-machine-reference-" + index)).Append(machine).ToArray();
        var graphs = related.ToDictionary(graph => (graph.Owner.RootId.Value, graph.Id.Value));
        var kinds = source.Capabilities.Where(capability => capability.GetProperty("stateKind").ValueKind == JsonValueKind.String)
            .ToDictionary(capability => new GraphAuthoringCapabilityId(capability.GetProperty("id").GetString()!),
                capability => Enum.Parse<AuthoringStateKind>(capability.GetProperty("stateKind").GetString()!));
        var definition = new AuthoringStateMachineDefinition(contract.GetProperty("anchors").EnumerateArray()
            .Select(anchor => new AuthoringStateAnchor(Enum.Parse<AuthoringStateKind>(anchor.GetProperty("kind").GetString()!),
                anchor.GetProperty("name").GetString()!, anchor.GetProperty("identity").GetString()!)).ToArray(),
            new(contract.GetProperty("bodyRole").GetString()!), new(contract.GetProperty("conditionRole").GetString()!));
        AuthoringStateMachineContract.ValidateDocument(machine, kinds,
            new(contract.GetProperty("bodyField").GetString()!), new(contract.GetProperty("conditionField").GetString()!),
            reference => graphs[(reference.RootId.Value, reference.DocumentId.Value)], definition, true);
        return new { source.Revision, source.Domain, DocumentId = machine.Id.Value, Scope = "StateMachine",
            Valid = true, StateCount = machine.Nodes.Length, TransitionCount = machine.Edges.Length };
    }

    public object ValidateFlowGraph(AuthoringFlowGraphValidationRequest request)
    {
        Catalog source = Read();
        if (source.Revision != request.DefinitionsRevision)
            throw new InvalidOperationException($"流图接收的定义修订已变化：请求 {request.DefinitionsRevision}，当前 {source.Revision}。");
        AuthoringGraphDocument graph = AuthoringDocumentJson.ReadGraph(request.Content, "flow-graph-request");
        if (!source.Document.TryGetProperty("flowGraphs", out JsonElement flowGraphs))
            throw new InvalidOperationException("当前能力描述尚未包含流图入口定义，须从目标 Editor 重新导出。");
        JsonElement contract = flowGraphs.EnumerateArray().SingleOrDefault(value => value.GetProperty("role").GetString() == graph.RoleId.Value);
        if (contract.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException($"当前正式定义没有 '{graph.RoleId.Value}' 流图页面。");
        var definitions = source.Capabilities.ToDictionary(value => value.GetProperty("id").GetString()!);
        var allowed = source.Capabilities.Where(value => value.GetProperty("roles").EnumerateArray()
            .Any(role => role.GetString() == graph.RoleId.Value))
            .Select(value => new GraphAuthoringCapabilityId(value.GetProperty("id").GetString()!)).ToHashSet();
        AuthoringFlowGraphAnchor[] anchors = contract.GetProperty("anchors").EnumerateArray()
            .Select(value => new AuthoringFlowGraphAnchor(value.GetProperty("name").GetString()!,
                value.GetProperty("identity").GetString()!, new(value.GetProperty("capability").GetString()!))).ToArray();
        AuthoringFlowGraphNode[] nodes = graph.Nodes.ToArray().Select(node => new AuthoringFlowGraphNode(node.Id,
            node.CapabilityId, AuthoringPortProjection.ProjectComplete(definitions[node.CapabilityId.Value], node))).ToArray();
        AuthoringFlowGraphContract.Validate("graph:" + graph.Id.Value, nodes, graph.Edges.ToArray(), allowed, anchors, true);
        return new { source.Revision, source.Domain, DocumentId = graph.Id.Value, Scope = "FlowGraphTopology",
            Valid = true, NodeCount = graph.Nodes.Length, EdgeCount = graph.Edges.Length };
    }

    sealed record Catalog(string Revision, string Domain, JsonElement[] Capabilities, JsonElement Document);
}

