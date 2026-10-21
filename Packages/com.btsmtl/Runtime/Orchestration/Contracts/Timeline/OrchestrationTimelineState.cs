using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage]
    [ValueOperations]
    public partial struct OrchestrationTimelineSequenceState : IEquatable<OrchestrationTimelineSequenceState>
    {
        public ulong NextPlaybackIdentity;
    }

    [ValueStorage]
    [ValueOperations]
    public partial struct OrchestrationTimelineControl<TTime> :
        IEquatable<OrchestrationTimelineControl<TTime>>
        where TTime : unmanaged, IEquatable<TTime>
    {
        public TTime Rate;
        public bool Paused;
    }

    [ValueStorage]
    [ValueOperations]
    public partial struct OrchestrationTimelineStopContext : IEquatable<OrchestrationTimelineStopContext>
    {
        public uint Cause;
        public ulong Tick;
        public int SourceIdentityId;
    }

    [ValueStorage]
    [ValueOperations]
    public partial struct OrchestrationTimelineStart<TTime> :
        IEquatable<OrchestrationTimelineStart<TTime>>
        where TTime : unmanaged, IEquatable<TTime>
    {
        public int ContentIdentityId;
        public int ContentRevisionId;
        public OrchestrationTimelinePlaybackMode Mode;
        public ulong PlaybackIdentity;
        public ulong CallerPlaybackIdentity;
        public int SourceIdentityId;
        public ulong SourceGeneration;
        public ulong OwnerInstanceId;
        public ulong InputSequence;
        public ulong StartTick;
        public TTime StartTime;

        public bool IsSamePlayback(in OrchestrationTimelineStart<TTime> other) =>
            ContentIdentityId == other.ContentIdentityId && ContentRevisionId == other.ContentRevisionId &&
            Mode == other.Mode && PlaybackIdentity == other.PlaybackIdentity &&
            CallerPlaybackIdentity == other.CallerPlaybackIdentity &&
            SourceIdentityId == other.SourceIdentityId && SourceGeneration == other.SourceGeneration &&
            OwnerInstanceId == other.OwnerInstanceId && InputSequence == other.InputSequence;

        public static IEqualityComparer<OrchestrationTimelineStart<TTime>> PlaybackComparer { get; } =
            new PlaybackIdentityComparer();

        sealed class PlaybackIdentityComparer : IEqualityComparer<OrchestrationTimelineStart<TTime>>
        {
            public bool Equals(OrchestrationTimelineStart<TTime> left, OrchestrationTimelineStart<TTime> right) =>
                left.IsSamePlayback(in right);

            public int GetHashCode(OrchestrationTimelineStart<TTime> value) =>
                HashCode.Combine(HashCode.Combine(value.ContentIdentityId, value.ContentRevisionId, value.Mode,
                    value.PlaybackIdentity, value.CallerPlaybackIdentity, value.SourceIdentityId,
                    value.SourceGeneration, value.OwnerInstanceId), value.InputSequence);
        }

        public OrchestrationTimelineStart<TTarget> WithStartTime<TTarget>(TTarget startTime)
            where TTarget : unmanaged, IEquatable<TTarget> => new()
        {
            ContentIdentityId = ContentIdentityId,
            ContentRevisionId = ContentRevisionId,
            Mode = Mode,
            PlaybackIdentity = PlaybackIdentity,
            CallerPlaybackIdentity = CallerPlaybackIdentity,
            SourceIdentityId = SourceIdentityId,
            SourceGeneration = SourceGeneration,
            OwnerInstanceId = OwnerInstanceId,
            InputSequence = InputSequence,
            StartTick = StartTick,
            StartTime = startTime
        };
    }

    [ValueStorage]
    [ValueOperations]
    public partial struct OrchestrationTimelinePlaybackState<TTime> :
        IEquatable<OrchestrationTimelinePlaybackState<TTime>>
        where TTime : unmanaged, IEquatable<TTime>
    {
        public OrchestrationTimelineProgress<TTime, OrchestrationTimelinePlaybackStatus,
            OrchestrationTimelineControl<TTime>, OrchestrationTimelineStopContext> Progress;
        public OrchestrationTimelineStart<TTime> Start;
        public ulong LastConsumedTick;
        public TTime LastConsumedClockTime;
        public ulong InvocationSequence;
        public int TreeExitRequestCount;
    }
}
