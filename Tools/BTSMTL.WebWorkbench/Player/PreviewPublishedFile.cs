using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace BTSMTL.WebWorkbench;

public static class PreviewPublishedFile
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct RenameInfo
    {
        public uint Flags;
        public nint RootDirectory;
        public uint FileNameLength;
        public char FileName;
    }

    static readonly int s_NameOffset = Marshal.OffsetOf<RenameInfo>(nameof(RenameInfo.FileName)).ToInt32();
    static readonly int s_LengthOffset = Marshal.OffsetOf<RenameInfo>(nameof(RenameInfo.FileNameLength)).ToInt32();

    public static unsafe void Write(string path, Action<Stream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".write";
        using SafeFileHandle handle = CreateFileW(pending, 0x40010000, 5, 0, 1, 0x80, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法创建预览发布文件 '{pending}'。");
        using var stream = new FileStream(handle, FileAccess.Write);
        write(stream);
        stream.Flush();
        byte[] name = Encoding.Unicode.GetBytes(Path.GetFullPath(path));
        byte[] rename = new byte[checked(s_NameOffset + name.Length + 2)];
        BinaryPrimitives.WriteUInt32LittleEndian(rename, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(rename.AsSpan(s_LengthOffset), checked((uint)name.Length));
        name.CopyTo(rename, s_NameOffset);
        fixed (byte* information = rename)
            if (!SetFileInformationByHandle(handle, 22, information, checked((uint)rename.Length)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法原子发布预览文件 '{path}'。");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    static extern SafeFileHandle CreateFileW(string fileName, uint access, uint share, nint security,
        uint creation, uint attributes, nint template);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern unsafe bool SetFileInformationByHandle(SafeFileHandle file, int informationClass,
        void* information, uint bufferSize);
}
