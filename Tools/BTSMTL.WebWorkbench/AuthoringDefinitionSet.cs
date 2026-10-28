using System.Text.Json;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Timeline;

namespace BTSMTL.WebWorkbench;

public sealed record AuthoringGraphReferenceDefinition(GraphAuthoringFieldId Field,
    GraphAuthoringDocumentRoleId Role);

public sealed class AuthoringDefinitionSet
{
    public AuthoringDefinitionSet(AuthoringDefinitionSnapshot source)
    {
        Source = source;
        Domain = new(source.Domain);
        Nodes = source.Capabilities.Select(node => AuthoringDocumentJson.ReadNodeDefinition(node.GetRawText(),
            "definitions/node:" + node.GetProperty("id").GetString())).ToDictionary(node => node.Id);
        InputTypes = source.Document.GetProperty("valueTypes").EnumerateObject().ToDictionary(value => value.Name,
            value => AuthoringDocumentJson.ReadValueDefinition(value.Value.GetRawText(), "definitions/value:" + value.Name),
            StringComparer.Ordinal);
        Roots = source.Document.GetProperty("rootAuthoring").EnumerateArray().ToDictionary(
            root => root.GetProperty("kind").GetString()!, root => Object(root.GetProperty("description")), StringComparer.Ordinal);
        JsonElement graphs = source.Document.GetProperty("graphAuthoring");
        FlowGraph = Object(graphs.GetProperty("flow"));
        MachineGraph = Object(graphs.GetProperty("stateMachine"));
        Edge = Object(graphs.GetProperty("edge"));
        FlowAnchors = source.Document.GetProperty("flowGraphs").EnumerateArray().ToDictionary(
            graph => new GraphAuthoringDocumentRoleId(graph.GetProperty("role").GetString()!),
            graph => (IReadOnlyList<AuthoringFlowGraphAnchor>)graph.GetProperty("anchors").EnumerateArray()
                .Select(anchor => new AuthoringFlowGraphAnchor(anchor.GetProperty("name").GetString()!,
                    anchor.GetProperty("identity").GetString()!, new(anchor.GetProperty("capability").GetString()!))).ToArray());
        JsonElement machine = source.Document.GetProperty("stateMachine");
        MachineRole = new(machine.GetProperty("role").GetString()!);
        MachineBodyField = new(machine.GetProperty("bodyField").GetString()!);
        MachineConditionField = new(machine.GetProperty("conditionField").GetString()!);
        Machine = new(machine.GetProperty("anchors").EnumerateArray().Select(anchor => new AuthoringStateAnchor(
                Enum.Parse<AuthoringStateKind>(anchor.GetProperty("kind").GetString()!),
                anchor.GetProperty("name").GetString()!, anchor.GetProperty("identity").GetString()!)).ToArray(),
            new(machine.GetProperty("bodyRole").GetString()!), new(machine.GetProperty("conditionRole").GetString()!));
        StateKinds = Nodes.Values.Where(node => node.StateKind.HasValue)
            .ToDictionary(node => node.Id, node => node.StateKind!.Value);
        GraphReferences = source.Document.GetProperty("nodeAuthoring").EnumerateArray().ToDictionary(
            node => new GraphAuthoringCapabilityId(node.GetProperty("id").GetString()!),
            node => (IReadOnlyList<AuthoringGraphReferenceDefinition>)node.GetProperty("graphs").EnumerateArray()
                .Select(reference => new AuthoringGraphReferenceDefinition(new(reference.GetProperty("field").GetString()!),
                    new(reference.GetProperty("role").GetString()!))).ToArray());
        TimelineContracts = AuthoringDocumentJson.ReadTimelineContracts(
            source.Document.GetProperty("timelineContracts").GetRawText(), "definitions/timeline-contracts");
        JsonElement timeline = source.Document.GetProperty("timelineAuthoring");
        Timeline = Object(timeline.GetProperty("document"));
        Tracks = Objects(timeline.GetProperty("tracks"));
        Clips = Objects(timeline.GetProperty("clips"));
        JsonElement marker = timeline.GetProperty("marker");
        MarkerKind = marker.GetProperty("kind").GetString()!;
        Marker = Object(marker.GetProperty("description"));
        Binding = Object(timeline.GetProperty("binding"));
    }

    public AuthoringDefinitionSnapshot Source { get; }
    public GraphAuthoringDomainId Domain { get; }
    public IReadOnlyDictionary<GraphAuthoringCapabilityId, AuthoringNodeDefinition> Nodes { get; }
    public IReadOnlyDictionary<string, AuthoringValueDefinition> InputTypes { get; }
    public IReadOnlyDictionary<string, AuthoringObjectDefinition> Roots { get; }
    public AuthoringObjectDefinition FlowGraph { get; }
    public AuthoringObjectDefinition MachineGraph { get; }
    public AuthoringObjectDefinition Edge { get; }
    public IReadOnlyDictionary<GraphAuthoringDocumentRoleId, IReadOnlyList<AuthoringFlowGraphAnchor>> FlowAnchors { get; }
    public GraphAuthoringDocumentRoleId MachineRole { get; }
    public GraphAuthoringFieldId MachineBodyField { get; }
    public GraphAuthoringFieldId MachineConditionField { get; }
    public AuthoringStateMachineDefinition Machine { get; }
    public IReadOnlyDictionary<GraphAuthoringCapabilityId, AuthoringStateKind> StateKinds { get; }
    public IReadOnlyDictionary<GraphAuthoringCapabilityId, IReadOnlyList<AuthoringGraphReferenceDefinition>> GraphReferences { get; }
    public TimelineDefinitionCatalog<TimelineClipDefinition> TimelineContracts { get; }
    public AuthoringObjectDefinition Timeline { get; }
    public IReadOnlyDictionary<string, AuthoringObjectDefinition> Tracks { get; }
    public IReadOnlyDictionary<string, AuthoringObjectDefinition> Clips { get; }
    public string MarkerKind { get; }
    public AuthoringObjectDefinition Marker { get; }
    public AuthoringObjectDefinition Binding { get; }

    static IReadOnlyDictionary<string, AuthoringObjectDefinition> Objects(JsonElement definitions) =>
        definitions.EnumerateArray().ToDictionary(value => value.GetProperty("kind").GetString()!,
            value => Object(value.GetProperty("description")), StringComparer.Ordinal);

    static AuthoringObjectDefinition Object(JsonElement definition) =>
        AuthoringDocumentJson.ReadObjectDefinition(definition.GetRawText(), "definitions/authoring");
}
