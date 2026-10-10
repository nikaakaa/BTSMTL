using System;
using System.Collections.Generic;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct TimelineRuntimePlaybackSnapshot
    {
        public const string CurrentSchema = "btsmtl.timeline.direct-runtime.v8";
        public bool IsValid => Handle.IsValid && Generation != 0;

        internal TimelineRuntimePlaybackSnapshot(TimelineRuntimePlayback playback)
            : this(playback,
                TimelineSnapshotItems<CanonicalTextValue>.CopyFrom(playback.ActiveClipIds),
                TimelineSnapshotItems<AbilityTimelineTreeClipExit>.CopyFrom(playback.ExitedTreeDecisionClips),
                TimelineSnapshotItems<AbilityTimelineTreeClipExit>.CopyFrom(playback.PendingTreeDecisionClips))
        {
            RequestId = playback.RequestId.ToString();
        }

        internal TimelineRuntimePlaybackSnapshot(TimelineRuntimePlayback playback, AbilityTimelineSnapshotStorage storage)
            : this(playback,
                storage.ActiveClipIds.Capture(playback.ActiveClipIds),
                storage.TreeDecisionExits.Capture(playback.ExitedTreeDecisionClips),
                storage.PendingTreeDecisionExits.Capture(playback.PendingTreeDecisionClips))
        {
        }

        TimelineRuntimePlaybackSnapshot(TimelineRuntimePlayback playback,
            TimelineSnapshotItems<CanonicalTextValue> activeClipIds,
            TimelineSnapshotItems<AbilityTimelineTreeClipExit> treeDecisionExits,
            TimelineSnapshotItems<AbilityTimelineTreeClipExit> pendingTreeDecisionExits)
        {
            Schema = CurrentSchema;
            TickRate = playback.TickRate;
            Handle = playback.Handle;
            Generation = playback.Generation;
            RequestId = playback.RequestId;
            ExecutionIdentity = playback.ExecutionIdentity;
            PlaybackMode = playback.PlaybackMode;
            NumericTarget = playback.NumericTarget;
            ContentRevision = playback.ContentRevision;
            State = playback.State;
            CursorTime = playback.CursorTime;
            Cycle = playback.Cycle;
            SectionId = playback.SectionId;
            ActiveClipIds = activeClipIds;
            HasStopContext = playback.HasStopContext;
            StopContext = playback.StopContext;
            InitialBoundaryPending = playback.InitialBoundaryPending;
            TimeCarry = playback.TimeCarry;
            Control = playback.Control;
            TreeDecisionExits = treeDecisionExits;
            PendingTreeDecisionExits = pendingTreeDecisionExits;
        }

        public TimelineRuntimePlaybackSnapshot(
            TimelineRuntimePlaybackHandle handle,
            ulong generation,
            CanonicalTextValue requestId,
            TimelineExecutionIdentity executionIdentity,
            TimelinePlaybackMode playbackMode,
            TimelineRuntimeNumericTarget numericTarget,
            CanonicalTextValue contentRevision,
            TimelineRuntimePlaybackState state,
            FixedScalar cursorTime,
            int cycle,
            CanonicalTextValue sectionId,
            TimelineSnapshotItems<CanonicalTextValue> activeClipIds,
            bool hasStopContext,
            TimelinePlaybackStopContext stopContext,
            bool initialBoundaryPending,
            int timeCarry,
            AbilityTimelinePlaybackControl control,
            TimelineSnapshotItems<AbilityTimelineTreeClipExit> treeDecisionExits,
            TimelineSnapshotItems<AbilityTimelineTreeClipExit> pendingTreeDecisionExits,
            int tickRate)
        {
            TickRate = tickRate;
            Schema = CurrentSchema;
            Handle = handle;
            Generation = generation;
            RequestId = requestId.IsNull ? string.Empty : requestId;
            ExecutionIdentity = executionIdentity;
            PlaybackMode = playbackMode;
            NumericTarget = numericTarget;
            ContentRevision = contentRevision.IsNull ? string.Empty : contentRevision;
            State = state;
            CursorTime = cursorTime;
            Cycle = cycle;
            SectionId = sectionId.IsNull ? string.Empty : sectionId;
            ActiveClipIds = activeClipIds;
            HasStopContext = hasStopContext;
            StopContext = stopContext;
            InitialBoundaryPending = initialBoundaryPending;
            TimeCarry = timeCarry;
            Control = control;
            TreeDecisionExits = treeDecisionExits;
            PendingTreeDecisionExits = pendingTreeDecisionExits;
        }

        public string Schema { get; }
        public TimelineRuntimePlaybackHandle Handle { get; }
        public ulong Generation { get; }
        public CanonicalTextValue RequestId { get; }
        public TimelineExecutionIdentity ExecutionIdentity { get; }
        public TimelinePlaybackMode PlaybackMode { get; }
        public TimelineRuntimeNumericTarget NumericTarget { get; }
        public CanonicalTextValue ContentRevision { get; }
        public TimelineRuntimePlaybackState State { get; }
        public FixedScalar CursorTime { get; }
        public int Cycle { get; }
        public CanonicalTextValue SectionId { get; }
        public TimelineSnapshotItems<CanonicalTextValue> ActiveClipIds { get; }
        public bool HasStopContext { get; }
        public TimelinePlaybackStopContext StopContext { get; }
        public bool InitialBoundaryPending { get; }
        public int TimeCarry { get; }
        public AbilityTimelinePlaybackControl Control { get; }
        public TimelineSnapshotItems<AbilityTimelineTreeClipExit> TreeDecisionExits { get; }
        public TimelineSnapshotItems<AbilityTimelineTreeClipExit> PendingTreeDecisionExits { get; }
        public int TickRate { get; }


    }

}
