using System;
using UnityEngine;

namespace BTSMTL.Preview
{
    public enum PreviewExecutionMode : byte { Interactive, Batch }
    public enum PreviewPlaybackState : byte { Idle, Loading, Paused, Playing, Stepping, Seeking, Failed }

    public readonly struct PreviewPlaybackAvailability
    {
        readonly bool m_ArgumentFailure;

        internal PreviewPlaybackAvailability(string reason, bool argumentFailure)
        {
            Reason = reason;
            m_ArgumentFailure = argumentFailure;
        }

        public bool Available => Reason.Length == 0;
        public string Reason { get; }

        internal void Require(string parameterName)
        {
            if (Available)
                return;
            if (m_ArgumentFailure)
                throw new ArgumentOutOfRangeException(parameterName, Reason);
            throw new InvalidOperationException(Reason);
        }
    }

    [Serializable]
    public class PreviewAgentCommand
    {
        public string type;
        public string commandId;
        public int count;
        public int frame;
        public string executionMode;
        public bool enabled;
        public int width;
        public int height;
        public int quality;
        public float maxFps;
        public string path;
    }

    [Serializable]
    public class PreviewAgentReceipt
    {
        public string type = "ack";
        public string commandId;
        public bool ok;
        public string error;
        public string failureKind;
        public int frame;
        public string path;
    }

    public sealed class PreviewPlaybackController
    {
        int m_RemainingSteps;
        int m_TickRate;

        public PreviewExecutionMode Mode { get; private set; }
        public PreviewPlaybackState State { get; private set; }
        public int Frame { get; private set; } = -1;
        public int? InitialFrame { get; private set; }
        public int? EndFrame { get; private set; }
        public int SeekTarget { get; private set; } = -1;
        public bool CanStream => Mode == PreviewExecutionMode.Interactive;
        public bool NeedsFrame => State == PreviewPlaybackState.Playing ||
            State == PreviewPlaybackState.Stepping || State == PreviewPlaybackState.Seeking;

        public PreviewPlaybackAvailability ContentAvailability =>
            State == PreviewPlaybackState.Idle || State == PreviewPlaybackState.Loading ||
            State == PreviewPlaybackState.Failed
                ? new PreviewPlaybackAvailability("预览尚未载入可运行内容。", false)
                : new PreviewPlaybackAvailability(string.Empty, false);

        public PreviewPlaybackAvailability StepAvailability(int count)
        {
            PreviewPlaybackAvailability content = ContentAvailability;
            if (!content.Available)
                return content;
            return count <= 0
                ? new PreviewPlaybackAvailability("逐帧前进次数必须大于零。", true)
                : content;
        }

        public PreviewPlaybackAvailability SeekForwardAvailability(int frame)
        {
            PreviewPlaybackAvailability content = ContentAvailability;
            if (!content.Available)
                return content;
            return frame < Frame || EndFrame.HasValue && frame > EndFrame.Value
                ? new PreviewPlaybackAvailability("目标帧须位于当前恢复帧与本次输入结束帧之间。", true)
                : content;
        }

        public void BeginLoad(PreviewExecutionMode mode, int tickRate, int? endFrame)
        {
            if (State == PreviewPlaybackState.Loading)
                throw new InvalidOperationException("上一次预览载入尚未完成。");
            Mode = mode;
            m_TickRate = tickRate;
            EndFrame = endFrame;
            Frame = -1;
            InitialFrame = null;
            SeekTarget = -1;
            m_RemainingSteps = 0;
            SetState(PreviewPlaybackState.Loading);
        }

        public void Loaded(int frame)
        {
            Frame = frame;
            InitialFrame = frame;
            SetState(PreviewPlaybackState.Paused);
        }

        public void RestoreCompleted(int frame, int? endFrame)
        {
            ContentAvailability.Require(null);
            Frame = frame;
            EndFrame = endFrame;
            SeekTarget = -1;
            m_RemainingSteps = 0;
            SetState(PreviewPlaybackState.Paused);
        }

        public void Play()
        {
            ContentAvailability.Require(null);
            SetState(EndFrame.HasValue && Frame >= EndFrame.Value
                ? PreviewPlaybackState.Paused : PreviewPlaybackState.Playing);
        }

        public void Pause()
        {
            ContentAvailability.Require(null);
            SetState(PreviewPlaybackState.Paused);
        }

        public void Step(int count)
        {
            StepAvailability(count).Require(nameof(count));
            m_RemainingSteps = count;
            SetState(PreviewPlaybackState.Stepping);
        }

        public void SeekForward(int frame)
        {
            SeekForwardAvailability(frame).Require(nameof(frame));
            SeekTarget = frame;
            SetState(frame == Frame ? PreviewPlaybackState.Paused : PreviewPlaybackState.Seeking);
        }

        public void ObserveFrame(int frame)
        {
            int advanced = frame - Frame;
            Frame = frame;
            if (State == PreviewPlaybackState.Stepping)
                m_RemainingSteps -= advanced;
            if (State == PreviewPlaybackState.Seeking && frame >= SeekTarget ||
                State == PreviewPlaybackState.Stepping && m_RemainingSteps <= 0 ||
                State == PreviewPlaybackState.Playing && EndFrame.HasValue && frame >= EndFrame.Value)
                SetState(PreviewPlaybackState.Paused);
        }

        public void Fail() => SetState(PreviewPlaybackState.Failed);
        public void Stop() => SetState(PreviewPlaybackState.Idle);

        void SetState(PreviewPlaybackState state)
        {
            State = state;
            QualitySettings.vSyncCount = 0;
            Time.captureFramerate = Mode == PreviewExecutionMode.Batch ? m_TickRate : 0;
            // 批量任务结束后等待下一任务时恢复帧率上限，常驻池中的空闲播放器不空转。
            bool unlimited = Mode == PreviewExecutionMode.Batch
                ? state != PreviewPlaybackState.Idle && state != PreviewPlaybackState.Failed
                : state == PreviewPlaybackState.Seeking;
            Application.targetFrameRate = unlimited ? -1 : m_TickRate;
        }

    }
}
