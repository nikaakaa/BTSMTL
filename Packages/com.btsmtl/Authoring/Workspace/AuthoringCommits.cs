using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BTSMTL.Authoring.Workspace
{

// 一个文件的计划写入：Content 为 null 表示删除；ExpectedRevision 为 null 不核对，空串要求文件不存在。
public sealed class AuthoringFileChange
{
    public AuthoringFileChange(string path, string content, string expectedRevision = null)
    { Path = path; Content = content; ExpectedRevision = expectedRevision; }
    public string Path { get; }
    public string Content { get; }
    public string ExpectedRevision { get; }
}
public sealed class AuthoringReadRevision
{
    public AuthoringReadRevision(string path, string revision) { Path = path; Revision = revision; }
    public string Path { get; }
    public string Revision { get; }
}

// 作者数据的跨文件整体提交。同一作者根目录（Authoring）每个进程一个实例：读取持有读锁，提交持有写锁；
// 进程内锁之外再持有根目录锁文件，Unity Editor、Host 与 CLI 之间同样读写互斥。
// 新内容先全部写到目标旁的同卷临时文件，再写事务日志，逐个原子替换后删除日志。中途失败时只向前完成，不回滚。
public sealed class AuthoringCommits
{
    public const string JournalName = ".transaction.json";
    public const string LockName = ".transaction.lock";
    static readonly ConcurrentDictionary<string, AuthoringCommits> s_Roots = new(StringComparer.OrdinalIgnoreCase);
    static readonly UTF8Encoding Utf8 = new(false);
    readonly AuthoringReaderWriterLock m_Lock;

    sealed class JournalEntry
    {
        public JournalEntry(string target, string pending) { Target = target; Pending = pending; }
        public string Target { get; }
        public string Pending { get; }
    }

    AuthoringCommits(string root)
    {
        Root = root;
        m_Lock = new AuthoringReaderWriterLock(Path.Combine(root, LockName));
        using (AuthoringProcessLock.Acquire(Path.Combine(root, LockName), true))
            Recover();
    }

    public string Root { get; }
    string JournalPath => Path.Combine(Root, JournalName);

    public static AuthoringCommits For(string root) =>
        s_Roots.GetOrAdd(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            path => new AuthoringCommits(path));

    public static string Revision(string content)
    {
        using SHA256 hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Utf8.GetBytes(content))).Replace("-", "").ToLowerInvariant();
    }

    public Task<T> Read<T>(Func<Task<T>> action) => m_Lock.Read(action);

    public Task<T> Write<T>(Func<Task<T>> action) => m_Lock.Write(() =>
    {
        Recover();
        return action();
    });

    // 只能在 Write 内调用：任一读取修订不一致时拒绝，不写入任何文件。
    public void Commit(IReadOnlyList<AuthoringFileChange> changes, IReadOnlyList<AuthoringReadRevision> reads = null)
    {
        if (!m_Lock.IsWriting)
            throw new InvalidOperationException("作者数据提交必须持有作者写锁。");
        var targets = changes.Select(change => (Change: change, Target: Inside(change.Path))).ToArray();
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (AuthoringReadRevision read in reads ?? Array.Empty<AuthoringReadRevision>())
            expected.Add(Path.GetFullPath(read.Path), read.Revision);
        foreach (var (change, target) in targets)
        {
            if (change.ExpectedRevision != null && !expected.ContainsKey(target))
                expected.Add(target, change.ExpectedRevision);
        }
        foreach (var (target, revision) in expected)
        {
            string actual = File.Exists(target) ? Revision(File.ReadAllText(target, Encoding.UTF8)) : null;
            if (revision.Length == 0 ? actual != null : actual != revision)
                throw new InvalidOperationException(
                    $"作者文件 '{Path.GetRelativePath(Root, target)}' 已在读取后变化，期望修订 {revision}、实际 {actual ?? "不存在"}，请重新读取后再提交。");
        }
        if (targets.Length == 0)
            return;
        var entries = new List<JournalEntry>();
        foreach (var (change, target) in targets)
        {
            string pending = null;
            if (change.Content != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                pending = target + "." + Guid.NewGuid().ToString("N") + ".pending";
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(Utf8.GetBytes(change.Content));
                    stream.Flush(true);
                }
            }
            entries.Add(new JournalEntry(Path.GetRelativePath(Root, target), pending == null ? null : Path.GetRelativePath(Root, pending)));
        }
        string journalPending = JournalPath + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var stream = new FileStream(journalPending, FileMode.CreateNew, FileAccess.Write))
        {
            byte[] bytes = Utf8.GetBytes(JsonConvert.SerializeObject(entries));
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }
        File.Move(journalPending, JournalPath);
        Complete(entries);
    }

    // Host 启动时与每次提交前调用：按残留日志完成剩余替换。
    void Recover()
    {
        if (!File.Exists(JournalPath))
            return;
        Complete(JsonConvert.DeserializeObject<JournalEntry[]>(File.ReadAllText(JournalPath, Utf8)));
    }

    void Complete(IReadOnlyList<JournalEntry> entries)
    {
        foreach (JournalEntry entry in entries)
        {
            string target = Path.Combine(Root, entry.Target);
            if (entry.Pending == null)
            {
                if (File.Exists(target))
                    File.Delete(target);
                continue;
            }
            string pending = Path.Combine(Root, entry.Pending);
            // 临时文件已不在说明此前已替换完成。
            if (!File.Exists(pending))
                continue;
            if (File.Exists(target))
                File.Replace(pending, target, null);
            else
                File.Move(pending, target);
        }
        File.Delete(JournalPath);
    }

    string Inside(string path)
    {
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"作者提交只能写入 {Root} 内的文件：{path}");
        return full;
    }
}

// 跨进程读写锁：读取以共享读打开锁文件，写入以独占打开；句柄随进程退出由系统释放，不留残锁。
// 争用时在当前线程短轮询，不让出到同步上下文：Unity 主线程上的同步等待调用不会因此自锁。
static class AuthoringProcessLock
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    const int RetryMilliseconds = 10;

    public static FileStream Acquire(string path, bool exclusive)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, exclusive ? FileAccess.ReadWrite : FileAccess.Read,
                    exclusive ? FileShare.None : FileShare.Read);
            }
            catch (IOException) when (DateTime.UtcNow - started < Timeout)
            {
                Thread.Sleep(RetryMilliseconds);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    $"作者库锁 '{path}' 被其它进程持有超过 {Timeout.TotalSeconds} 秒，请确认 Unity Editor、Host 或 CLI 没有卡在作者读写中。", exception);
            }
        }
    }
}

// 异步读写锁：写入优先；同一调用链已持有的读取可重入，写入内的读取与写入可重入。读取内不得请求写入。
// 最外层持有者再取跨进程锁文件：进程内读者各持一个共享句柄，写者持独占句柄。
sealed class AuthoringReaderWriterLock
{
    readonly string m_ProcessLockPath;
    readonly SemaphoreSlim m_Writer = new(1);
    readonly object m_Sync = new();
    readonly AsyncLocal<bool> m_Writing = new();
    readonly AsyncLocal<bool> m_Reading = new();
    int m_Readers;
    TaskCompletionSource<object> m_Drained;
    TaskCompletionSource<object> m_WriterDone;

    public AuthoringReaderWriterLock(string processLockPath) => m_ProcessLockPath = processLockPath;

    public bool IsWriting => m_Writing.Value;

    public async Task<T> Read<T>(Func<Task<T>> action)
    {
        if (m_Writing.Value || m_Reading.Value)
            return await action();
        while (true)
        {
            Task wait;
            lock (m_Sync)
            {
                if (m_WriterDone == null)
                {
                    m_Readers++;
                    break;
                }
                wait = m_WriterDone.Task;
            }
            await wait;
        }
        try
        {
            using FileStream shared = AuthoringProcessLock.Acquire(m_ProcessLockPath, false);
            m_Reading.Value = true;
            return await action();
        }
        finally
        {
            m_Reading.Value = false;
            lock (m_Sync)
                if (--m_Readers == 0)
                    m_Drained?.TrySetResult(null);
        }
    }

    public async Task<T> Write<T>(Func<Task<T>> action)
    {
        if (m_Writing.Value)
            return await action();
        await m_Writer.WaitAsync();
        Task drained;
        lock (m_Sync)
        {
            m_WriterDone = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_Drained = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (m_Readers == 0)
                m_Drained.SetResult(null);
            drained = m_Drained.Task;
        }
        try
        {
            await drained;
            using FileStream exclusive = AuthoringProcessLock.Acquire(m_ProcessLockPath, true);
            m_Writing.Value = true;
            return await action();
        }
        finally
        {
            m_Writing.Value = false;
            TaskCompletionSource<object> done;
            lock (m_Sync)
            {
                done = m_WriterDone;
                m_WriterDone = null;
                m_Drained = null;
            }
            done.SetResult(null);
            m_Writer.Release();
        }
    }
}
}
