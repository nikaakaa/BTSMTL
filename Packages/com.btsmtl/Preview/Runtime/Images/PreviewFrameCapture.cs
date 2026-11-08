using System;
using System.IO;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace BTSMTL.Preview
{
    public enum PreviewImageEncoding : byte { Jpeg, Png }

    public readonly struct PreviewEncodedFrame : IDisposable
    {
        public PreviewEncodedFrame(int frame, int width, int height, bool flipY, bool pixelsFlipY,
            Matrix4x4 viewProjection, PreviewImageEncoding encoding, NativeArray<byte> bytes,
            NativeArray<byte> pixels, int captureSlot, long captureTimestamp, Action release)
        {
            CaptureTimestamp = captureTimestamp;
            Frame = frame;
            Width = width;
            Height = height;
            FlipY = flipY;
            PixelsFlipY = pixelsFlipY;
            ViewProjection = viewProjection;
            Encoding = encoding;
            Bytes = bytes;
            Pixels = pixels;
            CaptureSlot = captureSlot;
            m_Release = release;
        }

        public int Frame { get; }
        public int Width { get; }
        public int Height { get; }
        // FlipY 针对编码图（编码器把首行当图像底行）；PixelsFlipY 针对按首行在上显示的原始 RGBA，二者相反。
        public bool FlipY { get; }
        public bool PixelsFlipY { get; }
        public Matrix4x4 ViewProjection { get; }
        public PreviewImageEncoding Encoding { get; }
        // 未要求编码的帧没有 Bytes，只借出读回的 RGBA（行序同 GPU 读回，PixelsFlipY 指示上下翻转），Dispose 后归还。
        public NativeArray<byte> Bytes { get; }
        public NativeArray<byte> Pixels { get; }
        public bool IsEncoded => Bytes.IsCreated;
        public int CaptureSlot { get; }
        // 帧末发起采集时的 Stopwatch 计数。
        public long CaptureTimestamp { get; }
        readonly Action m_Release;

        public void Dispose()
        {
            if (Bytes.IsCreated)
                Bytes.Dispose();
            m_Release();
        }
    }

    public sealed class PreviewFrameCapture : IDisposable
    {
        readonly RenderTexture m_Screen;
        readonly RenderTexture m_Target;
        readonly CaptureSlot[] m_Slots;
        readonly int m_Width;
        readonly int m_Height;
        readonly PreviewImageEncoding m_Encoding;
        readonly bool m_FlipY;
        long m_CaptureOrder;

        public PreviewFrameCapture(int screenWidth, int screenHeight, int width, int height,
            PreviewImageEncoding encoding, int quality, int capacity = 1)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "预览画面与面板尺寸必须大于零。");
            m_Screen = new RenderTexture(screenWidth, screenHeight, 0, RenderTextureFormat.ARGB32);
            m_Target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            m_Slots = new CaptureSlot[capacity];
            for (int index = 0; index < capacity; index++)
                m_Slots[index] = new CaptureSlot(this, index, checked(width * height * 4));
            Quality = quality;
            m_Width = width;
            m_Height = height;
            m_Encoding = encoding;
            m_FlipY = SystemInfo.graphicsUVStartsAtTop;
        }

        public int Width => m_Width;
        public int Height => m_Height;
        public Exception Failure { get; private set; }
        public int Quality { get; set; }
        public bool IsBusy
        {
            get
            {
                for (int index = 0; index < m_Slots.Length; index++)
                    if (Volatile.Read(ref m_Slots[index].State) != SlotState.Free)
                        return true;
                return false;
            }
        }

        public bool CanCapture
        {
            get
            {
                for (int index = 0; index < m_Slots.Length; index++)
                    if (Volatile.Read(ref m_Slots[index].State) == SlotState.Free)
                        return true;
                return false;
            }
        }

        public bool TryCaptureAtFrameEnd(int frame, Camera camera, out int captureSlot)
        {
            for (int index = 0; index < m_Slots.Length; index++)
            {
                CaptureSlot slot = m_Slots[index];
                if (Volatile.Read(ref slot.State) != SlotState.Free)
                    continue;
                slot.Frame = frame;
                slot.Order = ++m_CaptureOrder;
                slot.Quality = Quality;
                slot.ViewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
                slot.CaptureTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                Volatile.Write(ref slot.State, SlotState.Readback);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(m_Screen);
                Graphics.Blit(m_Screen, m_Target);
                slot.Request = AsyncGPUReadback.RequestIntoNativeArray(
                    ref slot.Pixels, m_Target, 0, TextureFormat.RGBA32, slot.OnReadback);
                slot.Request.Value.WaitForCompletion();
                captureSlot = index;
                return true;
            }
            captureSlot = -1;
            return false;
        }

        static readonly Func<int, bool> s_EncodeAll = _ => true;

        public bool TryTakeCompleted(out PreviewEncodedFrame frame) => TryTakeCompleted(s_EncodeAll, out frame);

        // encode 按采集槽决定本帧是否编码；不编码的帧只借出读回像素。
        public bool TryTakeCompleted(Func<int, bool> encode, out PreviewEncodedFrame frame)
        {
            CaptureSlot selected = null;
            for (int index = 0; index < m_Slots.Length; index++)
            {
                CaptureSlot slot = m_Slots[index];
                int state = Volatile.Read(ref slot.State);
                if ((state == SlotState.Readback || state == SlotState.Ready) &&
                    (selected == null || slot.Order < selected.Order))
                    selected = slot;
            }
            if (selected == null || Volatile.Read(ref selected.State) != SlotState.Ready)
            {
                frame = default;
                return false;
            }
            try
            {
                NativeArray<byte> bytes;
                bool flipY = m_FlipY;
                bool pixelsFlipY = !m_FlipY;
                if (!encode(selected.Index))
                    bytes = default;
                else if (m_Encoding == PreviewImageEncoding.Jpeg)
                    bytes = ImageConversion.EncodeNativeArrayToJPG(selected.Pixels, GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)m_Width, (uint)m_Height, 0, selected.Quality);
                else
                {
                    if (m_FlipY)
                        FlipPixelRows(selected.Pixels);
                    bytes = ImageConversion.EncodeNativeArrayToPNG(selected.Pixels, GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)m_Width, (uint)m_Height);
                    flipY = false;
                    pixelsFlipY = true;
                }
                Volatile.Write(ref selected.State, SlotState.Encoded);
                frame = new PreviewEncodedFrame(selected.Frame, m_Width, m_Height,
                    flipY, pixelsFlipY, selected.ViewProjection, m_Encoding, bytes, selected.Pixels, selected.Index, selected.CaptureTimestamp, selected.Release);
                return true;
            }
            catch (Exception failure)
            {
                Failure = failure;
                selected.Release();
                frame = default;
                return false;
            }
        }

        void FlipPixelRows(NativeArray<byte> pixels)
        {
            var rgbaView = pixels.Reinterpret<uint>(sizeof(byte));
            for (int row = 0; row < m_Height / 2; row++)
            {
                int topRowStart = row * m_Width;
                int bottomRowStart = (m_Height - row - 1) * m_Width;
                for (int column = 0; column < m_Width; column++)
                {
                    uint pixel = rgbaView[topRowStart + column];
                    rgbaView[topRowStart + column] = rgbaView[bottomRowStart + column];
                    rgbaView[bottomRowStart + column] = pixel;
                }
            }
        }

        public void Dispose()
        {
            for (int index = 0; index < m_Slots.Length; index++)
            {
                CaptureSlot slot = m_Slots[index];
                if (slot.Request.HasValue)
                    slot.Request.Value.WaitForCompletion();
                slot.Pixels.Dispose();
            }
            m_Screen.Release();
            m_Target.Release();
            UnityEngine.Object.Destroy(m_Screen);
            UnityEngine.Object.Destroy(m_Target);
        }

        static class SlotState
        {
            internal const int Free = 0;
            internal const int Readback = 1;
            internal const int Ready = 2;
            internal const int Encoded = 3;
        }

        sealed class CaptureSlot
        {
            readonly PreviewFrameCapture m_Owner;
            internal readonly int Index;
            internal readonly Action<AsyncGPUReadbackRequest> OnReadback;
            internal readonly Action Release;
            internal NativeArray<byte> Pixels;
            internal AsyncGPUReadbackRequest? Request;
            internal Matrix4x4 ViewProjection;
            internal long Order;
            internal long CaptureTimestamp;
            internal int Frame;
            internal int Quality;
            internal int State;

            internal CaptureSlot(PreviewFrameCapture owner, int index, int bytes)
            {
                m_Owner = owner;
                Index = index;
                Pixels = new NativeArray<byte>(bytes, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                OnReadback = CompleteReadback;
                Release = ReleaseFrame;
            }

            void CompleteReadback(AsyncGPUReadbackRequest request)
            {
                if (request.hasError)
                {
                    m_Owner.Failure = new InvalidOperationException("预览画面 GPU 读回失败，帧 " + Frame);
                    ReleaseFrame();
                    return;
                }
                Volatile.Write(ref State, SlotState.Ready);
            }

            void ReleaseFrame() => Volatile.Write(ref State, SlotState.Free);
        }
    }

    public static class PreviewPngImageArchive
    {
        public static unsafe string Write(in PreviewEncodedFrame frame, string absoluteFilePath)
        {
            if (!Path.IsPathFullyQualified(absoluteFilePath))
                throw new ArgumentException("独立截图需要绝对输出文件路径。", nameof(absoluteFilePath));
            if (frame.Encoding != PreviewImageEncoding.Png)
                throw new ArgumentException("独立截图归档只接收 PNG，逐帧 JPEG 交运行档案写入器。", nameof(frame));
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteFilePath));
            using var stream = new FileStream(absoluteFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.Write(new ReadOnlySpan<byte>(
                NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(frame.Bytes), frame.Bytes.Length));
            return absoluteFilePath;
        }
    }
}
