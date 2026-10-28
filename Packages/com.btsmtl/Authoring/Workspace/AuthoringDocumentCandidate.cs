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
    AuthoringDocumentAcceptance Receive(AuthoringDocumentSet documents, string? expectedDefinitionsRevision);
}

public enum AuthoringDocumentAcceptanceStage { Structure, Domain }

public sealed record AuthoringDocumentAcceptance(AuthoringDocumentSet Documents, AuthoringDocumentAcceptanceStage Stage,
    string DefinitionsRevision,
    IReadOnlyList<AuthoringSourceFile> Dependencies);

public sealed class AuthoringDefinitionsConflictException : InvalidOperationException
{
    public AuthoringDefinitionsConflictException(string expected, string current)
        : base($"作者定义已变化：请求 {expected}，当前 {current}。")
    { ExpectedRevision = expected; CurrentRevision = current; }
    public string ExpectedRevision { get; }
    public string CurrentRevision { get; }
}

public sealed record AuthoringDocumentText(string Path, string Text);
public sealed record AuthoringDocumentDifference(string Path, IReadOnlyList<GraphAuthoringElementId> DocumentIds,
    string Kind, string BeforeRevision, string AfterRevision);

public sealed class AuthoringDocumentCandidate
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    readonly AuthoringFileChange[] m_Changes;

    AuthoringDocumentCandidate(string requestId, AuthoringRootFiles baseline, AuthoringRootFiles input,
        AuthoringDocumentAcceptance acceptance, AuthoringFileChange[] changes, AuthoringDocumentDifference[] differences)
    {
        RequestId = requestId;
        Baseline = baseline;
        Input = input;
        Documents = acceptance.Documents;
        Stage = acceptance.Stage;
        DefinitionsRevision = acceptance.DefinitionsRevision;
        Dependencies = acceptance.Dependencies.ToArray();
        m_Changes = changes;
        Differences = differences;
        var identity = new StringBuilder(requestId).Append('\n').Append(baseline.Root.Id.Value)
            .Append('\n').Append(baseline.Revision).Append('\n').Append(input.Revision)
            .Append('\n').Append(DefinitionsRevision).Append('\n').Append(Stage).Append('\n');
        foreach (AuthoringSourceFile dependency in Dependencies.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
            identity.Append(dependency.Path).Append('\n').Append(dependency.Revision).Append('\n');
        foreach (AuthoringDocumentDifference difference in differences)
            identity.Append(difference.Path).Append('\n').Append(difference.BeforeRevision)
                .Append('\n').Append(difference.AfterRevision).Append('\n');
        Id = AuthoringFileRevision.Compute(Utf8.GetBytes(identity.ToString()));
    }

    public string Id { get; }
    public string RequestId { get; }
    public AuthoringRootFiles Baseline { get; }
    public AuthoringRootFiles Input { get; }
    public AuthoringDocumentSet Documents { get; }
    public AuthoringDocumentAcceptanceStage Stage { get; }
    public string DefinitionsRevision { get; }
    public IReadOnlyList<AuthoringSourceFile> Dependencies { get; }
    public IReadOnlyList<AuthoringDocumentDifference> Differences { get; }
    public IReadOnlyList<string> WriteSet => m_Changes.Select(change => change.Path).ToArray();
    internal IReadOnlyList<AuthoringFileChange> Changes => m_Changes;

    public static AuthoringDocumentCandidate Create(string requestId, AuthoringRootFiles baseline,
        AuthoringRootFiles inputSource, IReadOnlyList<AuthoringDocumentText> completeFiles,
        IAuthoringDocumentDomain domain, string definitionsRevision)
    {
        string rootPath = baseline.Files[0].Path;
        string directory = Path.GetDirectoryName(rootPath)!;
        var input = completeFiles.ToDictionary(file => AuthoringWorkspaceDirectory.Resolve(directory, file.Path),
            file => file.Text, StringComparer.OrdinalIgnoreCase);
        AuthoringRootDocument root = AuthoringDocumentJson.ReadRoot(input[rootPath], rootPath);
        if (!root.Id.Equals(baseline.Root.Id))
            throw new InvalidOperationException("候选必须保留精确根身份。");
        var files = new List<AuthoringSourceFile> { Source(rootPath, input[rootPath]) };
        foreach (AuthoringDocumentFile file in root.Files.Span)
        {
            string path = AuthoringWorkspaceDirectory.Resolve(directory, file.Path);
            files.Add(Source(path, input[path]));
        }
        if (files.Count != input.Count) throw new InvalidOperationException("完整候选包含未由精确根声明的文件。");
        var parsed = new AuthoringDocumentSet(AuthoringWorkspaceDirectory.Snapshot(root, files));
        AuthoringDocumentAcceptance acceptance = domain.Receive(parsed, definitionsRevision);
        parsed = acceptance.Documents;
        root = parsed.Root;
        parsed.ReceiveReferences();

        var original = baseline.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var originalIdentities = baseline.Root.Files.ToArray().ToDictionary(
            file => AuthoringWorkspaceDirectory.Resolve(directory, file.Path), file => file.Id, StringComparer.OrdinalIgnoreCase);
        originalIdentities.Add(rootPath, baseline.Root.Id);
        var priorDocuments = baseline.Root.Files.ToArray().ToDictionary(file => file.Id,
            file => original[AuthoringWorkspaceDirectory.Resolve(directory, file.Path)]);
        priorDocuments.Add(baseline.Root.Id, original[rootPath]);
        var final = new Dictionary<string, AuthoringSourceFile>(StringComparer.OrdinalIgnoreCase);
        var identities = root.Files.ToArray().ToDictionary(file => AuthoringWorkspaceDirectory.Resolve(directory, file.Path),
            file => file.Id, StringComparer.OrdinalIgnoreCase);
        identities.Add(rootPath, root.Id);
        var canonical = parsed.Documents.ToDictionary(item => item.Key, item => AuthoringDocumentSet.Canonical(item.Value));
        canonical.Add(root.Id, AuthoringDocumentJson.WriteRoot(root));
        foreach (AuthoringSourceFile file in files)
        {
            string text = canonical[identities[file.Path]];
            byte[] bytes = priorDocuments.TryGetValue(identities[file.Path], out AuthoringSourceFile? prior)
                ? Newtonsoft.Json.Linq.JToken.DeepEquals(AuthoringDocumentJson.ReadObject(prior.Text),
                    AuthoringDocumentJson.ReadObject(text)) ? prior.Bytes.ToArray() : Encode(prior, text)
                : Utf8.GetBytes(text);
            bool bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
            final.Add(file.Path, new(file.Path, AuthoringFileRevision.Compute(bytes),
                Utf8.GetString(bytes.AsSpan(bom ? 3 : 0)), bytes));
        }
        var changes = new List<AuthoringFileChange>();
        var differences = new List<AuthoringDocumentDifference>();
        foreach (string path in original.Keys.Union(final.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            original.TryGetValue(path, out AuthoringSourceFile? before);
            final.TryGetValue(path, out AuthoringSourceFile? after);
            originalIdentities.TryGetValue(path, out var beforeId);
            identities.TryGetValue(path, out var afterId);
            if (before?.Revision == after?.Revision && beforeId.Equals(afterId)) continue;
            if (before?.Revision != after?.Revision)
                changes.Add(new(path, before?.Bytes.ToArray(), after?.Bytes.ToArray()));
            GraphAuthoringElementId[] affected = new[] { beforeId, afterId }.Where(id => id.IsValid).Distinct().ToArray();
            differences.Add(new(path, affected, before == null ? "Create" : after == null ? "Delete" :
                beforeId.Equals(afterId) ? "Modify" : "Replace",
                before?.Revision ?? "", after?.Revision ?? ""));
        }
        AuthoringSourceFile[] accepted = new[] { final[rootPath] }.Concat(root.Files.ToArray()
            .Select(file => final[AuthoringWorkspaceDirectory.Resolve(directory, file.Path)])).ToArray();
        return new(requestId, baseline, inputSource,
            acceptance with { Documents = parsed.WithSource(AuthoringWorkspaceDirectory.Snapshot(root, accepted)) },
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
