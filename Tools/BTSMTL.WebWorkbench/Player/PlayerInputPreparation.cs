using System.Text.Json;
using BTSMTL.Authoring.Workspace;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

public sealed class PlayerInputUnavailableException(string message) : InvalidOperationException(message);

public static class PlayerInputPreparation
{
    static readonly JsonSerializerOptions s_Json = new(JsonSerializerDefaults.Web) { IncludeFields = true, AllowOutOfOrderMetadataProperties = true };
    public static JsonElement Value<T>(T value) => JsonSerializer.SerializeToElement(value, s_Json);

    public static string OutputDirectory(string directory)
    {
        string output = Path.GetFullPath(directory);
        if (!Path.IsPathFullyQualified(directory) ||
            !string.Equals(Path.GetPathRoot(output), "D:\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Player归档输出需要D盘绝对目录。");
        return output;
    }

    public static string Canonical(JsonElement value) => AuthoringJsonText.Compact(JToken.Parse(value.GetRawText()));
    public static bool Same(JsonElement left, JsonElement right) => Canonical(left) == Canonical(right);
}
