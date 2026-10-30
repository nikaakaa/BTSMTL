using System;

namespace BTSMTL.Preview
{
    public interface IPreviewCheckpointAdapter
    {
        string ContentIdentity { get; }
        PreviewRestoreSupport RestoreSupport { get; }
        string RestoreLimitation { get; }
        void Capture(int slot, int frame);
        void Restore(int slot);
    }

    public enum PreviewResumeStatus : byte
    {
        Restored,
        RestartOnly,
        SelfCheckRequired,
        NoCheckpoint,
        ContentChanged
    }

    public readonly struct PreviewResumeResult
    {
        public PreviewResumeResult(PreviewResumeStatus status, int frame)
        {
            Status = status;
            Frame = frame;
        }

        public PreviewResumeStatus Status { get; }
        public int Frame { get; }
    }

    public sealed class PreviewCheckpointStore
    {
        readonly IPreviewCheckpointAdapter m_Adapter;
        readonly int[] m_Frames;
        readonly int m_Interval;
        readonly string m_ContentIdentity;
        int m_NextSlot;

        public PreviewCheckpointStore(IPreviewCheckpointAdapter adapter, int capacity, int interval)
        {
            if (capacity <= 0 || interval <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "检查点容量与间隔必须大于零。");
            m_Adapter = adapter;
            m_Frames = new int[capacity];
            Array.Fill(m_Frames, -1);
            m_Interval = interval;
            m_ContentIdentity = adapter.ContentIdentity;
        }

        public void CaptureCommittedFrame(int frame, bool keyPosition = false)
        {
            if (m_Adapter.RestoreSupport == PreviewRestoreSupport.RestartOnly ||
                !keyPosition && frame % m_Interval != 0)
                return;
            int slot = m_NextSlot;
            m_Frames[slot] = -1;
            m_Adapter.Capture(slot, frame);
            m_Frames[slot] = frame;
            m_NextSlot = (slot + 1) % m_Frames.Length;
        }

        public PreviewResumeResult RestoreAtOrBefore(int frame)
        {
            if (m_Adapter.RestoreSupport == PreviewRestoreSupport.RestartOnly)
                return new PreviewResumeResult(PreviewResumeStatus.RestartOnly, -1);
            if (m_Adapter.RestoreSupport == PreviewRestoreSupport.SelfCheckRequired)
                return new PreviewResumeResult(PreviewResumeStatus.SelfCheckRequired, -1);
            if (m_ContentIdentity != m_Adapter.ContentIdentity)
                return new PreviewResumeResult(PreviewResumeStatus.ContentChanged, -1);
            int selected = -1;
            int closest = -1;
            for (int slot = 0; slot < m_Frames.Length; slot++)
                if (m_Frames[slot] <= frame && m_Frames[slot] > closest)
                {
                    selected = slot;
                    closest = m_Frames[slot];
                }
            if (selected < 0)
                return new PreviewResumeResult(PreviewResumeStatus.NoCheckpoint, -1);
            m_Adapter.Restore(selected);
            return new PreviewResumeResult(PreviewResumeStatus.Restored, closest);
        }
    }
}
