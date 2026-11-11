using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BTSMTL.WebWorkbench;

public sealed record PlayerCommandReceipt(string Stage, string? CommandId, JsonNode? Ack)
{
    public Guid? JobId { get; init; }
    public JsonNode? Result { get; init; }
    public string? AdoptedMode { get; init; }
    public BTSMTL.WebWorkbench.PreviewContentIdentity? Content { get; init; }
    public BTSMTL.WebWorkbench.PreviewClock? Clock { get; init; }
    public double? Milliseconds { get; init; }
}
public sealed record PlayerLiveIdentity(Guid LiveId, string PlayerBuildId, int Width, int Height, bool Running,
    string? LiveProtocol, string? ObservationProtocol, string[] ExecutionModes, string? ImageEncoding, string[] Channels);

// 常驻 Unity 预览播放器：主机启动并持有进程，播放器经 WebSocket 连回；
// 网页与 CLI/MCP 的命令经主机转给播放器，画面与逐帧结果由同一 Player 报告。
public sealed class PlayerLiveSessions
{
    const string LiveProtocol = "btsmtl.preview-live/1";
    const string StreamOn = "{\"type\":\"stream\",\"enabled\":true}";
    const string StreamOff = "{\"type\":\"stream\",\"enabled\":false}";
    static readonly JsonSerializerOptions s_Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly HashSet<string> s_Commands = new(StringComparer.Ordinal)
    {
        "load", "play", "pause", "step", "seek", "bake", "retime", "camera", "stream", "surface", "input", "pick", "capture", "stop"
    };
    readonly PlayerPreviewJobs m_Builds;
    readonly IPlayerAdapter m_Adapter;
    readonly string m_Live;
    readonly string m_Temporary;
    readonly ConcurrentDictionary<Guid, LivePlayer> m_Players = new();
    readonly SemaphoreSlim m_StartGate = new(1);

    public PlayerLiveSessions(IPlayerAdapter adapter, PlayerPreviewJobs builds)
    {
        m_Adapter = adapter;
        m_Builds = builds;
        m_Live = Path.Combine(adapter.RunsDirectory, "live");
        m_Temporary = adapter.TemporaryDirectory;
    }

    public object List() => m_Players.Values.OrderByDescending(player => player.StartedAt).Select(Describe).ToArray();

    public object Read(Guid id) => Describe(Require(id));

    public PlayerLiveIdentity Identity(Guid id)
    {
        LivePlayer player = Require(id);
        lock (player.Gate)
            return IdentityUnlocked(player);
    }

    // 网页按播放器进程复制共享画面区句柄；进程未起或已退出时为空。
    public int? ProcessId(Guid id)
    {
        LivePlayer player = Require(id);
        lock (player.Gate)
            return player.Process is { HasExited: false } process ? process.Id : null;
    }

    static PlayerLiveIdentity IdentityUnlocked(LivePlayer player) =>
        new(player.Id, player.BuildId, player.Width, player.Height, player.Process is { HasExited: false },
            (string?)player.Hello?["liveProtocol"], (string?)player.Hello?["observationProtocol"],
            player.Hello?["executionModes"]?.Deserialize<string[]>() ?? [], (string?)player.Hello?["imageEncoding"],
            player.Hello?["channels"]?.Deserialize<string[]>() ?? []);

    public Task Ready(Guid id, CancellationToken cancellation) => Require(id).Ready.Task.WaitAsync(cancellation);

    internal long? PeakWorkingSet(Guid id)
    {
        LivePlayer player = Require(id);
        Process? process;
        lock (player.Gate)
            process = player.Process;
        if (process is not { HasExited: false })
            return null;
        try
        {
            process.Refresh();
            return process.PeakWorkingSet64;
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return null;
        }
    }

    public async Task<object> Start(StartLivePlayerRequest request, int port)
    {
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("常驻播放器需要非空 requestId。");
        if (request.Width < 64 || request.Height < 64)
            throw new ArgumentException("画面尺寸至少 64。");
        await m_StartGate.WaitAsync();
        try
        {
            if (m_Players.TryGetValue(request.RequestId, out LivePlayer? existing))
                return existing.BuildId == request.PlayerBuildId ? Describe(existing)
                    : throw new InvalidOperationException("同一请求身份已用于另一个播放器构建。");
            string executable = m_Builds.Executable(request.PlayerBuildId);
            string directory = Path.Combine(m_Live, request.RequestId.ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "captures"));
            var player = new LivePlayer(request.RequestId, request.PlayerBuildId, directory,
                Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), request.Width, request.Height, port, request.HideWindow, m_Adapter);
            Launch(player, executable);
            m_Players[player.Id] = player;
            return Describe(player);
        }
        finally { m_StartGate.Release(); }
    }

    // 代码更新后换用新播放器版本：同一常驻身份与网页连接，旧进程停止后启动新版本，连回时重放最近一次载入。
    public async Task<object> Swap(Guid id, SwapLivePlayerRequest request)
    {
        LivePlayer player = Require(id);
        string executable = m_Builds.Executable(request.PlayerBuildId);
        await m_StartGate.WaitAsync();
        try
        {
            await Terminate(player);
            lock (player.Gate)
            {
                player.Process = null;
                player.BuildId = request.PlayerBuildId;
                player.State = "starting";
                player.ExitCode = null;
                player.Hello = null;
                player.Status = null;
                player.LatestFrame = null;
                player.Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                player.ReloadPending = player.LastLoad != null;
            }
            Launch(player, executable);
            Broadcast(player, Envelope("status", player));
            return Describe(player);
        }
        finally { m_StartGate.Release(); }
    }

    void Launch(LivePlayer player, string executable)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = Path.GetDirectoryName(executable)!,
            WindowStyle = player.HideWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        };
        foreach (string argument in m_Adapter.LaunchArguments(
                     $"ws://127.0.0.1:{player.Port}/api/preview/live/{player.Id:N}/player?token={player.Token}",
                     player.Directory, player.Width, player.Height, player.HideWindow))
            start.ArgumentList.Add(argument);
        start.Environment["TEMP"] = m_Temporary;
        start.Environment["TMP"] = m_Temporary;
        Process process = Process.Start(start) ?? throw new InvalidOperationException($"无法启动播放器 '{executable}'。");
        lock (player.Gate)
            player.Process = process;
        _ = Observe(player, process);
    }

    async Task Observe(LivePlayer player, Process process)
    {
        await process.WaitForExitAsync();
        lock (player.Gate)
        {
            if (player.Process != process)
                return;
            player.ExitCode = process.ExitCode;
            player.State = "exited";
        }
        player.CancelPending("播放器进程已退出。");
        Broadcast(player, Envelope("status", player));
    }

    // 网页、CLI 与 MCP 共用：校验并展开命令，转给播放器；有回执的命令等待片刻后返回实际结果。
    public Task<PlayerCommandReceipt> Load(Guid id, LoadPlayerPreviewRequest request) => Command(id,
        JsonSerializer.SerializeToNode(new { Type = "load", CommandId = request.CommandId.ToString(),
            request.Content, request.Channels, request.ExecutionMode,
            request.Quality }, s_Json)!.AsObject());

    public async Task<PlayerCommandReceipt> Command(Guid id, JsonObject command)
    {
        LivePlayer player = Require(id);
        player.RequireConnected();
        JsonObject resolved = Resolve(player, command, out string type, out string? commandId, out Guid? jobId);
        if (type == "resume")
        {
            PlayerPreviewTarget target;
            lock (player.Gate)
                target = player.PreviewTargetUnlocked()
                    ?? throw new InvalidOperationException("续播采集需要Player已确认采用的原场景与输入来源。");
            PlayerPreviewJobReceipt receipt = await m_Builds.Resume(jobId!.Value, id, target, resolved, this);
            return new(receipt.Stage, commandId, null) { JobId = receipt.JobId };
        }
        return await SendResolvedCommand(id, resolved);
    }

    internal async Task<PlayerCommandReceipt> SendResolvedCommand(Guid id, JsonObject resolved)
    {
        LivePlayer player = Require(id);
        string type = (string)resolved["type"]!;
        if (type == "stream")
        {
            player.ConfigureStream(resolved);
            return new("sent", null, null);
        }
        string? commandId = type is "input" or "camera" ? null : (string?)resolved["commandId"];
        Task<JsonNode>? ack = commandId == null ? null : player.Expect(commandId);
        if (type == "load")
        {
            lock (player.Gate)
            {
                player.LastLoad = (JsonObject)resolved.DeepClone();
                player.LatestFrame = null;
            }
            if ((string?)resolved["content"]?["recording"]?["jobId"] is { } jobId)
                player.BeginArchive(jobId);
        }
        if (type == "resume")
            player.BeginArchive((string)resolved["jobId"]!);
        try { await player.SendToPlayer(resolved.ToJsonString()); }
        catch (InvalidOperationException)
        {
            if (commandId != null)
                player.Forget(commandId);
            throw;
        }
        if (ack == null)
            return new("sent", null, null);
        Task finished = await Task.WhenAny(ack, Task.Delay(type is "load" or "seek" ? 15000 : 3000));
        return finished == ack
            ? CommandReceipt(player, commandId!, ack.Result)
            : new("submitted", commandId, null);
    }

    public PlayerCommandReceipt ReadCommand(Guid id, string commandId)
    {
        LivePlayer player = Require(id);
        lock (player.Gate)
        {
            if (player.Acks.TryGetValue(commandId, out var ack))
                return CommandReceipt(player, commandId, ack.Message.DeepClone());
            if (!player.Sent.TryGetValue(commandId, out SentCommand sent))
                throw new KeyNotFoundException($"常驻播放器没有命令 '{commandId}'。");
            if (sent.JobId is { } jobId)
            {
                PlayerPreviewJobReceipt job = m_Builds.ReadJob(jobId).Receipt;
                return new(job.Stage is "queued" or "running" ? "submitted" : "not-applied", commandId, null)
                    { JobId = jobId };
            }
            return new(player.State == "exited" ? "not-applied" : "submitted", commandId, null);
        }
    }

    public async Task<PlayerCommandReceipt> CompleteCommand(Guid id, string commandId, CancellationToken cancellation)
    {
        JsonNode ack = await Require(id).CommandResult(commandId).WaitAsync(cancellation);
        return CommandReceipt(Require(id), commandId, ack);
    }

    PlayerCommandReceipt CommandReceipt(LivePlayer player, string commandId, JsonNode ack)
    {
        SentCommand sent;
        long receivedTimestamp;
        lock (player.Gate)
        {
            sent = player.Sent[commandId];
            receivedTimestamp = player.Acks[commandId].ReceivedTimestamp;
        }
        string? path = (string?)ack["path"];
        var projected = m_Adapter.Message(player.Id, (JsonObject)ack);
        return new((bool?)ack["ok"] == true ? "applied" : "rejected", commandId,
            projected.Json)
        {
            JobId = sent.JobId,
            Milliseconds = Stopwatch.GetElapsedTime(sent.StartedTimestamp, receivedTimestamp).TotalMilliseconds,
            AdoptedMode = (string?)projected.Json["adoptedMode"],
            Content = projected.Content,
            Clock = projected.Clock,
            Result = sent.Type == "checkpoint-self-check" && !string.IsNullOrEmpty(path)
                ? JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) : null
        };
    }

    internal Task<JsonObject> ArchiveResult(Guid id, Guid jobId, CancellationToken cancellation) =>
        Require(id).ArchiveResult(jobId.ToString("N")).WaitAsync(cancellation);

    internal void ForgetArchive(Guid id, Guid jobId) => Require(id).ForgetArchive(jobId.ToString("N"));

    public string Capture(Guid id, string name)
    {
        LivePlayer player = Require(id);
        if (!Guid.TryParse(Path.GetFileNameWithoutExtension(name), out _) || Path.GetExtension(name) != ".png")
            throw new ArgumentException("截图名必须是命令身份加 .png。");
        string path = Path.Combine(player.Directory, "captures", name);
        return File.Exists(path) ? path : throw new FileNotFoundException($"没有截图 '{name}'。");
    }

    internal PreviewFrameSource FrameSource(Guid id, ReadOnlySpan<byte> packet) => FrameSource(Require(id), packet);

    PreviewFrameSource FrameSource(LivePlayer player, ReadOnlySpan<byte> packet)
    {
        lock (player.Gate)
            return m_Adapter.FrameSource(player.Id, packet, player.Acks[PlayerPreviewVideo.LoadCommandId(packet).ToString()].Message);
    }

    JsonObject Resolve(LivePlayer player, JsonObject command, out string type, out string? commandId, out Guid? jobId)
    {
        type = (string?)command["type"] ?? throw new ArgumentException("命令需要 type。");
        if (!s_Commands.Contains(type) && !m_Adapter.Commands.Contains(type))
            throw new ArgumentException($"常驻播放器没有命令 '{type}'。");
        commandId = (string?)command["commandId"];
        bool needsId = type is not ("input" or "camera" or "stream");
        if (needsId)
        {
            if (!Guid.TryParse(commandId, out Guid parsedId))
                throw new ArgumentException($"命令 '{type}' 需要 GUID 形式的 commandId，重查时保持不变。");
            commandId = parsedId.ToString();
        }
        else
            commandId = null;
        jobId = null;
        var resolved = (JsonObject)command.DeepClone();
        if (needsId)
            resolved["commandId"] = commandId;
        if (type == "capture")
            resolved["path"] = Path.Combine(player.Directory, "captures", Guid.Parse(commandId!).ToString("N") + ".png");
        if (type == "load")
            resolved = ResolveLoad(player, resolved.Deserialize<LoadPlayerPreviewRequest>(WorkbenchArtifacts.JsonOptions)!);
        resolved = m_Adapter.ResolveCommand(resolved, player.PreviewTargetUnlocked()?.Content);
        if (type == "resume")
            jobId = Guid.Parse((string)resolved["jobId"]!);
        lock (player.Gate)
            if (commandId != null && !player.Sent.TryAdd(commandId, new(type, jobId, Stopwatch.GetTimestamp())))
                throw new InvalidOperationException($"命令 '{commandId}' 已经提交；请用读取命令结果重查，不要重发。");
        return resolved;
    }

    JsonObject ResolveLoad(LivePlayer player, LoadPlayerPreviewRequest request)
    {
        lock (player.Gate)
            if ((string?)player.Hello?["liveProtocol"] != LiveProtocol)
                throw new PlayerInputUnavailableException("所选Player未声明btsmtl.preview-live/1内容与时钟协议，需要采用对应正常构建基包。");
        JsonObject content = m_Adapter.PrepareContent(request.Content);
        var prepared = new JsonObject
        {
            ["type"] = "load", ["commandId"] = request.CommandId.ToString(), ["content"] = content
        };
        prepared["executionMode"] = JsonSerializer.SerializeToNode(request.ExecutionMode, WorkbenchArtifacts.JsonOptions);
        prepared["quality"] = request.Quality;
        prepared["channels"] = JsonSerializer.SerializeToNode(request.Channels, WorkbenchArtifacts.JsonOptions);
        if (content["recording"] is { } recording)
        {
            prepared["width"] = (int)recording["width"]!;
            prepared["height"] = (int)recording["height"]!;
        }
        else
        {
            prepared["width"] = player.Width;
            prepared["height"] = player.Height;
        }
        return prepared;
    }

    public Task Stop(Guid id) => Terminate(Require(id));

    // 快预览复用已连接的同一构建播放器，只换载入内容；进程退出、断开或构建变了才重开。
    public bool Connected(Guid id, string buildId)
    {
        if (!m_Players.TryGetValue(id, out LivePlayer? player))
            return false;
        lock (player.Gate)
            return player.BuildId == buildId && player.State == "connected" && player.PlayerPump != null;
    }

    public async Task ReleaseQuick(Guid id)
    {
        await Stop(id);
        m_Players.TryRemove(id, out _);
    }

    static async Task Terminate(LivePlayer player)
    {
        Process? process = player.Process;
        if (process is { HasExited: false })
        {
            try { await player.SendToPlayer(JsonSerializer.Serialize(new { type = "stop", commandId = Guid.NewGuid().ToString() })); }
            catch (InvalidOperationException) { }
            if (!process.WaitForExit(5000))
                process.Kill(true);
            await process.WaitForExitAsync();
        }
    }

    public void StopAll()
    {
        foreach (LivePlayer player in m_Players.Values)
            if (player.Process is { HasExited: false })
                player.Process.Kill(true);
    }

    public async Task AcceptPlayer(Guid id, string token, WebSocket socket)
    {
        LivePlayer player = Require(id);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(token), Encoding.ASCII.GetBytes(player.Token)))
            throw new UnauthorizedAccessException("播放器连接口令不符。");
        var pump = new SocketPump(socket);
        lock (player.Gate)
        {
            if (player.PlayerPump != null)
                throw new InvalidOperationException("该播放器已经连接。");
            player.PlayerPump = pump;
            player.State = "connected";
        }
        Task sending = pump.Run();
        try
        {
            await Receive(socket, text => OnPlayerText(player, text), packet => OnPlayerVideo(player, packet));
        }
        finally
        {
            lock (player.Gate)
            {
                if (player.PlayerPump == pump)
                {
                    player.PlayerPump = null;
                    if (player.State != "exited" && player.State != "starting")
                        player.State = "disconnected";
                }
            }
            pump.Complete();
            await sending;
            player.CancelPending("播放器连接已断开。");
            Broadcast(player, Envelope("status", player));
        }
    }

    public async Task AcceptViewer(Guid id, WebSocket socket)
    {
        LivePlayer player = Require(id);
        var pump = new SocketPump(socket);
        lock (player.Gate)
        {
            bool demand = player.StreamDemandUnlocked;
            bool continuous = player.ContinuousStreamUnlocked;
            player.Viewers[pump] = 0;
            pump.Text(Envelope("status", player));
            bool resuming = !continuous && player.ContinuousStreamUnlocked;
            if (!resuming && player.LatestFrame is { } frame && PlayerPreviewVideo.IsSurface(frame))
                pump.Video(frame);
            player.UpdateStreamDemandUnlocked(demand);
        }
        Task sending = pump.Run();
        try
        {
            await Receive(socket, async text =>
            {
                try { await Command(id, JsonNode.Parse(text)!.AsObject()); }
                catch (Exception failure) when (failure is ArgumentException or InvalidOperationException
                                                    or JsonException or KeyNotFoundException or IOException)
                {
                    pump.Text(JsonSerializer.Serialize(new { type = "error", error = failure.Message }));
                }
            }, null);
        }
        finally
        {
            lock (player.Gate)
            {
                bool demand = player.StreamDemandUnlocked;
                player.Viewers.TryRemove(pump, out _);
                player.UpdateStreamDemandUnlocked(demand);
            }
            pump.Complete();
            await sending;
        }
    }

    static async Task Receive(WebSocket socket, Func<string, Task> text, Action<byte[]>? binary)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;
                if (result.MessageType == WebSocketMessageType.Text)
                    await text(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                else
                    binary?.Invoke(message.ToArray());
                message.SetLength(0);
            }
        }
        catch (WebSocketException) { }
    }

    async Task OnPlayerText(LivePlayer player, string text)
    {
        JsonObject message = JsonNode.Parse(text)!.AsObject();
        switch ((string?)message["type"])
        {
            case "hello":
                JsonObject? reload = null;
                lock (player.Gate)
                {
                    player.Hello = message;
                    player.UpdateStreamDemandUnlocked();
                    player.Ready.TrySetResult(true);
                    if (player.ReloadPending && player.LastLoad != null)
                    {
                        reload = (JsonObject)player.LastLoad.DeepClone();
                        reload["commandId"] = Guid.NewGuid().ToString();
                        reload["width"] = player.Width;
                        reload["height"] = player.Height;
                        player.Sent.Add((string)reload["commandId"]!, new("load", null, Stopwatch.GetTimestamp()));
                        player.LastLoad = (JsonObject)reload.DeepClone();
                    }
                    player.ReloadPending = false;
                }
                if (reload != null)
                {
                    await player.SendToPlayer(reload.ToJsonString());
                }
                break;
            case "status":
                lock (player.Gate)
                    player.Status = message;
                break;
            case "archive":
                player.CompleteArchive(message);
                break;
            case "ack":
            case "pick":
                player.Complete(message);
                break;
        }
        if ((string?)message["type"] == "status")
            Broadcast(player, Envelope("status", player));
        else
            Broadcast(player, message.ToJsonString());
    }

    void OnPlayerVideo(LivePlayer player, byte[] packet)
    {
        PlayerPreviewVideo.RequirePacket(packet);
        string loadCommandId = PlayerPreviewVideo.LoadCommandId(packet).ToString();
        lock (player.Gate)
        {
            if ((string?)player.LastLoad?["commandId"] != loadCommandId ||
                !player.Acks.TryGetValue(loadCommandId, out var ack) || (bool?)ack.Message["ok"] != true)
                return;
            player.LatestFrame = packet;
            if (PlayerPreviewVideo.IsSurface(packet))
                foreach (SocketPump viewer in player.Viewers.Keys)
                    viewer.Video(packet);
        }
    }

    void Broadcast(LivePlayer player, string json)
    {
        foreach (SocketPump viewer in player.Viewers.Keys)
            viewer.Text(json);
    }

    string Envelope(string type, LivePlayer player) =>
        JsonSerializer.Serialize(new { type, live = Describe(player) }, s_Json);

    object Describe(LivePlayer player)
    {
        lock (player.Gate)
            return new
            {
                LiveId = player.Id.ToString("N"), player.BuildId, player.StartedAt, player.State, player.ExitCode,
                player.Width, player.Height, Hello = player.Hello?.DeepClone(),
                Identity = IdentityUnlocked(player),
                Player = player.Status == null ? null : m_Adapter.Status(player.Id, player.Status),
                PreviewTarget = player.PreviewTargetUnlocked(),
                Viewers = player.Viewers.Count,
                ProcessId = player.Process is { HasExited: false } process ? process.Id : (int?)null,
                LatestSample = player.LatestFrame is { } video
                    ? FrameSource(player, video) : null
            };
    }

    LivePlayer Require(Guid id) => m_Players.TryGetValue(id, out LivePlayer? player) ? player
        : throw new KeyNotFoundException($"没有常驻播放器 '{id:N}'。");

    readonly record struct SentCommand(string Type, Guid? JobId, long StartedTimestamp);

    sealed class LivePlayer(Guid id, string buildId, string directory, string token, int width, int height, int port,
        bool hideWindow, IPlayerAdapter adapter)
    {
        public readonly object Gate = new();
        public Guid Id { get; } = id;
        public string BuildId { get; set; } = buildId;
        public string Directory { get; } = directory;
        public string Token { get; } = token;
        public int Width { get; } = width;
        public int Height { get; } = height;
        public int Port { get; } = port;
        public bool HideWindow { get; } = hideWindow;
        public JsonObject? LastLoad { get; set; }
        public bool ReloadPending { get; set; }
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
        public Process? Process { get; set; }
        public string State { get; set; } = "starting";
        public int? ExitCode { get; set; }
        public SocketPump? PlayerPump { get; set; }
        public ConcurrentDictionary<SocketPump, byte> Viewers { get; } = new();
        public JsonObject? Hello { get; set; }
        public TaskCompletionSource<bool> Ready { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public JsonObject? Status { get; set; }
        // LatestFrame 为最近一帧采样头，供最新采样身份与新连上的画面连接。
        public byte[]? LatestFrame { get; set; }
        public Dictionary<string, SentCommand> Sent { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (JsonNode Message, long ReceivedTimestamp)> Acks { get; } = new(StringComparer.Ordinal);
        readonly Dictionary<string, TaskCompletionSource<JsonNode>> m_Pending = new(StringComparer.Ordinal);
        readonly Dictionary<string, TaskCompletionSource<JsonObject>> m_Archives = new(StringComparer.Ordinal);
        // 显式连续推流开关；未设时有观看者才连续采样写画面区。快档画面只按定位单次截取，不靠连续推流。
        bool? m_ContinuousStreamOverride;

        public bool ContinuousStreamUnlocked => m_ContinuousStreamOverride ?? !Viewers.IsEmpty;
        public bool StreamDemandUnlocked => ContinuousStreamUnlocked;

        public void UpdateStreamDemandUnlocked(bool? previous = null)
        {
            bool enabled = StreamDemandUnlocked;
            if (enabled != previous && Hello != null)
                PlayerPump?.Text(enabled ? StreamOn : StreamOff);
        }

        public void ConfigureStream(JsonObject command)
        {
            bool enabled = (bool?)command["enabled"] == true;
            lock (Gate)
            {
                SocketPump pump = PlayerPump ?? throw new InvalidOperationException("播放器尚未连接或已断开。");
                m_ContinuousStreamOverride = enabled;
                command["enabled"] = StreamDemandUnlocked;
                pump.Text(command.ToJsonString());
            }
        }

        public void RequireConnected()
        {
            lock (Gate)
                if (PlayerPump == null)
                    throw new InvalidOperationException("播放器尚未连接或已断开；命令未提交。");
        }

        public Task SendToPlayer(string json)
        {
            SocketPump pump;
            lock (Gate)
                pump = PlayerPump ?? throw new InvalidOperationException("播放器尚未连接或已断开。");
            pump.Text(json);
            return Task.CompletedTask;
        }

        public Task<JsonNode> Expect(string commandId)
        {
            lock (Gate)
            {
                var completion = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_Pending[commandId] = completion;
                return completion.Task;
            }
        }

        public Task<JsonNode> CommandResult(string commandId)
        {
            lock (Gate)
                return Acks.TryGetValue(commandId, out var ack)
                    ? Task.FromResult(ack.Message.DeepClone()) : m_Pending[commandId].Task;
        }

        public void BeginArchive(string jobId)
        {
            lock (Gate)
                m_Archives.Add(jobId, new(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        public Task<JsonObject> ArchiveResult(string jobId)
        {
            lock (Gate)
                return m_Archives[jobId].Task;
        }

        public void ForgetArchive(string jobId)
        {
            lock (Gate)
                m_Archives.Remove(jobId);
        }

        public void CompleteArchive(JsonObject message)
        {
            if ((string?)message["status"] is not ("completed" or "failed")) return;
            string jobId = (string)message["jobId"]!;
            lock (Gate)
            {
                if (m_Archives.TryGetValue(jobId, out var completion))
                    completion.TrySetResult((JsonObject)message.DeepClone());
                if ((string?)LastLoad?["content"]?["recording"]?["jobId"] == jobId)
                    LastLoad!["content"]!.AsObject().Remove("recording");
            }
        }

        public void Forget(string commandId)
        {
            lock (Gate)
            {
                Sent.Remove(commandId);
                m_Pending.Remove(commandId);
            }
        }

        public void Complete(JsonObject ack)
        {
            string? commandId = (string?)ack["commandId"];
            if (commandId == null)
                return;
            long receivedTimestamp = Stopwatch.GetTimestamp();
            if ((string?)ack["type"] == "pick")
                ack["ok"] = true;
            TaskCompletionSource<JsonNode>? completion;
            lock (Gate)
            {
                Acks[commandId] = (ack.DeepClone(), receivedTimestamp);
                m_Pending.Remove(commandId, out completion);
            }
            completion?.TrySetResult(ack.DeepClone());
        }

        public void CancelPending(string reason)
        {
            TaskCompletionSource<JsonNode>[] pending;
            lock (Gate)
            {
                pending = m_Pending.Values.ToArray();
                m_Pending.Clear();
            }
            foreach (TaskCompletionSource<JsonNode> completion in pending)
                completion.TrySetResult(new JsonObject { ["ok"] = false, ["error"] = reason });
            lock (Gate)
                foreach (var completion in m_Archives.Values)
                    completion.TrySetException(new InvalidOperationException(reason));
            Ready.TrySetException(new InvalidOperationException(reason));
        }

        public JsonObject? AppliedLoadUnlocked()
        {
            if (PlayerPump == null ||
                LastLoad?["content"] is not JsonObject content ||
                LastLoad["commandId"] is not JsonNode commandId ||
                !Acks.TryGetValue((string)commandId!, out var ack) || (bool?)ack.Message["ok"] != true ||
                !PlayerInputPreparation.Same(adapter.ContentIdentity(content), adapter.ContentIdentity(ack.Message["content"]!.AsObject())))
                return null;
            return LastLoad;
        }

        public PlayerPreviewTarget? PreviewTargetUnlocked()
        {
            if (Hello?["playerBuildId"] is not JsonNode buildId || AppliedLoadUnlocked() is not { } load)
                return null;
            JsonElement selected = adapter.SelectedContent(load["content"]!.AsObject());
            return new(Id, (string)buildId!, Guid.Parse((string)load["commandId"]!), selected,
                load["channels"]?.Deserialize<PlayerObservationChannel[]>(WorkbenchArtifacts.JsonOptions));
        }

    }

    // 单连接发送泵：文本按序全部发送；画面只保留最新一帧，发送跟不上就丢旧帧。
    sealed class SocketPump(WebSocket socket)
    {
        readonly ConcurrentQueue<string> m_Text = new();
        byte[]? m_Video;
        readonly SemaphoreSlim m_Signal = new(0);
        volatile bool m_Completed;

        public void Text(string json)
        {
            m_Text.Enqueue(json);
            m_Signal.Release();
        }

        public void Video(byte[] packet)
        {
            Volatile.Write(ref m_Video, packet);
            m_Signal.Release();
        }

        public void Complete()
        {
            m_Completed = true;
            m_Signal.Release();
        }

        public async Task Run()
        {
            try
            {
                while (!m_Completed && socket.State == WebSocketState.Open)
                {
                    await m_Signal.WaitAsync();
                    while (m_Text.TryDequeue(out string? text))
                        await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
                    if (Interlocked.Exchange(ref m_Video, null) is { } video)
                        await socket.SendAsync(video, WebSocketMessageType.Binary, true, CancellationToken.None);
                }
            }
            catch (WebSocketException) { }
        }
    }
}

public static class PlayerLiveApi
{
    public static void MapPlayerLiveApi(this WebApplication app, PlayerLiveSessions live)
    {
        app.Lifetime.ApplicationStopping.Register(live.StopAll);
        RouteGroupBuilder routes = app.MapGroup("/api/preview/live");
        routes.MapGet("", live.List);
        routes.MapPost("", (StartLivePlayerRequest request, HttpContext context) =>
            live.Start(request, context.Connection.LocalPort));
        routes.MapGet("/{id:guid}", (Guid id) => live.Read(id));
        routes.MapPost("/{id:guid}/commands", (Guid id, JsonObject command) => live.Command(id, command));
        routes.MapPost("/{id:guid}/load", (Guid id, LoadPlayerPreviewRequest request) => live.Load(id, request));
        routes.MapGet("/{id:guid}/commands/{commandId:guid}",
            (Guid id, Guid commandId) => live.ReadCommand(id, commandId.ToString()));
        routes.MapPost("/{id:guid}/stop", async (Guid id) => { await live.Stop(id); return live.Read(id); });
        routes.MapPost("/{id:guid}/swap", (Guid id, SwapLivePlayerRequest request) => live.Swap(id, request));
        routes.MapGet("/{id:guid}/captures/{name}", (Guid id, string name) => Results.File(live.Capture(id, name), "image/png"));
        routes.Map("/{id:guid}/player", async (Guid id, string token, HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest || !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress!))
                return Results.BadRequest();
            live.Read(id);
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
            await live.AcceptPlayer(id, token, socket);
            return Results.Empty;
        });
    }
}
