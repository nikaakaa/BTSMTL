using System;
using UnityEngine;

namespace BTSMTL.Preview
{
    public enum PreviewExecutionMode : byte { Interactive, Batch }
    public enum PreviewPlaybackState : byte { Idle, Loading, Paused, Playing, Stepping, Seeking, Failed }

    [Serializable]
    public class PreviewAgentCommand
    {
        public string type;
        public string commandId;
        public int count;
        public int frame;
        public string mode;
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
        public int? EndFrame { get; private set; }
        public int SeekTarget { get; private set; } = -1;
        public bool CanStream => Mode == PreviewExecutionMode.Interactive;
        public bool NeedsFrame => State == PreviewPlaybackState.Playing ||
            State == PreviewPlaybackState.Stepping || State == PreviewPlaybackState.Seeking;

        public void BeginLoad(PreviewExecutionMode mode, int tickRate, int? endFrame)
        {
            if (State == PreviewPlaybackState.Loading)
                throw new InvalidOperationException("上一次预览载入尚未完成。");
            Mode = mode;
            m_TickRate = tickRate;
            EndFrame = endFrame;
            Frame = -1;
            SeekTarget = -1;
            m_RemainingSteps = 0;
            SetState(PreviewPlaybackState.Loading);
        }

        public void Loaded(int frame)
        {
            Frame = frame;
            SetState(PreviewPlaybackState.Paused);
        }

        public void Play()
        {
            RequireLoaded();
            SetState(EndFrame.HasValue && Frame >= EndFrame.Value
                ? PreviewPlaybackState.Paused : PreviewPlaybackState.Playing);
        }

        public void Pause()
        {
            RequireLoaded();
            SetState(PreviewPlaybackState.Paused);
        }

        public void Step(int count)
        {
            RequireLoaded();
            if (count <= 0)
                throw new ArgumentOutOfRangeException(nameof(count), "逐帧前进次数必须大于零。");
            m_RemainingSteps = count;
            SetState(PreviewPlaybackState.Stepping);
        }

        public void SeekForward(int frame)
        {
            RequireLoaded();
            if (frame < Frame || EndFrame.HasValue && frame > EndFrame.Value)
                throw new ArgumentOutOfRangeException(nameof(frame), "目标帧须位于当前恢复帧与本次输入结束帧之间。");
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
            Application.targetFrameRate = Mode == PreviewExecutionMode.Batch || state == PreviewPlaybackState.Seeking
                ? -1 : m_TickRate;
        }

        void RequireLoaded()
        {
            if (State == PreviewPlaybackState.Idle || State == PreviewPlaybackState.Loading ||
                State == PreviewPlaybackState.Failed)
                throw new InvalidOperationException("预览尚未载入可运行内容。");
        }
    }
}
