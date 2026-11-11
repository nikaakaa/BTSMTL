using System.Buffers.Binary;

namespace BTSMTL.WebWorkbench;

public static class PlayerPreviewVideo
{
    public const int HeaderBytes = 132;
    const int SurfaceFlag = 4;

    // 共享画面区帧只有采样头，像素在网页持有的画面区同序号帧槽；其余帧为采样头加 JPEG。
    public static void RequirePacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderBytes || !packet[..4].SequenceEqual("BTP1"u8) ||
            (packet.Length == HeaderBytes) != IsSurface(packet))
            throw new InvalidDataException("Player画面需要正式BTP1采样头，共享画面区帧只有采样头，其余帧带JPEG内容。");
    }

    public static bool IsSurface(ReadOnlySpan<byte> packet) => (Flags(packet) & SurfaceFlag) != 0;

    public static Guid LoadCommandId(ReadOnlySpan<byte> packet) => new(packet.Slice(52, 16));
    public static ulong Sequence(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadUInt64LittleEndian(packet.Slice(20, 8));
    public static int Width(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(12, 4));
    public static int Height(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(16, 4));
    public static bool FlipY(ReadOnlySpan<byte> packet) => (Flags(packet) & 1) != 0;

    public static int ClockKind(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(4, 4));
    static int Flags(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(8, 4));
    public static bool HasRequestedPosition(ReadOnlySpan<byte> packet) => (Flags(packet) & 2) != 0;
    public static long Position(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(28, 8));
    public static long RequestedPosition(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(36, 8));
    public static long ProjectTick(ReadOnlySpan<byte> packet) => BinaryPrimitives.ReadInt64LittleEndian(packet.Slice(44, 8));

    public static float[] WorldToClip(ReadOnlySpan<byte> packet)
    {
        var matrix = new float[16];
        for (int index = 0; index < matrix.Length; index++)
            matrix[index] = BinaryPrimitives.ReadSingleLittleEndian(packet.Slice(68 + index * 4, 4))
                * (index is >= 8 and < 12 ? -1 : 1);
        return matrix;
    }
}
