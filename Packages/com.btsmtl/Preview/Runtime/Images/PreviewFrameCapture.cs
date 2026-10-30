using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace BTSMTL.Preview
{
    public enum PreviewImageEncoding : byte { Jpeg, Png }

    public readonly struct PreviewEncodedFrame
    {
        public PreviewEncodedFrame(int frame, int width, int height, bool flipY,
            Matrix4x4 viewProjection, PreviewImageEncoding encoding, byte[] bytes)
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
        public byte[] Bytes { get; }
    }

    public sealed class PreviewFrameCapture : IDisposable
    {
        readonly RenderTexture m_Screen;
        readonly RenderTexture m_Target;
        readonly byte[] m_Pixels;
        readonly int m_Quality;
        readonly int m_Width;
        readonly int m_Height;
        readonly PreviewImageEncoding m_Encoding;
        readonly bool m_FlipY;
        readonly Action<AsyncGPUReadbackRequest> m_OnReadback;
        readonly Action m_Encode;
        AsyncGPUReadbackRequest? m_Request;
        Task m_EncodeTask;
        PreviewEncodedFrame m_Completed;
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
            m_Pixels = new byte[checked(width * height * 4)];
            m_Quality = quality;
            m_Width = width;
            m_Height = height;
            m_Encoding = encoding;
            m_FlipY = SystemInfo.graphicsUVStartsAtTop;
            m_OnReadback = CompleteReadback;
            m_Encode = Encode;
        }

        public Exception Failure { get; private set; }
        public bool IsBusy => Volatile.Read(ref m_Busy) != 0 || Volatile.Read(ref m_Ready) != 0;

        public bool TryCaptureAtFrameEnd(int frame, Camera camera)
        {
            if (IsBusy)
                return false;
            Volatile.Write(ref m_Busy, 1);
            m_Frame = frame;
            m_ViewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
            ScreenCapture.CaptureScreenshotIntoRenderTexture(m_Screen);
            Graphics.Blit(m_Screen, m_Target);
            m_Request = AsyncGPUReadback.Request(m_Target, 0, TextureFormat.RGBA32, m_OnReadback);
            return true;
        }

        public bool TryTakeCompleted(out PreviewEncodedFrame frame)
        {
            if (Volatile.Read(ref m_Ready) == 0)
            {
                frame = default;
                return false;
            }
            frame = m_Completed;
            m_Completed = default;
            Volatile.Write(ref m_Ready, 0);
            return true;
        }

        void CompleteReadback(AsyncGPUReadbackRequest request)
        {
            if (request.hasError)
            {
                Failure = new InvalidOperationException("预览画面 GPU 读回失败，帧 " + m_Frame);
                Volatile.Write(ref m_Busy, 0);
                return;
            }
            request.GetData<byte>().CopyTo(m_Pixels);
            m_EncodeTask = Task.Run(m_Encode);
        }

        void Encode()
        {
            try
            {
                byte[] bytes = m_Encoding == PreviewImageEncoding.Jpeg
                    ? ImageConversion.EncodeArrayToJPG(m_Pixels, GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)m_Width, (uint)m_Height, 0, m_Quality)
                    : ImageConversion.EncodeArrayToPNG(m_Pixels, GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)m_Width, (uint)m_Height);
                m_Completed = new PreviewEncodedFrame(m_Frame, m_Width, m_Height,
                    m_FlipY, m_ViewProjection, m_Encoding, bytes);
                Volatile.Write(ref m_Ready, 1);
            }
            catch (Exception failure)
            {
                Failure = failure;
            }
            finally
            {
                Volatile.Write(ref m_Busy, 0);
            }
        }

        public void Dispose()
        {
            if (m_Request.HasValue)
                m_Request.Value.WaitForCompletion();
            m_EncodeTask?.GetAwaiter().GetResult();
            m_Screen.Release();
            m_Target.Release();
            UnityEngine.Object.Destroy(m_Screen);
            UnityEngine.Object.Destroy(m_Target);
        }
    }

    public sealed class PreviewFrameImageArchive
    {
        readonly string m_Directory;

        public PreviewFrameImageArchive(string directory)
        {
            if (!Path.IsPathRooted(directory))
                throw new ArgumentException("预览帧画面归档需要绝对输出目录。", nameof(directory));
            m_Directory = directory;
            Directory.CreateDirectory(directory);
        }

        public string Write(in PreviewEncodedFrame frame)
        {
            string extension = frame.Encoding == PreviewImageEncoding.Jpeg ? ".jpg" : ".png";
            string path = Path.Combine(m_Directory, "frame-" + frame.Frame.ToString("D10") + extension);
            File.WriteAllBytes(path, frame.Bytes);
            return path;
        }
    }
}
