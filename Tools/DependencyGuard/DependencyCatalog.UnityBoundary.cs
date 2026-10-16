using System.Text.RegularExpressions;

namespace Btsmtl.DependencyGuard;

sealed partial class DependencyCatalog
{
    readonly List<(string Path, Module Owner)> sourceOwners = [];
    readonly Dictionary<Module, HashSet<DependencyRule>> dependencySourceRules = new();
    public List<VendorSourceMatch> VerifiedVendorSources { get; } = [];
    public List<ProjectBinaryReference> ProjectBinaryReferences { get; } = [];

    void PrepareSourceBoundaries()
    {
        foreach (DependencyRule rule in rules.DependencyRules.Where(rule => rule.CheckProjectDependencySources))
        {
            var pending = new Queue<Module>(Modules.Where(module => IsControlled(module) && rule.Applies(module)));
            var visited = new HashSet<Module>();
            while (pending.Count != 0)
            {
                Module current = pending.Dequeue();
                if (!visited.Add(current)) continue;
                if (!dependencySourceRules.TryGetValue(current, out HashSet<DependencyRule>? applicable))
                    dependencySourceRules.Add(current, applicable = []);
                applicable.Add(rule);
                if (current.AssemblyDefinitionPath is not null)
                    pending.Enqueue(byPath[current.AssemblyDefinitionPath]);
                foreach (Dependency dependency in current.Dependencies)
                    foreach (Module target in DependencyTargets(current, dependency))
                        pending.Enqueue(target);
            }
        }
        foreach (string path in sourceFiles.Order(StringComparer.Ordinal))
            foreach (Module owner in SourceOwners(path))
                sourceOwners.Add((path, owner));
        if (clientRoot is null) return;
        var owners = sourceOwners.Where(item => item.Owner.Kind == "asmdef")
            .ToLookup(item => item.Path, item => item.Owner, StringComparer.OrdinalIgnoreCase);
        foreach (VendorSourceProvenance provenance in rules.VendorSourceProvenance)
        {
            string definitionPath = FullPath(clientRoot, provenance.RootAssemblyDefinition);
            foreach (VendorSourceFile source in provenance.Sources)
            {
                string path = FullPath(clientRoot, source.Path);
                if (!owners[path].Any(owner => owner.Name == provenance.RootAssembly &&
                    owner.Path.Equals(definitionPath, StringComparison.OrdinalIgnoreCase)))
                    continue;
                ReadText(path);
                string hash = Inputs[path];
                if (!hash.Equals(source.SHA256, StringComparison.OrdinalIgnoreCase)) continue;
                VerifiedVendorSources.Add(new(path, definitionPath, provenance.RuleId, provenance.Origin,
                    provenance.Version, provenance.ArchiveSHA256, hash));
            }
        }
    }

    public DependencyRule[] SourceRules(string path, Module owner) => rules.DependencyRules
        .Where(rule => (rule.Applies(owner) || dependencySourceRules.TryGetValue(owner, out HashSet<DependencyRule>? inherited) && inherited.Contains(rule)) &&
            !VerifiedVendorSources.Any(match => match.Rule == rule.Id && match.Path.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                match.AssemblyDefinition.Equals(owner.Path, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    bool AllowsVendorEngineDependency(Module source, DependencyRule rule, Dependency dependency)
    {
        if (clientRoot is null || dependency.Kind is not ("assembly" or "engine")) return false;
        bool engineReference = source.Kind == "asmdef" ? dependency.Kind == "engine" :
            dependency.Kind == "assembly" && dependency.BinaryPath is not null && BinaryReferenceKind(dependency.BinaryPath, dependency.Name) == "UnityEngineLibrary";
        if (!engineReference) return false;
        string? definition = source.Kind == "asmdef" ? source.Path : source.AssemblyDefinitionPath;
        foreach (VendorSourceProvenance provenance in rules.VendorSourceProvenance)
        {
            if (provenance.RuleId != rule.Id || source.Name != provenance.RootAssembly || definition is null ||
                !definition.Equals(FullPath(clientRoot, provenance.RootAssemblyDefinition), StringComparison.OrdinalIgnoreCase) ||
                !GuardRules.Matches(provenance.AllowedDirectEngineAssemblies, dependency.Name))
                continue;
            if (VerifiedVendorSources.Any(match => match.Rule == rule.Id && match.AssemblyDefinition.Equals(definition, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    IEnumerable<Module> DependencyTargets(Module current, Dependency dependency) => IsInputOnlyCandidate(current, dependency) ? [] : dependency.TargetPath is not null
        ? Modules.Where(module => module.Path.Equals(dependency.TargetPath, StringComparison.OrdinalIgnoreCase))
        : Modules.Where(module => module.Name == dependency.Name && module.Kind == current.Kind);

    void CheckProjectPlugins(Module module)
    {
        UnityPluginRule[] applicable = rules.UnityPluginRules.Where(rule => rule.Applies(module)).ToArray();
        if (applicable.Length == 0) return;
        foreach (Dependency dependency in module.Dependencies.Where(item => item.BinaryPath is not null))
        {
            string path = dependency.BinaryPath!;
            string kind = BinaryReferenceKind(path, dependency.Name);
            ProjectBinaryReferences.Add(new(module.Path, module.AssemblyDefinitionPath!, dependency.Name, path, kind));
            if (IsInputOnlyCandidate(module, dependency)) continue;
            if (kind != "PrecompiledPlugin") continue;
            foreach (UnityPluginRule rule in applicable)
            {
                string[] allowed = rule.PrecompiledReferencesByAssembly.GetValueOrDefault(module.Name, []);
                if (!allowed.Any(name => name.Equals(Path.GetFileName(path), StringComparison.Ordinal) && AssemblyName(name) == dependency.Name))
                    AddViolation(rule.Id, module, dependency.Line,
                        $"实际 csproj 引用了未核定的预编译插件 {dependency.Name}（{path}）；所属 asmdef：{module.AssemblyDefinitionPath}；本程序集允许的直接插件：{(allowed.Length == 0 ? "无" : string.Join("、", allowed))}。");
            }
        }
    }

    string BinaryReferenceKind(string path, string assembly)
    {
        if (clientRoot is not null && Path.GetDirectoryName(path)!.Equals(Path.Combine(clientRoot, "Library", "ScriptAssemblies"), StringComparison.OrdinalIgnoreCase) &&
            Modules.Count(module => module.Kind == "asmdef" && module.Name == assembly) == 1)
            return "ScriptAssembly";
        if (IsUnderDirectory(path, sdkRoot) || clientRoot is not null && IsUnderDirectory(path, clientRoot) ||
            linkedPackageDirectories.Any(directory => IsUnderDirectory(path, directory)))
            return "PrecompiledPlugin";
        if (!binaryAssemblyNames.TryGetValue(path, out string? identity) || identity != assembly ||
            !Path.GetFileNameWithoutExtension(path).Equals(assembly, StringComparison.Ordinal))
            return "PrecompiledPlugin";
        string normalized = path.Replace('\\', '/');
        const string unityData = "/Editor/Data/";
        int unity = normalized.IndexOf(unityData, StringComparison.OrdinalIgnoreCase);
        if (unity >= 0 && File.Exists(normalized[..(unity + "/Editor".Length)] + "/Unity.exe"))
        {
            string relative = normalized[(unity + unityData.Length)..];
            if (Regex.IsMatch(relative, @"^(?:NetStandard|UnityReferenceAssemblies|MonoBleedingEdge/lib/mono)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return "Framework";
            if (relative.StartsWith("Managed/", StringComparison.OrdinalIgnoreCase))
            {
                if (Regex.IsMatch(assembly, @"^Unity(?:Engine|Editor)(?:\.|$)", RegexOptions.CultureInvariant))
                    return "UnityEngineLibrary";
                if (assembly.StartsWith("Unity.", StringComparison.Ordinal))
                    return "UnityEditorSdkLibrary";
            }
            if (Regex.IsMatch(relative, @"^PlaybackEngines/[^/]+/(?:[^/]+/)*UnityEditor\.[^/]+\.Extensions\.dll$", RegexOptions.CultureInvariant) &&
                Regex.IsMatch(assembly, @"^UnityEditor\..+\.Extensions$", RegexOptions.CultureInvariant))
                return "UnityEditorSdkLibrary";
        }
        Match dotnet = Regex.Match(normalized, @"^(?<root>.+/dotnet)/packs/[^/]+\.Ref/[^/]+/ref/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (dotnet.Success && File.Exists(dotnet.Groups["root"].Value + "/dotnet.exe")) return "Framework";
        foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string directory = Environment.GetFolderPath(folder);
            if (directory.Length != 0 && IsUnderDirectory(path, Path.Combine(directory, "Reference Assemblies", "Microsoft", "Framework")))
                return "Framework";
        }
        return "PrecompiledPlugin";
    }

    static bool IsUnderDirectory(string path, string directory) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
