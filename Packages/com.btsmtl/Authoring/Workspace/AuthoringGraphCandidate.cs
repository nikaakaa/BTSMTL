#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Workspace
{

public sealed class AuthoringGraphWorkspaceFile
{
    static readonly UTF8Encoding Utf8 = new(false, true);

    public AuthoringGraphWorkspaceFile(string path, byte[] bytes)
    {
        Path = System.IO.Path.GetFullPath(path);
        Bytes = bytes;
        Revision = AuthoringFileRevision.Compute(bytes);
        HasByteOrderMark = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        Text = Utf8.GetString(bytes.AsSpan(HasByteOrderMark ? 3 : 0));
        NewLine = Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        Document = AuthoringDocumentJson.ReadGraph(Text, Path);
    }

    public string Path { get; }
    public byte[] Bytes { get; }
    public string Revision { get; }
    public string Text { get; }
    public bool HasByteOrderMark { get; }
    public string NewLine { get; }
    public AuthoringGraphDocument Document { get; }

    public static AuthoringGraphWorkspaceFile Read(string path) => new(path, File.ReadAllBytes(path));

    internal byte[] Encode(string canonicalText)
    {
        byte[] text = Utf8.GetBytes(canonicalText.Replace("\n", NewLine, StringComparison.Ordinal));
        return HasByteOrderMark ? Encoding.UTF8.Preamble.ToArray().Concat(text).ToArray() : text;
    }
}

public sealed record AuthoringGraphEditBatch(GraphAuthoringElementId DocumentId,
    IReadOnlyList<AuthoringGraphEdit> Edits);

public sealed record AuthoringCandidateFile(string Path, string BeforeRevision, string AfterRevision,
    ReadOnlyMemory<byte> Before, ReadOnlyMemory<byte> After, GraphAuthoringElementId DocumentId,
    IReadOnlyList<GraphAuthoringElementId> ChangedElements);

public sealed class AuthoringGraphCandidate
{
    AuthoringGraphCandidate(string requestId, AuthoringRootFiles baseline,
        AuthoringCandidateFile[] files, AuthoringGraphEditBatch[] reverseEdits,
        IReadOnlyList<AuthoringGraphDocument> documents)
    {
        RequestId = requestId;
        RootId = baseline.Root.Id;
        Baseline = baseline;
        Files = files;
        ReverseEdits = reverseEdits;
        Documents = documents;
        var identity = new StringBuilder(requestId).Append('\n').Append(RootId.Value).Append('\n')
            .Append(baseline.Revision).Append('\n');
        foreach (AuthoringCandidateFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
            identity.Append(file.Path).Append('\n').Append(file.BeforeRevision).Append('\n').Append(file.AfterRevision).Append('\n');
        Id = AuthoringFileRevision.Compute(Encoding.UTF8.GetBytes(identity.ToString()));
    }

    public string Id { get; }
    public string RequestId { get; }
    public GraphAuthoringElementId RootId { get; }
    public AuthoringRootFiles Baseline { get; }
    public IReadOnlyList<AuthoringCandidateFile> Files { get; }
    public IReadOnlyList<AuthoringGraphEditBatch> ReverseEdits { get; }
    public IReadOnlyList<AuthoringGraphDocument> Documents { get; }

    public static AuthoringGraphCandidate Create(string requestId, AuthoringRootFiles root,
        IReadOnlyList<AuthoringGraphEditBatch> batches,
        Action<IReadOnlyList<AuthoringGraphDocument>> receive) => Build(requestId, root, batches,
            receive, AuthoringGraphMutation.Apply);

    public static AuthoringGraphCandidate CreateInverse(string requestId, AuthoringRootFiles current,
        AuthoringGraphCandidate applied, Action<IReadOnlyList<AuthoringGraphDocument>> receive)
    {
        if (!current.Root.Id.Equals(applied.RootId) ||
            current.Root.DefinitionVersion != applied.Baseline.Root.DefinitionVersion)
            throw new AuthoringUndoConflictException(current.Root.Id.Value, current.Root.Id.Value,
                "root-context", applied.RootId.Value + "@" + applied.Baseline.Root.DefinitionVersion,
                current.Root.Id.Value + "@" + current.Root.DefinitionVersion);
        var expected = applied.Documents.ToDictionary(document => document.Id);
        return Build(requestId, current, applied.ReverseEdits, receive, (graph, edits) =>
        {
            AuthoringGraphEditResult result = AuthoringGraphMutation.ApplyInverse(expected[graph.Id], graph,
                edits, out AuthoringGraphDocument after);
            expected[graph.Id] = after;
            return result;
        });
    }

    static AuthoringGraphCandidate Build(string requestId, AuthoringRootFiles root,
        IReadOnlyList<AuthoringGraphEditBatch> batches, Action<IReadOnlyList<AuthoringGraphDocument>> receive,
        Func<AuthoringGraphDocument, IReadOnlyList<AuthoringGraphEdit>, AuthoringGraphEditResult> mutate)
    {
        string directory = System.IO.Path.GetDirectoryName(root.Files[0].Path)!;
        var rootFiles = root.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        AuthoringGraphWorkspaceFile[] baseline = root.Root.Files.ToArray()
            .Where(file => file.Kind == AuthoringDocumentKind.Graph)
            .Select(file => rootFiles[AuthoringWorkspaceDirectory.Resolve(directory, file.Path)])
            .Select(file => new AuthoringGraphWorkspaceFile(file.Path, file.Bytes.ToArray())).ToArray();
        var documents = baseline.ToDictionary(file => file.Document.Id, file => file.Document);
        var sources = baseline.ToDictionary(file => file.Document.Id);
        var changed = new Dictionary<GraphAuthoringElementId, HashSet<GraphAuthoringElementId>>();
        var inverses = new List<AuthoringGraphEditBatch>();
        foreach (AuthoringGraphEditBatch batch in batches)
        {
            AuthoringGraphEditResult result = mutate(documents[batch.DocumentId], batch.Edits);
            documents[batch.DocumentId] = result.Document;
            if (!changed.TryGetValue(batch.DocumentId, out HashSet<GraphAuthoringElementId>? elements))
                changed.Add(batch.DocumentId, elements = new());
            elements.UnionWith(result.ChangedElements);
            inverses.Insert(0, new AuthoringGraphEditBatch(batch.DocumentId, result.ReverseEdits));
        }
        AuthoringGraphDocument[] complete = documents.Values.OrderBy(document => document.Id.Value,
            StringComparer.Ordinal).ToArray();
        receive(complete);
        var files = new List<AuthoringCandidateFile>();
        foreach (var change in changed.OrderBy(change => change.Key.Value, StringComparer.Ordinal))
        {
            AuthoringGraphWorkspaceFile source = sources[change.Key];
            string canonical = AuthoringDocumentJson.WriteGraph(documents[change.Key]);
            if (canonical == AuthoringDocumentJson.WriteGraph(source.Document))
                continue;
            byte[] after = source.Encode(canonical);
            files.Add(new AuthoringCandidateFile(source.Path, source.Revision,
                AuthoringFileRevision.Compute(after), source.Bytes, after, change.Key,
                change.Value.OrderBy(element => element.Value, StringComparer.Ordinal).ToArray()));
        }
        return new AuthoringGraphCandidate(requestId, root, files.ToArray(), inverses.ToArray(), complete);
    }
}
}
