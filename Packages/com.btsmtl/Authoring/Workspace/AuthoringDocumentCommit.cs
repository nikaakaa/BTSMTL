#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTSMTL.Authoring.Storage;

namespace BTSMTL.Authoring.Workspace
{

public enum AuthoringCommitStatus { Saved, Conflict, Failed }
public sealed record AuthoringRevisionConflict(string Path, string ExpectedRevision, string CurrentRevision,
    IReadOnlyList<string> ChangedElements);
public sealed record AuthoringCommittedFile(string Path, string Revision, IReadOnlyList<string> ChangedElements);
public sealed record AuthoringDocumentCommitResult(string CandidateId, string RequestId, string RootId,
    AuthoringCommitStatus Status, string Revision, string ContentRevision,
    IReadOnlyList<AuthoringCommittedFile> Files, IReadOnlyList<AuthoringRevisionConflict> Conflicts,
    AuthoringFileWriteResult? Transaction);

public sealed class AuthoringDocumentCommit
{
    readonly ConcurrentDictionary<string, AuthoringDocumentCommitResult> m_Results = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, (string CandidateId, Lazy<AuthoringDocumentCommitResult> Result)> m_Requests = new(StringComparer.Ordinal);
    public event Action<AuthoringDocumentCommitResult>? Saved;

    public AuthoringDocumentCommitResult? Query(string requestId) => m_Results.TryGetValue(requestId, out var result) ? result : null;

    public Task<AuthoringDocumentCommitResult> Commit(AuthoringDocumentCandidate candidate,
        CancellationToken cancellation = default) => Task.Run(() =>
        {
            var request = m_Requests.GetOrAdd(candidate.RequestId, _ => (candidate.Id,
                new Lazy<AuthoringDocumentCommitResult>(() => Execute(candidate, cancellation))));
            if (request.CandidateId != candidate.Id)
                throw new InvalidOperationException("同一保存请求身份不能提交不同候选。");
            return request.Result.Value;
        }, cancellation);

    AuthoringDocumentCommitResult Execute(AuthoringDocumentCandidate candidate, CancellationToken cancellation)
    {
        string[] paths = candidate.Baseline.Files.Select(file => file.Path)
            .Concat(candidate.Documents.Source.Files.Select(file => file.Path)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var baseline = candidate.Baseline.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var accepted = candidate.Documents.Source.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        AuthoringFileChange[] files = paths.Select(path => new AuthoringFileChange(path,
            baseline.TryGetValue(path, out var before) ? before.Bytes.ToArray() : null,
            accepted.TryGetValue(path, out var after) ? after.Bytes.ToArray() : null)).ToArray();
        AuthoringFileWriteResult transaction = AuthoringFileTransaction.Write(
            Path.GetDirectoryName(candidate.Baseline.Files[0].Path)!, files, Array.Empty<string>(), cancellation);
        AuthoringRevisionConflict[] conflicts = transaction.Conflicts.Select(conflict => new AuthoringRevisionConflict(
            conflict.Path, conflict.ExpectedRevision, conflict.CurrentRevision, Elements(conflict.Path))).ToArray();
        return Record(Result(transaction.Success ? AuthoringCommitStatus.Saved :
            conflicts.Length != 0 ? AuthoringCommitStatus.Conflict : AuthoringCommitStatus.Failed, conflicts, transaction));

        string[] Elements(string path)
        {
            if (string.Equals(path, candidate.Baseline.Files[0].Path, StringComparison.OrdinalIgnoreCase))
                return new[] { candidate.Baseline.Root.Id.Value };
            string directory = Path.GetDirectoryName(candidate.Baseline.Files[0].Path)!;
            return candidate.Baseline.Root.Files.ToArray().Where(file => string.Equals(
                    AuthoringWorkspaceDirectory.Resolve(directory, file.Path), path, StringComparison.OrdinalIgnoreCase))
                .Select(file => file.Id.Value).Concat(candidate.Differences.Where(file =>
                    string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(file => file.DocumentIds).Select(id => id.Value)).Distinct(StringComparer.Ordinal).ToArray();
        }

        AuthoringDocumentCommitResult Record(AuthoringDocumentCommitResult result)
        {
            if (m_Results.TryAdd(candidate.RequestId, result) && result.Status == AuthoringCommitStatus.Saved) Saved?.Invoke(result);
            return result;
        }

        AuthoringDocumentCommitResult Result(AuthoringCommitStatus status,
            IReadOnlyList<AuthoringRevisionConflict> conflicts, AuthoringFileWriteResult? transaction) =>
            new(candidate.Id, candidate.RequestId, candidate.Baseline.Root.Id.Value, status,
                status == AuthoringCommitStatus.Saved ? candidate.Documents.Source.Revision : "",
                status == AuthoringCommitStatus.Saved ? candidate.Documents.ContentRevision : "",
                status == AuthoringCommitStatus.Saved ? candidate.Differences.Select(file => new AuthoringCommittedFile(
                    file.Path, file.AfterRevision, file.DocumentIds.Select(id => id.Value).ToArray())).ToArray() : Array.Empty<AuthoringCommittedFile>(),
                conflicts, transaction);
    }
}
}
