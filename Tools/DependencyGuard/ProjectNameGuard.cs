using System.Text;

namespace Btsmtl.DependencyGuard;

sealed class ProjectNameGuard(DependencyCatalog catalog, GuardRules rules, string sdkRoot)
{
    public int SourceCount { get; private set; }

    public void Check()
    {
        foreach (ProjectNameRule rule in rules.ProjectNameRules)
        foreach (string root in rule.Roots)
        {
            string directory = Path.GetFullPath(Path.Combine(sdkRoot, root));
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"规则 {rule.Id} 的扫描目录 '{root}' 不存在。");
            foreach (string path in Files(directory, rule))
                Check(rule, root, path);
        }
    }

    static IEnumerable<string> Files(string directory, ProjectNameRule rule)
    {
        foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
            if (rule.Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                yield return file;
        foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            if (!rule.ExcludedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                foreach (string file in Files(child, rule))
                    yield return file;
    }

    void Check(ProjectNameRule rule, string root, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        catalog.RecordInput(path, bytes);
        SourceCount++;
        string[] lines = Encoding.UTF8.GetString(bytes).Split('\n');
        for (int index = 0; index < lines.Length; index++)
            foreach (string name in rule.ForbiddenNames)
                if (lines[index].Contains(name, StringComparison.OrdinalIgnoreCase))
                    catalog.Violations.Add(new(rule.Id, path, index + 1, root, $"出现项目名字 '{name}'：{rule.Reason}"));
    }
}
