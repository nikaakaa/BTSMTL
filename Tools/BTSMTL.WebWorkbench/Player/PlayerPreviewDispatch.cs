using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using BTSMTL.Authoring.Workspace;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public sealed record PlayerPreviewResume(Guid LiveId, JsonObject Command);
public sealed record PlayerPreviewJobReceipt(PlayerPreviewRequest Request, string Stage,
    DateTimeOffset AcceptedAt, DateTimeOffset? FinishedAt, Guid? LiveId, string? ArchivePath,
    PlayerCommandReceipt? Load, JsonObject? Result, string? Error, Guid? CachedFrom = null)
{
    public Guid JobId => Request.RequestId;
    public string PlayerBuildId => Request.PlayerBuildId;
    public string StatusPath => $"/api/preview/player-jobs/{JobId:N}";
    public PlayerPreviewResume? Resume { get; init; }
    public string? AdoptedMode { get; init; }
}

public sealed record PlayerPreviewJobList(int Capacity, int Concurrency, int Players, int Queued, int ArchiveCacheCapacity,
    PlayerPreviewJobReceipt[] Items);
public sealed record PlayerPreviewJobView(PlayerPreviewJobReceipt Receipt, PreviewArchiveDigest? Archive);
public sealed record PlayerPreviewCancellation(Guid JobId, bool CancellationRequested, string JobStage, string Reason);

public sealed partial class PlayerPreviewJobs
{
    const int QueueCapacity = 8;
    const int ArchiveCacheCapacity = 16;
    readonly Channel<QueuedPlayerPreview> m_Queue = Channel.CreateBounded<QueuedPlayerPreview>(new BoundedChannelOptions(QueueCapacity)
    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    readonly ConcurrentDictionary<Guid, SemaphoreSlim> m_Gates = new();
    readonly ConcurrentDictionary<Guid, CancellationTokenSource> m_Cancellations = new();
    readonly ConcurrentDictionary<Guid, SemaphoreSlim> m_ResumeGates = new();
    readonly CancellationTokenSource m_Stop = new();
    readonly object m_PoolGate = new();
    readonly List<PoolPlayer> m_Pool = new();
    readonly SemaphoreSlim m_PoolReleased = new(0);
    readonly Dictionary<string, PlayerPreviewJobReceipt> m_ArchiveCache = new(StringComparer.Ordinal);
    readonly Queue<string> m_ArchiveCacheOrder = new();
    readonly Dictionary<string, Task> m_Running = new(StringComparer.Ordinal);
    long m_PlayerPeakBytes;

    public bool HasActiveJobs => !m_Cancellations.IsEmpty;

    sealed record QueuedPlayerPreview(PlayerPreviewJobReceipt Receipt, PlayerLiveSessions Live, int Port,
        CancellationTokenSource Cancellation);

    public Task<PlayerPreviewJobReceipt> Start(PlayerPreviewRequest input, PlayerLiveSessions live, int port)
    {
        if (input.Mode != PlayerPreviewMode.Real)
            throw new ArgumentException("Player作业必须明确mode=real；off只编辑内容，quick使用原生Timeline快速路线接口，不能创建Player执行作业。");
        if (input.Channels == null)
            throw new ArgumentException("Player作业需要明确channels录制通道；使用creation-options读取本次预览通道。");
        m_Adapter.ValidateJob(input);
        return Enqueue(input, null, live, port);
    }

    internal Task<PlayerPreviewJobReceipt> Resume(Guid jobId, Guid liveId, PlayerPreviewTarget target,
        JsonObject command, PlayerLiveSessions live)
    {
        PlayerPreviewRequest request = m_Adapter.ResumeRequest(jobId, target, command);
        return Enqueue(request, new(liveId, (JsonObject)command.DeepClone()), live, 0);
    }

    async Task<PlayerPreviewJobReceipt> Enqueue(PlayerPreviewRequest input, PlayerPreviewResume? resume,
        PlayerLiveSessions live, int port)
    {
        if (input.RequestId == Guid.Empty)
            throw new ArgumentException("Player作业需要非空requestId/jobId。");
        SemaphoreSlim gate = m_Gates.GetOrAdd(input.RequestId, _ => new(1));
        await gate.WaitAsync();
        try
        {
            string directory = RequestDirectory(input.RequestId);
            string requestPath = Path.Combine(directory, "request.json");
            object requestValue = resume == null
                ? input : new { Request = input, Resume = resume };
            byte[] request = System.Text.Encoding.UTF8.GetBytes(AuthoringJsonText.Compact(JToken.Parse(
                JsonSerializer.Serialize(requestValue, WorkbenchArtifacts.JsonOptions))));
            if (File.Exists(requestPath))
            {
                if (!File.ReadAllBytes(requestPath).AsSpan().SequenceEqual(request))
                    throw new InvalidOperationException("同一Player作业身份已用于不同构建、内容、输入或采集范围。");
                return ReadJobReceipt(input.RequestId);
            }
            Directory.CreateDirectory(directory);
            WorkbenchArtifacts.WriteBytes(requestPath, request);
            var receipt = new PlayerPreviewJobReceipt(input, "queued", DateTimeOffset.UtcNow,
                null, null, null, null, null, null) { Resume = resume };
            SaveJob(receipt);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_Stop.Token);
            m_Cancellations[input.RequestId] = cancellation;
            if (!m_Queue.Writer.TryWrite(new(receipt, live, port, cancellation)))
            {
                m_Cancellations.TryRemove(input.RequestId, out _);
                cancellation.Dispose();
                return SaveJob(receipt with { Stage = "rejected", FinishedAt = DateTimeOffset.UtcNow,
                    Error = $"常驻Player作业队列已满或已停止，容量为{QueueCapacity}。" });
            }
            return receipt;
        }
        finally { gate.Release(); }
    }

    public PlayerPreviewJobList ListJobs()
    {
        string directory = Path.Combine(m_Runs, "player-jobs");
        int players;
        lock (m_PoolGate)
            players = m_Pool.Count;
        return new(QueueCapacity, m_PoolLimit, players, m_Queue.Reader.Count, ArchiveCacheCapacity,
            Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "receipt.json", SearchOption.AllDirectories)
                .Select(path => WorkbenchArtifacts.Read<PlayerPreviewJobReceipt>(path)).OrderByDescending(receipt => receipt.AcceptedAt).ToArray()
                : []);
    }

    public PlayerPreviewJobView ReadJob(Guid id)
    {
        PlayerPreviewJobReceipt receipt = ReadJobReceipt(id);
        return new(receipt, receipt.ArchivePath is { } path ? m_Adapter.ArchiveDigest(path) : null);
    }

    public async Task<PlayerPreviewCancellation> Cancel(Guid id)
    {
        SemaphoreSlim gate = m_Gates.GetOrAdd(id, _ => new(1));
        await gate.WaitAsync();
        try
        {
            PlayerPreviewJobReceipt receipt = ReadJobReceipt(id);
            if (receipt.Stage is "queued" or "running" &&
                m_Cancellations.TryGetValue(id, out CancellationTokenSource? cancellation))
            {
                cancellation.Cancel();
                return new(id, true, receipt.Stage, "已请求取消；作业终态以同一作业回执为准。");
            }
            return new(id, false, receipt.Stage, "当前Host没有正在执行或排队的该作业。");
        }
        finally { gate.Release(); }
    }

    public string ArchiveFile(Guid id, string relative)
    {
        string directory = Path.GetDirectoryName(SealedArchive(id))!;
        string path = Path.GetFullPath(Path.Combine(directory, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("归档文件必须属于本次封存的正式目录。");
        return File.Exists(path) ? path : throw new FileNotFoundException($"Player归档没有文件'{relative}'。");
    }

    public string SealedArchive(Guid id)
    {
        PlayerPreviewJobReceipt receipt = ReadJobReceipt(id);
        return receipt.ArchivePath
            ?? throw new InvalidOperationException("本次尚未收到Player的封存归档回执，不能读取其二进制页或图片。");
    }

    public void Stop()
    {
        m_Queue.Writer.TryComplete();
        m_Stop.Cancel();
    }

    async Task Dispatch()
    {
        await foreach (QueuedPlayerPreview queued in m_Queue.Reader.ReadAllAsync())
        {
            if (queued.Receipt.Resume is { } resume)
                _ = RunResume(queued, resume.LiveId);
            else
                await Admit(queued);
        }
    }

    // 已有同内容同输入的档案直接返回；同键作业仍在运行时等它结束再判断，不另占播放器。
    async Task Admit(QueuedPlayerPreview queued)
    {
        PlayerPreviewJobReceipt receipt = queued.Receipt;
        CancellationToken cancellation = queued.Cancellation.Token;
        string key = ArchiveCacheKey(receipt.Request);
        TaskCompletionSource? running = null;
        PoolPlayer player;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            PlayerPreviewJobReceipt? cached;
            Task? previous = null;
            lock (m_PoolGate)
            {
                if (!m_ArchiveCache.TryGetValue(key, out cached) && !m_Running.TryGetValue(key, out previous))
                {
                    running = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    m_Running.Add(key, running.Task);
                }
            }
            if (cached != null)
            {
                _ = PlayerInputPreparation.OutputDirectory(receipt.Request.OutputDirectory);
                SaveJob(receipt with { Stage = "completed", FinishedAt = DateTimeOffset.UtcNow,
                    LiveId = cached.LiveId, ArchivePath = cached.ArchivePath, Result = cached.Result,
                    AdoptedMode = cached.AdoptedMode, CachedFrom = cached.JobId });
                await Finish(queued, null);
                return;
            }
            if (previous != null)
            {
                _ = AfterRunning(previous, queued);
                return;
            }
            player = await AcquirePlayer(queued.Live, receipt.Request, queued.Port, cancellation);
        }
        catch (Exception failure)
        {
            EndRunning(key, running);
            SaveFailure(receipt, failure, cancellation);
            await Finish(queued, null);
            return;
        }
        _ = Run(queued, player, key, running);
    }

    async Task AfterRunning(Task previous, QueuedPlayerPreview queued)
    {
        try { await previous.WaitAsync(queued.Cancellation.Token); }
        catch (Exception failure)
        {
            SaveFailure(queued.Receipt, failure, queued.Cancellation.Token);
            await Finish(queued, null);
            return;
        }
        await Admit(queued);
    }

    // 续播使用调用者自己的实时会话，不占常驻池；同一会话的续播依次执行。
    async Task RunResume(QueuedPlayerPreview queued, Guid liveId)
    {
        SemaphoreSlim gate = m_ResumeGates.GetOrAdd(liveId, _ => new(1));
        try { await gate.WaitAsync(queued.Cancellation.Token); }
        catch (Exception failure)
        {
            SaveFailure(queued.Receipt, failure, queued.Cancellation.Token);
            await Finish(queued, null);
            return;
        }
        try { await Run(queued, null, null, null); }
        finally { gate.Release(); }
    }

    async Task Run(QueuedPlayerPreview queued, PoolPlayer? player, string? cacheKey, TaskCompletionSource? running)
    {
        PlayerPreviewJobReceipt receipt = queued.Receipt;
        CancellationToken cancellation = queued.Cancellation.Token;
        try
        {
            PlayerPreviewRequest request = receipt.Request;
            Guid id = player?.LiveId ?? receipt.Resume!.LiveId;
            receipt = SaveJob(receipt with { Stage = "running", LiveId = id });
            using (var starting = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                starting.CancelAfter(TimeSpan.FromMinutes(2));
                await queued.Live.Ready(id, starting.Token);
            }
            PlayerLiveIdentity identity = queued.Live.Identity(id);
            if (identity.ObservationProtocol != m_Adapter.ObservationProtocol ||
                !identity.ExecutionModes.Contains(receipt.Resume == null ? "batch" : "interactive") ||
                identity.ImageEncoding != "image/jpeg" || request.Channels!.Any(channel =>
                    !identity.Channels.Contains(JsonNamingPolicy.CamelCase.ConvertName(channel.ToString()), StringComparer.Ordinal)))
            {
                receipt = SaveJob(receipt with { Stage = "unavailable", FinishedAt = DateTimeOffset.UtcNow,
                    Error = "选中Player未声明正式观察协议、本次执行方式、image/jpeg或请求的全部通道，需要采用对应正常构建基包。" });
                return;
            }
            PlayerCommandReceipt load = receipt.Resume is { } checkpoint
                ? await queued.Live.SendResolvedCommand(id, checkpoint.Command)
                : await queued.Live.Load(id, m_Adapter.JobLoad(request));
            if (load.Stage == "submitted")
                load = await queued.Live.CompleteCommand(id, load.CommandId!, cancellation);
            receipt = receipt with { Load = load };
            if (load.Stage != "applied")
                throw new InvalidOperationException((string?)load.Ack?["error"] ?? "Player未采用作业内容与输入。");
            receipt = SaveJob(receipt with { AdoptedMode = load.AdoptedMode });
            JsonObject result = await queued.Live.ArchiveResult(id, request.RequestId, cancellation);
            receipt = receipt with { Result = result };
            string path = Path.GetFullPath((string)result["path"]!);
            string expected = Path.GetFullPath(Path.Combine(request.OutputDirectory, "archive.json"));
            if (!string.Equals(path, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Player实际归档路径与本次输出目录不一致。");
            PlayerArchiveResult archive = m_Adapter.ValidateArchive(path, request);
            bool completed = (string?)result["status"] == "completed" && archive.Completed;
            receipt = SaveJob(receipt with { Stage = completed ? "completed" : "failed", ArchivePath = path,
                AdoptedMode = archive.AdoptedMode,
                FinishedAt = DateTimeOffset.UtcNow, Error = completed ? null : (string?)result["error"] });
            if (completed && cacheKey != null) CacheArchive(cacheKey, receipt);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (receipt.LiveId is { } id)
                await queued.Live.Stop(id);
            receipt = SaveJob(receipt with { Stage = "cancelled", FinishedAt = DateTimeOffset.UtcNow, Error = "Player作业已取消。" });
        }
        catch (Exception failure)
        {
            receipt = SaveFailure(receipt, failure, cancellation);
        }
        finally
        {
            if (player != null)
                ReleasePlayer(queued.Live, player);
            EndRunning(cacheKey, running);
            await Finish(queued, receipt.LiveId);
        }
    }

    PlayerPreviewJobReceipt SaveFailure(PlayerPreviewJobReceipt receipt, Exception failure, CancellationToken cancellation) =>
        SaveJob(failure switch
        {
            OperationCanceledException when cancellation.IsCancellationRequested =>
                receipt with { Stage = "cancelled", FinishedAt = DateTimeOffset.UtcNow, Error = "Player作业已取消。" },
            PlayerInputUnavailableException =>
                receipt with { Stage = "unavailable", FinishedAt = DateTimeOffset.UtcNow, Error = failure.Message },
            _ => receipt with { Stage = m_Stop.IsCancellationRequested ? "cancelled" : "failed",
                FinishedAt = DateTimeOffset.UtcNow, Error = failure.Message }
        });

    async Task Finish(QueuedPlayerPreview queued, Guid? liveId)
    {
        if (liveId is { } id)
            queued.Live.ForgetArchive(id, queued.Receipt.JobId);
        SemaphoreSlim gate = m_Gates[queued.Receipt.JobId];
        await gate.WaitAsync();
        try
        {
            m_Cancellations.TryRemove(queued.Receipt.JobId, out _);
            queued.Cancellation.Dispose();
        }
        finally { gate.Release(); }
    }

    void EndRunning(string? key, TaskCompletionSource? running)
    {
        if (running == null)
            return;
        lock (m_PoolGate)
            m_Running.Remove(key!);
        running.SetResult();
    }

    // 常驻池：优先复用同构建、同画面尺寸的空闲播放器；没有时按实测内存决定是否新开，否则替换空闲的旧播放器或排队等待。
    async Task<PoolPlayer> AcquirePlayer(PlayerLiveSessions live, PlayerPreviewRequest request, int port,
        CancellationToken cancellation)
    {
        while (true)
        {
            PoolPlayer? chosen = null;
            bool start = false;
            bool replace = false;
            lock (m_PoolGate)
            {
                m_Pool.RemoveAll(player => !player.Busy && !live.Identity(player.LiveId).Running);
                PoolPlayer? idle = null;
                foreach (PoolPlayer player in m_Pool)
                {
                    if (player.Busy)
                        continue;
                    PlayerLiveIdentity identity = live.Identity(player.LiveId);
                    if (identity.PlayerBuildId == request.PlayerBuildId &&
                        identity.Width == request.Width && identity.Height == request.Height)
                    {
                        chosen = player;
                        break;
                    }
                    idle ??= player;
                }
                if (chosen == null && CanGrowPool())
                {
                    chosen = new PoolPlayer();
                    m_Pool.Add(chosen);
                    start = true;
                }
                else if (chosen == null && idle != null)
                {
                    chosen = idle;
                    start = replace = true;
                }
                if (chosen != null)
                    chosen.Busy = true;
            }
            if (chosen == null)
            {
                await m_PoolReleased.WaitAsync(cancellation);
                continue;
            }
            if (!start)
                return chosen;
            try
            {
                if (replace)
                    await live.Stop(chosen.LiveId);
                m_Stop.Token.ThrowIfCancellationRequested();
                Guid id = Guid.NewGuid();
                await live.Start(new(id, request.PlayerBuildId, request.Width, request.Height, HideWindow: true), port);
                chosen.LiveId = id;
                return chosen;
            }
            catch
            {
                lock (m_PoolGate)
                    m_Pool.Remove(chosen);
                m_PoolReleased.Release();
                throw;
            }
        }
    }

    // 第一个播放器总能启动；之后须已实测过播放器峰值工作集，且新开后仍给系统留下总物理内存的 1/8。
    bool CanGrowPool()
    {
        if (m_Pool.Count == 0)
            return true;
        if (m_Pool.Count >= m_PoolLimit || m_PlayerPeakBytes == 0)
            return false;
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new InvalidOperationException($"无法读取系统物理内存：{Marshal.GetLastPInvokeError()}。");
        return (long)status.AvailablePhysical - m_PlayerPeakBytes >= (long)(status.TotalPhysical / 8);
    }

    void ReleasePlayer(PlayerLiveSessions live, PoolPlayer player)
    {
        long? peak = live.PeakWorkingSet(player.LiveId);
        lock (m_PoolGate)
        {
            if (peak is { } bytes)
                m_PlayerPeakBytes = Math.Max(m_PlayerPeakBytes, bytes);
            player.Busy = false;
        }
        m_PoolReleased.Release();
    }

    string ArchiveCacheKey(PlayerPreviewRequest request) => WorkbenchArtifacts.Hash(System.Text.Encoding.UTF8.GetBytes(
        AuthoringJsonText.Compact(JToken.Parse(JsonSerializer.Serialize(new
        {
            Protocol = m_Adapter.ObservationProtocol, request.Mode, request.PlayerBuildId, request.ProjectParameters,
            request.Width, request.Height, request.Quality, Channels = request.Channels!.Order().ToArray()
        }, WorkbenchArtifacts.JsonOptions)))));

    void CacheArchive(string key, PlayerPreviewJobReceipt receipt)
    {
        lock (m_PoolGate)
        {
            if (m_ArchiveCache.Count == ArchiveCacheCapacity)
                m_ArchiveCache.Remove(m_ArchiveCacheOrder.Dequeue());
            m_ArchiveCache.Add(key, receipt);
            m_ArchiveCacheOrder.Enqueue(key);
        }
    }

    string RequestDirectory(Guid id) => Path.Combine(m_Runs, "player-jobs", id.ToString("N"));

    PlayerPreviewJobReceipt ReadJobReceipt(Guid id) =>
        WorkbenchArtifacts.Read<PlayerPreviewJobReceipt>(Path.Combine(RequestDirectory(id), "receipt.json"));

    PlayerPreviewJobReceipt SaveJob(PlayerPreviewJobReceipt receipt)
    {
        WorkbenchArtifacts.Write(Path.Combine(RequestDirectory(receipt.JobId), "receipt.json"), receipt);
        return receipt;
    }

    sealed class PoolPlayer
    {
        public Guid LiveId;
        public bool Busy;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
