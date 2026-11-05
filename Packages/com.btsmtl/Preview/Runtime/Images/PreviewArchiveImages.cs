using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace BTSMTL.Preview
{
    // 项目运行档案对通用画面录制提供的部分：录制区间、逐帧来源与画面写入。
    public interface IPreviewArchiveImageSink
    {
        bool Closed { get; }
        bool WantsImages { get; }
        int FirstFrame { get; }
        int LastFrame { get; }
        // 帧末开始读回前冻结该帧来源，读回完成后随画面一起写入档案。
        void FreezeImageSource(int frame, Camera camera, int width, int height, bool flipY);
        void StoreImage(in PreviewEncodedFrame frame);
        void FreezeStillSource(int frame, Camera camera, int width, int height, bool flipY);
        void CompleteStill(int frame, string relativePath);
        void ObserveFrameEnd(Camera camera);
        void Finish();
    }

    // 运行档案画面：确认帧末按面板尺寸采集 JPEG 写入档案，请求帧另存全屏 PNG，区间画面齐全后结束档案。
    public sealed class PreviewArchiveImages : IDisposable
    {
        readonly IPreviewArchiveImageSink m_Sink;
        readonly PreviewPlaybackController m_Playback;
        readonly Func<Camera> m_Camera;
        readonly string m_Output;
        readonly int m_Width;
        readonly int m_Height;
        readonly HashSet<int> m_Stills;
        PreviewFrameCapture m_Image;
        PreviewFrameCapture m_Still;
        string m_StillPath;
        int m_LastImageFrame = -1;

        public PreviewArchiveImages(IPreviewArchiveImageSink sink, PreviewPlaybackController playback,
            Func<Camera> camera, string output, int width, int height, int quality, int[] stillFrames)
        {
            if (!Path.IsPathFullyQualified(output))
                throw new ArgumentException("运行档案画面需要绝对输出目录。", nameof(output));
            m_Sink = sink;
            m_Playback = playback;
            m_Camera = camera;
            m_Output = output;
            m_Width = width;
            m_Height = height;
            m_Stills = new HashSet<int>(stillFrames ?? Array.Empty<int>());
            if (sink.WantsImages)
                m_Image = new PreviewFrameCapture(Screen.width, Screen.height, width, height,
                    PreviewImageEncoding.Jpeg, quality);
        }

        // 批量推进与定位须等当前确认帧的画面写入后再前进。
        public bool IsPending => m_Still != null ||
            m_Sink.WantsImages && m_Playback.Frame >= m_Sink.FirstFrame &&
            (m_LastImageFrame < m_Playback.Frame || m_Image.IsBusy);

        public IEnumerator Record(Action<Exception> fail)
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (!m_Sink.Closed)
            {
                yield return endOfFrame;
                if (m_Sink.Closed)
                    yield break;
                try { RecordFrameEnd(); }
                catch (Exception failure)
                {
                    fail(failure);
                    yield break;
                }
            }
        }

        void RecordFrameEnd()
        {
            int frame = m_Playback.Frame;
            if (m_Still != null)
            {
                if (m_Still.TryTakeCompleted(out PreviewEncodedFrame png))
                {
                    try
                    {
                        PreviewPngImageArchive.Write(in png, Path.Combine(m_Output, m_StillPath));
                        m_Sink.CompleteStill(png.Frame, m_StillPath);
                    }
                    finally { png.Dispose(); }
                    m_Still.Dispose();
                    m_Still = null;
                }
                else if (m_Still.Failure != null)
                    throw m_Still.Failure;
            }
            if (m_Sink.WantsImages && m_Image.TryTakeCompleted(out PreviewEncodedFrame encoded))
            {
                try
                {
                    m_Sink.StoreImage(in encoded);
                    m_LastImageFrame = encoded.Frame;
                }
                finally { encoded.Dispose(); }
            }
            if (m_Sink.WantsImages && m_Image.Failure != null)
                throw m_Image.Failure;
            m_Sink.ObserveFrameEnd(m_Camera());
            if (m_Stills.Remove(frame))
            {
                m_StillPath = "frames/frame-" + frame.ToString("D6", CultureInfo.InvariantCulture) + ".png";
                Camera camera = m_Camera();
                m_Still = new PreviewFrameCapture(Screen.width, Screen.height, Screen.width, Screen.height,
                    PreviewImageEncoding.Png, 100);
                m_Sink.FreezeStillSource(frame, camera, Screen.width, Screen.height, SystemInfo.graphicsUVStartsAtTop);
                m_Still.TryCaptureAtFrameEnd(frame, camera, out _);
            }
            if (m_Still == null && (!m_Sink.WantsImages || m_LastImageFrame == frame) && frame >= m_Sink.LastFrame)
                m_Sink.Finish();
            else if (m_Sink.WantsImages && frame >= m_Sink.FirstFrame && m_LastImageFrame < frame && !m_Image.IsBusy)
            {
                Camera camera = m_Camera();
                if (!camera)
                    throw new InvalidOperationException($"第 {frame} 帧没有实际输出相机，无法保存画面。");
                m_Sink.FreezeImageSource(frame, camera, m_Width, m_Height, SystemInfo.graphicsUVStartsAtTop);
                m_Image.TryCaptureAtFrameEnd(frame, camera, out _);
            }
        }

        public void Dispose()
        {
            m_Image?.Dispose();
            m_Image = null;
            m_Still?.Dispose();
            m_Still = null;
        }
    }
}
