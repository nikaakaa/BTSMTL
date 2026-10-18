using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Btsmtl.BlackboardData.Generation;

internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--retire-other-targets")
        {
            RetireOtherTargets(Path.GetFullPath(args[1]), JsonSerializer.Deserialize<string[]>(args[2])!);
            return 0;
        }
        if (args.Length != 4)
        {
            Console.Error.WriteLine("用法：ThirdPersonSimulation.BlackboardData.Generator <声明输入.json> <输出目录> <Luban.dll> <预编译程序集引用JSON数组>；导出前退役旧目标：--retire-other-targets <输出目录> <当前数值目标JSON数组>");
            return 2;
        }

        string output = Path.GetFullPath(args[1]);
        string pendingRoot = Path.Combine(output, ".generation");
        string pending = Path.Combine(pendingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            using var schema = new BlackboardSchema(args[0]);
            schema.Write(pending, JsonSerializer.Deserialize<string[]>(args[3])!);
            foreach (string target in schema.TargetNames)
                GenerateCSharp(args[2], Path.Combine(pending, target));
            schema.WriteModuleFactories(pending);
            schema.WriteManifest(pending);
            Publish(pending, output);
        }
        finally
        {
            string resolved = Path.GetFullPath(pending);
            if (!resolved.StartsWith(Path.GetFullPath(pendingRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("黑板生成临时目录不属于本次输出。");
            Directory.Delete(resolved, true);
        }
        return 0;
    }

    static void Publish(string pending, string output)
    {
        const string manifestName = "blackboard.manifest.json";
        string manifestPath = Path.Combine(output, manifestName);
        using JsonDocument next = JsonDocument.Parse(File.ReadAllText(Path.Combine(pending, manifestName), Encoding.UTF8));
        using JsonDocument? previous = File.Exists(manifestPath)
            ? JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8)) : null;
        bool structureChanged = previous == null || previous.RootElement.GetProperty("structureIdentity").GetString() != next.RootElement.GetProperty("structureIdentity").GetString();
        bool defaultsChanged = previous == null || previous.RootElement.GetProperty("defaultsIdentity").GetString() != next.RootElement.GetProperty("defaultsIdentity").GetString();
        bool logicChanged = previous == null || previous.RootElement.GetProperty("inputs").GetRawText() != next.RootElement.GetProperty("inputs").GetRawText();
        var previousFiles = previous == null ? new Dictionary<string, string>(StringComparer.Ordinal)
            : previous.RootElement.GetProperty("files").EnumerateArray()
                .ToDictionary(file => file.GetProperty("path").GetString()!, file => file.GetProperty("hash").GetString()!, StringComparer.Ordinal);
        bool codeChanged = next.RootElement.GetProperty("files").EnumerateArray()
            .Where(file => file.GetProperty("path").GetString()!.EndsWith(".cs", StringComparison.Ordinal) ||
                file.GetProperty("path").GetString()!.EndsWith(".asmdef", StringComparison.Ordinal))
            .Any(file => !previousFiles.TryGetValue(file.GetProperty("path").GetString()!, out string? hash) ||
                hash != file.GetProperty("hash").GetString());
        var files = next.RootElement.GetProperty("files").EnumerateArray()
            .Select(file => file.GetProperty("path").GetString()!).ToHashSet(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string source = Path.Combine(pending, file);
            string destination = Path.Combine(output, file);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            byte[] content = File.ReadAllBytes(source);
            if (!File.Exists(destination) || !content.AsSpan().SequenceEqual(File.ReadAllBytes(destination)))
                File.WriteAllBytes(destination, content);
        }
        if (previous != null)
        {
            foreach (JsonElement file in previous.RootElement.GetProperty("files").EnumerateArray())
            {
                string relative = file.GetProperty("path").GetString()!;
                if (files.Contains(relative))
                    continue;
                RetireOwnedFile(output, relative);
            }
        }
        File.Copy(Path.Combine(pending, manifestName), manifestPath, true);
        Console.WriteLine($"黑板构建：类型结构 {(structureChanged ? "变化" : "复用")}；C# 源码 {(codeChanged ? "变化，需编译" : "复用")}；默认配置 {(defaultsChanged ? "变化，需发布" : "复用")}；图逻辑／依赖 {(logicChanged ? "变化，按原图构建" : "复用")}；C++ 仅预留 schema。");
    }

    static void RetireOtherTargets(string output, string[] currentTargetNames)
    {
        using JsonDocument previous = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(output, "blackboard.manifest.json"), Encoding.UTF8));
        var current = currentTargetNames.ToHashSet(StringComparer.Ordinal);
        var retired = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement owner in previous.RootElement.GetProperty("owners").EnumerateArray())
            foreach (JsonProperty target in owner.GetProperty("contracts").EnumerateObject())
                if (!current.Contains(target.Name))
                    retired.Add(target.Name);
        int paths = 0;
        foreach (JsonElement file in previous.RootElement.GetProperty("files").EnumerateArray())
        {
            string relative = file.GetProperty("path").GetString()!;
            int separator = relative.IndexOf('/');
            if (separator < 0 || !retired.Contains(relative[..separator]))
                continue;
            RetireOwnedFile(output, relative);
            paths++;
        }
        Console.WriteLine($"黑板导出准备：已按正式清单退役 {paths} 个旧数值目标拥有路径；根程序集与原拥有清单保留，当前数据仍须正式导出和生成。");
    }

    static void RetireOwnedFile(string output, string relative)
    {
        string retired = Path.GetFullPath(Path.Combine(output, relative));
        if (!retired.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("黑板生成清单包含输出目录之外的文件。");
        File.Delete(retired);
        File.Delete(retired + ".meta");
    }

    static void GenerateCSharp(string entry, string directory)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (string argument in new[] { Path.GetFullPath(entry), "--conf", Path.Combine(directory, "luban.conf"),
            "-t", "runtime", "-c", "cs-bin", "-d", "bin", "--strict", "--locale", "zh",
            "--customTemplateDir", Path.Combine(AppContext.BaseDirectory, "Templates"),
            "-x", "outputCodeDir=" + Path.Combine(directory, "CSharp"),
            "-x", "outputDataDir=" + Path.Combine(directory, "Data") })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Luban C# 生成失败，退出码 {process.ExitCode}。");
    }
}
