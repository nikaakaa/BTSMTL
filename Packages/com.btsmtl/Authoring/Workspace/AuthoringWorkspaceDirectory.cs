#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Storage;

namespace BTSMTL.Authoring.Workspace
{

public sealed record AuthoringSourceFile(string Path, string Revision, string Text,
#if !UNITY_5_3_OR_NEWER
    [property: System.Text.Json.Serialization.JsonIgnore]
#endif
    ReadOnlyMemory<byte> Bytes);
public sealed record AuthoringRootFiles(AuthoringRootDocument Root, string Revision,
    IReadOnlyList<AuthoringSourceFile> Files);
public sealed record AuthoringRootEntry(string Id, string DisplayName, string Domain, string Kind,
    string Path, string Revision, IReadOnlyList<string> ReferencedRoots, IReadOnlyList<AuthoringSourceProblem> Problems);
public sealed record AuthoringSourceProblem(string Path, string Code, string Message);
public sealed record AuthoringPage<T>(IReadOnlyList<T> Items, int Offset, int Total, int? NextOffset);

public sealed class AuthoringWorkspaceDirectory
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    public AuthoringWorkspaceDirectory(string path) => Path = System.IO.Path.GetFullPath(path);
    public string Path { get; }

    public AuthoringSourceFile ReadSource(string relativePath)
    {
        string path = Resolve(Path, relativePath);
        using var scope = AuthoringFileAccessScope.Acquire(new[] { path });
        return ReadFile(path);
    }

    public AuthoringPage<AuthoringRootEntry> List(int offset, int limit)
    {
        RequirePage(offset, limit);
        string[] paths = Directory.EnumerateFiles(Path, "root.json", SearchOption.AllDirectories)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var items = new List<AuthoringRootEntry>();
        foreach (string path in paths.Skip(offset).Take(limit))
        {
            try
            {
                AuthoringSourceFile file;
                using (AuthoringFileAccessScope.Acquire(new[] { path })) file = ReadFile(path);
                AuthoringRootDocument root = AuthoringDocumentJson.ReadRoot(file.Text, file.Path);
                items.Add(new(root.Id.Value, root.DisplayName, root.DomainId.Value, root.Kind,
                    System.IO.Path.GetRelativePath(Path, path).Replace('\\', '/'), file.Revision,
                    root.ExternalReferences.ToArray().Select(reference => reference.RootId.Value)
                        .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    Array.Empty<AuthoringSourceProblem>()));
            }
            catch (Exception error) when (error is IOException or FormatException or
                Newtonsoft.Json.JsonException or ArgumentException)
            {
                items.Add(new(string.Empty, System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path))!,
                    string.Empty, string.Empty, System.IO.Path.GetRelativePath(Path, path).Replace('\\', '/'),
                    string.Empty, Array.Empty<string>(),
                    new[] { new AuthoringSourceProblem(path, "root_input_invalid", error.Message) }));
            }
        }
        int next = offset + items.Count;
        return new(items, offset, paths.Length, next < paths.Length ? next : null);
    }

    public AuthoringRootFiles Read(string rootPath)
    {
        string path = Resolve(Path, rootPath);
        AuthoringSourceFile manifest = ReadFile(path);
        AuthoringRootDocument root = AuthoringDocumentJson.ReadRoot(manifest.Text, manifest.Path);
        string directory = System.IO.Path.GetDirectoryName(path)!;
        using var scope = AuthoringFileAccessScope.Acquire(new[] { path }.Concat(root.Files.ToArray()
            .Select(file => Resolve(directory, file.Path))));
        AuthoringSourceFile source = ReadFile(path);
        if (source.Revision != manifest.Revision)
            throw new IOException("读取期间作者根清单已变化，请按当前文件重新读取：" + path);
        var files = new List<AuthoringSourceFile> { source };
        foreach (AuthoringDocumentFile file in root.Files.Span)
            files.Add(ReadFile(Resolve(directory, file.Path)));
        return Snapshot(root, files);
    }

    internal static AuthoringRootFiles Snapshot(AuthoringRootDocument root, IReadOnlyList<AuthoringSourceFile> files)
    {
        string directory = System.IO.Path.GetDirectoryName(files[0].Path)!;
        string revision = AuthoringFileRevision.Compute(Utf8.GetBytes(string.Join("\n",
            files.Select(file => System.IO.Path.GetRelativePath(directory, file.Path).Replace('\\', '/') +
                        ":" + file.Revision).OrderBy(value => value, StringComparer.Ordinal))));
        return new(root, revision, files);
    }

    public AuthoringPage<AuthoringRootEntry> Referencing(string rootId, int offset, int limit)
    {
        RequirePage(offset, limit);
        var matches = new List<AuthoringRootEntry>();
        int cursor = 0;
        do
        {
            AuthoringPage<AuthoringRootEntry> page = List(cursor, 200);
            foreach (AuthoringRootEntry entry in page.Items)
            {
                if (entry.ReferencedRoots.Contains(rootId, StringComparer.Ordinal))
                    matches.Add(entry);
            }
            if (page.NextOffset is not int next) break;
            cursor = next;
        } while (true);
        return new(matches.Skip(offset).Take(limit).ToArray(), offset, matches.Count,
            offset + limit < matches.Count ? offset + limit : null);
    }

    public static string Resolve(string directory, string relativePath)
    {
        string normalizedDirectory = System.IO.Path.GetFullPath(directory)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(normalizedDirectory, relativePath));
        if (System.IO.Path.IsPathRooted(relativePath) || !path.StartsWith(
                normalizedDirectory + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"作者文件必须属于明确根目录：'{relativePath}'。", nameof(relativePath));
        return path;
    }

    static AuthoringSourceFile ReadFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        bool bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        return new(path, AuthoringFileRevision.Compute(bytes), Utf8.GetString(bytes.AsSpan(bom ? 3 : 0)), bytes);
    }

    static void RequirePage(int offset, int limit)
    {
        if (offset < 0 || limit < 1 || limit > 200)
            throw new ArgumentException("分页要求 offset ≥ 0，limit 在 1～200 之间。");
    }
}
}
