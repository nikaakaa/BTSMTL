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
        public PreviewEncodedFrame(int frame, int width, int height, bool flipY,
            Matrix4x4 viewProjection, PreviewImageEncoding encoding, NativeArray<byte> bytes)
        {
            Frame = frame;
            Width = width;
            Height = height;
            FlipY = flipY;
            ViewProjection = viewProjection;
            Encoding = encoding;
            Bytes = bytes;
        }

        public int Frame { get; }
        public int Width { get; }
        public int Height { get; }
        public bool FlipY { get; }
        public Matrix4x4 ViewProjection { get; }
        public PreviewImageEncoding Encoding { get; }
        public NativeArray<byte> Bytes { get; }

        public void Dispose() => Bytes.Dispose();
    }

    public sealed class PreviewFrameCapture : IDisposable
    {
        readonly RenderTexture m_Screen;
        readonly RenderTexture m_Target;
        NativeArray<byte> m_Pixels;
        readonly int m_Width;
        readonly int m_Height;
        readonly PreviewImageEncoding m_Encoding;
        readonly bool m_FlipY;
        readonly Action<AsyncGPUReadbackRequest> m_OnReadback;
        AsyncGPUReadbackRequest? m_Request;
        Matrix4x4 m_ViewProjection;
        int m_Frame;
        int m_Busy;
        int m_Ready;

        public PreviewFrameCapture(int screenWidth, int screenHeight, int width, int height,
            PreviewImageEncoding encoding, int quality)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "预览画面与面板尺寸必须大于零。");
            m_Screen = new RenderTexture(screenWidth, screenHeight, 0, RenderTextureFormat.ARGB32);
            m_Target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            m_Pixels = new NativeArray<byte>(checked(width * height * 4),
                Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Quality = quality;
            m_Width = width;
            m_Height = height;
            m_Encoding = encoding;
            m_FlipY = SystemInfo.graphicsUVStartsAtTop;
            m_OnReadback = CompleteReadback;
        }

        public Exception Failure { get; private set; }
        public int Quality { get; set; }
        public bool IsBusy => Volatile.Read(ref m_Busy) != 0;

        public bool TryCaptureAtFrameEnd(int frame, Camera camera)
        {
            if (IsBusy)
                return false;
            Volatile.Write(ref m_Busy, 1);
            m_Frame = frame;
            m_ViewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(m_Screen);
            Graphics.Blit(m_Screen, m_Target);
            m_Request = AsyncGPUReadback.RequestIntoNativeArray(
                ref m_Pixels, m_Target, 0, TextureFormat.RGBA32, m_OnReadback);
            return true;
        }

        public bool TryTakeCompleted(out PreviewEncodedFrame frame)
        {
            if (Volatile.Read(ref m_Ready) == 0)
            {
                frame = default;
                return false;
            }
            Volatile.Write(ref m_Ready, 0);
            try
            {
                NativeArray<byte> bytes = m_Encoding == PreviewImageEncoding.Jpeg
                    ? ImageConversion.EncodeNativeArrayToJPG(m_Pixels, GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)m_Width, (uint)m_Height, 0, Quality)
                    : ImageConversion.EncodeNativeArrayToPNG(m_Pixels, GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)m_Width, (uint)m_Height);
                frame = new PreviewEncodedFrame(m_Frame, m_Width, m_Height,
                    m_FlipY, m_ViewProjection, m_Encoding, bytes);
                return true;
            }
            catch (Exception failure)
            {
                Failure = failure;
                frame = default;
                return false;
            }
            finally
            {
                Volatile.Write(ref m_Busy, 0);
            }
        }

        void CompleteReadback(AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
            {
                Failure = new InvalidOperationException("预览画面 GPU 读回失败，帧 " + m_Frame);
                Volatile.Write(ref m_Busy, 0);
                return;
            }
            Volatile.Write(ref m_Ready, 1);
        }

        public void Dispose()
        {
            if (m_Request.HasValue)
                m_Request.Value.WaitForCompletion();
            m_Pixels.Dispose();
            m_Screen.Release();
            m_Target.Release();
            UnityEngine.Object.Destroy(m_Screen);
            UnityEngine.Object.Destroy(m_Target);
        }
    }

    public sealed class PreviewPngImageArchive
    {
        readonly string m_Directory;

        public PreviewPngImageArchive(string directory)
        {
            if (!Path.IsPathRooted(directory))
                throw new ArgumentException("预览帧画面归档需要绝对输出目录。", nameof(directory));
            m_Directory = directory;
            Directory.CreateDirectory(directory);
        }

        public unsafe string Write(in PreviewEncodedFrame frame)
        {
            if (frame.Encoding != PreviewImageEncoding.Png)
                throw new ArgumentException("独立截图归档只接收 PNG，逐帧 JPEG 交运行档案写入器。", nameof(frame));
            string path = Path.Combine(m_Directory, "frame-" + frame.Frame.ToString("D10") + ".png");
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            stream.Write(new ReadOnlySpan<byte>(
                NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(frame.Bytes), frame.Bytes.Length));
            return path;
        }
    }
}
