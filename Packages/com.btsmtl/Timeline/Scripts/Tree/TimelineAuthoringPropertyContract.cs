#if UNITY_EDITOR
using UnityEngine;

namespace BTSMTL.Timeline
{
    public static class TimelineAuthoringPropertyContract
    {
        public static UnityEngine.Object ReferenceAsset(Clip clip) =>
            TimelineAuthoringClipBinding.SourceAsset(clip);

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
