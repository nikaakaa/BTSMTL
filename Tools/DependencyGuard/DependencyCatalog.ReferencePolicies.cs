using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace Btsmtl.DependencyGuard;

sealed partial class DependencyCatalog
{
    readonly Dictionary<string, UnityReferencePolicyFacts> referencePolicyFacts = new(StringComparer.OrdinalIgnoreCase);

    public AutomaticReferencePolicyProduction[] ProduceReferencePolicies(string requestPath, out string batchId)
    {
        var request = JsonSerializer.Deserialize<AutomaticReferencePolicyProductionRequest>(ReadText(requestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, RespectRequiredConstructorParameters = true,
                RespectNullableAnnotations = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ??
            throw new InvalidDataException("自动引用来源生成请求必须是完整对象。");
        if (request.Version != 1 || request.BatchId.Length == 0 || request.RulesSHA256 != RulesSHA256 ||
            !Path.GetFullPath(request.ClientRoot).Equals(clientRoot, StringComparison.OrdinalIgnoreCase) ||
            request.Compilations.Length == 0)
            throw new InvalidDataException("来源生成请求版本、批次、规则、目标 Client 或实际编译目标不匹配。");
        batchId = request.BatchId;
        LoadBoundaryFiles(request.Files);
        foreach (OfficialInstallation installation in request.Installations)
            LoadOfficialInstallation(installation);
        var result = new List<AutomaticReferencePolicyProduction>();
        foreach (AutomaticReferencePolicyProductionCompilation input in request.Compilations)
        {
            string definition = Path.GetFullPath(input.AssemblyDefinition);
            if (!byPath.TryGetValue(definition, out Module? owner) || owner.Kind != "asmdef")
                throw new InvalidDataException($"来源生成没有唯一实际 asmdef：{definition}。");
            if (!officialInstallations.TryGetValue(input.OriginId, out var origin) || origin.Rule.Kind != "UnityCompiler")
                throw new InvalidDataException($"来源生成没有已核定的安装编译器：{input.OriginId}。");
            CompilationExecutionReceipt receipt = ExecutionReceipt(input.Receipt, batchId);
            RequireReceiptInput(receipt, definition);
            RequireReceiptInput(receipt, origin.Installation.Producer);
            RequireReceiptInput(receipt, origin.Installation.VersionFile);
            UnityReferencePolicyFacts facts = ReadReferencePolicyFacts(input.ReferencePolicyFacts,
                input.PolicyCaptureSource, receipt, origin.Installation, batchId);
            var arguments = CSharpCommandLineParser.Default.Parse(
                ResponseArguments(input.ResponseFile, input.WorkingDirectory, receipt),
                input.WorkingDirectory, null, null);
            if (arguments.Errors.Any(error => error.Severity == DiagnosticSeverity.Error))
                throw new InvalidDataException($"来源生成的实际 rsp 未完整解析：{input.ResponseFile}；{string.Join("；", arguments.Errors)}。");
            string[] references = arguments.MetadataReferences.Select(reference =>
                ResolveActualReference(reference.Reference, input.WorkingDirectory, input.ResponseFile, arguments)).ToArray();
            foreach (string path in arguments.SourceFiles.Select(source => UnityPolicyPath(source.Path, input.WorkingDirectory))
                .Concat(references).Concat(arguments.AnalyzerReferences.Select(reference =>
                    ResolveActualReference(reference.FilePath, input.WorkingDirectory, input.ResponseFile, arguments)))
                .Concat(arguments.AdditionalFiles.Select(source => UnityPolicyPath(source.Path, input.WorkingDirectory)))
                .Concat(arguments.AnalyzerConfigPaths.Select(path => UnityPolicyPath(path, input.WorkingDirectory))))
            {
                ActualBytes(path);
                RequireReceiptInput(receipt, path);
            }
            var context = ReadAutomaticReferenceContext(facts, owner, input.WorkingDirectory,
                input.ResponseFile, arguments, receipt, out _);
            var claims = new List<AutomaticReferencePolicy>();
            foreach (string reference in references.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string? kind = AutomaticReferenceKinds(facts, context.Script, context.Target,
                    reference, input.WorkingDirectory).FirstOrDefault();
                if (kind is not null)
                    claims.Add(new AutomaticReferencePolicy(reference, kind));
            }
            result.Add(new AutomaticReferencePolicyProduction(definition, Path.GetFullPath(input.ResponseFile),
                references, claims.ToArray()));
        }
        return result.ToArray();
    }

    UnityReferencePolicyFacts ReadReferencePolicyFacts(string factsPath, string captureSource,
        CompilationExecutionReceipt receipt, OfficialInstallation installation, string batchId)
    {
        string path = Path.GetFullPath(factsPath);
        if (!receipt.Outputs.Keys.Any(output => Path.GetFullPath(output).Equals(path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"政策原始快照不是本批正式过程的实际输出：{path}。");
        RequireReceiptInput(receipt, captureSource);
        ActualBytes(captureSource);
        if (!boundaryFiles[Path.GetFullPath(captureSource)].SHA256.Equals(
                rules.UnityActualBoundary.PolicyCaptureSourceSHA256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"政策采集源码身份不匹配：{captureSource}。");
        if (referencePolicyFacts.TryGetValue(path, out UnityReferencePolicyFacts? prior))
        {
            foreach (UnitySelectedPackage package in prior.SelectedPackages)
                RequireReceiptInput(receipt, Path.Combine(Path.GetFullPath(package.ResolvedPath), "package.json"));
            return prior;
        }
        var facts = ActualJson<UnityReferencePolicyFacts>(path);
        if (facts.Version != 2 || facts.BatchId != batchId || facts.CapturePhase != "CompilationStarted" ||
            !Path.GetFullPath(facts.ProducerPath).Equals(Path.GetFullPath(installation.Producer), StringComparison.OrdinalIgnoreCase) ||
            facts.ProviderType != "UnityEditor.Scripting.ScriptCompilation.PrecompiledAssemblyProvider")
            throw new InvalidDataException($"政策快照不是本次原始官方编译上下文：{path}。");
        using var producer = new PEReader(new MemoryStream(ActualBytes(installation.Producer)));
        var metadata = producer.GetMetadataReader();
        if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D") != facts.ProducerModuleVersionId)
            throw new InvalidDataException($"政策快照的已加载生产者不是本批安装原件：{path}。");
        if (!Path.GetFullPath(facts.Settings.ProjectDirectory).Equals(clientRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"政策快照目标项目不匹配：{path}。");
        ReadSelectedPackages(facts, receipt);
        referencePolicyFacts.Add(path, facts);
        return facts;
    }

    (UnityReferencePolicyScript Script, UnityReferencePolicyTarget Target) ReadAutomaticReferenceContext(
        UnityReferencePolicyFacts facts, Module owner, string workingDirectory, string responseFile,
        CSharpCommandLineArguments arguments, CompilationExecutionReceipt receipt,
        out HashSet<string> declaredReferences)
    {
        UnityReferencePolicyScript[] scripts = facts.ScriptAssemblies.Where(script =>
            script.AsmDefPath is { Length: > 0 } && UnityPolicyPath(script.AsmDefPath, facts.Settings.ProjectDirectory)
                .Equals(owner.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
        UnityReferencePolicyTarget[] targets = facts.TargetAssemblies.Where(target =>
            target.AsmDefPath.Length != 0 && UnityPolicyPath(target.AsmDefPath, facts.Settings.ProjectDirectory)
                .Equals(owner.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (scripts.Length != 1 || targets.Length != 1 || scripts[0].Filename != targets[0].Filename ||
            scripts[0].Filename != owner.Name + ".dll" || scripts[0].Flags != targets[0].Flags)
            throw new InvalidDataException($"政策原件没有本次目标的唯一 ScriptAssembly/TargetAssembly：{owner.Path}。");
        UnityReferencePolicyScript script = scripts[0];
        UnityReferencePolicyTarget target = targets[0];
        IEnumerable<string> authorArguments = (script.ResponseFiles ?? []).SelectMany(response =>
                ResponseArguments(UnityPolicyPath(response, workingDirectory), workingDirectory, receipt))
            .Concat(ExpandResponseArguments(script.AdditionalCompilerArguments ?? [], workingDirectory, receipt));
        var authorOptions = CSharpCommandLineParser.Default.Parse(authorArguments, workingDirectory, null, null);
        if (authorOptions.Errors.Any(error => error.Severity == DiagnosticSeverity.Error))
            throw new InvalidDataException($"原始作者响应文件/编译参数未完整解析：{responseFile}。");
        declaredReferences = PathSet(authorOptions.MetadataReferences.Select(reference =>
            ResolveActualReference(reference.Reference, workingDirectory, responseFile, arguments)));
        return (script, target);
    }

    HashSet<string> ReadAutomaticReferencePolicies(UnityCompilerTrace trace, CompilationExecutionReceipt receipt,
        UnityCompilationProof proof, UnityReferencePolicyFacts facts, Module owner,
        CSharpCommandLineArguments arguments, string[] references, out HashSet<string> declaredReferences)
    {
        var context = ReadAutomaticReferenceContext(facts, owner, proof.WorkingDirectory,
            proof.ResponseFile, arguments, receipt, out declaredReferences);
        var automatic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AutomaticReferencePolicy claim in trace.AutomaticReferencePolicies)
        {
            string reference = UnityPolicyPath(claim.Reference, proof.WorkingDirectory);
            if (!references.Contains(reference, StringComparer.OrdinalIgnoreCase) || !automatic.Add(reference))
                throw new InvalidDataException($"自动政策候选不在完整实际引用中或重复：{reference}。");
            if (!AutomaticReferenceKinds(facts, context.Script, context.Target, reference, proof.WorkingDirectory)
                .Contains(claim.Kind, StringComparer.Ordinal))
                throw new InvalidDataException($"本批原始政策事实不能证明候选的自动来源：{claim.Kind}；{reference}。");
        }
        return automatic;
    }

    IEnumerable<string> AutomaticReferenceKinds(UnityReferencePolicyFacts facts, UnityReferencePolicyScript script,
        UnityReferencePolicyTarget target, string reference, string workingDirectory)
    {
        ActualBinary binary = ReadActualBinary(reference);
        string filename = binary.Name + ".dll";
        bool rawReference = script.References.Any(path => UnityPolicyPath(path, workingDirectory).Equals(reference, StringComparison.OrdinalIgnoreCase)) ||
            script.ScriptAssemblyReferences.Contains(filename, StringComparer.Ordinal);
        if (!rawReference)
            yield break;
        bool codegen = Path.GetFileNameWithoutExtension(target.Filename).StartsWith(facts.CodeGen.Prefix, StringComparison.OrdinalIgnoreCase) &&
            (Path.GetFileNameWithoutExtension(target.Filename).EndsWith(facts.CodeGen.CompilerSuffix, StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileNameWithoutExtension(target.Filename).EndsWith(facts.CodeGen.CodeGenSuffix, StringComparison.OrdinalIgnoreCase));
        bool defaultEngine = (target.Flags & 512) == 0;
        bool explicitPlugins = (target.Flags & 128) != 0;
        bool editorTests = (target.Flags & 1) != 0 || facts.TestRunner.PlayModeTestRunnerEnabled;
        string[] testNames = [facts.TestRunner.EditorTestRunnerAssemblyName, facts.TestRunner.EngineTestRunnerAssemblyName];
        if (CachedPrecompiled(facts, reference) is UnityReferencePolicyBinary cached &&
            !cached.Redirected && (explicitPlugins ? defaultEngine && (cached.Flags & 32) == 0 :
                (cached.Flags & 64) == 0 && (defaultEngine || (cached.Flags & 32) != 0)))
            yield return "DefaultPrecompiled";
        if (defaultEngine && facts.UnityAssemblies is not null && facts.UnityAssemblies.Any(item =>
            !item.Redirected && (item.Flags & 2048) == 0 && UnityPolicyPath(item.Path, workingDirectory).Equals(reference, StringComparison.OrdinalIgnoreCase)))
            yield return "DefaultEngine";
        if (defaultEngine && !codegen &&
            !facts.AutoReferencedPackages.IgnoreAssemblies.Contains(target.Filename, StringComparer.Ordinal) &&
            (facts.Settings.BuildingForEditor ? facts.AutoReferencedPackages.EditorAssemblyNames : facts.AutoReferencedPackages.RuntimeAssemblyNames)
                .Contains(filename, StringComparer.Ordinal) && target.References.Contains(filename, StringComparer.Ordinal))
            yield return "AutoReferencedPackage";
        if (!codegen && editorTests && !testNames.Contains(target.Filename, StringComparer.Ordinal) &&
            testNames.Contains(filename, StringComparer.Ordinal) && target.References.Contains(filename, StringComparer.Ordinal))
            yield return "TestRunner";
        if (editorTests && filename == facts.TestRunner.NunitAssemblyName &&
            (!explicitPlugins || !target.ExplicitPrecompiledReferences.Contains(filename, StringComparer.Ordinal)) &&
            CachedPrecompiled(facts, reference) is UnityReferencePolicyBinary nunit &&
            nunit.Key == facts.TestRunner.NunitAssemblyName && !nunit.Redirected)
            yield return "NUnit";
    }

    UnityReferencePolicyBinary? CachedPrecompiled(UnityReferencePolicyFacts facts, string reference)
    {
        if (!facts.Settings.BuildingForEditor || facts.Settings.ExtraGeneralDefines is { Length: > 0 } ||
            facts.EditorPrecompiledAssemblies is null)
            return null;
        return facts.EditorPrecompiledAssemblies.FirstOrDefault(binary =>
            UnityPolicyPath(binary.Path, facts.Settings.ProjectDirectory).Equals(reference, StringComparison.OrdinalIgnoreCase));
    }

    string UnityPolicyPath(string path, string projectDirectory)
    {
        string normalized = path.Replace('\\', '/');
        string projectPackages = Path.TrimEndingDirectorySeparator(clientRoot!).Replace('\\', '/') + "/Packages/";
        if (normalized.StartsWith(projectPackages, StringComparison.OrdinalIgnoreCase))
            normalized = "Packages/" + normalized[projectPackages.Length..];
        if (normalized.StartsWith("Packages/", StringComparison.Ordinal))
        {
            int separator = normalized.IndexOf('/', "Packages/".Length);
            string package = separator < 0 ? normalized["Packages/".Length..] : normalized["Packages/".Length..separator];
            if (!selectedPackages.TryGetValue(package, out UnitySelectedPackage? selected))
                throw new InvalidDataException($"本批官方选包快照缺少虚拟路径的实际根与版本身份：{path}。");
            return separator < 0 ? Path.GetFullPath(selected.ResolvedPath) : FullPath(selected.ResolvedPath, normalized[(separator + 1)..]);
        }
        return FullPath(projectDirectory, path);
    }
}
