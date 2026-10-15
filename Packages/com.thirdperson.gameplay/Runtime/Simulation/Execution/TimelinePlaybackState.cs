using TimelinePlaybackProgress = BTSMTL.Orchestration.OrchestrationTimelineProgress<
    Photon.Deterministic.FP,
    BTSMTL.Timeline.TimelineRuntimePlaybackState,
    ThirdPersonSimulation.AbilityTimelinePlaybackControl,
    BTSMTL.Timeline.TimelinePlaybackStopContext>;
using System;
using ThirdPersonSimulation;
using Photon.Deterministic;

namespace BTSMTL.Timeline
{
    [ValueOperations]
    public readonly partial struct TimelinePlaybackState
    {
        public TimelinePlaybackState(
            CanonicalTextValue requestId,
            TimelineExecutionIdentity executionIdentity,
            CanonicalTextValue contentRevision,
            in TimelinePlaybackProgress progress,
            CanonicalTextValue sectionId,
            TimelineSnapshotItems<CanonicalTextValue> activeClipIds,
            TimelineSnapshotItems<AbilityTimelineTreeClipExit> treeDecisionExits,
            TimelineSnapshotItems<AbilityTimelineTreeClipExit> pendingTreeDecisionExits)
        {
            RequestId = requestId.Trim();
            ExecutionIdentity = executionIdentity;
            ContentRevision = contentRevision.Trim();
            Progress = progress;
            SectionId = sectionId.IsNull ? CanonicalTextValue.Empty : sectionId;
            ActiveClipIds = activeClipIds;
            TreeDecisionExits = treeDecisionExits;
            PendingTreeDecisionExits = pendingTreeDecisionExits;
            RequireValid();
        }

        public CanonicalTextValue RequestId { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public CanonicalTextValue ContentRevision { get; }
        public TimelinePlaybackProgress Progress { get; }
        public TimelineRuntimePlaybackState State => Progress.State;
        public FP CursorTime => Progress.CursorTime;
        public int Cycle => Progress.Cycle;
        public int TimeCarry => Progress.TimeCarry;
        public AbilityTimelinePlaybackControl Control => Progress.Control;
        public CanonicalTextValue SectionId { get; }
        public TimelineSnapshotItems<CanonicalTextValue> ActiveClipIds { get; }
        public TimelineSnapshotItems<AbilityTimelineTreeClipExit> TreeDecisionExits { get; }
        public TimelineSnapshotItems<AbilityTimelineTreeClipExit> PendingTreeDecisionExits { get; }
        public bool HasStopContext => Progress.HasStopContext;
        public TimelinePlaybackStopContext StopContext => Progress.StopContext;
        public bool InitialBoundaryPending => Progress.InitialBoundaryPending;

        [ValueValidation]
        void RequireValid()
        {
            if (RequestId.IsEmpty || ContentRevision.IsEmpty)
                throw new ArgumentException("Timeline playback identity is incomplete.");
            if (State == TimelineRuntimePlaybackState.Unprepared)
                throw new ArgumentOutOfRangeException(nameof(State));
            if (CursorTime < FP._0 || Cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(CursorTime));
        }
    }
}
