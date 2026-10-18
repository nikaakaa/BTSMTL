using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

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
            m_OverlapErrors = new string[count * count];
            int slot = 0;
            for (int i = 0; i < timeline.Tracks.Count; i++)
                for (int j = 0; j < timeline.Tracks[i].Clips.Count; j++)
                    m_ClipIdentities[slot++] = timeline.Tracks[i].Clips[j].AuthoringId;
            Bind(timeline);
            for (int i = 0; i < count; i++)
            {
                m_InvalidRangeErrors[i] = $"Clip '{m_Clips[i].Name}' 的起点、时长、混合或 ClipIn 不符合时间约束。";
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
                OrchestrationTime duration = range.End - range.Start;
                if (range.Start < OrchestrationTime.Zero || duration < OrchestrationTime.Zero ||
                    catalog.RequireClip(m_Clips[i].ContractKind).RequiresPositiveDuration && duration == OrchestrationTime.Zero ||
                    range.EaseIn < OrchestrationTime.Zero || range.EaseOut < OrchestrationTime.Zero || range.ClipIn < OrchestrationTime.Zero)
                {
                    error = m_InvalidRangeErrors[i];
                    return false;
                }
                if (m_Clips[i] is ITimelineClipTimeRangeValidator validator &&
                    !validator.TryValidateTimeRange(in range, out error))
                    return false;
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
#if UNITY_EDITOR
            ApplyRanges(ranges);
            try
            {
                for (int i = 0; i < m_Clips.Length; i++)
                    m_Clips[i].UpdateMix();
                var errors = new List<string>();
                bool valid = m_Timeline.ValidateContent(catalog, errors);
                error = string.Join("\n", errors);
                return valid;
            }
            finally
            {
                ApplyRanges(m_Ranges);
                for (int i = 0; i < m_Clips.Length; i++)
                    m_Clips[i].UpdateMix();
            }
#else
            error = string.Empty;
            return true;
#endif
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
        void ApplyRanges(ReadOnlySpan<TimelineClipTimeRange> ranges)
        {
            for (int i = 0; i < m_Clips.Length; i++)
                m_Clips[i].ApplyTimeRange(in ranges[i]);
        }

        public void Apply()
        {
            ApplyRanges(m_Ranges);
            m_Timeline.RefreshTimeRanges();
        }
#endif
    }
}
