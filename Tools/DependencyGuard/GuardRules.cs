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

    public bool Applies(Module module) => module.Kind == "package"
        ? GuardRules.Matches(SourcePackages, module.Name)
        : GuardRules.Matches(SourceAssemblies, module.Name) || GuardRules.Matches(SourcePackages, module.Package);

    public bool Forbids(Module source, string dependency, string kind, bool direct = true) => kind == "package"
        ? GuardRules.Matches(ForbiddenPackages, dependency)
        : GuardRules.Matches(ForbiddenAssemblies, dependency) ||
          direct && AllowedAssemblies.Length != 0 && dependency != source.Name && !GuardRules.Matches(AllowedAssemblies, dependency);
}

sealed record Dependency(string Name, string Kind, int Line, string? TargetPath = null);

sealed class Module(string name, string path, string kind, string package = "")
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public string Kind { get; } = kind;
    public string Package { get; } = package;
    public List<Dependency> Dependencies { get; } = [];
}

sealed record Violation(string Rule, string File, int Line, string Module, string Detail);
sealed record GuardInput(string Path, string SHA256, string? FinalSHA256, bool Changed);
