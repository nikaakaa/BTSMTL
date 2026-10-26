using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Workspace;

public abstract record AuthoringGraphEdit;
public sealed record RenameGraph(string DisplayName) : AuthoringGraphEdit;
public sealed record SetGraphField(GraphAuthoringFieldId FieldId, AuthoringValue Value) : AuthoringGraphEdit;
public sealed record RemoveGraphField(GraphAuthoringFieldId FieldId) : AuthoringGraphEdit;
public sealed record InsertNode(int Index, AuthoringNodeDocument Node) : AuthoringGraphEdit;
public sealed record DeleteNode(GraphAuthoringElementId NodeId) : AuthoringGraphEdit;
public sealed record RenameNode(GraphAuthoringElementId NodeId, string DisplayName) : AuthoringGraphEdit;
public sealed record SetNodeField(GraphAuthoringElementId NodeId, GraphAuthoringFieldId FieldId,
    AuthoringValue Value) : AuthoringGraphEdit;
public sealed record RemoveNodeField(GraphAuthoringElementId NodeId, GraphAuthoringFieldId FieldId) : AuthoringGraphEdit;
public sealed record ReplaceDynamicPorts(GraphAuthoringElementId NodeId,
    AuthoringPortDocument[] Ports) : AuthoringGraphEdit;
public sealed record InsertEdge(int Index, AuthoringEdgeDocument Edge) : AuthoringGraphEdit;
public sealed record DeleteEdge(GraphAuthoringElementId EdgeId) : AuthoringGraphEdit;
public sealed record ReorderEdges(GraphAuthoringElementId SourceNodeId, GraphAuthoringPortId SourcePortId,
    GraphAuthoringElementId[] EdgeIds) : AuthoringGraphEdit;
public sealed record SetEdgeOrder(GraphAuthoringElementId EdgeId, int Order) : AuthoringGraphEdit;
public sealed record SetEdgeField(GraphAuthoringElementId EdgeId, GraphAuthoringFieldId FieldId,
    AuthoringValue Value) : AuthoringGraphEdit;
public sealed record RemoveEdgeField(GraphAuthoringElementId EdgeId, GraphAuthoringFieldId FieldId) : AuthoringGraphEdit;
public sealed record InsertVariable(int Index, AuthoringVariableDocument Variable) : AuthoringGraphEdit;
public sealed record DeleteVariable(GraphAuthoringElementId VariableId) : AuthoringGraphEdit;
public sealed record ConfigureVariable(GraphAuthoringElementId VariableId, string Key, string TypeId,
    AuthoringNamedValue[] Declaration, AuthoringValue DefaultValue) : AuthoringGraphEdit;

public sealed record AuthoringGraphEditResult(AuthoringGraphDocument Document,
    IReadOnlyList<AuthoringGraphEdit> ReverseEdits, IReadOnlyList<GraphAuthoringElementId> ChangedElements);
