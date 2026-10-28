using System.Text.Json;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

public static class AuthoringGraphEditTransport
{
    public static AuthoringGraphEdit Read(GraphEditRequest edit) => edit switch
    {
        RenameGraphRequest value => new RenameGraph(value.DisplayName),
        SetGraphFieldRequest value => new SetGraphField(new(value.FieldId), Value(value.Value)),
        RemoveGraphFieldRequest value => new RemoveGraphField(new(value.FieldId)),
        InsertNodeRequest value => new InsertNode(value.Index, new(new(value.Node.Id), new(value.Node.Capability),
            value.Node.DisplayName, Fields(value.Node.Fields), value.Node.DynamicPorts.Select(Port).ToArray())),
        DeleteNodeRequest value => new DeleteNode(new(value.NodeId)),
        RenameNodeRequest value => new RenameNode(new(value.NodeId), value.DisplayName),
        SetNodeFieldRequest value => new SetNodeField(new(value.NodeId), new(value.FieldId), Value(value.Value)),
        RemoveNodeFieldRequest value => new RemoveNodeField(new(value.NodeId), new(value.FieldId)),
        ReplaceDynamicPortsRequest value => new ReplaceDynamicPorts(new(value.NodeId), value.Ports.Select(Port).ToArray()),
        InsertEdgeRequest value => new InsertEdge(value.Index, new(new(value.Edge.Id), new(value.Edge.SourceNode),
            new(value.Edge.SourcePort), new(value.Edge.TargetNode), new(value.Edge.TargetPort), value.Edge.Order,
            Fields(value.Edge.Fields))),
        DeleteEdgeRequest value => new DeleteEdge(new(value.EdgeId)),
        ReorderEdgesRequest value => new ReorderEdges(new(value.SourceNodeId), new(value.SourcePortId),
            value.EdgeIds.Select(id => new GraphAuthoringElementId(id)).ToArray()),
        SetEdgeOrderRequest value => new SetEdgeOrder(new(value.EdgeId), value.Order),
        SetEdgeFieldRequest value => new SetEdgeField(new(value.EdgeId), new(value.FieldId), Value(value.Value)),
        RemoveEdgeFieldRequest value => new RemoveEdgeField(new(value.EdgeId), new(value.FieldId)),
        InsertVariableRequest value => new InsertVariable(value.Index, new(new(value.Variable.Id), value.Variable.Key,
            value.Variable.Type, Fields(value.Variable.Declaration), Value(value.Variable.DefaultValue))),
        DeleteVariableRequest value => new DeleteVariable(new(value.VariableId)),
        ConfigureVariableRequest value => new ConfigureVariable(new(value.VariableId), value.Key, value.TypeId,
            Fields(value.Declaration), Value(value.DefaultValue)),
        _ => throw new ArgumentException($"图修改 '{edit.GetType().Name}' 没有共同操作映射。")
    };

    static AuthoringValue Value(JsonElement value) =>
        AuthoringDocumentJson.ReadValue(value.GetRawText(), "graph-edit-request");

    static AuthoringNamedValue[] Fields(Dictionary<string, JsonElement> values) =>
        values.Select(field => new AuthoringNamedValue(new(field.Key), Value(field.Value))).ToArray();

    static AuthoringPortDocument Port(AuthoringPortRequest port) => new(new(port.Id), port.DisplayName,
        port.ValueType, Enum.Parse<GraphAuthoringPortDirection>(port.Direction),
        Enum.Parse<GraphAuthoringPortCapacity>(port.Capacity), port.InterfacePortId);
}
