using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public sealed record AuthoringGraphEndpoint(string GraphId, string NodeId, string PortId);
public sealed record AuthoringGraphPosition(double X, double Y);
public sealed record AuthoringGraphNode(string Id, string? DisplayName, string Type, string InvocationPath,
    AuthoringGraphPosition? Position, string SourcePath);
public sealed record AuthoringGraphPort(string NodeId, string Id, string DisplayName, string Direction, bool IsTrigger,
    string ValueType, string InvocationPath, string? ObjectKind = null, string? ObjectKindName = null,
    string[]? AcceptedObjectKinds = null);
public sealed record AuthoringGraphEdge(string Id, string Kind, string InvocationPath, AuthoringGraphEndpoint Source,
    AuthoringGraphEndpoint Target, bool Available, string Reason, string SourcePath);
public sealed record AuthoringGraphAvailability(string Channel, bool Available, bool Complete, string Reason);
public sealed record AuthoringGraphOwner(string GraphId, string NodeId);

public sealed record AuthoringGraphProjection(string ActorId, string ProgramId, string ProgramRevision, string Id,
    string? DisplayName, string? Role, List<AuthoringGraphNode> Nodes, List<AuthoringGraphPort> Ports,
    List<AuthoringGraphEdge> Edges, AuthoringGraphAvailability[] Availability)
{
    public const string DocumentActor = "document";
    public required string Owner { get; init; }
    public required string OwnerName { get; init; }
    public required string File { get; init; }
    public required string AuthoringPath { get; init; }
    public AuthoringGraphOwner? DeclaredOwner { get; init; }
    public string? ContentDomain { get; init; }
    public string[]? Domains { get; init; }
    public string? VariableOwnerId { get; init; }

    public void AddFlow(JArray sourceNodes, Func<JObject, AuthoringGraphNode> describeNode,
        Func<JObject, IEnumerable<AuthoringGraphPort>> describePorts, Func<JToken, string> sourcePath)
    {
        var triggers = new HashSet<(string Node, string Port)>();
        foreach (JObject source in sourceNodes)
        {
            Nodes.Add(describeNode(source));
            foreach (AuthoringGraphPort port in describePorts(source))
            {
                if (port.IsTrigger) triggers.Add((port.NodeId, port.Id));
                Ports.Add(port);
            }
        }
        foreach (JObject node in sourceNodes)
        {
            string nodeId = (string)node["UID"]!;
            foreach (JToken edge in node["outConnections"]!)
            {
                string sourcePort = (string)edge["sourcePortID"]!;
                Edges.Add(new((string)edge["UID"]!, triggers.Contains((nodeId, sourcePort)) ? "Child" : "Value",
                    AuthoringPath, new(Id, nodeId, sourcePort),
                    new(Id, (string)edge["targetNode"]!, (string)edge["targetPortID"]!),
                    true, string.Empty, sourcePath(edge)));
            }
        }
    }

    public AuthoringGraphProjection Complete()
    {
        bool placed = Nodes.All(node => node.Position != null);
        return this with
        {
            Availability =
            [
                new("author-layout", true, placed, placed ? string.Empty : "部分节点没有作者布局坐标。"),
                new("author-display-names", true, true, string.Empty),
                new("author-value-connections", true, true, string.Empty)
            ]
        };
    }
}
