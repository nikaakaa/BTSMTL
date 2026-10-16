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
            Console.WriteLine("DependencyGuard --sdk-root <BTSMTL> [--client-root <UnityClient>] [--unity-evidence <batch.json>] [--rules <rules.json>] [--report <report.json>]");
            Console.WriteLine("DependencyGuard --sdk-root <BTSMTL> --client-root <UnityClient> --produce-reference-policies <request.json> --report <claims.json> [--rules <rules.json>]");
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
        if (rules.Version != 7)
            throw new InvalidDataException($"不支持依赖规则版本 {rules.Version}。");
        var catalog = new DependencyCatalog(sdkRoot, clientRoot, rules);
        catalog.RecordInput(rulePath, ruleBytes);
        string toolBinaryPath = typeof(Program).Assembly.Location;
        catalog.RecordInput(toolBinaryPath, File.ReadAllBytes(toolBinaryPath));
        catalog.UnityEvidencePath = clientRoot is null ? null : options.TryGetValue("--unity-evidence", out string? evidence)
            ? Path.GetFullPath(evidence) : Path.Combine(clientRoot, rules.UnityActualBoundary.EvidenceRelativePath);
        catalog.RulesSHA256 = catalog.Inputs[rulePath];
        catalog.GuardExecutionInputs.UnionWith(new[] { rulePath, toolBinaryPath });
        if (catalog.UnityEvidencePath is not null) catalog.GuardExecutionInputs.Add(catalog.UnityEvidencePath);
        if (options.TryGetValue("--produce-reference-policies", out string? policyRequest))
        {
            if (clientRoot is null || !options.TryGetValue("--report", out string? policyOutput))
                throw new ArgumentException("来源主张生成必须提供 --client-root 与 --report。");
            catalog.LoadDeclarations();
            AutomaticReferencePolicyProduction[] policies = catalog.ProduceReferencePolicies(Path.GetFullPath(policyRequest), out string batchId);
            GuardInput[] policyInputs = ReadFinalInputs(catalog);
            bool changed = policyInputs.Any(input => input.Changed);
            int policyExit = changed ? 2 : 0;
            var result = new { Version = 1, Operation = "ProduceAutomaticReferencePolicies", BatchId = batchId,
                RulesVersion = rules.Version, RulesSHA256 = catalog.RulesSHA256,
                ToolBinaryPath = toolBinaryPath, ToolBinarySHA256 = catalog.Inputs[toolBinaryPath],
                Compilations = policies, Inputs = policyInputs, InputsChanged = changed,
                ScanCompleted = false, QuantumChecked = false, ExitCode = policyExit };
            File.WriteAllText(Path.GetFullPath(policyOutput), JsonSerializer.Serialize(result,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            Console.WriteLine($"DependencyGuard：已生成 {policies.Length} 个目标的自动来源主张；未执行完整守护；输入改变={changed}；退出码 {policyExit}。");
            return policyExit;
        }
        var sourceGuard = new SourceGuard(catalog, rules, clientRoot);
        var stages = new List<GuardStage>();
        bool RunStage(string name, bool required, bool available, Action action, string prerequisite)
        {
            if (!required)
            {
                stages.Add(new(name, false, false, "NotRequired", "当前 SDK-only 范围不包含此 Unity 实际证据阶段。"));
                return true;
            }
            if (!available)
            {
                stages.Add(new(name, true, false, "Blocked", prerequisite));
                return false;
            }
            try
            {
                action();
                stages.Add(new(name, true, true, "Completed", "本阶段已按其正式输入范围完成。"));
                return true;
            }
            catch (FileNotFoundException error)
            {
                Incomplete("INPUT_CHANGED", error.FileName ?? sdkRoot, "已枚举输入缺失：" + error.Message);
            }
            catch (DirectoryNotFoundException error)
            {
                Incomplete("INPUT_CHANGED", sdkRoot, "输入目录缺失：" + error.Message);
            }
            catch (InvalidDataException error)
            {
                Incomplete("INCOMPLETE_STAGE", catalog.UnityEvidencePath ?? rulePath, error.Message);
            }
            catch (JsonException error)
            {
                Incomplete("INCOMPLETE_STAGE", catalog.UnityEvidencePath ?? rulePath, "正式输入协议不完整或格式无效：" + error.Message);
            }
            catch (System.Xml.XmlException error)
            {
                Incomplete("INCOMPLETE_STAGE", sdkRoot, "工程声明 XML 无效：" + error.Message);
            }
            catch (BadImageFormatException error)
            {
                Incomplete("INCOMPLETE_STAGE", catalog.UnityEvidencePath ?? sdkRoot, "实际程序集原件无效：" + error.Message);
            }
            return false;

            void Incomplete(string rule, string file, string detail)
            {
                stages.Add(new(name, true, false, "Incomplete", detail));
                catalog.Violations.Add(new(rule, file, 1, "DependencyGuard/" + name, detail));
            }
        }
        bool declarations = RunStage("DeclarationInputs", true, true, catalog.LoadDeclarations, "");
        bool actualInputs = RunStage("UnityEvidence", clientRoot is not null, declarations,
            catalog.LoadActualUnityInputs, "声明输入未完成，不能接收实际编译证据。");
        if (!actualInputs) catalog.DiscardActualUnityInputs();
        bool sources = RunStage("SourceOwnership", true, declarations, catalog.PrepareSources, "声明输入未完成，源码归属尚未建立。");
        bool declaredDependencies = RunStage("DeclaredDependencies", true, sources,
            catalog.CheckDeclaredDependencies, "正式声明和源码边界未完成。");
        bool sourceScan = RunStage("StructuralSourceScan", true, sources,
            sourceGuard.Check, "源码归属未完成，不能声称全部源已扫描。");
        bool coverage = RunStage("ActualCompilationCoverage", clientRoot is not null, sources && actualInputs,
            catalog.CheckActualCompilationCoverage, "本批实际编译证据或源码范围未完成。");
        bool actualDependencies = RunStage("ActualDependencies", clientRoot is not null, coverage,
            catalog.CheckActualDependencies, "本批完整实际编译覆盖未完成，Unity 自动边和产物依赖尚未判定。");
        bool actualBindings = RunStage("ActualSourceBindings", clientRoot is not null, coverage && sourceScan,
            catalog.CheckActualSourceBindings, "本批完整引用、生成源或结构源码扫描未完成，不能以词法猜测声称语义绑定完成。");
        Violation[] violations = catalog.Violations.Distinct().OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Line).ThenBy(item => item.Rule, StringComparer.Ordinal).ThenBy(item => item.Detail, StringComparer.Ordinal).ToArray();
        string scope = clientRoot is null ? "SDK-only（未检查 Quantum 宿主与实际作者身份）" : "完整工作区（SDK + 显式 Client）";
        Console.WriteLine($"DependencyGuard：{scope}；{catalog.Modules.Count} 份依赖声明，{sourceGuard.SourceCount} 份源码。");
        foreach (Violation violation in violations)
            Console.WriteLine($"{violation.File}({violation.Line}): error {violation.Rule}: [{violation.Module}] {violation.Detail}");
        var inputs = ReadFinalInputs(catalog);
        bool inputsChanged = inputs.Any(item => item.Changed);
        bool scanCompleted = stages.All(stage => !stage.Required || stage.Completed) && !inputsChanged;
        bool sourceScanCompleted = sourceScan && !inputsChanged;
        bool actualSourceBindingsCompleted = clientRoot is not null && actualBindings && !inputsChanged;
        bool quantumChecked = clientRoot is not null && scanCompleted && actualDependencies && actualSourceBindingsCompleted;
        int exitCode = !scanCompleted ? 2 : violations.Length == 0 ? 0 : 1;
        Console.WriteLine($"DependencyGuard：{violations.Length} 项违规；扫描完成={scanCompleted}；输入改变={inputsChanged}；退出码 {exitCode}。");
        if (options.TryGetValue("--report", out string? reportPath))
        {
            var report = new { RulesVersion = rules.Version, ToolBinaryPath = toolBinaryPath, ToolBinarySHA256 = catalog.Inputs[toolBinaryPath],
                Scope = scope, SdkRoot = sdkRoot, ClientRoot = clientRoot, ScanCompleted = scanCompleted, QuantumChecked = quantumChecked,
                DeclarationScanCompleted = declarations && sources && declaredDependencies && !inputsChanged,
                SourceScanCompleted = sourceScanCompleted, ActualSourceBindingsCompleted = actualSourceBindingsCompleted,
                Stages = stages, sourceGuard.ScannedSources,
                ModuleCount = catalog.Modules.Count, sourceGuard.SourceCount, sourceGuard.RoleNames, sourceGuard.SkillNames,
                catalog.VerifiedVendorSources, catalog.ProjectBinaryReferences,
                catalog.ActualCompilations, catalog.AutomaticCompilationCandidates, catalog.VerifiedOfficialMetadata,
                Violations = violations, Inputs = inputs, InputsChanged = inputsChanged, ExitCode = exitCode };
            File.WriteAllText(Path.GetFullPath(reportPath), JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        return exitCode;
    }

    static GuardInput[] ReadFinalInputs(DependencyCatalog catalog) =>
        catalog.Inputs.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
        {
            string? finalHash = File.Exists(item.Key) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.Key))) : null;
            return new GuardInput(item.Key, item.Value, finalHash, item.Value != finalHash);
        }).ToArray();

    sealed record GuardStage(string Name, bool Required, bool Completed, string Status, string Detail);
}
