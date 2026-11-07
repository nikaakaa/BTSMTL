using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Workspace
{
    public sealed class AuthoringCatalogEntry
    {
        internal AuthoringCatalogEntry(string adapter, string domain, string file, JObject source)
        {
            Adapter = adapter;
            Domain = domain;
            File = file;
            Value = source.Value<string>("value") ?? throw new InvalidDataException($"目录 '{file}' 的条目缺少 value。");
            Display = source.Value<string>("display") ?? throw new InvalidDataException($"目录 '{file}' 的 '{Value}' 缺少 display。");
            Context = source.Value<string>("context") ?? throw new InvalidDataException($"目录 '{file}' 的 '{Value}' 缺少 context。");
            Revision = source.Value<string>("revision") ?? throw new InvalidDataException($"目录 '{file}' 的 '{Value}' 缺少 revision。");
            Data = source["data"];
        }

        public string Adapter { get; }
        public string Domain { get; }
        public string File { get; }
        public string Value { get; }
        public string Display { get; }
        public string Context { get; }
        public string Revision { get; }
        public JToken Data { get; }
        public JObject ToJson() => new JObject
        {
            ["value"] = Value, ["display"] = Display, ["context"] = Context,
            ["revision"] = Revision, ["data"] = Data?.DeepClone()
        };
    }

    public sealed class AuthoringCatalogResolution
    {
        internal AuthoringCatalogResolution(AuthoringCatalogEntry entry, string state, string reason, string revision)
        { Entry = entry; State = state; Reason = reason; Revision = revision; }
        public AuthoringCatalogEntry Entry { get; }
        public string State { get; }
        public string Reason { get; }
        public string Revision { get; }
        public bool Available => State == "available";
    }

    public sealed class AuthoringCatalogSnapshot
    {
        readonly IReadOnlyDictionary<(string Domain, string Value), AuthoringCatalogEntry[]> m_Entries;
        internal AuthoringCatalogSnapshot(string revision, IReadOnlyList<AuthoringCatalogEntry> entries,
            IReadOnlyDictionary<string, string> inputs)
        {
            Revision = revision;
            Entries = entries;
            Inputs = inputs;
            m_Entries = entries.GroupBy(item => (item.Domain, item.Value)).ToDictionary(group => group.Key, group => group.ToArray());
        }

        public string Revision { get; }
        public IReadOnlyList<AuthoringCatalogEntry> Entries { get; }
        public IReadOnlyDictionary<string, string> Inputs { get; }

        public AuthoringCatalogResolution Resolve(string domain, string value, string context)
        {
            AuthoringCatalogEntry[] candidates = m_Entries.TryGetValue((domain, value), out AuthoringCatalogEntry[] available)
                ? available.Where(item => item.Context == context || item.Context == "").ToArray() : Array.Empty<AuthoringCatalogEntry>();
            AuthoringCatalogEntry[] exact = candidates.Where(item => item.Context == context).ToArray();
            AuthoringCatalogEntry[] selected = exact.Length != 0 ? exact : candidates;
            string state = selected.Length == 1 ? "available" : selected.Length == 0 ? "missing" : "conflict";
            string reason = state == "available" ? null : state == "missing"
                ? $"领域 '{domain}' 的引用 '{value}' 不在上下文 '{context}' 的适配者目录中。"
                : $"领域 '{domain}'、引用 '{value}'、上下文 '{context}' 命中多个适配者目录条目：{string.Join(", ", selected.Select(item => item.File))}。";
            string revision = AuthoringCommits.Revision(domain + "\n" + value + "\n" + context + "\n" + state + "\n" +
                string.Join("\n", selected.OrderBy(item => item.File, StringComparer.Ordinal)
                    .Select(item => item.Adapter + "\n" + item.Context + "\n" + item.Revision)));
            return new AuthoringCatalogResolution(selected.Length == 1 ? selected[0] : null, state, reason, revision);
        }

        public bool TryResolve(string domain, string value, string context, out AuthoringCatalogEntry entry)
        {
            AuthoringCatalogResolution resolution = Resolve(domain, value, context);
            entry = resolution.Entry;
            return resolution.Available;
        }

        public AuthoringCatalogEntry Require(string domain, string value, string context, string file, string pointer)
        {
            AuthoringCatalogResolution resolution = Resolve(domain, value, context);
            return resolution.Available ? resolution.Entry : throw new InvalidDataException($"{file}:{pointer}: {resolution.Reason}");
        }

        public JObject ToJson(string knownRevision = null)
        {
            bool changed = knownRevision != Revision;
            return new JObject
            {
                ["revision"] = Revision, ["changed"] = changed,
                ["domains"] = changed ? new JArray(Entries.GroupBy(item => (item.Adapter, item.Domain)).Select(group => new JObject
                {
                    ["adapter"] = group.Key.Adapter, ["domain"] = group.Key.Domain,
                    ["items"] = new JArray(group.Select(item => item.ToJson()))
                })) : null
            };
        }
    }

    public sealed class AuthoringCatalog
    {
        // 按文件修改时间与长度判定复用，与作者文档快照一致；戳变化才重新读盘、算修订与解析。
        sealed class Cached
        {
            public string Stamp;
            public string Text;
            public string Revision;
            public AuthoringCatalogEntry[] Entries;
        }

        static readonly ConcurrentDictionary<string, Cached> s_Files = new(StringComparer.Ordinal);
        readonly string m_Workspace;

        public AuthoringCatalog(string workspace) => m_Workspace = Path.GetFullPath(workspace);

        public AuthoringCatalogSnapshot Read(string adapter = null)
        {
            string adapterRoot = Path.Combine(m_Workspace, "Adapters");
            string[] adapters = adapter == null
                ? Directory.Exists(adapterRoot) ? Directory.GetDirectories(adapterRoot) : Array.Empty<string>()
                : new[] { Path.Combine(adapterRoot, adapter) };
            var entries = new List<AuthoringCatalogEntry>();
            var inputs = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var revisions = new StringBuilder();
            foreach (string path in adapters.OrderBy(item => item, StringComparer.Ordinal))
            {
                string directory = Path.Combine(path, "catalog");
                if (!Directory.Exists(directory))
                    continue;
                foreach (FileInfo info in new DirectoryInfo(directory).EnumerateFiles("*.json")
                             .OrderBy(item => item.FullName, StringComparer.Ordinal))
                {
                    string file = info.FullName;
                    string stamp = info.LastWriteTimeUtc.Ticks + "|" + info.Length;
                    if (!s_Files.TryGetValue(file, out Cached parsed) || parsed.Stamp != stamp)
                        s_Files[file] = parsed = Parse(file, path, stamp);
                    entries.AddRange(parsed.Entries);
                    inputs.Add(file, parsed.Text);
                    revisions.Append(Path.GetRelativePath(m_Workspace, file)).Append(':').Append(parsed.Revision).Append('\n');
                }
            }
            return new AuthoringCatalogSnapshot(AuthoringCommits.Revision(revisions.ToString()), entries, inputs);
        }

        static Cached Parse(string file, string adapterPath, string stamp)
        {
            string text = System.IO.File.ReadAllText(file, Encoding.UTF8);
            string revision = AuthoringCommits.Revision(text);
            string adapter = Path.GetFileName(adapterPath), domain = Path.GetFileNameWithoutExtension(file);
            using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
            AuthoringCatalogEntry[] entries = JArray.Load(reader).Cast<JObject>()
                .Select(item => new AuthoringCatalogEntry(adapter, domain, file, item)).ToArray();
            return new Cached { Stamp = stamp, Text = text, Revision = revision, Entries = entries };
        }
    }
}
