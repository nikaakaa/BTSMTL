using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Btsmtl.BlackboardData.Generation;

internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("用法：Btsmtl.BlackboardData.Generator <声明输入.json> <输出目录> <Luban.dll>");
            return 2;
        }

        string output = Path.GetFullPath(args[1]);
        string pendingRoot = Path.Combine(output, ".generation");
        string pending = Path.Combine(pendingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        using var schema = new BlackboardSchema(args[0]);
        try
        {
            schema.Write(pending);
            foreach (string target in new[] { "FPGameplay", "Float32" })
                GenerateCSharp(args[2], Path.Combine(pending, target));
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
                string retired = Path.GetFullPath(Path.Combine(output, relative));
                if (!retired.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("黑板生成清单包含输出目录之外的文件。");
                File.Delete(retired);
                File.Delete(retired + ".meta");
            }
        }
        File.Copy(Path.Combine(pending, manifestName), manifestPath, true);
        Console.WriteLine($"黑板构建：类型结构 {(structureChanged ? "变化" : "复用")}；C# 源码 {(codeChanged ? "变化，需编译" : "复用")}；默认配置 {(defaultsChanged ? "变化，需发布" : "复用")}；图逻辑／依赖 {(logicChanged ? "变化，按原图构建" : "复用")}；C++ 仅预留 schema。");
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
