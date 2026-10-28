using System.Text;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Storage;
using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

public sealed class AuthoringGraphEdits(AuthoringDefinitionCatalog definitions,
    AuthoringWorkspaceDirectory workspace, AuthoringWorkspaceDirectory migrations)
{
    public IResult Preview(GraphEditPreviewRequest request)
    {
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("图修改预览需要非空 requestId。");
        string definitionsRevision = definitions.CurrentRevision();
        if (definitionsRevision != request.DefinitionsRevision)
            return Results.Conflict(new { request.RequestId, Stage = "definitions-conflict",
                ExpectedRevision = request.DefinitionsRevision, ActualRevision = definitionsRevision });
        AuthoringWorkspaceDirectory source = request.Source switch
        {
            WorkbenchDocumentSource.Workspace => workspace,
            WorkbenchDocumentSource.Migrations => migrations,
            _ => throw new ArgumentException("图修改来源必须是 Workspace 或 Migrations。")
        };
        AuthoringRootFiles baseline = source.Read(request.RootPath);
        if (baseline.Revision != request.RootRevision)
            return Results.Conflict(new { request.RequestId, Stage = "baseline-conflict",
                request.RootPath, ExpectedRevision = request.RootRevision, ActualRevision = baseline.Revision });
        string directory = Path.GetDirectoryName(baseline.Files[0].Path)!;
        var files = baseline.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var graphs = baseline.Root.Files.ToArray().Where(file => file.Kind == AuthoringDocumentKind.Graph)
            .ToDictionary(file => file.Id, file =>
            {
                AuthoringSourceFile original = files[AuthoringWorkspaceDirectory.Resolve(directory, file.Path)];
                return (Declaration: file, Original: original,
                    Document: AuthoringDocumentJson.ReadGraph(original.Text, original.Path));
            });
        var changed = new Dictionary<GraphAuthoringElementId, HashSet<GraphAuthoringElementId>>();
        foreach (GraphEditBatchRequest batch in request.Batches)
        {
            var id = new GraphAuthoringElementId(batch.DocumentId);
            var graph = graphs[id];
            AuthoringGraphEditResult result = AuthoringGraphMutation.Apply(graph.Document,
                batch.Edits.Select(AuthoringGraphEditTransport.Read).ToArray());
            graphs[id] = (graph.Declaration, graph.Original, result.Document);
            if (!changed.TryGetValue(id, out HashSet<GraphAuthoringElementId>? elements))
                changed.Add(id, elements = new());
            elements.UnionWith(result.ChangedElements);
        }
        var previewFiles = changed.OrderBy(change => change.Key.Value, StringComparer.Ordinal).Select(change =>
        {
            var graph = graphs[change.Key];
            string content = AuthoringDocumentJson.WriteGraph(graph.Document);
            string before = AuthoringDocumentJson.WriteGraph(
                AuthoringDocumentJson.ReadGraph(graph.Original.Text, graph.Original.Path));
            return new { graph.Declaration.Path, DocumentId = change.Key.Value,
                BeforeRevision = graph.Original.Revision,
                ContentRevision = AuthoringFileRevision.Compute(Encoding.UTF8.GetBytes(content)).ToLowerInvariant(),
                Changed = content != before, BeforeContent = graph.Original.Text, Content = content,
                ChangedElements = change.Value.OrderBy(id => id.Value, StringComparer.Ordinal).Select(id => id.Value),
                Topology = Topology(graph.Document, content, graphs.Values.Select(value => value.Document).ToArray(),
                    request.DefinitionsRevision) };
        }).ToArray();
        return Results.Ok(new { request.RequestId, Stage = "graph-edit-preview", baseline.Revision,
            request.DefinitionsRevision, RootId = baseline.Root.Id.Value, Files = previewFiles,
            SaveReady = false, Acceptance = "not-received",
            Pending = new[] { "完整字段及最终引用的领域接收", "共同编译、发布及 Unity 派生" } });
    }

    object Topology(AuthoringGraphDocument graph, string content, AuthoringGraphDocument[] graphs, string revision)
    {
        try
        {
            return graph.RoleId.Value == "StateMachine"
                ? definitions.ValidateStateMachine(new(content, graphs.Where(item => !item.Id.Equals(graph.Id))
                    .Select(AuthoringDocumentJson.WriteGraph).ToArray(), revision))
                : definitions.ValidateFlowGraph(new(content, revision));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return new { Valid = false, Scope = "GraphTopology", Message = error.Message };
        }
    }

}

