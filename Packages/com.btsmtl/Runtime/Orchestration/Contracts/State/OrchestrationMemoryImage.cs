using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationMemorySection
    {
        public OrchestrationMemorySection(int offsetBytes, int count)
        {
            OffsetBytes = offsetBytes;
            Count = count;
        }

        public int OffsetBytes { get; }
        public int Count { get; }
    }

    // 准备期把多张表排进一块 16 字节对齐的连续内存，表头固定放在开头。
    public sealed unsafe class OrchestrationMemoryImageWriter
    {
        internal const int Alignment = 16;
        byte[] m_Buffer;
        int m_Length;

        public OrchestrationMemoryImageWriter(int headerSize)
        {
            m_Length = Align(headerSize);
            m_Buffer = new byte[Math.Max(m_Length * 4, 256)];
        }

        public OrchestrationMemorySection Add<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            int offset = Align(m_Length);
            int bytes = checked(values.Length * sizeof(T));
            Ensure(offset + bytes);
            SpanReinterpret.AsBytes(values).CopyTo(m_Buffer.AsSpan(offset));
            m_Length = offset + bytes;
            return new OrchestrationMemorySection(offset, values.Length);
        }

        public byte[] Finish<THeader>(in THeader header) where THeader : unmanaged
        {
            var image = new byte[Align(m_Length)];
            m_Buffer.AsSpan(0, m_Length).CopyTo(image);
            SpanReinterpret.Cast<byte, THeader>(image.AsSpan(0, sizeof(THeader)))[0] = header;
            return image;
        }

        public static int SizeOf<T>() where T : unmanaged => sizeof(T);

        public static ReadOnlySpan<T> Section<T>(ReadOnlySpan<byte> image,
            OrchestrationMemorySection section) where T : unmanaged =>
            SpanReinterpret.Cast<byte, T>(image.Slice(section.OffsetBytes, section.Count * sizeof(T)));

        public static Span<T> Section<T>(Span<byte> memory,
            OrchestrationMemorySection section) where T : unmanaged =>
            SpanReinterpret.Cast<byte, T>(memory.Slice(section.OffsetBytes, section.Count * sizeof(T)));

        public static ref readonly THeader Header<THeader>(ReadOnlySpan<byte> image) where THeader : unmanaged =>
            ref SpanReinterpret.Cast<byte, THeader>(image.Slice(0, sizeof(THeader)))[0];

        public static ref THeader Header<THeader>(Span<byte> memory) where THeader : unmanaged =>
            ref SpanReinterpret.Cast<byte, THeader>(memory.Slice(0, sizeof(THeader)))[0];

        internal static int Align(int value) => checked(value + Alignment - 1) & ~(Alignment - 1);

        void Ensure(int length)
        {
            if (length <= m_Buffer.Length)
                return;
            Array.Resize(ref m_Buffer, Math.Max(length, m_Buffer.Length * 2));
        }
    }

    // 推进期可写内存的布局：只算各段偏移，内存由宿主提供。
    public unsafe struct OrchestrationMemoryLayoutBuilder
    {
        int m_Offset;

        public OrchestrationMemoryLayoutBuilder(int headerSize) => m_Offset = headerSize;

        public OrchestrationMemorySection Add<T>(int count) where T : unmanaged
        {
            int start = OrchestrationMemoryImageWriter.Align(m_Offset);
            m_Offset = checked(start + count * sizeof(T));
            return new OrchestrationMemorySection(start, count);
        }

        public int SizeBytes => OrchestrationMemoryImageWriter.Align(m_Offset);
    }
}
