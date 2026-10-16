using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Btsmtl.DependencyGuard;

sealed partial class DependencyCatalog(string sdkRoot, string? clientRoot, GuardRules rules)
{
    readonly Dictionary<string, Module> byPath = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<Module>> byGuid = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Module> sourceDirectories = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> assemblyReferences = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> sourceFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> texts = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> binaryAssemblyNames = new(StringComparer.OrdinalIgnoreCase);
    public List<Module> Modules { get; } = [];
    public List<Violation> Violations { get; } = [];
    public Dictionary<string, string> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsControlled(Module module) => GuardRules.Matches(rules.ControlledAssemblies, module.Name) ||
        GuardRules.Matches(rules.ControlledPackages, module.Package) || module.Kind == "package" && GuardRules.Matches(rules.ControlledPackages, module.Name);

    public void LoadDeclarations()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in new[] { "Packages", "Projects", "Tools" })
            files.UnionWith(Enumerate(Path.Combine(sdkRoot, directory)));
        if (clientRoot is not null)
        {
            foreach (string directory in new[] { "Assets", "Packages", "Library/PackageCache" })
                if (Directory.Exists(Path.Combine(clientRoot, directory)))
                    files.UnionWith(Enumerate(Path.Combine(clientRoot, directory)));
            files.UnionWith(Directory.EnumerateFiles(clientRoot, "*.csproj"));
            ReadLinkedPackages(files);
        }
        LoadDeclarationFiles(files);
    }

    void LoadDeclarationFiles(HashSet<string> files)
    {
        int previousModuleCount = Modules.Count;
        foreach (string path in files.Where(path => Path.GetFileName(path) == "package.json").Order(StringComparer.Ordinal))
            LoadPackage(path);
        foreach (string path in files.Where(path => path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            LoadAssemblyDefinition(path);
        foreach (string path in files.Where(path => path.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase)))
        {
            using JsonDocument document = ReadJson(path);
            assemblyReferences[Path.GetDirectoryName(path)!] = document.RootElement.GetProperty("reference").GetString()!;
        }
        foreach (string path in files.Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
            LoadProject(path);
        sourceFiles.UnionWith(files.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
        if (Modules.Count == previousModuleCount) return;
        BindProjectDefinitions();
        foreach (Module module in Modules.Where(module => module.Kind == "asmdef"))
        {
            for (int index = 0; index < module.Dependencies.Count; index++)
            {
                Dependency dependency = module.Dependencies[index];
                if (dependency.Name.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase))
                {
                    if (byGuid.TryGetValue(dependency.Name[5..], out List<Module>? targets) && targets.Count == 1)
                        module.Dependencies[index] = dependency with { Name = targets[0].Name, TargetPath = targets[0].Path };
                }
                else
                    module.Dependencies[index] = dependency with { Name = AssemblyName(dependency.Name) };
            }
        }
    }

    public void PrepareSources()
    {
        PrepareSourceBoundaries();
        CheckProjectSourceInputs();
        foreach (Module module in Modules.Where(module => module.Kind == "asmdef" && (IsControlled(module) || dependencySourceRules.ContainsKey(module))))
            foreach (Dependency dependency in module.Dependencies.Where(item => item.Name.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase)))
            {
                List<Module>? targets = byGuid.GetValueOrDefault(dependency.Name[5..]);
                AddViolation("UNRESOLVED_GUID", module, dependency.Line, $"无法唯一解析真实 asmdef 引用 {dependency.Name}。" +
                    (targets is null ? "" : " 候选：" + string.Join("；", targets.Select(target => target.Path))));
            }
    }

    void CheckUnityPlugins(Module module, JsonElement root)
    {
        foreach (UnityPluginRule rule in rules.UnityPluginRules.Where(rule => rule.Applies(module)))
        {
            if (!root.TryGetProperty("overrideReferences", out JsonElement overrideReferences) || !overrideReferences.GetBoolean())
                AddViolation(rule.Id, module, FindLine(module.Path, "overrideReferences"),
                    "中立程序集必须显式声明 overrideReferences=true；省略或 false 会接受 Unity 自动引用的全部启用插件，不能仅凭源码或独立工程宣称已隔离 Photon/Quantum。");
            string[] allowed = rule.PrecompiledReferencesByAssembly.GetValueOrDefault(module.Name, []);
            if (root.TryGetProperty("precompiledReferences", out JsonElement references))
                foreach (JsonElement reference in references.EnumerateArray())
                {
                    string name = reference.GetString()!;
                    if (!allowed.Contains(name, StringComparer.Ordinal))
                        AddViolation(rule.Id, module, FindLine(module.Path, name),
                            $"中立程序集声明了未核定的预编译插件 {name}；本程序集允许的直接插件：{(allowed.Length == 0 ? "无" : string.Join("、", allowed))}。");
                }
        }
    }

    public IEnumerable<(string Path, Module Owner)> Sources()
    {
        foreach ((string path, Module owner) in sourceOwners)
            yield return (path, owner);
    }

    IEnumerable<Module> SourceOwners(string path)
    {
        if (actualSourceOwners.TryGetValue(path, out HashSet<Module>? actualOwners))
        {
            foreach (Module owner in actualOwners.OrderBy(owner => owner.Path, StringComparer.Ordinal)) yield return owner;
            yield break;
        }
        Module? projectOwner = null;
        string? directory = Path.GetDirectoryName(path);
        while (directory is not null)
        {
            if (sourceDirectories.TryGetValue(directory, out Module? module))
            {
                if (module.Kind == "asmdef") { yield return module; yield break; }
                projectOwner ??= module;
            }
            if (assemblyReferences.TryGetValue(directory, out string? reference))
            {
                Module[] targets = reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase)
                    ? byGuid.GetValueOrDefault(reference[5..], []).ToArray()
                    : Modules.Where(module => module.Kind == "asmdef" && module.Name == reference).ToArray();
                if (targets.Length == 1)
                {
                    yield return targets[0];
                    yield break;
                }
                if (IsControlledSource(path) || GuardRules.Matches(rules.ControlledAssemblies, reference))
                    Violations.Add(new("UNRESOLVED_ASMREF", path, 1, reference, "无法唯一解析源码所属程序集。"));
                yield break;
            }
            directory = Path.GetDirectoryName(directory);
        }
        if (projectSourceOwners.TryGetValue(path, out HashSet<Module>? owners))
            foreach (Module owner in owners.OrderBy(owner => owner.Path, StringComparer.Ordinal))
                yield return owner;
        else if (projectOwner is not null)
            yield return projectOwner;
    }

    bool IsControlledSource(string path)
    {
        if (GuardRules.Matches(rules.ControlledPackages, PackageFor(path))) return true;
        if (projectSourceOwners.TryGetValue(path, out HashSet<Module>? owners) &&
            owners.Any(owner => IsControlled(owner) || dependencySourceRules.ContainsKey(owner))) return true;
        for (string? directory = Path.GetDirectoryName(path); directory is not null; directory = Path.GetDirectoryName(directory))
            if (sourceDirectories.TryGetValue(directory, out Module? owner)) return IsControlled(owner) || dependencySourceRules.ContainsKey(owner);
        return false;
    }

    bool IsUnityGeneratedProject(Module module) => module.Kind == "project" && clientRoot is not null &&
        Path.GetDirectoryName(module.Path)!.Equals(clientRoot, StringComparison.OrdinalIgnoreCase);

    public void CheckDeclaredDependencies()
    {
        foreach (Module source in Modules.Where(module => IsControlled(module) && !IsUnityGeneratedProject(module)))
        {
            CheckDependencyChains(source, false);
            if (source.Kind == "package")
                CheckPackageCycle(source, source, [], new HashSet<string>(StringComparer.Ordinal));
        }
        foreach (Module module in Modules.Where(module => module.Kind == "project" && !IsUnityGeneratedProject(module)))
            CheckProjectPlugins(module);
    }

    public void CheckActualDependencies()
    {
        foreach (Module source in Modules.Where(module => IsControlled(module) && IsUnityGeneratedProject(module)))
            CheckDependencyChains(source, true);
        foreach (Module module in Modules.Where(IsUnityGeneratedProject))
            CheckProjectPlugins(module);
        CheckActualOutputDependencies();
    }

    void CheckDependencyChains(Module source, bool actualUnityInputs)
    {
        foreach (DependencyRule rule in rules.DependencyRules.Where(rule => rule.Applies(source)))
        {
            var pending = new Queue<(Module Module, string Chain)>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source.Path };
            pending.Enqueue((source, source.Name));
            while (pending.Count != 0)
            {
                (Module current, string chain) = pending.Dequeue();
                if (!actualUnityInputs && IsUnityGeneratedProject(current)) continue;
                foreach (Dependency dependency in current.Dependencies)
                {
                    if (IsInputOnlyCandidate(current, dependency)) continue;
                    string nextChain = chain + " -> " + dependency.Name;
                    bool framework = dependency.BinaryPath is not null && BinaryReferenceKind(dependency.BinaryPath, dependency.Name) == "Framework";
                    if (rule.Forbids(source, dependency.Name, dependency.Kind, current == source, framework) &&
                        !(current == source && AllowsVendorEngineDependency(source, rule, dependency)))
                        AddViolation(rule.Id, source, current == source ? dependency.Line : 1, $"{rule.Reason} 依赖链：{nextChain}；声明：{current.Path}:{dependency.Line}。");
                    foreach (Module target in DependencyTargets(current, dependency))
                        if (visited.Add(target.Path))
                            pending.Enqueue((target, nextChain));
                }
            }
        }
    }

    void CheckPackageCycle(Module source, Module current, List<string> chain, HashSet<string> visited)
    {
        if (!visited.Add(current.Name))
            return;
        chain.Add(current.Name);
        foreach (Dependency dependency in current.Dependencies)
        {
            if (dependency.Name == source.Name)
                AddViolation("PACKAGE_CYCLE", source, dependency.Line, "包依赖成环：" + string.Join(" -> ", chain.Append(source.Name)));
            else
                foreach (Module target in Modules.Where(module => module.Kind == "package" && module.Name == dependency.Name && IsControlled(module)))
                    CheckPackageCycle(source, target, chain, visited);
        }
        chain.RemoveAt(chain.Count - 1);
    }

    Module LoadProject(string path)
    {
        path = Path.GetFullPath(path);
        if (byPath.TryGetValue(path, out Module? existing))
            return existing;
        XDocument document = XDocument.Parse(ReadText(path), LoadOptions.SetLineInfo);
        string name = document.Descendants().LastOrDefault(element => element.Name.LocalName == "AssemblyName")?.Value ?? Path.GetFileNameWithoutExtension(path);
        Module module = Add(new(name, path, "project", PackageFor(path)));
        sourceDirectories.TryAdd(Path.GetDirectoryName(path)!, module);
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSBuildProjectDirectory"] = Path.GetDirectoryName(path)!, ["MSBuildProjectName"] = Path.GetFileNameWithoutExtension(path),
            ["Configuration"] = "Debug", ["SolutionDir"] = sdkRoot + Path.DirectorySeparatorChar
        };
        string? parent = Path.GetDirectoryName(path);
        while (parent is not null)
        {
            string props = Path.Combine(parent, "Directory.Build.props");
            if (File.Exists(props)) { ReadProjectItems(props, module, properties, new HashSet<string>(StringComparer.OrdinalIgnoreCase)); break; }
            parent = Path.GetDirectoryName(parent);
        }
        ReadProjectItems(path, module, properties, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return module;
    }

    void ReadProjectItems(string path, Module module, Dictionary<string, string> properties, HashSet<string> visited)
    {
        if (!visited.Add(path))
            return;
        XDocument document = XDocument.Parse(ReadText(path), LoadOptions.SetLineInfo);
        string directory = Path.GetDirectoryName(path)!;
        properties["MSBuildThisFileDirectory"] = directory + Path.DirectorySeparatorChar;
        foreach (XElement element in document.Root!.Elements())
        {
            if (element.Name.LocalName == "PropertyGroup")
                foreach (XElement property in element.Elements())
                    properties[property.Name.LocalName] = Expand(property.Value, properties);
            if (element.Name.LocalName == "Import" && element.Attribute("Sdk") is null)
            {
                string import = Expand(element.Attribute("Project")!.Value, properties);
                if (!import.Contains("$(", StringComparison.Ordinal) && !import.Contains('*'))
                {
                    string importPath = FullPath(directory, import);
                    if (File.Exists(importPath))
                        ReadProjectItems(importPath, module, properties, visited);
                }
                properties["MSBuildThisFileDirectory"] = directory + Path.DirectorySeparatorChar;
            }
            if (element.Name.LocalName != "ItemGroup")
                continue;
            foreach (XElement item in element.Elements())
            {
                string kind = item.Name.LocalName;
                if (kind is "Compile" or "None")
                {
                    ReadProjectSourceItem(path, module, directory, properties, item);
                    continue;
                }
                if (kind is not ("ProjectReference" or "Reference"))
                    continue;
                if (item.Attribute("Remove") is XAttribute removal)
                {
                    string removed = Expand(removal.Value, properties);
                    module.Dependencies.RemoveAll(dependency => kind == "ProjectReference"
                        ? dependency.TargetPath == FullPath(directory, removed)
                        : dependency.Name == AssemblyName(removed));
                    continue;
                }
                if (item.Attribute("ReferenceOutputAssembly")?.Value == "false" || item.Attribute("OutputItemType")?.Value == "Analyzer" ||
                    item.Elements().Any(child => child.Name.LocalName == "ReferenceOutputAssembly" && child.Value == "false"))
                    continue;
                string include = Expand((item.Attribute("Include") ?? item.Attribute("Update"))!.Value, properties);
                int line = ((System.Xml.IXmlLineInfo)item).LineNumber;
                if (kind == "ProjectReference")
                {
                    string targetPath = FullPath(directory, include);
                    if (include.Contains("$(", StringComparison.Ordinal) || !File.Exists(targetPath))
                    {
                        unresolvedProjectReferences.Add((module, path, line, include + " -> " + targetPath));
                        module.Dependencies.Add(new(Path.GetFileNameWithoutExtension(include), "assembly", line));
                    }
                    else
                    {
                        Module target = LoadProject(targetPath);
                        module.Dependencies.Add(new(target.Name, "assembly", line, target.Path));
                    }
                }
                else
                {
                    string referenceName = AssemblyName(include);
                    string? binaryPath = null;
                    string? hint = item.Attribute("HintPath")?.Value ?? item.Elements().FirstOrDefault(child => child.Name.LocalName == "HintPath")?.Value;
                    if (hint is not null)
                    {
                        string expanded = Expand(hint, properties);
                        string hintPath = FullPath(directory, expanded);
                        if (!expanded.Contains("$(", StringComparison.Ordinal)) binaryPath = hintPath;
                        if (!expanded.Contains("$(", StringComparison.Ordinal) && File.Exists(hintPath))
                        {
                            if (!binaryAssemblyNames.TryGetValue(hintPath, out string? binaryName))
                            {
                                using var stream = new MemoryStream(ReadBytes(hintPath));
                                using var reader = new PEReader(stream);
                                binaryName = reader.GetMetadataReader().GetString(reader.GetMetadataReader().GetAssemblyDefinition().Name);
                                binaryAssemblyNames.Add(hintPath, binaryName);
                            }
                            referenceName = binaryName;
                        }
                    }
                    module.Dependencies.Add(new(referenceName, "assembly", line, BinaryPath: binaryPath));
                }
            }
        }
    }

    Module Add(Module module) { Modules.Add(module); byPath.Add(module.Path, module); return module; }
    public void AddViolation(string rule, Module module, int line, string detail) => Violations.Add(new(rule, module.Path, line, module.Name, detail));
    public string ReadText(string path)
    {
        if (!texts.TryGetValue(path, out string? text)) texts.Add(path, text = Encoding.UTF8.GetString(ReadBytes(path)).TrimStart('\uFEFF'));
        return text;
    }
    byte[] ReadBytes(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        RecordInput(path, bytes);
        return bytes;
    }
    public void RecordInput(string path, byte[] bytes) => Inputs.TryAdd(path, Convert.ToHexString(SHA256.HashData(bytes)));
    JsonDocument ReadJson(string path) => JsonDocument.Parse(ReadText(path));
    int FindLine(string path, string value)
    {
        string text = ReadText(path);
        int position = text.IndexOf(value, StringComparison.Ordinal);
        return position < 0 ? 1 : text[..position].Count(character => character == '\n') + 1;
    }
    string PackageFor(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        while (directory is not null)
        {
            string package = Path.Combine(directory, "package.json");
            if (byPath.TryGetValue(package, out Module? module)) return module.Name;
            directory = Path.GetDirectoryName(directory);
        }
        return "";
    }
    static string AssemblyName(string text)
    {
        string name = text.Split(',')[0].Trim();
        return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name) : name;
    }
    static string FullPath(string directory, string path) => Path.GetFullPath(Path.Combine(directory, path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
    static string Expand(string text, Dictionary<string, string> properties)
    {
        string expanded;
        do { expanded = text; text = Regex.Replace(text, @"\$\(([^)]+)\)", match => properties.GetValueOrDefault(match.Groups[1].Value, match.Value)); }
        while (text != expanded);
        return text;
    }
    static IEnumerable<string> Enumerate(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            foreach (string child in Directory.EnumerateDirectories(directory))
                if (Path.GetFileName(child) is not (".git" or ".codex" or "obj" or "bin" or "node_modules" or ".tools") &&
                    !Path.GetFileName(child).EndsWith('~'))
                    pending.Push(child);
            foreach (string path in Directory.EnumerateFiles(directory))
                if (Path.GetExtension(path) is ".cs" or ".csproj" or ".asmdef" or ".asmref" || Path.GetFileName(path) == "package.json")
                    yield return Path.GetFullPath(path);
        }
    }
}
