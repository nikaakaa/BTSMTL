using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BTSMTL.Preview
{
    public enum PreviewAgentCommandResult : byte { Unknown, Completed, Deferred }

    // 项目对通用代理提供的部分：载入与推进经 Adapter，定位、状态、回执格式与项目命令由项目自己处理。
    public interface IPreviewAgentProject<TCommand> where TCommand : PreviewAgentCommand
    {
        IPreviewProjectAdapter<TCommand> Adapter { get; }
        PreviewPlaybackController Playback { get; }
        bool IsReady { get; }
        TCommand ParseCommand(string json);
        void RequireCommandAvailable(TCommand command);
        void RequireSourceReady();
        // Completed 由通用代理回执成功；Deferred 由项目自行回执；Unknown 表示项目也不认识该命令。
        PreviewAgentCommandResult HandleProjectCommand(TCommand command);
        void Ack(TCommand command, bool ok, string error);
        void WriteStatus();
        void Fail(string stage, Exception failure, string commandId);
        bool CanCaptureStreamFrame { get; }
        Camera ActiveCamera();
        // 实时画面开始采集时冻结来源，读回完成后由 SendStreamFrame 连同画面一起发出。
        void FreezeStreamSource(int captureSlot);
        void SendStreamFrame(in PreviewEncodedFrame frame);
        void OnShutdown(string reason);
    }

    // 通用预览代理：命令分派、推进、断开即退出、状态节拍、实时画面节流与独立 PNG 截图。
    public sealed class PreviewAgentRuntime<TCommand> where TCommand : PreviewAgentCommand
    {
        public const int StreamCapacity = 3;
        const float StatusInterval = 0.25f;
        readonly MonoBehaviour m_Owner;
        readonly IPreviewAgentProject<TCommand> m_Project;
        readonly Action<string> m_SendText;
        readonly CancellationTokenSource m_Cancel = new CancellationTokenSource();
        readonly WaitForEndOfFrame m_EndOfFrame = new WaitForEndOfFrame();
        PreviewFrameCapture m_StreamCapture;
        Coroutine m_StreamLoop;
        bool m_CaptureRequested;
        Task m_Close;
        float m_NextStatus;
        double m_NextVideo;
        float m_MaxFps = 30f;
        int m_StreamQuality = 75;

        public PreviewAgentRuntime(MonoBehaviour owner, IPreviewAgentProject<TCommand> project,
            PreviewAgentConnection connection)
            : this(owner, project, connection, connection.SendText) { }

        public PreviewAgentRuntime(MonoBehaviour owner, IPreviewAgentProject<TCommand> project,
            Action<string> receiveLocalMessage)
            : this(owner, project, null, receiveLocalMessage) { }

        PreviewAgentRuntime(MonoBehaviour owner, IPreviewAgentProject<TCommand> project,
            PreviewAgentConnection connection, Action<string> sendText)
        {
            m_Owner = owner;
            m_Project = project;
            Connection = connection;
            m_SendText = sendText;
            Application.runInBackground = true;
        }

        PreviewAgentConnection Connection { get; }
        public CancellationToken Cancellation => m_Cancel.Token;
        public bool StreamEnabled { get; set; } = true;
        public bool IsStreaming => StreamEnabled && m_Project.Playback.CanStream;

        public Task ConnectAsync(Uri endpoint, int frameRate)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = frameRate;
            return Connection.ConnectAsync(endpoint);
        }

        public void SendText(string json) => m_SendText(json);

        // 调用即转移已借用封包的所有权，返回 false 时连接已释放。
        public bool TrySendStreamImage(PreviewImagePacket packet, bool requested) =>
            Connection.TrySendStreamImage(packet, requested);

        // 返回 false 表示连接已结束并开始退出，本帧不再做后续处理。
        public bool Update()
        {
            if (Connection != null)
            {
                if (Connection.State is PreviewConnectionState.Closed or PreviewConnectionState.Failed)
                {
                    Shutdown(Connection.Failure?.ToString() ?? "播放器与 Host 的连接已结束。");
                    return false;
                }
                while (Connection.TryReadCommand(out string json))
                    Handle(json);
            }
            if (m_Project.IsReady)
            {
                try { m_Project.Adapter.AdvanceFrame(); }
                catch (Exception failure) { m_Project.Fail("run", failure, null); }
            }
            return true;
        }

        public void UpdateStatus()
        {
            if (Time.unscaledTime >= m_NextStatus)
                SendStatus();
        }

        public void SendStatus()
        {
            m_NextStatus = Time.unscaledTime + StatusInterval;
            m_Project.WriteStatus();
        }

        public void RequestStreamFrame() => m_CaptureRequested = true;

        public void LateUpdate()
        {
            PreviewPlaybackState state = m_Project.Playback.State;
            if (!IsStreaming || m_StreamLoop == null ||
                state is not (PreviewPlaybackState.Paused or PreviewPlaybackState.Playing or
                    PreviewPlaybackState.Stepping or PreviewPlaybackState.Seeking))
                return;

            double now = Time.unscaledTimeAsDouble;
            if (now < m_NextVideo)
                return;

            double period = 1d / m_MaxFps;
            double periodsToAdvance = Math.Floor((now - m_NextVideo) / period) + 1d;
            m_NextVideo += periodsToAdvance * period;
            // 周期边界舍入到当前时刻时，截止点仍需推进一拍。
            if (m_NextVideo <= now)
                m_NextVideo += period;
            if (m_StreamCapture.CanCapture)
                RequestStreamFrame();
        }

        public void Handle(string json)
        {
            TCommand command;
            try { command = m_Project.ParseCommand(json); }
            catch (Exception failure)
            {
                Debug.LogException(failure);
                return;
            }
            try
            {
                m_Project.RequireCommandAvailable(command);
                PreviewPlaybackController playback = m_Project.Playback;
                switch (command.type)
                {
                    case "load":
                        if (playback.State == PreviewPlaybackState.Loading)
                            throw new InvalidOperationException("上一次载入尚未完成。");
                        m_Owner.StartCoroutine(m_Project.Adapter.Load(command));
                        return;
                    case "play":
                        m_Project.RequireSourceReady();
                        playback.Play();
                        break;
                    case "pause":
                        m_Project.RequireSourceReady();
                        playback.Pause();
                        break;
                    case "step":
                        m_Project.RequireSourceReady();
                        playback.Step(command.count);
                        break;
                    case "stream":
                        ConfigureStream(command);
                        break;
                    case "input":
                        m_Project.Adapter.InjectInput(command);
                        return;
                    case "capture":
                        m_Owner.StartCoroutine(CaptureStill(command));
                        return;
                    case "stop":
                        m_Project.Ack(command, true, null);
                        Shutdown("播放器已停止。");
                        return;
                    default:
                        PreviewAgentCommandResult result = m_Project.HandleProjectCommand(command);
                        if (result == PreviewAgentCommandResult.Deferred)
                            return;
                        if (result == PreviewAgentCommandResult.Unknown)
                            throw new InvalidDataException($"常驻播放器不认识命令 '{command.type}'。");
                        break;
                }
                m_Project.Ack(command, true, null);
            }
            catch (Exception failure)
            {
                m_Project.Ack(command, false, failure.Message);
            }
        }

        void ConfigureStream(TCommand command)
        {
            if (!m_Project.Playback.CanStream && command.enabled)
                throw new InvalidOperationException("批量预览只写逐帧归档，不推送实时画面。");
            StreamEnabled = command.enabled;
            if (command.quality > 0)
            {
                m_StreamQuality = Mathf.Clamp(command.quality, 10, 100);
                if (m_StreamCapture != null)
                    m_StreamCapture.Quality = m_StreamQuality;
            }
            if (command.maxFps > 0)
                m_MaxFps = Mathf.Clamp(command.maxFps, 1f, 60f);
        }

        // 载入时按面板尺寸准备实时画面采集；批量模式不推流，不建采集。
        public void PrepareStreamCapture(int width, int height, int quality)
        {
            m_StreamQuality = quality;
            if (m_Project.Playback.CanStream)
            {
                m_StreamCapture = new PreviewFrameCapture(Screen.width, Screen.height, width, height,
                    PreviewImageEncoding.Jpeg, m_StreamQuality, StreamCapacity);
                m_StreamLoop = m_Owner.StartCoroutine(CaptureStreamFrames());
            }
        }

        public IEnumerator ReleaseStreamCapture()
        {
            if (m_StreamLoop != null)
                m_Owner.StopCoroutine(m_StreamLoop);
            m_StreamLoop = null;
            m_CaptureRequested = false;
            while (m_StreamCapture != null && m_StreamCapture.IsBusy)
            {
                SendCompletedStreamFrames();
                yield return null;
            }
            m_StreamCapture?.Dispose();
            m_StreamCapture = null;
        }

        IEnumerator CaptureStreamFrames()
        {
            while (true)
            {
                yield return m_EndOfFrame;
                SendCompletedStreamFrames();
                if (m_StreamCapture.Failure != null)
                {
                    m_StreamLoop = null;
                    m_Project.Fail("stream", m_StreamCapture.Failure, null);
                    yield break;
                }
                if (!m_CaptureRequested || !m_Project.CanCaptureStreamFrame)
                    continue;
                Camera camera = m_Project.ActiveCamera();
                if (!camera)
                    throw new InvalidOperationException("实时画面没有实际输出相机。");
                if (m_StreamCapture.TryCaptureAtFrameEnd(m_Project.Playback.Frame, camera, out int captureSlot))
                {
                    m_Project.FreezeStreamSource(captureSlot);
                    m_CaptureRequested = false;
                }
            }
        }

        void SendCompletedStreamFrames()
        {
            while (m_StreamCapture.TryTakeCompleted(out PreviewEncodedFrame encoded))
            {
                try { m_Project.SendStreamFrame(in encoded); }
                finally { encoded.Dispose(); }
            }
        }

        IEnumerator CaptureStill(TCommand command)
        {
            yield return m_EndOfFrame;
            using var capture = new PreviewFrameCapture(Screen.width, Screen.height, Screen.width, Screen.height,
                PreviewImageEncoding.Png, 100);
            capture.TryCaptureAtFrameEnd(m_Project.Playback.Frame, m_Project.ActiveCamera(), out _);
            PreviewEncodedFrame png;
            while (!capture.TryTakeCompleted(out png))
            {
                if (capture.Failure != null)
                {
                    m_Project.Ack(command, false, capture.Failure.ToString());
                    yield break;
                }
                yield return null;
            }
            try
            {
                PreviewPngImageArchive.Write(in png, command.path);
                SendText(JsonUtility.ToJson(new PreviewAgentReceipt
                {
                    commandId = command.commandId, ok = true, frame = png.Frame, path = command.path
                }));
            }
            catch (Exception failure)
            {
                m_Project.Ack(command, false, failure.Message);
            }
            finally { png.Dispose(); }
        }

        // 宿主断开或收到 stop 后结束：先由项目封存观察并释放会话，再关闭连接并退出进程。
        public async void Shutdown(string reason)
        {
            m_Owner.enabled = false;
            m_Owner.StopAllCoroutines();
            m_Project.OnShutdown(reason);
            m_Cancel.Cancel();
            if (Connection == null)
            {
                UnityEngine.Object.Destroy(m_Owner.gameObject);
                return;
            }
            m_Close = Connection.CloseAsync();
            await m_Close;
            Exception failure = Connection.Failure;
            int exitCode = failure == null ? 0 : 1;
            int failureId = failure == null ? 0 : RuntimeHelpers.GetHashCode(failure);
            Debug.Log($"[PreviewAgent.Quit] state={Connection.State} exitCode={exitCode} failureId={failureId}");
            Application.Quit(exitCode);
        }

        public void Destroy()
        {
            m_Cancel.Cancel();
            m_StreamCapture?.Dispose();
            m_StreamCapture = null;
            if (Connection != null)
                m_Close ??= Connection.CloseAsync();
        }
    }
}
