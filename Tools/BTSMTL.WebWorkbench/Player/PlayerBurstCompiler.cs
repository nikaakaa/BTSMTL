using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace BTSMTL.WebWorkbench;

// 预览播放器基包关闭 Unity 的 Burst AOT，代码更新后按 Burst 包自身的 Player 构建参数离线重编原生库：
// 根程序集取本次 Player 编译的全部脚本程序集，CPU 目标与优化档读出包工程的 Burst AOT 设置，产物随 DLL 一起进新版本。
public sealed class PlayerBurstCompiler(string project, string temporary)
{
    public const string LibraryName = "lib_burst_generated";
    public const string PluginDirectory = "Plugins/x86_64";

    public bool Enabled => (bool)Settings()["EnableBurstCompilation"]!;

    public async Task<PlayerBurstLibrary> Compile(string managed, IReadOnlyList<UnityPlayerAssemblyInput> assemblies,
        string directory, string cache)
    {
        var watch = Stopwatch.StartNew();
        JsonObject settings = Settings();
        string compiler = Compiler();
        string output = Path.Combine(directory, "out");
        string logs = Path.Combine(directory, "logs");
        string work = Path.Combine(directory, "temp");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(cache);
        // 各方法的编译结果按内容散列存进固定缓存目录，DLL 小改时只重编变化的方法；临时目录每次随机命名，编完即删。
        var options = new List<string>
        {
            "--platform=Windows", "--force-disable-frame-info-registration",
            "--assembly-folder=" + managed,
            "--output=" + Path.Combine(output, LibraryName),
            "--temp-folder=" + work,
            "--cache-directory=" + cache,
            "--decode-folder=" + Path.Combine(directory, "decode")
        };
        options.AddRange(Targets((int)settings["CpuTargetsX64"]!).Select(target => "--target=" + target));
        options.AddRange(Optimization(settings));
        foreach (UnityPlayerAssemblyInput assembly in assemblies)
        {
            string path = Path.Combine(managed, assembly.Name + ".dll");
            if (!File.Exists(path))
                continue;
            options.Add("--assembly-defines=" + assembly.Name + ";" + string.Join(";", assembly.Defines));
            options.Add("--root-assembly=" + path);
        }
        string response = Path.Combine(directory, "bcl.rsp");
        File.WriteAllLines(response, options, new UTF8Encoding(false));
        var start = new ProcessStartInfo(compiler)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("+burstc");
        start.ArgumentList.Add("@" + response);
        start.Environment["TEMP"] = temporary;
        start.Environment["TMP"] = temporary;
        using Process process = Process.Start(start)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string log = Path.Combine(logs, "bcl.log");
        await File.WriteAllTextAsync(log, await stdout + "\n" + await stderr, new UTF8Encoding(false));
        if (Directory.Exists(work))
            Directory.Delete(work, true);
        string library = Path.Combine(output, LibraryName + ".dll");
        if (process.ExitCode != 0 || !File.Exists(library))
            throw new InvalidOperationException($"Burst 原生库离线编译失败（退出代码 {process.ExitCode}），日志：{log}");
        return new PlayerBurstLibrary(output, PlayerScriptCompiler.Fact(compiler), PlayerScriptCompiler.Fact(library),
            PlayerScriptCompiler.Fact(response), log, watch.ElapsedMilliseconds);
    }

    JsonObject Settings() => JsonNode.Parse(File.ReadAllBytes(Path.Combine(project,
        "ProjectSettings", "BurstAotSettings_StandaloneWindows.json")))!["MonoBehaviour"]!.AsObject();

    // 编译器版本由包锁决定；包锁已与基包配方核对摘要，这里只按锁定版本找 PackageCache 中的 bcl。
    string Compiler()
    {
        JsonNode lockFile = JsonNode.Parse(File.ReadAllBytes(Path.Combine(project, "Packages", "packages-lock.json")))!;
        string version = (string?)lockFile["dependencies"]?["com.unity.burst"]?["version"]
            ?? throw new InvalidDataException("包工程未锁定 com.unity.burst，无法离线编译 Burst 原生库。");
        foreach (string package in Directory.EnumerateDirectories(Path.Combine(project, "Library", "PackageCache"), "com.unity.burst@*"))
        {
            string manifest = Path.Combine(package, "package.json");
            if (File.Exists(manifest) && (string?)JsonNode.Parse(File.ReadAllBytes(manifest))!["version"] == version)
                return Path.Combine(package, ".Runtime", "bcl.exe");
        }
        throw new FileNotFoundException($"包工程 PackageCache 中没有锁定版本 com.unity.burst {version}。");
    }

    // 位序号即 Burst 的 BurstTargetCpu 值，与 BurstPlatformAotSettings.GetDesktopCpu64Bit 一致。
    static IEnumerable<string> Targets(int bits)
    {
        string[] names = { "Auto", "X86_SSE2", "X86_SSE4", "X64_SSE2", "X64_SSE4", "AVX", "AVX2" };
        var targets = new List<string>();
        for (int bit = 0; bit < 32; bit++)
            if ((bits & (1 << bit)) != 0)
                targets.Add(bit is >= 3 and < 7 ? names[bit]
                    : throw new InvalidDataException($"Burst AOT 设置含有非 x64 桌面 CPU 目标位 {bit}。"));
        if (targets.Count == 0)
            targets.Add("X64_SSE2");
        return targets;
    }

    static IEnumerable<string> Optimization(JsonObject settings)
    {
        if ((int)settings["FloatMode"]! != 0 || (int?)settings["StackProtector"] is > 0 ||
            (bool)settings["EnableDebugInAllBuilds"]! || !string.IsNullOrWhiteSpace((string?)settings["DisabledWarnings"]))
            throw new InvalidDataException("包工程的 Burst AOT 设置含有离线编译尚未接入的选项（浮点模式、栈保护、调试或禁用警告）。");
        if (!(bool)settings["EnableOptimisations"]!)
            return new[] { "--disable-opt" };
        return (int)settings["OptimizeFor"]! switch
        {
            0 or 4 => new[] { "--opt-level=2" },
            1 => new[] { "--opt-level=3" },
            2 => new[] { "--opt-for-size", "--opt-level=3" },
            3 => new[] { "--opt-level=1" },
            int value => throw new InvalidDataException($"未知的 Burst OptimizeFor 值 {value}。")
        };
    }
}

public sealed record PlayerBurstLibrary(string OutputDirectory, PlayerCompilationFile Compiler, PlayerCompilationFile Library,
    PlayerCompilationFile Response, string Log, long Milliseconds);
