using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace BTSMTL.Preview
{
    // 与桌面壳共享的实时画面区：壳建映射并把句柄复制进本进程，播放器把读回的 RGBA 直接写入帧槽。
    // 布局：64 字节画面区头 + 3 个帧槽；帧槽 = 256 字节槽头 + 宽×高×4 像素，行序同 GPU 读回。
    // 槽头：0 槽序号，8 采集时刻，16 写完时刻（均为本机 QueryPerformanceCounter 计数，供壳量端到端延迟），24 起为项目帧头。
    // 写槽时先把槽序号清零，写完再置为本帧序号，最后发布画面区头的最新序号与槽号；读方前后两次核对槽序号丢弃被覆盖的帧。
    public sealed unsafe class PreviewSharedSurface : IDisposable
    {
        public const int HeaderBytes = 64;
        public const int SlotCount = 3;
        public const int SlotHeaderBytes = 256;
        public const int FrameHeaderOffset = 24;
        public const int FrameHeaderCapacity = SlotHeaderBytes - FrameHeaderOffset;
        const uint FileMapWrite = 0x0002;

        readonly IntPtr m_Mapping;
        readonly byte* m_View;
        readonly long m_SlotStride;
        int m_LatestSlot = -1;

        public PreviewSharedSurface(long handle, int width, int height, long bytes)
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor)
                throw new PlatformNotSupportedException("共享画面区目前只支持 Windows 桌面壳。");
            if (handle == 0 || width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(handle), "共享画面区需要有效句柄和正尺寸。");
            m_SlotStride = SlotHeaderBytes + (long)width * height * 4;
            long required = HeaderBytes + SlotCount * m_SlotStride;
            if (bytes != required)
                throw new ArgumentException($"共享画面区大小 {bytes} 与 {width}×{height} 的布局 {required} 不符。", nameof(bytes));
            m_Mapping = new IntPtr(handle);
            m_View = (byte*)MapViewOfFile(m_Mapping, FileMapWrite, 0, 0, new UIntPtr((ulong)bytes));
            if (m_View == null)
            {
                int error = Marshal.GetLastWin32Error();
                CloseHandle(m_Mapping);
                throw new InvalidOperationException($"映射共享画面区失败，Win32 错误 {error}。");
            }
            Width = width;
            Height = height;
            *(int*)(m_View + 12) = width;
            *(int*)(m_View + 16) = height;
            *(int*)(m_View + 20) = SlotCount;
            *(int*)(m_View + 24) = SlotHeaderBytes;
        }

        public int Width { get; }
        public int Height { get; }

        // 取下一个写入槽（不是最新发布的槽），返回其项目帧头区域；写完必须调用 Commit。
        public Span<byte> Begin(out int slot)
        {
            slot = (m_LatestSlot + 1) % SlotCount;
            Interlocked.Exchange(ref *(long*)Slot(slot), 0L);
            return new Span<byte>(Slot(slot) + FrameHeaderOffset, FrameHeaderCapacity);
        }

        // captureTimestamp 为 Stopwatch 计数的采集时刻，按写完时刻的间隔折算到 QueryPerformanceCounter。
        public void Commit(int slot, ulong sequence, NativeArray<byte> pixels, long captureTimestamp)
        {
            long length = (long)Width * Height * 4;
            if (pixels.Length != length)
                throw new ArgumentException($"写入共享画面区的像素长度 {pixels.Length} 与 {Width}×{Height} 不符。", nameof(pixels));
            byte* target = Slot(slot);
            UnsafeUtility.MemCpy(target + SlotHeaderBytes, NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(pixels), length);
            long elapsed = Stopwatch.GetTimestamp() - captureTimestamp;
            QueryPerformanceCounter(out long written);
            QueryPerformanceFrequency(out long frequency);
            *(long*)(target + 8) = written - (long)(elapsed * (double)frequency / Stopwatch.Frequency);
            *(long*)(target + 16) = written;
            Interlocked.Exchange(ref *(long*)target, (long)sequence);
            *(int*)(m_View + 8) = slot;
            Interlocked.Exchange(ref *(long*)m_View, (long)sequence);
            m_LatestSlot = slot;
        }

        byte* Slot(int slot) => m_View + HeaderBytes + slot * m_SlotStride;

        public void Dispose()
        {
            UnmapViewOfFile(m_View);
            CloseHandle(m_Mapping);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern void* MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        static extern bool UnmapViewOfFile(void* view);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceCounter(out long count);

        [DllImport("kernel32.dll")]
        static extern bool QueryPerformanceFrequency(out long frequency);
    }
}
