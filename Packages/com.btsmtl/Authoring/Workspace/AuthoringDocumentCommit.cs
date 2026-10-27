#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    public AuthoringDocumentCommitResult? Query(string requestId) => m_Results.TryGetValue(requestId, out var result) ? result : null;

    public Task<AuthoringDocumentCommitResult> Commit(AuthoringDocumentCandidate candidate,
        CancellationToken cancellation = default) => Task.Run(() => Execute(candidate, cancellation), cancellation);

    AuthoringDocumentCommitResult Execute(AuthoringDocumentCandidate candidate, CancellationToken cancellation)
    {
        string[] paths = candidate.Baseline.Files.Select(file => file.Path)
            .Concat(candidate.WriteSet).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var acquired = new List<Mutex>();
        try
        {
            foreach (string path in paths)
            {
                string key = Path.GetFullPath(path);
                if (Path.DirectorySeparatorChar == '\\') key = key.ToUpperInvariant();
                var mutex = new Mutex(false, "BTSMTL.Authoring." + AuthoringFileRevision.Compute(Encoding.UTF8.GetBytes(key)));
                try
                {
                    if (WaitHandle.WaitAny(new WaitHandle[] { mutex, cancellation.WaitHandle }) == 1)
                    { mutex.Dispose(); cancellation.ThrowIfCancellationRequested(); }
                }
                catch (AbandonedMutexException) { acquired.Add(mutex); continue; }
                acquired.Add(mutex);
            }
            if (m_Results.TryGetValue(candidate.RequestId, out var recorded))
            {
                if (recorded.CandidateId != candidate.Id)
                    throw new InvalidOperationException("同一保存请求身份不能提交不同候选。");
                return recorded;
            }
            var baseline = candidate.Baseline.Files.ToDictionary(file => file.Path, file => file.Revision,
                StringComparer.OrdinalIgnoreCase);
            var accepted = candidate.Documents.Source.Files.ToDictionary(file => file.Path, file => file.Revision,
                StringComparer.OrdinalIgnoreCase);
            var current = paths.ToDictionary(path => path, path => File.Exists(path)
                ? AuthoringFileRevision.Compute(File.ReadAllBytes(path)) : "", StringComparer.OrdinalIgnoreCase);
            bool alreadySaved = current.All(file => file.Value == (accepted.TryGetValue(file.Key, out var revision) ? revision : ""));
            if (alreadySaved) return Record(Result(AuthoringCommitStatus.Saved, Array.Empty<AuthoringRevisionConflict>(), null));
            var conflicts = new List<AuthoringRevisionConflict>();
            foreach (string path in paths)
            {
                string expected = baseline.TryGetValue(path, out var revision) ? revision : "";
                if (current[path] == expected) continue;
                conflicts.Add(new(path, expected, current[path], candidate.Differences.Where(file =>
                    string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase))
                    .Select(file => file.DocumentId.Value).ToArray()));
            }
            if (conflicts.Count != 0)
                return Record(Result(AuthoringCommitStatus.Conflict, conflicts, null));
            cancellation.ThrowIfCancellationRequested();
            AuthoringFileWriteResult transaction = AuthoringFileTransaction.Write(
                Path.GetDirectoryName(candidate.Baseline.Files[0].Path)!, candidate.Changes, Array.Empty<string>());
            return Record(Result(transaction.Success ? AuthoringCommitStatus.Saved : AuthoringCommitStatus.Failed,
                Array.Empty<AuthoringRevisionConflict>(), transaction));
        }
        finally { foreach (Mutex mutex in acquired.AsEnumerable().Reverse()) { mutex.ReleaseMutex(); mutex.Dispose(); } }

        AuthoringDocumentCommitResult Record(AuthoringDocumentCommitResult result)
        { m_Results.TryAdd(candidate.RequestId, result); return result; }

        AuthoringDocumentCommitResult Result(AuthoringCommitStatus status,
            IReadOnlyList<AuthoringRevisionConflict> conflicts, AuthoringFileWriteResult? transaction) =>
            new(candidate.Id, candidate.RequestId, candidate.Baseline.Root.Id.Value, status,
                status == AuthoringCommitStatus.Saved ? candidate.Documents.Source.Revision : "",
                status == AuthoringCommitStatus.Saved ? candidate.Documents.ContentRevision : "",
                status == AuthoringCommitStatus.Saved ? candidate.Differences.Select(file => new AuthoringCommittedFile(
                    file.Path, file.AfterRevision, new[] { file.DocumentId.Value })).ToArray() : Array.Empty<AuthoringCommittedFile>(),
                conflicts, transaction);
    }
}
}
