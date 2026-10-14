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
            TimelineRuntimePlaybackState state,
            FP cursorTime,
            int cycle,
            int timeCarry,
            AbilityTimelinePlaybackControl control,
            CanonicalTextValue sectionId,
            TimelineSnapshotItems<CanonicalTextValue> activeClipIds,
            TimelineSnapshotItems<AbilityTimelineTreeClipExit> treeDecisionExits,
            TimelineSnapshotItems<AbilityTimelineTreeClipExit> pendingTreeDecisionExits,
            bool hasStopContext,
            TimelinePlaybackStopContext stopContext,
            bool initialBoundaryPending)
        {
            RequestId = requestId.Trim();
            ExecutionIdentity = executionIdentity;
            ContentRevision = contentRevision.Trim();
            State = state;
            CursorTime = cursorTime;
            Cycle = cycle;
            TimeCarry = timeCarry;
            Control = control;
            SectionId = sectionId.IsNull ? CanonicalTextValue.Empty : sectionId;
            ActiveClipIds = activeClipIds;
            TreeDecisionExits = treeDecisionExits;
            PendingTreeDecisionExits = pendingTreeDecisionExits;
            HasStopContext = hasStopContext;
            StopContext = stopContext;
            InitialBoundaryPending = initialBoundaryPending;
            RequireValid();
        }

        public CanonicalTextValue RequestId { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public CanonicalTextValue ContentRevision { get; }
        public TimelineRuntimePlaybackState State { get; }
        public FP CursorTime { get; }
        public int Cycle { get; }
        public int TimeCarry { get; }
        public AbilityTimelinePlaybackControl Control { get; }
        public CanonicalTextValue SectionId { get; }
        public TimelineSnapshotItems<CanonicalTextValue> ActiveClipIds { get; }
        public TimelineSnapshotItems<AbilityTimelineTreeClipExit> TreeDecisionExits { get; }
        public TimelineSnapshotItems<AbilityTimelineTreeClipExit> PendingTreeDecisionExits { get; }
        public bool HasStopContext { get; }
        public TimelinePlaybackStopContext StopContext { get; }
        public bool InitialBoundaryPending { get; }

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
