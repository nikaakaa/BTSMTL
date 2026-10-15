using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Btsmtl.DependencyGuard;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help"))
        {
            Console.WriteLine("DependencyGuard --sdk-root <BTSMTL> [--client-root <UnityClient>] [--rules <rules.json>] [--report <report.json>]");
            return args.Length == 0 ? 2 : 0;
        }
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < args.Length; index += 2)
            options.Add(args[index], args[index + 1]);
        string sdkRoot = Path.GetFullPath(options["--sdk-root"]);
        string? clientRoot = options.TryGetValue("--client-root", out string? client) ? Path.GetFullPath(client) : null;
        string rulePath = options.TryGetValue("--rules", out string? suppliedRules)
            ? Path.GetFullPath(suppliedRules) : Path.Combine(AppContext.BaseDirectory, "rules.json");
        byte[] ruleBytes = File.ReadAllBytes(rulePath);
        var rules = JsonSerializer.Deserialize<GuardRules>(ruleBytes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (rules.Version != 1)
            throw new InvalidDataException($"不支持依赖规则版本 {rules.Version}。");
        var catalog = new DependencyCatalog(sdkRoot, clientRoot, rules);
        catalog.RecordInput(rulePath, ruleBytes);
        var sourceGuard = new SourceGuard(catalog, rules, clientRoot);
        bool scanCompleted = true;
        try
        {
            catalog.Load();
            catalog.CheckDependencies();
            sourceGuard.Check();
        }
        catch (FileNotFoundException error)
        {
            scanCompleted = false;
            catalog.Violations.Add(new("INPUT_CHANGED", error.FileName!, 1, "DependencyGuard", "扫描已枚举输入时文件被删除，检查未完成：" + error.Message));
        }
        catch (DirectoryNotFoundException error)
        {
            scanCompleted = false;
            catalog.Violations.Add(new("INPUT_CHANGED", sdkRoot, 1, "DependencyGuard", "扫描输入目录被删除，检查未完成：" + error.Message));
        }
        Violation[] violations = catalog.Violations.Distinct().OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Line).ThenBy(item => item.Rule, StringComparer.Ordinal).ThenBy(item => item.Detail, StringComparer.Ordinal).ToArray();
        string scope = clientRoot is null ? "SDK-only（未检查 Quantum 宿主与实际作者身份）" : "完整工作区（SDK + 显式 Client）";
        Console.WriteLine($"DependencyGuard：{scope}；{catalog.Modules.Count} 份依赖声明，{sourceGuard.SourceCount} 份源码。");
        foreach (Violation violation in violations)
            Console.WriteLine($"{violation.File}({violation.Line}): error {violation.Rule}: [{violation.Module}] {violation.Detail}");
        int exitCode = !scanCompleted ? 2 : violations.Length == 0 ? 0 : 1;
        Console.WriteLine($"DependencyGuard：{violations.Length} 项违规；扫描完成={scanCompleted}；退出码 {exitCode}。");
        if (options.TryGetValue("--report", out string? reportPath))
        {
            var inputs = catalog.Inputs.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
            {
                string? finalHash = File.Exists(item.Key) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Key))) : null;
                return new GuardInput(item.Key, item.Value, finalHash, item.Value != finalHash);
            }).ToArray();
            var report = new { Scope = scope, SdkRoot = sdkRoot, ClientRoot = clientRoot, ScanCompleted = scanCompleted, QuantumChecked = clientRoot is not null && scanCompleted,
                ModuleCount = catalog.Modules.Count, sourceGuard.SourceCount, sourceGuard.RoleNames, sourceGuard.SkillNames,
                Violations = violations, Inputs = inputs, InputsChanged = !scanCompleted || inputs.Any(item => item.Changed), ExitCode = exitCode };
            File.WriteAllText(Path.GetFullPath(reportPath), JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        return exitCode;
    }
}
