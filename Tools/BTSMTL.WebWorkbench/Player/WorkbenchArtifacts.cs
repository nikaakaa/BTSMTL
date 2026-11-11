using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace BTSMTL.WebWorkbench;

public static class WorkbenchArtifacts
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        AllowOutOfOrderMetadataProperties = true,
        PropertyNameCaseInsensitive = true
    };

    public static T Read<T>(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)!;
    }

    public static void Write<T>(string path, T value) =>
        PreviewPublishedFile.Write(path, stream => JsonSerializer.Serialize(stream, value, JsonOptions));

    public static void WriteBytes(string path, byte[] bytes) => PreviewPublishedFile.Write(path, stream => stream.Write(bytes));

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static void LinkImmutable(string destination, string source)
    {
        if (!CreateHardLink(destination, source, IntPtr.Zero))
            throw new IOException($"无法硬链接 '{source}'：Win32 错误 {Marshal.GetLastWin32Error()}。");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
