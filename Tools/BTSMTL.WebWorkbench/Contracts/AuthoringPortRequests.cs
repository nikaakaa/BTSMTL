namespace BTSMTL.WebWorkbench;

public sealed record AuthoringGraphPortRequest(string Content, string DefinitionsRevision);
public sealed record AuthoringPortProblem(string Code, string Message);
public sealed record AuthoringProjectedPort(string Id, string DisplayName, string ValueType,
    string Direction, string Capacity, bool Required, int Order, string InterfacePortId);
public sealed record AuthoringProjectedNodePorts(string NodeId, AuthoringProjectedPort[] Ports,
    AuthoringPortProblem? Problem);
