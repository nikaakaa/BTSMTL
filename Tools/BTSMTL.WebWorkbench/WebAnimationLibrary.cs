using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

public sealed class WebAnimationLibrary
{
    sealed record Header(
        [property: JsonRequired] string Schema,
        [property: JsonRequired] bool Complete,
        [property: JsonRequired] string File,
        [property: JsonRequired] string Sha256);

    public sealed record Snapshot(string Revision, string Hash, string FilePath, string Json);
    readonly string manifestPath;
    readonly object gate = new();
    readonly Dictionary<string, Snapshot> versions = new(StringComparer.Ordinal);
    Snapshot? current;

    public WebAnimationLibrary(string manifestPath) => this.manifestPath = Path.GetFullPath(manifestPath);

    public Snapshot Read()
    {
        lock (gate)
        {
            byte[] bytes = File.ReadAllBytes(manifestPath);
            string revision = Convert.ToHexString(SHA256.HashData(bytes));
            if (current?.Revision == revision) return current;
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            Header header = JsonSerializer.Deserialize<Header>(bytes, options)!;
            if (header.Schema != "btsmtl-web-animation-library/1" || !header.Complete)
                throw new InvalidDataException("网页动画清单尚未完整导出或使用了未知协议。");
            string directory = Path.GetDirectoryName(manifestPath)!;
            string path = Path.GetFullPath(Path.Combine(directory, header.File));
            if (Path.IsPathRooted(header.File) || !path.StartsWith(directory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"网页动画文件必须属于清单所在目录：'{header.File}'。");
            using (FileStream file = File.OpenRead(path))
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(file)), header.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("网页glb的实际字节与动画清单版本不一致。");
            current = new Snapshot(revision, header.Sha256, path, System.Text.Encoding.UTF8.GetString(bytes));
            versions[header.Sha256] = current;
            return current;
        }
    }

    public Snapshot Version(string hash)
    {
        lock (gate) return versions[hash];
    }
}
