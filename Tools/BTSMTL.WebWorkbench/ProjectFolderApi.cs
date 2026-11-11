namespace BTSMTL.WebWorkbench;

public sealed record ProjectFolderRoot(string Name, string? Directory,
    Func<IReadOnlyDictionary<string, string>>? Mounts = null, Func<string, string?>? FileIdentity = null);
public sealed record ProjectFolderEntry(string Name, string Path);
public sealed record ProjectFileEntry(string Name, string Path, string Extension, long Length, string? Guid);
public sealed record ProjectFolderListing(string Path, ProjectFolderEntry[] Folders, ProjectFileEntry[] Files);

public static class ProjectFolderApi
{
    public static void MapProjectFolderApi(this WebApplication app, ProjectFolderRoot[] roots)
    {
        app.MapGet("/api/project/folder", (string? path) =>
        {
            if (string.IsNullOrEmpty(path))
                return Results.Json(new ProjectFolderListing("",
                    roots.Select(root => new ProjectFolderEntry(root.Name, root.Name)).ToArray(), []));
            string[] segments = path.Split('/');
            if (segments.Any(segment => segment is "" or "." or "..") || roots.All(root => root.Name != segments[0]))
                return Results.BadRequest(new { error = $"'{path}' 不在 Project 目录内。" });
            ProjectFolderRoot root = roots.Single(root => root.Name == segments[0]);
            IReadOnlyDictionary<string, string>? mounts = root.Mounts?.Invoke();
            if (mounts != null && segments.Length == 1)
                return Results.Json(new ProjectFolderListing(path,
                    mounts.Keys.Select(name => new ProjectFolderEntry(name, path + "/" + name)).ToArray(), []));
            string? directory = mounts == null ? Path.Combine([root.Directory!, .. segments.Skip(1)])
                : mounts.TryGetValue(segments[1], out string? mount) ? Path.Combine([mount, .. segments.Skip(2)]) : null;
            if (directory == null || !Directory.Exists(directory))
                return Results.NotFound(new { error = $"目录 '{path}' 不存在。" });
            var info = new DirectoryInfo(directory);
            ProjectFolderEntry[] folders = info.EnumerateDirectories().Where(child => !Hidden(child.Name))
                .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                .Select(child => new ProjectFolderEntry(child.Name, path + "/" + child.Name)).ToArray();
            ProjectFileEntry[] files = info.EnumerateFiles()
                .Where(file => !Hidden(file.Name) && !file.Name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => new ProjectFileEntry(file.Name, path + "/" + file.Name, file.Extension.ToLowerInvariant(),
                    file.Length, root.FileIdentity?.Invoke(file.FullName))).ToArray();
            return Results.Json(new ProjectFolderListing(path, folders, files));
        });
    }

    static bool Hidden(string name) => name.StartsWith('.') || name.EndsWith('~');
}
