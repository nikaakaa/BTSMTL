#if UNITY_EDITOR
using UnityEngine;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineAuthoringReferenceValue
    {
        public TimelineAuthoringReferenceValue(UnityEngine.Object asset, string errorCode = "", string errorMessage = "")
        {
            Asset = asset;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }
        public UnityEngine.Object Asset { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public bool IsMissing => !Asset && !string.IsNullOrEmpty(ErrorCode);
    }

    public static class TimelineAuthoringPropertyContract
    {
        public static UnityEngine.Object ReferenceAsset(Clip clip) =>
            TimelineAuthoringClipBinding.SourceAsset(clip);

        public static TimelineAuthoringReferenceValue Reference(Clip clip)
        {
            UnityEngine.Object asset = ReferenceAsset(clip);
            if (asset)
                return new TimelineAuthoringReferenceValue(asset);
            if (clip is ITimelineAuthoringClipSource source)
                return new TimelineAuthoringReferenceValue(null, source.SourceMissingErrorCode, source.SourceMissingError);
            if (clip is ITimelineTreeClipAuthoringSource)
                return new TimelineAuthoringReferenceValue(null, "timeline_tree_source_unsupported", "TreeClip不是正式资产节点图引用。");
            return new TimelineAuthoringReferenceValue(null);
        }

        public static bool NeedsSegmentOverride(Clip clip, UnityEngine.Object referenceObject)
        {
            if (clip is ITimelineClipExitSource exit && exit.ClipExitSource == TimelineClipExitSource.TreeDecision)
                return false;
            return clip.EndTime != TimelineAuthoringClipBinding.DefaultEndTime(clip, clip.StartTime, referenceObject) ||
                clip.SelfEaseInTime.RawValue != 0 || clip.SelfEaseOutTime.RawValue != 0 || clip.ClipInTime.RawValue != 0;
        }

    }
}
#endif
