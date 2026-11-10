using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BTSMTL.WebWorkbench.Shell;

sealed class DesktopLaunch : IDisposable
{
    readonly Profile m_Profile;
    readonly string m_PipeName;
    readonly Mutex m_Owner;
    readonly EventWaitHandle m_Activate;
    NamedPipeClientStream? m_Lease;

    sealed record Profile
    {
        public required string Workspace { get; init; }
        public required string Host { get; init; }
        public required string Temporary { get; init; }
        public required int Port { get; init; }
        public required string[] HostArguments { get; init; }
    }

    DesktopLaunch(Profile profile, string pipeName, Mutex owner, EventWaitHandle activate)
    {
        m_Profile = profile;
        m_PipeName = pipeName;
        m_Owner = owner;
        m_Activate = activate;
    }

    public static DesktopLaunch? Claim()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Workbench.desktop.json");
        var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path, Encoding.UTF8))
            ?? throw new InvalidDataException($"桌面产品配置为空：{path}");
        string workspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.Workspace));
        if (!Directory.Exists(workspace))
            throw new DirectoryNotFoundException($"作者工作区不存在：{workspace}");
        string key = "BTSMTL.Workbench." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(workspace.ToUpperInvariant())));
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\" + key + ".activate");
        var owner = new Mutex(false, "Local\\" + key + ".window");
        bool claimed;
        try { claimed = owner.WaitOne(0); }
        catch (AbandonedMutexException) { claimed = true; }
        if (claimed)
            return new DesktopLaunch(profile, key, owner, activate);
        activate.Set();
        activate.Dispose();
        owner.Dispose();
        return null;
    }

    public RegisteredWaitHandle OnActivate(Action activate) =>
        ThreadPool.RegisterWaitForSingleObject(m_Activate, (_, _) => activate(), null, Timeout.Infinite, false);

    public async Task<Uri> Connect(CancellationToken cancellation)
    {
        try { return await Attach(400, cancellation); }
        catch (TimeoutException) { }

        string host = Path.Combine(AppContext.BaseDirectory, m_Profile.Host);
        if (!File.Exists(host))
            throw new FileNotFoundException("发布物缺少工作台 Host。", host);
        Directory.CreateDirectory(m_Profile.Temporary);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        start.Environment["TEMP"] = m_Profile.Temporary;
        start.Environment["TMP"] = m_Profile.Temporary;
        foreach (string argument in new[] { host, "serve", "--DesktopSession", m_PipeName,
            "--Workspace", m_Profile.Workspace, "--urls", $"http://127.0.0.1:{m_Profile.Port}",
            "--contentRoot", AppContext.BaseDirectory }.Concat(m_Profile.HostArguments))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动工作台 Host。");
        using var starting = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var error = process.StandardError.ReadToEndAsync(starting.Token);
        var connecting = Attach(35_000, starting.Token);
        var exited = process.WaitForExitAsync(starting.Token);
        try
        {
            if (await Task.WhenAny(connecting, exited) == connecting)
                return await connecting;
            await exited;
            throw new InvalidOperationException($"工作台 Host 启动失败（退出码 {process.ExitCode}）。\n{(await error).Trim()}");
        }
        finally
        {
            starting.Cancel();
        }
    }

    async Task<Uri> Attach(int timeout, CancellationToken cancellation)
    {
        var pipe = new NamedPipeClientStream(".", m_PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout, cancellation);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            string? address = await reader.ReadLineAsync(cancellation);
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1")
                throw new InvalidDataException("工作台 Host 没有返回就绪的本机地址。");
            m_Lease = pipe;
            return uri;
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    public async Task WaitForDisconnect(CancellationToken cancellation)
    {
        using NamedPipeClientStream lease = m_Lease!;
        try { await lease.ReadAsync(new byte[1], cancellation); }
        catch (IOException) { }
        finally { m_Lease = null; }
    }

    public void Dispose()
    {
        m_Lease?.Dispose();
        m_Owner.ReleaseMutex();
        m_Owner.Dispose();
        m_Activate.Dispose();
    }
}
