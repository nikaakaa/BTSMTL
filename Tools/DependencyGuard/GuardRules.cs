using System.Text.RegularExpressions;

namespace Btsmtl.DependencyGuard;

sealed class GuardRules
{
    public int Version { get; set; }
    public string[] ControlledAssemblies { get; set; } = [];
    public string[] ControlledPackages { get; set; } = [];
    public string[] SourceIdentityAssemblies { get; set; } = [];
    public string[] RoleNames { get; set; } = [];
    public string AuthorIdentityDirectory { get; set; } = "";
    public string[] SkillIdentityFields { get; set; } = [];
    public string[] RetiredDomainOpcodes { get; set; } = [];
    public string[] RetiredTypes { get; set; } = [];
    public DependencyRule[] DependencyRules { get; set; } = [];
    public UnityPluginRule[] UnityPluginRules { get; set; } = [];
    public TimelineBoundaryRule[] TimelineBoundaryRules { get; set; } = [];
    public VendorSourceProvenance[] VendorSourceProvenance { get; set; } = [];
    public UnityActualBoundaryRule UnityActualBoundary { get; set; } = new();

    public static bool Matches(IEnumerable<string> patterns, string text) =>
        patterns.Any(pattern => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
}

sealed class DependencyRule
{
    public string Id { get; set; } = "";
    public string Reason { get; set; } = "";
    public string[] SourceAssemblies { get; set; } = [];
    public string[] SourcePackages { get; set; } = [];
    public string[] ForbiddenAssemblies { get; set; } = [];
    public string[] ForbiddenPackages { get; set; } = [];
    public string[] ForbiddenNamespaces { get; set; } = [];
    public string[] AllowedAssemblies { get; set; } = [];
    public bool CheckProjectDependencySources { get; set; }

    public bool Applies(Module module) => module.Kind == "package"
        ? GuardRules.Matches(SourcePackages, module.Name)
        : GuardRules.Matches(SourceAssemblies, module.Name) || GuardRules.Matches(SourcePackages, module.Package);

    public bool Forbids(Module source, string dependency, string kind, bool direct = true, bool framework = false) => kind == "package"
        ? GuardRules.Matches(ForbiddenPackages, dependency)
        : GuardRules.Matches(ForbiddenAssemblies, dependency) ||
          direct && AllowedAssemblies.Length != 0 && dependency != source.Name && !framework && !GuardRules.Matches(AllowedAssemblies, dependency);
}

sealed class UnityPluginRule
{
    public string Id { get; set; } = "";
    public string[] SourceAssemblies { get; set; } = [];
    public string[] SourcePackages { get; set; } = [];
    public Dictionary<string, string[]> PrecompiledReferencesByAssembly { get; set; } = new(StringComparer.Ordinal);

    public bool Applies(Module module) => (module.Kind == "asmdef" || module.Kind == "project" && module.AssemblyDefinitionPath is not null) &&
        (GuardRules.Matches(SourceAssemblies, module.Name) || GuardRules.Matches(SourcePackages, module.Package));
}

sealed class VendorSourceProvenance
{
    public string Origin { get; set; } = "";
    public string Version { get; set; } = "";
    public string ArchiveSHA256 { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string RootAssembly { get; set; } = "";
    public string RootAssemblyDefinition { get; set; } = "";
    public string[] AllowedDirectEngineAssemblies { get; set; } = [];
    public VendorSourceFile[] Sources { get; set; } = [];
}

sealed class VendorSourceFile
{
    public string Path { get; set; } = "";
    public string SHA256 { get; set; } = "";
}

sealed class UnityActualBoundaryRule
{
    public string EvidenceRelativePath { get; set; } = "";
    public string PolicyCaptureSourceSHA256 { get; set; } = "";
    public OfficialProducerRule[] OfficialProducers { get; set; } = [];
    public string[] ProhibitedProjectAttributeTypes { get; set; } = [];
}

sealed class OfficialProducerRule
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Version { get; set; } = "";
    public string VersionFileSHA256 { get; set; } = "";
    public string ProducerSHA256 { get; set; } = "";
    public string RuntimeSHA256 { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string[] MetadataTypes { get; set; } = [];
    public string[] AssetBaseTypes { get; set; } = [];
}

sealed class TimelineBoundaryRule
{
    public string[] SourceAssemblies { get; set; } = [];
    public string[] SourcePackages { get; set; } = [];
    public TimelineTypeFamily[] TypeFamilies { get; set; } = [];
    public string[] SampleContainers { get; set; } = [];
    public string[] AllowedSampleTypes { get; set; } = [];
    public string[] KindHolderTypes { get; set; } = [];
    public string[] ContractTypes { get; set; } = [];
    public string[] AllowedKinds { get; set; } = [];

    public bool Applies(Module module) => GuardRules.Matches(SourceAssemblies, module.Name) ||
        GuardRules.Matches(SourcePackages, module.Package);
}

sealed class TimelineTypeFamily
{
    public string Name { get; set; } = "";
    public string[] BaseTypes { get; set; } = [];
    public string[] AllowedTypes { get; set; } = [];
}

sealed record Dependency(string Name, string Kind, int Line, string? TargetPath = null, string? BinaryPath = null);

sealed class Module(string name, string path, string kind, string package = "", string? assemblyDefinitionPath = null)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public string Kind { get; } = kind;
    public string Package { get; set; } = package;
    public string? AssemblyDefinitionPath { get; set; } = assemblyDefinitionPath;
    public List<Dependency> Dependencies { get; } = [];
}

sealed record Violation(string Rule, string File, int Line, string Module, string Detail);
sealed record GuardInput(string Path, string SHA256, string? FinalSHA256, bool Changed);
sealed record VendorSourceMatch(string Path, string AssemblyDefinition, string Rule, string Origin, string Version, string ArchiveSHA256, string SHA256);
sealed record ProjectBinaryReference(string Project, string AssemblyDefinition, string Assembly, string Path, string Kind);
