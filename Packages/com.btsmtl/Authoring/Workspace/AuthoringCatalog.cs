using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

    public sealed class AuthoringCatalogSnapshot
    {
        internal AuthoringCatalogSnapshot(string revision, IReadOnlyList<AuthoringCatalogEntry> entries,
            IReadOnlyDictionary<string, string> inputs)
        {
            Revision = revision;
            Entries = entries;
            Inputs = inputs;
        }

        public string Revision { get; }
        public IReadOnlyList<AuthoringCatalogEntry> Entries { get; }
        public IReadOnlyDictionary<string, string> Inputs { get; }

        public bool TryResolve(string domain, string value, string context, out AuthoringCatalogEntry entry)
        {
            AuthoringCatalogEntry[] candidates = Entries.Where(item => item.Domain == domain && item.Value == value &&
                (item.Context == context || item.Context == "")).ToArray();
            AuthoringCatalogEntry[] exact = candidates.Where(item => item.Context == context).ToArray();
            AuthoringCatalogEntry[] selected = exact.Length != 0 ? exact : candidates;
            if (selected.Length > 1)
                throw new InvalidDataException($"领域 '{domain}'、引用 '{value}'、上下文 '{context}' 命中多个适配者目录条目：{string.Join(", ", selected.Select(item => item.File))}。");
            entry = selected.SingleOrDefault();
            return entry != null;
        }

        public AuthoringCatalogEntry Require(string domain, string value, string context, string file, string pointer) =>
            TryResolve(domain, value, context, out AuthoringCatalogEntry entry) ? entry : throw new InvalidDataException(
                $"{file}:{pointer}: 领域 '{domain}' 的引用 '{value}' 不在上下文 '{context}' 的适配者目录中。");

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
        sealed class Cached
        {
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
                foreach (string file in Directory.EnumerateFiles(directory, "*.json").OrderBy(item => item, StringComparer.Ordinal))
                {
                    string text = System.IO.File.ReadAllText(file, Encoding.UTF8);
                    string revision = AuthoringCommits.Revision(text);
                    Cached parsed = s_Files.AddOrUpdate(file, _ => Parse(file, path, text, revision),
                        (_, current) => current.Revision == revision ? current : Parse(file, path, text, revision));
                    entries.AddRange(parsed.Entries);
                    inputs.Add(file, parsed.Text);
                    revisions.Append(Path.GetRelativePath(m_Workspace, file)).Append(':').Append(revision).Append('\n');
                }
            }
            return new AuthoringCatalogSnapshot(AuthoringCommits.Revision(revisions.ToString()), entries, inputs);
        }

        static Cached Parse(string file, string adapterPath, string text, string revision)
        {
            string adapter = Path.GetFileName(adapterPath), domain = Path.GetFileNameWithoutExtension(file);
            AuthoringCatalogEntry[] entries = JArray.Parse(text).Cast<JObject>()
                .Select(item => new AuthoringCatalogEntry(adapter, domain, file, item)).ToArray();
            foreach (var duplicate in entries.GroupBy(item => (item.Value, item.Context)).Where(group => group.Count() > 1))
                throw new InvalidDataException($"目录 '{file}' 的引用 '{duplicate.Key.Value}'、上下文 '{duplicate.Key.Context}' 重复。");
            return new Cached { Text = text, Revision = revision, Entries = entries };
        }
    }
}
