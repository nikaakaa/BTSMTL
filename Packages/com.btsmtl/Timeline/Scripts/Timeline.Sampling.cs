using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [Serializable]
    public abstract class SampledTrack : Track
    {
    }

    [Serializable]
    public abstract class SampledClip : Clip
    {
        [SerializeField]
        float m_SourceStartTime;
        [SerializeField]
        float m_SourceEndTime;

        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve WeightCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public abstract string CurveChannelPrefix { get; }
        public float SourceStartTime => m_SourceStartTime;
        public float SourceEndTime => m_SourceEndTime;
        public override IReadOnlyList<TimelineCurveChannelDescriptor> CurveChannels =>
            TimelineCurveChannelCatalog.Sampled(this);
        public override TimelineContentSampling CaptureSampling(int weightCurveIndex, int easeInCurveIndex,
            int easeOutCurveIndex) => new TimelineContentSampling(weightCurveIndex, easeInCurveIndex,
            easeOutCurveIndex, TimelineSampleSourceMapping.ClipSeconds, ExtraPolationMode.None,
            TimelineSampleWindow.CurrentPosition, false, 0.0001f);

        public override TimelineClipTimeRange CaptureTimeRange() => new TimelineClipTimeRange(
            StartTime, EndTime, SelfEaseInTime, SelfEaseOutTime, ClipInTime,
            SourceStartTime, SourceEndTime, OtherEaseInTime, OtherEaseOutTime);

#if UNITY_EDITOR
        protected SampledClip(Track track, OrchestrationTime time) : base(track, time)
        {
        }

        public void ConfigureSourceRange(float sourceStartTime, float sourceEndTime)
        {
            m_SourceStartTime = sourceStartTime;
            m_SourceEndTime = sourceEndTime;
        }

        public override void ApplyTimeRange(in TimelineClipTimeRange range)
        {
            base.ApplyTimeRange(in range);
            ConfigureSourceRange(range.SourceStart, range.SourceEnd);
        }
#endif
    }
}
