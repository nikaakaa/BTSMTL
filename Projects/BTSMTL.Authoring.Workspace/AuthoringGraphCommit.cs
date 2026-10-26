using System.Collections.Concurrent;
using System.Security.Cryptography;
using BTSMTL.Authoring.Storage;

namespace BTSMTL.Authoring.Workspace;

public enum AuthoringCommitStatus { Saved, Conflict, Failed }
public sealed record AuthoringRevisionConflict(string Path, string ExpectedRevision, string CurrentRevision,
    IReadOnlyList<string> ChangedElements);
public sealed record AuthoringCommittedFile(string Path, string Revision, IReadOnlyList<string> ChangedElements);
public sealed record AuthoringGraphCommitResult(string CandidateId, string RequestId, string RootId,
    AuthoringCommitStatus Status, IReadOnlyList<AuthoringCommittedFile> Files,
    IReadOnlyList<AuthoringRevisionConflict> Conflicts, AuthoringFileWriteResult? Transaction);

public sealed class AuthoringGraphCommit
{
    readonly ConcurrentDictionary<string, SemaphoreSlim> m_Files = new(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<string, AuthoringGraphCommitResult> m_Saved = new(StringComparer.Ordinal);

    public async Task<AuthoringGraphCommitResult> Commit(AuthoringGraphCandidate candidate,
        CancellationToken cancellation = default)
    {
        string[] paths = candidate.Baseline.Files.Select(file => file.Path)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var acquired = new List<SemaphoreSlim>();
        try
        {
            foreach (string path in paths)
            {
                SemaphoreSlim gate = m_Files.GetOrAdd(path, _ => new(1));
                await gate.WaitAsync(cancellation);
                acquired.Add(gate);
            }
            if (m_Saved.TryGetValue(candidate.Id, out AuthoringGraphCommitResult? saved)) return saved;
            var conflicts = new List<AuthoringRevisionConflict>();
            foreach (AuthoringSourceFile source in candidate.Baseline.Files)
            {
                string current = File.Exists(source.Path)
                    ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.Path))) : "";
                if (current == source.Revision) continue;
                AuthoringCandidateFile? change = candidate.Files.SingleOrDefault(file => file.Path == source.Path);
                conflicts.Add(new(source.Path, source.Revision, current,
                    change == null ? Array.Empty<string>() : change.ChangedElements.Select(id => id.Value).ToArray()));
            }
            if (conflicts.Count != 0)
                return Result(AuthoringCommitStatus.Conflict, Array.Empty<AuthoringCommittedFile>(), conflicts, null);
            AuthoringFileWriteResult transaction = AuthoringFileTransaction.Write(
                Path.GetDirectoryName(candidate.Baseline.Files[0].Path)!, candidate.Files
                .Select(file => new AuthoringFileChange(file.Path, file.Before.ToArray(), file.After.ToArray()))
                .ToArray(), Array.Empty<string>());
            AuthoringGraphCommitResult result = Result(transaction.Success ? AuthoringCommitStatus.Saved :
                AuthoringCommitStatus.Failed, transaction.Success ? candidate.Files.Select(file => new AuthoringCommittedFile(
                    file.Path, file.AfterRevision, file.ChangedElements.Select(id => id.Value).ToArray())).ToArray() :
                Array.Empty<AuthoringCommittedFile>(), Array.Empty<AuthoringRevisionConflict>(), transaction);
            if (transaction.Success) m_Saved.TryAdd(candidate.Id, result);
            return result;
        }
        finally { foreach (SemaphoreSlim gate in acquired.AsEnumerable().Reverse()) gate.Release(); }

        AuthoringGraphCommitResult Result(AuthoringCommitStatus status, IReadOnlyList<AuthoringCommittedFile> files,
            IReadOnlyList<AuthoringRevisionConflict> conflicts, AuthoringFileWriteResult? transaction) =>
            new(candidate.Id, candidate.RequestId, candidate.RootId.Value, status, files, conflicts, transaction);
    }
}
