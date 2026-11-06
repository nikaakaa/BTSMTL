using System;
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
        public static readonly AuthoringDocumentKind Timeline = new("timeline", "Timelines", "timeline.json", "m_AuthoringId");
        public static readonly AuthoringDocumentKind Curve = new("curve", "Curves", "curve.json", "m_AuthoringId");
        public static readonly AuthoringDocumentKind Scenario = new("scenario", "Scenarios", null, null);
        public static readonly IReadOnlyList<AuthoringDocumentKind> All = new[] { Skill, Timeline, Curve, Scenario };

        AuthoringDocumentKind(string id, string directory, string rootFile, string identityMember)
        { Id = id; Directory = directory; RootFile = rootFile; IdentityMember = identityMember; }
        public string Id { get; }
        public string Directory { get; }
        public string RootFile { get; }
        public string IdentityMember { get; }
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
            Revision = AuthoringCommits.Revision(new JArray(files.OrderBy(file => file.Key, StringComparer.Ordinal)
                .Select(file => new JArray(file.Key, file.Value))).ToString(Formatting.None));
        }

        public AuthoringDocumentKind Kind { get; }
        public string Identity { get; }
        public string DirectoryPath { get; }
        public string DocumentPath { get; }
        public string SourcePath => Kind.RootFile == null ? DirectoryPath : Path.Combine(DirectoryPath, Kind.RootFile);
        public string Scope => (string)Root?["m_AuthoringScope"];
        public string ContentDomain => (string)Root?["m_ContentDomain"];
        public string SourceIdentity => Kind == AuthoringDocumentKind.Curve ? (string)Root["m_SourceIdentity"] : Identity;
        public string Revision { get; }
        public IReadOnlyDictionary<string, string> Files { get; }
        public JObject Root { get; }
        public JObject Read(string relativePath) => Parse(Files[relativePath]);

        internal static JObject Parse(string text)
        {
            using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        }
    }

    public sealed class AuthoringDocumentElement
    {
        internal AuthoringDocumentElement(AuthoringDocumentSnapshot owner, string kind, string identity, string path, JObject value)
        { Owner = owner; Kind = kind; Identity = identity; RelativePath = path; Value = value; }
        public AuthoringDocumentSnapshot Owner { get; }
        public string Kind { get; }
        public string Identity { get; }
        public string RelativePath { get; }
        public string SourcePath => Path.Combine(Owner.DirectoryPath, RelativePath);
        public JObject Value { get; }
    }

    public sealed class AuthoringDocumentWrite
    {
        public AuthoringDocumentWrite(AuthoringDocumentKind kind, string directoryPath, string expectedRevision,
            IReadOnlyDictionary<string, string> files)
        { Kind = kind; DirectoryPath = directoryPath; ExpectedRevision = expectedRevision; Files = files; }
        public AuthoringDocumentKind Kind { get; }
        public string DirectoryPath { get; }
        public string ExpectedRevision { get; }
        public IReadOnlyDictionary<string, string> Files { get; }
    }

    public sealed class AuthoringDocumentRead
    {
        public AuthoringDocumentRead(string directoryPath, string revision)
        { DirectoryPath = directoryPath; Revision = revision; }
        public string DirectoryPath { get; }
        public string Revision { get; }
    }

    public sealed class AuthoringDocumentRepository
    {
        readonly AuthoringCommits m_Commits;

        public AuthoringDocumentRepository(string workspace)
        {
            Workspace = Path.GetFullPath(workspace);
            m_Commits = AuthoringCommits.For(Workspace);
        }

        public string Workspace { get; }

        public string DirectoryPath(AuthoringDocumentKind kind, string identity) =>
            Inside(Path.Combine(Workspace, kind.Directory, identity));

        public Task<IReadOnlyList<AuthoringDocumentSnapshot>> Read() =>
            m_Commits.Read(() => Task.FromResult(Capture()));

        public Task<AuthoringDocumentSnapshot> Open(string identityOrPath) => m_Commits.Read(() =>
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
                documents[directory] = new AuthoringDocumentSnapshot(write.Kind, Workspace, directory, write.Files);
            }
            return documents.Values.OrderBy(document => document.DirectoryPath, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<AuthoringDocumentElement> Elements(IReadOnlyList<AuthoringDocumentSnapshot> documents)
        {
            var elements = new List<AuthoringDocumentElement>();
            foreach (AuthoringDocumentSnapshot owner in documents)
                foreach (var file in owner.Files.OrderBy(file => file.Key, StringComparer.Ordinal))
                {
                    string folder = Path.GetDirectoryName(file.Key)?.Replace('\\', '/');
                    string kind = file.Key == "timeline.json" || folder == "timelines" ? "timeline" :
                        folder == "graphs" ? "graph" : folder == "inputs" ? "input" :
                        file.Key == "curve.json" ? "curve" : null;
                    if (kind == null) continue;
                    JObject value = owner.Read(file.Key);
                    string identity = kind == "input" ? Path.GetFileNameWithoutExtension(file.Key) :
                        (string)value[kind == "graph" ? "AuthoringId" : "m_AuthoringId"];
                    elements.Add(new AuthoringDocumentElement(owner, kind, identity, file.Key, value));
                }
            return elements;
        }

        public AuthoringDocumentElement Resolve(IReadOnlyList<AuthoringDocumentSnapshot> documents, string kind, string identity) =>
            Elements(documents).Single(element => element.Kind == kind && element.Identity == identity);

        public AuthoringDocumentSnapshot ResolveContent(IReadOnlyList<AuthoringDocumentSnapshot> documents,
            string domain, string sourceIdentity) => documents.Single(document =>
                document.ContentDomain == domain && document.SourceIdentity == sourceIdentity);

        public Task<IReadOnlyList<AuthoringDocumentSnapshot>> Save(IReadOnlyList<AuthoringDocumentWrite> writes,
            IReadOnlyList<AuthoringDocumentRead> reads) => m_Commits.Write(() =>
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
            Elements(edited).Where(element => element.Kind == "graph" || element.Kind == "timeline" || element.Kind == "curve")
                .GroupBy(element => (element.Kind, element.Identity)).ToList().ForEach(group =>
                {
                    if (group.Count() != 1)
                        throw new InvalidDataException($"作者 {group.Key.Kind} 身份 '{group.Key.Identity}' 有多个正式来源。");
                });
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
            m_Commits.Commit(changes);
            return Task.FromResult(edited);
        });

        IReadOnlyList<AuthoringDocumentWrite> Prepare(IReadOnlyList<AuthoringDocumentWrite> writes) => writes.Select(write =>
        {
            string directory = Inside(write.DirectoryPath);
            string library = Path.Combine(Workspace, write.Kind.Directory) + Path.DirectorySeparatorChar;
            if (!directory.StartsWith(library, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"{write.Kind.Id} 文档必须位于 {library}：{directory}");
            return new AuthoringDocumentWrite(write.Kind, directory, write.ExpectedRevision, write.Files);
        }).ToArray();

        IReadOnlyList<AuthoringDocumentSnapshot> Capture()
        {
            var documents = new List<AuthoringDocumentSnapshot>();
            foreach (AuthoringDocumentKind kind in AuthoringDocumentKind.All)
            {
                string library = Path.Combine(Workspace, kind.Directory);
                if (!Directory.Exists(library)) continue;
                IEnumerable<string> directories = kind.RootFile == null ? Directory.EnumerateDirectories(library) :
                    Directory.EnumerateFiles(library, kind.RootFile, SearchOption.AllDirectories).Select(Path.GetDirectoryName);
                foreach (string directory in directories.OrderBy(path => path, StringComparer.Ordinal))
                {
                    var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
                        .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
                            path => File.ReadAllText(path, Encoding.UTF8), StringComparer.Ordinal);
                    documents.Add(new AuthoringDocumentSnapshot(kind, Workspace, directory, files));
                }
            }
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
