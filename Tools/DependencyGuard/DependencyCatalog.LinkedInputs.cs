using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Btsmtl.DependencyGuard;

sealed partial class DependencyCatalog
{
    readonly HashSet<string> linkedPackageDirectories = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, UnitySelectedPackage> selectedPackages = new(StringComparer.Ordinal);
    readonly Dictionary<string, HashSet<Module>> projectSourceOwners = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, HashSet<Module>> actualSourceOwners = new(StringComparer.OrdinalIgnoreCase);
    readonly List<(Module Owner, string Declaration, int Line, string Input)> unresolvedProjectSources = [];
    readonly List<(Module Owner, string Declaration, int Line, string Input)> unresolvedProjectReferences = [];

    void ReadLinkedPackages(HashSet<string> files)
    {
        string directory = Path.Combine(clientRoot!, "Packages");
        using JsonDocument manifest = ReadJson(Path.Combine(directory, "manifest.json"));
        foreach (JsonProperty dependency in manifest.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            string value = dependency.Value.GetString()!;
            if (!value.StartsWith("file:", StringComparison.Ordinal)) continue;
            string packageDirectory = FullPath(directory, Uri.UnescapeDataString(value[5..]));
            linkedPackageDirectories.Add(packageDirectory);
            files.UnionWith(Enumerate(packageDirectory));
        }
    }

    void ReadSelectedPackages(UnityReferencePolicyFacts facts, CompilationExecutionReceipt receipt)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (UnitySelectedPackage package in facts.SelectedPackages)
        {
            string directory = Path.GetFullPath(package.ResolvedPath);
            string manifestPath = Path.Combine(directory, "package.json");
            RequireReceiptInput(receipt, manifestPath);
            using JsonDocument manifest = JsonDocument.Parse(ActualBytes(manifestPath));
            if (manifest.RootElement.GetProperty("name").GetString() != package.Name ||
                manifest.RootElement.GetProperty("version").GetString() != package.Version ||
                package.AssetPath != "Packages/" + package.Name || package.PackageId.Length == 0 || package.Source.Length == 0)
                throw new InvalidDataException($"本批已选包身份、虚拟根或真实 package.json 不匹配：{package.Name}；{manifestPath}。");
            if (selectedPackages.TryGetValue(package.Name, out UnitySelectedPackage? prior))
            {
                if (prior != package)
                    throw new InvalidDataException($"同批官方选包事实不一致：{package.Name}；{prior.ResolvedPath}；{package.ResolvedPath}。");
                continue;
            }
            selectedPackages.Add(package.Name, package);
            linkedPackageDirectories.Add(directory);
            files.UnionWith(Enumerate(directory));
        }
        if (files.Count != 0) LoadDeclarationFiles(files);
    }

    void LoadPackage(string path)
    {
        if (byPath.ContainsKey(path)) return;
        using JsonDocument document = ReadJson(path);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("name", out JsonElement name)) return;
        Module module = Add(new(name.GetString()!, path, "package"));
        if (root.TryGetProperty("dependencies", out JsonElement dependencies))
            foreach (JsonProperty dependency in dependencies.EnumerateObject())
                module.Dependencies.Add(new(dependency.Name, "package", FindLine(path, dependency.Name)));
    }

    void LoadAssemblyDefinition(string path)
    {
        if (byPath.ContainsKey(path)) return;
        using JsonDocument document = ReadJson(path);
        JsonElement root = document.RootElement;
        Module module = Add(new(root.GetProperty("name").GetString()!, path, "asmdef", PackageFor(path)));
        CheckUnityPlugins(module, root);
        string directory = Path.GetDirectoryName(path)!;
        if (sourceDirectories.TryGetValue(directory, out Module? existing) && existing.Kind == "project")
            sourceDirectories.Remove(directory);
        sourceDirectories.Add(directory, module);
        string guid = Regex.Match(ReadText(path + ".meta"), @"(?m)^guid: ([a-fA-F0-9]{32})").Groups[1].Value;
        if (!byGuid.TryGetValue(guid, out List<Module>? owners)) byGuid.Add(guid, owners = []);
        owners.Add(module);
        foreach (string property in new[] { "references", "precompiledReferences" })
            if (root.TryGetProperty(property, out JsonElement references))
                foreach (JsonElement reference in references.EnumerateArray())
                    module.Dependencies.Add(new(reference.GetString()!, "assembly", FindLine(path, reference.GetString()!)));
        if (!root.TryGetProperty("noEngineReferences", out JsonElement noEngine) || !noEngine.GetBoolean())
            module.Dependencies.Add(new("UnityEngine", "engine", FindLine(path, "noEngineReferences")));
    }

    void BindProjectDefinitions()
    {
        foreach (Module module in Modules.Where(module => module.Kind == "project"))
        {
            Module[] definitions = Modules.Where(candidate => candidate.Kind == "asmdef" && candidate.Name == module.Name).ToArray();
            if (definitions.Length == 1)
            {
                module.Package = definitions[0].Package;
                module.AssemblyDefinitionPath = definitions[0].Path;
            }
            else if (definitions.Length > 1 && IsControlled(module))
                AddViolation("AMBIGUOUS_PROJECT_ASMDEF", module, 1, "同名 asmdef 不唯一，不能确认实际项目包归属：" + string.Join("；", definitions.Select(item => item.Path)));
            else if (definitions.Length == 0 && IsControlled(module) && clientRoot is not null &&
                Path.GetDirectoryName(module.Path)!.Equals(clientRoot, StringComparison.OrdinalIgnoreCase))
                AddViolation("UNRESOLVED_PROJECT_ASMDEF", module, 1, "Client 根目录的受控生成项目没有同名 asmdef，不能确认实际包与 Unity 插件边界；须在正式项目生成入口同步当前程序集声明。");
        }
        foreach (var input in unresolvedProjectReferences.Where(input => IsControlled(input.Owner)))
            Violations.Add(new("UNRESOLVED_PROJECT", input.Declaration, input.Line, input.Owner.Name,
                $"ProjectReference 无真实目标：{input.Input}。"));
    }

    void ReadProjectSourceItem(string declaration, Module owner, string directory, Dictionary<string, string> properties, XElement item)
    {
        string kind = item.Name.LocalName;
        int line = ((System.Xml.IXmlLineInfo)item).LineNumber;
        if (item.Attribute("Remove") is XAttribute removal)
        {
            if (kind == "Compile")
                foreach (string pattern in Expand(removal.Value, properties).Split(';', StringSplitOptions.RemoveEmptyEntries))
                    foreach (var source in projectSourceOwners.Where(source => MatchesProjectPattern(source.Key, directory, pattern)))
                        source.Value.Remove(owner);
            return;
        }
        string[] excludes = Expand(item.Attribute("Exclude")?.Value ?? "", properties).Split(';', StringSplitOptions.RemoveEmptyEntries);
        string include = Expand((item.Attribute("Include") ?? item.Attribute("Update"))!.Value, properties);
        foreach (string pattern in include.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (kind == "None" && Path.GetExtension(pattern) is not (".asmdef" or ".asmref")) continue;
            foreach (string path in ProjectInputFiles(declaration, owner, directory, line, pattern))
            {
                if (excludes.Any(exclude => MatchesProjectPattern(path, directory, exclude))) continue;
                if (kind == "Compile")
                {
                    if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    sourceFiles.Add(path);
                    if (!projectSourceOwners.TryGetValue(path, out HashSet<Module>? owners)) projectSourceOwners.Add(path, owners = []);
                    owners.Add(owner);
                }
                else
                {
                    for (string? parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
                    {
                        string package = Path.Combine(parent, "package.json");
                        if (!File.Exists(package)) continue;
                        linkedPackageDirectories.Add(parent);
                        LoadPackage(package);
                        break;
                    }
                    if (Path.GetExtension(path) == ".asmdef") LoadAssemblyDefinition(path);
                    else
                    {
                        using JsonDocument document = ReadJson(path);
                        assemblyReferences[Path.GetDirectoryName(path)!] = document.RootElement.GetProperty("reference").GetString()!;
                    }
                }
            }
        }
    }

    IEnumerable<string> ProjectInputFiles(string declaration, Module owner, string directory, int line, string pattern)
    {
        if (pattern.Contains("$(", StringComparison.Ordinal))
        {
            unresolvedProjectSources.Add((owner, declaration, line, pattern));
            yield break;
        }
        string path = FullPath(directory, pattern);
        int wildcard = path.IndexOfAny(['*', '?']);
        if (wildcard < 0)
        {
            if (File.Exists(path)) yield return path;
            else unresolvedProjectSources.Add((owner, declaration, line, path));
            yield break;
        }
        string normalized = path.Replace('\\', '/');
        string root = normalized[..normalized.LastIndexOf('/', wildcard)];
        if (!Directory.Exists(root))
        {
            unresolvedProjectSources.Add((owner, declaration, line, path));
            yield break;
        }
        foreach (string source in Enumerate(root))
            if (MatchesProjectPattern(source, directory, pattern)) yield return source;
    }

    static bool MatchesProjectPattern(string path, string directory, string pattern)
    {
        string expression = Regex.Escape(FullPath(directory, pattern).Replace('\\', '/'))
            .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal).Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal).Replace(@"\?", "[^/]", StringComparison.Ordinal);
        return Regex.IsMatch(path.Replace('\\', '/'), "^" + expression + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    void CheckProjectSourceInputs()
    {
        foreach (var input in unresolvedProjectSources.Where(input => IsControlled(input.Owner) || dependencySourceRules.ContainsKey(input.Owner)))
            Violations.Add(new("UNRESOLVED_PROJECT_SOURCE", input.Declaration, input.Line, input.Owner.Name,
                $"实际项目源码或程序集声明输入无法解析：{input.Input}；不能忽略该依赖的源码检查。"));
    }
}
