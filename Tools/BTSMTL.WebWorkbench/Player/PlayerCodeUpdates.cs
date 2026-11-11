using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BTSMTL.WebWorkbench;

public sealed class PlayerCodeUpdates
{
    const string ManifestFileName = "preview-player.json";
    readonly string m_Players;
    readonly PlayerScriptCompiler m_Compiler;
    readonly PlayerBurstCompiler m_Burst;
    static readonly JsonSerializerOptions s_Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly SemaphoreSlim m_Gate = new(1);

    public PlayerCodeUpdates(string players, PlayerScriptCompiler compiler, PlayerBurstCompiler burst)
    {
        m_Players = Path.TrimEndingDirectorySeparator(Path.GetFullPath(players));
        m_Compiler = compiler;
        m_Burst = burst;
    }

    public async Task<object> Update(UpdatePlayerCodeRequest request)
    {
        if (request.RequestId == Guid.Empty)
            throw new ArgumentException("代码更新需要非空 requestId，它也是新播放器版本身份。");
        await m_Gate.WaitAsync();
        try
        {
            string versionId = request.RequestId.ToString("N");
            string target = Path.Combine(m_Players, versionId);
            if (File.Exists(Path.Combine(target, ManifestFileName)))
            {
                JsonObject existing = ReadManifest(versionId);
                if ((string?)existing["parentBuildId"] != request.PlayerBuildId)
                    throw new InvalidOperationException("同一请求身份已用于更新另一个播放器版本。");
                if ((string?)existing["codeUpdate"]?["compiler"] != PlayerScriptCompiler.Mode)
                    return new { State = "rejected", BuildId = request.PlayerBuildId, ParentBuildId = (string?)null,
                        Refusals = new[] { "此请求身份已用于历史编译版本；请用新的 requestId 执行正式离线 Player 更新，不能把旧 Editor 产物当成本次编译。" } };
                return new { State = "created", BuildId = versionId, ParentBuildId = request.PlayerBuildId, Update = existing["codeUpdate"] };
            }
            return await Run(request.PlayerBuildId, versionId, target);
        }
        finally { m_Gate.Release(); }
    }

    async Task<object> Run(string sourceId, string versionId, string target)
    {
        var watch = Stopwatch.StartNew();
        JsonObject source = ReadManifest(sourceId);
        string baseId = (string?)source["baseBuildId"]
            ?? throw new InvalidOperationException($"播放器构建 '{sourceId}' 没有编译配方，需按当前构建入口整包构建一次。");
        string sourceDirectory = Path.Combine(m_Players, sourceId);
        string managed = Path.Combine(sourceDirectory,
            Path.GetFileNameWithoutExtension((string)source["executable"]!) + "_Data", "Managed");
        string recipeDirectory = Path.Combine(m_Players, baseId, "recipe");
        JsonObject recipe = JsonNode.Parse(File.ReadAllBytes(Path.Combine(recipeDirectory, "recipe.json")))!.AsObject();
        var refusals = new List<string>();
        Dictionary<string, RecipeAssembly> assemblies = recipe["assemblies"]!.AsArray()
            .Select(node => RecipeAssembly.Read(node!.AsObject(), recipeDirectory))
            .ToDictionary(assembly => assembly.Name, StringComparer.Ordinal);
        var sourceChanges = new List<object>();
        foreach (RecipeAssembly assembly in assemblies.Values)
            foreach (string problem in assembly.DefinitionProblems())
                refusals.Add(problem);
        if (refusals.Count > 0)
            return Rejected(sourceId, refusals, sourceChanges, watch);

        string project = (string)recipe["projectDirectory"]!;
        var sources = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (RecipeAssembly assembly in assemblies.Values)
        {
            IReadOnlyList<string> files = assembly.CurrentSources(project);
            sources.Add(assembly.Name, files);
            string[] added = files.Except(assembly.RecordedSources, StringComparer.Ordinal).ToArray();
            string[] removed = assembly.RecordedSources.Except(files, StringComparer.Ordinal).ToArray();
            if (added.Length > 0 || removed.Length > 0)
                sourceChanges.Add(new { assembly.Name, Added = added, Removed = removed });
        }
        PlayerScriptCompilation compilation = await m_Compiler.Compile(Guid.Parse(versionId),
            Path.Combine(sourceDirectory, ManifestFileName), Path.Combine(m_Players, "CodeCompilations"), recipe, sources);
        long compileMilliseconds = watch.ElapsedMilliseconds;
        PlayerCompilationEvidence evidence = compilation.Evidence;
        if (compilation is FailedPlayerScripts)
        {
            refusals.Add("按原配方进行的离线 Player 编译或 IL 后处理失败，实际 diagnostics 保存在本次正式编译回执。");
            return Rejected(sourceId, refusals, sourceChanges, watch, evidence);
        }
        var compiled = (CompiledPlayerScripts)compilation;
        Dictionary<string, PlayerCompiledAssembly> outputs = compiled.Assemblies.ToDictionary(assembly => assembly.Name, StringComparer.Ordinal);
        var changed = new List<(RecipeAssembly Recipe, PlayerCompiledAssembly Output, UnityPlayerAssemblyInput Input)>();
        foreach (RecipeAssembly assembly in assemblies.Values)
        {
            if (!outputs.TryGetValue(assembly.Name, out PlayerCompiledAssembly? output))
            {
                refusals.Add($"原程序集 '{assembly.Name}' 已不在本次正式 Player 编译中，需要整包重建。");
                continue;
            }
            UnityPlayerAssemblyInput input = compiled.Inputs[output.CompilerInputIndex];
            if (!input.Defines.ToHashSet(StringComparer.Ordinal).SetEquals(assembly.Defines))
                refusals.Add($"{assembly.Name} 的实际 Player 编译宏已变化，需要整包重建。");
            if (NormalizedHash(output.Dll.Path) == NormalizedHash(Path.Combine(managed, assembly.Name + ".dll")))
                continue;
            if (!assembly.Replaceable)
                refusals.Add($"{assembly.Name} 已变化，但原构建配方未授权替换（{assembly.Reason}），需要按完整构建配方重打基包。");
            else
                changed.Add((assembly, output, input));
        }
        foreach (PlayerCompiledAssembly output in compiled.Assemblies.Where(output => !assemblies.ContainsKey(output.Name)))
        {
            string current = Path.Combine(managed, output.Name + ".dll");
            if (!File.Exists(current))
                refusals.Add($"本次 Player 编译新增程序集 '{output.Name}'，需要整包重建。");
            else if (NormalizedHash(current) != NormalizedHash(output.Dll.Path))
                refusals.Add($"非项目依赖程序集 '{output.Name}' 已变化，需要整包重建。");
        }
        if (refusals.Count == 0 && changed.Count > 0)
        {
            var current = assemblies.Keys.ToDictionary(name => name, name => Path.Combine(managed, name + ".dll"), StringComparer.Ordinal);
            var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
            foreach (var assembly in changed)
                next[assembly.Recipe.Name] = assembly.Output.Dll.Path;
            string[] roots = recipe["serializedTypes"]!.AsArray().Select(node => (string)node!).ToArray();
            refusals.AddRange(PlayerSerializedLayout.Compare(current, next, roots));
        }
        if (refusals.Count > 0)
            return Rejected(sourceId, refusals, sourceChanges, watch, evidence);
        // 基包没有 Burst 原生库（Unity AOT 已关），首个代码版本即使 DLL 未变也补编；之后每个新版本都按新 DLL 重编。
        string plugins = Path.Combine(Path.GetDirectoryName(managed)!, PlayerBurstCompiler.PluginDirectory);
        bool burst = m_Burst.Enabled;
        if (changed.Count == 0 && (!burst || File.Exists(Path.Combine(plugins, PlayerBurstCompiler.LibraryName + ".dll"))))
            return new
            {
                State = "unchanged", BuildId = sourceId, ParentBuildId = (string?)null, Changed = Array.Empty<string>(),
                SourceChanges = sourceChanges, Compilation = new { evidence.Path, evidence.Sha256 },
                CompileMilliseconds = compileMilliseconds, TotalMilliseconds = watch.ElapsedMilliseconds
            };

        string staging = target + ".partial";
        if (Directory.Exists(staging))
            Directory.Delete(staging, true);
        LinkTree(sourceDirectory, staging, Path.Combine(sourceDirectory, "recipe"));
        string stagedManaged = Path.Combine(staging, Path.GetRelativePath(sourceDirectory, managed));
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var assembly in changed)
        {
            string destination = Path.Combine(stagedManaged, assembly.Recipe.Name + ".dll");
            File.Delete(destination);
            File.Copy(assembly.Output.Dll.Path, destination);
            string pdb = Path.ChangeExtension(destination, ".pdb");
            if (File.Exists(pdb))
                File.Delete(pdb);
            File.Copy(assembly.Output.Pdb.Path, pdb);
            hashes[assembly.Recipe.Name] = assembly.Output.Dll.Sha256;
        }
        PlayerBurstLibrary? library = null;
        if (burst)
        {
            string stagedPlugins = Path.Combine(staging, Path.GetRelativePath(sourceDirectory, plugins));
            Directory.CreateDirectory(stagedPlugins);
            string destination = Path.Combine(stagedPlugins, PlayerBurstCompiler.LibraryName + ".dll");
            if (File.Exists(destination))
                File.Delete(destination);
            string compilations = Path.Combine(m_Players, "CodeCompilations");
            try
            {
                library = await m_Burst.Compile(stagedManaged, compiled.Inputs, Path.Combine(compilations, versionId, "burst"),
                    Path.Combine(compilations, "burst-cache"));
            }
            catch (InvalidOperationException failure)
            {
                Directory.Delete(staging, true);
                refusals.Add(failure.Message);
                return Rejected(sourceId, refusals, sourceChanges, watch, evidence);
            }
            File.Copy(library.Library.Path, destination);
        }
        var manifest = (JsonObject)source.DeepClone();
        manifest["buildId"] = versionId;
        manifest["parentBuildId"] = sourceId;
        manifest["builtAt"] = DateTimeOffset.UtcNow.ToString("O");
        foreach (JsonNode? entry in manifest["assemblies"]!.AsArray())
            if (hashes.TryGetValue((string)entry!["name"]!, out string? hash))
                entry["sha256"] = hash;
        manifest["codeUpdate"] = JsonSerializer.SerializeToNode(new
        {
            Compiler = PlayerScriptCompiler.Mode, Compilation = new { evidence.Path, evidence.Sha256 },
            Changed = changed.Select(assembly => new
            {
                Name = assembly.Recipe.Name, assembly.Output.Dll.Sha256, PdbSha256 = assembly.Output.Pdb.Sha256,
                SourceCount = assembly.Input.SourceFiles.Length, assembly.Output.CompilerInputIndex
            }).ToArray(), SourceChanges = sourceChanges, CompileMilliseconds = compileMilliseconds,
            Burst = library == null ? null : new
            {
                Compiler = library.Compiler, Library = library.Library.Sha256, Response = library.Response,
                library.Log, library.Milliseconds
            }
        }, s_Json);
        File.WriteAllText(Path.Combine(staging, ManifestFileName), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
        Directory.Move(staging, target);
        return new
        {
            State = "created", BuildId = versionId, ParentBuildId = sourceId,
            Changed = changed.Select(assembly => assembly.Recipe.Name).ToArray(), SourceChanges = sourceChanges,
            Compilation = new { evidence.Path, evidence.Sha256 },
            CompileMilliseconds = compileMilliseconds, TotalMilliseconds = watch.ElapsedMilliseconds
        };
    }

    static object Rejected(string sourceId, List<string> refusals, List<object> sourceChanges, Stopwatch watch,
        PlayerCompilationEvidence? compilation = null) => new
    {
        State = "rejected", BuildId = sourceId, ParentBuildId = (string?)null, Refusals = refusals,
        SourceChanges = sourceChanges, Compilation = compilation == null ? null : new
        {
            compilation.Path, compilation.Sha256, Diagnostics = compilation.Manifest["diagnostics"]
        }, TotalMilliseconds = watch.ElapsedMilliseconds
    };

    JsonObject ReadManifest(string buildId)
    {
        if (string.IsNullOrWhiteSpace(buildId) || buildId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("需要明确的预览播放器构建身份。");
        string path = Path.Combine(m_Players, buildId, ManifestFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"没有预览播放器构建 '{buildId}'。");
        JsonObject manifest = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        return (string?)manifest["buildId"] == buildId ? manifest
            : throw new InvalidDataException($"预览播放器构建清单 '{path}' 的身份不符。");
    }

    // 版本目录只读共用：未变化文件硬链接到上一版本，替换时先删链接再写新文件，不就地改写共用文件。
    static void LinkTree(string from, string to, string skip)
    {
        Directory.CreateDirectory(to);
        foreach (string directory in Directory.EnumerateDirectories(from))
            if (!string.Equals(directory, skip, StringComparison.OrdinalIgnoreCase))
                LinkTree(directory, Path.Combine(to, Path.GetFileName(directory)), skip);
        foreach (string file in Directory.EnumerateFiles(from))
        {
            if (Path.GetFileName(file) == ManifestFileName)
                continue;
            WorkbenchArtifacts.LinkImmutable(Path.Combine(to, Path.GetFileName(file)), file);
        }
    }

    // 只比较运行时实际执行的内容：元数据块（MVID 清零）、各方法 IL 体、静态初始化数据与托管资源。
    // PE 时间戳、调试目录与 PDB 路径不参与；Unity 对部分程序集写绝对 PDB 路径，长度不同会让其后字节整体错位。
    internal static string NormalizedHash(string path)
    {
        using var reader = new PEReader(ImmutableArray.Create(File.ReadAllBytes(path)));
        MetadataReader metadata = reader.GetMetadataReader();
        byte[] block = reader.GetMetadata().GetContent().ToArray();
        int mvid = metadata.GetHeapMetadataOffset(HeapIndex.Guid) +
                   (MetadataTokens.GetHeapOffset(metadata.GetModuleDefinition().Mvid) - 1) * 16;
        Array.Clear(block, mvid, 16);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(block);
        foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
        {
            int rva = metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
            if (rva == 0)
                continue;
            int size = reader.GetMethodBody(rva).Size;
            hash.AppendData(reader.GetSectionData(rva).GetContent(0, size).AsSpan());
        }
        foreach (FieldDefinitionHandle handle in metadata.FieldDefinitions)
        {
            FieldDefinition field = metadata.GetFieldDefinition(handle);
            int rva = field.GetRelativeVirtualAddress();
            if (rva != 0)
                hash.AppendData(reader.GetSectionData(rva).GetContent(0, FieldDataSize(metadata, field)).AsSpan());
        }
        DirectoryEntry resources = reader.PEHeaders.CorHeader!.ResourcesDirectory;
        if (resources.Size > 0)
            hash.AppendData(reader.GetSectionData(resources.RelativeVirtualAddress).GetContent(0, resources.Size).AsSpan());
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    // 带 RVA 的字段是编译器生成的静态数组初始化数据，大小来自其值类型的 ClassLayout 或基础类型宽度。
    static int FieldDataSize(MetadataReader metadata, FieldDefinition field)
    {
        BlobReader signature = metadata.GetBlobReader(field.Signature);
        signature.ReadSignatureHeader();
        SignatureTypeCode code = signature.ReadSignatureTypeCode();
        return code switch
        {
            SignatureTypeCode.Boolean or SignatureTypeCode.SByte or SignatureTypeCode.Byte => 1,
            SignatureTypeCode.Char or SignatureTypeCode.Int16 or SignatureTypeCode.UInt16 => 2,
            SignatureTypeCode.Int32 or SignatureTypeCode.UInt32 or SignatureTypeCode.Single => 4,
            SignatureTypeCode.Int64 or SignatureTypeCode.UInt64 or SignatureTypeCode.Double => 8,
            SignatureTypeCode.TypeHandle when signature.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } type =>
                metadata.GetTypeDefinition((TypeDefinitionHandle)type).GetLayout().Size,
            _ => throw new InvalidDataException($"无法确定字段 '{metadata.GetString(field.Name)}' 的 RVA 数据大小。")
        };
    }

    internal static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    sealed class RecipeAssembly
    {
        public required string Name { get; init; }
        public required bool Replaceable { get; init; }
        public string? Reason { get; init; }
        public required string[] Roots { get; init; }
        public required string[] Excludes { get; init; }
        public required (string Path, string Sha256)[] Definitions { get; init; }
        public required List<string> RecordedSources { get; init; }
        public required List<string> Defines { get; init; }

        public static RecipeAssembly Read(JsonObject node, string recipe)
        {
            string name = (string)node["name"]!;
            string folder = Path.Combine(recipe, name);
            var sources = new List<string>();
            var defines = new List<string>();
            foreach (string line in File.ReadAllLines(Path.Combine(folder, name + ".rsp"), Encoding.UTF8))
            {
                if (line.StartsWith('"') && line.EndsWith(".cs\"", StringComparison.OrdinalIgnoreCase))
                    sources.Add(Unquote(line));
                else if (line.StartsWith("-define:", StringComparison.Ordinal))
                    defines.AddRange(line[8..].Split(';', ','));
            }
            return new RecipeAssembly
            {
                Name = name, Replaceable = (bool)node["replaceable"]!, Reason = (string?)node["reason"],
                Roots = node["roots"]!.AsArray().Select(root => (string)root!).ToArray(),
                Excludes = node["excludes"]!.AsArray().Select(root => (string)root!).ToArray(),
                Definitions = node["definitions"]!.AsArray()
                    .Select(item => ((string)item!["path"]!, (string)item["sha256"]!)).ToArray(),
                RecordedSources = sources, Defines = defines
            };
        }

        public IEnumerable<string> DefinitionProblems()
        {
            string[] changed = Definitions
                .Where(definition => !File.Exists(definition.Path) ||
                    Sha256(File.ReadAllBytes(definition.Path)) != definition.Sha256)
                .Select(definition => $"{Name} 的程序集定义 '{definition.Path}' 已变化（引用、宏或平台），需要整包重建。")
                .ToArray();
            if (changed.Length > 0)
                return changed;
            var known = new HashSet<string>(Definitions.Select(definition => definition.Path), StringComparer.OrdinalIgnoreCase);
            return Roots.SelectMany(root => Walk(root, "*.asm*"))
                .Where(file => (file.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                    file.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase)) && !known.Contains(file))
                .Select(file => $"{Name} 的源码目录下新增程序集定义 '{file}'，需要整包重建。");
        }

        public IReadOnlyList<string> CurrentSources(string project)
        {
            if (!Replaceable)
                return RecordedSources;
            string prefix = Path.GetFullPath(project).Replace('\\', '/').TrimEnd('/') + "/";
            return Roots.SelectMany(root => Walk(root, "*.cs"))
                .Select(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? file[prefix.Length..] : file)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        IEnumerable<string> Walk(string directory, string pattern)
        {
            foreach (string file in Directory.EnumerateFiles(directory, pattern))
                yield return file.Replace('\\', '/');
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(child);
                string normalized = child.Replace('\\', '/');
                if (name.StartsWith('.') || name.EndsWith('~') ||
                    Excludes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    continue;
                foreach (string file in Walk(child, pattern))
                    yield return file;
            }
        }

        static string Unquote(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
    }
}

public static class PlayerCodeUpdateApi
{
    public static void MapPlayerCodeUpdateApi(this WebApplication app, PlayerCodeUpdates updates) =>
        app.MapPost("/api/preview/players/code-updates", (UpdatePlayerCodeRequest request) => updates.Update(request));
}
