using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Workspace;

public static class AuthoringGraphMutation
{
    public static AuthoringGraphEditResult Apply(AuthoringGraphDocument graph,
        IReadOnlyList<AuthoringGraphEdit> edits)
    {
        var draft = new Draft(graph);
        foreach (AuthoringGraphEdit edit in edits)
            draft.Apply(edit);
        return draft.Complete();
    }

    sealed class Draft
    {
        readonly AuthoringGraphDocument source;
        readonly List<AuthoringNamedValue> fields;
        readonly List<AuthoringNodeDocument> nodes;
        readonly List<AuthoringEdgeDocument> edges;
        readonly List<AuthoringVariableDocument> variables;
        readonly List<AuthoringGraphEdit[]> inverses = new();
        readonly HashSet<GraphAuthoringElementId> changed = new();
        string displayName;

        public Draft(AuthoringGraphDocument graph)
        {
            source = graph;
            displayName = graph.DisplayName;
            fields = graph.Fields.ToArray().ToList();
            nodes = graph.Nodes.ToArray().ToList();
            edges = graph.Edges.ToArray().ToList();
            variables = graph.Variables.ToArray().ToList();
        }

        public void Apply(AuthoringGraphEdit edit)
        {
            switch (edit)
            {
                case RenameGraph rename:
                    Record(source.Id, new RenameGraph(displayName));
                    displayName = rename.DisplayName;
                    break;
                case SetGraphField set:
                    SetField(fields, set.FieldId, set.Value,
                        old => old == null ? new RemoveGraphField(set.FieldId) : new SetGraphField(set.FieldId, old), source.Id);
                    break;
                case RemoveGraphField remove:
                    RemoveField(fields, remove.FieldId, old => new SetGraphField(remove.FieldId, old), source.Id);
                    break;
                case InsertNode insert:
                    nodes.Insert(insert.Index, insert.Node);
                    Record(insert.Node.Id, new DeleteNode(insert.Node.Id));
                    break;
                case DeleteNode delete:
                    int nodeIndex = nodes.FindIndex(node => node.Id.Equals(delete.NodeId));
                    AuthoringNodeDocument removedNode = nodes[nodeIndex];
                    var restore = new List<AuthoringGraphEdit> { new InsertNode(nodeIndex, removedNode) };
                    for (int index = 0; index < edges.Count; index++)
                        if (edges[index].SourceNodeId.Equals(delete.NodeId) || edges[index].TargetNodeId.Equals(delete.NodeId))
                        {
                            restore.Add(new InsertEdge(index, edges[index]));
                            changed.Add(edges[index].Id);
                        }
                    edges.RemoveAll(edge => edge.SourceNodeId.Equals(delete.NodeId) || edge.TargetNodeId.Equals(delete.NodeId));
                    nodes.RemoveAt(nodeIndex);
                    Record(delete.NodeId, restore.ToArray());
                    break;
                case RenameNode rename:
                    UpdateNode(rename.NodeId, node =>
                    {
                        Record(node.Id, new RenameNode(node.Id, node.DisplayName));
                        return Copy(node, displayName: rename.DisplayName);
                    });
                    break;
                case SetNodeField set:
                    UpdateNode(set.NodeId, node =>
                    {
                        var values = node.Fields.ToArray().ToList();
                        SetField(values, set.FieldId, set.Value,
                            old => old == null ? new RemoveNodeField(node.Id, set.FieldId) : new SetNodeField(node.Id, set.FieldId, old), node.Id);
                        return Copy(node, fields: values.ToArray());
                    });
                    break;
                case RemoveNodeField remove:
                    UpdateNode(remove.NodeId, node =>
                    {
                        var values = node.Fields.ToArray().ToList();
                        RemoveField(values, remove.FieldId, old => new SetNodeField(node.Id, remove.FieldId, old), node.Id);
                        return Copy(node, fields: values.ToArray());
                    });
                    break;
                case ReplaceDynamicPorts ports:
                    UpdateNode(ports.NodeId, node =>
                    {
                        Record(node.Id, new ReplaceDynamicPorts(node.Id, node.DynamicPorts.ToArray()));
                        return Copy(node, ports: ports.Ports);
                    });
                    break;
                case InsertEdge insert:
                    edges.Insert(insert.Index, insert.Edge);
                    Record(insert.Edge.Id, new DeleteEdge(insert.Edge.Id));
                    break;
                case DeleteEdge delete:
                    int edgeIndex = edges.FindIndex(edge => edge.Id.Equals(delete.EdgeId));
                    Record(delete.EdgeId, new InsertEdge(edgeIndex, edges[edgeIndex]));
                    edges.RemoveAt(edgeIndex);
                    break;
                case ReorderEdges reorder:
                    AuthoringEdgeDocument[] previous = edges.Where(edge => edge.SourceNodeId.Equals(reorder.SourceNodeId) &&
                        edge.SourcePortId.Equals(reorder.SourcePortId)).OrderBy(edge => edge.Order).ToArray();
                    if (previous.Length != reorder.EdgeIds.Length ||
                        !previous.Select(edge => edge.Id).ToHashSet().SetEquals(reorder.EdgeIds))
                        throw new InvalidOperationException("连接重排必须提供该出口的完整连接集合，不能增加或遗漏连接。");
                    Record(reorder.SourceNodeId, previous.Select(edge =>
                        (AuthoringGraphEdit)new SetEdgeOrder(edge.Id, edge.Order)).ToArray());
                    for (int index = 0; index < reorder.EdgeIds.Length; index++)
                    {
                        int current = edges.FindIndex(edge => edge.Id.Equals(reorder.EdgeIds[index]));
                        AuthoringEdgeDocument edge = edges[current];
                        edges[current] = Copy(edge, order: index);
                    }
                    break;
                case SetEdgeOrder set:
                    UpdateEdge(set.EdgeId, edge =>
                    {
                        Record(edge.Id, new SetEdgeOrder(edge.Id, edge.Order));
                        return Copy(edge, order: set.Order);
                    });
                    break;
                case SetEdgeField set:
                    UpdateEdge(set.EdgeId, edge =>
                    {
                        var values = edge.Fields.ToArray().ToList();
                        SetField(values, set.FieldId, set.Value,
                            old => old == null ? new RemoveEdgeField(edge.Id, set.FieldId) : new SetEdgeField(edge.Id, set.FieldId, old), edge.Id);
                        return Copy(edge, fields: values.ToArray());
                    });
                    break;
                case RemoveEdgeField remove:
                    UpdateEdge(remove.EdgeId, edge =>
                    {
                        var values = edge.Fields.ToArray().ToList();
                        RemoveField(values, remove.FieldId, old => new SetEdgeField(edge.Id, remove.FieldId, old), edge.Id);
                        return Copy(edge, fields: values.ToArray());
                    });
                    break;
                case InsertVariable insert:
                    variables.Insert(insert.Index, insert.Variable);
                    Record(insert.Variable.Id, new DeleteVariable(insert.Variable.Id));
                    break;
                case DeleteVariable delete:
                    int variableIndex = variables.FindIndex(variable => variable.Id.Equals(delete.VariableId));
                    Record(delete.VariableId, new InsertVariable(variableIndex, variables[variableIndex]));
                    variables.RemoveAt(variableIndex);
                    break;
                case ConfigureVariable configure:
                    int target = variables.FindIndex(variable => variable.Id.Equals(configure.VariableId));
                    AuthoringVariableDocument old = variables[target];
                    Record(old.Id, new ConfigureVariable(old.Id, old.Key, old.TypeId, old.Declaration.ToArray(), old.DefaultValue));
                    variables[target] = new AuthoringVariableDocument(old.Id, configure.Key, configure.TypeId,
                        configure.Declaration, configure.DefaultValue);
                    break;
                default:
                    throw new ArgumentException($"未提供图修改 '{edit.GetType().Name}' 的正式处理。", nameof(edit));
            }
        }

        void SetField(List<AuthoringNamedValue> values, GraphAuthoringFieldId id, AuthoringValue value,
            Func<AuthoringValue?, AuthoringGraphEdit> reverse, GraphAuthoringElementId element)
        {
            int index = values.FindIndex(field => field.FieldId.Equals(id));
            Record(element, reverse(index < 0 ? null : values[index].Value));
            var field = new AuthoringNamedValue(id, value);
            if (index < 0) values.Add(field);
            else values[index] = field;
        }

        void RemoveField(List<AuthoringNamedValue> values, GraphAuthoringFieldId id,
            Func<AuthoringValue, AuthoringGraphEdit> reverse, GraphAuthoringElementId element)
        {
            int index = values.FindIndex(field => field.FieldId.Equals(id));
            Record(element, reverse(values[index].Value));
            values.RemoveAt(index);
        }

        void UpdateNode(GraphAuthoringElementId id, Func<AuthoringNodeDocument, AuthoringNodeDocument> update)
        {
            int index = nodes.FindIndex(node => node.Id.Equals(id));
            nodes[index] = update(nodes[index]);
        }

        void UpdateEdge(GraphAuthoringElementId id, Func<AuthoringEdgeDocument, AuthoringEdgeDocument> update)
        {
            int index = edges.FindIndex(edge => edge.Id.Equals(id));
            edges[index] = update(edges[index]);
        }

        void Record(GraphAuthoringElementId element, params AuthoringGraphEdit[] reverse)
        {
            changed.Add(element);
            inverses.Add(reverse);
        }

        public AuthoringGraphEditResult Complete() => new(new AuthoringGraphDocument(source.Id,
            source.RoleId, source.Owner, displayName, source.DefinitionVersion, fields.ToArray(),
            nodes.ToArray(), edges.ToArray(), variables.ToArray()),
            inverses.AsEnumerable().Reverse().SelectMany(batch => batch).ToArray(),
            changed.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray());

        static AuthoringNodeDocument Copy(AuthoringNodeDocument node, string? displayName = null,
            AuthoringNamedValue[]? fields = null, AuthoringPortDocument[]? ports = null) => new(node.Id,
            node.CapabilityId, displayName ?? node.DisplayName, fields ?? node.Fields, ports ?? node.DynamicPorts);

        static AuthoringEdgeDocument Copy(AuthoringEdgeDocument edge, int? order = null,
            AuthoringNamedValue[]? fields = null) => new(edge.Id, edge.SourceNodeId, edge.SourcePortId,
            edge.TargetNodeId, edge.TargetPortId, order ?? edge.Order, fields ?? edge.Fields);
    }
}
