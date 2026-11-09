using BTSMTL.Authoring.Workspace;
using BTSMTL.Graphs;

namespace BTSMTL.WebWorkbench;

// 一个作者根目录下的直接子目录（如技能或场景方案）：读取共享工作区读锁并行，写入与后续处理持写锁独占，原文读写，修订覆盖全部 JSON 文本。
public sealed class AuthoringFolders
{
    readonly string m_Root;
    readonly string m_Kind;
    readonly AuthoringCommits m_Commits;
    readonly AuthoringDocumentRepository m_Documents;
    readonly AuthoringDocumentKind m_DocumentKind;

    // 作者工作区下的全部读取与提交共用同一把读写锁与事务日志；根目录是工作区下某一文档种类的目录。
    public AuthoringFolders(AuthoringDocumentService documents, AuthoringDocumentKind kind, string displayName)
    {
        m_DocumentKind = kind;
        m_Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(documents.Workspace, kind.Directory)));
        m_Kind = displayName;
        m_Commits = AuthoringCommits.For(documents.Workspace);
        m_Documents = documents.Repository;
    }

    public AuthoringCommits Commits => m_Commits;

    public string[] List() => m_Documents.Read().GetAwaiter().GetResult().Where(document => document.Kind == m_DocumentKind)
        .Select(document => Path.GetRelativePath(m_Root, document.DirectoryPath).Replace('\\', '/'))
        .Order(StringComparer.Ordinal).ToArray();

    public Task<T> For<T>(string name, Func<string, Task<T>> action) => m_Commits.Read(() => action(DirectoryPath(name)));

    public Task<T> ForWrite<T>(string name, Func<string, Task<T>> action) => m_Commits.Write(() => action(DirectoryPath(name)));

    public Task<T> ForWrite<T>(Func<Task<T>> action) => m_Commits.Write(action);

    public Task<T> ForDocument<T>(string documentPath, Func<string, Task<T>> action) =>
        m_Commits.Read(() => action(DocumentDirectoryPath(documentPath)));

    public Task<T> ForDocumentWrite<T>(string documentPath, Func<string, Task<T>> action) =>
        m_Commits.Write(() => action(DocumentDirectoryPath(documentPath)));

    string DocumentDirectoryPath(string documentPath)
    {
        int separator = documentPath.IndexOf('/');
        AuthoringDocumentKind kind = AuthoringDocumentKind.All.Single(kind => kind.Directory == documentPath[..separator]);
        return m_Documents.DirectoryPath(kind, documentPath[(separator + 1)..]);
    }

    string DirectoryPath(string name)
    {
        string directory = Path.GetFullPath(Path.Combine(m_Root, name));
        if (!string.Equals(Path.GetDirectoryName(directory), m_Root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{m_Kind}必须是已配置目录的直接子目录。", nameof(name));
        return directory;
    }

    public string FilePath(string directory, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(directory, relative));
        if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"保存路径必须是当前{m_Kind}目录内的 JSON 文件。", nameof(relative));
        return path;
    }

    public void RequireSaveRevision(string directory, string? expectedRevision)
    {
        string name = Path.GetFileName(directory);
        if (expectedRevision == null)
        {
            if (File.Exists(directory) || Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new InvalidOperationException($"{m_Kind} '{name}' 的首次保存目标已被占用，请读取现有内容后再保存。");
            return;
        }
        if (!Directory.Exists(directory))
            throw new InvalidOperationException($"{m_Kind} '{name}' 的已保存来源已被移除，请重新读取当前内容。");
        string revision = Snapshot(name, directory).Content.Revision;
        if (revision != expectedRevision)
            throw new InvalidOperationException($"{m_Kind} '{name}' 的磁盘来源已变化，期望修订 {expectedRevision}、实际 {revision}，请重新读取后再保存。");
    }

    public IReadOnlyList<AuthoringDocumentSnapshot> WriteRecording(string directory, SkillAuthoringWrite content,
        IReadOnlyList<BtsmtlGraphVariableDeclarations> variables)
    {
        AuthoringDocumentSnapshot? current = m_Documents.Read().GetAwaiter().GetResult()
            .SingleOrDefault(document => document.DirectoryPath == directory);
        var files = current == null ? new Dictionary<string, string>(StringComparer.Ordinal)
            : current.Files.ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal);
        string Relative(string path) => Path.GetRelativePath(directory, FilePath(directory, path)).Replace('\\', '/');
        foreach (SkillAuthoringFile file in content.Files)
            files[Relative(file.Path)] = file.Content;
        foreach (string removed in content.RemovedFiles)
            files.Remove(Relative(removed));
        var writes = new List<AuthoringDocumentWrite> { new(m_DocumentKind, directory, current?.Revision ?? "", files) };
        foreach (BtsmtlGraphVariableDeclarations declarations in variables)
            writes.Add(new AuthoringDocumentWrite(AuthoringDocumentKind.Variables,
                m_Documents.DirectoryPath(AuthoringDocumentKind.Variables, declarations.OwnerId), "",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [AuthoringDocumentKind.Variables.RootFile] = AuthoringJsonText.Text(BtsmtlGraphVariableDeclarationsJson.Write(declarations))
                }));
        HashSet<string> written = writes.Select(write => write.DirectoryPath).ToHashSet(StringComparer.Ordinal);
        return m_Documents.Save(writes, []).GetAwaiter().GetResult()
            .Where(document => written.Contains(document.DirectoryPath)).ToArray();
    }

    public void Delete(string directory)
    {
        string? other = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .FirstOrDefault(path => !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase));
        if (other != null)
            throw new InvalidDataException($"{m_Kind}目录包含非 JSON 作者文件 '{other}'，不能删除整个目录。");
        m_Commits.Commit(Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(path => new AuthoringFileChange(path, null)).ToArray());
        Directory.Delete(directory, recursive: true);
    }

    public (SkillAuthoringSnapshot Content, SkillAuthoringFileRevision[] Files) Snapshot(string name, string directory) =>
        Snapshot(m_Documents.Open(directory).GetAwaiter().GetResult());

    public static (SkillAuthoringSnapshot Content, SkillAuthoringFileRevision[] Files) Snapshot(AuthoringDocumentSnapshot document)
    {
        SkillAuthoringFile[] files = document.Files.Select(file => new SkillAuthoringFile(file.Key, file.Value))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        SkillAuthoringFileRevision[] revisions = files.Select(file => new SkillAuthoringFileRevision(file.Path,
            AuthoringCommits.Revision(file.Content))).ToArray();
        return (new(document.DocumentPath, document.Revision, files, files), revisions);
    }

    public Task<SkillAuthoringSnapshot> Read(string name) => For(name, directory =>
        Task.FromResult(Snapshot(name, directory).Content));

    public Task<SkillAuthoringChanges> Changes(string name, string revision) => For(name, directory =>
    {
        var snapshot = Snapshot(name, directory);
        return Task.FromResult(new SkillAuthoringChanges(name, snapshot.Content.Revision,
            snapshot.Content.Revision != revision, snapshot.Files));
    });
}
