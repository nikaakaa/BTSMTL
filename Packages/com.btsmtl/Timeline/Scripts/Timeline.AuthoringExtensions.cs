using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using UnityEngine;

namespace BTSMTL.Timeline
{
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

    public interface ITimelineAuthoringSourceCreationTrack
    {
        Type SourceAssetType { get; }
        string CreateSourceLabel { get; }
    }

    public interface ITimelineAuthoringReferenceCurveSource
    {
        IReadOnlyList<TimelineReferenceCurveDescriptor> ReferenceCurves { get; }
    }

    public sealed class TimelineReferenceCurveDescriptor
    {
        readonly Action<AnimationCurve, float, float, float> m_Update;

        public TimelineReferenceCurveDescriptor(string channelId, string displayName, string referenceLabel,
            Action<AnimationCurve, float, float, float> update)
        {
            ChannelId = channelId;
            DisplayName = displayName;
            ReferenceLabel = referenceLabel;
            m_Update = update;
        }

        public string ChannelId { get; }
        public string DisplayName { get; }
        public string ReferenceLabel { get; }
        public void Update(AnimationCurve target, float sourceStart, float sourceEnd, float duration) =>
            m_Update(target, sourceStart, sourceEnd, duration);
    }
#endif
}
