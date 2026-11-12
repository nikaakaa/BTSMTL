using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BTSMTL.Authoring.Workspace;
using BTSMTL.Orchestration;
using BTSMTL.Timeline;

namespace BTSMTL.WebWorkbench;

public sealed class QuickPreviewService(IPreviewQuickAdapter adapter, PlayerServices? players = null)
{
    readonly PlayerLiveSessions? m_Live = players?.Live;
    static readonly JsonSerializerOptions s_QuickJson = new(JsonSerializerDefaults.Web);
    readonly ConcurrentDictionary<Guid, QuickJob> m_QuickJobs = new();
    readonly SemaphoreSlim m_QuickStart = new(1);
    volatile QuickJob? m_ActiveQuick;
    public bool HasActiveQuickJob => m_ActiveQuick?.Work.IsCompleted == false;
    // 快预览播放器跨作业常驻：新作业只发新的载入，进程启动与场景准备只付一次。
    static readonly TimeSpan QuickPlayerIdle = TimeSpan.FromSeconds(60);
    Guid? m_QuickPlayer;
    // 同一成员的发布代数有上限，避免持续编辑耗尽 NTFS 的硬链接数。
    const int QuickMemberGenerationLimit = 512;
    (string Directory, Dictionary<string, string> Members, int Generation)? m_QuickMembers;
    // 读取来源与推导作业共用同一次编译；网页改输入轨会连发多次读取，键相同就等同一个任务。
    readonly object m_CompileGate = new();
    (string Key, Task<QuickPreviewCompilation> Task)? m_Compiled;

    Task<QuickPreviewCompilation> CompileQuick(IPreviewQuickCapture capture, Action<string>? mark = null)
    {
        string key = capture.ContentKey;
        mark?.Invoke("计算编译键");
        lock (m_CompileGate)
        {
            if (m_Compiled is { } cached && cached.Key == key && !cached.Task.IsFaulted && !cached.Task.IsCanceled)
                return cached.Task;
            Task<QuickPreviewCompilation> task = Task.Run(() => QuickPreviewCompilation.Compile(capture));
            m_Compiled = (key, task);
            return task;
        }
    }

    public async Task<PreviewQuickReply<PreviewQuickDescription>> DescribeQuick(PreviewQuickTarget target,
        CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            IPreviewQuickEnvironment actor = await adapter.Open(target, cancellation);
            if (target.Root == null)
                return QuickReply(true, false, null, null, Describe(actor, await actor.Documents(), null, null));
            var capture = await actor.Capture(target.Root, null, cancellation);
            var compiled = await CompileQuick(capture).WaitAsync(cancellation);
            return QuickReply(true, false, null, null, Describe(actor, capture, compiled, new QuickPreviewInputs(capture, actor)));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return QuickReply<PreviewQuickDescription>(false, false, "preview_request_invalid", Explain(error), null);
        }
    }

    PreviewQuickDescription Describe(IPreviewQuickEnvironment actor, IPreviewQuickCapture capture,
        QuickPreviewCompilation compiled, QuickPreviewInputs inputs)
    {
        PreviewQuickSource identity = capture.Identity;
        // 缓存的编译结果可能来自同内容的上一次读取，状态机入口换成本次的保存修订。
        PreviewQuickMachine[] machines = compiled.Machines.Select(machine =>
            machine with { Authoring = identity.Authoring with { Entry = machine.Authoring.Entry } }).ToArray();
        return Describe(actor, capture.Documents(),
            new(identity.Authoring, QuickLabel(capture, compiled), machines, inputs.Schemes(), inputs.CurrentInputScheme,
                inputs.DefaultInput, capture.SceneApplyUnavailableReason,
                compiled.Publication.RuntimeConditions.Values.OrderBy(condition => condition.Key, StringComparer.Ordinal).ToArray()), identity);
    }

    static PreviewQuickDescription Describe(IPreviewQuickEnvironment actor, PreviewQuickDocumentChoice[] documents,
        PreviewQuickRootDescription? root, PreviewQuickSource? identity) =>
        new(actor.ScenePath, actor.CarrierPath, actor.CarrierIdentity, actor.ActorId, actor.TickRate,
            documents, Declarations(actor), actor.Requests.Select(request => new PreviewQuickInputRequest(request.InputId, request.DurationSeconds)).ToArray(),
            root, identity, "从选定场景的正式角色和作者文档准备独立播放器快预览。");

    public async Task<PreviewQuickReply<PreviewQuickStatus>> PlanQuick(PreviewQuickRequest request, int localPort,
        CancellationToken cancellation)
    {
        if (request.Mode != "quick" || request.Target.Root == null)
            return QuickReply<PreviewQuickStatus>(false, false, "preview_request_invalid", "快预览请求需要 mode=quick 和明确的作者入口。", null);
        if (!double.IsFinite(request.InitialSeconds) || request.InitialSeconds < 0)
            return QuickReply<PreviewQuickStatus>(false, false, "preview_request_invalid", "快预览起始游标需要有限的非负秒数。", null);
        await m_QuickStart.WaitAsync(cancellation);
        try
        {
            if (m_ActiveQuick != null)
            {
                await Cancel(m_ActiveQuick);
                if (m_ActiveQuick.Status.CleanupErrors.Length != 0)
                    return QuickReply(false, false, "preview_cleanup_failed", "上一个快预览播放器尚未释放。", m_ActiveQuick.Status);
            }
            cancellation.ThrowIfCancellationRequested();
            var job = new QuickJob(Guid.NewGuid(), request.TimeoutSeconds);
            m_QuickJobs.TryAdd(job.Id, job);
            m_ActiveQuick = job;
            job.Work = Task.Run(() => RunQuick(job, request, localPort));
            return QuickStatusReply(job.Status);
        }
        finally { m_QuickStart.Release(); }
    }

    // 带上调用方已知阶段时等到阶段变化或作业结束再答复（最长 10 秒），网页不用定时轮询就能立刻看到就绪。
    public async Task<PreviewQuickReply<PreviewQuickStatus>> QuickStatus(string jobId, string? stage, CancellationToken cancellation)
    {
        if (FindQuick(jobId) is not { } job)
            return QuickReply<PreviewQuickStatus>(false, false, "preview_job_missing", "找不到快预览作业。", null);
        if (stage != null)
            await Task.WhenAny(job.WhenStageLeaves(stage), Task.Delay(TimeSpan.FromSeconds(10), cancellation));
        cancellation.ThrowIfCancellationRequested();
        return QuickStatusReply(job.Status);
    }

    public async Task<PreviewQuickReply<PreviewQuickStatus>> SeekQuick(string jobId, PreviewQuickSeekRequest request,
        CancellationToken cancellation)
    {
        QuickJob? job = FindQuick(jobId);
        if (job == null)
            return QuickReply<PreviewQuickStatus>(false, false, "preview_job_missing", "找不到快预览作业。", null);
        if (string.IsNullOrEmpty(request.RequestId) || !double.IsFinite(request.Seconds) || request.Seconds < 0)
            return QuickReply(false, false, "preview_request_invalid", "快预览定位需要请求身份和有限的非负秒数。", job.Status);
        await job.SeekGate.WaitAsync(cancellation);
        try
        {
            PreviewQuickStatus status = job.Status;
            if (job.CancelSource.IsCancellationRequested || status.Stage != "ready" || job.Player == null)
                return QuickReply(false, false, "preview_not_ready", "当前快预览作业没有可以定位的播放器。", status);
            if (status.Source!.Revision != request.SourceRevision || status.Plan!.InputRevision != request.InputRevision)
                return QuickReply(false, false, "preview_revision_changed", "快预览定位的作者或输入修订已变化。", status);
            using var deadline = job.Deadline();
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellation);
            var (start, end) = job.TimeRangeRaw;
            long timeRaw = request.Seconds <= start / (double)OrchestrationTime.Scale ? start
                : request.Seconds >= end / (double)OrchestrationTime.Scale ? end
                : OrchestrationTime.FromSeconds(request.Seconds).RawValue;
            PlayerCommandReceipt receipt = await Complete(job.Player!.Value, m_Live!.Command(job.Player.Value, new JsonObject
            {
                ["type"] = "seek", ["commandId"] = Guid.NewGuid().ToString(),
                ["position"] = new JsonObject { ["hostFrame"] = timeRaw.ToString(CultureInfo.InvariantCulture) }
            }), wait.Token);
            if (receipt.Stage != "applied")
                return QuickReply(false, false, "preview_seek_rejected",
                    receipt.Ack?["error"]?.ToString() ?? "播放器拒绝本次快预览定位。", job.Status);
            PreviewQuickFrame frame = Frame(job, receipt.Ack!, status.Plan, request.RequestId, request.Seconds);
            job.Update(value => value with { Frame = frame, SamplesDone = value.SamplesDone + 1, SamplesTotal = value.SamplesTotal + 1 });
            return QuickStatusReply(job.Status);
        }
        catch (OperationCanceledException) when (job.CancelSource.IsCancellationRequested)
        {
            return QuickReply(false, false, "preview_cancelled", "快预览作业已取消。", job.Status);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return QuickReply(false, false, "preview_request_cancelled", "网页已改取更新的时刻，本次定位不再等待。", job.Status);
        }
        catch (Exception error)
        {
            await FailQuick(job, error is OperationCanceledException ? "等待播放器烘焙到该时刻并写出画面超时。" : error.Message);
            return QuickStatusReply(job.Status);
        }
        finally { job.SeekGate.Release(); }
    }

    // 拖技能 Timeline 片段：不读文档、不重编、不写包，按候选在内存里重推路线，播放器原地换时间并先出游标这一帧。
    public async Task<PreviewQuickReply<PreviewQuickStatus>> RetimeQuick(string jobId, PreviewQuickRetimeRequest request,
        CancellationToken cancellation)
    {
        QuickJob? job = FindQuick(jobId);
        if (job == null)
            return QuickReply<PreviewQuickStatus>(false, false, "preview_job_missing", "找不到快预览作业。", null);
        if (string.IsNullOrEmpty(request.RequestId) || !double.IsFinite(request.Seconds) || request.Seconds < 0 ||
            request.Clips.Length == 0)
            return QuickReply(false, false, "preview_request_invalid", "片段时间候选需要请求身份、片段与有限的非负秒数。", job.Status);
        await job.SeekGate.WaitAsync(cancellation);
        try
        {
            PreviewQuickStatus status = job.Status;
            if (job.CancelSource.IsCancellationRequested || status.Stage != "ready" || job.Retiming == null)
                return QuickReply(false, false, "preview_not_ready", "当前快预览作业没有可以修改片段时间的路线。", status);
            if (status.Source!.Revision != request.SourceRevision || status.Plan!.InputRevision != request.InputRevision)
                return QuickReply(false, false, "preview_revision_changed", "片段时间候选的作者或输入修订已变化。", status);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            QuickRetime retime;
            try
            {
                retime = job.Retiming.Retime(request);
            }
            catch (InvalidDataException error)
            {
                return QuickReply(false, false, "preview_request_invalid", error.Message, status);
            }
            double routeMs = clock.Elapsed.TotalMilliseconds;
            var (start, end) = (retime.Route.RootStart.RawValue, retime.Route.End.RawValue);
            if (job.Player == null)
            {
                job.TimeRangeRaw = (start, end);
                job.Update(value => value with { Plan = retime.Plan });
                return QuickStatusReply(job.Status);
            }
            long cursor = request.Seconds <= start / (double)OrchestrationTime.Scale ? start
                : request.Seconds >= end / (double)OrchestrationTime.Scale ? end
                : OrchestrationTime.FromSeconds(request.Seconds).RawValue;
            retime.Payload["cursor"] = cursor;
            using var deadline = job.Deadline();
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellation);
            PlayerCommandReceipt receipt = await Complete(job.Player!.Value, m_Live!.Command(job.Player.Value, new JsonObject
            {
                ["type"] = "retime", ["commandId"] = Guid.NewGuid().ToString(), ["retime"] = retime.Payload
            }), wait.Token);
            if (receipt.Stage != "applied")
                return QuickReply(false, false, "preview_retime_rejected",
                    receipt.Ack?["error"]?.ToString() ?? "播放器拒绝本次片段时间候选。", job.Status);
            job.TimeRangeRaw = (start, end);
            PreviewQuickFrame frame = Frame(job, receipt.Ack!, retime.Plan, request.RequestId, request.Seconds);
            string note = $"片段时间候选 路线 {routeMs.ToString("0.0", CultureInfo.InvariantCulture)} ms（读 " +
                $"{retime.ReadMs.ToString("0.0", CultureInfo.InvariantCulture)} · 推 {retime.RouteMs.ToString("0.0", CultureInfo.InvariantCulture)} · 写 " +
                $"{retime.WriteMs.ToString("0.0", CultureInfo.InvariantCulture)}）· 至画面 " +
                $"{clock.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms";
            job.Update(value => value with { Plan = retime.Plan, Frame = frame, SamplesDone = value.SamplesDone + 1, SamplesTotal = value.SamplesTotal + 1,
                Notes = [.. value.Notes.Where(item => !item.StartsWith("片段时间候选 ", StringComparison.Ordinal)), note] });
            return QuickStatusReply(job.Status);
        }
        catch (OperationCanceledException) when (job.CancelSource.IsCancellationRequested)
        {
            return QuickReply(false, false, "preview_cancelled", "快预览作业已取消。", job.Status);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return QuickReply(false, false, "preview_request_cancelled", "网页已发出更新的片段时间，本次不再等待。", job.Status);
        }
        catch (Exception error)
        {
            if (error is not OperationCanceledException)
                Console.Error.WriteLine($"[Workbench.QuickRetime] {error}");
            await FailQuick(job, error is OperationCanceledException ? "等待播放器按片段时间候选写出画面超时。" : error.Message);
            return QuickStatusReply(job.Status);
        }
        finally { job.SeekGate.Release(); }
    }

    public async Task<PreviewQuickReply<PreviewQuickStatus>> CancelQuick(string jobId, CancellationToken cancellation)
    {
        QuickJob? job = FindQuick(jobId);
        if (job == null)
            return QuickReply<PreviewQuickStatus>(false, false, "preview_job_missing", "找不到快预览作业。", null);
        await m_QuickStart.WaitAsync(cancellation);
        try { await Cancel(job); }
        finally { m_QuickStart.Release(); }
        _ = ReleaseIdleQuickPlayer(job);
        return QuickStatusReply(job.Status);
    }

    // 网页改输入、换方案或重推时先取消旧作业再发新作业，播放器留给紧接着的新作业；
    // 取消后一段时间没有新作业（离开快预览）才关掉进程。
    async Task ReleaseIdleQuickPlayer(QuickJob job)
    {
        await Task.Delay(QuickPlayerIdle);
        await m_QuickStart.WaitAsync();
        try
        {
            if (m_ActiveQuick == job && job.Status.Stage is ("cancelled" or "failed"))
                await ReleaseQuickPlayer();
        }
        finally { m_QuickStart.Release(); }
    }

    async Task RunQuick(QuickJob job, PreviewQuickRequest request, int localPort)
    {
        using var timeout = new CancellationTokenSource(job.Timeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, job.CancelSource.Token);
        try
        {
            // 分段耗时写进作业说明，网页“计算说明”里能直接看到慢在哪一步。
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var timings = new List<string>();
            void Mark(string stage)
            {
                timings.Add($"{stage} {clock.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} 秒");
                clock.Restart();
            }
            deadline.Token.ThrowIfCancellationRequested();
            IPreviewQuickEnvironment actor = await adapter.Open(request.Target, deadline.Token);
            Mark("读取场景与角色声明");
            var capture = await actor.Capture(request.Target.Root!, Mark, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            if (request.SourceRevision != null && capture.Identity.Revision != request.SourceRevision)
                throw new InvalidOperationException("作者文档或场景已变化，请重新读取快预览来源。");
            var inputs = new QuickPreviewInputs(capture, actor);
            var input = inputs.Resolve(request.Input, request.RangeEndSeconds);
            Mark("读取输入方案");
            var compiled = await CompileQuick(capture, Mark).WaitAsync(deadline.Token);
            Mark("编译");
            var route = compiled.Route(input);
            job.TimeRangeRaw = (route.RootStart.RawValue, route.End.RawValue);
            PreviewQuickPlan plan = QuickPreviewPlanProjection.Describe(capture, actor, compiled, input, route);
            Mark("算路线");
            // 跟随当前内容的请求不再单独读取来源：同一次捕获的完整描述随作业状态交回网页。
            PreviewQuickDescription? description = request.SourceRevision == null
                ? Describe(actor, capture, compiled, inputs) : null;
            Mark("来源描述");
            var graph = capture.Identity.Authoring.Entry as PreviewQuickGraphEntry;
            job.Retiming = new QuickRetimeSource(capture, actor, compiled, input, graph?.GraphId);
            job.Update(status => status with { Source = capture.Identity, Plan = plan, Description = description });
            string? playerReason = players == null ? "未注册项目播放器适配。" : players.Jobs.UnavailableReason;
            string? selectedBuild = playerReason == null ? actor.SelectPlayerBuild(players!.Jobs.PublishedBuilds()) : null;
            if (playerReason == null && selectedBuild == null)
                playerReason = $"没有匹配场景 '{actor.ScenePath}' 与角色 '{actor.ActorId}' 的播放器构建。";
            if (playerReason != null)
            {
                job.Update(status => status with { Stage = "ready", Completed = true,
                    Notes = [.. status.Notes, playerReason + " 路线已推导，画面不可用。", string.Join(" · ", timings)] });
                return;
            }
            string build = selectedBuild!;
            Mark("选播放器构建");
            string output = PlayerInputPreparation.OutputDirectory(request.OutputDirectory);
            var motionPrograms = compiled.Publication.Programs;
            Mark("准备表现程序");
            deadline.Token.ThrowIfCancellationRequested();
            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            var previousMembers = m_QuickMembers is { } candidate && candidate.Generation < QuickMemberGenerationLimit &&
                string.Equals(Path.GetDirectoryName(candidate.Directory), Path.GetDirectoryName(output), StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(candidate.Directory) ? m_QuickMembers : null;
            bool ReuseMember(string relative, string hash)
            {
                members.Add(relative, hash);
                if (previousMembers is not { } previousPackage ||
                    !previousPackage.Members.TryGetValue(relative, out string? previousHash) || previousHash != hash)
                    return false;
                WorkbenchArtifacts.LinkImmutable(Path.Combine(output, relative), Path.Combine(previousPackage.Directory, relative));
                return true;
            }
            job.Publication = compiled.Content.Publish(new(output, capture.Identity, input.Revision,
                route, input.Body, motionPrograms, ReuseMember));
            m_QuickMembers = (output, members, (previousMembers?.Generation ?? 0) + 1);
            Mark("写预览包");
            // 路线由 Host 静态推导，载入播放器前就交给网页；播放器准备画面失败时仍保留，可看输入推出了哪些 Timeline。
            job.Update(status => status with { Stage = "loading", OutputDirectory = output, Source = capture.Identity, Plan = plan,
                Description = description });
            deadline.Token.ThrowIfCancellationRequested();
            if (m_QuickPlayer is { } previous && !m_Live!.Connected(previous, build))
                await ReleaseQuickPlayer();
            if (m_QuickPlayer == null)
            {
                m_QuickPlayer = Guid.NewGuid();
                await m_Live!.Start(new(m_QuickPlayer.Value, build, HideWindow: true), localPort);
            }
            Guid player = m_QuickPlayer.Value;
            job.Player = player;
            await m_Live!.Ready(player, deadline.Token);
            Mark("播放器就绪");
            deadline.Token.ThrowIfCancellationRequested();
            job.Loading = true;
            await RequireApplied(player, m_Live!.Load(player, new(job.LoadId,
                job.Publication.Content, [PlayerObservationChannel.Images])), timeout.Token);
            job.Loading = false;
            deadline.Token.ThrowIfCancellationRequested();
            Mark("播放器载入");
            // 烘焙在播放器里逐采样点进行，画面留在播放器内存；网页接好共享画面区后按时刻定位，烘焙未到的时刻由播放器挂起到位再写。
            // 沿用原会话换包时，改动时刻之前的画面照用，播放器从第一个缺画面的采样点续算。
            await RequireApplied(player, m_Live!.Command(player, new JsonObject
                { ["type"] = "bake", ["commandId"] = Guid.NewGuid().ToString() }), deadline.Token);
            Mark("开始烘焙");
            PlayerLiveIdentity identity = m_Live!.Identity(player);
            var quickPlayer = new PreviewQuickPlayer(player.ToString("N"), m_Live!.ProcessId(player), identity.Width, identity.Height);
            string summary = Path.Combine(output, "quick-summary.json");
            WorkbenchArtifacts.Write(summary, new
            {
                source = capture.Identity, plan, playerBuildId = build, packageRevision = job.Publication.Revision
            });
            job.Update(status => status with { Stage = "ready", Completed = true, AdoptedMode = "quick",
                Plan = plan, Player = quickPlayer, SamplesDone = 0, SamplesTotal = 0, SummaryPath = summary,
                Notes = [.. status.Notes, string.Join(" · ", timings)] });
        }
        catch (Exception error)
        {
            await FailQuick(job, error is OperationCanceledException
                ? job.CancelSource.IsCancellationRequested ? "快预览作业已取消。" : "准备快预览或等待首帧超时。"
                : Explain(error));
        }
    }

    async Task<PlayerCommandReceipt> RequireApplied(Guid id, Task<PlayerCommandReceipt> command, CancellationToken cancellation)
    {
        PlayerCommandReceipt receipt = await Complete(id, command, cancellation);
        if (receipt.Stage != "applied")
            throw new InvalidOperationException(receipt.Ack?["error"]?.ToString() ?? "播放器拒绝快预览命令。");
        return receipt;
    }

    async Task<PlayerCommandReceipt> Complete(Guid id, Task<PlayerCommandReceipt> command, CancellationToken cancellation)
    {
        PlayerCommandReceipt receipt = await command.WaitAsync(cancellation);
        return receipt.Stage == "submitted" ? await m_Live!.CompleteCommand(id, receipt.CommandId!, cancellation) : receipt;
    }

    // 定位回执：播放器已把不晚于请求时刻的烘焙点写进共享画面区，回执带帧槽序号、像素翻转与实际采样时刻。
    static PreviewQuickFrame Frame(QuickJob job, JsonNode ack, PreviewQuickPlan plan, string requestId, double requestedSeconds)
    {
        PreviewQuickStatus status = job.Status;
        string sequence = (string?)ack["frame"]?["sequence"] ??
            throw new InvalidDataException("播放器的快预览定位回执缺少共享画面区帧槽序号。");
        bool flipY = (bool?)ack["frame"]?["flipY"] ?? throw new InvalidDataException("播放器的快预览定位回执缺少像素翻转。");
        long sampled = long.Parse((string?)ack["hostFrame"] ?? throw new InvalidDataException("播放器的快预览定位回执缺少采样时刻。"),
            CultureInfo.InvariantCulture);
        return new(status.JobId, requestId, status.Source!.Revision, plan.InputRevision, requestedSeconds,
            sampled / (double)OrchestrationTime.Scale, job.Publication!.Revision + ":" + sequence, sequence, flipY);
    }

    // 已发送的载入等到原命令完成。只有无法确认采用结果时才释放播放器。
    async Task Cancel(QuickJob job)
    {
        if (job.Status.Stage is "cancelled" or "failed")
            return;
        job.Update(status => status with { CancellationRequested = true });
        job.CancelSource.Cancel();
        await job.Work;
        await job.SeekGate.WaitAsync();
        try
        {
            string[] cleanup = job.Loading && job == m_ActiveQuick ? await ReleaseQuickPlayer() : [];
            job.Update(status => status with { Stage = cleanup.Length == 0 ? "cancelled" : "failed",
                Completed = true, Frame = null, CleanupErrors = cleanup, Error = cleanup.FirstOrDefault() });
        }
        finally { job.SeekGate.Release(); }
    }

    async Task FailQuick(QuickJob job, string message)
    {
        bool cancelled = job.CancelSource.IsCancellationRequested;
        string[] cleanup = !cancelled && job.Player.HasValue ? await ReleaseQuickPlayer() : [];
        job.Update(status => status with { Stage = cancelled ? "cancelled" : "failed",
            Completed = true, Frame = null, Error = message, CleanupErrors = cleanup });
        if (!cancelled && !job.Player.HasValue)
            _ = ReleaseIdleQuickPlayer(job);
    }

    async Task<string[]> ReleaseQuickPlayer()
    {
        if (m_QuickPlayer is not { } player)
            return [];
        try
        {
            await m_Live!.ReleaseQuick(player);
            m_QuickPlayer = null;
            return [];
        }
        catch (Exception error)
        {
            return [error.Message];
        }
    }

    static string Explain(Exception error) => WorkbenchErrors.Explain(error);

    QuickJob? FindQuick(string jobId) => Guid.TryParse(jobId, out Guid id) ? m_QuickJobs.GetValueOrDefault(id) : null;

    static PreviewQuickReply<PreviewQuickStatus> QuickStatusReply(PreviewQuickStatus status) => status.Stage switch
    {
        "cancelled" => QuickReply(false, false, "preview_cancelled", status.Error, status),
        "failed" => QuickReply(false, false, "preview_failed", status.Error, status),
        _ => QuickReply(true, !status.Completed, null, null, status)
    };

    static PreviewQuickReply<T> QuickReply<T>(bool success, bool pending, string? code, string? message, T? data)
    {
        object? diagnostic = data is PreviewQuickStatus status ? status with { Frame = null, Description = null } : data;
        return new(success, pending, code, message, data, JsonSerializer.Serialize(new { success, pending, code, message, data = diagnostic }, s_QuickJson));
    }

    static PreviewQuickInputDeclaration[] Declarations(IPreviewQuickEnvironment actor) => actor.Inputs.Select(input =>
        new PreviewQuickInputDeclaration(input.InputId, input.ValueKind.ToString(), input.ValueType.Name)).ToArray();

    static string QuickLabel(IPreviewQuickCapture capture, QuickPreviewCompilation compiled) =>
        compiled.Standalone?.Name ?? capture.Label;

    sealed class QuickJob(Guid id, int requestedTimeout)
    {
        readonly object m_Gate = new();
        PreviewQuickStatus m_Status = new(id.ToString("N"), "planning", false, false, "quick", null,
            null, 0, 1, null, null, null, null, [], [], [], null, null, null);

        public Guid Id { get; } = id;
        public TimeSpan Timeout { get; } = TimeSpan.FromSeconds(requestedTimeout <= 0 ? 180 : Math.Min(requestedTimeout, 600));
        public Guid LoadId { get; } = Guid.NewGuid();
        public CancellationTokenSource CancelSource { get; } = new();
        public SemaphoreSlim SeekGate { get; } = new(1);
        public Task Work { get; set; } = Task.CompletedTask;
        public Guid? Player { get; set; }
        public bool Loading { get; set; }
        public PreviewQuickPublication? Publication { get; set; }
        public QuickRetimeSource? Retiming { get; set; }
        public (long Start, long End) TimeRangeRaw { get; set; }
        public PreviewQuickStatus Status { get { lock (m_Gate) return m_Status; } }

        TaskCompletionSource m_Changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Update(Func<PreviewQuickStatus, PreviewQuickStatus> update)
        {
            TaskCompletionSource? changed = null;
            lock (m_Gate)
            {
                PreviewQuickStatus previous = m_Status;
                m_Status = update(m_Status);
                if (m_Status.Stage != previous.Stage || m_Status.Completed != previous.Completed)
                {
                    changed = m_Changed;
                    m_Changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            changed?.TrySetResult();
        }

        public Task WhenStageLeaves(string stage)
        {
            lock (m_Gate)
                return m_Status.Stage != stage || m_Status.Completed ? Task.CompletedTask : m_Changed.Task;
        }

        public CancellationTokenSource Deadline()
        {
            var deadline = CancellationTokenSource.CreateLinkedTokenSource(CancelSource.Token);
            deadline.CancelAfter(Timeout);
            return deadline;
        }
    }
}
