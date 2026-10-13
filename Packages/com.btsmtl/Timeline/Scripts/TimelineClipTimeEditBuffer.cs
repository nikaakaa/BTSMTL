using System;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;

namespace BTSMTL.Timeline
{
#if UNITY_EDITOR
    public interface ITimelineTimeRangePreview
    {
        TimelineAsset Source { get; }
        TimelineAsset Workspace { get; }
        TimelineData WorkingData { get; }
        bool IsEditing { get; }
        int ClipCount { get; }
        string ClipIdentityAt(int index);
        void BeginEdit(string name);
        bool TrySetRanges(ReadOnlySpan<TimelineClipTimeRange> ranges, out string error);
        void NotifyControlsSynchronized();
        void CompleteEdit();
        void CancelEdit();
        bool SynchronizeUndo();
    }
#endif

    public sealed class TimelineClipTimeEditBuffer
    {
        TimelineData m_Timeline;
        readonly Clip[] m_Clips;
        readonly string[] m_ClipIdentities;
        readonly TimelineClipTimeRange[] m_Ranges;
        readonly string[] m_InvalidRangeErrors;
        readonly string[] m_InvalidMotionErrors;
        readonly string[] m_InvalidCameraErrors;
        readonly string[] m_OverlapErrors;

        public TimelineClipTimeEditBuffer(TimelineData timeline)
        {
            m_Timeline = timeline;
            int count = 0;
            for (int i = 0; i < timeline.Tracks.Count; i++)
                count += timeline.Tracks[i].Clips.Count;
            m_Clips = new Clip[count];
            m_ClipIdentities = new string[count];
            m_Ranges = new TimelineClipTimeRange[count];
            m_InvalidRangeErrors = new string[count];
            m_InvalidMotionErrors = new string[count];
            m_InvalidCameraErrors = new string[count];
            m_OverlapErrors = new string[count * count];
            int slot = 0;
            for (int i = 0; i < timeline.Tracks.Count; i++)
                for (int j = 0; j < timeline.Tracks[i].Clips.Count; j++)
                    m_ClipIdentities[slot++] = timeline.Tracks[i].Clips[j].AuthoringId;
            Bind(timeline);
            for (int i = 0; i < count; i++)
            {
                m_InvalidRangeErrors[i] = $"Clip '{m_Clips[i].Name}' 的起点、时长、混合或 ClipIn 不符合时间约束。";
                m_InvalidMotionErrors[i] = $"Motion Clip '{m_Clips[i].Name}' 的源区间必须位于素材内，并且不能长于时间条。";
                m_InvalidCameraErrors[i] = $"镜头片段 '{m_Clips[i].Name}' 必须有正窗口时长与合法的源起止范围。";
                for (int j = i + 1; j < count; j++)
                    if (ReferenceEquals(m_Clips[i].Track, m_Clips[j].Track))
                        m_OverlapErrors[i * count + j] = $"Track '{m_Clips[i].Track.Name}' 不允许 Clip '{m_Clips[i].Name}' 与 '{m_Clips[j].Name}' 重叠。";
            }
            Capture();
        }

        public int Count => m_Ranges.Length;
        public TimelineData Timeline => m_Timeline;
        public TimelineClipTimeRange this[int index] => m_Ranges[index];
        public ReadOnlySpan<TimelineClipTimeRange> Ranges => m_Ranges;
        public Clip ClipAt(int index) => m_Clips[index];

        public void Bind(TimelineData timeline)
        {
            m_Timeline = timeline;
            for (int i = 0; i < timeline.Tracks.Count; i++)
                for (int j = 0; j < timeline.Tracks[i].Clips.Count; j++)
                    for (int slot = 0; slot < m_Clips.Length; slot++)
                        if (m_ClipIdentities[slot] == timeline.Tracks[i].Clips[j].AuthoringId)
                        {
                            m_Clips[slot] = timeline.Tracks[i].Clips[j];
                            break;
                        }
        }

        public void Capture()
        {
            for (int i = 0; i < m_Clips.Length; i++)
                m_Ranges[i] = m_Clips[i].CaptureTimeRange();
        }

        public bool HasChanges()
        {
            for (int i = 0; i < m_Clips.Length; i++)
                if (!m_Ranges[i].Equals(m_Clips[i].CaptureTimeRange()))
                    return true;
            return false;
        }

        public void CopyFrom(TimelineClipTimeEditBuffer source) => source.m_Ranges.AsSpan().CopyTo(m_Ranges);
        public void SetRanges(ReadOnlySpan<TimelineClipTimeRange> source) => source.CopyTo(m_Ranges);

        public bool HasChangesIn(TimelineClipTimeEditBuffer source)
        {
            for (int i = 0; i < m_Ranges.Length; i++)
                if (!m_Ranges[i].Equals(source.m_Ranges[i]))
                    return true;
            return false;
        }

        public bool TryValidate(ReadOnlySpan<TimelineClipTimeRange> ranges,
            TimelineContractCatalog catalog, out string error)
        {
            if (ranges.Length != m_Clips.Length)
            {
                error = "时间范围结果与当前 Timeline 的 Clip 布局不一致。";
                return false;
            }
            for (int i = 0; i < m_Clips.Length; i++)
            {
                ref readonly TimelineClipTimeRange range = ref ranges[i];
                FixedScalar duration = range.End - range.Start;
                if (range.Start < FixedScalar.Zero || duration < FixedScalar.Zero ||
                    catalog.RequireClip(m_Clips[i].ContractKind).RequiresPositiveDuration && duration == FixedScalar.Zero ||
                    range.EaseIn < FixedScalar.Zero || range.EaseOut < FixedScalar.Zero || range.ClipIn < FixedScalar.Zero)
                {
                    error = m_InvalidRangeErrors[i];
                    return false;
                }
                if (m_Clips[i] is MotionCurveClip motion &&
                    (!float.IsFinite(range.SourceStart) || !float.IsFinite(range.SourceEnd) || range.SourceStart < 0f ||
                     range.SourceEnd <= range.SourceStart || range.SourceEnd > motion.SourceCurve.Duration ||
                     FixedScalar.FromDouble(range.SourceEnd - range.SourceStart) > duration))
                {
                    error = m_InvalidMotionErrors[i];
                    return false;
                }
                if (m_Clips[i] is CameraEffectClip { Effect: ITimelineCameraSourceAsset source } &&
                    (duration <= FixedScalar.Zero || !float.IsFinite(range.SourceStart) || !float.IsFinite(range.SourceEnd) ||
                     range.SourceStart < 0f || range.SourceEnd <= range.SourceStart || range.SourceEnd > source.SourceDuration))
                {
                    error = m_InvalidCameraErrors[i];
                    return false;
                }
            }
            int trackStart = 0;
            for (int trackIndex = 0; trackIndex < m_Timeline.Tracks.Count; trackIndex++)
            {
                Track track = m_Timeline.Tracks[trackIndex];
                int trackEnd = trackStart + track.Clips.Count;
                if (catalog.RequireTrack(track.ContractKind).OverlapPolicy == TimelineTrackOverlapPolicy.Reject)
                    for (int i = trackStart; i < trackEnd; i++)
                        for (int j = i + 1; j < trackEnd; j++)
                            if (ranges[i].End > ranges[j].Start && ranges[j].End > ranges[i].Start)
                            {
                                error = m_OverlapErrors[i * m_Clips.Length + j];
                                return false;
                            }
                trackStart = trackEnd;
            }
            error = string.Empty;
            return true;
        }

        public StableHash ComputeRevision(string baselineContentHash)
        {
            CanonicalWriter writer = StableHash.BeginHash();
            writer.WriteText(baselineContentHash);
            writer.WriteInt32(m_Ranges.Length);
            for (int i = 0; i < m_Ranges.Length; i++)
                m_Ranges[i].Write(writer);
            return writer.ComputeHash();
        }

        public int[] CreateContentSlots(TimelineContentUnit content)
        {
            var slots = new int[m_Clips.Length];
            for (int i = 0; i < m_Clips.Length; i++)
                for (int j = 0; j < content.Clips.Count; j++)
                    if (m_Clips[i].AuthoringId == content.Clips[j].AuthoringId)
                    {
                        slots[i] = j;
                        break;
                    }
            return slots;
        }

#if UNITY_EDITOR
        public void Apply()
        {
            for (int i = 0; i < m_Clips.Length; i++)
                m_Clips[i].ApplyTimeRange(in m_Ranges[i]);
            m_Timeline.RefreshTimeRanges();
        }
#endif
    }
}
