#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Workspace
{

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

    public static AuthoringGraphEditResult ApplyInverse(AuthoringGraphDocument expected,
        AuthoringGraphDocument current, IReadOnlyList<AuthoringGraphEdit> edits,
        out AuthoringGraphDocument expectedAfter)
    {
        var prior = new Draft(expected);
        var actual = new Draft(current);
        if (!expected.RoleId.Equals(current.RoleId) || !expected.Owner.Equals(current.Owner) ||
            expected.DefinitionVersion != current.DefinitionVersion)
            throw new AuthoringUndoConflictException(current.Id.Value, current.Id.Value, "document-context",
                prior.Fragment(Array.Empty<AuthoringNodeDocument>()), actual.Fragment(Array.Empty<AuthoringNodeDocument>()));
        foreach (AuthoringGraphEdit edit in edits)
        {
            string wanted = prior.Fingerprint(edit), found = actual.Fingerprint(edit);
            if (wanted != found)
                throw new AuthoringUndoConflictException(current.Id.Value, Element(edit, current.Id),
                    Field(edit), wanted, found);
            AuthoringGraphEdit rebased = actual.RebaseInsertion(prior, edit);
            prior.Apply(edit);
            actual.Apply(rebased);
        }
        expectedAfter = prior.Complete().Document;
        return actual.Complete();
    }

    static string Element(AuthoringGraphEdit edit, GraphAuthoringElementId graph) => edit switch
    {
        DeleteNode value => value.NodeId.Value, RenameNode value => value.NodeId.Value,
        SetNodeField value => value.NodeId.Value, RemoveNodeField value => value.NodeId.Value,
        ReplaceDynamicPorts value => value.NodeId.Value, InsertNode value => value.Node.Id.Value,
        DeleteEdge value => value.EdgeId.Value, SetEdgeOrder value => value.EdgeId.Value,
        SetEdgeField value => value.EdgeId.Value, RemoveEdgeField value => value.EdgeId.Value,
        InsertEdge value => value.Edge.Id.Value, ReorderEdges value => value.SourceNodeId.Value,
        DeleteVariable value => value.VariableId.Value, ConfigureVariable value => value.VariableId.Value,
        InsertVariable value => value.Variable.Id.Value, _ => graph.Value
    };

    static string Field(AuthoringGraphEdit edit) => edit switch
    {
        SetGraphField value => value.FieldId.Value, RemoveGraphField value => value.FieldId.Value,
        SetNodeField value => value.FieldId.Value, RemoveNodeField value => value.FieldId.Value,
        SetEdgeField value => value.FieldId.Value, RemoveEdgeField value => value.FieldId.Value,
        RenameGraph or RenameNode => "displayName", ReplaceDynamicPorts => "dynamicPorts",
        SetEdgeOrder or ReorderEdges => "order", _ => "element"
    };

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

        public string Fingerprint(AuthoringGraphEdit edit) => edit switch
        {
            RenameGraph => displayName,
            SetGraphField value => Member(fields, value.FieldId),
            RemoveGraphField value => Member(fields, value.FieldId),
            InsertNode value => nodes.Any(node => node.Id.Equals(value.Node.Id)) ? "occupied" : "absent",
            DeleteNode value => Fragment(nodes.Where(node => node.Id.Equals(value.NodeId)).ToArray(),
                edges.Where(edge => edge.SourceNodeId.Equals(value.NodeId) || edge.TargetNodeId.Equals(value.NodeId)).ToArray()),
            RenameNode value => NodePart(value.NodeId, node => node.DisplayName),
            SetNodeField value => NodePart(value.NodeId, node => Member(node.Fields.ToArray(), value.FieldId)),
            RemoveNodeField value => NodePart(value.NodeId, node => Member(node.Fields.ToArray(), value.FieldId)),
            ReplaceDynamicPorts value => NodePart(value.NodeId, node => Fragment(new[] {
                new AuthoringNodeDocument(node.Id, node.CapabilityId, "", Array.Empty<AuthoringNamedValue>(), node.DynamicPorts) })),
            InsertEdge value => edges.Any(edge => edge.Id.Equals(value.Edge.Id)) ? "occupied" : "absent",
            DeleteEdge value => Fragment(Array.Empty<AuthoringNodeDocument>(), edges.Where(edge => edge.Id.Equals(value.EdgeId)).ToArray()),
            SetEdgeOrder value => EdgePart(value.EdgeId, edge => edge.Order.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            SetEdgeField value => EdgePart(value.EdgeId, edge => Member(edge.Fields.ToArray(), value.FieldId)),
            RemoveEdgeField value => EdgePart(value.EdgeId, edge => Member(edge.Fields.ToArray(), value.FieldId)),
            ReorderEdges value => Fragment(Array.Empty<AuthoringNodeDocument>(), edges.Where(edge =>
                edge.SourceNodeId.Equals(value.SourceNodeId) && edge.SourcePortId.Equals(value.SourcePortId)).ToArray()),
            InsertVariable value => variables.Any(variable => variable.Id.Equals(value.Variable.Id)) ? "occupied" : "absent",
            DeleteVariable value => VariablePart(value.VariableId),
            ConfigureVariable value => VariablePart(value.VariableId),
            _ => throw new ArgumentException($"图修改 '{edit.GetType().Name}' 没有撤销读取范围。", nameof(edit))
        };

        string NodePart(GraphAuthoringElementId id, Func<AuthoringNodeDocument, string> read)
        {
            AuthoringNodeDocument? node = nodes.SingleOrDefault(node => node.Id.Equals(id));
            return node == null ? "missing-node" : "node:" + read(node);
        }

        string EdgePart(GraphAuthoringElementId id, Func<AuthoringEdgeDocument, string> read)
        {
            int index = edges.FindIndex(edge => edge.Id.Equals(id));
            return index < 0 ? "missing-edge" : "edge:" + read(edges[index]);
        }

        string VariablePart(GraphAuthoringElementId id) => Fragment(Array.Empty<AuthoringNodeDocument>(),
            Array.Empty<AuthoringEdgeDocument>(), variables.Where(variable => variable.Id.Equals(id)).ToArray());

        internal string Fragment(AuthoringNodeDocument[] selectedNodes, AuthoringEdgeDocument[]? selectedEdges = null,
            AuthoringVariableDocument[]? selectedVariables = null) => AuthoringDocumentJson.WriteGraph(new(
                source.Id, source.RoleId, source.Owner, "", source.DefinitionVersion,
                Array.Empty<AuthoringNamedValue>(), selectedNodes, selectedEdges ?? Array.Empty<AuthoringEdgeDocument>(),
                selectedVariables ?? Array.Empty<AuthoringVariableDocument>()));

        static string Member(IEnumerable<AuthoringNamedValue> values, GraphAuthoringFieldId id)
        {
            AuthoringNamedValue[] matching = values.Where(field => field.FieldId.Equals(id)).ToArray();
            return matching.Length == 0 ? "absent-field" : AuthoringDocumentJson.WriteValue(matching.Single().Value);
        }

        public AuthoringGraphEdit RebaseInsertion(Draft prior, AuthoringGraphEdit edit) => edit switch
        {
            InsertNode value => value with { Index = Insertion(prior.nodes.Select(node => node.Id).ToArray(),
                nodes.Select(node => node.Id).ToArray(), value.Index) },
            InsertEdge value => value with { Index = Insertion(prior.edges.Select(edge => edge.Id).ToArray(),
                edges.Select(edge => edge.Id).ToArray(), value.Index) },
            InsertVariable value => value with { Index = Insertion(prior.variables.Select(variable => variable.Id).ToArray(),
                variables.Select(variable => variable.Id).ToArray(), value.Index) },
            _ => edit
        };

        static int Insertion(GraphAuthoringElementId[] prior, GraphAuthoringElementId[] current, int index)
        {
            for (int next = index; next < prior.Length; next++)
            {
                int anchor = Array.IndexOf(current, prior[next]);
                if (anchor >= 0) return anchor;
            }
            return current.Length;
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
}
