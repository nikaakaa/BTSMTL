using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
    public sealed class AuthoringDocumentKind
    {
        public static readonly AuthoringDocumentKind Skill = new("skill", "Skills", "skill.json", "m_AbilityId");
        public static readonly AuthoringDocumentKind Timeline = new("timeline", "Timelines", "timeline.json", "m_AuthoringId", "timeline");
        public static readonly AuthoringDocumentKind Curve = new("curve", "Curves", "curve.json", "m_AuthoringId", "curve", "m_SourceIdentity");
        public static readonly AuthoringDocumentKind Scenario = new("scenario", "Scenarios", null, null);
        public static readonly AuthoringDocumentKind Profile = new("profile", "Profiles", "profile.json",
            "m_AuthoringId", "profile", "m_SourceIdentity");
        public static readonly AuthoringDocumentKind Variables = new("variables", "Variables", "variables.json",
            "m_OwnerId", "variables");
        public static readonly AuthoringDocumentKind Graph = new("graph", "Graphs", "graph.json", "AuthoringId", "graph");
        public static readonly IReadOnlyList<AuthoringDocumentKind> All = new[] { Skill, Timeline, Curve, Scenario, Profile, Variables, Graph };

        AuthoringDocumentKind(string id, string directory, string rootFile, string identityMember,
            string rootElementKind = null, string sourceIdentityMember = null)
        {
            Id = id;
            Directory = directory;
            RootFile = rootFile;
            IdentityMember = identityMember;
            RootElementKind = rootElementKind;
            SourceIdentityMember = sourceIdentityMember;
        }
        public string Id { get; }
        public string Directory { get; }
        public string RootFile { get; }
        public string IdentityMember { get; }
        public string RootElementKind { get; }
        public string SourceIdentityMember { get; }
    }

    public sealed class AuthoringDocumentSnapshot
    {
        internal AuthoringDocumentSnapshot(AuthoringDocumentKind kind, string workspace, string directory,
            IReadOnlyDictionary<string, string> files)
        {
            Kind = kind;
            DirectoryPath = directory;
            DocumentPath = Path.GetRelativePath(workspace, directory).Replace('\\', '/');
            Files = new ReadOnlyDictionary<string, string>(files.ToDictionary(file => file.Key, file => file.Value, StringComparer.Ordinal));
            Root = kind.RootFile == null ? null : Parse(files[kind.RootFile]);
            Identity = kind.RootFile == null ? Path.GetFileName(directory) : (string)Root[kind.IdentityMember];
            if (string.IsNullOrEmpty(Identity))
                throw new InvalidDataException($"作者文档 '{directory}' 缺少正式身份字段 '{kind.IdentityMember}'。");
            if (kind.SourceIdentityMember != null)
            {
                if (string.IsNullOrEmpty(SourceIdentity))
                    throw new InvalidDataException($"作者文档 '{directory}' 缺少正式来源身份字段 '{kind.SourceIdentityMember}'。");
                if (string.IsNullOrEmpty(ContentDomain))
                    throw new InvalidDataException($"作者文档 '{directory}' 缺少正式来源领域字段 'm_ContentDomain'。");
            }
            Revision = AuthoringCommits.Revision(new JArray(files.OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => new JArray(file.Key, file.Value))).ToString(Formatting.None));
        }

        public AuthoringDocumentKind Kind { get; }
        public string Identity { get; }
        public string DirectoryPath { get; }
        public string DocumentPath { get; }
        public string SourcePath => Kind.RootFile == null ? DirectoryPath : Path.Combine(DirectoryPath, Kind.RootFile);
        public string Scope => (string)Root?["m_AuthoringScope"];
        public string ContentDomain => Kind == AuthoringDocumentKind.Timeline ? Kind.RootElementKind :
            (string)Root?["m_ContentDomain"];
        public string SourceIdentity => Kind.SourceIdentityMember == null ? Identity : (string)Root[Kind.SourceIdentityMember];
        public string Revision { get; }
        public IReadOnlyDictionary<string, string> Files { get; }
        // 快照按文件戳在同一工作区的读取之间复用，Root 与 Read 的结果只读使用。
        public JObject Root { get; }
        public JObject Read(string relativePath) => Parse(Files[relativePath]);
        internal ConcurrentDictionary<Delegate, AuthoringDocumentIdentities> Identities { get; } = new();

        internal static JObject Parse(string text)
        {
            using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }
    }

    public sealed class AuthoringDocumentElement
    {
        readonly Func<object, object> m_ReadAuthoring;

        public AuthoringDocumentElement(AuthoringDocumentSnapshot owner, string domain, string identity, string path,
            JObject value, Type authoringType, Func<object, object> readAuthoring)
            : this(owner, domain, identity, path, value, domain, identity)
        {
            AuthoringType = authoringType;
            m_ReadAuthoring = readAuthoring;
            if (string.IsNullOrEmpty(identity))
                throw new InvalidDataException($"作者文档 '{owner.DocumentPath}/{path}' 的元素 '{domain}' 在 '{JsonPath}' 缺少正式身份。");
        }

        internal AuthoringDocumentElement(AuthoringDocumentSnapshot owner, string kind, string identity, string path,
            JObject value, string contentDomain, string sourceIdentity)
        {
            Owner = owner; Kind = kind; Identity = identity; RelativePath = path; Value = value;
            ContentDomain = contentDomain; SourceIdentity = sourceIdentity;
            m_ReadAuthoring = authoring => authoring;
        }
        public AuthoringDocumentSnapshot Owner { get; }
        public string Kind { get; }
        public string Identity { get; }
        public string RelativePath { get; }
        public string SourcePath => Path.Combine(Owner.DirectoryPath, RelativePath);
        public JObject Value { get; }
        public string ContentDomain { get; }
        public string SourceIdentity { get; }
        public string JsonPath => AuthoringJsonPointer.Of(Value);
        public Type AuthoringType { get; }
        public object ReadAuthoring(object authoring) => m_ReadAuthoring(authoring);
    }

    public sealed class AuthoringDocumentWrite
    {
        public AuthoringDocumentWrite(AuthoringDocumentKind kind, string directoryPath, string expectedRevision,
            IReadOnlyDictionary<string, string> files)
        { Kind = kind; DirectoryPath = directoryPath; ExpectedRevision = expectedRevision; Files = files; }
        AuthoringDocumentWrite(AuthoringDocumentKind kind, string directoryPath, string expectedRevision)
        {
            Kind = kind; DirectoryPath = directoryPath; ExpectedRevision = expectedRevision;
            Files = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
            IsDeletion = true;
        }
        public static AuthoringDocumentWrite Delete(AuthoringDocumentKind kind, string directoryPath,
            string expectedRevision) => new(kind, directoryPath, expectedRevision);
        public AuthoringDocumentKind Kind { get; }
        public string DirectoryPath { get; }
        public string ExpectedRevision { get; }
        public IReadOnlyDictionary<string, string> Files { get; }
        public bool IsDeletion { get; }
    }

    public sealed class AuthoringDocumentRead
    {
        public AuthoringDocumentRead(string directoryPath, string revision)
        { DirectoryPath = directoryPath; Revision = revision; }
        public string DirectoryPath { get; }
        public string Revision { get; }
    }

    // 单份文档参与全库身份唯一性检查的键；随快照缓存，未变化的文档不再重新解析。
    sealed class AuthoringDocumentIdentities
    {
        public AuthoringDocumentIdentities((string Kind, string Identity)[] roots, (string Domain, string Source)[] contents)
        { Roots = roots; Contents = contents; }
        public (string Kind, string Identity)[] Roots { get; }
        public (string Domain, string Source)[] Contents { get; }
    }

    // 同一工作区的文档快照，按目录内全部 JSON 的相对路径、修改时间与长度判定复用。
    sealed class AuthoringDocumentCache
    {
        static readonly ConcurrentDictionary<string, AuthoringDocumentCache> s_Workspaces = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, (string Stamp, AuthoringDocumentSnapshot Snapshot)> m_Documents = new(StringComparer.OrdinalIgnoreCase);

        public static AuthoringDocumentCache For(string workspace) => s_Workspaces.GetOrAdd(workspace, _ => new AuthoringDocumentCache());

        public AuthoringDocumentSnapshot Read(AuthoringDocumentKind kind, string workspace, string directory)
        {
            FileInfo[] files = new DirectoryInfo(directory).GetFiles("*.json", SearchOption.AllDirectories);
            var relative = new string[files.Length];
            for (int i = 0; i < files.Length; i++)
                relative[i] = Path.GetRelativePath(directory, files[i].FullName).Replace('\\', '/');
            Array.Sort(relative, files, StringComparer.Ordinal);
            var stamp = new StringBuilder();
            for (int i = 0; i < files.Length; i++)
                stamp.Append(relative[i]).Append('|').Append(files[i].LastWriteTimeUtc.Ticks).Append('|').Append(files[i].Length).Append('\n');
            string key = stamp.ToString();
            lock (m_Documents)
                if (m_Documents.TryGetValue(directory, out var cached) && cached.Stamp == key)
                    return cached.Snapshot;
            var contents = new Dictionary<string, string>(files.Length, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
                contents.Add(relative[i], File.ReadAllText(files[i].FullName, Encoding.UTF8));
            var snapshot = new AuthoringDocumentSnapshot(kind, workspace, directory, contents);
            lock (m_Documents)
                m_Documents[directory] = (key, snapshot);
            return snapshot;
        }

        public void Retain(HashSet<string> directories)
        {
            lock (m_Documents)
                foreach (string removed in m_Documents.Keys.Where(directory => !directories.Contains(directory)).ToArray())
                    m_Documents.Remove(removed);
        }
    }

    public sealed class AuthoringDocumentRepository
    {
        static readonly Func<AuthoringDocumentSnapshot, IEnumerable<AuthoringDocumentElement>> NoDeclaredElements =
            _ => Array.Empty<AuthoringDocumentElement>();
        readonly AuthoringDocumentCache m_Cache;
        readonly Func<AuthoringDocumentSnapshot, IEnumerable<AuthoringDocumentElement>> m_DeclaredElements;

        // 只组合内存编辑视图的调用方（如持锁宿主启动的编译子进程）不触碰磁盘，不能在构造时抢占跨进程作者锁。
        AuthoringCommits Commits => AuthoringCommits.For(Workspace);

        public AuthoringDocumentRepository(string workspace,
            Func<AuthoringDocumentSnapshot, IEnumerable<AuthoringDocumentElement>> declaredElements = null)
        {
            Workspace = Path.GetFullPath(workspace);
            m_Cache = AuthoringDocumentCache.For(Workspace);
            m_DeclaredElements = declaredElements;
        }

        public string Workspace { get; }

        public string DirectoryPath(AuthoringDocumentKind kind, string identity) =>
            Inside(Path.Combine(Workspace, kind.Directory, identity));

        public Task<IReadOnlyList<AuthoringDocumentSnapshot>> Read() =>
            Commits.Read(() => Task.FromResult(Capture()));

        public Task<AuthoringDocumentSnapshot> Open(string identityOrPath) => Commits.Read(() =>
            Task.FromResult(Find(Capture(), identityOrPath)));

        public IReadOnlyList<AuthoringDocumentSnapshot> EditingView(IReadOnlyList<AuthoringDocumentSnapshot> source,
            IReadOnlyList<AuthoringDocumentWrite> writes) => Edit(source, Prepare(writes));

        IReadOnlyList<AuthoringDocumentSnapshot> Edit(IReadOnlyList<AuthoringDocumentSnapshot> source,
            IReadOnlyList<AuthoringDocumentWrite> writes)
        {
            var documents = source.ToDictionary(document => document.DirectoryPath, StringComparer.OrdinalIgnoreCase);
            foreach (AuthoringDocumentWrite write in writes)
            {
                string directory = write.DirectoryPath;
                if (write.IsDeletion)
                    documents.Remove(directory);
                else
                    documents[directory] = new AuthoringDocumentSnapshot(write.Kind, Workspace, directory, write.Files);
            }
            return documents.Values.OrderBy(document => document.DirectoryPath, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<AuthoringDocumentElement> Elements(IReadOnlyList<AuthoringDocumentSnapshot> documents) =>
            EnumerateElements(documents, null, null).ToArray();

        IEnumerable<AuthoringDocumentElement> EnumerateElements(IReadOnlyList<AuthoringDocumentSnapshot> documents,
            string contentDomain, string sourceIdentity)
        {
            foreach (AuthoringDocumentSnapshot owner in documents)
            {
                if (owner.Kind.RootElementKind == null || contentDomain != null && owner.ContentDomain != contentDomain ||
                    sourceIdentity != null && owner.SourceIdentity != sourceIdentity)
                    continue;
                yield return new AuthoringDocumentElement(owner, owner.Kind.RootElementKind, owner.Identity,
                    owner.Kind.RootFile, owner.Root, owner.ContentDomain, owner.SourceIdentity);
            }
            foreach (AuthoringDocumentSnapshot owner in documents)
            {
                foreach (var file in owner.Files.Where(file => file.Key != owner.Kind.RootFile)
                             .OrderBy(file => file.Key, StringComparer.Ordinal))
                {
                    string folder = Path.GetDirectoryName(file.Key)?.Replace('\\', '/');
                    string kind = folder == "timelines" ? "timeline" :
                        folder == "graphs" ? "graph" : folder == "inputs" ? "input" : null;
                    if (kind == null) continue;
                    string domain = kind == "timeline" ? AuthoringDocumentKind.Timeline.RootElementKind : null;
                    if (contentDomain != null && domain != contentDomain) continue;
                    JObject value = owner.Read(file.Key);
                    string identity = kind == "input" ? Path.GetFileNameWithoutExtension(file.Key) :
                        (string)value[kind == "graph" ? "AuthoringId" : "m_AuthoringId"];
                    if (sourceIdentity != null && identity != sourceIdentity) continue;
                    yield return new AuthoringDocumentElement(owner, kind, identity, file.Key, value,
                        domain, domain == null ? null : identity);
                }
                if (m_DeclaredElements != null)
                    foreach (AuthoringDocumentElement element in m_DeclaredElements(owner))
                        if ((contentDomain == null || element.ContentDomain == contentDomain) &&
                            (sourceIdentity == null || element.SourceIdentity == sourceIdentity))
                            yield return element;
            }
        }

        public AuthoringDocumentElement Resolve(IReadOnlyList<AuthoringDocumentSnapshot> documents, string kind, string identity) =>
            EnumerateElements(documents, null, null).Single(element => element.Kind == kind && element.Identity == identity);

        public AuthoringDocumentSnapshot ResolveContent(IReadOnlyList<AuthoringDocumentSnapshot> documents,
            string domain, string sourceIdentity) => ResolveContentElement(documents, domain, sourceIdentity).Owner;

        public AuthoringDocumentElement ResolveContentElement(IReadOnlyList<AuthoringDocumentSnapshot> documents,
            string domain, string sourceIdentity) => EnumerateElements(documents, domain, sourceIdentity).First();

        public Task<IReadOnlyList<AuthoringDocumentSnapshot>> Save(IReadOnlyList<AuthoringDocumentWrite> writes,
            IReadOnlyList<AuthoringDocumentRead> reads) => Commits.Write(() =>
        {
            writes = Prepare(writes);
            IReadOnlyList<AuthoringDocumentSnapshot> captured = Capture();
            var documents = captured.ToDictionary(document => document.DirectoryPath, StringComparer.OrdinalIgnoreCase);
            var expected = reads.Select(read => (Directory: Inside(read.DirectoryPath), read.Revision))
                .Concat(writes.Select(write => (Directory: write.DirectoryPath, Revision: write.ExpectedRevision)))
                .GroupBy(read => read.Directory, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Select(read => read.Revision).Distinct(StringComparer.Ordinal).Single(),
                    StringComparer.OrdinalIgnoreCase);
            foreach (var read in expected)
            {
                string actual = documents.TryGetValue(read.Key, out AuthoringDocumentSnapshot document) ? document.Revision : "";
                if (actual != read.Value)
                    throw new InvalidOperationException($"作者文档 '{read.Key}' 已在读取后变化，期望修订 {read.Value}、实际 {actual}。");
            }
            IReadOnlyList<AuthoringDocumentSnapshot> edited = Edit(captured, writes);
            var roots = new HashSet<(string, string)>();
            var contents = new HashSet<(string, string)>();
            foreach (AuthoringDocumentSnapshot document in edited)
            {
                AuthoringDocumentIdentities identities = IdentitiesOf(document);
                foreach (var root in identities.Roots)
                    if (!roots.Add(root))
                        throw new InvalidDataException($"作者 {root.Kind} 身份 '{root.Identity}' 有多个正式来源。");
                foreach (var content in identities.Contents)
                    if (!contents.Add(content))
                        throw new InvalidDataException($"作者内容 '{content.Domain}' 的身份 '{content.Source}' 有多个正式来源。");
            }
            var changes = new List<AuthoringFileChange>();
            foreach (AuthoringDocumentWrite write in writes)
            {
                string directory = write.DirectoryPath;
                var previous = documents.TryGetValue(directory, out AuthoringDocumentSnapshot document)
                    ? document.Files : new Dictionary<string, string>();
                foreach (var file in write.Files)
                {
                    string path = DocumentFile(directory, file.Key);
                    if (!previous.TryGetValue(file.Key, out string old) || old != file.Value)
                        changes.Add(new AuthoringFileChange(path, file.Value));
                }
                foreach (string removed in previous.Keys.Except(write.Files.Keys, StringComparer.Ordinal))
                    changes.Add(new AuthoringFileChange(DocumentFile(directory, removed), null));
            }
            Commits.Commit(changes);
            return Task.FromResult(edited);
        });

        IReadOnlyList<AuthoringDocumentWrite> Prepare(IReadOnlyList<AuthoringDocumentWrite> writes) => writes.Select(write =>
        {
            string directory = Inside(write.DirectoryPath);
            string library = Path.Combine(Workspace, write.Kind.Directory) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(library, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{write.Kind.Id} 文档必须位于 {library}：{directory}");
            return write.IsDeletion ? AuthoringDocumentWrite.Delete(write.Kind, directory, write.ExpectedRevision) :
                new AuthoringDocumentWrite(write.Kind, directory, write.ExpectedRevision, write.Files);
        }).ToArray();

        AuthoringDocumentIdentities IdentitiesOf(AuthoringDocumentSnapshot document) =>
            document.Identities.GetOrAdd(m_DeclaredElements ?? NoDeclaredElements, _ =>
            {
                AuthoringDocumentElement[] elements = EnumerateElements(new[] { document }, null, null).ToArray();
                return new AuthoringDocumentIdentities(
                    elements.Where(element => element.Kind != "input" && element.Value.Parent == null)
                        .Select(element => (element.Kind, element.Identity)).ToArray(),
                    elements.Where(element => element.ContentDomain != null)
                        .Select(element => (element.ContentDomain, element.SourceIdentity)).ToArray());
            });

        IReadOnlyList<AuthoringDocumentSnapshot> Capture()
        {
            var documents = new List<AuthoringDocumentSnapshot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AuthoringDocumentKind kind in AuthoringDocumentKind.All)
            {
                string library = Path.Combine(Workspace, kind.Directory);
                if (!Directory.Exists(library)) continue;
                IEnumerable<string> directories = kind.RootFile == null ? Directory.EnumerateDirectories(library) :
                    Directory.EnumerateFiles(library, kind.RootFile, SearchOption.AllDirectories).Select(Path.GetDirectoryName);
                foreach (string directory in directories.OrderBy(path => path, StringComparer.Ordinal))
                {
                    documents.Add(m_Cache.Read(kind, Workspace, directory));
                    seen.Add(directory);
                }
            }
            m_Cache.Retain(seen);
            return documents;
        }

        static AuthoringDocumentSnapshot Find(IReadOnlyList<AuthoringDocumentSnapshot> documents, string identityOrPath) =>
            documents.Single(document => document.Identity == identityOrPath || document.DocumentPath == identityOrPath ||
                string.Equals(document.DirectoryPath.Replace('\\', '/'), identityOrPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(document.SourcePath.Replace('\\', '/'), identityOrPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

        string Inside(string path)
        {
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(Workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"作者文档必须位于 {Workspace}：{path}");
            return full;
        }

        static string DocumentFile(string directory, string relative)
        {
            string path = Path.GetFullPath(Path.Combine(directory, relative));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"作者文件必须位于文档目录 {directory}：{relative}");
            return path;
        }
    }
}
