using BTSMTL.Diagnostics;
using BTSMTL.Diagnostics.Editor;

namespace BTSMTL.Timeline.Editor
{
    public interface IBtsmtlExecutionTimelineAuthoringHost
    {
        bool IsTimeRangeResultPending { get; }
        long AdoptedTimeRangeResultRevision { get; }
        string TimeRangeResultError { get; }
        bool TryResolveTimeline(string timelineAuthoringId, out TimelineAsset source, out string reason);
        string GetTimeRangeUnavailableReason(TimelineAsset source, RuntimeContentRevision recordedRevision);
        bool TryOpenTimeRangePreview(TimelineAsset source, RuntimeContentRevision recordedRevision,
            out ITimelineTimeRangePreview preview, out string reason);
        // 该 Clip 在入口提供了所属状态某条出边读取的状态。
        bool IsTransitionProvider(in RuntimeTimelinePlaybackProvenance owner, RuntimeContentRevision recordedRevision,
            in RuntimeSourceElementKey clip);
        int GetOutsideReferenceCount(TimelineAsset source, string trackId, string clipId);
        void Navigate(in RuntimeExecutionSpan span);
    }
}
