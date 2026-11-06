using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;

namespace BTSMTL.Timeline
{
    public interface ITimelineAuthoringBoundClipSource
    {
        float TotalSourceDuration { get; }
    }

    public interface ITimelineAuthoringSourceAsset
    {
        float SourceDuration { get; }
        string ContentRevision { get; }
        IReadOnlyList<TimelineCurveChannelDescriptor> CurveChannels { get; }
        event Action ContentChanged;
    }

    public interface ITimelineAuthoringEditableSourceAsset : ITimelineAuthoringSourceAsset
    {
        void ReplaceCurves(IReadOnlyList<TimelineCurveChannelValue> curves);
    }

    public interface ITimelineClipTimeRangeValidator
    {
        bool TryValidateTimeRange(in TimelineClipTimeRange range, out string error);
    }

#if UNITY_EDITOR
    public interface ITimelineAuthoringSourceFrameGrid
    {
        float SourceFrameRate { get; }
        string SourceFrameLabel { get; }
    }
#endif

#if UNITY_EDITOR || BTSMTL_AUTHORING
    public interface ITimelineAuthoringClipSplitSource
    {
        bool CanSplit { get; }
        void SplitSource(Clip copy, OrchestrationTime splitTime);
    }

    public interface ITimelineAuthoringSourceTimeEditing
    {
        void AdjustSourceRange(in TimelineClipTimeRange original, double originalStart,
            double currentStart, double currentEnd, bool trimStart,
            out float sourceStart, out float sourceEnd);
    }
#endif

#if UNITY_EDITOR
    public interface ITimelineAuthoringSourceCreationTrack
    {
        Type SourceAssetType { get; }
        string CreateSourceLabel { get; }
    }
#endif

#if UNITY_EDITOR || BTSMTL_AUTHORING
    public interface ITimelineAuthoringReferenceCurveSource
    {
        IReadOnlyList<TimelineReferenceCurveDescriptor> ReferenceCurves { get; }
    }

    public sealed class TimelineReferenceCurveDescriptor
    {
        readonly Func<TimelineCurve, float, float, float, TimelineCurve> m_Update;

        public TimelineReferenceCurveDescriptor(string channelId, string displayName, string referenceLabel,
            Func<TimelineCurve, float, float, float, TimelineCurve> update)
        {
            ChannelId = channelId;
            DisplayName = displayName;
            ReferenceLabel = referenceLabel;
            m_Update = update;
        }

        public string ChannelId { get; }
        public string DisplayName { get; }
        public string ReferenceLabel { get; }
        public TimelineCurve Update(TimelineCurve target, float sourceStart, float sourceEnd, float duration) =>
            m_Update(target, sourceStart, sourceEnd, duration);
    }
#endif
}
