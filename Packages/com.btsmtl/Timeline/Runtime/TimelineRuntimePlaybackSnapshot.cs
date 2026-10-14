using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct TimelineRuntimePlaybackSnapshot
    {
        public bool IsValid => Handle.IsValid && Generation != 0;

        internal TimelineRuntimePlaybackSnapshot(TimelineRuntimePlayback playback)
            : this(playback,
                TimelineSnapshotItems<CanonicalTextValue>.CopyFrom(playback.ActiveClipIds),
                TimelineSnapshotItems<AbilityTimelineTreeClipExit>.CopyFrom(playback.ExitedTreeDecisionClips),
                TimelineSnapshotItems<AbilityTimelineTreeClipExit>.CopyFrom(playback.PendingTreeDecisionClips))
        {
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
            : this(playback.Handle, playback.Generation, playback.PlaybackMode, playback.NumericTarget,
                playback.TickRate, new TimelinePlaybackState(playback.RequestId, playback.ExecutionIdentity,
                    playback.ContentRevision, playback.State, playback.CursorTime, playback.Cycle, playback.TimeCarry,
                    playback.Control, playback.SectionId, activeClipIds, treeDecisionExits, pendingTreeDecisionExits,
                    playback.HasStopContext, playback.StopContext, playback.InitialBoundaryPending))
        {
        }

        public TimelineRuntimePlaybackSnapshot(TimelineRuntimePlaybackHandle handle, ulong generation,
            TimelinePlaybackMode playbackMode, TimelineRuntimeNumericTarget numericTarget, int tickRate,
            TimelinePlaybackState playback)
        {
            Handle = handle;
            Generation = generation;
            PlaybackMode = playbackMode;
            NumericTarget = numericTarget;
            TickRate = tickRate;
            Playback = playback;
        }

        public TimelineRuntimePlaybackHandle Handle { get; }
        public ulong Generation { get; }
        public TimelinePlaybackMode PlaybackMode { get; }
        public TimelineRuntimeNumericTarget NumericTarget { get; }
        public int TickRate { get; }
        public TimelinePlaybackState Playback { get; }
    }
}
