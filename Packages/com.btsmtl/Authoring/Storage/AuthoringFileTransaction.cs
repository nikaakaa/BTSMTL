#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BTSMTL.Authoring.Storage
{

public sealed class AuthoringFileChange
{
    public AuthoringFileChange(string path, byte[]? before, byte[]? after)
    { Path = path; Before = before; After = after; }
    public string Path { get; }
    public byte[]? Before { get; }
    public byte[]? After { get; }
}

public sealed class AuthoringFileRecovery
{
    public AuthoringFileRecovery(string path, string backupPath, string error)
    { Path = path; BackupPath = backupPath; Error = error; }
    public string Path { get; }
    public string BackupPath { get; }
    public string Error { get; }
}

public sealed class AuthoringFileWriteResult
{
    public AuthoringFileWriteResult(bool success, string failedPath, string error,
        IReadOnlyList<string> created, IReadOnlyList<string> modified, IReadOnlyList<string> deleted,
        IReadOnlyList<string> restored, IReadOnlyList<AuthoringFileRecovery> recovery)
    {
        Success = success; FailedPath = failedPath; Error = error;
        Created = created; Modified = modified; Deleted = deleted; Restored = restored; Recovery = recovery;
    }
    public bool Success { get; }
    public string FailedPath { get; }
    public string Error { get; }
    public IReadOnlyList<string> Created { get; }
    public IReadOnlyList<string> Modified { get; }
    public IReadOnlyList<string> Deleted { get; }
    public IReadOnlyList<string> Restored { get; }
    public IReadOnlyList<AuthoringFileRecovery> Recovery { get; }
}

public static class AuthoringFileTransaction
{
    sealed class Entry
    {
        internal Entry(AuthoringFileChange change, string directory, string id, int index)
        {
            Change = change;
            Staging = Path.Combine(Path.GetDirectoryName(change.Path)!, $".btsmtl-{id}-{index}.write");
            Backup = Path.Combine(directory, $".btsmtl-{id}-{index}.restore");
        }
        internal readonly AuthoringFileChange Change;
        internal readonly string Staging;
        internal readonly string Backup;
    }

    public static AuthoringFileWriteResult Write(string directory, IReadOnlyList<AuthoringFileChange> changes,
        IReadOnlyList<string> retiredDirectories)
    {
        string id = Guid.NewGuid().ToString("N");
        Entry[] plan = changes.Select((change, index) => new Entry(change, directory, id, index)).ToArray();
        var committed = new List<Entry>();
        var createdDirectories = new List<string>();
        var deletedDirectories = new List<string>();
        var created = new List<string>();
        var modified = new List<string>();
        var deleted = new List<string>();
        var restored = new List<string>();
        var recovery = new List<AuthoringFileRecovery>();
        string failedPath = "", failure = "";
        try
        {
            foreach (Entry entry in plan.Where(entry => entry.Change.After != null))
            {
                failedPath = entry.Change.Path;
                var missing = new List<string>();
                for (string current = Path.GetDirectoryName(entry.Change.Path)!;
                    !Directory.Exists(current); current = Path.GetDirectoryName(current)!)
                    missing.Add(current);
                foreach (string missingDirectory in missing.AsEnumerable().Reverse())
                {
                    Directory.CreateDirectory(missingDirectory);
                    createdDirectories.Add(missingDirectory);
                }
                using var file = new FileStream(entry.Staging, FileMode.CreateNew, FileAccess.Write);
                file.Write(entry.Change.After!);
                file.Flush(true);
            }
            foreach (Entry entry in plan)
            {
                AuthoringFileChange change = entry.Change;
                failedPath = change.Path;
                if (change.After == null)
                {
                    File.Move(change.Path, entry.Backup);
                    deleted.Add(change.Path);
                }
                else if (change.Before == null)
                {
                    File.Move(entry.Staging, change.Path);
                    created.Add(change.Path);
                }
                else
                {
                    File.Replace(entry.Staging, change.Path, entry.Backup);
                    modified.Add(change.Path);
                }
                committed.Add(entry);
            }
            foreach (string retiredDirectory in retiredDirectories)
            {
                failedPath = retiredDirectory;
                Directory.Delete(retiredDirectory);
                deletedDirectories.Add(retiredDirectory);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            failure = error.Message;
            foreach (string deletedDirectory in deletedDirectories.AsEnumerable().Reverse())
                Recover(deletedDirectory, "", () => Directory.CreateDirectory(deletedDirectory));
            foreach (Entry entry in committed.AsEnumerable().Reverse())
            {
                AuthoringFileChange change = entry.Change;
                Recover(change.Path, entry.Backup, () =>
                {
                    if (change.After == null)
                        File.Move(entry.Backup, change.Path);
                    else
                    {
                        byte[] current = File.ReadAllBytes(change.Path);
                        if (!current.AsSpan().SequenceEqual(change.After))
                            throw new IOException("文件在本次写入后被其它入口修改，保留其内容和原始恢复文件。");
                        if (change.Before == null) File.Delete(change.Path);
                        else File.Replace(entry.Backup, change.Path, null);
                    }
                });
            }
        }
        foreach (Entry entry in plan)
        {
            if (File.Exists(entry.Staging)) Cleanup(entry.Staging);
            if (failure.Length == 0 && File.Exists(entry.Backup)) Cleanup(entry.Backup);
        }
        if (failure.Length != 0)
            foreach (string createdDirectory in createdDirectories.AsEnumerable().Reverse())
                Recover(createdDirectory, "", () => Directory.Delete(createdDirectory));
        return new(failure.Length == 0, failure.Length == 0 ? "" : failedPath,
            failure, created, modified, deleted, restored, recovery);

        void Recover(string path, string backup, Action action)
        {
            try { action(); restored.Add(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { recovery.Add(new(path, File.Exists(backup) ? backup : "", error.Message)); }
        }
        void Cleanup(string path)
        {
            try { File.Delete(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { recovery.Add(new(path, path, error.Message)); }
        }
    }
}
}
