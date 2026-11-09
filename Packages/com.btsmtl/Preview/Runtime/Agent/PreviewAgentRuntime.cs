using System;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

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
        // 只有项目按请求要的单帧样本（如快预览定位目标）才编码成图像；实时画面只写共享画面区。
        bool WantsEncodedStreamFrame(int captureSlot);
        void SendStreamFrame(in PreviewEncodedFrame frame);
        void ReleaseStreamBuffers();
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
        Vector2Int m_StreamSize;
        PreviewFrameCapture m_StreamCapture;
        PreviewSharedSurface m_Surface;
        readonly Func<int, bool> m_EncodeStreamFrame;
        Coroutine m_StreamLoop;
        bool m_CaptureRequested;
        Task m_Close;
        float m_NextStatus;
        double m_NextVideo;
        float m_MaxFps = 30f;
        int m_StreamQuality = 75;
        int? m_ActiveRenderInterval;
        int m_StillFrameRequests;
        bool m_TraceRendering;
        int m_RenderedFrames;
        int m_LastRenderedLoop = -1;

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
            m_EncodeStreamFrame = m_Project.WantsEncodedStreamFrame;
            Application.runInBackground = true;
        }

        PreviewAgentConnection Connection { get; }
        // 尺寸与当前载入画面一致的共享画面区；未接入或尺寸不符时为 null，此时只发按请求编码的单帧。
        public PreviewSharedSurface Surface =>
            m_Surface != null && m_Surface.Width == m_StreamSize.x && m_Surface.Height == m_StreamSize.y ? m_Surface : null;
        public CancellationToken Cancellation => m_Cancel.Token;
        // 宿主已发来、本帧尚未处理的命令：长时间的增量工作见到它就让出，命令在下一次更新开头处理。
        public bool HasPendingCommand => Connection.HasCommand;
        public bool StreamEnabled { get; set; } = true;
        public bool IsStreaming => StreamEnabled && m_Project.Playback.CanStream;
        public int IdleRenderInterval { get; set; } = 1;

        public Task ConnectAsync(Uri endpoint, int frameRate)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = frameRate;
            PreviewProcessPower.DisableThrottling();
            return Connection.ConnectAsync(endpoint);
        }

        public void SendText(string json) => m_SendText(json);

        // 调用即转移已借用封包的所有权，返回 false 时连接已释放。
        public bool TrySendStreamImage(PreviewImagePacket packet) =>
            Connection.TrySendStreamImage(packet);

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
                // 空闲时不让引擎睡到下一帧再取命令：在这里等命令信号，最多约一帧，命令一到当帧处理；
                // 没有命令仍按帧率上限出帧，阻塞等待不占 CPU。
                if (IsIdle && Application.targetFrameRate > 0)
                    Connection.WaitCommand(Math.Max(0, 1000 / Application.targetFrameRate - 1));
                while (Connection.TryReadCommand(out string json))
                    Handle(json);
            }
            if (m_Project.IsReady)
            {
                try { m_Project.Adapter.AdvanceFrame(); }
                catch (Exception failure) { m_Project.Fail("run", failure, null); }
            }
            UpdateRenderCadence();
            return true;
        }

        // 暂停且没有在途截图、推流或单帧请求：本帧除等命令外无事可做。
        bool IsIdle => m_Project.Playback.State == PreviewPlaybackState.Paused && !IsStreaming &&
            !m_CaptureRequested && m_StreamLoop == null && m_StillFrameRequests == 0;

        void UpdateRenderCadence()
        {
            if (IdleRenderInterval > 1 && IsIdle)
            {
                if (!m_ActiveRenderInterval.HasValue)
                {
                    m_ActiveRenderInterval = OnDemandRendering.renderFrameInterval;
                    OnDemandRendering.renderFrameInterval = IdleRenderInterval;
                }
            }
            else
                RestoreRenderCadence();
        }

        void RestoreRenderCadence()
        {
            if (m_ActiveRenderInterval is not int interval)
                return;
            OnDemandRendering.renderFrameInterval = interval;
            m_ActiveRenderInterval = null;
        }

        public void EnableRenderTrace()
        {
            m_TraceRendering = true;
            RenderPipelineManager.endCameraRendering += CountOutputRender;
        }

        void CountOutputRender(ScriptableRenderContext context, Camera camera)
        {
            int frame = Time.frameCount;
            if (frame != m_LastRenderedLoop && m_Project.IsReady && camera == m_Project.ActiveCamera())
            {
                m_LastRenderedLoop = frame;
                m_RenderedFrames++;
            }
        }

        void TraceRender(string phase, string commandId = null)
        {
            if (!m_TraceRendering)
                return;
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                "[Workbench.PreviewRender] phase={0} epochMs={1} ticks={2} loop={3} rendered={4} lastRender={5} interval={6} frame={7} command={8}",
                phase, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), System.Diagnostics.Stopwatch.GetTimestamp(),
                Time.frameCount, m_RenderedFrames, m_LastRenderedLoop, OnDemandRendering.renderFrameInterval,
                m_Project.Playback.Frame, commandId);
        }

        public void UpdateStatus()
        {
            if (Time.unscaledTime >= m_NextStatus)
                SendStatus();
        }

        public void SendStatus()
        {
            m_NextStatus = Time.unscaledTime + StatusInterval;
            TraceRender("status");
            m_Project.WriteStatus();
        }

        public void RequestStreamFrame() => m_CaptureRequested = true;

        public void LateUpdate()
        {
            PreviewPlaybackState state = m_Project.Playback.State;
            if (m_StreamSize.x == 0 ||
                state is not (PreviewPlaybackState.Paused or PreviewPlaybackState.Playing or
                    PreviewPlaybackState.Stepping or PreviewPlaybackState.Seeking))
                return;

            if (IsStreaming)
                SchedulePeriodicFrame(Time.unscaledTimeAsDouble);
            if (m_StreamLoop == null && m_CaptureRequested)
            {
                m_StreamCapture = new PreviewFrameCapture(Screen.width, Screen.height, m_StreamSize.x, m_StreamSize.y,
                    PreviewImageEncoding.Jpeg, m_StreamQuality, StreamCapacity);
                m_StreamLoop = m_Owner.StartCoroutine(CaptureStreamFrames());
            }
        }

        void SchedulePeriodicFrame(double now)
        {
            if (now < m_NextVideo)
                return;

            double period = 1d / m_MaxFps;
            double periodsToAdvance = Math.Floor((now - m_NextVideo) / period) + 1d;
            m_NextVideo += periodsToAdvance * period;
            // 周期边界舍入到当前时刻时，截止点仍需推进一拍。
            if (m_NextVideo <= now)
                m_NextVideo += period;
            if (m_StreamCapture == null || m_StreamCapture.CanCapture)
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
                if (command.type != "stream")
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
                    case "surface":
                        AttachSurface(command);
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
            if (command.enabled && !StreamEnabled)
                m_NextVideo = Time.unscaledTimeAsDouble;
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

        // 句柄为 0 表示撤下画面区；新句柄已由壳复制进本进程，归本进程关闭。
        void AttachSurface(TCommand command)
        {
            m_Surface?.Dispose();
            m_Surface = null;
            if (command.surfaceHandle != 0)
                m_Surface = new PreviewSharedSurface(command.surfaceHandle, command.width, command.height, command.surfaceBytes);
        }

        // 载入确定画面尺寸，采集资源由实时需求或显式取图创建。
        public void PrepareStreamCapture(int width, int height, int quality)
        {
            m_StreamQuality = quality;
            if (m_Project.Playback.CanStream)
                m_StreamSize = new Vector2Int(width, height);
        }

        public IEnumerator ReleaseStreamCapture()
        {
            m_StreamSize = default;
            if (m_StreamLoop != null)
                m_Owner.StopCoroutine(m_StreamLoop);
            m_StreamLoop = null;
            m_CaptureRequested = false;
            while (m_StreamCapture != null && m_StreamCapture.IsBusy)
            {
                SendCompletedStreamFrames();
                yield return null;
            }
            DisposeStreamCapture();
        }

        IEnumerator CaptureStreamFrames()
        {
            while (true)
            {
                yield return m_EndOfFrame;
                SendCompletedStreamFrames();
                if (m_StreamCapture.Failure != null)
                {
                    Exception failure = m_StreamCapture.Failure;
                    DisposeStreamCapture();
                    m_Project.Fail("stream", failure, null);
                    yield break;
                }
                if (!IsStreaming && !m_CaptureRequested && !m_StreamCapture.IsBusy)
                {
                    DisposeStreamCapture();
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
                    TraceRender("jpeg");
                    m_CaptureRequested = false;
                    SendCompletedStreamFrames();
                }
            }
        }

        void SendCompletedStreamFrames()
        {
            while (m_StreamCapture.TryTakeCompleted(m_EncodeStreamFrame, out PreviewEncodedFrame encoded))
            {
                TraceRender("jpeg-ready");
                try
                {
                    if (encoded.IsEncoded || Surface != null)
                        m_Project.SendStreamFrame(in encoded);
                }
                finally { encoded.Dispose(); }
            }
        }

        void DisposeStreamCapture()
        {
            m_StreamLoop = null;
            if (m_StreamCapture == null)
                return;
            m_StreamCapture.Dispose();
            m_StreamCapture = null;
            m_Project.ReleaseStreamBuffers();
        }

        IEnumerator CaptureStill(TCommand command)
        {
            m_StillFrameRequests++;
            yield return m_EndOfFrame;
            m_StillFrameRequests--;
            using var capture = new PreviewFrameCapture(Screen.width, Screen.height, Screen.width, Screen.height,
                PreviewImageEncoding.Png, 100);
            capture.TryCaptureAtFrameEnd(m_Project.Playback.Frame, m_Project.ActiveCamera(), out _);
            TraceRender("png", command.commandId);
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
            RestoreRenderCadence();
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
            RestoreRenderCadence();
            if (m_TraceRendering)
                RenderPipelineManager.endCameraRendering -= CountOutputRender;
            m_Cancel.Cancel();
            DisposeStreamCapture();
            if (Connection != null)
                m_Close ??= Connection.CloseAsync();
        }
    }

    // 预览播放器窗口隐藏、空闲时阻塞等命令，Windows 会把它当后台低活跃进程降频或调到效率核，
    // 命令到达后整段计算变慢；连接宿主时声明本进程不接受执行速度与计时器精度节流。
    static class PreviewProcessPower
    {
        internal static void DisableThrottling()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var state = new PowerThrottlingState
            {
                Version = 1, ControlMask = ExecutionSpeed | IgnoreTimerResolution, StateMask = 0
            };
            if (SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state,
                    System.Runtime.InteropServices.Marshal.SizeOf<PowerThrottlingState>()))
                Debug.Log("[PreviewAgent] 已关闭进程电源节流。");
            else
                Debug.LogWarning($"[PreviewAgent] 关闭进程电源节流失败：{System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        const int ProcessPowerThrottling = 4;
        const uint ExecutionSpeed = 0x1;
        const uint IgnoreTimerResolution = 0x4;

        struct PowerThrottlingState
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information,
            int size);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();
#endif
    }
}
