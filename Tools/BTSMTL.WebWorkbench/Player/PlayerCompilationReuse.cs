using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

// 编译节点按“归一化参数 + 全部输入内容”取指纹；历史回执中同指纹且产物摘要仍一致的节点直接取用其产物。
sealed class PlayerCompilationReuse
{
    internal const string FileName = "reusable-outputs.json";
    const string Format = "q3-preview-player-reusable-outputs/1";
    readonly ConcurrentDictionary<string, string> m_Hashes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (PlayerReusableNode Node, string Compilation)> m_Candidates = new(StringComparer.Ordinal);
    readonly List<PlayerReusableNode> m_Nodes = new();
    readonly List<JsonObject> m_Reused = new();
    readonly string m_Compiler;

    public PlayerCompilationReuse(string compilations, string current, JsonObject recipe, IEnumerable<string> tools)
    {
        m_Compiler = Format + "\n" + string.Join("\n", tools.Select(name => (string)recipe["compiler"]![name]!["sha256"]!));
        foreach (string file in Directory.EnumerateDirectories(compilations)
                     .Where(directory => !string.Equals(Path.GetFullPath(directory), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
                     .Select(directory => Path.Combine(directory, FileName)).Where(File.Exists)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
            foreach (PlayerReusableNode node in JsonSerializer.Deserialize<PlayerReusableNode[]>(File.ReadAllBytes(file), WorkbenchArtifacts.JsonOptions)!)
                m_Candidates.TryAdd(node.Key, (node, Path.GetFileName(Path.GetDirectoryName(file))!));
    }

    public int ReusedCount { get { lock (m_Nodes) return m_Reused.Count; } }

    public PlayerCompilationFile Fact(string path) => new(Path.GetFullPath(path), Hash(path));

    string Hash(string path) => m_Hashes.GetOrAdd(Path.GetFullPath(path), full => PlayerScriptCompiler.Fact(full).Sha256);

    public string Key(string kind, JsonObject node, IEnumerable<string> text, IEnumerable<string> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Append(string value)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value));
            hash.AppendData(stackalloc byte[] { 0 });
        }
        Append(m_Compiler);
        Append(kind);
        Append(node["Env"]?.ToJsonString() ?? "");
        foreach (string value in text)
            Append(value);
        Append("<files>");
        foreach (string file in files)
            Append(Hash(file));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public bool TryReuse(string name, string key, string[] outputs)
    {
        if (!m_Candidates.TryGetValue(key, out var found) || found.Node.Outputs.Length != outputs.Length ||
            found.Node.Outputs.Any(output => !File.Exists(output.Path) || Hash(output.Path) != output.Sha256))
            return false;
        PlayerReusableNode candidate = found.Node;
        for (int i = 0; i < outputs.Length; i++)
        {
            File.Copy(candidate.Outputs[i].Path, outputs[i]);
            m_Hashes[Path.GetFullPath(outputs[i])] = candidate.Outputs[i].Sha256;
        }
        lock (m_Nodes)
            m_Reused.Add(new JsonObject { ["name"] = name, ["key"] = key, ["from"] = found.Compilation });
        Record(name, key, outputs);
        return true;
    }

    public void Record(string name, string key, string[] outputs)
    {
        var node = new PlayerReusableNode(name, key, outputs.Select(Fact).ToArray());
        lock (m_Nodes)
            m_Nodes.Add(node);
    }

    public JsonArray Reused()
    {
        lock (m_Nodes)
            return new JsonArray(m_Reused.Select(node => node.DeepClone()).ToArray());
    }

    public void Write(string directory)
    {
        lock (m_Nodes)
            File.WriteAllText(Path.Combine(directory, FileName),
                JsonSerializer.Serialize(m_Nodes, WorkbenchArtifacts.JsonOptions), new UTF8Encoding(false));
    }
}

sealed record PlayerReusableNode([property: JsonRequired] string Name, [property: JsonRequired] string Key,
    [property: JsonRequired] PlayerCompilationFile[] Outputs);
