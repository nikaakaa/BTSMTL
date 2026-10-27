#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Storage;

namespace BTSMTL.Authoring.Workspace
{
public interface IAuthoringDocumentDomain
{
    GraphAuthoringDomainId DomainId { get; }
    string DefinitionVersion { get; }
    void Receive(AuthoringDocumentSet documents);
}

public sealed record AuthoringDocumentText(string Path, string Text);
public sealed record AuthoringDocumentDifference(string Path, GraphAuthoringElementId DocumentId,
    string Kind, string BeforeRevision, string AfterRevision);

public sealed class AuthoringDocumentCandidate
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    readonly AuthoringFileChange[] m_Changes;

    AuthoringDocumentCandidate(string requestId, AuthoringRootFiles baseline, AuthoringDocumentSet documents,
        AuthoringFileChange[] changes, AuthoringDocumentDifference[] differences)
    {
        RequestId = requestId;
        Baseline = baseline;
        Documents = documents;
        m_Changes = changes;
        Differences = differences;
        var identity = new StringBuilder(requestId).Append('\n').Append(baseline.Root.Id.Value)
            .Append('\n').Append(baseline.Revision).Append('\n');
        foreach (AuthoringDocumentDifference difference in differences)
            identity.Append(difference.Path).Append('\n').Append(difference.BeforeRevision)
                .Append('\n').Append(difference.AfterRevision).Append('\n');
        Id = AuthoringFileRevision.Compute(Utf8.GetBytes(identity.ToString()));
    }

    public string Id { get; }
    public string RequestId { get; }
    public AuthoringRootFiles Baseline { get; }
    public AuthoringDocumentSet Documents { get; }
    public IReadOnlyList<AuthoringDocumentDifference> Differences { get; }
    public IReadOnlyList<string> WriteSet => m_Changes.Select(change => change.Path).ToArray();
    internal IReadOnlyList<AuthoringFileChange> Changes => m_Changes;

    public static AuthoringDocumentCandidate Create(string requestId, AuthoringRootFiles baseline,
        IReadOnlyList<AuthoringDocumentText> completeFiles, IAuthoringDocumentDomain domain)
    {
        string rootPath = baseline.Files[0].Path;
        string directory = Path.GetDirectoryName(rootPath)!;
        var input = completeFiles.ToDictionary(file => AuthoringWorkspaceDirectory.Resolve(directory, file.Path),
            file => file.Text, StringComparer.OrdinalIgnoreCase);
        AuthoringRootDocument root = AuthoringDocumentJson.ReadRoot(input[rootPath], rootPath);
        if (!root.Id.Equals(baseline.Root.Id) || !root.DomainId.Equals(domain.DomainId))
            throw new InvalidOperationException("候选必须保留精确根身份和原领域。");
        if (root.DefinitionVersion != domain.DefinitionVersion)
            throw new InvalidOperationException($"候选默认定义版本 {root.DefinitionVersion} 不属于当前领域 {domain.DefinitionVersion}。");
        var files = new List<AuthoringSourceFile> { Source(rootPath, input[rootPath]) };
        foreach (AuthoringDocumentFile file in root.Files.Span)
        {
            string path = AuthoringWorkspaceDirectory.Resolve(directory, file.Path);
            files.Add(Source(path, input[path]));
        }
        if (files.Count != input.Count) throw new InvalidOperationException("完整候选包含未由精确根声明的文件。");
        var parsed = new AuthoringDocumentSet(AuthoringWorkspaceDirectory.Snapshot(root, files));
        parsed.ReceiveReferences();
        domain.Receive(parsed);

        var original = baseline.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var final = new Dictionary<string, AuthoringSourceFile>(StringComparer.OrdinalIgnoreCase);
        var identities = root.Files.ToArray().ToDictionary(file => AuthoringWorkspaceDirectory.Resolve(directory, file.Path),
            file => file.Id, StringComparer.OrdinalIgnoreCase);
        identities.Add(rootPath, root.Id);
        var canonical = parsed.Documents.ToDictionary(item => item.Key, item => AuthoringDocumentSet.Canonical(item.Value));
        canonical.Add(root.Id, AuthoringDocumentJson.WriteRoot(root));
        foreach (AuthoringSourceFile file in files)
        {
            string text = canonical[identities[file.Path]];
            byte[] bytes = original.TryGetValue(file.Path, out AuthoringSourceFile? prior)
                ? Encode(prior, text) : Utf8.GetBytes(text);
            final.Add(file.Path, new(file.Path, AuthoringFileRevision.Compute(bytes), Utf8.GetString(bytes), bytes));
        }
        var changes = new List<AuthoringFileChange>();
        var differences = new List<AuthoringDocumentDifference>();
        foreach (string path in original.Keys.Union(final.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            original.TryGetValue(path, out AuthoringSourceFile? before);
            final.TryGetValue(path, out AuthoringSourceFile? after);
            if (before?.Revision == after?.Revision) continue;
            GraphAuthoringElementId document = identities.TryGetValue(path, out var current) ? current :
                baseline.Root.Files.ToArray().Single(file => AuthoringWorkspaceDirectory.Resolve(directory, file.Path) == path).Id;
            changes.Add(new(path, before?.Bytes.ToArray(), after?.Bytes.ToArray()));
            differences.Add(new(path, document, before == null ? "Create" : after == null ? "Delete" : "Modify",
                before?.Revision ?? "", after?.Revision ?? ""));
        }
        AuthoringSourceFile[] accepted = new[] { final[rootPath] }.Concat(root.Files.ToArray()
            .Select(file => final[AuthoringWorkspaceDirectory.Resolve(directory, file.Path)])).ToArray();
        return new(requestId, baseline, new AuthoringDocumentSet(AuthoringWorkspaceDirectory.Snapshot(root, accepted)),
            changes.ToArray(), differences.ToArray());
    }

    public static IReadOnlyList<AuthoringDocumentText> Texts(AuthoringRootFiles source)
    {
        string directory = Path.GetDirectoryName(source.Files[0].Path)!;
        return source.Files.Select(file => new AuthoringDocumentText(
            Path.GetRelativePath(directory, file.Path).Replace('\\', '/'), file.Text)).ToArray();
    }

    static AuthoringSourceFile Source(string path, string text)
    {
        byte[] bytes = Utf8.GetBytes(text);
        return new(path, AuthoringFileRevision.Compute(bytes), text, bytes);
    }

    static byte[] Encode(AuthoringSourceFile source, string text)
    {
        string newline = source.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        byte[] bytes = Utf8.GetBytes(text.Replace("\r\n", "\n").Replace("\n", newline));
        return source.Bytes.Span.StartsWith(Encoding.UTF8.Preamble)
            ? Encoding.UTF8.Preamble.ToArray().Concat(bytes).ToArray() : bytes;
    }
}
}
