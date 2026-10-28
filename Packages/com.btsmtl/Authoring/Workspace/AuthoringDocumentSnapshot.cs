#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Storage;

namespace BTSMTL.Authoring.Workspace
{
public sealed record AuthoringGraphSource(AuthoringDocumentSet Root, AuthoringGraphDocument Document,
    AuthoringSourceFile Source);

public sealed class AuthoringDocumentSnapshot
{
    readonly Dictionary<GraphAuthoringElementId, AuthoringDocumentSet> m_Roots;

    internal AuthoringDocumentSnapshot(IReadOnlyList<AuthoringRootFiles> sources)
    {
        m_Roots = sources.Select(source => new AuthoringDocumentSet(source)).ToDictionary(documents => documents.Root.Id);
        Revision = Identity(documents => documents.Source.Revision);
        ContentRevision = Identity(documents => documents.ContentRevision);
    }

    public string Revision { get; }
    public string ContentRevision { get; }
    public IReadOnlyDictionary<GraphAuthoringElementId, AuthoringDocumentSet> Roots => m_Roots;

    public AuthoringDocumentSet Root(GraphAuthoringElementId rootId) => m_Roots.TryGetValue(rootId, out var root)
        ? root : throw new KeyNotFoundException("冻结的作者依赖中没有根：" + rootId.Value);

    public AuthoringGraphSource Graph(GraphAuthoringElementId rootId, GraphAuthoringElementId documentId)
    {
        AuthoringDocumentSet root = Root(rootId);
        if (!root.Documents.TryGetValue(documentId, out object? document) || document is not AuthoringGraphDocument graph)
            throw new KeyNotFoundException($"冻结的作者根 {rootId.Value} 中没有图文档：{documentId.Value}。");
        return new(root, graph, root.Sources[documentId]);
    }

    string Identity(Func<AuthoringDocumentSet, string> revision) => AuthoringFileRevision.Compute(Encoding.UTF8.GetBytes(
        string.Join("\n", m_Roots.OrderBy(root => root.Key.Value, StringComparer.Ordinal)
            .Select(root => root.Key.Value + ":" + revision(root.Value)))));
}
}
