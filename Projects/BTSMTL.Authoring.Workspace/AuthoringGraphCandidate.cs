using System.Security.Cryptography;
using System.Text;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Workspace;

public sealed class AuthoringGraphWorkspaceFile
{
    static readonly UTF8Encoding Utf8 = new(false, true);

    public AuthoringGraphWorkspaceFile(string path, byte[] bytes)
    {
        Path = System.IO.Path.GetFullPath(path);
        Bytes = bytes;
        Revision = Convert.ToHexString(SHA256.HashData(bytes));
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
    AuthoringGraphCandidate(string requestId, GraphAuthoringElementId rootId,
        AuthoringCandidateFile[] files, AuthoringGraphEditBatch[] reverseEdits,
        IReadOnlyList<AuthoringGraphDocument> documents)
    {
        RequestId = requestId;
        RootId = rootId;
        Files = files;
        ReverseEdits = reverseEdits;
        Documents = documents;
        var identity = new StringBuilder(requestId).Append('\n').Append(rootId.Value).Append('\n');
        foreach (AuthoringCandidateFile file in files.OrderBy(file => file.Path, StringComparer.Ordinal))
            identity.Append(file.Path).Append('\n').Append(file.BeforeRevision).Append('\n').Append(file.AfterRevision).Append('\n');
        Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())));
    }

    public string Id { get; }
    public string RequestId { get; }
    public GraphAuthoringElementId RootId { get; }
    public IReadOnlyList<AuthoringCandidateFile> Files { get; }
    public IReadOnlyList<AuthoringGraphEditBatch> ReverseEdits { get; }
    public IReadOnlyList<AuthoringGraphDocument> Documents { get; }

    public static AuthoringGraphCandidate Create(string requestId, GraphAuthoringElementId rootId,
        IReadOnlyList<AuthoringGraphWorkspaceFile> baseline, IReadOnlyList<AuthoringGraphEditBatch> batches,
        Action<IReadOnlyList<AuthoringGraphDocument>> receive)
    {
        var documents = baseline.ToDictionary(file => file.Document.Id, file => file.Document);
        var sources = baseline.ToDictionary(file => file.Document.Id);
        var changed = new Dictionary<GraphAuthoringElementId, HashSet<GraphAuthoringElementId>>();
        var inverses = new List<AuthoringGraphEditBatch>();
        foreach (AuthoringGraphEditBatch batch in batches)
        {
            AuthoringGraphEditResult result = AuthoringGraphMutation.Apply(documents[batch.DocumentId], batch.Edits);
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
                Convert.ToHexString(SHA256.HashData(after)), source.Bytes, after, change.Key,
                change.Value.OrderBy(element => element.Value, StringComparer.Ordinal).ToArray()));
        }
        return new AuthoringGraphCandidate(requestId, rootId, files.ToArray(), inverses.ToArray(), complete);
    }
}
