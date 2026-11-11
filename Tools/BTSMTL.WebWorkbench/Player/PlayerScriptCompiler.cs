using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

public sealed class PlayerScriptCompiler(string project, string temporary)
{
    internal const string Mode = "unity-player-offline";
    static readonly string[] s_Tools = { "runtime", "roslyn", "ilPostProcessorTrigger", "ilPostProcessorRunner" };
    static readonly int s_Parallelism = Math.Clamp(Environment.ProcessorCount / 4, 1, 8);

    public async Task<PlayerScriptCompilation> Compile(Guid requestId, string parentManifestPath,
        string outputDirectory, JsonObject recipe, IReadOnlyDictionary<string, IReadOnlyList<string>> sources)
    {
        byte[] parentBytes = File.ReadAllBytes(parentManifestPath);
        JsonObject parent = JsonNode.Parse(parentBytes)!.AsObject();
        string parentHash = PlayerCodeUpdates.Sha256(parentBytes);
        string directory = Path.Combine(outputDirectory, requestId.ToString("N"));
        string manifestPath = Path.Combine(directory, "player-script-compilation.json");
        if (File.Exists(manifestPath))
            return ReadExisting(manifestPath, requestId, (string)parent["buildId"]!, parentHash);
        Directory.CreateDirectory(directory);
        var diagnostics = new JsonArray();
        var executed = new JsonArray();
        var manifest = new JsonObject
        {
            ["format"] = "q3-preview-player-script-compilation/1", ["compilerMode"] = Mode,
            ["requestId"] = requestId.ToString("N"), ["parentBuildId"] = (string)parent["buildId"]!,
            ["baseBuildId"] = (string)parent["baseBuildId"]!, ["parentManifestSha256"] = parentHash,
            ["projectDirectory"] = Path.GetFullPath(project), ["unityVersion"] = (string)parent["unityVersion"]!,
            ["startedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"), ["diagnostics"] = diagnostics
        };
        var outputs = new List<PlayerCompiledAssembly>();
        UnityPlayerAssemblyInput[] assemblyInputs = Array.Empty<UnityPlayerAssemblyInput>();
        PlayerCompilationReuse? reuse = null;
        string stage = "recipe";
        try
        {
            foreach (string field in new[] { "compilerInputs", "compilerDag", "compiler", "compilationFiles", "unityVersion" })
                if (recipe[field] == null)
                    diagnostics.Add(new JsonObject { ["stage"] = stage, ["message"] = $"原 Player 基包未封存 '{field}'；需要用完整配方生产者重打基包，不能从当前 Editor 补取。" });
            if (diagnostics.Count > 0)
                return Finish(false);
            if ((string)recipe["format"]! != "q3-preview-player-code-recipe/1" ||
                !string.Equals(Path.GetFullPath((string)recipe["projectDirectory"]!), Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase) ||
                (string)recipe["unityVersion"]! != (string)parent["unityVersion"]!)
                throw new InvalidDataException("Player 配方与目标项目、基包 Unity 版本或正式格式不符。");
            string lockPath = Path.Combine(project, "Packages", "packages-lock.json");
            manifest["packagesLock"] = JsonSerializer.SerializeToNode(Fact(lockPath), WorkbenchArtifacts.JsonOptions);
            if ((string)manifest["packagesLock"]!["sha256"]! != (string)recipe["packagesLockSha256"]!)
                throw new InvalidDataException("Player 基包的 Unity 包锁已变化，需要整包重建。");
            var facts = new List<PlayerCompilationFile>
            {
                recipe["compilerInputs"]!.Deserialize<PlayerCompilationFile>(WorkbenchArtifacts.JsonOptions)!,
                recipe["compilerDag"]!.Deserialize<PlayerCompilationFile>(WorkbenchArtifacts.JsonOptions)!
            };
            facts.AddRange(recipe["compilationFiles"]!.Deserialize<PlayerCompilationFile[]>(WorkbenchArtifacts.JsonOptions)!);
            facts.AddRange(s_Tools.Select(name => recipe["compiler"]![name]!.Deserialize<PlayerCompilationFile>(WorkbenchArtifacts.JsonOptions)!));
            foreach (PlayerCompilationFile file in facts.DistinctBy(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(file.Path))
                    diagnostics.Add(new JsonObject { ["stage"] = stage, ["message"] = $"原 Player 必需编译输入缺失：{file.Path}" });
                else if (Fact(file.Path).Sha256 != file.Sha256)
                    diagnostics.Add(new JsonObject { ["stage"] = stage, ["message"] = $"原 Player 编译输入与封存摘要不符：{file.Path}" });
            }
            if (diagnostics.Count > 0)
                return Finish(false);
            string work = Path.Combine(directory, "work");
            string logs = Path.Combine(directory, "logs");
            Directory.CreateDirectory(work);
            Directory.CreateDirectory(logs);
            using var inputs = new PlayerCompilationInputs(Path.GetFullPath(project), work, recipe, sources);
            JsonObject compilation = inputs.Data[PlayerCompilationInputs.DataKey]!.AsObject();
            if ((string)compilation["BuildTarget"]! != "StandaloneWindows64" || (bool)compilation["Debug"]! ||
                (string)parent["scriptingBackend"]! != "Mono2x" ||
                inputs.Assemblies.Any(assembly => assembly.Defines.Contains("UNITY_EDITOR", StringComparer.Ordinal)))
                throw new InvalidDataException("封存输入不是本次 Mono StandaloneWindows64 Player 的正式编译条件。");
            assemblyInputs = inputs.Assemblies;
            foreach (JsonNode? assembly in recipe["assemblies"]!.AsArray())
                if (assembly["compilerInputIndex"] == null ||
                    (string)assembly["name"]! != assemblyInputs[(int)assembly["compilerInputIndex"]!].Name)
                    throw new InvalidDataException($"原 Player 程序集 '{assembly["name"]}' 缺少一致的编译输入身份。");
            manifest["baseCompilerInputs"] = recipe["compilerInputs"]!.DeepClone();
            manifest["baseCompilerDag"] = recipe["compilerDag"]!.DeepClone();
            manifest["compiler"] = recipe["compiler"]!.DeepClone();
            compilation["OutputDirectory"] = Path.GetDirectoryName(inputs.Output(inputs.Ilpp(0), ".dll"));
            string usedInputs = Path.Combine(directory, "compiler-inputs.json");
            File.WriteAllText(usedInputs, inputs.Data.ToJsonString(), new UTF8Encoding(false));
            manifest["compilerInputs"] = JsonSerializer.SerializeToNode(Fact(usedInputs), WorkbenchArtifacts.JsonOptions);
            manifest["immutableAssemblies"] = new JsonArray(inputs.ImmutableAssemblies.Values.Select(assembly => assembly.DeepClone()).ToArray());
            var commands = new PlayerCompilationProcesses(work, temporary, logs);
            var cache = new PlayerCompilationReuse(outputDirectory, directory, recipe, s_Tools);
            reuse = cache;
            using var compilers = new SemaphoreSlim(s_Parallelism);
            bool failed = false;
            var results = new Task<bool>?[assemblyInputs.Length];
            // 依赖任务都在首个 await 之前同步创建，results 只在请求线程写入。
            Task<bool> Build(int index) => results[index] ??= BuildAssembly(index);
            async Task<bool> BuildAssembly(int index)
            {
                if (inputs.ImmutableAssemblies.ContainsKey(index))
                    return true;
                Task<bool>[] dependencies = assemblyInputs[index].References.Select(Build).ToArray();
                if ((await Task.WhenAll(dependencies)).Contains(false))
                    return false;
                JsonObject node = inputs.Csc(index);
                string name = "csc-" + assemblyInputs[index].Name;
                string[] nodeOutputs = inputs.Outputs(node);
                string key = cache.Key("csc", node, inputs.CscKeyText(index), inputs.CscInputFiles(index));
                if (cache.TryReuse(name, key, nodeOutputs))
                    return true;
                await compilers.WaitAsync();
                try
                {
                    if (failed)
                        return false;
                    if (!await Execute(node, name, (string)recipe["compiler"]!["runtime"]!["path"]!, inputs.CscArguments(index), project))
                    {
                        failed = true;
                        return false;
                    }
                }
                finally { compilers.Release(); }
                cache.Record(name, key, nodeOutputs);
                return true;
            }
            async Task<bool> Execute(JsonObject node, string name, string executable, string[] arguments, string workingDirectory)
            {
                int exit = await commands.Run(name, executable, arguments, PlayerCompilationInputs.Environment(node), workingDirectory);
                string log = Path.Combine(logs, name + ".log");
                lock (executed)
                {
                    executed.Add(new JsonObject
                    {
                        ["annotation"] = (string)node["Annotation"]!, ["executable"] = executable,
                        ["arguments"] = JsonSerializer.SerializeToNode(arguments), ["workingDirectory"] = workingDirectory,
                        ["environment"] = node["Env"]!.DeepClone(), ["exitCode"] = exit,
                        ["log"] = JsonSerializer.SerializeToNode(Fact(log), WorkbenchArtifacts.JsonOptions)
                    });
                    if (exit == 0)
                        return true;
                    diagnostics.Add(new JsonObject { ["stage"] = stage, ["assembly"] = name,
                        ["message"] = $"原 Unity 工具退出代码 {exit}。", ["log"] = log,
                        ["output"] = File.ReadAllText(log, Encoding.UTF8) });
                    return false;
                }
            }
            stage = "csc";
            if ((await Task.WhenAll(Enumerable.Range(0, assemblyInputs.Length).Select(Build).ToArray())).Contains(false))
                return Finish(false);
            stage = "ilpp";
            (string[] configurationText, List<string> configurationFiles) = inputs.IlppKeyInputs(inputs.Configuration);
            var pending = new List<(int Index, string Key, string[] Outputs)>();
            for (int i = 0; i < assemblyInputs.Length; i++)
            {
                JsonObject node = inputs.Ilpp(i);
                (string[] text, List<string> files) = inputs.IlppKeyInputs(node);
                string key = cache.Key("ilpp", node, configurationText.Concat(text), configurationFiles.Concat(files));
                string[] nodeOutputs = inputs.Outputs(node);
                if (!cache.TryReuse("ilpp-" + assemblyInputs[i].Name, key, nodeOutputs))
                    pending.Add((i, key, nodeOutputs));
            }
            if (pending.Count > 0)
                await using (PlayerCompilationProcesses.IlppRunner runner = commands.StartIlpp(
                    (string)recipe["compiler"]!["ilPostProcessorRunner"]!["path"]!, requestId))
                {
                    if (!await runner.WaitForReady())
                    {
                        diagnostics.Add(new JsonObject { ["stage"] = stage, ["message"] = "原 Unity ILPP Runner 在命名管道就绪前退出。",
                            ["log"] = Path.Combine(logs, "ilpp-runner.log") });
                        return Finish(false);
                    }
                    string trigger = (string)recipe["compiler"]!["ilPostProcessorTrigger"]!["path"]!;
                    if (!await Execute(inputs.Configuration, "ilpp-configuration", trigger,
                            inputs.IlppArguments(inputs.Configuration, runner.PipeName), work))
                        return Finish(false);
                    foreach ((int index, string key, string[] nodeOutputs) in pending)
                    {
                        string name = "ilpp-" + assemblyInputs[index].Name;
                        if (!await Execute(inputs.Ilpp(index), name, trigger,
                                inputs.IlppArguments(inputs.Ilpp(index), runner.PipeName), work))
                            return Finish(false);
                        cache.Record(name, key, nodeOutputs);
                    }
                }
            for (int i = 0; i < assemblyInputs.Length; i++)
                outputs.Add(new PlayerCompiledAssembly(assemblyInputs[i].Name, i,
                    cache.Fact(inputs.Output(inputs.Ilpp(i), ".dll")), cache.Fact(inputs.Output(inputs.Ilpp(i), ".pdb"))));
            manifest["sources"] = JsonSerializer.SerializeToNode(inputs.SourceFiles.Select(cache.Fact).ToArray(), WorkbenchArtifacts.JsonOptions);
            manifest["assemblies"] = JsonSerializer.SerializeToNode(outputs, WorkbenchArtifacts.JsonOptions);
            return Finish(true);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException or Win32Exception or JsonException)
        {
            diagnostics.Add(new JsonObject { ["stage"] = stage, ["message"] = failure.Message });
            return Finish(false);
        }

        PlayerScriptCompilation Finish(bool success)
        {
            string execution = Path.Combine(directory, "execution.json");
            File.WriteAllText(execution, new JsonObject { ["nodes"] = executed }.ToJsonString(), new UTF8Encoding(false));
            manifest["compilerDag"] = JsonSerializer.SerializeToNode(Fact(execution), WorkbenchArtifacts.JsonOptions);
            string responses = Path.Combine(directory, "work", "responses");
            if (Directory.Exists(responses))
                manifest["responseFiles"] = JsonSerializer.SerializeToNode(Directory.EnumerateFiles(responses)
                    .Select(Fact).ToArray(), WorkbenchArtifacts.JsonOptions);
            if (reuse != null)
            {
                reuse.Write(directory);
                manifest["reusedNodes"] = reuse.Reused();
            }
            manifest["state"] = success ? "compiled" : "failed";
            manifest["completedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
            byte[] bytes = Encoding.UTF8.GetBytes(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllBytes(manifestPath, bytes);
            var evidence = new PlayerCompilationEvidence(manifestPath, PlayerCodeUpdates.Sha256(bytes), manifest);
            return success ? new CompiledPlayerScripts(evidence, outputs.ToArray(), assemblyInputs) : new FailedPlayerScripts(evidence);
        }
    }

    PlayerScriptCompilation ReadExisting(string path, Guid requestId, string parentId, string parentHash)
    {
        byte[] bytes = File.ReadAllBytes(path);
        JsonObject manifest = JsonNode.Parse(bytes)!.AsObject();
        var identity = manifest.Deserialize<PlayerCompilationIdentity>(WorkbenchArtifacts.JsonOptions)!;
        if (identity.Format != "q3-preview-player-script-compilation/1" || (string?)manifest["compilerMode"] != Mode ||
            Guid.Parse(identity.RequestId) != requestId || identity.ParentBuildId != parentId || identity.ParentManifestSha256 != parentHash ||
            !string.Equals(Path.GetFullPath(identity.ProjectDirectory), Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("此请求的已有回执不属于本次离线 Player 编译；不得复用旧 Editor 编译回执。");
        var evidence = new PlayerCompilationEvidence(path, PlayerCodeUpdates.Sha256(bytes), manifest);
        if ((string)manifest["state"]! == "failed")
            return new FailedPlayerScripts(evidence);
        var assemblies = manifest["assemblies"]!.Deserialize<PlayerCompiledAssembly[]>(WorkbenchArtifacts.JsonOptions)!;
        foreach (PlayerCompilationFile file in assemblies.SelectMany(assembly => new[] { assembly.Dll, assembly.Pdb }))
            if (Fact(file.Path).Sha256 != file.Sha256)
                throw new InvalidDataException($"已有离线编译产物 '{file.Path}' 与请求回执不符。");
        var inputFile = manifest["compilerInputs"]!.Deserialize<PlayerCompilationFile>(WorkbenchArtifacts.JsonOptions)!;
        byte[] inputBytes = File.ReadAllBytes(inputFile.Path);
        if (PlayerCodeUpdates.Sha256(inputBytes) != inputFile.Sha256)
            throw new InvalidDataException($"已有离线编译输入 '{inputFile.Path}' 与请求回执不符。");
        JsonNode inputs = JsonNode.Parse(inputBytes)![PlayerCompilationInputs.DataKey]!;
        return new CompiledPlayerScripts(evidence, assemblies,
            inputs["Assemblies"]!.Deserialize<UnityPlayerAssemblyInput[]>(WorkbenchArtifacts.JsonOptions)!);
    }

    internal static PlayerCompilationFile Fact(string path)
    {
        using FileStream file = File.OpenRead(path);
        return new PlayerCompilationFile(Path.GetFullPath(path), Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant());
    }
}

sealed record PlayerCompilationIdentity([property: JsonRequired] string Format,
    [property: JsonRequired] string RequestId, [property: JsonRequired] string ParentBuildId,
    [property: JsonRequired] string ParentManifestSha256, [property: JsonRequired] string ProjectDirectory);

public sealed record PlayerCompilationFile([property: JsonRequired] string Path, [property: JsonRequired] string Sha256);
public sealed record PlayerCompiledAssembly([property: JsonRequired] string Name,
    [property: JsonRequired] int CompilerInputIndex, [property: JsonRequired] PlayerCompilationFile Dll,
    [property: JsonRequired] PlayerCompilationFile Pdb);
public sealed record UnityPlayerAssemblyInput([property: JsonRequired] string Name,
    [property: JsonRequired] string[] SourceFiles, [property: JsonRequired] string[] Defines,
    [property: JsonRequired] int[] References);
public sealed record PlayerCompilationEvidence(string Path, string Sha256, JsonObject Manifest);
public abstract record PlayerScriptCompilation(PlayerCompilationEvidence Evidence);
public sealed record FailedPlayerScripts(PlayerCompilationEvidence Evidence) : PlayerScriptCompilation(Evidence);
public sealed record CompiledPlayerScripts(PlayerCompilationEvidence Evidence, PlayerCompiledAssembly[] Assemblies,
    UnityPlayerAssemblyInput[] Inputs) : PlayerScriptCompilation(Evidence);
